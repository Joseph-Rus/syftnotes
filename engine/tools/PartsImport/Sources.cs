using System.Text.Json;
using System.Text.Json.Nodes;

namespace PartsImport;

/// <summary>Who made a part and under what licence: what the credits say.</summary>
sealed record Origin(string Source, string Author, string Licence, string LicenceUrl, string Url, string Credit);

/// <summary>
/// Where parts come from, each pinned: Bioicons at one commit (the licence and author read from each file's own path,
/// and only the allowed licences), Wokwi's elements at one package version (MIT), and Study Stash's own parts in
/// <c>house/</c>. Downloads are kept in the cache, so a rebuild doesn't fetch again.
/// </summary>
static class Sources
{
    public const string BioiconsCommit = "d29e766ea7580b8063c4f47b29e872db40a4d979";
    public const string WokwiVersion = "1.9.2";

    /// <summary>Bioicons licence folders that may be imported (never cc-by-sa, nc or anything else).</summary>
    public static readonly Dictionary<string, (string Name, string Url)> Allowed = new()
    {
        ["cc-0"] = ("CC0 1.0", "https://creativecommons.org/publicdomain/zero/1.0/"),
        ["cc-by-3.0"] = ("CC BY 3.0", "https://creativecommons.org/licenses/by/3.0/"),
        ["cc-by-4.0"] = ("CC BY 4.0", "https://creativecommons.org/licenses/by/4.0/"),
        ["mit"] = ("MIT", "https://opensource.org/licenses/MIT"),
        ["bsd"] = ("BSD", "https://opensource.org/licenses/BSD-3-Clause"),
    };

    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };

    public static string Cache => Path.Combine(Tool.Dir, "cache");

    /// <summary>Every SVG in Bioicons at the pinned commit: its path under static/icons and its size.</summary>
    public static async Task<List<(string Path, long Size)>> BioiconsFilesAsync()
    {
        string file = Path.Combine(Cache, $"bioicons-{BioiconsCommit[..8]}.json");
        if (!File.Exists(file))
        {
            Directory.CreateDirectory(Cache);
            var req = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/duerrsimon/bioicons/git/trees/{BioiconsCommit}?recursive=1");
            req.Headers.UserAgent.ParseAdd("StudyStash-PartsImport");
            var res = await Http.SendAsync(req);
            res.EnsureSuccessStatusCode();
            await File.WriteAllTextAsync(file, await res.Content.ReadAsStringAsync());
        }
        var tree = JsonNode.Parse(await File.ReadAllTextAsync(file))!["tree"]!.AsArray();
        return tree.Select(n => ((string)n!["path"]!, (long?)n["size"] ?? 0))
            .Where(p => p.Item1.StartsWith("static/icons/", StringComparison.Ordinal) && p.Item1.EndsWith(".svg", StringComparison.Ordinal))
            .Select(p => (p.Item1["static/icons/".Length..], p.Item2)).ToList();
    }

    /// <summary>A part's file and who made it, from its manifest reference: <c>bio:licence/category/author/file.svg</c>,
    /// <c>wokwi:tag</c>, <c>house:file.svg</c> or <c>house:file.svg#group</c>.</summary>
    public static async Task<(string Svg, Origin Origin)> GetAsync(string from)
    {
        int colon = from.IndexOf(':');
        string kind = from[..colon], rest = from[(colon + 1)..];
        switch (kind)
        {
            case "bio":
            {
                var segs = rest.Split('/');
                if (segs.Length < 4 || !Allowed.TryGetValue(segs[0], out var lic)) throw new Refused($"licence folder '{segs[0]}' isn't one that may be imported");
                string local = Path.Combine(Cache, "bio", rest.Replace('/', '_'));
                if (!File.Exists(local))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(local)!);
                    string url = $"https://raw.githubusercontent.com/duerrsimon/bioicons/{BioiconsCommit}/static/icons/{string.Join('/', segs.Select(Uri.EscapeDataString))}";
                    await File.WriteAllBytesAsync(local, await Http.GetByteArrayAsync(url));
                }
                string author = segs[2].Replace('_', ' ').Replace("--", " ").Replace('-', ' ');
                var origin = segs[2] switch
                {
                    "Servier" => new Origin("Servier Medical Art (via Bioicons)", "Servier", lic.Name, lic.Url, "https://smart.servier.com/",
                        $"Servier Medical Art (smart.servier.com), {lic.Name}"),
                    "DBCLS" => new Origin("Togo Picture Gallery (via Bioicons)", "DBCLS", lic.Name, lic.Url, "https://togotv.dbcls.jp/en/pics.html",
                        "© 2016 DBCLS TogoTV / CC-BY-4.0"),
                    _ => new Origin("Bioicons", author, lic.Name, lic.Url, "https://bioicons.com", $"{author} (via Bioicons), {lic.Name}"),
                };
                return (await File.ReadAllTextAsync(local), origin);
            }
            case "wokwi":
            {
                var all = await WokwiAsync();
                if (all[rest] is not JsonValue v) throw new Refused($"Wokwi element '{rest}' didn't draw");
                return (v.GetValue<string>(), new Origin("Wokwi elements", "Uri Shaked and contributors", "MIT", "https://github.com/wokwi/wokwi-elements/blob/main/LICENSE",
                    "https://github.com/wokwi/wokwi-elements", "Wokwi elements © 2020 Uri Shaked, MIT"));
            }
            case "wiki":
            {
                // A Wikimedia Commons file, only when Commons says it's public domain, CC0 or plain CC BY.
                string local = Path.Combine(Cache, "wiki", rest);
                string meta = local + ".json";
                if (!File.Exists(meta))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(local)!);
                    var req = new HttpRequestMessage(HttpMethod.Get, "https://commons.wikimedia.org/w/api.php?action=query&format=json&prop=imageinfo&iiprop=url&iiextmetadatafilter=LicenseShortName|Artist&iiprop=url|extmetadata&titles=File:" + Uri.EscapeDataString(rest));
                    req.Headers.UserAgent.ParseAdd("StudyStash-PartsImport/1.0 (https://github.com/Joseph-Rus/study-stash)");
                    await File.WriteAllTextAsync(meta, await (await Http.SendAsync(req)).Content.ReadAsStringAsync());
                }
                var page = JsonNode.Parse(await File.ReadAllTextAsync(meta))!["query"]!["pages"]!.AsObject().First().Value!;
                var info = page["imageinfo"]![0]!;
                string licence = (string?)info["extmetadata"]?["LicenseShortName"]?["value"] ?? "";
                string artist = System.Text.RegularExpressions.Regex.Replace((string?)info["extmetadata"]?["Artist"]?["value"] ?? "", "<[^>]+>", "").Trim();
                if (licence is not ("Public domain" or "CC0" or "CC BY 3.0" or "CC BY 4.0")) throw new Refused($"Commons says '{licence}', which isn't a licence that may be imported");
                if (!File.Exists(local))
                {
                    var req = new HttpRequestMessage(HttpMethod.Get, (string)info["url"]!);
                    req.Headers.UserAgent.ParseAdd("StudyStash-PartsImport/1.0 (https://github.com/Joseph-Rus/study-stash)");
                    await File.WriteAllBytesAsync(local, await (await Http.SendAsync(req)).Content.ReadAsByteArrayAsync());
                }
                string url = "https://commons.wikimedia.org/wiki/File:" + rest;
                return (await File.ReadAllTextAsync(local), new Origin("Wikimedia Commons", artist, licence, licence == "Public domain" ? "https://creativecommons.org/publicdomain/mark/1.0/" : "https://creativecommons.org/licenses/", url,
                    $"{artist} (Wikimedia Commons), {licence}"));
            }
            case "house":
            {
                string file = rest, group = "";
                if (rest.IndexOf('#') is var hash and > 0) { file = rest[..hash]; group = rest[(hash + 1)..]; }
                string text = await File.ReadAllTextAsync(Path.Combine(Tool.Dir, "house", file));
                if (group.Length > 0) text = Harvest.Group(text, group);
                return (text, new Origin("Study Stash", "Study Stash", "MIT", "https://opensource.org/licenses/MIT", "https://github.com/Joseph-Rus/study-stash",
                    "Study Stash's own parts, MIT"));
            }
        }
        throw new Refused($"unknown source '{kind}'");
    }

    static JsonObject? wokwi;

    /// <summary>Every Wokwi element's drawing, from the pinned package (downloaded once), drawn by wokwi.mjs.</summary>
    static async Task<JsonObject> WokwiAsync()
    {
        if (wokwi is not null) return wokwi;
        string dir = Path.Combine(Cache, $"wokwi-{WokwiVersion}");
        string json = Path.Combine(dir, "elements.json");
        if (!File.Exists(json))
        {
            Directory.CreateDirectory(dir);
            string tgz = Path.Combine(dir, "elements.tgz");
            await File.WriteAllBytesAsync(tgz, await Http.GetByteArrayAsync($"https://registry.npmjs.org/@wokwi/elements/-/elements-{WokwiVersion}.tgz"));
            Run("tar", $"xzf elements.tgz", dir);
            string esm = Path.Combine(dir, "package", "dist", "esm");
            var tags = Directory.GetFiles(esm, "*-element.js").Select(Path.GetFileName).Where(f => !f!.Contains(".spec")).Select(f => f![..^"-element.js".Length]);
            Run("node", $"\"{Path.Combine(Tool.Dir, "wokwi.mjs")}\" \"{esm}\" \"{Path.Combine(dir, "work")}\" \"{json}\" {string.Join(' ', tags)}", dir);
        }
        return wokwi = JsonNode.Parse(await File.ReadAllTextAsync(json))!.AsObject();
    }

    static void Run(string exe, string args, string dir)
    {
        var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe, args) { WorkingDirectory = dir, RedirectStandardOutput = true })!;
        p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        if (p.ExitCode != 0) throw new InvalidOperationException($"{exe} failed");
    }

    public static JsonSerializerOptions Json { get; } = new() { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
}

/// <summary>A part cut out of one of Study Stash's own illustrations: the group with that id, alone, with what it
/// uses.</summary>
static class Harvest
{
    /// <summary>A whole illustration without its callouts, and a region for each named part (its title the region's
    /// name), after any the manifest gives itself.</summary>
    public static (string Svg, RegionSpec[] Regions) Whole(string svg, RegionSpec[] given)
    {
        var root = System.Xml.Linq.XElement.Parse(svg);
        foreach (var l in root.Descendants().Where(e => (string?)e.Attribute("id") is "labels").ToList()) l.Remove();
        var regions = given.ToList();
        foreach (var g in root.Descendants().Where(e => e.Name.LocalName == "g" && (string?)e.Attribute("id") is { Length: > 0 } id && id != "art"))
        {
            string id = (string)g.Attribute("id")!;
            string name = g.Elements().FirstOrDefault(e => e.Name.LocalName == "title")?.Value.Trim() ?? id;
            if (regions.All(r => r.Id != id)) regions.Add(new RegionSpec(id, name, "", null, null, null, Group: id));
        }
        foreach (var t in root.Elements().Where(e => e.Name.LocalName is "title" or "desc").ToList()) t.Remove();
        return (root.ToString(), regions.ToArray());
    }

    public static string Group(string svg, string id)
    {
        var root = System.Xml.Linq.XElement.Parse(svg);
        var g = root.Descendants().FirstOrDefault(e => (string?)e.Attribute("id") == id) ?? throw new Refused($"no group '{id}'");
        var ns = root.Name.Namespace;
        // The group where the whole drawing has it: inside its ancestors' transforms.
        System.Xml.Linq.XElement placed = new(g);
        foreach (var a in g.Ancestors().Where(a => a.Parent is not null && a.Attribute("transform") is not null))
            placed = new System.Xml.Linq.XElement(ns + "g", new System.Xml.Linq.XAttribute("transform", (string)a.Attribute("transform")!), placed);
        var copy = new System.Xml.Linq.XElement(ns + "svg", root.Attribute("viewBox"),
            root.Descendants().Where(e => e.Name.LocalName == "defs").Select(d => new System.Xml.Linq.XElement(d)),
            placed);
        foreach (var t in copy.Descendants().Where(e => e.Name.LocalName is "title" or "desc").ToList()) t.Remove();
        return copy.ToString();
    }
}
