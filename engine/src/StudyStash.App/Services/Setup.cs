using System.Globalization;
using StudyStash.App.ViewModels;
using StudyStash.Audio;
using StudyStash.Core;

namespace StudyStash.App.Services;

/// <summary>What setup's buttons do, and what it shows as things change (the microphone's permission, the download).</summary>
public static class Setup
{
    /// <summary>"mac-mini" → "http://mac-mini:8787"; an https address or one with a port is kept as it is.</summary>
    public static string NormalizeAddress(string text)
    {
        string t = text.Trim().TrimEnd('/');
        if (t.Length == 0) return t;
        if (!t.Contains("://", StringComparison.Ordinal)) t = "http://" + t;
        if (Uri.TryCreate(t, UriKind.Absolute, out var u) && u.IsDefaultPort && u.Scheme == Uri.UriSchemeHttp && !t[(t.IndexOf("://", StringComparison.Ordinal) + 3)..].Contains(':'))
            t = $"{u.Scheme}://{u.Host}:8787{u.PathAndQuery.TrimEnd('/')}";
        return t;
    }

    static string Person() =>
        Environment.UserName is { Length: > 0 } u ? char.ToUpper(u[0], CultureInfo.InvariantCulture) + u[1..] : "Me";

    /// <summary>The role an installer was made for ("library" or "laptop" in its Info.plist or study-stash.ini), or
    /// null for a build that doesn't say.</summary>
    public static AppRole? Preset(string? preset) => preset switch
    {
        "library" => AppRole.Library,
        "laptop" => AppRole.Laptop,
        _ => null,
    };

    /// <summary>The welcome's choice to start on. A run that stopped part-way picks up where it was: a library already
    /// made here keeps its flow, and a laptop already connected to a library on another computer stays a laptop.
    /// Otherwise the installer suggests one (<see cref="SetupModel.Suggested"/>).</summary>
    public static AppRole StartingRole(AppRole? installer, AppRole saved, string serverUrl)
    {
        if (saved != AppRole.Laptop) return saved;
        if (serverUrl.Length > 0 && !IsThisComputer(serverUrl)) return AppRole.Laptop;
        return SetupModel.Suggested(installer);
    }

    /// <summary>An address on this computer itself (the library a one-computer setup made).</summary>
    public static bool IsThisComputer(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) && (u.IsLoopback || u.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase));

    /// <summary>Setup for this computer, starting on <paramref name="role"/>'s choice (or, when not given, the one
    /// <see cref="StartingRole"/> works out), with what's already known filled in. <paramref name="tailscale"/> and
    /// <paramref name="hostName"/> say where a laptop can reach a library made here, and <paramref name="here"/> makes it
    /// (tests give their own).</summary>
    public static SetupModel Make(AppHost host, AppRole? role = null, Func<TailscaleInfo>? tailscale = null, Func<string>? hostName = null,
        LibraryHere? here = null)
    {
        var cc = host.Client();
        var m = SetupModel.For(Skin.Current, role ?? StartingRole(Preset(Apps.RolePreset()), host.Settings.Role, cc.ServerUrl));
        m.Address = cc.ServerUrl;
        m.LibraryName = $"{Person()}'s library";
        here ??= LibraryHere.ThisComputer();
        m.NotesFolder = here.Folder ?? LibraryHere.DefaultFolder;
        // Setup run again on the library: it keeps its name, password and notes folder, and it starts at login as it
        // should (Finish writes the login item afresh, pointing at this copy of the app).
        if (LibraryHere.Existing(host.Home) is { } had)
        {
            m.ExistingLibrary = true;
            if (had.PoolName.Length > 0) m.LibraryName = had.PoolName;
            m.Password = had.PoolPassword;
            m.NotesFolder = had.PoolDir;
            m.StartAtLogin = true;
        }
        if (host.LoginItems.StartsAtLogin(host.Home)) m.StartAtLogin = true;
        m.ModelName = host.Model.Name;
        m.ModelSize = About(host.Model.Bytes);
        if (cc.ServerUrl.Length > 0 && host.Library == LibraryState.Connected && (m.IsLaptop || host.LocalLibrary is not null))
        {
            m.LibraryOk = true;
            m.LibraryResult = m.IsLaptop ? $"Connected to {cc.PoolName}." : $"{cc.PoolName} is ready on this {m.DeviceWord}.";
        }

        m.OnAllowMic = async () =>
        {
            // macOS shows its own prompt once; the step shows the answer as soon as there is one.
            await host.AskMicAsync();
            Refresh(m, host);
        };
        m.OnMicSettings = () => Dialogs.OpenUrl(host.MicSettingsUrl);
        m.OnTaskbarSettings = () => Dialogs.OpenUrl("ms-settings:taskbar");
        m.OnRetryModel = () => _ = host.DownloadModelAsync();
        m.OnConnect = () => ConnectAsync(m, host, here);
        m.OnFind = () => FindAsync(m, host);
        m.OnAddClass = () => AddClassAsync(m, host);
        // Leaving Classes adds the Canvas courses that are ticked (and links them); a time that can't be read stays.
        m.LeaveAsync = step => step == SetupStep.Classes ? AddCoursesAsync(m, host) : Task.FromResult(true);
        m.CanLeave = step =>
        {
            if ((step is SetupStep.Password or SetupStep.Library || step == SetupStep.Welcome && m.IsOneComputer) && !m.LibraryOk)
            {
                m.LibraryResult = m.IsLaptop ? "Connect to your library first." : "Create the library first.";
                return false;
            }
            return true;
        };
        m.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(SetupModel.Step)) return;
            if (m.Step == SetupStep.Model) _ = host.DownloadModelAsync();
            if (m.Step == SetupStep.Done && m.IsLibrary) _ = FillAddressesAsync(m, host, tailscale ?? (() => HostInfo.Tailscale()), hostName ?? LanName);
        };
        foreach (var c in host.Timetable.Classes)
            m.Classes.Add(new SetupClass { Name = c.Name, When = string.Join(", ", c.Times.Select(t => t.Describe())), Dot = Skin.ClassDot(Math.Max(0, host.ColorOf(c.Name))) });
        Refresh(m, host);
        return m;
    }

    public static void Refresh(SetupModel m, AppHost host)
    {
        var mic = host.MicAccess();
        m.MicAllowed = mic == MicAccess.Allowed;
        m.MicDenied = mic is MicAccess.Denied or MicAccess.Restricted;
        m.ModelReady = host.ModelReady;
        m.ModelProblem = host.DownloadProblem;
        if (host.ModelReady)
        {
            m.ModelProgress = 1;
            m.ModelDone = $"{host.Model.Name} is ready";
            m.ModelLeft = "";
        }
        else if (host.Downloading is { } d)
        {
            m.ModelProgress = d.Fraction;
            m.ModelDone = d.Amount;
            m.ModelLeft = d.Left() ?? "";
        }
    }

    /// <summary>A model's size for "The model is about 3 GB": whole gigabytes for the big ones, as the design says it.</summary>
    public static string About(long bytes) => bytes >= 2_500_000_000 ? $"{Math.Round(bytes / 1e9)} GB" : WhisperModel.SizeOf(bytes);

    /// <summary>Saves what setup decided (the role, that it's done) and, only if the box was ticked, starts Study
    /// Stash at login: the login item is written afresh, so one left pointing at an older copy of the app works again.
    /// Never touches login items otherwise: that would change this computer unasked.</summary>
    public static void Finish(SetupModel m, AppHost host)
    {
        host.Save(s =>
        {
            s.Role = m.Role;
            s.SetupDone = true;
        });
        // Only the library's flows ask; a laptop's setup never changes its login items.
        if (m.StartAtLogin && m.Steps.Any(s => s.Step == SetupStep.StartAtLogin))
        {
            try
            {
                host.LoginItems.StartAtLogin(true, host.Home);
            }
            catch (Exception e) when (e is InvalidOperationException or IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                host.Log($"[app] start at login: {e.Message}");
            }
        }
    }

    /// <summary>This computer's name on the home network: "mac-mini.local" on a Mac (Bonjour answers it), its plain
    /// name on Windows.</summary>
    static string LanName()
    {
        string name = Machine.HostName();
        return OperatingSystem.IsMacOS() && !name.Contains('.', StringComparison.Ordinal) ? name + ".local" : name;
    }

    /// <summary>The library's last page: its address at home straight away, then its Tailscale one if Tailscale is on
    /// (asking Tailscale takes a moment, so it comes second).</summary>
    public static async Task FillAddressesAsync(SetupModel m, AppHost host, Func<TailscaleInfo> tailscale, Func<string> hostName)
    {
        int port = Configs.Load(host.Home).WebPort;
        m.Addresses.Clear();
        m.Addresses.Add(new SetupAddress("At home", $"http://{hostName()}:{port}"));
        var ts = await Task.Run(tailscale);
        if (ts.Running && ts.Dns.Length > 0 && m.Addresses.All(a => a.Where != "With Tailscale"))
            m.Addresses.Add(new SetupAddress("With Tailscale", $"http://{ts.Dns}:{port}"));
    }

    /// <summary>Every tick while setup's window is open: the microphone is open exactly while its step shows, it's
    /// allowed, and nothing is recording; copies its levels and whether it's heard anything into the model.</summary>
    public static void TickMic(SetupModel m, AppHost host, MicCheck mic)
    {
        if (m.OnMicrophone && m.MicAllowed && host.Recorder.Current is null) mic.Open(host.OpenMic);
        else mic.Close();
        if (mic.Heard) m.MicHeard = true;
        m.MicLevels = mic.Levels();
    }

    static async Task ConnectAsync(SetupModel m, AppHost host, LibraryHere here)
    {
        m.Connecting = true;
        m.LibraryResult = null;
        try
        {
            if (m.IsOneComputer)
            {
                // Just this computer: the library is made quietly, reachable only from here, with a password of its
                // own (a laptop added later gets one the student picks). One already here keeps who can reach it.
                string done = await here.CreateAsync(host, m.LibraryName, null, Person(), AppRole.Both, localOnly: !m.ExistingLibrary);
                m.LibraryOk = true;
                m.LibraryResult = done;
            }
            else if (m.IsLibrary)
            {
                string done = await here.CreateAsync(host, m.LibraryName, m.Password, Person(), m.Role);
                m.LibraryOk = true;
                m.LibraryResult = done;
            }
            else
            {
                string url = NormalizeAddress(m.Address);
                if (url.Length == 0) throw new InvalidOperationException("Type your library's address, like http://mac-mini:8787.");
                var health = await LibraryApi.CheckServerAsync(url, m.Password.Trim());
                var cc = host.Client();
                cc.ServerUrl = url;
                cc.PoolKey = m.Password.Trim();
                cc.PoolName = health["pool_name"]?.GetValue<string>() ?? "";
                if (cc.DisplayName.Length == 0) cc.DisplayName = Person();
                host.SaveClient(cc);
                m.Address = url;
                m.LibraryOk = true;
                m.LibraryResult = $"Connected to {cc.PoolName}.";
            }
            await host.CheckLibraryAsync();
        }
        catch (Exception e) when (e is InvalidOperationException or ArgumentException or HttpRequestException or System.Text.Json.JsonException or IOException)
        {
            m.LibraryOk = false;
            m.LibraryResult = e.Message switch
            {
                "wrong password" => "That password isn't right.",
                var s when s.StartsWith("could not reach", StringComparison.Ordinal) => "Can't reach that address. Is the library's computer on, and is Tailscale connected?",
                var s => char.ToUpper(s[0], CultureInfo.InvariantCulture) + s[1..],
            };
        }
        finally
        {
            m.Connecting = false;
        }
    }

    /// <summary>"Find it": looks for a library on this computer or your Tailscale network, with no password, and
    /// fills in the address (and, for a Tailscale one, says its name so you know whose password to type).</summary>
    static async Task FindAsync(SetupModel m, AppHost host)
    {
        m.Finding = true;
        m.LibraryResult = null;
        try
        {
            var cfg = Configs.Load(host.Home);
            var extra = new List<int>();
            if (File.Exists(cfg.ConfigPath)) extra.Add(cfg.WebPort);
            // The self-test's own library isn't on the usual port (never 8787, so a real one is never mistaken for it).
            if (Environment.GetEnvironmentVariable("STUDYSTASH_SELFTEST_LIBRARY_PORT") is { Length: > 0 } sp && int.TryParse(sp, out int p)) extra.Add(p);
            var found = await new LibraryFinder { ExtraPorts = extra }.FindAsync();
            if (found is null)
            {
                m.LibraryResult = "No library answered. Type its address, like http://mac-mini:8787.";
            }
            else
            {
                m.Address = found.Url;
                m.LibraryResult = found.Local ? $"Found a library on this {m.DeviceWord}."
                    : found.Name.Length > 0 ? $"Found {found.Name} on your Tailscale network. Type its password."
                    : "Found a library on your Tailscale network. Type its password.";
            }
        }
        finally
        {
            m.Finding = false;
        }
    }

    /// <summary>
    /// Classes' Continue with Canvas courses found: every ticked course becomes a class (in the library and the
    /// timetable, with its times when typed), each is linked to its course, and Canvas is asked to sync in the
    /// background. Every "when" is read first, so one that can't be read stops the step (false, saying how to write it)
    /// before anything changes. <paramref name="canvas"/> is the library's Canvas API (tests give their own).
    /// </summary>
    public static async Task<bool> AddCoursesAsync(SetupModel m, AppHost host, CanvasClient? canvas = null)
    {
        var ticked = m.Courses.Where(c => c.Ticked && c.Name.Trim().Length > 0).ToList();
        if (ticked.Count == 0) return true;
        var times = new Dictionary<SetupCourse, List<ClassTime>>();
        foreach (var c in ticked)
        {
            if (c.When.Trim().Length == 0)
            {
                times[c] = [];
            }
            else if (ClassTime.ParseMany(c.When) is { } parsed)
            {
                times[c] = parsed;
            }
            else
            {
                m.ClassProblem = $"Write when {c.Name} meets like “Tue Thu 10:00–11:15” or “MWF 9–9:50”.";
                return false;
            }
        }
        m.ClassProblem = null;
        m.AddingCourses = true;
        try
        {
            var t = host.Timetable;
            var lib = host.Remote();
            foreach (var c in ticked)
            {
                string name = c.Name.Trim();
                if (lib is not null && !t.Classes.Any(x => x.Name == name))
                {
                    try
                    {
                        await lib.AddClassAsync(name);
                    }
                    catch (Exception e) when (e is HttpRequestException or TaskCanceledException or LibraryRefusedException)
                    {
                        // An older library can't take classes over its API: the timetable still has it.
                    }
                }
                t.Classes.RemoveAll(x => x.Name == name);
                t.Classes.Add(new TimetableClass(name, times[c]));
            }
            host.SaveTimetable(t);
            var cc = host.Client();
            canvas ??= cc.ServerUrl.Length > 0 ? new CanvasClient(cc.ServerUrl, cc.PoolKey) : null;
            if (canvas is not null)
            {
                try
                {
                    await canvas.SaveAsync(courses: ticked.ToDictionary(c => c.Name.Trim(), c => double.Parse(c.Id, CultureInfo.InvariantCulture)));
                    _ = SyncSoonAsync(canvas, host);
                }
                catch (Exception e) when (e is CanvasLibraryException or HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or FormatException)
                {
                    // The classes are in; Settings → Canvas can link them later.
                    host.Log($"[canvas] setup couldn't link the courses: {e.Message}");
                }
            }
            await host.CheckLibraryAsync();
            return true;
        }
        finally
        {
            m.AddingCourses = false;
        }
    }

    /// <summary>The first sync, asked for without waiting: it takes minutes, and setup moves on.</summary>
    static async Task SyncSoonAsync(CanvasClient canvas, AppHost host)
    {
        try
        {
            await canvas.SaveAsync(sync: true);
        }
        catch (Exception e) when (e is CanvasLibraryException or HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            host.Log($"[canvas] setup couldn't start the first sync: {e.Message}");
        }
    }

    static async Task AddClassAsync(SetupModel m, AppHost host)
    {
        string name = m.NewClass.Trim();
        if (name.Length == 0) return;
        List<ClassTime> times = [];
        if (m.NewWhen.Trim().Length > 0)
        {
            if (ClassTime.ParseMany(m.NewWhen) is not { } parsed)
            {
                m.ClassProblem = "Write the days and times like “Tue Thu 10:00–11:15” or “MWF 9–9:50”.";
                return;
            }
            times = parsed;
        }
        m.ClassProblem = null;
        bool existing = host.Timetable.Classes.Any(c => c.Name == name);
        if (!existing && host.Remote() is { } lib)
        {
            try
            {
                await lib.AddClassAsync(name);
                await host.CheckLibraryAsync();
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or LibraryRefusedException)
            {
                // An older library can't take classes over its API: the timetable still has it.
            }
        }
        var t = host.Timetable;
        t.Classes.RemoveAll(c => c.Name == name);
        t.Classes.Add(new TimetableClass(name, times));
        host.SaveTimetable(t);
        string when = string.Join(", ", times.Select(x => x.Describe()));
        var dot = Skin.ClassDot(Math.Max(0, host.ColorOf(name)));
        if (m.Classes.FirstOrDefault(c => c.Name == name) is { } row) row.When = when;
        else m.Classes.Add(new SetupClass { Name = name, When = when, Dot = dot });
        m.NewClass = "";
        m.NewWhen = "";
    }
}
