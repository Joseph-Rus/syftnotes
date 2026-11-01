using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Ellipse = Avalonia.Controls.Shapes.Ellipse;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StudyStash.App.Services;
using StudyStash.App.ViewModels;
using StudyStash.App.Views;
using StudyStash.Core;
using StudyStash.Core.Ai;
using StudyStash.Core.Setup;
using StudyStash.Library;

namespace StudyStash.App.Tests;

/// <summary>
/// A film of first-run setup with a real AI behind it, drawn off screen (nothing opens on the computer's screen): the
/// app's own setup views and view models (guided, and by hand), wired the way Shell.ShowSetup wires them, over a real
/// AppHost in a throwaway home and a real library on its own port. The provider's real CLI runs for every turn; the
/// student's side is scripted (a pointer drawn where it clicks, answers typed into the chat). Each frame is the window
/// as the app draws it, saved to STUDYSTASH_SHOTS with a list of which frames are waiting (sped up in the GIF).
/// Only runs with STUDYSTASH_FILM=1: see qa-bench/offscreen-film/README.md, and film.sh beside it, which also guards
/// the AI's own config files and keeps the CLIs away from the student's own MCP servers.
/// </summary>
public sealed class SetupFilm
{
    [AvaloniaFact]
    public async Task Setup_with_a_real_ai_drawn_off_screen()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("STUDYSTASH_FILM") == "1", "A film, not a test: STUDYSTASH_FILM=1 (qa-bench/offscreen-film).");
        await using var film = new SetupFilmRig(SetupFilmOptions.FromEnvironment());
        await film.RunAsync();
    }
}

/// <summary>What to film: which AI the student picks, and which way through setup.</summary>
public sealed record SetupFilmOptions
{
    /// <summary>claude, codex, ollama or gemini.</summary>
    public required string Provider { get; init; }
    /// <summary>guided (pick the AI on the first screen, then its chat), byhand (setup by hand's steps, the AI picked for
    /// notes) or free ("I don't have a paid plan", then setup by hand: the free AI's way, and its default).</summary>
    public required string Flow { get; init; }
    /// <summary>The first port the throwaway library tries (never 8787).</summary>
    public int Port { get; init; } = 9431;
    public required string Frames { get; init; }
    public required string Log { get; init; }
    public TimeSpan Longest { get; init; } = TimeSpan.FromMinutes(16);
    /// <summary>Words for the caption about the CLI (film.sh says which binary and version, and any stand-in).</summary>
    public string Cli { get; init; } = "";

    public static SetupFilmOptions FromEnvironment()
    {
        string Env(string name, string fallback) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } v ? v : fallback;
        string provider = Env("STUDYSTASH_FILM_PROVIDER", "claude");
        string frames = Env("STUDYSTASH_SHOTS", Path.Combine(Path.GetTempPath(), "studystash-film", provider));
        int port = int.Parse(Env("STUDYSTASH_FILM_PORT", "9431"), CultureInfo.InvariantCulture);
        if (port is 8787 or 8788) throw new InvalidOperationException("The film's library never uses 8787: that's the owner's real library.");
        return new SetupFilmOptions
        {
            Provider = provider, Flow = Env("STUDYSTASH_FILM_FLOW", provider == "ollama" ? "free" : "guided"), Port = port, Frames = frames,
            Log = Env("STUDYSTASH_FILM_LOG", Path.Combine(frames, "film.log")), Cli = Env("STUDYSTASH_FILM_CLI", ""),
            Longest = TimeSpan.FromMinutes(double.Parse(Env("STUDYSTASH_FILM_MINUTES", "16"), CultureInfo.InvariantCulture)),
        };
    }
}

sealed class SetupFilmRig : IAsyncDisposable
{
    const int Width = 1000, Height = 860, CaptionHeight = 40;

    readonly SetupFilmOptions o;
    readonly TempHome home = new();
    readonly StreamWriter log, index;
    readonly Stopwatch clock = Stopwatch.StartNew();
    readonly Random random = new(7);
    readonly Mirror models = new();
    readonly MicCheck mic = new();
    FilmMic? speaking;
    readonly AppHost host;
    readonly SetupModel setup;
    readonly GuidedSetupModel guided;
    readonly GuidedSetup driver;
    readonly SetupTools tools;
    SetupMcpHost? door;
    readonly MacGuidedSetup guidedView;
    readonly MacSetup manualView;

    readonly Window window;
    readonly Border stage = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    readonly Canvas over = new() { IsHitTestVisible = false };
    readonly Avalonia.Controls.Shapes.Path pointer = new()
    {
        Data = Geometry.Parse("M0,0 L0,17 L4.6,13 L7.6,20 L10.4,18.8 L7.4,12 L13,12 Z"), Fill = Brushes.Black, Stroke = Brushes.White, StrokeThickness = 1.2,
    };
    readonly Ellipse ring = new() { Width = 26, Height = 26, Stroke = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0)), StrokeThickness = 3, IsVisible = false };
    readonly TextBlock captionLeft = new() { Foreground = Brushes.White, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    readonly TextBlock captionRight = new() { MaxWidth = 640, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = new SolidColorBrush(Color.Parse("#F5C451")), FontSize = 12.5, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right };
    Point at = new(Width / 2.0, Height - 160);
    int frames;
    bool finished;
    string result = "", shortResult = "";
    readonly List<string> outcome = [];

    public SetupFilmRig(SetupFilmOptions options)
    {
        o = options;
        Directory.CreateDirectory(o.Frames);
        foreach (string old in Directory.EnumerateFiles(o.Frames, "f-*.png")) File.Delete(old);
        log = new StreamWriter(o.Log, append: false) { AutoFlush = true };
        index = new StreamWriter(Path.Combine(o.Frames, "frames.tsv"), append: false) { AutoFlush = true };
        Log($"film: {o.Provider} {o.Flow}, home {home.Path}, library from port {o.Port}");

        // The transcription model never really downloads (it's not what this films): every request for it waits.
        foreach (var m in StudyStash.Audio.WhisperModels.All) models.Hold.Add(m.File);
        // A first run's microphone: not asked yet, allowed once the student presses Allow (no system prompt here).
        host = new AppHost(home.Path, microphone: () => speaking = new FilmMic(), log: s => Log("app " + s), loginItems: new CountingLoginItems(),
            models: new ModelSetting(null, Mirror: "https://mirror.example/models"), http: new HttpClient(models), micPermissions: new FilmMicAccess());
        setup = Services.Setup.Make(host, AppRole.Both, here: new LibraryHere { Command = [BuiltEngine()], Folder = home["Syft Notes"], FirstPort = o.Port });
        // Nothing outside the film opens: pages, settings panes and terminals are only written down.
        setup.OnMicSettings = () => Log("(would open the microphone's settings)");
        setup.OnOpenUrl = url => Log($"(would open {url})");
        setup.OnTaskbarSettings = () => Log("(would open the taskbar's settings)");
        var services = new GuidedServices
        {
            Home = host.Home,
            Windows = false,
            // Never a real installer: an AI that isn't here stops the film at its install screen.
            Installer = cli => throw new InvalidOperationException($"the film never installs {cli.Name}"),
            OpenUrl = url => Log($"(would open {url})"),
            Downloading = () => host.Downloading?.Fraction,
            Log = s => Log("app " + s),
            // No ChatGPT or Claude app to connect: the card that would change their settings never shows.
            AiApps = () => [],
            ConnectAiApp = null,
        };
        guided = new GuidedSetupModel(setup, services, () => host.Settings, host.Save);
        driver = new GuidedSetup(guided, host) { MakeCanvas = () => Task.FromResult<CanvasConnectModel?>(null), Installer = null };
        tools = new SetupTools(driver);

        // The views, wired as Shell.ShowSetup wires them.
        guidedView = new MacGuidedSetup { DataContext = guided, DrawChrome = true };
        manualView = new MacSetup { DataContext = setup, DrawChrome = true };
        guided.OnFinish = () => setup.OnFinish?.Invoke();
        setup.OnFinish = () =>
        {
            Services.Setup.Finish(setup, host);
            finished = true;
            Log("setup finished: Open Syft Notes");
        };
        setup.OnEnter = Enter;
        setup.OnCopy = _ => { };
        setup.OnGuided = () => guided.BackToChatCommand.Execute(null);
        guided.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(GuidedSetupModel.Screen)) return;
            setup.GuidedLabel = guided.AiReady ? $"Set up with {guided.Brand} instead" : "Set up with an AI instead";
            stage.Child = guided.Screen == GuidedScreen.Manual ? manualView : guidedView;
            Log($"screen: {guided.Screen}");
        };
        var leave = setup.LeaveAsync;
        setup.LeaveAsync = async step =>
        {
            if (step != SetupStep.Ai) return leave is null || await leave(step);
            if (setup.Ai is not { } ai) return true;
            if (!await ai.SaveAsync()) return false;
            setup.NotesSummary = ai.ChoiceWords;
            return true;
        };

        Skin.UseTheme(ColourThemes.Default);
        ((App)Application.Current!).UseSkin(SkinKind.Mac);
        stage.Child = guidedView;
        over.Children.Add(ring);
        over.Children.Add(pointer);
        var caption = new Border
        {
            Height = CaptionHeight, Background = new SolidColorBrush(Color.Parse("#1D2025")), Padding = new Thickness(16, 0),
            Child = new Grid { ColumnDefinitions = new ColumnDefinitions("*,16,Auto"), Children = { captionLeft, captionRight } },
        };
        Grid.SetColumn(captionRight, 2);
        DockPanel.SetDock(caption, Dock.Bottom);
        window = new Window
        {
            Width = Width, Height = Height + CaptionHeight, RequestedThemeVariant = ThemeVariant.Light,
            Content = new DockPanel { Children = { caption, new Panel { Background = new SolidColorBrush(Color.Parse("#DADDE2")), Children = { stage, over } } } },
        };
        Look.Apply(window);
        Place();
        Caption("");
    }

    static string BuiltEngine()
    {
        var bin = new DirectoryInfo(AppContext.BaseDirectory);
        string config = bin.Parent!.Name, framework = bin.Name;
        string dir = System.IO.Path.GetFullPath(System.IO.Path.Combine(bin.FullName, "..", "..", "..", "..", "..", "src", "StudyStash.Engine", "bin", config, framework));
        return System.IO.Path.Combine(dir, OperatingSystem.IsWindows() ? "studystash.exe" : "studystash");
    }

    string Who => o.Provider switch { "codex" => "ChatGPT", "ollama" => "Ollama", "gemini" => "Gemini", _ => "Claude" };

    void Log(string line)
    {
        lock (log) log.WriteLine($"{clock.Elapsed:mm\\:ss\\.f} {line}");
    }

    void Caption(string right)
    {
        string flow = o.Flow switch { "guided" => "guided setup", "free" => "\"I don't have a paid plan\", then setup by hand", _ => "setup by hand" };
        captionLeft.Text = $"OFF-SCREEN FILM · {flow} · {(o.Cli.Length > 0 ? o.Cli : Who)} · throwaway library · the student is scripted · waits sped up";
        captionRight.Text = right;
    }

    // --- the steps setup by hand makes as they open (Shell.EnterSetupStep) ----------------------------------------------

    void Enter(SetupStep step)
    {
        Log($"step: {step}");
        switch (step)
        {
            case SetupStep.Ai:
                if (setup.Ai is null)
                {
                    var cc = host.Client();
                    setup.Ai = new AiSetupModel(new AiRemote(cc.ServerUrl, cc.PoolKey))
                    {
                        Lede = $"They run on this {setup.DeviceWord}, as part of your library. You can change this later in Settings → Your AI.",
                        Windows = false,
                        OpenUrl = url => Log($"(would open {url})"),
                        WriteNotes = WriteNotesAsync,
                        NotesOn = NotesOnAsync,
                        ChosenBefore = setup.Again,
                        // As Shell.EnterSetupStep: the first screen's answer is the one the step opens on, and any AI
                        // that takes one can have an API key pasted there.
                        Prefer = guided.NotesPick,
                        TakesKeys = true,
                        OpenKeyFor = guided.NotesKeyFor,
                        SetUpPaid = guided.GetReadyForNotesAsync,
                    };
                    guided.ReadyForNotes = id =>
                    {
                        if (setup.Ai?.Engines.FirstOrDefault(r => r.Id == id) is { } row) _ = row.CheckAgainCommand.ExecuteAsync(null);
                    };
                }
                _ = setup.Ai.Load();
                break;
            case SetupStep.Canvas when setup.Canvas is null:
                // Canvas isn't what this films: the step is drawn over a pretend Canvas, and the student skips it.
                var ctx = CanvasFixtures.Context(new FakeLibrary());
                setup.Canvas = new CanvasConnectModel(ctx, new CanvasWatch(ctx), forSetup: true) { ShowFooter = false, FinishLabel = setup.ContinueLabel };
                setup.Canvas.OnSkip = () => setup.SkipCommand.Execute(null);
                setup.Canvas.OnFinish = () => setup.NextCommand.Execute(null);
                break;
        }
    }

    async Task<bool?> NotesOnAsync()
    {
        if (host.Remote() is not { } lib) return null;
        return (await lib.SettingsAsync(HttpMethod.Get))?["notes"]?["write"] is JsonValue v && v.TryGetValue(out bool on) ? on : null;
    }

    async Task<bool> WriteNotesAsync(bool on)
    {
        if (host.Remote() is not { } lib) return false;
        return await lib.SettingsAsync(HttpMethod.Post, "", new JsonObject { ["notes"] = new JsonObject { ["write"] = on, ["sort"] = on } }) is not null;
    }

    // --- drawing ---------------------------------------------------------------------------------------------------------

    void Place()
    {
        Canvas.SetLeft(pointer, at.X);
        Canvas.SetTop(pointer, at.Y);
        Canvas.SetLeft(ring, at.X - 13);
        Canvas.SetTop(ring, at.Y - 13);
    }

    void Settle()
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>The microphone's check, ticked as the app's own tick does; while it's open, the pretend microphone
    /// hears the student talking.</summary>
    void TickMic()
    {
        if (mic.IsOpen && speaking is { } m) m.Play(0.06, 0.25 + random.NextDouble() * 0.6);
        Services.Setup.TickMic(setup, host, mic, windows: false);
    }

    /// <summary>One frame: <paramref name="kind"/> is act (the student doing something), hold, or wait (the AI or the
    /// library working: these are the ones sped up).</summary>
    void Frame(string kind = "act")
    {
        TickMic();
        Settle();
        var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("nothing rendered");
        string name = $"f-{frames++:0000}.png";
        using (var file = File.Create(System.IO.Path.Combine(o.Frames, name))) frame.Save(file, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        index.WriteLine($"{name}\t{kind}");
    }

    void Hold(double seconds)
    {
        for (int i = 0; i < Math.Max(1, (int)(seconds * 12)); i++) Frame("hold");
    }

    async Task MoveToAsync(Control c, double fx = 0.5, double fy = 0.5, int steps = 8)
    {
        Settle();
        if (c.TranslatePoint(new Point(c.Bounds.Width * fx, c.Bounds.Height * fy), window) is not { } to) throw new InvalidOperationException($"can't find {c} on screen");
        var from = at;
        for (int i = 1; i <= steps; i++)
        {
            double t = i / (double)steps, eased = t * t * (3 - 2 * t);
            at = new Point(from.X + (to.X - from.X) * eased, from.Y + (to.Y - from.Y) * eased);
            Place();
            window.MouseMove(at);
            Frame();
        }
        await Task.Yield();
    }

    /// <summary>A click where the pointer is, through the window (the view's own buttons and bindings do the rest).</summary>
    async Task ClickAsync()
    {
        ring.IsVisible = true;
        Frame();
        Frame();
        ring.IsVisible = false;
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        Settle();
        window.MouseMove(at);
        Frame();
        await Task.Delay(30);
    }

    async Task PressAsync(Control c, string what, double fx = 0.5, double fy = 0.5)
    {
        Log($"student: {what}");
        Caption($"Student: {what}");
        await MoveToAsync(c, fx, fy);
        Hold(0.2);
        await ClickAsync();
    }

    /// <summary>Waits for the AI or the library, a frame every so often (and each time the AI's words grow), until
    /// <paramref name="idle"/> has held for a second. False when it took longer than <paramref name="most"/>.</summary>
    async Task<bool> WaitAsync(Func<bool> idle, string what, TimeSpan? most = null)
    {
        Caption(what);
        var took = Stopwatch.StartNew();
        var lastFrame = Stopwatch.StartNew();
        var calm = Stopwatch.StartNew();
        int words = Words();
        bool wasIdle = false;
        Frame("wait");
        while (took.Elapsed < (most ?? TimeSpan.FromMinutes(4)))
        {
            await Task.Delay(100);
            TickMic();
            Settle();
            bool now = idle();
            if (!now || !wasIdle) calm.Restart();
            wasIdle = now;
            int w = Words();
            if (w - words >= 30 && lastFrame.ElapsedMilliseconds > 150 || lastFrame.ElapsedMilliseconds > 900)
            {
                Frame(w != words ? "stream" : "wait");
                words = w;
                lastFrame.Restart();
            }
            if (now && calm.ElapsedMilliseconds > 1000)
            {
                Frame("wait");
                return true;
            }
        }
        Log($"waited {took.Elapsed.TotalSeconds:0} s for: {what}");
        return false;
    }

    int Words() => guided.Thread.OfType<AiEntry>().Sum(a => a.Text.Length);

    async Task TypeAsync(TextBox field, string text)
    {
        Log($"student types: {text}");
        Caption("Student types an answer");
        await MoveToAsync(field, 0.3, 0.5, 6);
        await ClickAsync();
        for (int i = 0; i < text.Length; i += 3)
        {
            field.Text = text[..Math.Min(text.Length, i + 3)];
            field.CaretIndex = field.Text.Length;
            Frame();
        }
        Hold(0.3);
    }

    Button ButtonFor(Control under, Func<Button, bool> which) =>
        under.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.IsEffectivelyVisible && b.IsEffectivelyEnabled && which(b))
        ?? throw new InvalidOperationException("no such button on screen");

    static string Said(Button b) => b.Content as string ?? b.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault()?.Text ?? "";

    // --- the film -----------------------------------------------------------------------------------------------------------

    public async Task RunAsync()
    {
        window.Show();
        door = await SetupMcpHost.StartAsync(tools);
        guided.Door = new SetupDoor(door.Url, door.Token, tools.NewTurn);
        Caption("Setup opens");
        await guided.OpenAsync();
        Log($"found: claude {guided.ClaudeFound.VersionText} at {guided.ClaudeFound.Exe}; codex {guided.CodexFound.VersionText} at {guided.CodexFound.Exe}");
        Hold(1.2);
        try
        {
            if (o.Flow == "guided") await GuidedAsync();
            else await ByHandAsync();
            if (finished) await CheckLibraryAsync();
        }
        catch (Exception e) when (e is InvalidOperationException or HttpRequestException or TaskCanceledException)
        {
            Stop($"The film stopped: {e.Message}");
        }
        Log("RESULT: " + (result.Length > 0 ? result : "(none)"));
        foreach (string line in outcome) Log("OUTCOME: " + line);
        File.WriteAllLines(System.IO.Path.Combine(o.Frames, "result.txt"), [result, .. outcome]);
        Caption(shortResult.Length > 0 ? shortResult : result);
        Hold(2.6);
    }

    /// <summary>Where the film ends, and why (the log has it whole; the caption a short line).</summary>
    void Stop(string why, string? caption = null)
    {
        result = why;
        shortResult = caption ?? why;
        Log("STOP: " + why);
    }

    // Guided setup: pick the AI, Continue, and then the chat.
    async Task GuidedAsync()
    {
        // The first screen's answers are its Pick buttons, each with the AI's id (claude, codex, gemini and free).
        var picks = guidedView.GetVisualDescendants().OfType<Button>().Where(b => b.Command == guided.PickCommand && b.IsEffectivelyVisible).ToList();
        Log("first screen offers: " + string.Join(", ", picks.Select(b => b.CommandParameter as string)));
        if (picks.FirstOrDefault(b => b.CommandParameter as string == o.Provider) is not { } pick)
        {
            Stop($"Guided setup has no {Who} to pick: its first screen offers {string.Join(", ", picks.Select(b => AutomationProperties.GetName(b)))}.", $"Stops: guided setup has no {Who} to pick");
            await MoveToAsync(picks[^1], 0.5, 0.5, 10);
            Hold(1.5);
            return;
        }
        await PressAsync(pick, $"picks {Who}");
        Hold(0.5);
        await PressAsync(ButtonFor(guidedView, b => b.Name == "Continue"), "Continue");
        // An answer with no chat (Gemini): setup by hand, its notes step opening on that AI.
        if (guided.Screen == GuidedScreen.Manual)
        {
            Log($"{Who} has no setup chat: Continue went on to setup by hand");
            Caption($"{Who}: no setup chat, so setup by hand opens on it");
            Hold(0.8);
            await ByHandStepsAsync();
            return;
        }
        // Signed in already (the CLI's own status says so), the plan check runs: one tiny turn with no tools.
        if (!await WaitAsync(() => guided.Screen == GuidedScreen.Chat || guided is { Screen: GuidedScreen.SignIn, CheckingPlan: false } && (guided.HasSignInProblem || guided.HasPlanProblem || guided.ShowOpenSignIn) || guided.Screen == GuidedScreen.Install,
                $"{Who}: checking the sign-in and plan (real)", TimeSpan.FromMinutes(3)))
        {
            Stop($"Nothing happened after Continue (screen {guided.Screen}).");
            return;
        }
        Log($"after Continue: screen {guided.Screen}, signed in {guided.SignedIn}, ready {guided.AiReady}, plan problem {guided.PlanProblem} {guided.PlanProblemTitle} {guided.PlanProblemText}, sign-in problem {guided.SignInProblem}");
        if (guided.Screen != GuidedScreen.Chat)
        {
            Stop(guided.Screen switch
            {
                GuidedScreen.Install => $"Stopped at \"Get {Who} ready\": the CLI wasn't found (the film never installs).",
                _ when guided.HasPlanProblem => $"Stopped at Sign in: {guided.PlanProblemTitle} {guided.PlanProblemText}".Trim(),
                _ when guided.HasSignInProblem => $"Stopped at Sign in: {guided.SignInProblem}",
                _ => $"Stopped at Sign in: not signed in (the film never opens a sign-in page).",
            });
            Hold(2);
            return;
        }
        await ChatAsync();
    }

    bool ChatIdle() => !guided.Busy && guided.OpenCard is not { Busy: true } && !setup.Connecting && !setup.AddingCourses;

    async Task ChatAsync()
    {
        int moves = 0, plain = 0;
        bool classesSaid = false;
        while (!finished && clock.Elapsed < o.Longest)
        {
            if (!await WaitAsync(ChatIdle, $"{Who} is answering (real)"))
            {
                Stop($"{Who} didn't finish its turn within 4 minutes.");
                return;
            }
            if (finished) break;
            if (guided.HasChatProblem)
            {
                Stop($"The chat stopped: {guided.ChatProblemText}");
                Hold(2.5);
                return;
            }
            if (guided.Screen != GuidedScreen.Chat)
            {
                Stop($"The chat left for {guided.Screen}: {guided.SignInProblem ?? guided.PlanProblemTitle}");
                return;
            }
            if (++moves > 26)
            {
                Stop("The chat went on for 26 of the student's moves without finishing.");
                return;
            }
            string last = guided.Thread.OfType<AiEntry>().LastOrDefault()?.Text ?? "";
            Log($"{Who} said: {last.Replace('\n', ' ')}");
            // The question ask_student showed (joined to the AI's words when they don't already have it word for word).
            if (typeof(GuidedSetupModel).GetField("question", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(guided) is string asked)
                Log($"ask_student: {asked} [{string.Join(" | ", guided.QuickReplies)}]");
            if (guided.OpenCard is { Open: true } card && await CardAsync(card)) continue;
            if (guided.QuickReplies.Count > 0)
            {
                string reply = PickReply(last, [.. guided.QuickReplies]);
                var button = ButtonFor(guidedView, b => b.Command == guided.ReplyCommand && b.CommandParameter as string == reply);
                await PressAsync(button, $"taps \"{reply}\"");
                plain = 0;
                continue;
            }
            string answer = Answer(last, ref classesSaid, out bool isPlain);
            plain = isPlain ? plain + 1 : 0;
            if (plain > 3)
            {
                Stop("The chat kept asking things the scripted student has no answer for.");
                return;
            }
            var field = guidedView.GetVisualDescendants().OfType<TextBox>().First(t => t.Name == "Field");
            await TypeAsync(field, answer);
            await PressAsync(ButtonFor(guidedView, b => b.Name == "Send"), "sends it");
        }
        if (!finished) Stop("Setup didn't finish in the time the film allows.");
    }

    /// <summary>The open card's one action, as the student would press it. False when the card is one the student
    /// answers in words instead.</summary>
    async Task<bool> CardAsync(CardEntry card)
    {
        Log($"card: {card.Kind} ({card.Title})");
        string? action = card.Kind switch
        {
            "computer_setup" => "setup",
            "model_download" => "download",
            "start_at_login" => "on",
            "finish" => "finish",
            "ai_app" => "notnow",
            "taskbar_tip" => "done",
            "microphone_check" when setup.ShowAllowMic => "allow",
            _ => null,
        };
        if (card.Kind == "microphone_check" && action is null)
        {
            // The student talks: the pretend microphone's levels move until Syft Notes hears them (the card folds).
            Caption("Student talks (a pretend microphone)");
            for (int i = 0; i < 30 && !setup.MicHeard; i++) Frame();
            Hold(0.6);
            return setup.MicHeard;
        }
        if (action is null) return false;
        var button = ButtonFor(guidedView, b => b.Command == card.PressCommand && b.CommandParameter as string == action);
        await PressAsync(button, $"presses \"{Said(button)}\"");
        return true;
    }

    static string PickReply(string question, IReadOnlyList<string> replies)
    {
        string q = question.ToLowerInvariant();
        string? Find(string pattern) => replies.FirstOrDefault(r => System.Text.RegularExpressions.Regex.IsMatch(r, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase));
        if (Find(@"just this") is { } one) return one;
        if (q.Contains("canvas")) return Find(@"^no\b|not\b|don't|skip|later") ?? replies[^1];
        if (q.Contains("log in") || q.Contains("login") || q.Contains("start")) return Find(@"yes|turn|on\b|sure") ?? replies[0];
        if (q.Contains("model") || q.Contains("download")) return Find(@"recommend|yes|download|sure") ?? replies[0];
        return Find(@"yes|sure|ok|continue|sounds|let's|ready|type|add") ?? replies[0];
    }

    static string Answer(string said, ref bool classesSaid, out bool plain)
    {
        string s = said.ToLowerInvariant();
        plain = false;
        if (s.Contains("canvas")) return "No, my school doesn't use Canvas.";
        if (s.Contains("class") || s.Contains("course") || s.Contains("taking"))
        {
            if (!classesSaid)
            {
                classesSaid = true;
                return "BIO 110, intro to biology, and CS 101, intro to programming.";
            }
            return "That's all my classes.";
        }
        if (s.Contains("log in") || s.Contains("login")) return "Yes please, turn it on.";
        if (s.Contains("model") || s.Contains("download")) return "Yes, the one you recommend.";
        plain = true;
        return "Sounds good. What's next?";
    }

    // Setup by hand: Continue through each step, the AI picked on the notes step.
    async Task ByHandAsync()
    {
        if (o.Flow == "free")
        {
            await PressAsync(guidedView.GetVisualDescendants().OfType<Button>().First(b => b.Name == "PickFree"), "picks \"I don't have a paid plan\"");
            Hold(0.4);
            await PressAsync(ButtonFor(guidedView, b => b.Name == "Continue"), "Continue");
        }
        else await PressAsync(ButtonFor(guidedView, b => b.Name == "ByHand"), "Set up by hand");
        await ByHandStepsAsync();
    }

    /// <summary>Setup by hand, page by page, to the end (or where it stops).</summary>
    async Task ByHandStepsAsync()
    {
        await WaitAsync(() => guided.Screen == GuidedScreen.Manual, "Setup by hand opens");
        Hold(0.6);
        for (int guard = 0; guard < 20 && !finished; guard++)
        {
            var step = setup.Step;
            Log($"by hand: {step}");
            switch (step)
            {
                case SetupStep.Ai:
                    if (!await NotesStepAsync()) return;
                    break;
                case SetupStep.Canvas:
                    Hold(0.6);
                    await PressAsync(ButtonFor(manualView, b => b.Command == setup.SkipCommand), "Skip for now");
                    await WaitAsync(() => setup.Step != SetupStep.Canvas, "");
                    continue;
                case SetupStep.Classes:
                    var name = manualView.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(t => t.IsEffectivelyVisible);
                    if (name is not null && setup.Classes.Count == 0) await TypeAsync(name, "BIO 110");
                    break;
                case SetupStep.Microphone:
                    if (setup.ShowAllowMic)
                    {
                        await PressAsync(ButtonFor(manualView, b => b.Command == setup.AllowMicCommand), "Allow (a pretend microphone)");
                        await WaitAsync(() => !setup.ShowAllowMic, "");
                    }
                    Caption("Student talks (a pretend microphone)");
                    for (int i = 0; i < 20 && !setup.MicHeard; i++) Frame();
                    break;
                default:
                    Hold(step == SetupStep.Done ? 1.2 : 0.5);
                    break;
            }
            var next = ButtonFor(manualView, b => b.Command == setup.NextCommand);
            await PressAsync(next, $"{Said(next)} ({step})");
            if (!await WaitAsync(() => !setup.Connecting && setup.Ai is not { Busy: true } && (finished || setup.Step != step || setup.LibraryResult is { Length: > 0 } && !setup.LibraryOk || setup.Ai is { HasSay: true }),
                    step == SetupStep.Welcome ? "Making the library (real)" : step == SetupStep.Ai ? "Saving who writes the notes" : ""))
            {
                Stop($"Stuck on {step}.");
                return;
            }
            if (!finished && setup.Step == step)
            {
                Stop($"Continue stayed on {step}: {setup.LibraryResult ?? setup.Ai?.Say ?? setup.ClassProblem ?? ""}".TrimEnd(' ', ':'));
                Hold(2);
                return;
            }
        }
    }

    /// <summary>The notes step: the student picks their AI's row. False when the row can't be picked as it is (what
    /// the step says is the film's result).</summary>
    async Task<bool> NotesStepAsync()
    {
        if (!await WaitAsync(() => setup.Ai is { } ai && ai.Engines.Count > 0, "The library looks at the AIs on this Mac (real)", TimeSpan.FromSeconds(60)))
        {
            Stop("The notes step never listed any AI.");
            return false;
        }
        var ai = setup.Ai!;
        foreach (var r in ai.Engines) Log($"notes row: {r.Id} \"{r.Name}\" state {r.State}, can write {r.CanWrite}, one button {r.ShowOneButton} \"{r.OpenLabel}\", selected {r.Selected}; {r.About}");
        Hold(1.2);
        if (ai.Engines.FirstOrDefault(r => r.Id == o.Provider) is not { } row)
        {
            Stop($"The notes step has no row for {Who}.");
            Hold(2);
            return false;
        }
        var button = manualView.GetVisualDescendants().OfType<Button>().First(b => b.DataContext == row && b.Command == row.SelectCommand);
        await PressAsync(button, $"picks {row.Name}", 0.3, 0.5);
        await WaitAsync(() => !ai.Busy && guided.Screen == GuidedScreen.Manual, "");
        Hold(0.8);
        if (guided.Screen != GuidedScreen.Manual)
        {
            Stop($"Picking {row.Name} went to guided setup's {guided.Screen} screen.");
            return false;
        }
        if (ai.SelectedNotes != o.Provider)
        {
            Stop($"{row.Name} can't be picked as it is. The step says: {ai.Say ?? "(nothing)"} The row says: {row.About} It offers \"{(row.ShowOneButton ? row.OpenLabel : "")}\"; \"{ai.ChoiceWords}\" stays picked.",
                $"Stops at Notes: {row.Name} can't be picked without \u201c{row.OpenLabel}\u201d");
            Hold(3);
            // Could it write notes as this computer has it? The library's own check says (a real one-word answer).
            if (o.Provider == "ollama")
            {
                var cc = host.Client();
                var said = await new AiRemote(cc.ServerUrl, cc.PoolKey).CheckAsync("ollama");
                outcome.Add($"library check of ollama as it is (no model downloaded): {said?.Said}; row state after: {said?.Overview?.Engines.FirstOrDefault(e => e.Id == "ollama")?.State}");
            }
            return false;
        }
        return true;
    }

    /// <summary>After Open Syft Notes: what the library now writes notes with, and the library's own Check of that AI
    /// (a one-word answer through the same command its notes use).</summary>
    async Task CheckLibraryAsync()
    {
        var cc = host.Client();
        var ai = new AiRemote(cc.ServerUrl, cc.PoolKey);
        var overview = await ai.EnginesAsync();
        string notes = overview?.Notes ?? "?";
        bool on = await NotesOnAsync() ?? false;
        outcome.Add($"library notes engine: {notes}; notes writing on: {on}");
        Log($"library: notes by {notes}, writing on {on}");
        if (notes != o.Provider || !on)
        {
            Stop($"Setup finished, but the library writes notes with {Engines.Name(notes)} (on: {on}), not {Who}.");
            return;
        }
        Caption($"The library checks {Who} (real)");
        var check = ai.CheckAsync(notes);
        while (!check.IsCompleted) { Frame("wait"); await Task.Delay(400); }
        var said = await check;
        outcome.Add($"library check of {notes}: {said?.Said}");
        string state = said?.Overview?.Engines.FirstOrDefault(e => e.Id == notes)?.State ?? "?";
        result = state == "ready"
            ? $"Done: {Who} writes the notes, and the library's check says \"{said?.Said}\""
            : $"Setup finished with {Who} for notes, but the library's check says: {Py.Head(said?.Said ?? "nothing", 300)}";
        shortResult = state == "ready" ? $"Done \u00b7 notes by {Who} \u00b7 the library's check: \u201c{said?.Said}\u201d" : $"Finished, but the library's check of {Who} failed";
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            window.Close();
        }
        catch (InvalidOperationException)
        {
        }
        mic.Close();
        mic.Dispose();
        guided.Dispose();
        if (door is not null) await door.DisposeAsync();
        host.StopDownload();
        if (host.LocalLibrary is { } svc) await svc.StopAsync();
        host.Dispose();
        log.Dispose();
        index.Dispose();
        if (Environment.GetEnvironmentVariable("STUDYSTASH_FILM_KEEP") != "1") home.Dispose();
    }
}

/// <summary>The microphone's permission on a first run: not asked, then allowed when asked.</summary>
sealed class FilmMicAccess : StudyStash.Audio.IMicPermissions
{
    bool asked;

    public StudyStash.Audio.MicAccess Access() => asked ? StudyStash.Audio.MicAccess.Allowed : StudyStash.Audio.MicAccess.NotAsked;

    public Task<StudyStash.Audio.MicAccess> AskAsync()
    {
        asked = true;
        return Task.FromResult(StudyStash.Audio.MicAccess.Allowed);
    }
}

/// <summary>The student's voice for the microphone's check: a tone at whatever loudness the film plays.</summary>
sealed class FilmMic : IAudioSource
{
    public string Name => "Film microphone";
    public int SampleRate => Sound.Rate;
    public int Channels => 1;
    public event Action<float[]>? Samples;
    public event Action<string>? Failed { add { } remove { } }
    double phase;

    public void Start()
    {
    }

    public void Stop()
    {
    }

    public void Dispose()
    {
    }

    public void Play(double seconds, double amplitude)
    {
        var buf = new float[(int)(seconds * SampleRate)];
        for (int i = 0; i < buf.Length; i++) buf[i] = (float)(amplitude * Math.Sin(phase += 2 * Math.PI * 220 / SampleRate));
        Samples?.Invoke(buf);
    }
}
