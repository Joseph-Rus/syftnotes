using System.Security.Cryptography;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using StudyStash.App.Services;
using StudyStash.App.ViewModels;
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
/// </summary>
public static class SelfTest
{
    public static string? Dir => Environment.GetEnvironmentVariable("STUDYSTASH_SELFTEST") is { Length: > 0 } d ? d : null;

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
        using var bmp = new RenderTargetBitmap(new PixelSize(size.Width * 2, size.Height * 2), new Vector(192, 192));
        bmp.Render(w);
        using var f = File.Create(Path.Combine(Dir!, name + ".png"));
        bmp.Save(f, PngBitmapEncoderOptions.Default);
        Say($"{name}: {w.Bounds.Width:0}×{w.Bounds.Height:0} at {w.Position} (scale {w.RenderScaling:0.0#})");
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
            if (await Task.WhenAny(script, Task.Delay(TimeSpan.FromMinutes(8))) != script)
                throw new TimeoutException("the self-test ran past 8 minutes");
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
        Say($"skin {Skin.Current}, home {host.Home}");
        await RunSetupAsync(host);
        await RunRecordingAsync(host);
        await RunProblemsAsync(host);
        await RunSettingsAsync();
    }

    // --- setup -------------------------------------------------------------------------------------------------------

    static async Task RunSetupAsync(AppHost host)
    {
        bool opened = await Until(() => Shell.Windows.Setup is not null, 10);
        Say(opened ? "setup opened" : "setup didn't open");
        var m = Shell.Windows.SetupModel ?? throw new InvalidOperationException("no setup window");

        // Welcome: a build with no installer role asks, and starts on the laptop (which the self-test is).
        Say($"welcome: {m.FlowName}, {m.Steps.Count} steps");
        Shot(Shell.Windows.Setup, "setup-welcome");
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

        // Classes: CS 101, with a time covering right now, so recording follows the timetable straight to it.
        var now = DateTime.Now;
        var end = now.AddMinutes(80);
        var midnight = now.Date.AddDays(1).AddSeconds(-1);
        if (end > midnight) end = midnight;
        m.NewClass = "CS 101";
        string when = m.NewWhen = $"{Day(now.DayOfWeek)} {now.AddMinutes(-10):HH:mm}-{end:HH:mm}";
        await m.AddClassCommand.ExecuteAsync(null);
        Say(m.ClassProblem is null ? $"class added: {when}" : $"class problem: {m.ClassProblem}");
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

    static string Day(DayOfWeek d) => d switch
    {
        DayOfWeek.Monday => "Mon", DayOfWeek.Tuesday => "Tue", DayOfWeek.Wednesday => "Wed", DayOfWeek.Thursday => "Thu",
        DayOfWeek.Friday => "Fri", DayOfWeek.Saturday => "Sat", _ => "Sun",
    };

    // --- recording -----------------------------------------------------------------------------------------------

    static async Task RunRecordingAsync(AppHost host)
    {
        await PanelShot("panel-idle");
        Say($"panel: {Shell.Windows.PanelModel.RecordLabel} — {Shell.Windows.PanelModel.Hint}");

        Shell.Windows.RecordViaShortcut();
        var live = host.Recorder.Current;
        Say(live is null ? "recording didn't start" : $"recording {live.Id} for {live.ClassName}");
        if (live is null) throw new InvalidOperationException("Record didn't start a lecture");
        await Wait(1.5);
        Shot(Shell.Windows.Recorder, "recorder-pill");

        Shell.Windows.TogglePause();
        await Wait(0.5);
        Shot(Shell.Windows.Recorder, "recorder-paused");
        Shell.Windows.TogglePause();

        await PanelShot("panel-recording");

        // Keep recording until there's enough transcript for the notes engine to bother with, or 300 s of audio.
        bool enough = await Until(() => host.Lectures.Get(live.Id)?.Transcript().Length >= 1600 || (host.Recorder.Current?.Seconds ?? 0) >= 300, 300);
        var l = host.Lectures.Get(live.Id);
        Say(enough ? $"heard {l?.Transcript().Length} characters" : $"only heard {l?.Transcript().Length ?? 0} characters in time");
        Shell.Windows.ShowRecorder(expanded: true);
        await Wait(1);
        Shot(Shell.Windows.Recorder, "recorder-expanded");

        Shell.Windows.StopRecording();
        bool transcribed = await Until(() => host.Lectures.Get(live.Id)?.State is LectureState.Sending or LectureState.Writing or LectureState.Filed or LectureState.Failed, 120);
        l = host.Lectures.Get(live.Id);
        Say($"after stopping: {l?.State} ({l?.Segments.Count} lines, {TimedText.Clock(l?.Seconds ?? 0)})");
        if (!transcribed) throw new InvalidOperationException("Whisper never finished the lecture");

        bool filed = await Until(() => host.Lectures.Get(live.Id)?.State is LectureState.Filed or LectureState.Failed, 180);
        l = host.Lectures.Get(live.Id);
        Say(filed && l?.State == LectureState.Filed ? $"filed in '{l.FiledClass}'" : $"not filed: {l?.State} {l?.Error}");
        Say($"the self-test's engine saw {Engine!.SortRequests} sort request(s) and {Engine.NotesRequests} notes request(s)");
        if (l?.State != LectureState.Filed) throw new InvalidOperationException("the lecture was never filed");

        Shell.ShowLibrary();
        await Wait(1.5);
        Shell.Windows.OpenLecture(live.Id);
        await Wait(1);
        Shot(Shell.Windows.Main, "library-note");
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

    // --- problems: the library goes away, and comes back --------------------------------------------------------

    static async Task RunProblemsAsync(AppHost host)
    {
        await Library!.StopAsync();
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

        await Library.StartAsync();
        bool back = await Until(() => host.Library == LibraryState.Connected, 30);
        Say(back ? "library reachable again" : "library: didn't reconnect in 30 s");
        if (waiting is not null)
        {
            bool waitingFiled = await Until(() => host.Lectures.Get(waiting.Id)?.State == LectureState.Filed, 120);
            Say(waitingFiled ? "the waiting lecture was filed once the library answered" : $"the waiting lecture stayed {host.Lectures.Get(waiting.Id)?.State}");
        }

        // A changed password: the library says so, and setting it right again in Settings would fix it (not
        // exercised here — Settings' library section is a WS4/WS1 screen).
        await Library.StopAsync();
        var cfg = Library.Cfg;
        cfg.PoolPassword = "a-different-password";
        Configs.Save(cfg);
        await Library.StartAsync();
        bool wrongPw = await Until(() => host.Library == LibraryState.WrongPassword, 30);
        Say(wrongPw ? "password change noticed" : $"password change: library says {host.Library}");
        await PanelShot("panel-wrong-password");

        await Library.StopAsync();
        cfg.PoolPassword = LibraryPassword ?? "";
        Configs.Save(cfg);
        await Library.StartAsync();
        bool restored = await Until(() => host.Library == LibraryState.Connected, 30);
        Say(restored ? "password restored, connected again" : $"password restore: library says {host.Library}");
    }

    // --- settings ------------------------------------------------------------------------------------------------

    static async Task RunSettingsAsync()
    {
        Shell.ShowSettings();
        await Wait(1);
        Shot(Shell.Windows.Settings, "settings-library");
        if ((Shell.Windows.Settings?.Content as Control)?.DataContext is SettingsModel sm)
        {
            sm.Section = "Recording";
            await Wait(1);
            Shot(Shell.Windows.Settings, "settings-recording");
        }
        Shell.Windows.Settings?.Close();
    }
}
