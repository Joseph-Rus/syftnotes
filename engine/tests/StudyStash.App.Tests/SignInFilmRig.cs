using System.Globalization;
using System.Net;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StudyStash.App.Services;
using StudyStash.App.ViewModels;
using StudyStash.App.Views;
using StudyStash.Core;
using StudyStash.Core.Ai;
using StudyStash.Core.Tests;
using StudyStash.Library;
using Ellipse = Avalonia.Controls.Shapes.Ellipse;
using Shape = Avalonia.Controls.Shapes.Path;

namespace StudyStash.App.Tests;

/// <summary>
/// The computer the sign-in films run on, as far as the AIs go: stand-ins for Claude Code, Codex and agy (FakeAgents),
/// each behind a small shim that writes down how it was started (what it was asked, and whether it ran in Syft Notes'
/// hidden terminal, with no window, or piped to Syft Notes) before handing over to the stand-in. They start signed out and installed nowhere
/// the app looks until the film says (<see cref="Install"/>), and they're found only through <see cref="Which"/>, never
/// the PATH, so nothing here can reach the real Claude Code, Codex or agy on this Mac, an account or a browser. `open` and
/// `osascript` on the PATH are refusers that write down any try: on a Mac that's how a Terminal window would come up.
/// </summary>
sealed class StandIns : IDisposable
{
    readonly TempHome dir = new();
    readonly string oldPath;

    /// <summary>Where the app and the library find the CLIs (once installed).</summary>
    public string Bin => dir["bin"];
    public string this[string name] => dir[name];
    /// <summary>A stand-in's own folder: its sign-in (<c>signed-in</c>), <c>logins</c>, <c>login-delay</c> and replays.</summary>
    public string State(string id) => dir[$"cli-{id}"];
    string Staged(string id) => dir[$"staged-{id}"];
    string Launches => dir["launches.tsv"];
    string Refused => dir["refused.tsv"];
    /// <summary>Syft Notes' own terminal doors (guided setup's and the library's), each time one was called.</summary>
    public int TerminalDoors;
    /// <summary>Pages Syft Notes asked the system to open (none of the sign-ins here do: the provider's CLI opens it).</summary>
    public List<string> Pages { get; } = [];

    public StandIns()
    {
        Directory.CreateDirectory(Bin);
        File.WriteAllText(Launches, "");
        File.WriteAllText(Refused, "");
        foreach (string id in new[] { "claude", "codex", "gemini" })
        {
            string real = FakeAgents.WriteCli(State(id), id);
            Delay(id, 5);
            Directory.CreateDirectory(Staged(id));
            WriteShim(Path.Combine(Staged(id), FakeAgents.BinaryOf(id)), id, real);
        }
        string refusers = dir["refusers"];
        Directory.CreateDirectory(refusers);
        foreach (string name in new[] { "open", "osascript" })
        {
            string path = Path.Combine(refusers, name);
            File.WriteAllText(path, $"#!/bin/sh\nprintf '%s\\t%s\\n' '{name}' \"$*\" >> '{Refused}'\nexit 1\n");
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        oldPath = Environment.GetEnvironmentVariable("PATH") ?? "";
        Environment.SetEnvironmentVariable("PATH", refusers + Path.PathSeparator + oldPath);
    }

    /// <summary>The shim: it writes down the call, and Claude's turns, signed out, stop the way the real one's do.</summary>
    void WriteShim(string path, string id, string real)
    {
        string name = FakeAgents.BinaryOf(id);
        string signedOut = id == "claude"
            ? $$"""
                if [ "$1" = -p ] && [ ! -f '{{State(id)}}/signed-in' ]; then
                  cat > /dev/null
                  echo '{"type":"result","is_error":true,"result":"Not logged in · Please run /login","session_id":"s0"}'
                  exit 1
                fi
                """
            : "";
        File.WriteAllText(path, $$"""
            #!/bin/sh
            case "$1" in
              auth|login) what="$1${2:+ $2}";;
              exec) what="exec (a turn)";;
              *) what="$1";;
            esac
            for a in "$@"; do [ "$a" = -p ] && what="-p (a turn)"; done
            out=piped; [ -t 1 ] && out=hidden
            printf '%s\t%s\t%s\n' '{{name}}' "$what" "$out" >> '{{Launches}}'
            {{signedOut}}
            exec '{{real}}' "$@"

            """.Replace("\r\n", "\n"));
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    public string Exe(string id) => Path.Combine(Bin, FakeAgents.BinaryOf(id));
    public void Install(string id) => File.Copy(Path.Combine(Staged(id), FakeAgents.BinaryOf(id)), Exe(id), overwrite: true);
    public Func<string, string?> Which => FakeAgents.Which(Bin);
    public void Delay(string id, int seconds) => File.WriteAllText(Path.Combine(State(id), "login-delay"), seconds.ToString(CultureInfo.InvariantCulture));
    public void LoginFails(string id, bool on)
    {
        string f = Path.Combine(State(id), "login-fails");
        if (on) File.WriteAllText(f, "");
        else File.Delete(f);
    }
    public void SignOut(string id) => File.Delete(Path.Combine(State(id), "signed-in"));
    public bool SignedIn(string id) => File.Exists(Path.Combine(State(id), "signed-in"));
    public int Logins(string id) => FakeAgents.Logins(State(id));
    public void Replay(string id, IEnumerable<string> lines, int turn = 0) => FakeAgents.Replay(State(id), lines, turn);

    /// <summary>Its maker's installer, standing in: it copies the stand-in into <see cref="Bin"/>.</summary>
    public AgentInstall Installer(AgentCliInfo cli, string log) =>
        new(cli, false, log, FakeAgents.Installer(dir[$"installer-{cli.Id}"], cli.Id, "ok", Staged(cli.Id), Bin), Which) { Elevated = () => false };

    /// <summary>What was started, in order of first start: (command, how many times, how its output went).</summary>
    public IReadOnlyList<(string Command, int Times, string Output)> Record()
    {
        var rows = new List<(string Command, int Times, string Output)>();
        string[] lines;
        try
        {
            using var reader = new StreamReader(new FileStream(Launches, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete));
            lines = reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        }
        catch (IOException)
        {
            return rows;
        }
        foreach (string line in lines)
        {
            string[] f = line.Split('\t');
            if (f.Length < 3) continue;
            string command = $"{f[0]} {f[1]}";
            int at = rows.FindIndex(r => r.Command == command);
            if (at < 0) rows.Add((command, 1, f[2]));
            else rows[at] = (command, rows[at].Times + 1, rows[at].Output == "hidden" || f[2] == "hidden" ? "hidden" : "piped");
        }
        return rows;
    }

    /// <summary>Every try at `open` or `osascript` (none, in every film).</summary>
    public int RefusedTries => File.ReadAllLines(Refused).Count(l => l.Length > 0);

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("PATH", oldPath);
        if (Environment.GetEnvironmentVariable("STUDYSTASH_FILM_KEEP") != "1") dir.Dispose();
    }
}

/// <summary>
/// A real library, built as <c>serve</c> builds it (its pages and API, the pipeline that writes notes, the AIs' sign-ins
/// and the "signed in again" requeue), run in this process on a free loopback port (never 8787), in a throwaway home.
/// Its AIs are the stand-ins: found through <see cref="StandIns.Which"/>, and their turns run through the providers'
/// own classes pointed at the stand-ins (<see cref="AiProvider.At"/>), never at a command on the PATH. Its terminal door
/// (the last resort) only counts.
/// </summary>
sealed class FilmLibrary : IAsyncDisposable
{
    public const string Password = "film-pass";
    readonly WebApplication app;
    readonly CancellationTokenSource stop;
    readonly Task working;

    public Config Cfg { get; }
    public Store Store { get; }
    public AiJobs Ai { get; }
    public Pipeline Pipeline { get; }
    public string Url { get; }
    public AiRemote Remote => new(Url, Password);
    public RemoteLibrary Lib => new(Url, Password);

    FilmLibrary(WebApplication app, Config cfg, Store store, AiJobs ai, Pipeline pipeline, string url, CancellationTokenSource stop, Task working)
    {
        this.app = app;
        this.stop = stop;
        Cfg = cfg;
        Store = store;
        Ai = ai;
        Pipeline = pipeline;
        Url = url;
        this.working = working;
    }

    /// <summary>The stand-in notes, as an AI writes them (the words are the film's).</summary>
    public const string Notes = """
        ## Summary
        The cardiac cycle is one heartbeat: the atria and ventricles contract (systole) and relax (diastole) in a fixed order, and the four valves open and close on pressure alone. Cardiac output is how much blood the heart pumps each minute.

        ## Key points
        - Five phases, always in the same order: atrial systole, isovolumetric contraction, ventricular ejection, isovolumetric relaxation, ventricular filling.
        - **S1** ("lub") is the AV valves closing as the ventricles contract; **S2** ("dub") is the semilunar valves closing as they relax.
        - Most of ventricular filling is passive: atrial systole only tops it up (about 20%).
        - **CO = HR × SV**: about 70 beats a minute × 70 mL ≈ 5 L/min at rest.

        ## Definitions
        - **Systole**: the contraction phase of a chamber.
        - **Diastole**: the relaxation and filling phase.
        - **Stroke volume (SV)**: blood ejected by one ventricle per beat (EDV − ESV).

        ## Questions to review
        1. Why do the valves need no muscle to open and close?
        2. Which heart sound marks the start of ventricular systole?
        3. A heart rate of 80 and a stroke volume of 60 mL: what's the cardiac output?
        """;

    public static async Task<FilmLibrary> StartAsync(StandIns pc, Action<AiSettings>? settings = null, TimeSpan? patience = null)
    {
        var cfg = new Config(pc["library"], pc["library-notes"]) { PoolPassword = Password, PoolName = "Sam's library" };
        Directory.CreateDirectory(cfg.Home);
        var s = new AiSettings { Fallback = false };
        settings?.Invoke(s);
        s.Save(cfg.Home);
        var store = new Store(cfg.DbPath, cfg.PoolDir);
        var checks = EngineChecks.Off with
        {
            Which = pc.Which,
            OpenSignIn = (_, _, _) =>
            {
                Interlocked.Increment(ref pc.TerminalDoors);
                return "The film opens no terminal.";
            },
        };
        var ai = new AiJobs(cfg.Home)
        {
            Checks = checks, SignInPatience = patience ?? TimeSpan.FromMinutes(10),
            Providers = id => id switch
            {
                "claude" => new ClaudeProvider { At = pc.Exe("claude"), RefreshWait = TimeSpan.Zero, SignedOutWait = TimeSpan.Zero, Gate = new SignInGate() },
                "codex" => new CodexProvider { At = pc.Exe("codex") },
                "gemini" => new GeminiProvider { At = pc.Exe("gemini") },
                // The free AI isn't on this computer: nothing here ever reaches a real Ollama.
                _ => new ScriptedAi(id) { IsAvailable = false, Script = [new("error", "Ollama isn't on this computer.")] },
            },
        };
        var pipeline = new Pipeline(cfg, store, (_, _, _) => Task.FromResult("""{"class_name": "", "confidence": 0.3, "lecture_title": "The Cardiac Cycle", "topics": ["cardiac cycle"]}"""),
            ai.SummarizeAsync, _ => { }, notesModel: () => ai.Describe("notes", cfg));
        // As the library wires it (Cli): an AI signed in again writes the notes it couldn't meanwhile.
        ai.SignedInAgain += id =>
        {
            if (store.RequeueSignedIn(Engines.Name(id)) > 0) pipeline.Wake();
        };
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        WebHostBuilderKestrelExtensions.ConfigureKestrel(builder.WebHost, k => k.Listen(IPAddress.Loopback, 0));
        var app = LibraryWeb.Build(builder, cfg, store, pipeline, new LibraryWebOptions
        {
            ListModels = _ => Task.FromResult<List<(string, double)>?>(null), Tailscale = () => new TailscaleInfo(),
            Latest = _ => Task.FromResult<Release?>(null), Ai = ai, RamGb = () => 16, HostName = () => "sams-mac",
        });
        await app.StartAsync();
        string url = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        if (new Uri(url).Port is 8787 or 8788) throw new InvalidOperationException("never 8787: that's the owner's real library");
        var stop = new CancellationTokenSource();
        return new FilmLibrary(app, cfg, store, ai, pipeline, url, stop, pipeline.Start(stop.Token));
    }

    /// <summary>A recording, sent in the way the app sends one: the library files it (or writes its notes).</summary>
    public void Record(string id, string title, string date)
    {
        Store.Enqueue(new Meeting(id) { Title = title, Date = date, Transcript = Transcript });
        Pipeline.Wake();
    }

    /// <summary>Notes on or off, as the app's own WriteNotesAsync sets them.</summary>
    public async Task<bool> WriteNotesAsync(bool on) =>
        await Lib.SettingsAsync(HttpMethod.Post, "", new JsonObject { ["notes"] = new JsonObject { ["write"] = on, ["sort"] = on } }) is not null;

    /// <summary>What the lecturer said (long enough for notes: the library writes none under 1,500 characters).</summary>
    const string Transcript = """
        [00:00:04] Okay, let's get started. Today is the cardiac cycle: what happens in one heartbeat, in order, from the moment the atria contract to the moment the ventricles have filled again and the whole thing starts over.
        [00:01:12] There are five phases and they always go in the same order, so this is one to know for the midterm. Atrial systole, isovolumetric contraction, ventricular ejection, isovolumetric relaxation, and ventricular filling.
        [00:03:40] The valves don't have muscle. They open and close because of pressure differences, nothing else. When the pressure behind a valve is higher than the pressure in front of it, it opens; when that flips, it shuts.
        [00:05:15] Most of the filling of the ventricles is passive. Blood just flows in from the atria while everything is relaxed. Atrial systole only tops it up at the end, about twenty percent or so.
        [00:07:55] First heart sound, S1, the lub, is the AV valves closing as the ventricles start to contract. S2, the dub, is the semilunar valves closing as the ventricles relax. If you remember which valves close, you remember which sound is which.
        [00:10:30] During isovolumetric contraction all four valves are closed. The ventricles are squeezing but the volume doesn't change, so the pressure climbs fast until it beats the pressure in the aorta and the pulmonary trunk.
        [00:13:05] Then the semilunar valves open and we get ejection. Stroke volume is end diastolic volume minus end systolic volume, roughly seventy millilitres at rest.
        [00:15:20] Cardiac output is heart rate times stroke volume. At rest that's about seventy beats a minute times seventy millilitres, so about five litres a minute. Try one with a heart rate of eighty and a stroke volume of sixty for practice.
        """;

    public async ValueTask DisposeAsync()
    {
        stop.Cancel();
        Ai.SignIns.Dispose();
        await app.StopAsync();
        await app.DisposeAsync();
        try
        {
            await working.WaitAsync(TimeSpan.FromSeconds(10));
        }
        catch (Exception e) when (e is TimeoutException or OperationCanceledException)
        {
        }
        Store.Dispose();
    }
}

/// <summary>
/// The film's frame: a desk (a flat grey, as the computer's screen) with the app's windows on it, drawn by the app's own
/// views with their own chrome; a pointer drawn where the scripted student clicks; a caption bar along the bottom; and,
/// down the right, the test's own record of what Syft Notes started (from <see cref="StandIns.Record"/>), drawn in the
/// test's look, not the app's. Each frame goes to <c>STUDYSTASH_SHOTS/&lt;name&gt;/</c> with frames.tsv (act, hold or
/// wait: the GIF maker squeezes the waits).
/// </summary>
sealed class Desk : IDisposable
{
    public const double DeskWidth = 1040, DeskHeight = 900, PanelWidth = 300, CaptionHeight = 44;

    readonly StandIns pc;
    readonly Window window;
    readonly Panel desk = new() { Width = DeskWidth, Height = DeskHeight, ClipToBounds = true };
    readonly Border main = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    readonly Border overlay = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 40, 0, 0) };
    readonly Border corner = new() { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 18, 18, 0) };
    readonly Canvas over = new() { IsHitTestVisible = false };
    readonly Shape pointer = new()
    {
        Data = Geometry.Parse("M0,0 L0,17 L4.6,13 L7.6,20 L10.4,18.8 L7.4,12 L13,12 Z"), Fill = Brushes.Black, Stroke = Brushes.White, StrokeThickness = 1.2,
    };
    readonly Ellipse ring = new() { Width = 26, Height = 26, Stroke = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0)), StrokeThickness = 3, IsVisible = false };
    readonly TextBlock captionLeft = new() { Foreground = new SolidColorBrush(Color.Parse("#B9BEC7")), FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    readonly TextBlock captionRight = new()
    {
        Foreground = new SolidColorBrush(Color.Parse("#F5C451")), FontSize = 14, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center,
        TextAlignment = TextAlignment.Right, TextTrimming = TextTrimming.CharacterEllipsis,
    };
    readonly StackPanel record = new() { Spacing = 7 };
    readonly TextBlock terminals = new() { FontSize = 13, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
    readonly TextBlock doors = new() { FontSize = 11, TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.Parse("#9AA0AA")) };
    readonly StreamWriter index;
    readonly string dir;
    int frames;
    Point at;
    string recordShown = "";

    public Desk(string name, StandIns pc, ThemeVariant variant, string what, string? note = null)
    {
        this.pc = pc;
        dir = Path.Combine(Shot.Dir, name);
        Directory.CreateDirectory(dir);
        foreach (string old in Directory.EnumerateFiles(dir, "f-*.png")) File.Delete(old);
        index = new StreamWriter(Path.Combine(dir, "frames.tsv"), append: false) { AutoFlush = true };
        bool dark = variant == ThemeVariant.Dark;
        desk.Background = new SolidColorBrush(Color.Parse(dark ? "#2B2E33" : "#D9DCE1"));
        desk.Children.Add(main);
        desk.Children.Add(overlay);
        desk.Children.Add(corner);
        over.Children.Add(ring);
        over.Children.Add(pointer);

        var mono = new FontFamily("Menlo, Consolas, monospace");
        var panel = new Border
        {
            Width = PanelWidth, Background = new SolidColorBrush(Color.Parse("#14161A")), Padding = new Thickness(18, 20, 16, 16),
            BorderBrush = new SolidColorBrush(Color.Parse("#F5C451")), BorderThickness = new Thickness(3, 0, 0, 0),
            Child = new DockPanel
            {
                Children =
                {
                    new StackPanel
                    {
                        Spacing = 4, [DockPanel.DockProperty] = Dock.Top, Margin = new Thickness(0, 0, 0, 16),
                        Children =
                        {
                            new TextBlock { Text = "TEST RECORD · NOT PART OF THE APP", FontSize = 10.5, FontWeight = FontWeight.Bold, Foreground = new SolidColorBrush(Color.Parse("#F5C451")) },
                            new TextBlock { Text = "What Syft Notes started", FontSize = 17, FontWeight = FontWeight.SemiBold, Foreground = Brushes.White },
                            new TextBlock
                            {
                                Text = "Each stand-in writes down how it was started: what it was asked, and whether it ran in Syft Notes' hidden terminal (no window) or piped to Syft Notes.",
                                FontSize = 11, TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.Parse("#9AA0AA")),
                            },
                            new TextBlock
                            {
                                Text = note ?? "", IsVisible = note is not null, Margin = new Thickness(0, 4, 0, 0),
                                FontSize = 11, TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.Parse("#F5C451")),
                            },
                        },
                    },
                    new StackPanel
                    {
                        Spacing = 6, [DockPanel.DockProperty] = Dock.Bottom,
                        Children = { new Border { Height = 1, Background = new SolidColorBrush(Color.Parse("#2E3238")), Margin = new Thickness(0, 0, 0, 8) }, terminals, doors },
                    },
                    new ScrollViewer { Content = record, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden },
                },
            },
        };
        foreach (var t in new[] { terminals }) t.FontFamily = mono;
        var caption = new Border
        {
            Height = CaptionHeight, Background = new SolidColorBrush(Color.Parse("#1D2025")), Padding = new Thickness(16, 0),
            Child = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,16,*"), Children = { captionLeft, captionRight } },
        };
        Grid.SetColumn(captionRight, 2);
        captionLeft.Text = $"OFF-SCREEN FILM · {what}";
        DockPanel.SetDock(caption, Dock.Bottom);
        DockPanel.SetDock(panel, Dock.Right);
        window = new Window
        {
            Width = DeskWidth + PanelWidth, Height = DeskHeight + CaptionHeight, RequestedThemeVariant = variant,
            Content = new DockPanel { Children = { caption, panel, new Panel { Children = { desk, over } } } },
        };
        Look.Apply(window);
        at = new Point(DeskWidth * 0.62, DeskHeight * 0.7);
        Place();
        window.Show();
        Settle();
    }

    public Window Window => window;

    /// <summary>The app's main window on the desk (its view draws its own chrome).</summary>
    public void Show(Control view)
    {
        main.Child = view;
        Settle();
    }

    /// <summary>A small window over it (Get Claude ready), or none.</summary>
    public void Over(Control? view)
    {
        overlay.Child = view;
        Settle();
    }

    /// <summary>A notification in the top right corner of the screen, or none.</summary>
    public void Toast(Control? toast)
    {
        corner.Child = toast;
        Settle();
    }

    public void Caption(string text)
    {
        captionRight.Text = text;
        Settle();
    }

    public void Settle()
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }

    void Place()
    {
        Canvas.SetLeft(pointer, at.X);
        Canvas.SetTop(pointer, at.Y);
        Canvas.SetLeft(ring, at.X - 13);
        Canvas.SetTop(ring, at.Y - 13);
    }

    public void NoPointer() => pointer.IsVisible = false;
    public void Pointer() => pointer.IsVisible = true;

    /// <summary>The test's record, from the stand-ins' own log, drawn again when it changed.</summary>
    void Refresh()
    {
        var rows = pc.Record();
        int refused = pc.RefusedTries, door = pc.TerminalDoors;
        string now = string.Join("|", rows.Select(r => $"{r.Command}:{r.Times}:{r.Output}")) + $"#{refused}#{door}";
        if (now == recordShown) return;
        recordShown = now;
        record.Children.Clear();
        var mono = new FontFamily("Menlo, Consolas, monospace");
        if (rows.Count == 0)
            record.Children.Add(new TextBlock { Text = "(nothing yet)", FontSize = 12, Foreground = new SolidColorBrush(Color.Parse("#6B717B")), FontFamily = mono });
        foreach (var (command, times, output) in rows)
        {
            bool usage = command.EndsWith(" -p=/usage", StringComparison.Ordinal);
            bool signIn = command.EndsWith(" auth login", StringComparison.Ordinal) || command == "codex login" || usage;
            var line = new StackPanel { Spacing = 1 };
            line.Children.Add(new TextBlock
            {
                Text = command + (times > 1 ? $"  ×{times}" : ""), FontSize = 12.5, FontFamily = mono, TextWrapping = TextWrapping.Wrap,
                Foreground = signIn ? new SolidColorBrush(Color.Parse("#F5C451")) : Brushes.White, FontWeight = signIn ? FontWeight.Bold : FontWeight.Normal,
            });
            line.Children.Add(new TextBlock
            {
                Text = (output == "hidden" ? "in Syft Notes' hidden terminal · no window" : "piped to Syft Notes · no terminal")
                    + (usage ? (times > 1 ? " · the sign-in, then the allowance check" : " · the sign-in") : signIn ? " · the sign-in" : ""), FontSize = 10.5,
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.Parse("#7FD18B")),
            });
            record.Children.Add(line);
        }
        // A Terminal window would come up only through `open` or `osascript`, or Syft Notes' old terminal door.
        int any = refused + door;
        terminals.Text = $"Terminal windows opened: {any}";
        terminals.Foreground = new SolidColorBrush(Color.Parse(any == 0 ? "#7FD18B" : "#FF6B6B"));
        doors.Text = $"open / osascript tried: {refused} · Syft Notes' terminal door called: {door}";
    }

    /// <summary>One frame: <paramref name="kind"/> is act (the student doing something), hold, or wait (sped up).</summary>
    public void Frame(string kind = "act")
    {
        Refresh();
        Settle();
        var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("nothing rendered");
        string name = $"f-{frames++:0000}.png";
        using (var file = File.Create(Path.Combine(dir, name))) frame.Save(file, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        index.WriteLine($"{name}\t{kind}");
    }

    /// <summary>The page <paramref name="target"/> is on scrolls until it sits <paramref name="top"/> below the page's top.</summary>
    public void ScrollTo(Control target, double top = 16, double seconds = 0.9)
    {
        Settle();
        var scroll = target.GetVisualAncestors().OfType<ScrollViewer>().First();
        double from = scroll.Offset.Y;
        double to = Math.Clamp(from + target.TranslatePoint(new Point(0, 0), scroll)!.Value.Y - top, 0, Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height));
        int n = Math.Max(1, (int)(seconds * 12));
        for (int i = 1; i <= n; i++)
        {
            double t = i / (double)n, eased = t * t * (3 - 2 * t);
            scroll.Offset = new Vector(scroll.Offset.X, from + (to - from) * eased);
            Frame("hold");
        }
    }

    /// <summary>The words <paramref name="text"/> on screen in <paramref name="root"/>.</summary>
    public static TextBlock Words(Control root, string text) =>
        root.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == text && t.IsEffectivelyVisible);

    /// <summary>The lecture's notes scroll by <paramref name="by"/> over <paramref name="seconds"/>.</summary>
    public void Scroll(double by, double seconds)
    {
        var scroll = window.GetVisualDescendants().OfType<ScrollViewer>().First(s => s.Name == "NoteScroll" && s.IsEffectivelyVisible);
        double from = scroll.Offset.Y;
        int n = Math.Max(1, (int)(seconds * 12));
        for (int i = 1; i <= n; i++)
        {
            double t = i / (double)n, eased = t * t * (3 - 2 * t);
            scroll.Offset = new Vector(0, from + by * eased);
            Frame("hold");
        }
    }

    public void Hold(double seconds)
    {
        for (int i = 0; i < Math.Max(1, (int)(seconds * 12)); i++) Frame("hold");
    }

    public void Glide(Control c, double fx = 0.5, double fy = 0.5, int steps = 10)
    {
        Settle();
        c.BringIntoView();
        Settle();
        if (c.TranslatePoint(new Point(c.Bounds.Width * fx, c.Bounds.Height * fy), desk) is not { } to) throw new InvalidOperationException($"can't find {c} on the desk");
        var from = at;
        for (int i = 1; i <= steps; i++)
        {
            double t = i / (double)steps, eased = t * t * (3 - 2 * t);
            at = new Point(from.X + (to.X - from.X) * eased, from.Y + (to.Y - from.Y) * eased);
            Place();
            window.MouseMove(at);
            Frame();
        }
    }

    /// <summary>A click where the pointer is, through the window: the view's own button and bindings do the rest.</summary>
    public void Click()
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
    }

    public void Press(Control c, double fx = 0.5, double fy = 0.5)
    {
        Glide(c, fx, fy);
        Hold(0.25);
        Click();
    }

    /// <summary>Lets the app, the library and the stand-ins work, a frame every so often, until <paramref name="done"/>
    /// (then a moment more); throws when it took longer than <paramref name="most"/>.</summary>
    public async Task UntilAsync(Func<bool> done, string what, TimeSpan? most = null, Func<string>? state = null)
    {
        var took = System.Diagnostics.Stopwatch.StartNew();
        var last = System.Diagnostics.Stopwatch.StartNew();
        Frame("wait");
        while (!done())
        {
            if (took.Elapsed > (most ?? TimeSpan.FromSeconds(60))) throw new TimeoutException($"waited {took.Elapsed.TotalSeconds:0} s for {what}{(state is null ? "" : ": " + state())}");
            await Task.Delay(50);
            Settle();
            if (last.ElapsedMilliseconds > 250)
            {
                Frame("wait");
                last.Restart();
            }
        }
        Frame("wait");
    }

    /// <summary>The button on screen under <paramref name="root"/> that <paramref name="which"/> picks.</summary>
    public static Button ButtonIn(Control root, Func<Button, bool> which) =>
        root.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.IsEffectivelyVisible && which(b)) ?? throw new InvalidOperationException("no such button on screen");

    public void Dispose()
    {
        index.Dispose();
        try
        {
            window.Close();
        }
        catch (InvalidOperationException)
        {
        }
    }
}

/// <summary>What the films share: the stand-ins, the app's host in a throwaway home, and the library page as the app
/// builds it from what the library says about a lecture.</summary>
static class FilmParts
{
    public static bool On => Environment.GetEnvironmentVariable("STUDYSTASH_FILM") == "1" && !OperatingSystem.IsWindows();

    /// <summary>The app's host in a throwaway home: the transcription model's download is held (never fetched), the
    /// microphone's permission is asked with no system prompt, login items only count.</summary>
    public static AppHost Host(string home)
    {
        var models = new Mirror();
        foreach (var m in StudyStash.Audio.WhisperModels.All) models.Hold.Add(m.File);
        Directory.CreateDirectory(home);
        return new AppHost(home, microphone: () => new FilmMic(), log: _ => { }, loginItems: new CountingLoginItems(),
            models: new ModelSetting(null, Mirror: "https://mirror.example/models"), http: new HttpClient(models), micPermissions: new FilmMicAccess());
    }

    /// <summary>The app pointed at the library, as making it does (LibraryHere.CreateAsync).</summary>
    public static void Connect(AppHost host, FilmLibrary lib)
    {
        var cc = host.Client();
        cc.ServerUrl = lib.Url;
        cc.PoolKey = FilmLibrary.Password;
        cc.PoolName = lib.Cfg.PoolName;
        if (cc.DisplayName.Length == 0) cc.DisplayName = "Sam";
        host.SaveClient(cc);
        host.Save(s => s.Role = AppRole.Both);
    }

    static string S(JsonNode? n) => n is JsonValue v && v.TryGetValue(out string? s) ? s ?? "" : "";

    /// <summary>The library window, as a first run leaves it (no classes yet).</summary>
    public static LibraryModel Library() => new() { Status = "Library on this Mac", DrawChrome = true, OnAddClass = () => { } };

    public static Control LibraryView(LibraryModel m, SkinKind skin) => skin == SkinKind.Mac
        ? new MacLibrary { DataContext = m, Width = 1000, Height = 830 }
        : new WinLibrary { DataContext = m, Width = 1000, Height = 830 };

    /// <summary>
    /// A lecture's page as Shell.ShowLectureAsync, ShowFreeAi and ShowLectureAi make it from the library's own answer:
    /// its notes, or why there are none (the library writing them, notes off, the AI needing a sign-in), the "Get study
    /// notes" card where no AI writes notes yet, and the notes' own model once they're written.
    /// </summary>
    public static async Task ShowLectureAsync(LibraryModel page, FilmLibrary lib, string id, Action<string>? getReady = null)
    {
        var l = (await lib.Lib.LectureAsync(id))!;
        string status = S(l["status"]), notes = S(l["notes"]), noNotes = S(l["no_notes"]);
        bool writing = status is "queued" or "working", heard = S(l["transcript"]).Trim().Length > 0;
        string? why = noNotes switch
        {
            Summarize.NoNotesShort => NoteModel.TooShort(Nouns.Lecture, heard, false),
            Summarize.NoNotesOff => NoteModel.NotesAreOff(Nouns.Lecture, heard),
            _ => null,
        };
        string cls = S(l["class"]) is { } c && c != Configs.Unsorted ? c : "";
        var note = new NoteModel
        {
            Id = id, ClassName = cls, Noun = Nouns.Lecture, When = "Friday 30 October at 9:30 AM, 50 min", Title = S(l["title"]), Markdown = notes,
            Writing = writing && why is null,
            Pending = writing ? why ?? "Your library is writing the notes for this lecture. They show here when they're done."
                : notes.Length == 0 && S(l["error"]) is { Length: > 0 } err ? err
                : why ?? (notes.Length == 0 ? "There are no notes for this lecture." : null),
        }.Titled();
        page.Overview = null;
        page.Home.Selected = false;
        page.Welcome = null;
        page.Note = note;
        bool offer = !writing && notes.Length == 0 && heard && noNotes != Summarize.NoNotesShort;
        page.FreeAi = offer ? new FreeAiModel(lib.Remote, id) { Offer = true, Choose = noNotes == Summarize.NoNotesOff, OnGetReady = getReady } : null;
        if (!writing && notes.Length > 0)
        {
            var model = new AiNotesModel(lib.Remote);
            await model.Load(id, notes, S(l["notes_model"]), S(l["updated"]));
            page.Notes = model;
        }
        else page.Notes = null;
    }

    /// <summary>The lecture's status in the library: queued, working, done.</summary>
    public static async Task<string> StatusAsync(FilmLibrary lib, string id) => S((await lib.Lib.LectureAsync(id))?["status"]);
}
