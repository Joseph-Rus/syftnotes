using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace StudyStash.App.Tests;

/// <summary>
/// Just enough of a PDF reader to check what the notes' PDF holds, with nothing to install: its pages and their
/// sizes, whether anything on them is a picture, whether its fonts travel with it, its links, and each page's text,
/// read back the way a PDF viewer finds it (each font's ToUnicode map applied to the codes its pages show).
/// </summary>
sealed partial class PdfProbe
{
    readonly Dictionary<int, string> dicts = [];
    readonly Dictionary<int, byte[]> streams = [];
    readonly List<int> pages = [];

    public byte[] Bytes { get; }

    public PdfProbe(byte[] bytes)
    {
        Bytes = bytes;
        // Latin-1 keeps one character to a byte, so offsets in the text are offsets in the file.
        string text = Encoding.Latin1.GetString(bytes);
        foreach (Match m in ObjectStart().Matches(text))
        {
            int id = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            int from = m.Index + m.Length;
            int streamAt = text.IndexOf("stream", from, StringComparison.Ordinal);
            int end = text.IndexOf("endobj", from, StringComparison.Ordinal);
            if (streamAt >= 0 && streamAt < end && !text[from..streamAt].Contains("endstream"))
            {
                string dict = text[from..streamAt];
                int dataAt = streamAt + "stream".Length;
                if (text[dataAt] == '\r') dataAt++;
                if (text[dataAt] == '\n') dataAt++;
                int length = int.Parse(Length().Match(dict).Groups[1].Value, CultureInfo.InvariantCulture);
                byte[] data = bytes[dataAt..(dataAt + length)];
                if (dict.Contains("/FlateDecode"))
                {
                    using var inflate = new ZLibStream(new MemoryStream(data), CompressionMode.Decompress);
                    using var plain = new MemoryStream();
                    inflate.CopyTo(plain);
                    data = plain.ToArray();
                }
                dicts[id] = dict;
                streams[id] = data;
                end = text.IndexOf("endobj", dataAt + length, StringComparison.Ordinal);
            }
            else dicts[id] = text[from..end];
            if (PageType().IsMatch(dicts[id])) pages.Add(id);
        }
    }

    public bool IsPdf => Bytes is [(byte)'%', (byte)'P', (byte)'D', (byte)'F', ..];

    public int PageCount => pages.Count;

    /// <summary>Each page's width and height, in points.</summary>
    public IEnumerable<(double Width, double Height)> PageSizes => pages.Select(p =>
    {
        var box = MediaBox().Match(dicts[p]);
        return (double.Parse(box.Groups[3].Value, CultureInfo.InvariantCulture), double.Parse(box.Groups[4].Value, CultureInfo.InvariantCulture));
    });

    /// <summary>Whether anything in the file is a picture (a formula or diagram fallen back to pixels would be).</summary>
    public bool HasImages => dicts.Values.Any(d => d.Contains("/Subtype /Image"));

    /// <summary>The fonts drawn with that don't carry their own glyphs (neither a font file nor, as Type 3 fonts do,
    /// their glyphs drawn inside the PDF): each would look different, or wrong, on a computer without it.</summary>
    public IEnumerable<string> FontsNotEmbedded => dicts.Values
        .Where(d => d.Contains("/Type /FontDescriptor") && !d.Contains("/FontFile"))
        .Where(d => !dicts.Values.Any(f => f.Contains("/Subtype /Type3") && f.Contains($"/FontDescriptor {Id(d)} 0 R")))
        .Select(d => FontName().Match(d).Groups[1].Value);

    int Id(string dict) => dicts.First(kv => ReferenceEquals(kv.Value, dict)).Key;

    /// <summary>The web addresses the pages link to.</summary>
    public IEnumerable<string> Links => dicts.Values.SelectMany(d => Uri().Matches(d).Select(m => m.Groups[1].Value));

    /// <summary>Page <paramref name="index"/>'s text (from 0), in the order it's drawn.</summary>
    public string PageText(int index) => Text(dicts[pages[index]], ContentIds(dicts[pages[index]]));

    public string AllText => string.Join("\n", Enumerable.Range(0, PageCount).Select(PageText));

    static string Squash(string s) => WhiteSpace().Replace(s, "");

    /// <summary>Whether <paramref name="words"/> are on page <paramref name="index"/>, spaces aside (a PDF places
    /// its words rather than spelling out every space).</summary>
    public bool PageHas(int index, string words) => Squash(PageText(index)).Contains(Squash(words), StringComparison.Ordinal);

    /// <summary>How many times <paramref name="words"/> appear in the whole file's text, spaces aside.</summary>
    public int Count(string words) => Regex.Matches(Squash(AllText), Regex.Escape(Squash(words))).Count;

    IEnumerable<int> ContentIds(string owner)
    {
        var contents = Contents().Match(owner);
        if (!contents.Success) return [];
        return Ref().Matches(contents.Groups[1].Value).Select(m => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture));
    }

    /// <summary>The text a content stream (and the forms it draws) shows, with <paramref name="owner"/>'s resources.</summary>
    string Text(string owner, IEnumerable<int> contentIds)
    {
        string resources = Resources(owner);
        var fonts = Named(resources, "Font");
        var forms = Named(resources, "XObject");
        var text = new StringBuilder();
        foreach (int id in contentIds)
        {
            string content = Encoding.Latin1.GetString(streams[id]);
            Dictionary<string, string>? map = null;
            int width = 1;
            foreach (Match m in Operator().Matches(content))
            {
                if (m.Groups["font"].Success && fonts.TryGetValue(m.Groups["font"].Value, out int fontId))
                    (map, width) = ToUnicode(fontId);
                else if (m.Groups["hex"].Success && map is not null)
                {
                    string hex = m.Groups["hex"].Value;
                    for (int i = 0; i + 2 * width <= hex.Length; i += 2 * width)
                        text.Append(map.TryGetValue(hex.Substring(i, 2 * width).ToUpperInvariant(), out string? ch) ? ch : "");
                }
                else if (m.Groups["form"].Success && forms.TryGetValue(m.Groups["form"].Value, out int formId) && streams.ContainsKey(formId))
                    text.Append(Text(dicts[formId], [formId]));
                else if (m.Groups["et"].Success) text.Append('\n');
            }
        }
        return text.ToString();
    }

    /// <summary>An object's resource dictionary, followed when it's a reference.</summary>
    string Resources(string owner)
    {
        int at = owner.IndexOf("/Resources", StringComparison.Ordinal);
        if (at < 0) return "";
        string rest = owner[(at + "/Resources".Length)..].TrimStart();
        if (Ref().Match(rest) is { Success: true, Index: 0 } r) return dicts[int.Parse(r.Groups[1].Value, CultureInfo.InvariantCulture)];
        return Balanced(rest);
    }

    /// <summary>The names in a resource dictionary's /Font or /XObject entry, and the objects they are.</summary>
    Dictionary<string, int> Named(string resources, string kind)
    {
        int at = resources.IndexOf("/" + kind, StringComparison.Ordinal);
        if (at < 0) return [];
        string body = Balanced(resources[(at + kind.Length + 1)..].TrimStart());
        return NameRef().Matches(body).ToDictionary(m => m.Groups[1].Value, m => int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture));
    }

    /// <summary>The "&lt;&lt; … &gt;&gt;" dictionary <paramref name="s"/> starts with, nested ones and all.</summary>
    static string Balanced(string s)
    {
        if (!s.StartsWith("<<", StringComparison.Ordinal)) return "";
        int depth = 0;
        for (int i = 0; i < s.Length - 1; i++)
        {
            if (s[i] == '<' && s[i + 1] == '<') { depth++; i++; }
            else if (s[i] == '>' && s[i + 1] == '>')
            {
                depth--;
                i++;
                if (depth == 0) return s[..(i + 1)];
            }
        }
        return s;
    }

    readonly Dictionary<int, (Dictionary<string, string>, int)> cmaps = [];

    /// <summary>A font's codes and the text each stands for, and how many bytes a code takes.</summary>
    (Dictionary<string, string> Map, int Width) ToUnicode(int fontId)
    {
        if (cmaps.TryGetValue(fontId, out var known)) return known;
        var map = new Dictionary<string, string>();
        int width = 1;
        if (ToUnicodeRef().Match(dicts[fontId]) is { Success: true } r && streams.TryGetValue(int.Parse(r.Groups[1].Value, CultureInfo.InvariantCulture), out var data))
        {
            string cmap = Encoding.Latin1.GetString(data);
            foreach (Match block in BfChar().Matches(cmap))
                foreach (Match pair in HexPair().Matches(block.Groups[1].Value))
                {
                    width = pair.Groups[1].Value.Length / 2;
                    map[pair.Groups[1].Value.ToUpperInvariant()] = Utf16(pair.Groups[2].Value);
                }
            foreach (Match block in BfRange().Matches(cmap))
                foreach (Match range in HexRange().Matches(block.Groups[1].Value))
                {
                    string lo = range.Groups[1].Value;
                    width = lo.Length / 2;
                    int from = Convert.ToInt32(lo, 16), to = Convert.ToInt32(range.Groups[2].Value, 16);
                    if (range.Groups[3].Success)
                    {
                        int first = Convert.ToInt32(range.Groups[3].Value, 16);
                        for (int c = from; c <= to; c++) map[c.ToString("X" + lo.Length, CultureInfo.InvariantCulture)] = char.ConvertFromUtf32(first + c - from);
                    }
                    else
                    {
                        var each = HexString().Matches(range.Groups[4].Value).Select(m => m.Groups[1].Value).ToList();
                        for (int c = from; c <= to && c - from < each.Count; c++) map[c.ToString("X" + lo.Length, CultureInfo.InvariantCulture)] = Utf16(each[c - from]);
                    }
                }
        }
        return cmaps[fontId] = (map, width);
    }

    static string Utf16(string hex) => Encoding.BigEndianUnicode.GetString(Convert.FromHexString(hex));

    [GeneratedRegex(@"(\d+) 0 obj\s*")]
    private static partial Regex ObjectStart();
    [GeneratedRegex(@"/Length (\d+)")]
    private static partial Regex Length();
    [GeneratedRegex(@"/Type /Page(?![s\w])")]
    private static partial Regex PageType();
    [GeneratedRegex(@"/MediaBox \[([-\d.]+) ([-\d.]+) ([-\d.]+) ([-\d.]+)\]")]
    private static partial Regex MediaBox();
    [GeneratedRegex(@"/FontName /([^\s/]+)")]
    private static partial Regex FontName();
    [GeneratedRegex(@"/URI \(([^)]*)\)")]
    private static partial Regex Uri();
    [GeneratedRegex(@"/Contents\s*(\[[^\]]*\]|\d+ 0 R)")]
    private static partial Regex Contents();
    [GeneratedRegex(@"(\d+) 0 R")]
    private static partial Regex Ref();
    [GeneratedRegex(@"/([^\s/<>\[\]()]+)\s+(\d+) 0 R")]
    private static partial Regex NameRef();
    [GeneratedRegex(@"/(?<font>[^\s/<>\[\]()]+)\s+[-\d.]+\s+Tf|<(?<hex>[0-9A-Fa-f]*)>|/(?<form>[^\s/<>\[\]()]+)\s+Do\b|\b(?<et>ET)\b")]
    private static partial Regex Operator();
    [GeneratedRegex(@"/ToUnicode (\d+) 0 R")]
    private static partial Regex ToUnicodeRef();
    [GeneratedRegex(@"beginbfchar(.*?)endbfchar", RegexOptions.Singleline)]
    private static partial Regex BfChar();
    [GeneratedRegex(@"beginbfrange(.*?)endbfrange", RegexOptions.Singleline)]
    private static partial Regex BfRange();
    [GeneratedRegex(@"<([0-9A-Fa-f]+)>\s*<([0-9A-Fa-f]+)>")]
    private static partial Regex HexPair();
    [GeneratedRegex(@"<([0-9A-Fa-f]+)>\s*<([0-9A-Fa-f]+)>\s*(?:<([0-9A-Fa-f]+)>|(\[[^\]]*\]))")]
    private static partial Regex HexRange();
    [GeneratedRegex(@"<([0-9A-Fa-f]+)>")]
    private static partial Regex HexString();
    [GeneratedRegex(@"\s+")]
    private static partial Regex WhiteSpace();
}
