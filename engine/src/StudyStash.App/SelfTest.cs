using System.Security.Cryptography;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StudyStash.App.Controls.Rich;
using StudyStash.App.Platform;
using StudyStash.App.Services;
using StudyStash.App.ViewModels;
using StudyStash.App.Windows;
using StudyStash.Audio;
using StudyStash.Core;

namespace StudyStash.App;

/// <summary>
/// STUDYSTASH_SELFTEST=&lt;folder&gt;: the app tries itself, for real, in its own windows. <see cref="Prepare"/> runs
/// before Avalonia starts: it refuses a bad setup (a missing or unsafe --home, a bad microphone file, the wrong
/// model), then starts a library and an AI engine of its own (see <see cref="SelfTestEngine"/>) so nothing here ever
/// reaches the network, Hugging Face, a real Ollama, Canvas or Claude. <see cref="Run"/> then walks the real setup
/// screens, records a lecture with the pretend microphone, follows it through Whisper to the library and back as
/// notes, proves the queue and the problem states, pictures every surface, and quits — non-zero on any failure.
/// With STUDYSTASH_SELFTEST_FLOW=one-computer it proves Just this computer instead: setup makes the app's own library
/// (no second one is started), and the lecture is recorded, written down and given its notes all on this computer.
/// </summary>
public static partial class SelfTest
{
    public static string? Dir => Environment.GetEnvironmentVariable("STUDYSTASH_SELFTEST") is { Length: > 0 } d ? d : null;

    /// <summary>Setup chooses Just this computer: the app runs its own library, and nothing else is a library.</summary>
    internal static bool OneComputer => Environment.GetEnvironmentVariable("STUDYSTASH_SELFTEST_FLOW") == "one-computer";

    /// <summary>STUDYSTASH_SELFTEST_BACKGROUND=1 (CI's throwaway computers only): Just this computer's run ends by
    /// turning on "Answer your phone when Syft Notes is closed" for real, with a stand-in for Google Drive, so the
    /// script that started it can check, once the app has quit, that the library still answers the phone. Never set
    /// on a real computer: it registers a LaunchAgent, a Run key value or a systemd unit.</summary>
    internal static bool BackgroundOn => Dir is not null && OneComputer && Environment.GetEnvironmentVariable("STUDYSTASH_SELFTEST_BACKGROUND") == "1";

    /// <summary>The self-test's own library, once <see cref="Prepare"/> has started it.</summary>
    internal static LibraryService? Library { get; private set; }
    internal static SelfTestEngine? Engine { get; private set; }
    internal static string? LibraryPassword { get; private set; }

    static readonly List<string> said = [];

    static void Say(string line)
    {
        said.Add(line);
        Program.Log("[selftest] " + line);
    }

    // --- before Avalonia starts ------------------------------------------------------------------------------------

    /// <summary>Checks the self-test can run at all, and starts its own library and AI engine. Returns an exit code
    /// for <see cref="Program.Main"/> to return at once (refusing to start the app), or null to carry on.</summary>
    public static int? Prepare(string home)
    {
        if (Dir is null) return null;
        Directory.CreateDirectory(Dir);
        string? why = CheckHome(home);
        if (why is null && (Environment.GetEnvironmentVariable("STUDYSTASH_MIC_FILE") is not { Length: > 0 } mic || !IsReadableWav(mic)))
            why = "STUDYSTASH_MIC_FILE must be set to a readable WAV file";
        if (why is null && (Environment.GetEnvironmentVariable("STUDYSTASH_WHISPER_MODEL") is not { Length: > 0 } model || !IsTinyModel(model)))
            why = "STUDYSTASH_WHISPER_MODEL must be a path to the tiny model, matching its SHA-256";
        if (why is not null)
        {
            File.WriteAllLines(Path.Combine(Dir, "selftest.txt"), [$"FAILED prepare: {why}", "failed"]);
            return 2;
        }
        string modelPath = Environment.GetEnvironmentVariable("STUDYSTASH_WHISPER_MODEL")!;
        try
        {
            Directory.CreateDirectory(home);
            StartEnginesAsync(home, modelPath).GetAwaiter().GetResult();
        }
        catch (Exception e)
        {
            File.WriteAllLines(Path.Combine(Dir, "selftest.txt"), [$"FAILED prepare: couldn't start the self-test's own library: {e.Message}", "failed"]);
            return 2;
        }
        // The file we validated becomes the mirror's copy; from here on the app downloads "tiny" like a real model,
        // from our own engine instead of Hugging Face.
        Environment.SetEnvironmentVariable("STUDYSTASH_WHISPER_MODEL", "tiny");
        Environment.SetEnvironmentVariable("STUDYSTASH_MODEL_URL", Engine!.Url.TrimEnd('/') + "/models");
        if (Environment.GetEnvironmentVariable("STUDYSTASH_MIC_SPEED") is not { Length: > 0 }) Environment.SetEnvironmentVariable("STUDYSTASH_MIC_SPEED", "4");
        return null;
    }

    static string? CheckHome(string home)
    {
        if (string.IsNullOrEmpty(home) || Py.NormPath(home) == Py.NormPath(Configs.DefaultHome)) return "--home must be given, and can't be the default home";
        if (Directory.Exists(home) && Directory.EnumerateFileSystemEntries(home).Any()) return "--home must be a new, empty folder";
        return null;
    }

    static bool IsReadableWav(string path)
    {
        try
        {
            return File.Exists(path) && Sound.WavSeconds(path) > 0;
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    static bool IsTinyModel(string path) =>
        File.Exists(path) && new FileInfo(path).Length == WhisperModels.Tiny.Bytes
        && Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))) == WhisperModels.Tiny.Sha256;

    static async Task StartEnginesAsync(string home, string modelPath)
    {
        Engine = await SelfTestEngine.StartAsync(modelPath);
        if (OneComputer)
        {
            // Setup makes the library itself: its notes in this run's own folder, on a port far from a real 8787.
            Environment.SetEnvironmentVariable("STUDYSTASH_NOTES_DIR", Path.Combine(home, "Study Stash"));
            Environment.SetEnvironmentVariable("STUDYSTASH_LIBRARY_PORT", SelfTestPorts.FreePair().ToString());
            // The background run's stand-in for the Google Drive folder, which the library it registers is given.
            if (BackgroundOn) Environment.SetEnvironmentVariable("STUDYSTASH_DRIVE_FOLDER", DriveFolder(home));
            return;
        }
        string libHome = Path.Combine(home, "selftest-library");
        Directory.CreateDirectory(libHome);
        int port = SelfTestPorts.FreePair();
        LibraryPassword = StudyStash.Library.Http.TokenUrlSafe(12);
        var cfg = new Config(libHome, Path.Combine(libHome, "pool"))
        {
            PoolName = "Self-test library",
            PoolPassword = LibraryPassword,
            AdminPassword = StudyStash.Library.Http.TokenUrlSafe(12),
            WebHost = "127.0.0.1",
            WebPort = port,
            AutoUpdate = false,
            OllamaHost = Engine.Url,
            OllamaModel = "self-test-notes",
            Classes = [new ClassDef("CS 101"), new ClassDef("BIO 110")],
        };
        Directory.CreateDirectory(cfg.PoolDir);
        Configs.Save(cfg);
        new StudyStash.Core.Ai.AiSettings { Provider = "ollama" }.Save(libHome); // a fresh library has no AI chosen: this one picked the stand-in engine
        Library = new LibraryService(libHome, cfg);
        await Library.StartAsync();
        if (Library.State is not (LibraryServiceState.Running or LibraryServiceState.Elsewhere))
            throw new InvalidOperationException($"{Library.Failure ?? "the library didn't start"} {Library.LastLines}".TrimEnd());
        Environment.SetEnvironmentVariable("STUDYSTASH_SELFTEST_LIBRARY_PORT", port.ToString());
    }

    /// <summary>Both stopped, wherever the run got to: a second run needs a clean slate, and nothing is left holding
    /// the ports.</summary>
    static async Task StopEnginesAsync()
    {
        if (Library is not null) await Library.StopAsync();
        if (Engine is not null) await Engine.DisposeAsync();
    }

    // --- pictures and waiting ----------------------------------------------------------------------------------------

    static void Shot(Window? w, string name)
    {
        if (w is null || !w.IsVisible)
        {
            Say($"{name}: not showing");
            return;
        }
        var size = new PixelSize(Math.Max(1, (int)Math.Ceiling(w.Bounds.Width)), Math.Max(1, (int)Math.Ceiling(w.Bounds.Height)));
        // Twice the size on a Mac; on Windows at the window's own size, where a doubled bitmap drew the window twice
        // as large again and kept only its top-left quarter.
        int times = OperatingSystem.IsWindows() ? 1 : 2;
        using var bmp = new RenderTargetBitmap(new PixelSize(size.Width * times, size.Height * times), new Vector(96 * times, 96 * times));
        bmp.Render(w);
        using var f = File.Create(Path.Combine(Dir!, name + ".png"));
        bmp.Save(f, PngBitmapEncoderOptions.Default);
        Say($"{name}: {w.Bounds.Width:0}×{w.Bounds.Height:0} at {w.Position} (scale {w.RenderScaling:0.0#})");
        ScreenShot(w, name);
    }

    /// <summary>Waits until what's on screen has stopped changing, instead of for a guessed number of seconds: it
    /// follows the window's own frames (so a slow computer simply takes more of them) and returns once every open
    /// window has kept the same size and place for 15 frames in a row (about a quarter of a second at 60 a second),
    /// or after <paramref name="seconds"/> at most, then goes on with whatever is there.</summary>
    static async Task Settle(double seconds = 10)
    {
        var until = DateTime.UtcNow.AddSeconds(seconds);
        string last = "";
        int still = 0;
        while (still < 15 && DateTime.UtcNow < until)
        {
            await NextFrame();
            var now = string.Join(";", OpenWindows().Where(w => w.IsVisible).Select(w => $"{w.GetHashCode()}:{w.Bounds}:{w.Position}"));
            still = now == last ? still + 1 : 0;
            last = now;
        }
    }

    static IEnumerable<Window> OpenWindows() =>
        (Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.Windows.ToList() ?? new List<Window>();

    /// <summary>The next frame a visible window draws (the next pass of the UI thread when none is showing).</summary>
    static async Task NextFrame()
    {
        var frame = new TaskCompletionSource();
        if (OpenWindows().FirstOrDefault(w => w.IsVisible) is { } window) window.RequestAnimationFrame(_ => frame.TrySetResult());
        else Dispatcher.UIThread.Post(() => frame.TrySetResult(), DispatcherPriority.Render);
        // A window hidden between asking and drawing never answers; that must not hang the self-test.
        using var guard = new CancellationTokenSource();
        if (await Task.WhenAny(frame.Task, Task.Delay(TimeSpan.FromSeconds(2), guard.Token)) == frame.Task) guard.Cancel();
    }

    /// <summary>Reads the full-screen stand-in's standard output until it says its window is full screen.</summary>
    static async Task FullScreenReachedAsync(System.Diagnostics.Process helper)
    {
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            while (await helper.StandardOutput.ReadLineAsync(limit.Token) is { } line)
                if (line.Trim() == "fullscreen") return;
            Say("FAILED recorder over full screen: the full-screen stand-in quit before it was full screen");
        }
        catch (OperationCanceledException) { Say("FAILED recorder over full screen: the stand-in wasn't full screen within 30 s"); }
    }

    static async Task<bool> Until(Func<bool> done, double seconds)
    {
        var until = DateTime.UtcNow.AddSeconds(seconds);
        while (!done() && DateTime.UtcNow < until) await Task.Delay(250);
        return done();
    }

    /// <summary>Opens the dropdown the way the icon would, takes its picture under <paramref name="name"/>, and
    /// hides it again.</summary>
    static async Task PanelShot(string name)
    {
        Shell.Windows.OpenPanelViaIcon();
        await Settle();
        Shot(Shell.Windows.Panel, name);
        Shell.Windows.Panel?.Hide();
    }

    // --- running it ----------------------------------------------------------------------------------------------

    public static void Run() => Dispatcher.UIThread.Post(async () =>
    {
        Directory.CreateDirectory(Dir!);
        int code = 0;
        try
        {
            var script = Script();
            if (await Task.WhenAny(script, Task.Delay(TimeSpan.FromMinutes(12))) != script)
                throw new TimeoutException("the self-test ran past 12 minutes");
            await script;
            Say("ok");
        }
        catch (Exception e)
        {
            Say($"failed: {e}");
            code = 1;
        }
        await File.WriteAllLinesAsync(Path.Combine(Dir!, "selftest.txt"), said);
        await StopEnginesAsync();
        Shell.Quit(code);
    });

    static async Task Script()
    {
        var host = Shell.Host;
        Say($"skin {Skin.Current}, home {host.Home}{(OneComputer ? ", just this computer" : "")}");
        if (OneComputer) await RunOneComputerSetupAsync(host);
        else await RunSetupAsync(host);
        await RunRecordingAsync(host);
        await RunProblemsAsync(host);
        await RunNotificationsAsync();
        await RunSettingsAsync();
        await RunLooksAsync();
        if (BackgroundOn) await RunBackgroundAsync(host);
    }

    // --- setup -------------------------------------------------------------------------------------------------------

    /// <summary>Setup's first page says how Syft Notes works: seen, then on to the computer's choices.</summary>
    static async Task PastIntroAsync(SetupModel m)
    {
        if (!m.OnIntro) return;
        await Settle();
        Shot(Shell.Windows.Setup, "setup-intro");
        await m.NextCommand.ExecuteAsync(null);
        if (!m.OnWelcome) throw new InvalidOperationException($"expected the computer's choices after How it works, got {m.Step}");
    }

    static async Task RunSetupAsync(AppHost host)
    {
        bool opened = await Until(() => Shell.Windows.Setup is not null, 10);
        Say(opened ? "setup opened" : "setup didn't open");
        var first = Shell.Windows.SetupModel ?? throw new InvalidOperationException("no setup window");
        // A first run opens the one-button welcome. A laptop connecting to a library elsewhere (the two-computer setup,
        // frozen and hidden from students, who reach it from Settings → Connection) is setup by hand: the self-test
        // still proves it works.
        if (!first.OnStart) throw new InvalidOperationException($"a first run should open on the one-button welcome, got {first.Step}");
        await Settle();
        Shot(Shell.Windows.Setup, "setup-welcome");
        Shell.Windows.SetupByHand();
        bool byHand = await Until(() => Shell.Windows.SetupModel is { OneButton: false }, 10);
        Say(byHand ? "set up by hand, for a laptop" : "setup by hand didn't open");
        var m = Shell.Windows.SetupModel ?? throw new InvalidOperationException("no setup window");
        await PastIntroAsync(m);

        // Where lectures are kept: three choices, starting on just this computer; the self-test is a laptop with its own
        // library.
        Say($"welcome: {m.FlowName}, {m.Steps.Count} steps");
        await Settle(); // the window lays out its steps first
        Shot(Shell.Windows.Setup, "setup-computers");
        m.ChooseLaptopCommand.Execute(null);
        Say($"chose the laptop: {m.FlowName}, {m.Steps.Count} steps");
        m.NextCommand.Execute(null);

        // Library: find it (our own, on its own port, never 8787), a wrong password, then the right one.
        await m.FindCommand.ExecuteAsync(null);
        Say($"find: {m.LibraryResult}");
        Shot(Shell.Windows.Setup, "setup-library-found");

        m.ConnectPassword = "not-the-password";
        await m.ConnectCommand.ExecuteAsync(null);
        Say($"wrong password: {m.LibraryResult}");
        Shot(Shell.Windows.Setup, "setup-library-wrong-password");

        m.ConnectPassword = LibraryPassword ?? "";
        await m.ConnectCommand.ExecuteAsync(null);
        Say($"connect: {m.LibraryResult}");
        Shot(Shell.Windows.Setup, "setup-library");
        if (!m.LibraryOk) throw new InvalidOperationException("setup couldn't connect to the self-test's library");
        m.NextCommand.Execute(null);

        // Microphone: the pretend mic is already "allowed", so the level check should hear the looped fixture.
        bool heard = await Until(() => m.MicHeard, 10);
        Say(heard ? "microphone heard" : "microphone: nothing heard in 10 s");
        Shot(Shell.Windows.Setup, "setup-microphone");
        m.NextCommand.Execute(null);

        // What it's for: left as it is (school), so Canvas and Classes come next.
        if (m.OnUses)
        {
            await Settle();
            Shot(Shell.Windows.Setup, "setup-uses");
            await m.NextCommand.ExecuteAsync(null);
        }

        // The model has no step of its own: it started downloading on the microphone's step, from our own mirror (never
        // Hugging Face), and goes on while setup does. Waiting for it here keeps setup where it is (on Canvas).
        bool downloading = await Until(() => m.ModelProgress is > 0.05 and < 0.95 || m.ModelReady, 20);
        Say(downloading ? $"model downloading: {m.ModelDone} {m.ModelLeft}" : "model: no progress seen in 20 s");
        bool ready = await Until(() => m.ModelReady, 60);
        Say(ready ? "model ready" : $"model: not ready in 60 s ({m.ModelProblem})");
        if (!ready) throw new InvalidOperationException("the model never finished downloading");

        // Canvas is optional: the self-test skips it (there's no Chrome or Canvas here to connect).
        if (m.OnCanvas)
        {
            await Settle(); // the window grows to the step's size first
            Shot(Shell.Windows.Setup, "setup-canvas");
            m.SkipCommand.Execute(null);
        }

        // Classes: CS 101 and what it covers. Record picks no class, so the library sorts the lecture by what was said.
        m.NewClass = "CS 101";
        m.NewAbout = "Intro to computer science: recursion, the call stack, the midterm";
        await m.AddClassCommand.ExecuteAsync(null);
        Say(m.ClassProblem is null ? "class added: CS 101" : $"class problem: {m.ClassProblem}");
        Shot(Shell.Windows.Setup, "setup-classes");
        if (m.ClassProblem is not null) throw new InvalidOperationException(m.ClassProblem);
        m.NextCommand.Execute(null);

        if (m.OnTaskbar)
        {
            await Settle(); // the window lays out the step first
            Shot(Shell.Windows.Setup, "setup-taskbar");
            m.NextCommand.Execute(null);
        }

        // Done: how to record, then Open Syft Notes. The role stays Laptop; no login item is touched.
        if (m.OnDone)
        {
            await Settle();
            Shot(Shell.Windows.Setup, "setup-done");
        }
        if (Shell.Windows.Setup is not null && m.IsLast) m.NextCommand.Execute(null);
        bool closed = await Until(() => Shell.Windows.Setup is null, 10);
        Say(closed ? $"setup finished: role {host.Settings.Role}, setup done {host.Settings.SetupDone}" : "setup: the window never closed");
        await Settle();
        Shot(Shell.Windows.Main, "library-empty");
    }

    /// <summary>Just this computer, as every first run starts now: one page, one button. Get started makes the library
    /// here (listening to this computer alone), asks for the microphone, starts the model and, with no AI here, leaves
    /// the notes for the first lecture's page; the window closes on Home with "Ready when class starts". Open when you log
    /// in starts ticked and is unticked first (the self-test never adds a login item).</summary>
    static async Task RunOneComputerSetupAsync(AppHost host)
    {
        bool opened = await Until(() => Shell.Windows.Setup is not null, 10);
        Say(opened ? "setup opened" : "setup didn't open");
        var m = Shell.Windows.SetupModel ?? throw new InvalidOperationException("no setup window");
        if (!m.OnStart || m.Steps.Count != 1) throw new InvalidOperationException($"a first run should open on the one-button welcome, got {m.Step} in {m.Steps.Count} step(s)");
        Say($"welcome: \"{SetupModel.StartTitle}\" · {m.ContinueLabel} · open when you log in {(m.StartAtLogin ? "ticked" : "unticked")}");
        if (!m.StartAtLogin) throw new InvalidOperationException("Open Syft Notes when you log in should start ticked");
        await Settle();
        Shot(Shell.Windows.Setup, "setup-welcome");
        m.StartAtLogin = false;
        var started = m.NextCommand.ExecuteAsync(null);
        if (await Until(() => m.Starting || started.IsCompleted, 5) && m.Starting) Shot(Shell.Windows.Setup, "setup-getting-ready");
        await started;
        bool closed = await Until(() => Shell.Windows.Setup is null, 30);
        Say(closed ? $"get started: role {host.Settings.Role}, setup done {host.Settings.SetupDone}, home card {(host.Settings.WelcomeCard ? "on" : "off")}"
            : $"get started: the window never closed ({m.LibraryResult})");
        if (!closed || !host.Settings.SetupDone || host.Settings.Role != AppRole.Both) throw new InvalidOperationException("Get started didn't set up just this computer");
        if (!m.LibraryOk || host.LocalLibrary is not { } lib) throw new InvalidOperationException("Get started didn't make its library");
        if (host.LoginItems.StartsAtLogin(host.Home)) throw new InvalidOperationException("unticked, Get started still added a login item");
        var cfg = Configs.Load(host.Home);
        Say($"it listens on {cfg.WebHost}:{cfg.WebPort} ({(LibraryHere.OnlyHere(cfg) ? "this computer only" : "the network")}), notes in {cfg.PoolDir}");
        if (!LibraryHere.OnlyHere(cfg)) throw new InvalidOperationException("Just this computer's library listens to the network");
        // And it really does listen only on this computer: nothing a firewall (Windows') would ask about.
        if (ListeningOn(cfg.WebPort) is { } where)
        {
            Say($"port {cfg.WebPort} is open on {(where.Count == 0 ? "nothing" : string.Join(", ", where))}");
            if (where.Count == 0 || where.Any(a => !System.Net.IPAddress.IsLoopback(a)))
                throw new InvalidOperationException("Just this computer's library isn't listening on this computer alone");
        }
        // No AI here (the self-test finds none): the library writes no notes until the first lecture's page asks which.
        Say($"notes after Get started: {(cfg.SummaryEnabled ? "on" : "waiting for the first lecture's page to ask which AI")}");
        if (cfg.SummaryEnabled) throw new InvalidOperationException("with no AI here, Get started should leave the notes to the first lecture's page");
        // The self-test's own engine stands in for the AI a student would pick there (never a real Ollama), with a class
        // to sort into: the library starts again to use them.
        cfg.OllamaHost = Engine!.Url;
        cfg.OllamaModel = "self-test-notes";
        cfg.AutoUpdate = false;
        cfg.SummaryEnabled = true;
        cfg.OllamaEnabled = true;
        cfg.Classes = [new ClassDef("CS 101")];
        Configs.Save(cfg);
        new StudyStash.Core.Ai.AiSettings { Provider = "ollama" }.Save(cfg.Home); // picking the stand-in engine, as the lecture page's card would
        await lib.StopAsync();
        await lib.StartAsync();
        Say($"library restarted on the self-test's notes engine: {lib.State}");

        bool ready = await Until(() => host.ModelReady, 60);
        Say(ready ? "model ready" : $"model: not ready in 60 s ({host.DownloadProblem})");
        if (!ready) throw new InvalidOperationException("the model never finished downloading");

        Shell.ShowLibrary();
        bool card = await Until(() => Shell.Windows.WelcomeCard is not null, 10);
        await Settle();
        Shot(Shell.Windows.Main, "home-welcome-card");
        Say(card ? $"home: \"{WelcomeCardModel.Title}\" · {Shell.Windows.WelcomeCard!.ModelLine}" : "home: no \"Ready when class starts\" card");
        if (!card) throw new InvalidOperationException("Home didn't show \"Ready when class starts\" after Get started");
    }

    /// <summary>The addresses something listens on at <paramref name="port"/>, as the system lists them; null where it
    /// can't say.</summary>
    static List<System.Net.IPAddress>? ListeningOn(int port)
    {
        try
        {
            return [.. System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners()
                .Where(e => e.Port == port).Select(e => e.Address)];
        }
        catch (Exception e) when (e is PlatformNotSupportedException or System.Net.NetworkInformation.NetworkInformationException)
        {
            return null;
        }
    }

    // --- recording -----------------------------------------------------------------------------------------------

    /// <summary>The pill dragged past a Mac's menu bar, or Windows' taskbar, stops right against it: the system doesn't
    /// push it back a shadow's width. Then it goes back to its corner.</summary>
    static async Task CheckRecorderDragAsync()
    {
        if (Shell.Windows.Recorder is not Floating w || w.Panel() is not { } before) return;
        var screen = Placement.Pick(w.ScreenList(), before.Center);
        var area = screen.WorkingArea;
        bool mac = OperatingSystem.IsMacOS();
        int room = (int)(Floating.ShadowRoom * screen.Scaling);
        // Well past the edge, as a quick flick of the pointer would take it.
        var pointer = new PixelPoint(area.X + area.Width / 2, mac ? area.Y + 2 : area.Bottom - 2);
        Shell.Windows.DragRecorder(new PixelPoint(pointer.X - before.Width / 2 - room, mac ? area.Y - 400 : area.Bottom + 400), pointer);
        await Settle();
        if (w.Panel() is { } after)
        {
            int want = mac ? area.Y : area.Bottom, got = mac ? after.Y : after.Bottom;
            string edge = mac ? "top" : "bottom";
            Say(Math.Abs(got - want) <= 1 ? $"recorder dragged {(mac ? "up to the menu bar" : "down to the taskbar")}: its {edge} at {got}, right against it"
                : $"FAILED recorder drag: its {edge} is at {got}, not {want} ({after} in {area})");
        }
        Shell.Windows.ShowRecorder(expanded: false);
        await Settle();
    }

    /// <summary>A Mac: the library open, then another app brought forward, as a student does once class starts. The pill
    /// must really be on screen then — AppKit's own word for it: shown, a real size, and visible on the Space showing —
    /// not just "shown" to Avalonia (0.16.7's pill opened 0×0 and nobody saw it). Coming back to the library hides it.</summary>
    static async Task CheckRecorderAwayAsync()
    {
        if (!OperatingSystem.IsMacOS()) return;
        bool libraryWasOpen = Shell.Windows.Main?.IsVisible == true;
        Shell.Windows.ShowLibrary();
        await Settle();
        Desktop.Deactivate();
        bool seen = await Until(() => Shell.Windows.Recorder is Floating { IsVisible: true } f && f.Native() is { Visible: true, Width: > 20, Height: > 20, OnScreen: true }, 5);
        var w = Shell.Windows.Recorder as Floating;
        var native = w?.Native();
        // The library stays where it was, behind the other app: never taken off the screen as the pill shows.
        bool libraryKept = Shell.Windows.Main is Window m && Floating.NativeOf(m) is { Visible: true };
        Say(libraryKept ? "recorder away: the library window is still open behind the other app"
            : "FAILED recorder away: the library window went off the screen as the pill showed");
        Say(seen ? $"recorder away: on screen over another app, {native?.Width:0}×{native?.Height:0} pt"
            : $"FAILED recorder away: with another app in front the pill isn't on screen (shown {w?.IsVisible}, AppKit {native?.ToString() ?? "no window"}, {w?.Bounds.Width:0}×{w?.Bounds.Height:0})");
        ScreenPicture("recorder-away");
        // The pointer on it: Stop in the S's place, and the card with the class, the time and the last lines.
        Shell.Windows.RecorderModel.Hovered = true;
        await Settle();
        ScreenPicture("recorder-hover");
        Say($"recorder hover: {Shell.Windows.RecorderModel.HoverTitle}, {(Shell.Windows.RecorderModel.ShowStop ? "Stop" : "no Stop")} in the S's place");
        Shell.Windows.RecorderModel.Hovered = false;
        await CheckRecorderOverFullScreenAsync();
        Shell.Windows.ShowLibrary();
        await Settle();
        bool active = Shell.Windows.Main?.IsActive == true;
        Say(Shell.Windows.Recorder?.IsVisible == true ? $"FAILED recorder away: the library is open again ({(active ? "in front" : "macOS didn't bring it forward")}), yet the pill still shows"
            : $"recorder away: hidden again with the library open ({(active ? "in front" : "macOS didn't bring it forward")})");
        if (!libraryWasOpen) Shell.Windows.Main?.Hide();
        Shell.Windows.ShowRecorder(expanded: false);
        await Settle();
    }

    /// <summary>A Mac, given STUDYSTASH_SELFTEST_FULLSCREEN (a program that puts a window of its own in full screen:
    /// macos/fullscreen-app.swift, built): with that app full screen in front, in its own Space, the pill is still on
    /// screen over it. Only where asked: it takes over the screen for a few seconds.</summary>
    static async Task CheckRecorderOverFullScreenAsync()
    {
        if (Environment.GetEnvironmentVariable("STUDYSTASH_SELFTEST_FULLSCREEN") is not { Length: > 0 } helper || !File.Exists(helper)) return;
        Say(Floating.PanelFits() ? "recorder over full screen: the pill is a panel" : "FAILED recorder over full screen: Avalonia's panel no longer matches its window, so the pill can't be one");
        using var other = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(helper) { RedirectStandardOutput = true, UseShellExecute = false })!;
        try
        {
            // The helper says so itself when its window is full screen, in its own Space (after the animation).
            await FullScreenReachedAsync(other);
            var w = Shell.Windows.Recorder as Floating;
            bool seen = await Until(() => w?.Native() is { Visible: true, Width: > 20, Height: > 20, OnScreen: true }, 3);
            ScreenPicture("recorder-fullscreen");
            Say(seen ? "recorder over full screen: on screen over another app's full-screen window"
                : $"FAILED recorder over full screen: not on screen over another app's full-screen window (AppKit {w?.Native()?.ToString() ?? "no window"})");
        }
        finally
        {
            other.Kill();
            using var gone = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            try { await other.WaitForExitAsync(gone.Token); } catch (OperationCanceledException) { Say("FAILED recorder over full screen: the full-screen stand-in didn't quit within 20 s"); }
            await Settle();
        }
    }

    /// <summary>
    /// The recorder opened shows what's being said within a couple of seconds (the live words: Cactus Whistle on ARM, a
    /// small Whisper on x64) where this computer is fast enough for them, or later where its first passes were near the
    /// limit. Where a pass takes more than <see cref="LiveCaptioner.MostRatio"/> of the sound it hears (Whistle on a CI
    /// Intel Mac: 1.5×), the documented fallback instead, for this lecture: nothing hears the live words any more, the
    /// recorder says the lines come in about half a minute, and the transcript's own lines do come.
    /// <para>How fast the words came is said, not judged: the computer running this is a shared CI machine as often as
    /// not, and an Apple silicon one that happened to be busy (a pass at 0.31× the sound, words a second late) failed
    /// a release that had nothing to do with it. What fails is what doesn't depend on its speed: the wrong engine on
    /// Apple silicon, no words and no verdict at all, or a fallback that doesn't do what it says.</para>
    /// </summary>
    static async Task CheckLiveWordsAsync(AppHost host)
    {
        var recorder = Shell.Windows.RecorderModel;
        var lines = recorder.Lines;
        lines.Clear();
        bool appleSilicon = OperatingSystem.IsMacOS() && System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture == System.Runtime.InteropServices.Architecture.Arm64;
        const double allowed = 8;
        var opened = System.Diagnostics.Stopwatch.StartNew();
        Shell.Windows.ShowRecorder(expanded: true);
        // A line of live words, or the verdict that this computer is too slow (one or two short passes, of 2 s of sound
        // at most each). The transcript's own first line may come meanwhile: that's not the live words.
        bool shown = await Until(() => (host.Captions.First is not null && lines.Count > 0) || host.Captions.TooSlow, 30);
        double took = opened.Elapsed.TotalSeconds;
        string speed = (host.Captions.Ratio is { } r ? $"{r:0.000}× the sound it heard" : "no pass timed") + $", {host.LiveEngine}";
        if (host.Captions.TooSlow || !host.LiveWordsOn)
        {
            int passes = host.Captions.Passes;
            bool came = await Until(() => lines.Count > 0, 90);
            double waited = opened.Elapsed.TotalSeconds;
            bool stillHearing = host.Captions.Passes != passes;
            if (appleSilicon && host.LiveEngine != LiveEngine.Whistle) Say($"FAILED live words: Apple silicon without Cactus Whistle ({speed})");
            else if (OperatingSystem.IsLinux() && host.LiveEngine != LiveEngine.SmallWhisper) Say($"FAILED live words: Linux without its small Whisper ({speed})");
            else if (!came) Say($"FAILED live words: off here ({speed}), and the transcript's own lines didn't come either in 90 s");
            else if (stillHearing) Say($"FAILED live words: off here ({speed}), yet {host.Captions.Passes - passes} more pass(es) heard the sound");
            else if (recorder.QuickWords) Say("FAILED live words: off here, yet the recorder still says the words come in a few seconds");
            else Say($"live words: off on this computer ({(host.Captions.TooSlow ? $"too slow: {speed}" : "no live words' engine runs here")}); nothing hears them, "
                     + $"and the recorder showed the transcript's own line {waited:0.0} s after it opened");
        }
        else if (appleSilicon && host.LiveEngine != LiveEngine.Whistle) Say($"FAILED live words: Apple silicon without Cactus Whistle ({speed})");
        else if (OperatingSystem.IsLinux() && host.LiveEngine != LiveEngine.SmallWhisper) Say($"FAILED live words: Linux without its small Whisper ({speed})");
        else if (shown && took <= allowed) Say($"live words: the recorder showed \"{lines[^1].Text}\" {took:0.0} s after it opened ({speed})");
        // A computer near the limit (its first passes a little over 0.3×, then under) shows the words later than 8 s,
        // but shows them: said, not failed. Only no words and no verdict at all is a failure.
        else if (shown) Say($"live words: near the limit here: the recorder showed \"{lines[^1].Text}\" {took:0.0} s after it opened, "
                            + $"once a pass kept up ({speed})");
        else Say($"FAILED live words: no line {took:0} s after the recorder opened, and no verdict ({speed})");
        await Settle();
        // The newest line is where the student looks: in view, at the bottom.
        if (Shell.Windows.Recorder?.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault(v => v.Name == "Heard") is { } heard
            && lines.Count > 0)
        {
            var newest = heard.GetVisualDescendants().OfType<TextBlock>().LastOrDefault(t => t.Text == lines[^1].Text);
            var bottom = newest?.TranslatePoint(new Point(0, newest.Bounds.Height), heard);
            Say(bottom is { } b && b.Y >= 0 && b.Y <= heard.Viewport.Height + 1
                ? $"live words: {lines.Count} line(s), the newest in view"
                : $"FAILED live words: the newest line isn't in view ({lines.Count} lines, scrolled {heard.Offset.Y:0} of {heard.Extent.Height - heard.Viewport.Height:0})");
        }
        Shot(Shell.Windows.Recorder, "recorder-live-words");
        Shell.Windows.ShowRecorder(expanded: false);
    }

    static async Task RunRecordingAsync(AppHost host)
    {
        await PanelShot("panel-idle");
        Say($"panel: {Shell.Windows.PanelModel.RecordLabel} — {Shell.Windows.PanelModel.Hint}");

        Shell.Windows.RecordViaShortcut();
        var live = host.Recorder.Current;
        Say(live is null ? "recording didn't start" : $"recording {live.Id} for {(live.ClassName.Length > 0 ? live.ClassName : "the library to sort")}");
        if (live is null) throw new InvalidOperationException("Record didn't start a lecture");
        await Settle();
        Shot(Shell.Windows.Recorder, "recorder-pill");
        await CheckRecorderDragAsync();
        await CheckRecorderAwayAsync();

        Shell.Windows.TogglePause();
        await Settle();
        Shot(Shell.Windows.Recorder, "recorder-paused");
        Shell.Windows.TogglePause();
        await CheckLiveWordsAsync(host);

        await PanelShot("panel-recording");

        // Keep recording until there's enough transcript for the notes engine to bother with, or 300 s of audio.
        bool enough = await Until(() => host.Lectures.Get(live.Id)?.Transcript().Length >= 1600 || (host.Recorder.Current?.Seconds ?? 0) >= 300, 300);
        var l = host.Lectures.Get(live.Id);
        Say(enough ? $"heard {l?.Transcript().Length} characters" : $"only heard {l?.Transcript().Length ?? 0} characters in time");
        Shell.Windows.ShowRecorder(expanded: true);
        await Settle();
        Shot(Shell.Windows.Recorder, "recorder-expanded");

        Shell.Windows.StopRecording();
        bool transcribed = await Until(() => host.Lectures.Get(live.Id)?.State is LectureState.Sending or LectureState.Writing or LectureState.Filed or LectureState.Failed, 300); // a CI Mac has no GPU: Whisper works through the rest of the lecture on its CPU
        l = host.Lectures.Get(live.Id);
        Say($"after stopping: {l?.State} ({l?.Segments.Count} lines, {TimedText.Clock(l?.Seconds ?? 0)})");
        if (!transcribed) throw new InvalidOperationException("Whisper never finished the lecture");

        bool filed = await Until(() => host.Lectures.Get(live.Id)?.State is LectureState.Filed or LectureState.Failed, 180);
        l = host.Lectures.Get(live.Id);
        Say(filed && l?.State == LectureState.Filed ? $"filed in '{l.FiledClass}'" : $"not filed: {l?.State} {l?.Error}");
        Say($"the self-test's engine saw {Engine!.SortRequests} sort request(s) and {Engine.NotesRequests} notes request(s)");
        if (l?.State != LectureState.Filed) throw new InvalidOperationException("the lecture was never filed");
        if (OneComputer)
        {
            // The notes were written by this computer's own library.
            if (Engine.NotesRequests == 0) throw new InvalidOperationException("this computer's library never asked for the notes");
            Say($"notes written on this computer by its own library ({host.LocalLibrary?.State}, {host.Client().ServerUrl})");
        }

        Shell.ShowLibrary();
        await Settle();
        Shell.Windows.OpenLecture(live.Id);
        await Settle();
        Shot(Shell.Windows.Main, "library-note");
        await RunRichNotesAsync();
        Shell.Windows.OpenLecture(live.Id, transcript: true);
        await Settle();
        Shot(Shell.Windows.Main, "library-transcript");

        Shell.Windows.ToggleQuick();
        await Shell.Windows.Search("midterm");
        await Settle();
        Shot(Shell.Windows.Quick, "quick-search");
        Say($"quick search 'midterm': {Shell.Windows.QuickModel.Rows.Count} row(s)");
        Shell.Windows.Quick?.Hide();
    }

    // --- rich notes: formulas and a flowchart, drawn and opened larger -------------------------------------------

    /// <summary>
    /// The lecture's notes as the app shows them: its formulas typeset (CSharpMath) and its flowchart laid out (MSAGL)
    /// and drawn, each with ink in its own place in a picture of the window, which only happens when the maths and
    /// drawing libraries, and Skia's and HarfBuzz's own native code, came with the app. Then the flowchart opens larger
    /// the way a student would (Enter on it), in a window of its own, which closes again.
    /// </summary>
    static async Task RunRichNotesAsync()
    {
        var main = Shell.Windows.Main ?? throw new InvalidOperationException("the library window isn't open");
        bool drawn = await Until(() => main.GetVisualDescendants().OfType<MathView>().Any(m => m.Display)
            && main.GetVisualDescendants().OfType<DiagramView>().Any(d => d.Scene is not null), 30);
        var formulas = main.GetVisualDescendants().OfType<MathView>().ToList();
        var chart = main.GetVisualDescendants().OfType<DiagramView>().FirstOrDefault();
        Say($"rich notes: {formulas.Count} formula(s), {formulas.Count(f => f.ErrorMessage is null)} typeset; "
            + (chart?.Scene is { } s ? $"a flowchart laid out {s.Width:0}×{s.Height:0}" : "no flowchart drawn"));
        if (!drawn || formulas.Count < 4 || formulas.Any(f => f.ErrorMessage is not null))
            throw new InvalidOperationException("the notes' formulas weren't typeset: " + string.Join("; ", formulas.Select(f => f.ErrorMessage).OfType<string>()));
        if (chart?.Scene is null) throw new InvalidOperationException("the notes' flowchart was never drawn");

        foreach (var (what, control, least) in new (string, Control, int)[] { ("formula", formulas.First(f => f.Display), 150), ("flowchart", chart, 600) })
        {
            int ink = Ink(control);
            Say($"{what}: {control.Bounds.Width:0}×{control.Bounds.Height:0}, {ink} pixels of ink");
            if (control.Bounds.Width < 40 || control.Bounds.Height < 16 || ink < least) throw new InvalidOperationException($"the notes' {what} drew nothing");
        }
        chart.BringIntoView();
        await Settle();
        Shot(main, "library-note-rich");

        chart.Focus();
        chart.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter, Source = chart });
        bool opened = await Until(() => DiagramWindow.Current is { IsVisible: true }, 10);
        var larger = DiagramWindow.Current;
        Say(opened && larger is not null ? $"opened larger: '{larger.Title}', {larger.Bounds.Width:0}×{larger.Bounds.Height:0}" : "the flowchart didn't open larger");
        if (!opened || larger is null) throw new InvalidOperationException("Enter on the flowchart didn't open it larger");
        await Settle();
        Shot(larger, "diagram-larger");
        var big = larger.GetVisualDescendants().OfType<DiagramView>().First();
        int bigInk = Ink(big);
        Say($"the larger flowchart: {big.Bounds.Width:0}×{big.Bounds.Height:0}, {bigInk} pixels of ink");
        if (bigInk < 600) throw new InvalidOperationException("the larger window drew no flowchart");
        larger.Close();
        bool closed = await Until(() => DiagramWindow.Current is null, 5);
        Say(closed ? "the larger window closed" : "the larger window didn't close");
        if (!closed) throw new InvalidOperationException("the larger window didn't close");
    }

    /// <summary>How many pixels <paramref name="control"/> inks when it's drawn on its own (on nothing, so only what
    /// it draws counts; the window around it can't hide it or stand in for it), in the window's own units.</summary>
    static int Ink(Control control)
    {
        int times = OperatingSystem.IsWindows() ? 1 : 2;
        var size = new PixelSize(Math.Max(1, (int)Math.Ceiling(control.Bounds.Width * times)), Math.Max(1, (int)Math.Ceiling(control.Bounds.Height * times)));
        using var bmp = new RenderTargetBitmap(size, new Vector(96 * times, 96 * times));
        bmp.Render(control);
        var buffer = new byte[size.Width * size.Height * 4];
        var pinned = System.Runtime.InteropServices.GCHandle.Alloc(buffer, System.Runtime.InteropServices.GCHandleType.Pinned);
        try
        {
            bmp.CopyPixels(new PixelRect(size), pinned.AddrOfPinnedObject(), buffer.Length, size.Width * 4);
        }
        finally
        {
            pinned.Free();
        }
        int ink = 0;
        for (int i = 3; i < buffer.Length; i += 4)
            if (buffer[i] > 40) ink++;
        return ink / (times * times);
    }

    // --- problems: the library goes away, and comes back --------------------------------------------------------

    static async Task RunProblemsAsync(AppHost host)
    {
        // The self-test's own library, or (just this computer) the app's.
        var lib = Library ?? host.LocalLibrary ?? throw new InvalidOperationException("no library to stop");
        string password = lib.Cfg.PoolPassword;
        var stopping = System.Diagnostics.Stopwatch.StartNew();
        await lib.StopAsync();
        // Well under the 10 s it's given before being ended for good: it stopped when asked (on Windows, by its input ending).
        Say($"library asked to stop: gone in {stopping.Elapsed.TotalSeconds:0.0} s");
        bool gone = await Until(() => host.Library != LibraryState.Connected, 30);
        Say(gone ? $"library stopped: {host.Library}" : "library: still says Connected 30 s after stopping it");
        await PanelShot("panel-library-unreachable");

        Shell.Windows.RecordViaShortcut();
        var waiting = host.Recorder.Current;
        if (waiting is not null)
        {
            // ~8 s of audio, which the mic file plays at 4x speed
            if (!await Until(() => host.Recorder.Elapsed >= 8, 30)) Say("FAILED the waiting lecture didn't reach 8 s of audio in 30 s");
            Shell.Windows.StopRecording();
            await Settle();
            var wl = host.Lectures.Get(waiting.Id);
            Say($"recorded while the library was down: {wl?.State}");
            await PanelShot("panel-waiting");
        }
        else
        {
            Say("couldn't record while the library was down (model or mic problem)");
        }

        await lib.StartAsync();
        bool back = await Until(() => host.Library == LibraryState.Connected, 30);
        Say(back ? "library reachable again" : "library: didn't reconnect in 30 s");
        if (waiting is not null)
        {
            bool waitingFiled = await Until(() => host.Lectures.Get(waiting.Id)?.State == LectureState.Filed, 120);
            Say(waitingFiled ? "the waiting lecture was filed once the library answered" : $"the waiting lecture stayed {host.Lectures.Get(waiting.Id)?.State}");
        }

        // A changed password: the library says so, and setting it right again in Settings would fix it (not
        // exercised here — Settings' library section is a WS4/WS1 screen).
        // What's on disk (the classes setup added are there), and the password the library's checked with.
        await lib.StopAsync();
        var cfg = Configs.Load(lib.Cfg.Home);
        cfg.PoolPassword = lib.Cfg.PoolPassword = "a-different-password";
        Configs.Save(cfg);
        await lib.StartAsync();
        bool wrongPw = await Until(() => host.Library == LibraryState.WrongPassword, 30);
        Say(wrongPw ? "password change noticed" : $"password change: library says {host.Library}");
        await PanelShot("panel-wrong-password");

        await lib.StopAsync();
        cfg.PoolPassword = lib.Cfg.PoolPassword = password;
        Configs.Save(cfg);
        await lib.StartAsync();
        bool restored = await Until(() => host.Library == LibraryState.Connected, 30);
        Say(restored ? "password restored, connected again" : $"password restore: library says {host.Library}");
    }

    // --- settings ------------------------------------------------------------------------------------------------

    static async Task RunSettingsAsync()
    {
        Shell.ShowSettings();
        await Settle();
        Shot(Shell.Windows.Settings, "settings-library");
        if (OneComputer && (Shell.Windows.Settings?.Content as Control)?.DataContext is SettingsModel lib)
        {
            // Your library, on this computer: Add a laptop, for later.
            lib.Section = "Library";
            await Settle();
            Say($"settings: your library here, laptops can connect: {lib.Lib.LaptopsCanConnect}, add a laptop offered: {lib.Lib.CanChangeLaptops}");
            Shot(Shell.Windows.Settings, "settings-your-library");
        }
        if ((Shell.Windows.Settings?.Content as Control)?.DataContext is SettingsModel sm)
        {
            sm.Section = "Recording";
            await Settle();
            Shot(Shell.Windows.Settings, "settings-recording");
        }
        Shell.Windows.Settings?.Close();
    }
}
