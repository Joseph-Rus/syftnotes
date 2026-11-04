using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace StudyStash.Core;

/// <summary>What <see cref="MacDock.Repaired"/> made of the Dock's settings: <paramref name="Plist"/> to write back
/// (the same bytes when nothing changed), how many pinned tiles now point at the app, and why it left the settings alone
/// when it couldn't read them.</summary>
public sealed record DockRepair(byte[] Plist, int Tiles, string? Problem = null)
{
    public bool Changed => Tiles > 0;
}

/// <summary>
/// A Mac Dock icon pinned ("Keep in Dock") to the app before the rename (MacRename.cs) still points at
/// "/Applications/Study Stash.app", which is gone: it shows a "?" and opens nothing, beside a second icon for the app
/// that runs. On the first start from "Syft Notes.app" the pinned tile is pointed at this bundle instead, once per
/// account, and only when there is such a tile (otherwise the Dock is never touched, so it never blinks).
/// </summary>
public static class MacDock
{
    /// <summary>In the app's home: the repair has had its one go.</summary>
    public const string DoneFile = "dock-icon-repaired";
    const string Domain = "com.apple.dock";
    const string BundleId = "com.study-stash.app";
    static readonly TimeSpan Wait = TimeSpan.FromSeconds(20);
    static readonly XmlReaderSettings Reading = new() { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null };

    /// <summary>The Dock's settings (a plist as <c>defaults export com.apple.dock -</c> prints it) with each pinned tile
    /// for an "…/Study Stash.app" that's gone (and is ours, when the tile says whose it is) pointed at
    /// <paramref name="app"/>: its URL, in the form the tile already used, and its saved bookmark dropped so the Dock
    /// makes a new one. Every other tile and setting is left as it was. Anything it can't read changes nothing.</summary>
    public static DockRepair Repaired(byte[] plist, string app, Func<string, bool> exists)
    {
        try
        {
            using var reader = XmlReader.Create(new MemoryStream(plist), Reading);
            var doc = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
            if (Tiles(doc) is not { } tiles) return new DockRepair(plist, 0, "it has no pinned apps");
            int count = tiles.Count, fixedTiles = 0;
            foreach (var tile in tiles)
                if (Repair(tile, app, exists)) fixedTiles++;
            if (fixedTiles == 0) return new DockRepair(plist, 0);
            var output = new MemoryStream();
            using (var writer = XmlWriter.Create(output, new XmlWriterSettings { Encoding = new UTF8Encoding(false) }))
            {
                // DtdProcessing.Ignore drops the DOCTYPE on reading; it goes back as Apple writes it.
                if (doc.DocumentType is null) doc.Root?.AddBeforeSelf(new XDocumentType("plist", "-//Apple//DTD PLIST 1.0//EN", "http://www.apple.com/DTDs/PropertyList-1.0.dtd", null));
                doc.Save(writer);
            }
            byte[] bytes = output.ToArray();
            // Read back: the same number of pinned apps, or nothing is written.
            using var check = XmlReader.Create(new MemoryStream(bytes), Reading);
            if (Tiles(XDocument.Load(check))?.Count != count) return new DockRepair(plist, 0, "the pinned apps didn't come out the same");
            return new DockRepair(bytes, fixedTiles);
        }
        catch (Exception e) when (e is XmlException or InvalidOperationException or ArgumentException)
        {
            return new DockRepair(plist, 0, "it isn't a plist it can read");
        }
    }

    /// <summary>The pinned apps: the dicts in the top dict's persistent-apps array, or null.</summary>
    static List<XElement>? Tiles(XDocument doc) =>
        doc.Root is { Name.LocalName: "plist" } root && root.Elements().FirstOrDefault() is { Name.LocalName: "dict" } top
        && Value(top, "persistent-apps") is { Name.LocalName: "array" } apps
            ? [.. apps.Elements("dict")]
            : null;

    /// <summary>The element that follows &lt;key&gt;key&lt;/key&gt; in a dict.</summary>
    static XElement? Value(XElement dict, string key) =>
        dict.Elements("key").FirstOrDefault(k => k.Value == key)?.ElementsAfterSelf().FirstOrDefault();

    static bool Repair(XElement tile, string app, Func<string, bool> exists)
    {
        if (Value(tile, "tile-data") is not { Name.LocalName: "dict" } data) return false;
        if (Value(data, "bundle-identifier") is { } id && id.Value != BundleId) return false;
        if (Value(data, "file-data") is not { Name.LocalName: "dict" } file) return false;
        if (Value(file, "_CFURLString") is not { Name.LocalName: "string" } url) return false;
        // Type 15 is a file:// URL, 0 a plain path.
        bool isUrl = url.Value.StartsWith("file://", StringComparison.Ordinal);
        string path = (isUrl ? Uri.UnescapeDataString(url.Value["file://".Length..]) : url.Value).TrimEnd('/');
        if (!string.Equals(Path.GetFileName(path), MacRename.OldName, StringComparison.Ordinal) || exists(path)) return false;
        string bundle = app.TrimEnd('/') + "/";
        url.Value = isUrl ? "file://" + string.Join('/', bundle.Split('/').Select(Uri.EscapeDataString)) : bundle;
        if (data.Elements("key").FirstOrDefault(k => k.Value == "book") is { } book)
        {
            book.ElementsAfterSelf().FirstOrDefault()?.Remove();
            book.Remove();
        }
        return true;
    }

    /// <summary>
    /// The repair on this Mac, once per account (<see cref="DoneFile"/> in <paramref name="home"/>, written before it
    /// starts, so it never runs twice): read the Dock's settings, and only if a pinned tile needs it, keep a copy as they
    /// were in logs, write them back changed and restart the Dock. Anything unexpected leaves the Dock alone and logs
    /// one line; it never throws.
    /// </summary>
    public static void Repair(string home, string app, Runner run, Action<string> log)
    {
        try
        {
            string done = Path.Combine(home, DoneFile);
            if (File.Exists(done)) return;
            File.WriteAllText(done, "");
            if (run("/usr/bin/defaults", ["export", Domain, "-"], Wait) is not { ExitCode: 0, Stdout.Length: > 0 } export)
            {
                log("[dock] couldn't read the Dock's settings, so a pinned Syft Notes icon is left as it was");
                return;
            }
            byte[] before = Encoding.UTF8.GetBytes(export.Stdout);
            var fix = Repaired(before, app, Directory.Exists);
            if (fix.Problem is { } problem) log($"[dock] left the Dock alone: {problem}");
            if (!fix.Changed) return;
            string logs = Path.Combine(home, "logs");
            Directory.CreateDirectory(logs);
            string backup = Path.Combine(logs, "dock-before-repair.plist"), after = Path.Combine(logs, "dock-after-repair.plist");
            File.WriteAllBytes(backup, before);
            File.WriteAllBytes(after, fix.Plist);
            if (run("/usr/bin/defaults", ["import", Domain, after], Wait) is not { ExitCode: 0 })
            {
                log("[dock] couldn't write the Dock's settings, so a pinned Syft Notes icon is left as it was");
                return;
            }
            run("/usr/bin/killall", ["Dock"], Wait);
            log($"[dock] the pinned Syft Notes icon now opens {app} (the Dock as it was is in {backup})");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            log("[dock] left the Dock alone: " + e.Message);
        }
    }
}
