using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using SkiaSharp;
using StudyStash.Core.Rich;

namespace PartsImport;

static class Tool
{
    /// <summary>This tool's own folder (manifest, house parts, cache).</summary>
    public static string Dir { get; } = FindDir();

    static string FindDir()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "PartsImport.csproj"))) return d.FullName;
        return Directory.GetCurrentDirectory();
    }

    public static string Engine => Path.GetFullPath(Path.Combine(Dir, "..", ".."));
}

/// <summary>A part as the manifest (curated.json) gives it; a part found by the bulk rules gets the same from its
/// file's name and category.</summary>
sealed record Entry
{
    public string Id { get; init; } = "";
    public string From { get; init; } = "";
    public string Name { get; init; } = "";
    public string Category { get; init; } = "";
    public string View { get; init; } = "";
    public string Size { get; init; } = "";
    public string Tags { get; init; } = "";
    public RegionSpec[] Regions { get; init; } = [];
    public PortSpec[] Ports { get; init; } = [];
    public double[]? Crop { get; init; }
    public string? Drop { get; init; }
    public bool DropText { get; init; }
    public bool KeepColours { get; init; }
    public double Simplify { get; init; }
    /// <summary>One of Study Stash's own whole illustrations: its named groups stay whole, each a region named by its
    /// title; its callouts go.</summary>
    public bool Whole { get; init; }
    public string? DropStroke { get; init; }
    /// <summary>Where the part is best pointed at, as fractions of its box, when its middle isn't on it.</summary>
    public double[]? Anchor { get; init; }
}

static class Program
{
    static async Task<int> Main(string[] args)
    {
        switch (args.FirstOrDefault())
        {
            case "sheet":
            {
                // sheet out.png files...: the raw files side by side, to choose from.
                var items = args.Skip(2).Select(f => (Path.GetFileNameWithoutExtension(f), File.ReadAllText(f))).ToList();
                Preview.Sheet(items, args[1]);
                return 0;
            }
            case "leaves":
            {
                // leaves <from> out.png [x y w h]: a part's shapes numbered in drawing order over a grid of tenths, to
                // write its regions by.
                var (svg, _) = await Sources.GetAsync(args[1]);
                var built = Normalise.Build("look", Prepare.Plain(svg), [], recolour: false, simplify: args.Length > 7 ? double.Parse(args[7], CultureInfo.InvariantCulture) : 0);
                double[] crop = args.Length >= 7 ? args[3..7].Select(a => double.Parse(a, CultureInfo.InvariantCulture)).ToArray() : [0, 0, 1, 1];
                Leaves(built, crop, args[2]);
                Console.WriteLine($"{Normalise.LastLeaves.Count} shapes, box {Normalise.LastBox}, {built.Bytes / 1024} KB");
                return 0;
            }
            case "build":
                return await BuildAsync(args.Length > 1 ? args[1] : null);
        }
        Console.WriteLine("usage: build [look-dir] | leaves <from> out.png [x y w h [simplify]] | sheet out.png files...");
        return 1;
    }

    static void Leaves(BuiltPart built, double[] crop, string path)
    {
        var box = Normalise.LastBox;
        var view = new Bounds(box.X + crop[0] * box.W, box.Y + crop[1] * box.H, crop[2] * box.W, crop[3] * box.H);
        var svg = new XElement(XName.Get("svg", "http://www.w3.org/2000/svg"), new XAttribute("viewBox", F($"{view.X} {view.Y} {view.W} {view.H}")), built.Symbol.Elements());
        float scale = (float)(1400 / view.W);
        var pic = Preview.Picture(svg.ToString());
        using var bmp = new SKBitmap(1400, (int)(view.H * scale));
        using var c = new SKCanvas(bmp);
        c.Clear(SKColors.White);
        c.Save();
        c.Scale(scale);
        if (pic is not null) c.DrawPicture(pic);
        c.Restore();
        using var grid = new SKPaint { Color = new SKColor(0, 120, 255, 90), StrokeWidth = 1 };
        using var font = new SKFont(SKTypeface.FromFamilyName("Helvetica Neue", SKFontStyle.Bold), 13);
        using var ink = new SKPaint { Color = SKColors.Black, IsAntialias = true };
        using var halo = new SKPaint { Color = new SKColor(255, 255, 255, 220), IsAntialias = true };
        using var gridInk = new SKPaint { Color = new SKColor(0, 90, 220), IsAntialias = true };
        for (int i = 0; i <= 20; i++)
        {
            double fx = i / 20.0;
            float x = (float)((box.X + fx * box.W - view.X) * scale), y = (float)((box.Y + fx * box.H - view.Y) * scale);
            c.DrawLine(x, 0, x, bmp.Height, grid);
            c.DrawLine(0, y, bmp.Width, y, grid);
            if (i % 2 == 0)
            {
                c.DrawText(F($"{fx:0.0}"), x + 2, 12, font, gridInk);
                c.DrawText(F($"{fx:0.0}"), 2, y - 2, font, gridInk);
            }
        }
        var rnd = new Random(1);
        for (int i = 0; i < Normalise.LastLeaves.Count; i++)
        {
            var b = Normalise.LastLeaves[i];
            if (!view.Contains(b.CenterX, b.CenterY)) continue;
            float x = (float)((b.CenterX - view.X) * scale) + rnd.Next(-6, 6), y = (float)((b.CenterY - view.Y) * scale) + rnd.Next(-6, 6);
            string t = i.ToString(CultureInfo.InvariantCulture);
            float w = font.MeasureText(t);
            c.DrawRect(x - 1, y - 11, w + 2, 14, halo);
            c.DrawText(t, x, y, font, ink);
        }
        using var img = SKImage.FromBitmap(bmp);
        File.WriteAllBytes(path, img.Encode(SKEncodedImageFormat.Png, 90).ToArray());
    }

    static string F(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);

    sealed record Built(Entry Entry, Origin Origin, BuiltPart Part);

    static async Task<int> BuildAsync(string? look)
    {
        var manifest = JsonSerializer.Deserialize<Entry[]>(await File.ReadAllTextAsync(Path.Combine(Tool.Dir, "curated.json")), Sources.Json)!;
        var entries = manifest.ToList();
        var bulk = Bulk.Pick(await Sources.BioiconsFilesAsync(), entries.Select(e => e.From).ToHashSet());
        entries.AddRange(bulk);
        Console.WriteLine($"{manifest.Length} curated, {bulk.Count} by the bulk rules");

        var built = new List<Built>();
        var refused = new List<string>();
        foreach (var e in entries)
        {
            try
            {
                var (svg, origin) = await Sources.GetAsync(e.From);
                var regions = e.Regions;
                if (e.Whole) (svg, regions) = Harvest.Whole(svg, e.Regions);
                var part = Normalise.Build(e.Id, Prepare.Plain(svg), regions, e.Crop, e.Drop, !e.KeepColours && !e.Whole, e.DropText, e.Simplify, e.Whole, e.DropStroke);
                if (!manifest.Contains(e))
                {
                    // The bulk takes only light, wordless pictures: a labelled diagram's words aren't the lecture's.
                    if (part.Bytes > (e.Category == "anatomy" ? Bulk.MaxPartBytes * 3 / 2 : Bulk.MaxPartBytes)) { refused.Add($"{e.Id}: {part.Bytes / 1024} KB once cleaned, too heavy"); continue; }
                    if (part.Symbol.Descendants().Any(x => x.Name.LocalName == "text")) { refused.Add($"{e.Id}: has words in it (a labelled diagram)"); continue; }
                }
                foreach (string n in part.Notes) Console.WriteLine($"  {e.Id}: {n}");
                built.Add(new Built(e, origin, part));
            }
            catch (Exception ex) when (ex is Refused or System.Xml.XmlException or HttpRequestException)
            {
                refused.Add($"{e.Id}: {ex.Message}");
            }
        }
        foreach (string r in refused) Console.WriteLine("refused " + r);

        var xml = Library(built);
        string target = Path.Combine(Tool.Engine, "src", "StudyStash.Core", "Rich", "PartsLibrary.xml");
        string text = xml.ToString(SaveOptions.DisableFormatting).Replace("><part ", ">\n<part ").Replace("><credit ", ">\n<credit ");
        await File.WriteAllTextAsync(target, text + "\n");
        await File.WriteAllTextAsync(Path.Combine(Tool.Engine, "..", "docs", "parts-credits.md"), Credits(built));
        Console.WriteLine($"{built.Count} parts, {text.Length / 1024} KB -> {target}");
        foreach (var g in built.GroupBy(b => b.Entry.Category).OrderBy(g => g.Key)) Console.WriteLine($"  {g.Key}: {g.Count()}");

        if (look is not null)
        {
            Directory.CreateDirectory(look);
            foreach (var g in built.GroupBy(b => b.Entry.Category))
                foreach (var (chunk, k) in g.Chunk(40).Select((c, k) => (c, k)))
                    Preview.Sheet(chunk.Select(b => (b.Entry.Id, Standalone(b.Part.Symbol))).ToList(), Path.Combine(look, $"{g.Key}-{k + 1}.png"), 220, 8);
        }
        return refused.Count == entries.Count ? 1 : 0;
    }

    public static string Standalone(XElement symbol) =>
        new XElement(XName.Get("svg", "http://www.w3.org/2000/svg"), symbol.Attribute("viewBox"), symbol.Elements()).ToString(SaveOptions.DisableFormatting);

    static XElement Library(List<Built> built)
    {
        var root = new XElement("parts", new XAttribute("version", "1"), new XAttribute("bioicons", Sources.BioiconsCommit), new XAttribute("wokwi", Sources.WokwiVersion));
        foreach (var b in built)
        {
            var e = b.Entry;
            var p = new XElement("part",
                new XAttribute("id", e.Id), new XAttribute("name", e.Name), new XAttribute("category", e.Category),
                new XAttribute("tags", e.Tags), new XAttribute("from", e.From),
                new XAttribute("author", b.Origin.Author), new XAttribute("licence", b.Origin.Licence), new XAttribute("credit", b.Origin.Credit));
            var (ax, ay) = e.Anchor is { Length: 2 } an ? (Math.Round(b.Part.Box.X + an[0] * b.Part.Box.W, 2), Math.Round(b.Part.Box.Y + an[1] * b.Part.Box.H, 2)) : b.Part.Anchor;
            p.SetAttributeValue("x", ax);
            p.SetAttributeValue("y", ay);
            if (e.View.Length > 0) p.SetAttributeValue("view", e.View);
            if (e.Size.Length > 0) p.SetAttributeValue("size", e.Size);
            foreach (var (r, x, y) in b.Part.Regions)
                p.Add(new XElement("region", new XAttribute("id", r.Id), new XAttribute("name", r.Name), new XAttribute("tags", r.Tags ?? ""),
                    new XAttribute("x", x), new XAttribute("y", y)));
            var box = b.Part.Box;
            foreach (var port in e.Ports)
                p.Add(new XElement("port", new XAttribute("id", port.Id),
                    new XAttribute("x", Math.Round(box.X + port.X * box.W, 2)), new XAttribute("y", Math.Round(box.Y + port.Y * box.H, 2))));
            p.Add(b.Part.Symbol);
            root.Add(p);
        }
        return root;
    }

    static string Credits(List<Built> built)
    {
        var sb = new StringBuilder();
        sb.Append("# Illustration parts: credits\n\n");
        sb.Append("Generated by `engine/tools/PartsImport` from the parts library (`engine/src/StudyStash.Core/Rich/PartsLibrary.xml`); ");
        sb.Append("don't edit by hand. Every part was recoloured to Study Stash's palette, cropped and simplified, and made safe ");
        sb.Append("(see [parts-sources.md](parts-sources.md)).\n\n");
        foreach (var g in built.GroupBy(b => b.Origin.Source).OrderByDescending(g => g.Count()))
        {
            var o = g.First().Origin;
            sb.Append($"## {o.Source}: {g.Count()} part{(g.Count() == 1 ? "" : "s")}\n\n");
            foreach (var lic in g.GroupBy(b => (b.Origin.Licence, b.Origin.LicenceUrl)))
            {
                sb.Append($"Licence: [{lic.Key.Licence}]({lic.Key.LicenceUrl}). Source: {o.Url}\n\n");
                foreach (var a in lic.GroupBy(b => b.Origin.Credit))
                    sb.Append($"- {a.Key}, adapted: {string.Join(", ", a.Select(b => b.Entry.Name))}\n");
                sb.Append('\n');
            }
        }
        if (built.Any(b => b.Origin.Source == "Wokwi elements"))
            sb.Append("## Wokwi elements licence\n\n").Append(WokwiMit).Append('\n');
        return sb.ToString();
    }

    public const string WokwiMit = """
        The MIT License (MIT)

        Copyright (c) 2020 Uri Shaked

        Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated
        documentation files (the "Software"), to deal in the Software without restriction, including without limitation
        the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and
        to permit persons to whom the Software is furnished to do so, subject to the following conditions:

        The above copyright notice and this permission notice shall be included in all copies or substantial portions of
        the Software.

        THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO
        THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
        AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF
        CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
        DEALINGS IN THE SOFTWARE.
        """;
}

/// <summary>
/// The bulk of the library: Bioicons files picked by rule rather than one by one: in the categories students study,
/// under an allowed licence, light enough, one of each drawing (no colour variants, which the house palette makes the
/// same anyway, and no labelled versions). Their name and tags come from the file's name and category.
/// </summary>
static partial class Bulk
{
    public const long MaxRawBytes = 60_000;
    public const int MaxPartBytes = 22_000;

    /// <summary>Most parts the bulk rules take from one category of one source, so no one corner of biology crowds the
    /// library.</summary>
    const int Quota = 45;

    static readonly Dictionary<string, (string Category, string Tags)> Categories = new()
    {
        ["Human_physiology"] = ("anatomy", "anatomy, physiology, human body, organ"),
        ["Intracellular_components"] = ("cell", "cell, organelle, cell biology"),
        ["Cell_types"] = ("cell", "cell, cell type, cell biology"),
        ["Lab_apparatus"] = ("lab", "lab, laboratory, apparatus, equipment"),
        ["Chemistry"] = ("chemistry", "chemistry, lab, glassware"),
        ["Microbiology"] = ("microbiology", "microbiology, microbe, bacteria"),
        ["Viruses"] = ("microbiology", "virus, microbiology"),
        ["Plants_Algae"] = ("plants", "plant, botany, biology"),
        ["Animals"] = ("animals", "animal, zoology, biology, organism"),
        ["Tissues"] = ("anatomy", "tissue, histology"),
        ["Computer_hardware"] = ("computing", "computer, hardware, computing"),
    };

    static readonly string[] Colours = ["blue", "red", "green", "yellow", "orange", "purple", "gray", "grey", "pink", "white", "black", "bright", "faint", "dark", "light", "brown", "greenred", "greenbright", "orangebright", "yellowbright"];

    static readonly string[] Common =
    [
        "brain", "lung", "kidney", "liver", "stomach", "eye", "ear", "skin", "muscle", "bone", "skeleton", "skull", "spine", "vertebra",
        "neuron", "nerve", "intestine", "colon", "pancreas", "spleen", "bladder", "uterus", "ovary", "testis", "tooth", "tongue", "nose",
        "thyroid", "artery", "vein", "capillary", "blood", "heart", "joint", "knee", "hip", "shoulder", "elbow", "foot", "spinal", "alveol",
        "nephron", "villi", "trachea", "bronch", "larynx", "esophagus", "oesophagus", "gall", "adrenal", "pituitary", "placenta", "embryo",
        "mitochond", "nucleus", "golgi", "reticulum", "ribosom", "lysosom", "vesicle", "membrane", "chloroplast", "vacuole", "centriol", "cytoskeleton",
        "red-blood", "erythro", "platelet", "macrophage", "lymphocyte", "sperm", "oocyte", "egg", "stem-cell", "epithel",
        "flask", "beaker", "pipette", "burette", "funnel", "cylinder", "test-tube", "tube", "petri", "microscope", "centrifuge", "balance",
        "thermometer", "bunsen", "condenser", "column", "cuvette", "spectro", "pcr", "gel", "incubator", "hood",
        "bacteri", "virus", "yeast", "coli", "fung", "phage", "algae",
        "leaf", "flower", "root", "seed", "tree", "plant", "arabidopsis", "maize", "wheat", "rice", "moss",
        "mouse", "rat", "frog", "fish", "zebrafish", "bird", "chicken", "dog", "cat", "cow", "pig", "horse", "monkey", "human", "bee", "fly",
        "drosophila", "worm", "elegans", "snail", "butterfly", "ant",
        "computer", "laptop", "screen", "cpu", "gpu", "server", "workstation", "hdd",
    ];

    [GeneratedRegex(@"[-_ ]+")]
    private static partial Regex Separators();

    public static List<Entry> Pick(List<(string Path, long Size)> files, HashSet<string> curated)
    {
        var picked = new List<Entry>();
        var bases = new Dictionary<string, int>();
        var quotas = new Dictionary<string, int>();
        var ids = new HashSet<string>();
        // What students meet most comes first, so the quotas keep it.
        static int Rank(string path)
        {
            string name = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
            return Common.Any(w => name.Contains(w, StringComparison.Ordinal)) ? 0 : 1;
        }
        foreach (var (path, size) in files.OrderBy(f => Rank(f.Path)).ThenBy(f => f.Path, StringComparer.Ordinal))
        {
            var segs = path.Split('/');
            if (segs.Length != 4 || !Sources.Allowed.ContainsKey(segs[0]) || !Categories.TryGetValue(segs[1], out var cat)) continue;
            if (size > MaxRawBytes || size < 600 || curated.Contains("bio:" + path)) continue;
            string stem = Path.GetFileNameWithoutExtension(segs[3]);
            var words = Separators().Split(stem.ToLowerInvariant()).Where(w => w.Length > 0).ToList();
            if (words.Any(w => w is "label" or "labels" or "labeled" or "labelled" or "text" or "legend" or "scheme" or "icon" or "logo")) continue;
            if (words.Count > 1 && Colours.Contains(words[^1])) continue;
            string baseName = string.Join(' ', words.Where(w => !w.All(char.IsDigit)));
            if (baseName.Length < 3) continue;
            bases[baseName] = bases.GetValueOrDefault(baseName) + 1;
            if (bases[baseName] > 2) continue;
            string quotaKey = segs[1] + "/" + segs[2];
            quotas[quotaKey] = quotas.GetValueOrDefault(quotaKey) + 1;
            if (quotas[quotaKey] > Quota) continue;
            string id = string.Join('-', words);
            if (!ids.Add(id) && !ids.Add(id += "-" + segs[2].ToLowerInvariant())) continue;
            string name = char.ToUpperInvariant(baseName[0]) + baseName[1..];
            picked.Add(new Entry { Id = id, From = "bio:" + path, Name = name, Category = cat.Category, Tags = $"{baseName}, {cat.Tags}", Simplify = 0.5 });
        }
        return picked;
    }
}
