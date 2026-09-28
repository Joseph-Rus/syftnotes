using System.Globalization;
using StudyStash.Core;

namespace StudyStash.App.Services;

/// <summary>The library this computer used before it became the library itself: where it was and its password, so
/// its lectures can be brought over (now, or on Try again).</summary>
public sealed record OldLibrary(string Url, string Password, string Name);

/// <summary>
/// Changing what this computer is for after setup (Settings → Your library, docs/one-download.md). Nothing is ever
/// deleted: a laptop that becomes the library copies the old library's lectures (which keeps its own), and a library
/// that becomes a laptop stops its library and leaves its lectures, notes and settings where they are, ready for it to
/// become the library again.
/// </summary>
public static class RoleSwitch
{
    static string Device => OperatingSystem.IsWindows() ? "PC" : "Mac";

    /// <summary>The library a laptop sends to now, if it's on another computer (so there's something to bring over).</summary>
    public static OldLibrary? OldLibraryOf(AppHost host)
    {
        var cc = host.Client();
        return cc.ServerUrl.Length > 0 && !Setup.IsThisComputer(cc.ServerUrl) ? new OldLibrary(cc.ServerUrl, cc.PoolKey, cc.PoolName) : null;
    }

    /// <summary>
    /// A laptop becomes the library: the library is made on this computer the way setup makes one (a library already
    /// here comes back with its own lectures), open to laptops so the old library's computer can connect as one. It
    /// takes the old library's name and password when it has none of its own, so connecting over there is just a new
    /// address. This computer keeps recording (just this computer, <see cref="AppRole.Both"/>). Returns what to say.
    /// </summary>
    public static async Task<string> ToLibraryAsync(AppHost host, LibraryHere here, OldLibrary? old, string displayName)
    {
        bool had = LibraryHere.Existing(host.Home) is not null;
        string? name = had ? null : old?.Name is { Length: > 0 } n ? n : $"{displayName}'s library";
        string? password = had ? null : old?.Password is { Length: >= 4 } p ? p : null;
        string done = await here.CreateAsync(host, name, password, displayName, AppRole.Both, laptops: true);
        return done;
    }

    /// <summary>
    /// Copies every lecture from <paramref name="old"/> into the library now on this computer, saying how far it's
    /// got. The old library keeps all of its own. Returns what to say; throws <see cref="InvalidOperationException"/>
    /// saying why in plain words when it couldn't (the library here stays as it is, and it can be tried again).
    /// </summary>
    public static async Task<string> BringLecturesAsync(AppHost host, OldLibrary old, Action<MoveProgress>? progress = null,
        Func<string, HttpClient>? http = null, CancellationToken ct = default)
    {
        var cc = host.Client();
        if (!Setup.IsThisComputer(cc.ServerUrl)) throw new InvalidOperationException($"There's no library on this {Device} to bring them to yet.");
        http ??= url => new HttpClient { BaseAddress = new Uri(url.TrimEnd('/') + "/"), Timeout = TimeSpan.FromMinutes(2) };
        using var from = http(old.Url);
        using var to = http(cc.ServerUrl);
        var result = await LibraryMove.CopyAsync(from, old.Password, to, cc.PoolKey, progress, ct);
        await host.CheckLibraryAsync();
        return BroughtWords(result, old.Name);
    }

    /// <summary>"12 lectures came over from Sam's library. It keeps its own copy." and the like.</summary>
    public static string BroughtWords(MoveResult r, string from)
    {
        string who = from.Length > 0 ? from : "your old library";
        string lectures = r.Filed == 1 ? "1 lecture" : $"{r.Filed.ToString(CultureInfo.InvariantCulture)} lectures";
        if (r.Filed == 0 && r.Skipped == 0) return $"{Capital(who)} had no lectures to bring over.";
        string said = r.Filed == 0 ? $"Every lecture from {who} is already here." : $"{lectures} came over from {who}.";
        if (r.Filed > 0 && r.Skipped > 0) said += $" {(r.Skipped == 1 ? "1 was" : $"{r.Skipped} were")} already here.";
        return said + " It keeps its own copy.";
    }

    /// <summary>
    /// Before this library becomes a laptop: hands its lectures over to the library at <paramref name="address"/>, so
    /// nothing is left behind. This library is checked first, the same way <see cref="ToLaptopAsync"/> checks the one
    /// a laptop connects to; it keeps every lecture of its own, so sending them is safe to do more than once. Returns
    /// what to say; throws <see cref="InvalidOperationException"/> saying why when nothing went (a wrong password, or
    /// the address can't be reached).
    /// </summary>
    public static async Task<string> HandOffLecturesAsync(AppHost host, string address, string password,
        Action<MoveProgress>? progress = null, Func<string, HttpClient>? http = null, CancellationToken ct = default)
    {
        var cc = host.Client();
        if (!Setup.IsThisComputer(cc.ServerUrl)) throw new InvalidOperationException($"There's no library on this {Device} to send from.");
        string url = Setup.NormalizeAddress(address);
        if (url.Length == 0) throw new InvalidOperationException("Type the other library's address, like http://mac-mini:8787.");
        http ??= a => new HttpClient { BaseAddress = new Uri(a.TrimEnd('/') + "/"), Timeout = TimeSpan.FromMinutes(2) };
        using var from = http(cc.ServerUrl);
        using var to = http(url);
        var result = await LibraryMove.CopyAsync(from, cc.PoolKey, to, password.Trim(), progress, ct);
        return SentWords(result);
    }

    /// <summary>"12 lectures went to your new library. This one keeps its own copy." and the like.</summary>
    public static string SentWords(MoveResult r)
    {
        string lectures = r.Filed == 1 ? "1 lecture" : $"{r.Filed.ToString(CultureInfo.InvariantCulture)} lectures";
        if (r.Filed == 0 && r.Skipped == 0) return "There were no lectures to send.";
        string said = r.Filed == 0 ? "Every lecture is already there." : $"{lectures} went to your new library.";
        if (r.Filed > 0 && r.Skipped > 0) said += $" {(r.Skipped == 1 ? "1 was" : $"{r.Skipped} were")} already there.";
        return said + " This library keeps its own copy.";
    }

    /// <summary>The address is this computer's own library: its port, here (by loopback or by this computer's name).</summary>
    static bool IsOwnLibrary(AppHost host, string url)
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
        Func<string, string, Task<System.Text.Json.Nodes.JsonObject>>? check = null)
    {
        string url = Setup.NormalizeAddress(address);
        if (url.Length == 0) throw new InvalidOperationException("Type your library's address, like http://mac-mini:8787.");
        if (IsOwnLibrary(host, url))
            throw new InvalidOperationException($"That's this {Device}'s own library. Type the address of your library on the other computer.");
        System.Text.Json.Nodes.JsonObject health;
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
        if (!recordedBefore && !host.ModelReady) _ = host.DownloadModelAsync();
        await host.CheckLibraryAsync();
        string name = cc.PoolName.Length > 0 ? cc.PoolName : "your library";
        return $"This {Device} is a laptop now, sending lectures to {name}. Its own lectures stay here, and it can be the library again any time.";
    }
}
