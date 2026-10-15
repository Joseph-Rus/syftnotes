using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using StudyStash.App.Controls.Rich;
using StudyStash.App.Services;
using StudyStash.App.ViewModels;
using StudyStash.App.Windows;
using StudyStash.Core;

namespace StudyStash.App;

// THROWAWAY (memory soak, branch memory-soak-0.16, never merged). STUDYSTASH_SOAK=<rounds>: after the self-test has set
// the app up and filed its first lecture, everything a student does is done in rounds, and after each round what's left
// in memory is measured: the managed heap after full collections, the process's footprint (Activity Monitor's Memory on
// a Mac; private bytes on Windows), and which of the app's own windows, views and view models are still alive.
//   STUDYSTASH_SOAK_QUIET=1   on someone's own screen: nothing takes the keyboard, nothing pictures the screen
//   STUDYSTASH_SOAK_PAUSE=1   pauses after the warm-up (pause-A) and after the rounds (pause-B) until go-A / go-B appear
//                             beside soak.txt: a heap snapshot (dotnet-gcdump) is taken meanwhile
//   STUDYSTASH_SOAK_ONLY=a,b  only those actions in each round (settings, setup, canvas, pages, larger, edit, ask,
//                             record, windows)
//   STUDYSTASH_SOAK_EACH=<n>  then each action on its own, n times, measured before and after
public static partial class SelfTest
{
    // --- what's alive -----------------------------------------------------------------------------------------------

    static readonly List<WeakReference> tracked = [];
    static readonly ConditionalWeakTable<object, object> seenOnce = new();
    static readonly object mark = new();

    static bool Ours(object o) => o.GetType().Namespace?.StartsWith("StudyStash", StringComparison.Ordinal) == true;

    static void Track(object? o)
    {
        if (o is null || seenOnce.TryGetValue(o, out _)) return;
        seenOnce.Add(o, mark);
        tracked.Add(new WeakReference(o));
    }

    static bool tracking;

    static void StartTracking()
    {
        if (tracking) return;
        tracking = true;
        Control.LoadedEvent.AddClassHandler<Control>((c, _) =>
        {
            if (Ours(c)) Track(c);
            if (c.DataContext is { } d && Ours(d)) Track(d);
        }, handledEventsToo: true);
        StyledElement.DataContextProperty.Changed.AddClassHandler((StyledElement s, AvaloniaPropertyChangedEventArgs _) =>
        {
            if (s.DataContext is { } d && Ours(d)) Track(d);
        });
        Window.WindowOpenedEvent.AddClassHandler<Window>((w, _) => Track(w), handledEventsToo: true);
    }

    [DllImport("libproc.dylib")]
    static extern int proc_pid_rusage(int pid, int flavor, IntPtr buffer);

    /// <summary>A Mac: what Activity Monitor shows as Memory (the physical footprint). Windows: private bytes (what the
    /// app has committed, Task Manager's Commit size). Bytes.</summary>
    static long Footprint()
    {
        if (OperatingSystem.IsWindows())
        {
            using var p = Process.GetCurrentProcess();
            return p.PrivateMemorySize64;
        }
        if (!OperatingSystem.IsMacOS()) return Environment.WorkingSet;
        IntPtr buffer = Marshal.AllocHGlobal(2048);
        try
        {
            return proc_pid_rusage(Environment.ProcessId, 2, buffer) == 0 ? Marshal.ReadInt64(buffer, 72) : -1;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    sealed record Reading(long Heap, long Foot, long Set, Dictionary<string, int> Alive);

    static async Task<Reading> MeasureAsync()
    {
        for (int i = 0; i < 4; i++)
        {
            await Task.Delay(350); // the dispatcher lets go of what it was holding for the last frame
            // The large objects' heap too: left as it is, its holes count as heap (40 MB of them, once, after setup).
            System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
        }
        GC.Collect();
        long heap = GC.GetTotalMemory(forceFullCollection: true);
        return new Reading(heap, Footprint(), Environment.WorkingSet, CountAlive());
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static Dictionary<string, int> CountAlive()
    {
        tracked.RemoveAll(w => !w.IsAlive);
        var alive = new Dictionary<string, int>();
        foreach (var w in tracked)
            if (w.Target is { } o)
            {
                string n = o is Window win && !Ours(o) ? $"Window(\"{win.Title}\")" : o.GetType().Name;
                alive[n] = alive.GetValueOrDefault(n) + 1;
            }
        return alive;
    }

    static string Mb(long bytes) => (bytes / 1048576.0).ToString("0.0");

    static string FootWord => OperatingSystem.IsWindows() ? "private" : "footprint";

    static void SoakSay(string line)
    {
        Say(line);
        File.AppendAllText(Path.Combine(Dir!, "soak.txt"), line + "\n");
    }

    /// <summary>What grew from <paramref name="from"/> to <paramref name="to"/>, by the app's own types.</summary>
    static string Grew(Reading from, Reading to, int least = 1) =>
        to.Alive.Select(kv => (Name: kv.Key, By: kv.Value - from.Alive.GetValueOrDefault(kv.Key)))
            .Where(t => t.By >= least).OrderByDescending(t => t.By).Take(20).Select(t => $"{t.Name} +{t.By}").DefaultIfEmpty("nothing").Aggregate((a, b) => a + ", " + b);

    static async Task<double> IdleCpuAsync(double seconds)
    {
        using var p = Process.GetCurrentProcess();
        var cpu = p.TotalProcessorTime;
        var clock = Stopwatch.StartNew();
        await Wait(seconds);
        p.Refresh();
        return (p.TotalProcessorTime - cpu).TotalSeconds / clock.Elapsed.TotalSeconds * 100;
    }

    /// <summary>Idles until <c>go-NAME</c> appears beside the results (a heap snapshot is being taken), five minutes at most.</summary>
    static async Task PauseAsync(string name)
    {
        if (Environment.GetEnvironmentVariable("STUDYSTASH_SOAK_PAUSE") != "1") return;
        File.WriteAllText(Path.Combine(Dir!, "pause-" + name), Environment.ProcessId.ToString());
        bool went = await Until(() => File.Exists(Path.Combine(Dir!, "go-" + name)), 300);
        SoakSay(went ? $"(snapshot {name} taken)" : $"(snapshot {name}: nobody took it in 5 minutes)");
    }

    static async Task Quietly(string what, Func<Task> go)
    {
        try
        {
            await go();
        }
        catch (Exception e)
        {
            SoakSay($"  {what}: didn't finish: {e.GetType().Name}: {e.Message}");
        }
    }

    /// <summary>Waits for <paramref name="task"/> at most <paramref name="seconds"/>; false when it's still going.</summary>
    static async Task<bool> Within(Task task, double seconds) => await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(seconds))) == task;

    // --- the run's own facts -----------------------------------------------------------------------------------------

    sealed class Soaking(AppHost host, string cls, string first)
    {
        public AppHost Host { get; } = host;
        /// <summary>The setup's class, with the first lecture in it: the folder page visited every round (it doesn't grow).</summary>
        public string Class { get; } = cls;
        /// <summary>The lecture setup recorded, with written notes, a flowchart and a plot.</summary>
        public string First { get; } = first;
        /// <summary>Another lecture, recorded at the start: opening one after the other swaps real pages.</summary>
        public string Second { get; set; } = first;
        /// <summary>The folder each round's recording goes in, never visited: what grows by design (one more note a round)
        /// stays out of the pages measured.</summary>
        public const string Recordings = "SOAK 100";
        public int Round { get; set; }
    }

    // --- one of each thing a student does ------------------------------------------------------------------------------

    static readonly (string Id, string What, Func<Soaking, Task> Once)[] Actions =
    [
        ("settings", "Settings: every page, Check on Your AI's free AI, closed on a different page each time", SettingsOnce),
        ("setup", "Setup again, by hand and then with an AI, closed", SetupOnce),
        ("canvas", "Connect Canvas and Which browser, opened and closed", CanvasOnce),
        ("pages", "Library: Home, Due, a folder, two lectures, a transcript, back and forward, delete and new-group asks cancelled", PagesOnce),
        ("larger", "A lecture's flowchart and plot opened larger, played, closed", LargerOnce),
        ("edit", "A lecture's notes edited and saved; an edit left for another lecture and come back to, then cancelled", EditOnce),
        ("ask", "Ask: a lecture's ask bar, Home's, the quick panel's search and question", AskOnce),
        ("record", "Record a short lecture: its page, notes typed, live words, the pill and its card, the recorder opened and asked, the dropdown, pause, stop, filed, deleted", RecordOnce),
        ("windows", "The library window closed to the menu bar and opened again, and a notification", WindowsOnce),
    ];

    static IEnumerable<(string Id, string What, Func<Soaking, Task> Once)> Chosen()
    {
        string? only = Environment.GetEnvironmentVariable("STUDYSTASH_SOAK_ONLY");
        if (only is not { Length: > 0 }) return Actions;
        var ids = only.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return Actions.Where(a => ids.Contains(a.Id));
    }

    static readonly string[] SettingsPagesClosedOn = ["AI", "Canvas", "Access", "Library", "Recording", "Phone", "General"];

    static async Task SettingsOnce(Soaking s)
    {
        Shell.ShowSettings();
        await Wait(0.8);
        if ((Shell.Windows.Settings?.Content as Control)?.DataContext is not SettingsModel sm) throw new InvalidOperationException("Settings didn't open");
        foreach (var page in sm.NavItems.Select(n => n.Id).ToList())
        {
            sm.Section = page;
            await Wait(0.35);
            if (page == "AI")
            {
                // Check on the free AI (the self-test's own engine stands in for Ollama). Never Claude or ChatGPT: on the
                // owner's Mac that could reach a real account.
                await Until(() => sm.Engines.Engines.Any(r => r.Id == "ollama"), 8);
                if (sm.Engines.Engines.FirstOrDefault(r => r.Id == "ollama") is { } free)
                {
                    await free.CheckCommand.ExecuteAsync(null);
                    await Until(() => !sm.Engines.Busy, 10);
                }
            }
        }
        sm.Section = SettingsPagesClosedOn[(s.Round % SettingsPagesClosedOn.Length + SettingsPagesClosedOn.Length) % SettingsPagesClosedOn.Length];
        await Wait(0.6);
        Shell.Windows.Settings?.Close();
        await Until(() => Shell.Windows.Settings is null, 5);
        await Wait(0.2);
    }

    static async Task SetupOnce(Soaking s)
    {
        Shell.RunSetupAgain(byHand: true);
        await Wait(1.2);
        Shell.Windows.Setup?.Close();
        await Until(() => Shell.Windows.Setup is null, 5);
        await Wait(0.3);
        Shell.ShowSetup(false);
        await Until(() => Shell.Windows.Guided is { Steps.Count: > 0 }, 5);
        await Wait(1.2);
        Shell.Windows.Setup?.Close();
        await Until(() => Shell.Windows.Setup is null, 5);
        await Wait(0.3);
    }

    static async Task CanvasOnce(Soaking s)
    {
        Shell.ShowCanvasConnect();
        await Wait(1.2);
        Shell.Windows.CanvasConnect?.Close();
        await Until(() => Shell.Windows.CanvasConnect is null, 5);
        await Wait(0.3);
        Shell.Soak.ChooseBrowser("Chrome", "Edge");
        await Until(() => Shell.Soak.BrowserChoice is { IsVisible: true }, 5);
        await Wait(0.6);
        Shell.Soak.BrowserChoice?.Close();
        await Until(() => Shell.Soak.BrowserChoice is null, 5);
        await Wait(0.3);
    }

    /// <summary>The sidebar's own row for a folder (or Home), clicked.</summary>
    static async Task SidebarAsync(Func<ClassItem, bool> which, Func<Task> otherwise)
    {
        var lib = Shell.Soak.Library;
        var item = which(lib.Home) ? lib.Home : lib.Classes.FirstOrDefault(which);
        if (item is not null && lib.OnClass is { } click) click(item);
        else await otherwise();
        await Wait(0.6);
    }

    static async Task PagesOnce(Soaking s)
    {
        var lib = Shell.Soak.Library;
        Shell.ShowLibrary();
        await Wait(0.5);
        await SidebarAsync(c => c.IsHome, Shell.Soak.Home);
        await SidebarAsync(c => c.IsDue, Shell.Soak.Due);
        await SidebarAsync(c => !c.IsHome && !c.IsDue && c.Name == s.Class, () => Shell.Soak.Folder(s.Class));
        Shell.Windows.OpenLecture(s.First);
        await Wait(0.8);
        Shell.Windows.OpenLecture(s.First, transcript: true);
        await Wait(0.5);
        Shell.Windows.OpenLecture(s.Second);
        await Wait(0.8);
        lib.GoBackCommand.Execute(null);
        await Wait(0.6);
        lib.GoBackCommand.Execute(null);
        await Wait(0.6);
        lib.GoForwardCommand.Execute(null);
        await Wait(0.6);
        // Delete this lecture? No. A new group for a folder? Not now.
        lib.DeleteCommand.Execute(null);
        await Wait(0.3);
        lib.CancelDeleteCommand.Execute(null);
        lib.NamingFolder = new NewFolderAsk(s.Class);
        await Wait(0.3);
        lib.CancelFolderCommand.Execute(null);
        await SidebarAsync(c => c.IsHome, Shell.Soak.Home);
    }

    static async Task OpenFirstAsync(Soaking s, Func<Window, bool> ready, double seconds = 20)
    {
        Shell.ShowLibrary();
        Shell.Windows.OpenLecture(s.First);
        await Until(() => Shell.Windows.Main is { } main && ready(main), seconds);
    }

    static async Task LargerOnce(Soaking s)
    {
        await OpenFirstAsync(s, main => main.GetVisualDescendants().OfType<DiagramView>().Any(d => d.Scene is not null)
                                         && main.GetVisualDescendants().OfType<PlotView>().Any());
        var main = Shell.Windows.Main!;
        if (main.GetVisualDescendants().OfType<DiagramView>().FirstOrDefault(d => d.Scene is not null) is { } chart)
        {
            chart.BringIntoView();
            chart.Focus();
            chart.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter, Source = chart });
            if (!await Until(() => DiagramWindow.Current is { IsVisible: true }, 5)) throw new InvalidOperationException("the flowchart didn't open larger");
            await Wait(0.6);
            DiagramWindow.Current?.Close();
            await Until(() => DiagramWindow.Current is null, 5);
            await Wait(0.2);
        }
        else throw new InvalidOperationException("no flowchart drawn");
        if (main.GetVisualDescendants().OfType<PlotView>().FirstOrDefault() is { } plot)
        {
            PlotWindow.Open(plot, main);
            await Wait(0.5);
            if (PlotWindow.Current?.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => (AutomationProperties.GetName(b) ?? "").StartsWith("Play", StringComparison.Ordinal)) is { } play)
            {
                play.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Wait(0.6);
            }
            PlotWindow.Current?.Close();
            await Until(() => PlotWindow.Current is null, 5);
            await Wait(0.2);
        }
        else throw new InvalidOperationException("no plot drawn");
    }

    static async Task EditOnce(Soaking s)
    {
        var lib = Shell.Soak.Library;
        await OpenFirstAsync(s, _ => lib.Notes is { LectureId: var id, CurrentMarkdown.Length: > 0 } && id == s.First);
        if (lib.Notes is not { } notes) throw new InvalidOperationException("the lecture's notes didn't load");
        await Until(() => notes.EditCommand.CanExecute(null), 5);
        notes.EditCommand.Execute(null);
        if (!notes.Editing) throw new InvalidOperationException("Edit didn't open the editor");
        // One line of the student's own, the one from the last round taken out: the notes stay the same size.
        var lines = notes.EditText.Split('\n').Where(l => !l.StartsWith("My own line", StringComparison.Ordinal)).ToList();
        lines.Add($"My own line, round {s.Round}.");
        notes.EditText = string.Join('\n', lines);
        await Wait(0.4);
        await notes.SaveEditCommand.ExecuteAsync(null);
        await Until(() => !notes.Editing && !notes.Busy, 10);
        if (notes.EditProblem.Length > 0) throw new InvalidOperationException($"Save: {notes.EditProblem}");
        await Wait(1.5); // the page is read again with them
        // Typing left unsaved while another lecture is opened: it waits for this one, and comes back with it.
        await OpenFirstAsync(s, _ => lib.Notes is { LectureId: var id, CurrentMarkdown.Length: > 0 } && id == s.First);
        if (lib.Notes is not { } again) throw new InvalidOperationException("the lecture's notes didn't load again");
        await Until(() => again.EditCommand.CanExecute(null), 5);
        again.EditCommand.Execute(null);
        again.EditText += "\nNot saved yet.";
        await Wait(0.4);
        Shell.Windows.OpenLecture(s.Second);
        await Wait(1.2);
        Shell.Windows.OpenLecture(s.First);
        await Until(() => lib.Notes is { Editing: true, LectureId: var id } && id == s.First, 10);
        if (lib.Notes is { Editing: true } back) back.CancelEditCommand.Execute(null);
        else throw new InvalidOperationException("the unsaved edit didn't come back");
        await Wait(2.5); // what was kept for it is written down, then let go
    }

    static async Task AskAsync(AiAskModel? ask, string question, string where)
    {
        if (ask is null) throw new InvalidOperationException($"no ask bar on {where}");
        await Until(() => !ask.Busy, 5);
        ask.Question = question;
        if (!await Within(ask.AskCommand.ExecuteAsync(null), 40)) ask.Stop();
        await Wait(0.3);
    }

    static async Task AskOnce(Soaking s)
    {
        var lib = Shell.Soak.Library;
        await OpenFirstAsync(s, _ => lib.Ask is { LectureId: var id } && id == s.First);
        await AskAsync(lib.Ask, "What is a base case, and why does recursion need one?", "the lecture");
        await SidebarAsync(c => c.IsHome, Shell.Soak.Home);
        await Until(() => lib.Overview is { IsHome: true, Ask: not null }, 5);
        await AskAsync(lib.Overview?.Ask, "What's on the midterm?", "Home");
        if (Shell.Windows.Quick is not { IsVisible: true }) Shell.Windows.ToggleQuick();
        await Wait(0.3);
        await Shell.Windows.Search("midterm");
        await Wait(0.3);
        await Shell.Windows.Search("recursion base case");
        await Wait(0.3);
        await Within(Shell.Soak.Ask("What is on the midterm, and what is a base case?"), 40);
        await Wait(0.3);
        Shell.Windows.Quick?.Hide();
        await Wait(0.2);
    }

    static async Task RecordOnce(Soaking s)
    {
        var host = s.Host;
        var lib = Shell.Soak.Library;
        var rec = Shell.Soak.Recorder;
        Shell.ShowLibrary();
        await SidebarAsync(c => c.IsHome, Shell.Soak.Home);
        // Record, as Home's own button does, into the soak's folder.
        await Shell.Soak.Record(Soaking.Recordings);
        if (host.Recorder.Current?.Id is not { } id) throw new InvalidOperationException("Record didn't begin");
        await Until(() => lib.Note is { Live: true }, 5);
        // Notes typed on its page while it records.
        if (lib.Note is { Live: true } page)
        {
            page.Typed = $"Base case first. Round {s.Round}.";
            await Wait(0.8);
            page.Typed += "\nThen the recursive step.";
        }
        // The live words come in (Cactus Whistle on Apple silicon, a small Whisper elsewhere).
        bool words = await Until(() => rec.Lines.Count > 0, 12);
        if (!words) SoakSay($"  record (round {s.Round}): no live words in 12 s");
        // The pill, as when the student is in another app, and its card under the pointer (a Mac).
        Shell.Windows.ShowRecorder(expanded: false);
        await Wait(0.6);
        rec.Hovered = true;
        await Wait(1.0);
        rec.Hovered = false;
        await Wait(0.3);
        // The recorder opened up, with a question about what's been said so far.
        Shell.Windows.ShowRecorder(expanded: true);
        await Wait(1.0);
        if (rec.Ask is { } live) await AskAsync(live, "What has the lecture said so far?", "the recorder");
        Shell.Windows.ShowRecorder(expanded: false);
        await Wait(0.5);
        // The dropdown, with the live line in it.
        Shell.Windows.TogglePanel();
        await Wait(0.8);
        Shell.Windows.Panel?.Hide();
        Shell.Windows.TogglePause();
        await Wait(0.6);
        Shell.Windows.TogglePause();
        await Wait(1.0);
        Shell.Soak.Stop();
        if (!await Until(() => host.Lectures.Get(id)?.State is LectureState.Filed or LectureState.Failed, 240))
            throw new InvalidOperationException($"the lecture wasn't filed in 4 minutes ({host.Lectures.Get(id)?.State})");
        await Wait(1.5);
        if (s.Round == 0) return; // the soak's second lecture stays
        // Deleted from its own page, as a student deletes a test recording: the library holds the same notes each round,
        // so Home's list doesn't grow, and the app drops its copy once the library's trash lets it go.
        await Until(() => lib.Note is { Live: false, Local: false } n && n.Id == id, 10);
        lib.DeleteCommand.Execute(null);
        if (lib.Deleting is null) throw new InvalidOperationException("Delete didn't ask about the recording");
        await Wait(0.4);
        await lib.ConfirmDeleteCommand.ExecuteAsync(null);
        await Wait(1.0);
    }

    static async Task WindowsOnce(Soaking s)
    {
        Shell.Soak.CloseMain();
        await Wait(0.5);
        Shell.ShowLibrary();
        await Wait(1.0);
        Shell.Toast($"Lecture filed ({s.Round})", "Recursion and the call stack is in CS 101.", "Open", () => { }, TimeSpan.FromSeconds(1));
        await Wait(1.8);
    }

    // --- the run ---------------------------------------------------------------------------------------------------

    static async Task RoundAsync(Soaking s, IReadOnlyList<(string Id, string What, Func<Soaking, Task> Once)> actions, Dictionary<string, double> took)
    {
        foreach (var (id, _, once) in actions)
        {
            var clock = Stopwatch.StartNew();
            await Quietly($"{id} (round {s.Round})", () => once(s));
            took[id] = took.GetValueOrDefault(id) + clock.Elapsed.TotalSeconds;
        }
    }

    /// <summary>The dropdown, the recorder and the library each shown for a moment, then measured: what a hidden window
    /// was holding only until it next drew isn't counted as kept.</summary>
    static async Task ShowEverythingAsync()
    {
        Shell.Windows.TogglePanel();
        await Wait(0.8);
        Shell.Windows.Panel?.Hide();
        Shell.Windows.ShowRecorder(expanded: true);
        await Wait(0.8);
        Shell.Windows.Recorder?.Hide();
        Shell.ShowLibrary();
        await Shell.Soak.Home();
        await Wait(1.5);
    }

    static async Task RunSoakAsync(AppHost host)
    {
        StartTracking();
        int n = int.TryParse(Environment.GetEnvironmentVariable("STUDYSTASH_SOAK"), out int asked) && asked > 0 ? asked : 12;
        var actions = Chosen().ToList();
        string first = host.Lectures.All().Where(l => l.State == LectureState.Filed).OrderBy(l => l.Started, StringComparer.Ordinal).First().Id;
        string cls = host.Lectures.Get(first)?.FiledClass is { Length: > 0 } c ? c : "CS 101";
        var s = new Soaking(host, cls, first);
        File.WriteAllText(Path.Combine(Dir!, "soak.txt"), "");
        SoakSay($"Study Stash {StudyStash.Core.Engine.Version}, {RuntimeInformation.OSDescription} {RuntimeInformation.ProcessArchitecture}, skin {Skin.Current}"
                + $"{(OneComputer ? ", just this computer" : ", a laptop with its library elsewhere")}, live words: {host.LiveEngine}{(Shell.Soak.Quiet ? ", quiet" : "")}");
        SoakSay($"{n} round(s) of: {string.Join("; ", actions.Select(a => a.Id))}");

        // The folder each round's recording goes in.
        if (host.Remote() is { } remote)
        {
            await Quietly("adding the soak's folder", async () =>
            {
                await remote.AddClassAsync(Soaking.Recordings);
                await host.CheckLibraryAsync();
            });
        }
        // A second lecture, so opening one after another swaps real pages (round 0: it's kept).
        s.Round = 0;
        await Quietly("the second lecture", () => RecordOnce(s));
        s.Second = host.Lectures.All().Where(l => l.State == LectureState.Filed && l.Id != first).OrderBy(l => l.Started, StringComparer.Ordinal).LastOrDefault()?.Id ?? first;
        SoakSay($"class '{cls}', lectures {first} and {s.Second}; recordings go in '{Soaking.Recordings}'");

        // Two rounds first: what's made once (compiled code, fonts, styles, caches, pooled buffers) isn't counted.
        var took = new Dictionary<string, double>();
        for (int w = 1; w <= 2; w++)
        {
            s.Round = -w;
            await RoundAsync(s, actions, took);
        }
        Shell.Soak.CloseMain();
        await Wait(5);
        var idle0 = await MeasureAsync();
        double cpu0 = await IdleCpuAsync(30);
        SoakSay($"warmed up (2 rounds, {took.Values.Sum():0} s): heap {Mb(idle0.Heap)} MB, {FootWord} {Mb(idle0.Foot)} MB, working set {Mb(idle0.Set)} MB, "
                + $"idle CPU in the menu bar {cpu0:0.00}% of a core");
        await ShowEverythingAsync();
        var start = await MeasureAsync();
        SoakSay($"start: heap {Mb(start.Heap)} MB, {FootWord} {Mb(start.Foot)} MB, working set {Mb(start.Set)} MB");
        SoakSay("  alive at the start: " + string.Join(", ", start.Alive.OrderByDescending(kv => kv.Value).Take(40).Select(kv => $"{kv.Key} {kv.Value}")));
        await PauseAsync("A");

        took.Clear();
        var last = start;
        var all = Stopwatch.StartNew();
        for (int r = 1; r <= n; r++)
        {
            s.Round = r;
            var clock = Stopwatch.StartNew();
            await RoundAsync(s, actions, took);
            double seconds = clock.Elapsed.TotalSeconds;
            var now = await MeasureAsync();
            SoakSay($"round {r} ({seconds:0} s): heap {Mb(now.Heap)} MB ({(now.Heap - last.Heap) / 1024.0:+0;-0} KB), {FootWord} {Mb(now.Foot)} MB ({(now.Foot - last.Foot) / 1048576.0:+0.0;-0.0}), "
                    + $"working set {Mb(now.Set)} MB; app objects up since the start: {Grew(start, now)}");
            last = now;
        }
        // Each window shown again, so a hidden one lets go of what it held only until it next drew.
        await ShowEverythingAsync();
        var end = await MeasureAsync();
        SoakSay($"after {n} rounds ({all.Elapsed.TotalMinutes:0.0} min): heap {Mb(start.Heap)} → {Mb(end.Heap)} MB = {(end.Heap - start.Heap) / 1024.0 / n:0} KB a round; "
                + $"{FootWord} {Mb(start.Foot)} → {Mb(end.Foot)} MB = {(end.Foot - start.Foot) / 1024.0 / n:0} KB a round; working set {Mb(start.Set)} → {Mb(end.Set)} MB");
        SoakSay($"  app objects up since the start (after {n} rounds): {Grew(start, end)}");
        SoakSay("  seconds a round, by action: " + string.Join(", ", took.Select(kv => $"{kv.Key} {kv.Value / n:0.0}")));
        await PauseAsync("B");

        // Each action on its own, measured before and after: which one keeps what.
        if (int.TryParse(Environment.GetEnvironmentVariable("STUDYSTASH_SOAK_EACH"), out int each) && each > 0)
            foreach (var (id, what, once) in actions)
            {
                var before = await MeasureAsync();
                for (int i = 0; i < each; i++)
                {
                    s.Round = 1000 + i;
                    await Quietly($"{id} (alone, {i + 1})", () => once(s));
                }
                var after = await MeasureAsync();
                SoakSay($"{id} ×{each} alone: heap {(after.Heap - before.Heap) / 1024.0 / each:+0;-0} KB each, {FootWord} {(after.Foot - before.Foot) / 1024.0 / each:+0;-0} KB each; "
                        + $"app objects up: {Grew(before, after)} ({what})");
            }

        Shell.Soak.CloseMain();
        await Wait(5);
        var idle1 = await MeasureAsync();
        double cpu1 = await IdleCpuAsync(30);
        SoakSay($"end, in the menu bar: heap {Mb(idle1.Heap)} MB, {FootWord} {Mb(idle1.Foot)} MB, working set {Mb(idle1.Set)} MB, idle CPU {cpu1:0.00}% of a core (was {cpu0:0.00}%)");
        SoakSay("  alive at the end: " + string.Join(", ", idle1.Alive.OrderByDescending(kv => kv.Value).Take(40).Select(kv => $"{kv.Key} {kv.Value}")));
        SoakSay($"local recordings kept by the app: {host.Lectures.All().Count}");
    }
}
