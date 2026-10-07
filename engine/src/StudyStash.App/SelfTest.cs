using System.Security.Cryptography;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StudyStash.App.Controls.Rich;
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
        Library = new LibraryService(libHome, cfg);
        await Library.StartAsync();
        if (Library.State is not (LibraryServiceState.Running or LibraryServiceState.Elsewhere))
            throw new InvalidOperationException(Library.Failure ?? "the library didn't start");
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

    static async Task Wait(double seconds) => await Task.Delay(TimeSpan.FromSeconds(seconds));

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
        await Wait(0.5);
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
    }

    // --- setup -------------------------------------------------------------------------------------------------------

    static async Task RunSetupAsync(AppHost host)
    {
        bool opened = await Until(() => Shell.Windows.Setup is not null, 10);
        Say(opened ? "setup opened" : "setup didn't open");
        var m = Shell.Windows.SetupModel ?? throw new InvalidOperationException("no setup window");
        await ByHandAsync();

        // Welcome: three choices, starting on just this computer; the self-test is a laptop with its own library.
        Say($"welcome: {m.FlowName}, {m.Steps.Count} steps");
        await Wait(1); // the window lays out its steps first
        Shot(Shell.Windows.Setup, "setup-welcome");
        m.ChooseLaptopCommand.Execute(null);
        Say($"chose the laptop: {m.FlowName}, {m.Steps.Count} steps");
        m.NextCommand.Execute(null);

        // Library: find it (our own, on its own port, never 8787), a wrong password, then the right one.
        await m.FindCommand.ExecuteAsync(null);
        Say($"find: {m.LibraryResult}");
        Shot(Shell.Windows.Setup, "setup-library-found");

        m.Password = "not-the-password";
        await m.ConnectCommand.ExecuteAsync(null);
        Say($"wrong password: {m.LibraryResult}");
        Shot(Shell.Windows.Setup, "setup-library-wrong-password");

        m.Password = LibraryPassword ?? "";
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

        // Model: entering the step starts the download from our own mirror (never Hugging Face).
        bool downloading = await Until(() => m.ModelProgress is > 0.05 and < 0.95, 20);
        Say(downloading ? $"model downloading: {m.ModelDone} {m.ModelLeft}" : "model: no progress seen in 20 s");
        Shot(Shell.Windows.Setup, "setup-model-downloading");
        bool ready = await Until(() => m.ModelReady, 60);
        Say(ready ? "model ready" : $"model: not ready in 60 s ({m.ModelProblem})");
        Shot(Shell.Windows.Setup, "setup-model-ready");
        if (!ready) throw new InvalidOperationException("the model never finished downloading");
        m.NextCommand.Execute(null);

        // Canvas is optional: the self-test skips it (there's no Chrome or Canvas here to connect).
        if (m.OnCanvas)
        {
            await Wait(1); // the window grows to the step's size first
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
            Shot(Shell.Windows.Setup, "setup-taskbar");
            m.NextCommand.Execute(null);
        }

        // Done: how to record, then Open Study Stash. The role stays Laptop; no login item is touched.
        if (m.OnDone) Shot(Shell.Windows.Setup, "setup-done");
        if (Shell.Windows.Setup is not null && m.IsLast) m.NextCommand.Execute(null);
        bool closed = await Until(() => Shell.Windows.Setup is null, 10);
        Say(closed ? $"setup finished: role {host.Settings.Role}, setup done {host.Settings.SetupDone}" : "setup: the window never closed");
        await Wait(1);
        Shot(Shell.Windows.Main, "library-empty");
    }

    /// <summary>A first run opens guided setup on Pick your AI. The self-test finds no AI's CLI (it never installs one
    /// or reaches an account), so it takes the first screen's picture and goes on by hand, as a student can.</summary>
    static async Task ByHandAsync()
    {
        if (Shell.Windows.Guided is not { } g) return;
        await Until(() => g.Screen == ViewModels.GuidedScreen.PickAi && g.Steps.Count > 0, 10);
        Say($"guided setup: {g.Screen}, Claude here: {g.ClaudeHere}, ChatGPT here: {g.CodexHere}");
        await Wait(1);
        Shot(Shell.Windows.Setup, "setup-guided-pick");
        g.ByHandCommand.Execute(null);
        bool swapped = await Until(() => g.Screen == ViewModels.GuidedScreen.Manual, 5);
        Say(swapped ? "set up by hand" : "setup by hand didn't open");
    }

    /// <summary>Just this computer: the welcome's first choice makes the library here (listening to this computer
    /// alone), then the microphone, the model, who writes the notes, Canvas (skipped), a class, and start at login (left
    /// off: the self-test never adds a login item).</summary>
    static async Task RunOneComputerSetupAsync(AppHost host)
    {
        bool opened = await Until(() => Shell.Windows.Setup is not null, 10);
        Say(opened ? "setup opened" : "setup didn't open");
        var m = Shell.Windows.SetupModel ?? throw new InvalidOperationException("no setup window");
        await ByHandAsync();

        m.ChooseOneComputerCommand.Execute(null);
        Say($"welcome: {m.FlowName}, {m.Steps.Count} steps: {string.Join(", ", m.Steps.Select(x => x.Title))}");
        await Wait(1); // the sidebar lists the chosen setup's steps first
        Shot(Shell.Windows.Setup, "setup-welcome");
        await m.NextCommand.ExecuteAsync(null);
        Say($"library made here: {m.LibraryResult}");
        if (!m.LibraryOk || host.LocalLibrary is not { } lib) throw new InvalidOperationException("Just this computer didn't make its library");
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
        // Its notes engine is the self-test's own (never a real Ollama): the library starts again to use it.
        cfg.OllamaHost = Engine!.Url;
        cfg.OllamaModel = "self-test-notes";
        cfg.AutoUpdate = false;
        Configs.Save(cfg);
        await lib.StopAsync();
        await lib.StartAsync();
        Say($"library restarted on the self-test's notes engine: {lib.State}");

        bool heard = await Until(() => m.MicHeard, 10);
        Say(heard ? "microphone heard" : "microphone: nothing heard in 10 s");
        Shot(Shell.Windows.Setup, "setup-microphone");
        m.NextCommand.Execute(null);

        bool ready = await Until(() => m.ModelReady, 60);
        Say(ready ? "model ready" : $"model: not ready in 60 s ({m.ModelProblem})");
        Shot(Shell.Windows.Setup, "setup-model-ready");
        if (!ready) throw new InvalidOperationException("the model never finished downloading");
        m.NextCommand.Execute(null);

        // Who writes the notes: the engines on this computer, as the library sees them. The self-test's engine stands
        // in for Ollama (the library's own pick), whatever this computer has installed, so the step is left as it is.
        bool loaded = await Until(() => m.Ai is { Engines.Count: > 0 }, 15);
        await Wait(2); // the window grows to the step's size first
        Shot(Shell.Windows.Setup, "setup-notes");
        Say(loaded ? $"notes step: {string.Join(", ", m.Ai!.Engines.Select(e => $"{e.Name} ({e.State})"))}; no AI offered: {m.Ai.OffersNoAi}" : "notes step: the engines never loaded");
        if (!loaded) throw new InvalidOperationException("the notes step never read the library's engines");
        m.Go(SetupStep.Canvas);

        if (m.OnCanvas)
        {
            await Wait(1);
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

        // Start at login: shown, and left off (the self-test never adds a login item).
        if (!m.OnStartAtLogin) throw new InvalidOperationException($"expected Start at login, got {m.Step}");
        m.StartAtLogin = false;
        Shot(Shell.Windows.Setup, "setup-start-at-login");
        m.NextCommand.Execute(null);

        if (m.OnTaskbar)
        {
            Shot(Shell.Windows.Setup, "setup-taskbar");
            m.NextCommand.Execute(null);
        }

        if (m.OnDone) Shot(Shell.Windows.Setup, "setup-done");
        if (Shell.Windows.Setup is not null && m.IsLast) m.NextCommand.Execute(null);
        bool closed = await Until(() => Shell.Windows.Setup is null, 10);
        Say(closed ? $"setup finished: role {host.Settings.Role}, setup done {host.Settings.SetupDone}" : "setup: the window never closed");
        if (host.Settings.Role != AppRole.Both) throw new InvalidOperationException($"just this computer finished as {host.Settings.Role}");
        await Wait(1);
        Shot(Shell.Windows.Main, "library-empty");
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
        await Wait(0.5);
        if (w.Panel() is { } after)
        {
            int want = mac ? area.Y : area.Bottom, got = mac ? after.Y : after.Bottom;
            string edge = mac ? "top" : "bottom";
            Say(Math.Abs(got - want) <= 1 ? $"recorder dragged {(mac ? "up to the menu bar" : "down to the taskbar")}: its {edge} at {got}, right against it"
                : $"FAILED recorder drag: its {edge} is at {got}, not {want} ({after} in {area})");
        }
        Shell.Windows.ShowRecorder(expanded: false);
        await Wait(0.3);
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
            else if (!came) Say($"FAILED live words: off here ({speed}), and the transcript's own lines didn't come either in 90 s");
            else if (stillHearing) Say($"FAILED live words: off here ({speed}), yet {host.Captions.Passes - passes} more pass(es) heard the sound");
            else if (recorder.QuickWords) Say("FAILED live words: off here, yet the recorder still says the words come in a few seconds");
            else Say($"live words: off on this computer ({(host.Captions.TooSlow ? $"too slow: {speed}" : "no live words' engine runs here")}); nothing hears them, "
                     + $"and the recorder showed the transcript's own line {waited:0.0} s after it opened");
        }
        else if (appleSilicon && host.LiveEngine != LiveEngine.Whistle) Say($"FAILED live words: Apple silicon without Cactus Whistle ({speed})");
        else if (shown && took <= allowed) Say($"live words: the recorder showed \"{lines[^1].Text}\" {took:0.0} s after it opened ({speed})");
        // A computer near the limit (its first passes a little over 0.3×, then under) shows the words later than 8 s,
        // but shows them: said, not failed. Only no words and no verdict at all is a failure.
        else if (shown) Say($"live words: near the limit here: the recorder showed \"{lines[^1].Text}\" {took:0.0} s after it opened, "
                            + $"once a pass kept up ({speed})");
        else Say($"FAILED live words: no line {took:0} s after the recorder opened, and no verdict ({speed})");
        await Wait(2);
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
        await Wait(1.5);
        Shot(Shell.Windows.Recorder, "recorder-pill");
        await CheckRecorderDragAsync();

        Shell.Windows.TogglePause();
        await Wait(0.5);
        Shot(Shell.Windows.Recorder, "recorder-paused");
        Shell.Windows.TogglePause();
        await CheckLiveWordsAsync(host);

        await PanelShot("panel-recording");

        // Keep recording until there's enough transcript for the notes engine to bother with, or 300 s of audio.
        bool enough = await Until(() => host.Lectures.Get(live.Id)?.Transcript().Length >= 1600 || (host.Recorder.Current?.Seconds ?? 0) >= 300, 300);
        var l = host.Lectures.Get(live.Id);
        Say(enough ? $"heard {l?.Transcript().Length} characters" : $"only heard {l?.Transcript().Length ?? 0} characters in time");
        Shell.Windows.ShowRecorder(expanded: true);
        await Wait(1);
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
        await Wait(1.5);
        Shell.Windows.OpenLecture(live.Id);
        await Wait(1);
        Shot(Shell.Windows.Main, "library-note");
        await RunRichNotesAsync();
        Shell.Windows.OpenLecture(live.Id, transcript: true);
        await Wait(1);
        Shot(Shell.Windows.Main, "library-transcript");

        Shell.Windows.ToggleQuick();
        await Shell.Windows.Search("midterm");
        await Wait(1);
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
        await Wait(1);
        Shot(main, "library-note-rich");

        chart.Focus();
        chart.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter, Source = chart });
        bool opened = await Until(() => DiagramWindow.Current is { IsVisible: true }, 10);
        var larger = DiagramWindow.Current;
        Say(opened && larger is not null ? $"opened larger: '{larger.Title}', {larger.Bounds.Width:0}×{larger.Bounds.Height:0}" : "the flowchart didn't open larger");
        if (!opened || larger is null) throw new InvalidOperationException("Enter on the flowchart didn't open it larger");
        await Wait(1);
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
            await Wait(2); // ~8 s of audio at 4x speed
            Shell.Windows.StopRecording();
            await Wait(1);
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
        await Wait(1);
        Shot(Shell.Windows.Settings, "settings-library");
        if (OneComputer && (Shell.Windows.Settings?.Content as Control)?.DataContext is SettingsModel lib)
        {
            // Your library, on this computer: Add a laptop, for later.
            lib.Section = "Library";
            await Wait(1.5);
            Say($"settings: your library here, laptops can connect: {lib.Lib.LaptopsCanConnect}, add a laptop offered: {lib.Lib.CanChangeLaptops}");
            Shot(Shell.Windows.Settings, "settings-your-library");
        }
        if ((Shell.Windows.Settings?.Content as Control)?.DataContext is SettingsModel sm)
        {
            sm.Section = "Recording";
            await Wait(1);
            Shot(Shell.Windows.Settings, "settings-recording");
        }
        Shell.Windows.Settings?.Close();
    }
}
