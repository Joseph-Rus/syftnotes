using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using StudyStash.Core;

namespace StudyStash.App.Services;

/// <summary>The library this computer used before it became the library itself: where it was and its password, so
/// everything in it can be brought over (now, or on Try again). <paramref name="Fresh"/>: the library here was made
/// by the switch, so it takes the old one's way of writing notes too. <paramref name="Tried"/>: bringing it over has
/// started at least once.</summary>
public sealed record OldLibrary(string Url, string Password, string Name, bool Fresh = false, bool Tried = false);

/// <summary>
/// Changing what this computer is for after setup (Settings → Connection → This computer, docs/one-download.md).
/// Nothing is ever deleted: a laptop that uses just this computer copies everything from the library it used (which
/// keeps its own), and a library that becomes a laptop hands everything to the other library first, then stops its
/// library and leaves its lectures, notes and settings where they are, ready for it to become the library again.
/// </summary>
public static class RoleSwitch
{
    static string Device => OperatingSystem.IsWindows() ? "PC" : "Mac";

    /// <summary>The library a laptop sends to now, if it's on another computer (so there's something to bring over).</summary>
    public static OldLibrary? OldLibraryOf(AppHost host)
    {
        var cc = host.Client();
        return cc.ServerUrl.Length > 0 && Elsewhere(host, cc.ServerUrl) ? new OldLibrary(cc.ServerUrl, cc.PoolKey, cc.PoolName) : null;
    }

    /// <summary>The address is a library on another computer, not this one's own.</summary>
    internal static bool Elsewhere(AppHost host, string url) => !Setup.IsThisComputer(url) && !IsOwnLibrary(host, url);

    /// <summary>"mac-mini" for http://mac-mini.tail1234.ts.net:8787, the address itself for a numeric one: how the
    /// student knows the old library's computer.</summary>
    public static string ComputerOf(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u) || u.Host.Length == 0) return url;
        return u.HostNameType == UriHostNameType.Dns ? u.Host.Split('.')[0] : u.Host;
    }

    // --- what's still to come over: the old library, kept until everything from it is here --------------------------

    static string PendingPath(string home) => Path.Combine(home, "bring-from.json");

    /// <summary>The old library whose lectures haven't all come over to this computer's library yet: bringing them
    /// stopped part-way, or setup made this computer the library while it was connected to another. Null when there's
    /// nothing waiting.</summary>
    public static OldLibrary? Pending(string home)
    {
        try
        {
            if (!File.Exists(PendingPath(home)) || JsonNode.Parse(File.ReadAllText(PendingPath(home))) is not JsonObject o) return null;
            string url = o["url"]?.GetValue<string>() ?? "";
            return url.Length == 0 ? null : new OldLibrary(url, o["password"]?.GetValue<string>() ?? "", o["name"]?.GetValue<string>() ?? "",
                o["fresh"]?.GetValue<bool>() ?? false, o["tried"]?.GetValue<bool>() ?? false);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    /// <summary>Keeps <paramref name="old"/> (its password too, readable by the owner only, like client.toml) until
    /// everything from it is here.</summary>
    public static void Remember(string home, OldLibrary old)
    {
        Directory.CreateDirectory(home);
        string path = PendingPath(home);
        File.WriteAllText(path, new JsonObject
        {
            ["url"] = old.Url, ["password"] = old.Password, ["name"] = old.Name, ["fresh"] = old.Fresh, ["tried"] = old.Tried,
        }.ToJsonString());
        Py.OwnerOnly(path);
    }

    /// <summary>Nothing is waiting any more: it all came, or the student chose to leave it there.</summary>
    public static void Forget(string home)
    {
        try
        {
            File.Delete(PendingPath(home));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    // --- a laptop uses just this computer ------------------------------------------------------------------------

    /// <summary>
    /// A laptop becomes the library: the library is made on this computer the way setup makes one for just this
    /// computer (reachable from here only; a library already here comes back with its own lectures and who can reach
    /// it). It takes the old library's name and password when it has none of its own, and remembers the old library
    /// (<see cref="LibraryHere.CreateAsync"/>) until everything from it has come over. This computer keeps recording
    /// (<see cref="AppRole.Both"/>). Returns what to say.
    /// </summary>
    public static async Task<string> ToLibraryAsync(AppHost host, LibraryHere here, OldLibrary? old, string displayName)
    {
        bool had = LibraryHere.Existing(host.Home) is not null;
        string? name = had ? null : old?.Name is { Length: > 0 } n ? n : $"{displayName}'s library";
        string? password = had ? null : old?.Password is { Length: >= 4 } p ? p : null;
        return await here.CreateAsync(host, name, password, displayName, AppRole.Both, localOnly: !had);
    }

    /// <summary>
    /// Copies everything from <paramref name="old"/> into the library now on this computer (<see cref="LibraryMove"/>:
    /// classes, lectures with their notes and transcripts, attached files, the library folder's other files, chats),
    /// saying how far it's got. The old library keeps all of its own. Until it's all here, <paramref name="old"/> is
    /// remembered (<see cref="Pending"/>), so Settings can say so and try again; doing it again brings only what's
    /// still missing. Returns what to say; throws <see cref="InvalidOperationException"/> saying why in plain words
    /// when it couldn't finish (whatever came stays, and the library here keeps working).
    /// </summary>
    public static async Task<string> BringLecturesAsync(AppHost host, OldLibrary old, Action<MoveProgress>? progress = null,
        Func<string, HttpClient>? http = null, CancellationToken ct = default)
    {
        var cc = host.Client();
        if (!Setup.IsThisComputer(cc.ServerUrl)) throw new InvalidOperationException($"There's no library on this {Device} to bring them to yet.");
        Remember(host.Home, old with { Tried = true });
        http ??= Client;
        using var from = http(old.Url);
        using var to = http(cc.ServerUrl);
        var result = await LibraryMove.CopyAsync(from, old.Password, to, cc.PoolKey, progress, ct, takeSettings: old.Fresh);
        if (!result.Partial) Forget(host.Home);
        await host.CheckLibraryAsync();
        return BroughtWords(result, old.Name);
    }

    /// <summary>A library's address to talk to while bringing things over: a big attached file can take minutes.</summary>
    static HttpClient Client(string url) => new() { BaseAddress = new Uri(url.TrimEnd('/') + "/"), Timeout = TimeSpan.FromMinutes(10) };

    /// <summary>"12 lectures and 3 classes came over from Sam's library, with 4 files. It keeps its own copy." and
    /// the like, with what's still to be written, what couldn't come, and what waits for an update.</summary>
    public static string BroughtWords(MoveResult r, string from)
    {
        string who = from.Length > 0 ? from : "your old library";
        var said = new List<string>();
        string also = Also(r, lectures: false);
        if (r.Filed > 0)
            said.Add($"{Things(r.Filed, "lecture")}{(r.Classes > 0 ? " and " + Things(r.Classes, "class") : "")} came over from {who}{With(r)}.");
        else if (r.Skipped > 0)
            said.Add($"Every lecture from {who} is already here." + (also.Length > 0 ? $" {Capital(also)} came over too." : ""));
        else
            said.Add($"{Capital(who)} had no lectures to bring over." + (also.Length > 0 ? $" {Capital(also)} came over." : ""));
        if (r.Filed > 0 && r.Skipped > 0) said.Add($"{(r.Skipped == 1 ? "1 was" : $"{r.Skipped.ToString(CultureInfo.InvariantCulture)} were")} already here.");
        if (r.Writing > 0)
            said.Add(r.Writing == 1 ? $"1 of them had no notes yet: this {Device} writes them now."
                : $"{r.Writing.ToString(CultureInfo.InvariantCulture)} of them had no notes yet: this {Device} writes them now.");
        if (r.Missed > 0)
            said.Add(r.Missed == 1 ? $"1 file couldn't come: it isn't on {who} any more."
                : $"{r.Missed.ToString(CultureInfo.InvariantCulture)} files couldn't come: they aren't on {who} any more.");
        if (r.Partial)
            said.Add($"{Capital(who)} runs an older Study Stash, so its attached files, other notes and chats can't come yet: update it, then press Try again.");
        if (r.Filed > 0 || r.Skipped > 0 || also.Length > 0) said.Add("It keeps its own copy.");
        return string.Join(" ", said);
    }

    /// <summary>", with 4 files and 2 chats" (or nothing), after the lectures that came.</summary>
    static string With(MoveResult r)
    {
        var parts = new List<string>();
        if (r.Files > 0) parts.Add(Things(r.Files, "file"));
        if (r.Chats > 0) parts.Add(Things(r.Chats, "chat"));
        return parts.Count == 0 ? "" : ", with " + string.Join(" and ", parts);
    }

    /// <summary>"3 classes, 4 files and 2 chats": what came besides lectures (none: "").</summary>
    static string Also(MoveResult r, bool lectures)
    {
        var parts = new List<string>();
        if (lectures && r.Filed > 0) parts.Add(Things(r.Filed, "lecture"));
        if (r.Classes > 0) parts.Add(Things(r.Classes, "class"));
        if (r.Files > 0) parts.Add(Things(r.Files, "file"));
        if (r.Chats > 0) parts.Add(Things(r.Chats, "chat"));
        return parts.Count switch { 0 => "", 1 => parts[0], _ => string.Join(", ", parts[..^1]) + " and " + parts[^1] };
    }

    static string Things(int n, string one) =>
        n == 1 ? $"1 {one}" : $"{n.ToString(CultureInfo.InvariantCulture)} {(one.EndsWith('s') ? one + "es" : one + "s")}";

    // --- a library becomes a laptop --------------------------------------------------------------------------------

    /// <summary>
    /// Before this library becomes a laptop: hands everything in it over to the library at <paramref name="address"/>
    /// (<see cref="LibraryMove"/>), so nothing is left behind. It keeps every lecture of its own, so sending is safe
    /// to do more than once. Returns what to say; throws <see cref="InvalidOperationException"/> saying why when not
    /// everything went (a wrong password, an address that can't be reached, or a library too old to take attached
    /// files and chats): then this computer should stay the library.
    /// </summary>
    public static async Task<string> HandOffLecturesAsync(AppHost host, string address, string password,
        Action<MoveProgress>? progress = null, Func<string, HttpClient>? http = null, CancellationToken ct = default)
    {
        var cc = host.Client();
        if (!Setup.IsThisComputer(cc.ServerUrl)) throw new InvalidOperationException($"There's no library on this {Device} to send from.");
        string url = Setup.NormalizeAddress(address);
        if (url.Length == 0) throw new InvalidOperationException("Type the other library's address, like http://mac-mini:8787.");
        if (IsOwnLibrary(host, url))
            throw new InvalidOperationException($"That's this {Device}'s own library. Type the address of your library on the other computer.");
        http ??= Client;
        using var from = http(cc.ServerUrl);
        using var to = http(url);
        var result = await LibraryMove.CopyAsync(from, cc.PoolKey, to, password.Trim(), progress, ct);
        if (result.Partial)
            throw new InvalidOperationException(SentWords(result) + $" That library runs an older Study Stash, so attached files, other notes and chats can't go yet: update it, then press Switch again. This {Device} stays your library until then.");
        return SentWords(result);
    }

    /// <summary>"12 lectures went to your new library. This one keeps its own copy." and the like.</summary>
    public static string SentWords(MoveResult r)
    {
        string also = Also(r, lectures: false);
        if (r.Filed == 0 && r.Skipped == 0) return also.Length > 0 ? $"There were no lectures to send. {Capital(also)} went to your new library." : "There were no lectures to send.";
        string said = r.Filed == 0 ? "Every lecture is already there." + (also.Length > 0 ? $" {Capital(also)} went too." : "")
            : $"{Things(r.Filed, "lecture")}{(r.Classes > 0 ? " and " + Things(r.Classes, "class") : "")} went to your new library{With(r)}.";
        if (r.Filed > 0 && r.Skipped > 0) said += $" {(r.Skipped == 1 ? "1 was" : $"{r.Skipped.ToString(CultureInfo.InvariantCulture)} were")} already there.";
        if (r.Missed > 0) said += r.Missed == 1 ? " 1 file couldn't go: it was missing here." : $" {r.Missed.ToString(CultureInfo.InvariantCulture)} files couldn't go: they were missing here.";
        return said + " This library keeps its own copy.";
    }

    /// <summary>The address is this computer's own library: its port, here (by loopback or by this computer's name).</summary>
    internal static bool IsOwnLibrary(AppHost host, string url)
    {
        if (LibraryHere.Existing(host.Home) is not { } cfg || !Uri.TryCreate(url, UriKind.Absolute, out var u) || u.Port != cfg.WebPort) return false;
        string me = Machine.HostName();
        return Setup.IsThisComputer(url) || u.Host.Equals(me, StringComparison.OrdinalIgnoreCase)
            || u.Host.StartsWith(me + ".", StringComparison.OrdinalIgnoreCase);
    }

    static string Capital(string s) => char.ToUpper(s[0], CultureInfo.InvariantCulture) + s[1..];

    /// <summary>
    /// The library becomes a laptop that sends to the library at <paramref name="address"/>: that library is checked
    /// first (nothing changes if it can't be reached, the password is wrong, or it's this computer's own library), then this
    /// computer's library stops and client.toml points at the other one. Its lectures, notes folder and config stay on
    /// disk untouched. A computer that didn't record before gets its transcription model. Returns what to say; throws
    /// <see cref="InvalidOperationException"/> saying why when nothing changed.
    /// </summary>
    public static async Task<string> ToLaptopAsync(AppHost host, string address, string password,
        Func<string, string, Task<JsonObject>>? check = null)
    {
        string url = Setup.NormalizeAddress(address);
        if (url.Length == 0) throw new InvalidOperationException("Type your library's address, like http://mac-mini:8787.");
        if (IsOwnLibrary(host, url))
            throw new InvalidOperationException($"That's this {Device}'s own library. Type the address of your library on the other computer.");
        JsonObject health;
        try
        {
            health = await (check ?? ((a, k) => LibraryApi.CheckServerAsync(a, k)))(url, password.Trim());
        }
        catch (InvalidOperationException e)
        {
            throw new InvalidOperationException(e.Message == "wrong password" ? "That password isn't right."
                : "Can't reach that address. Is the library's computer on, and is Tailscale connected?", e);
        }
        if (host.LocalLibrary is { } svc) await svc.StopAsync();
        bool recordedBefore = host.Settings.Role != AppRole.Library;
        var cc = host.Client();
        cc.ServerUrl = url;
        cc.PoolKey = password.Trim();
        cc.PoolName = health["pool_name"]?.GetValue<string>() ?? "";
        host.SaveClient(cc);
        host.Save(s => s.Role = AppRole.Laptop);
        // Nothing comes to this computer's library now: whatever an old library still had stays there.
        Forget(host.Home);
        if (!recordedBefore && !host.ModelReady) _ = host.DownloadModelAsync();
        await host.CheckLibraryAsync();
        string name = cc.PoolName.Length > 0 ? cc.PoolName : "your library";
        return $"This {Device} is a laptop now, sending lectures to {name}. Its own lectures stay here, and it can be the library again any time.";
    }
}
