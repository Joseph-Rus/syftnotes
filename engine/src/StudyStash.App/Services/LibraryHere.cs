using StudyStash.Core;

namespace StudyStash.App.Services;

/// <summary>
/// Making this computer the library: writes its config, starts <see cref="LibraryService"/> as this app's own child
/// process (nothing installed), and points the laptop side at it. <paramref name="host"/> (see <see cref="CreateAsync"/>)
/// takes charge of that library from then on, so it stops with the app, even on the very first run, before
/// <see cref="AppHost.Start"/> would otherwise have started one itself.
/// </summary>
public sealed class LibraryHere
{
    /// <summary>The engine to run: the app itself (`StudyStash --home H serve`) unless a test gives its own.</summary>
    public IReadOnlyList<string>? Command { get; init; }
    /// <summary>Where a new library keeps its notes: <see cref="DefaultFolder"/> unless a test gives its own.</summary>
    public string? Folder { get; init; }
    /// <summary>The first port a new library tries (then the next free pair): 8787 unless a test gives its own.</summary>
    public int FirstPort { get; init; } = 8787;

    /// <summary>This computer's library maker. A copy under test (the self-test) keeps a new library's notes and port
    /// away from the real ones: STUDYSTASH_NOTES_DIR and STUDYSTASH_LIBRARY_PORT say where.</summary>
    public static LibraryHere ThisComputer() => new()
    {
        Folder = Environment.GetEnvironmentVariable("STUDYSTASH_NOTES_DIR") is { Length: > 0 } dir ? dir : null,
        FirstPort = Environment.GetEnvironmentVariable("STUDYSTASH_LIBRARY_PORT") is { Length: > 0 } p && int.TryParse(p, out int port) ? port : 8787,
    };

    /// <summary>The folder a new library keeps its notes in: Documents/Study Stash.</summary>
    public static string DefaultFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Study Stash");

    /// <summary>The library on this computer listens to this computer alone (just this computer's setup), not to
    /// the network.</summary>
    public static bool OnlyHere(Config cfg) => System.Net.IPAddress.TryParse(cfg.WebHost, out var ip) && System.Net.IPAddress.IsLoopback(ip);

    /// <summary>
    /// Settings → Your library's "Add a laptop" (and turning it off again): the library on this computer listens to the
    /// network, with <paramref name="password"/> for laptops to connect with, or to this computer alone. It starts
    /// again to listen the new way; this computer's own connection follows a new password. Throws saying why when
    /// there's no library here or the password is too short (nothing is changed then).
    /// </summary>
    public async Task LetLaptopsConnectAsync(AppHost host, bool laptops, string? password = null)
    {
        // What's on disk, not the running library's copy: its classes and settings may have changed since it started.
        var cfg = Existing(host.Home) ?? throw new InvalidOperationException("There's no library on this computer.");
        password = password?.Trim();
        if (laptops && password is not null && password.Length < 4) throw new ArgumentException("Use a password of at least 4 characters.");
        if (laptops && password is null && cfg.PoolPassword.Length < 4) throw new ArgumentException("Give your library a password first.");
        string old = cfg.PoolPassword;
        cfg.WebHost = laptops ? "0.0.0.0" : "127.0.0.1";
        if (laptops && password is not null) cfg.PoolPassword = password;
        Configs.Save(cfg);
        var cc = host.Client();
        if (cc.PoolKey == old || cc.ServerUrl.Length == 0)
        {
            cc.PoolKey = cfg.PoolPassword;
            host.SaveClient(cc);
        }
        if (host.LocalLibrary is { } svc)
        {
            await RestartAsync(svc, cfg);
        }
        else
        {
            var fresh = Command is null ? new LibraryService(host.Home, cfg) : new LibraryService(host.Home, cfg, Command);
            host.UseLocalLibrary(fresh);
            await fresh.StartAsync();
        }
        await host.CheckLibraryAsync();
    }

    /// <summary>The running library takes its new settings: its own copy learns the password it's checked with (and
    /// its name and who can reach it), and it starts again, reading the rest from disk.</summary>
    static async Task RestartAsync(LibraryService svc, Config saved)
    {
        svc.Cfg.PoolPassword = saved.PoolPassword;
        svc.Cfg.PoolName = saved.PoolName;
        svc.Cfg.WebHost = saved.WebHost;
        await svc.StopAsync();
        await svc.StartAsync();
    }

    /// <summary>8787 if it (and 8788, for Claude) are free; otherwise the next pair that both are.</summary>
    static int FreePortPair(int first)
    {
        for (int port = first; port < first + 200; port++)
            if (HostInfo.PortFree(port) && HostInfo.PortFree(port + 1))
                return port;
        throw new InvalidOperationException("No free ports found for the library.");
    }

    /// <summary>The library already on this computer (setup run again, or a laptop switching back): its config,
    /// or null when there's none (or it can't be read).</summary>
    public static Config? Existing(string home)
    {
        try
        {
            var cfg = Configs.Load(home);
            return File.Exists(cfg.ConfigPath) ? cfg : null;
        }
        catch (Exception e) when (e is FormatException or IOException or UnauthorizedAccessException or InvalidOperationException or Tomlyn.TomlException)
        {
            return null;
        }
    }

    /// <summary>
    /// Write config.toml for a library on this computer (or fill in one already here), start it, and point client.toml
    /// at it (both roles keep talking to it over the network, even Library-only, so the library window still works).
    /// A library already here keeps its notes folder, port and who can reach it, unless <paramref name="localOnly"/>
    /// keeps it to this computer or <paramref name="laptops"/> opens it to them; a null name or password keeps its own
    /// (or, for a new library, names it and makes up a password). One this app already runs (setup gone back and
    /// forth between its choices) starts again with the new settings.
    /// </summary>
    public async Task<string> CreateAsync(AppHost host, string? name, string? password, string displayName,
        AppRole role = AppRole.Both, bool localOnly = false, bool laptops = false)
    {
        var existing = Existing(host.Home);
        var cfg = existing ?? Configs.Load(host.Home);
        name = name?.Trim() is { Length: > 0 } n ? n : existing?.PoolName is { Length: > 0 } had ? had : "My library";
        password = password is null ? (existing?.PoolPassword is { Length: >= 4 } kept ? kept : StudyStash.Library.Http.TokenUrlSafe(12)) : password.Trim();
        if (password.Length < 4) throw new ArgumentException("Use a password of at least 4 characters.");
        if (existing is null)
        {
            cfg.PoolDir = Folder ?? DefaultFolder;
            cfg.WebPort = FreePortPair(FirstPort);
            cfg.WebHost = localOnly ? "127.0.0.1" : "0.0.0.0";
        }
        else if (localOnly || laptops)
        {
            cfg.WebHost = localOnly ? "127.0.0.1" : "0.0.0.0";
        }
        cfg.PoolName = name;
        cfg.PoolPassword = password;
        if (cfg.AdminPassword.Length == 0) cfg.AdminPassword = StudyStash.Library.Http.TokenUrlSafe(12);
        Directory.CreateDirectory(cfg.PoolDir);
        Configs.Save(cfg);
        LibraryService svc;
        if (host.LocalLibrary is { } running)
        {
            await RestartAsync(running, cfg);
            svc = running;
        }
        else
        {
            svc = Command is null ? new LibraryService(host.Home, cfg) : new LibraryService(host.Home, cfg, Command);
            await svc.StartAsync();
        }
        if (svc.State is not (LibraryServiceState.Running or LibraryServiceState.Elsewhere))
            throw new InvalidOperationException("The library didn't start." + (svc.Failure is { Length: > 0 } f ? $" {f}." : "") + " Its log is in the logs folder.");
        host.UseLocalLibrary(svc);
        host.Save(s => s.Role = role);
        var cc = host.Client();
        cc.ServerUrl = $"http://127.0.0.1:{cfg.WebPort}";
        cc.PoolKey = cfg.PoolPassword;
        cc.PoolName = cfg.PoolName;
        if (cc.DisplayName.Length == 0) cc.DisplayName = displayName;
        host.SaveClient(cc);
        return $"{name} is ready on this {(OperatingSystem.IsMacOS() ? "Mac" : "PC")}.";
    }
}
