using System.Text.Json;
using System.Text.Json.Nodes;

namespace StudyStash.Core.Canvas;

/// <summary>
/// The browser extension that reads Canvas with the person's own sign-in and hands it to the library. Its files are
/// inside the engine; <see cref="Ensure"/> writes them out as a folder Chrome, or any browser built on it (Edge,
/// Brave, Arc, Opera, Vivaldi), loads ("Load unpacked"), with a config.json (and config.js, for a copy from before
/// 1.4) that says where the library is and holds the extension's key. The library keeps its own folder ready by
/// itself: on start, and whenever the Canvas address changes. When Study Stash updates, the folder is brought up to
/// date, and the extension reloads itself from it the next time it checks in. Firefox, and the browsers built on it
/// (Zen, LibreWolf, Waterfox), keep only an add-on Mozilla has signed, so theirs is packed instead
/// (<see cref="PackForFirefox"/>) and connected by pasting a code (<see cref="ConnectionCode"/>), like a copy from the
/// Chrome Web Store (<see cref="PackForStore"/>).
/// </summary>
public static class Extension
{
    static readonly System.Reflection.Assembly Here = typeof(Extension).Assembly;

    /// <summary>Where Canvas keeps file bodies (a download redirects there): the extension may read them, and so may
    /// an AI's reads. One list for the extension's host permissions, its config.js and the library's own check.</summary>
    public static readonly IReadOnlyList<string> FileHosts = ["*.inscloudgate.net"];

    /// <summary>What the extension and the library say to each other. 2: the extension says when Chrome is signed
    /// out, passes on Canvas's rate limit, never hands over an error page or an oversized file as a file, reloads
    /// itself before taking work when its folder is newer, and posts files one at a time. 3: it waits for work (the
    /// library holds its request until there is some), says which library address it uses, and reloads itself when
    /// its folder's host permissions change too. The extension sends its own as <c>p</c>; config.json says which one
    /// the Study Stash that wrote the folder speaks.</summary>
    public const int Protocol = 3;

    static IEnumerable<string> Files() =>
        Here.GetManifestResourceNames().Where(n => n.StartsWith("extension/", StringComparison.Ordinal)).Select(n => n["extension/".Length..]);

    static byte[] Read(string name)
    {
        using var s = Here.GetManifestResourceStream("extension/" + name)!;
        using var m = new MemoryStream();
        s.CopyTo(m);
        return m.ToArray();
    }

    static readonly Lazy<string> Current = new(() => JsonNode.Parse(Read("manifest.json"))?["version"]?.GetValue<string>() ?? "");

    /// <summary>The version in this engine's copy ("1.3").</summary>
    public static string Version() => Current.Value;

    /// <summary>"1.2" is older than "1.3" (and "1.10" newer than "1.9"). False when either isn't a version.</summary>
    public static bool IsOlder(string version, string than) => Parse(version) is { } a && Parse(than) is { } b && a < b;

    static System.Version? Parse(string v) =>
        System.Version.TryParse(v.Contains('.', StringComparison.Ordinal) ? v : v + ".0", out var parsed) ? parsed : null;

    /// <summary>Whether a URL is on Canvas's file store: https, a host in <see cref="FileHosts"/>, nothing else in
    /// the address before the path.</summary>
    public static bool OnFileHost(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps && u.UserInfo.Length == 0 && u.IsDefaultPort
        && FileHosts.Any(h => h.StartsWith("*.", StringComparison.Ordinal)
            ? u.Host.EndsWith(h[1..], StringComparison.OrdinalIgnoreCase) : u.Host.Equals(h, StringComparison.OrdinalIgnoreCase));

    /// <summary>Where Chrome loads it from on this computer (<see cref="Folder(string, string, bool)"/> for this
    /// person and this system).</summary>
    public static string Folder(string home) => Folder(home, Py.UserHome(), OperatingSystem.IsMacOS());

    /// <summary>
    /// Where Chrome loads it from. On a Mac, Chrome's Load unpacked window hides dot-folders and ~/Library, and the
    /// home (~/.study-stash) is one, so the folder there is <c>~/Study Stash/Chrome extension</c>: a folder the window
    /// shows at once, never synced to iCloud (not Desktop or Documents), and only this person's (it holds what
    /// connects to the library). A second hidden home in the same account gets its own ("Chrome extension (1a2b3c4d)"),
    /// so the two never write over each other. Anywhere else, and for a home outside <paramref name="userHome"/> (a
    /// test's, or one the student put somewhere Chrome shows), it's the home's own chrome-extension, as it always was.
    /// </summary>
    public static string Folder(string home, string userHome, bool mac)
    {
        string inHome = InHome(home);
        if (!mac || userHome.Length == 0) return inHome;
        string full = Path.GetFullPath(home), user = Path.GetFullPath(userHome);
        string relative = Path.GetRelativePath(user, full);
        if (relative == "." || relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative)) return inHome;
        var parts = relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        bool hidden = parts[0] == "Library" || parts.Any(p => p.StartsWith('.'));
        if (!hidden) return inHome;
        string name = relative == ".study-stash" ? "Chrome extension"
            : $"Chrome extension ({Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(full)))[..8]})";
        return Path.Combine(user, "Study Stash", name);
    }

    /// <summary>The folder inside the home, where Study Stash wrote it before it had a folder of its own on a Mac. A
    /// Chrome that loaded it from there keeps loading it from there, so it's kept up to date too
    /// (<see cref="EnsureFor"/>).</summary>
    public static string InHome(string home) => Path.Combine(home, "chrome-extension");

    /// <summary>
    /// <see cref="Ensure"/> for a home: its folder (<see cref="Folder(string)"/>), and the one an older Study Stash
    /// wrote inside the home when that's another folder and it's there (a Chrome may have loaded it and still be
    /// connected through it: it gets the same update, so it never breaks). The result is the home's own folder.
    /// </summary>
    public static EnsureResult EnsureFor(string home, string library, string key, string canvasUrl) =>
        EnsureFor(home, Folder(home), library, key, canvasUrl);

    /// <summary><see cref="EnsureFor(string, string, string, string)"/> with the home's folder given (tests).</summary>
    public static EnsureResult EnsureFor(string home, string folder, string library, string key, string canvasUrl)
    {
        // The folder that holds it (~/Study Stash on a Mac), when Study Stash makes it, is only this person's too.
        if (!OperatingSystem.IsWindows() && Path.GetDirectoryName(Path.GetFullPath(folder)) is { } parent && !Directory.Exists(parent))
            Directory.CreateDirectory(parent, OwnerOnlyFolder);
        var result = Ensure(folder, library, key, canvasUrl);
        string old = InHome(home);
        if (!SamePath(old, folder) && File.Exists(Path.Combine(old, "manifest.json")))
        {
            try
            {
                if (Ensure(old, library, key, canvasUrl).Changed) result = result with { Changed = true };
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // The old folder is a courtesy to a Chrome that loaded it; the home's own folder is what counts.
            }
        }
        return result;
    }

    static bool SamePath(string a, string b) =>
        string.Equals(Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar), Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar),
            OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);

    /// <summary>How a manifest is written: indented, and with its quotes left as they are (Firefox's content policy
    /// has 'self' in it).</summary>
    static readonly JsonSerializerOptions Indented = new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    const UnixFileMode OwnerOnlyFolder = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    /// <summary>
    /// Make the folder Chrome loads, or bring it up to date, so it points at this library and this Canvas: the
    /// extension's files, a manifest that may reach only this Canvas (none while <paramref name="canvasUrl"/> is empty),
    /// its file store and this library, and config.json + config.js (the same: where the library is, the extension's
    /// key, the Canvas address), readable only by this person. <paramref name="library"/> is the library's address as
    /// this computer reaches it. Writes only what differs, so calling it again with the same values changes nothing;
    /// a folder an older Study Stash made (config.js only) gains config.json.
    /// </summary>
    public static EnsureResult Ensure(string dir, string library, string key, string canvasUrl)
    {
        // Only this person's, like its config: what connects to the library is in it.
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(dir);
        else
        {
            Directory.CreateDirectory(dir, OwnerOnlyFolder);
            File.SetUnixFileMode(dir, OwnerOnlyFolder);
        }
        bool changed = WriteScripts(dir);
        changed |= WriteManifest(dir, Hosts(library, canvasUrl));
        string json = ConfigJson(library, key, canvasUrl);
        changed |= WriteOwnerOnly(Path.Combine(dir, "config.json"), json + "\n");
        changed |= WriteOwnerOnly(Path.Combine(dir, "config.js"), $"const STUDY_STASH = {json};\n");
        return new EnsureResult(changed, dir);
    }

    /// <summary>config.json's text: where the library is, the extension's key, the Canvas address, Canvas's file
    /// store and the protocol.</summary>
    static string ConfigJson(string library, string key, string canvasUrl) => new JsonObject
    {
        ["app"] = library.TrimEnd('/'), ["key"] = key, ["canvas"] = canvasUrl.TrimEnd('/'),
        ["files"] = new JsonArray(FileHosts.Select(h => (JsonNode)h).ToArray()), ["protocol"] = Protocol,
    }.ToJsonString();

    /// <summary>
    /// The code a copy from a browser's store is connected with (it has no folder for Study Stash to write): the
    /// config.json <see cref="Ensure"/> would write for this library address, key and Canvas, in base64url so it
    /// survives being copied and pasted. The popup decodes it (connection.js <c>decodeCode</c>). Empty while there's
    /// no Canvas address: the extension couldn't ask Chrome for Canvas yet.
    /// </summary>
    public static string ConnectionCode(string library, string key, string canvasUrl) =>
        canvasUrl.Trim().Length == 0 || key.Length == 0 || library.Trim().Length == 0 ? ""
            : System.Buffers.Text.Base64Url.EncodeToString(System.Text.Encoding.UTF8.GetBytes(ConfigJson(library.Trim(), key, canvasUrl.Trim())));

    /// <summary>What a connection code connects to; null when the text isn't one (whitespace from copying is fine).</summary>
    public static ExtensionConnection? ReadConnectionCode(string code)
    {
        string text = string.Concat(code.Where(c => !char.IsWhiteSpace(c))).Trim('"', '\'').TrimEnd('=');
        if (text.Length == 0 || !System.Buffers.Text.Base64Url.IsValid(text)) return null;
        try
        {
            if (JsonNode.Parse(System.Buffers.Text.Base64Url.DecodeFromChars(text)) is not JsonObject o) return null;
            static string Str(JsonNode? n) => n is JsonValue v && v.TryGetValue(out string? s) ? s : "";
            return Str(o["app"]) is { Length: > 0 } app && Str(o["key"]) is { Length: > 0 } key ? new ExtensionConnection(app, key, Str(o["canvas"])) : null;
        }
        catch (Exception e) when (e is JsonException or FormatException)
        {
            return null;
        }
    }

    /// <summary>What a store copy (the Chrome Web Store's, Firefox's) may reach once the student allows it: any https
    /// or http site, asked for one by one when they paste a code (their school's Canvas and their library aren't known
    /// when it's published). Nothing is granted at install.</summary>
    public static readonly IReadOnlyList<string> StoreOptionalHosts = ["https://*/*", "http://*/*"];

    /// <summary>The manifest of the Chrome Web Store copy: this engine's, with no host permissions (it asks for its
    /// Canvas and library when connected by code, <see cref="StoreOptionalHosts"/>).</summary>
    public static JsonObject StoreManifest()
    {
        var manifest = JsonNode.Parse(Read("manifest.json"))!.AsObject();
        manifest.Remove("host_permissions");
        manifest["optional_host_permissions"] = new JsonArray(StoreOptionalHosts.Select(h => (JsonNode)h).ToArray());
        return manifest;
    }

    /// <summary>The files a store copy is made of: this engine's scripts, pages and icons (never a config: a store
    /// copy is connected by code), in name order.</summary>
    public static IReadOnlyList<string> StoreFiles() =>
        [.. Files().Where(f => f is not ("config.js" or "config.json")).Order(StringComparer.Ordinal)];

    /// <summary>The add-on's ID in Firefox: Mozilla signs the add-on under it, and an update must carry the same one,
    /// so it never changes once the add-on is published.</summary>
    public const string FirefoxId = "canvas@study-stash-app.web.app";

    /// <summary>The oldest Firefox the add-on installs in: 140 (June 2025, also an extended-support release) has
    /// everything the extension uses, and shows what it sends where before the student adds it.</summary>
    public const string FirefoxMinVersion = "140.0";

    /// <summary>What the Firefox copy tells the student it sends out of the browser before they add it (Firefox's
    /// own list of kinds): Canvas's pages and files, and the announcements and feedback among them. They go only to
    /// the student's own library.</summary>
    public static readonly IReadOnlyList<string> FirefoxDataSent = ["websiteContent", "personalCommunications"];

    /// <summary>
    /// The manifest of the Firefox copy: the store copy's (<see cref="StoreManifest"/>: no site until it's connected
    /// by code), run as a background page rather than a service worker (Firefox has none for extensions; the page's
    /// scripts are connection.js then background.js, which a service worker brings in itself), with Firefox's own
    /// settings (<see cref="FirefoxId"/>, <see cref="FirefoxMinVersion"/>, <see cref="FirefoxDataSent"/>) in place of
    /// Chrome's oldest version, and a content policy that leaves http alone: Firefox would otherwise turn every
    /// request into https, and a library on the student's own network is reached over http.
    /// </summary>
    public static JsonObject FirefoxManifest()
    {
        var manifest = StoreManifest();
        manifest.Remove("minimum_chrome_version");
        manifest["background"] = new JsonObject { ["scripts"] = new JsonArray("connection.js", "background.js") };
        manifest["content_security_policy"] = new JsonObject { ["extension_pages"] = "script-src 'self'" };
        manifest["browser_specific_settings"] = new JsonObject
        {
            ["gecko"] = new JsonObject
            {
                ["id"] = FirefoxId, ["strict_min_version"] = FirefoxMinVersion,
                ["data_collection_permissions"] = new JsonObject { ["required"] = new JsonArray(FirefoxDataSent.Select(k => (JsonNode)k).ToArray()) },
            },
        };
        return manifest;
    }

    /// <summary>
    /// Write the zip to upload to the Chrome Web Store: <see cref="StoreFiles"/> at its top level with
    /// <see cref="StoreManifest"/>. The same engine always makes the same bytes (fixed order, times and compression),
    /// so a new zip differs only when the extension did. Returns the names in it. Edge's and Opera's stores take the
    /// same zip.
    /// </summary>
    public static IReadOnlyList<string> PackForStore(string zipPath) => Pack(zipPath, StoreManifest());

    /// <summary>
    /// Write the zip Mozilla signs (addons.mozilla.org): the same files with <see cref="FirefoxManifest"/>, and the
    /// same bytes from the same engine. Firefox's developer and nightly builds install it unsigned.
    /// </summary>
    public static IReadOnlyList<string> PackForFirefox(string zipPath) => Pack(zipPath, FirefoxManifest());

    static IReadOnlyList<string> Pack(string zipPath, JsonObject manifest)
    {
        var names = StoreFiles();
        var stamp = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
        using var buffer = new MemoryStream();
        using (var zip = new System.IO.Compression.ZipArchive(buffer, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (string name in names)
            {
                byte[] bytes = name == "manifest.json"
                    ? System.Text.Encoding.UTF8.GetBytes(manifest.ToJsonString(Indented) + "\n") : Read(name);
                var entry = zip.CreateEntry(name, System.IO.Compression.CompressionLevel.Optimal);
                entry.LastWriteTime = stamp;
                using var into = entry.Open();
                into.Write(bytes);
            }
        }
        if (Path.GetDirectoryName(Path.GetFullPath(zipPath)) is { } parent) Directory.CreateDirectory(parent);
        File.WriteAllBytes(zipPath, buffer.ToArray());
        return names;
    }

    /// <summary><see cref="Ensure"/>, for callers that only want the folder back.</summary>
    public static string Prepare(string dir, string library, string key, string canvasUrl) => Ensure(dir, library, key, canvasUrl).Path;

    /// <summary>Whether the folder has what Chrome needs to load it and connect: a manifest, the service worker and
    /// the connection (config.json).</summary>
    public static bool Ready(string dir) =>
        File.Exists(Path.Combine(dir, "manifest.json")) && File.Exists(Path.Combine(dir, "background.js")) && File.Exists(Path.Combine(dir, "config.json"));

    /// <summary>
    /// Bring a folder <see cref="Ensure"/> made (perhaps by an older Study Stash) up to this one, keeping what it
    /// connects to: its scripts, pages and manifest take this version and config.json + config.js are rewritten with
    /// the same library address, key and Canvas. Chrome's running copy sees the new version on its next visit and
    /// reloads itself. A folder without manifest.json and a readable config isn't one of ours and is left alone.
    /// True when the version changed.
    /// </summary>
    public static bool Refresh(string dir)
    {
        string manifestPath = Path.Combine(dir, "manifest.json");
        if (!File.Exists(manifestPath) || Connection(dir) is not { } kept) return false;
        string before;
        try
        {
            before = JsonNode.Parse(File.ReadAllText(manifestPath)) is JsonObject { } old && old["version"] is JsonValue v && v.TryGetValue(out string? s) ? s : "";
        }
        catch (JsonException)
        {
            return false; // not a manifest Chrome could load either: the library writes a fresh folder on start
        }
        Ensure(dir, kept.App, kept.Key, kept.Canvas);
        return before != Version();
    }

    /// <summary>What a folder connects to (config.json, or the config.js of a folder from before 1.4); null when
    /// there's neither, or it can't be read.</summary>
    public static ExtensionConnection? Connection(string dir)
    {
        string text;
        string json = Path.Combine(dir, "config.json"), js = Path.Combine(dir, "config.js");
        if (File.Exists(json)) text = File.ReadAllText(json);
        else if (File.Exists(js))
        {
            text = File.ReadAllText(js);
            int open = text.IndexOf('{', StringComparison.Ordinal), close = text.LastIndexOf('}');
            if (open < 0 || close < open) return null;
            text = text[open..(close + 1)];
        }
        else return null;
        try
        {
            if (JsonNode.Parse(text) is not JsonObject o) return null;
            static string Str(JsonNode? n) => n is JsonValue v && v.TryGetValue(out string? s) ? s : "";
            return Str(o["app"]) is { Length: > 0 } app ? new ExtensionConnection(app, Str(o["key"]), Str(o["canvas"])) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Where the extension may reach: this Canvas (once there is one), its file store, and the library. A
    /// default port is left out ("https://mini.tail.ts.net/*"), the way Chrome writes match patterns.</summary>
    static JsonArray Hosts(string library, string canvasUrl)
    {
        var hosts = new JsonArray();
        if (Uri.TryCreate(canvasUrl.Trim(), UriKind.Absolute, out var canvas) && canvas.Host.Length > 0) hosts.Add(Origin(canvas) + "/*");
        foreach (string h in FileHosts) hosts.Add($"https://{h}/*");
        hosts.Add(Origin(new Uri(library)) + "/*");
        return hosts;
    }

    static string Origin(Uri u) => u.IsDefaultPort ? $"{u.Scheme}://{u.Host}" : $"{u.Scheme}://{u.Host}:{u.Port}";

    /// <summary>The extension's scripts, pages and pictures as this engine has them; true when any was written.</summary>
    static bool WriteScripts(string dir)
    {
        bool changed = false;
        foreach (string f in Files())
        {
            if (f is "manifest.json" or "config.js" or "config.json") continue;
            byte[] now = Read(f);
            string path = Path.Combine(dir, f);
            if (File.Exists(path) && File.ReadAllBytes(path).AsSpan().SequenceEqual(now)) continue;
            File.WriteAllBytes(path, now);
            changed = true;
        }
        return changed;
    }

    /// <summary>This engine's manifest with these host permissions, written only when it differs.</summary>
    static bool WriteManifest(string dir, JsonArray hosts)
    {
        var manifest = JsonNode.Parse(Read("manifest.json"))!.AsObject();
        manifest["host_permissions"] = hosts;
        string path = Path.Combine(dir, "manifest.json"), text = manifest.ToJsonString(Indented) + "\n";
        if (File.Exists(path) && File.ReadAllText(path) == text) return false;
        File.WriteAllText(path, text);
        return true;
    }

    /// <summary>A file only this person may read (it holds the extension's key), written only when it differs.</summary>
    static bool WriteOwnerOnly(string path, string text)
    {
        bool same = File.Exists(path) && File.ReadAllText(path) == text;
        if (!same) File.WriteAllText(path, text);
        Py.OwnerOnly(path);
        return !same;
    }
}

/// <summary>What <see cref="Extension.Ensure"/> did: whether anything in the folder changed, and where it is.</summary>
public sealed record EnsureResult(bool Changed, string Path);

/// <summary>What an extension folder connects to: the library's address, the extension's key, the Canvas address.</summary>
public sealed record ExtensionConnection(string App, string Key, string Canvas);
