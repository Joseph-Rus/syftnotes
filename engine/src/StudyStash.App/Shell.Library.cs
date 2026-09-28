using System.Globalization;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using StudyStash.App.Controls.Rich;
using StudyStash.App.Services;
using StudyStash.App.ViewModels;
using StudyStash.App.Views;
using StudyStash.Core;
using StudyStash.Core.Rich;

namespace StudyStash.App;

/// <summary>The shell's library side: the full app's columns (a class's lectures, Canvas's Due and a linked class's
/// own page, a lecture with its notes and ask bar, an assignment), search, and moving lectures.</summary>
public static partial class Shell
{
    static string? openClass;
    static string? openLecture;
    static int searchTurn;
    /// <summary>Every load of the library window (the whole thing, or one class) takes the next turn: a load that's
    /// no longer the latest by the time it's back stops instead of clobbering what a later one already drew.</summary>
    static int libraryTurn;
    static bool libraryReloadQueued;

    /// <summary>A lecture was filed, or the library just came back: if its window is open, the class showing gets its
    /// new note within a second (not on every change in that second, just the last one).</summary>
    static void RequestLibraryReload()
    {
        if (libraryReloadQueued || mainWindow?.IsVisible != true) return;
        libraryReloadQueued = true;
        Avalonia.Threading.DispatcherTimer.RunOnce(() =>
        {
            libraryReloadQueued = false;
            if (quitting || mainWindow?.IsVisible != true) return;
            _ = dueOpen ? ShowDueAsync() : openClass is { } cls ? ShowClassAsync(cls) : Task.CompletedTask;
        }, TimeSpan.FromSeconds(1));
    }

    /// <summary>The library's classes changed in Settings: the library window (when open) lists them again, and the
    /// dropdown's class switcher follows.</summary>
    static void LibraryClassesChanged()
    {
        if (quitting) return;
        _ = mainWindow?.IsVisible == true ? LoadLibraryAsync() : host.CheckLibraryAsync();
    }

    static string S(JsonNode? n) => n is JsonValue v && v.TryGetValue(out string? s) ? s ?? "" : "";

    static DateTimeOffset? Date(string? s) =>
        DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var d) ? d : null;

    static IBrush DotFor(string? className)
    {
        int c = host.ColorOf(className ?? "");
        return c >= 0 ? Skin.ClassDot(c) : Brushes.Gray;
    }

    /// <summary>"Tue 23 Sep · 1 h 12 min".</summary>
    static string CardMeta(JsonObject l)
    {
        string day = Date(S(l["date"]))?.LocalDateTime.ToString("ddd d MMM", CultureInfo.InvariantCulture) ?? "";
        return l["seconds"] is JsonValue v && v.TryGetValue(out double s) ? $"{day} · {TimedText.Length(s)}" : day;
    }

    static async Task LoadLibraryAsync()
    {
        int turn = ++libraryTurn;
        await host.CheckLibraryAsync();
        if (turn != libraryTurn) return; // a later load (or an explicit class) has already taken over
        library.Classes.Clear();
        foreach (var (name, color, count) in host.Classes())
            library.Classes.Add(new ClassItem { Name = name, Dot = Skin.ClassDot(color), Count = count });
        // A class removed (in Settings, say) isn't left open.
        if (openClass is not null && openClass != Configs.Unsorted && library.Classes.All(c => c.Name != openClass)) openClass = null;
        if (host.Library == LibraryState.Connected && !host.OlderLibrary && canvasDue is null) await LoadCanvasAsync();
        if (turn != libraryTurn) return;
        UpdateDueItem();
        UpdateNextDue();
        int unsorted = host.Overview?["unsorted"]?.GetValue<int>() ?? 0;
        library.Unsorted.Count = unsorted;
        if (host.Library != LibraryState.Connected || host.OlderLibrary)
        {
            ShowLectureList();
            library.Groups.Clear();
            library.ClassTitle = "";
            library.ClassCount = "";
            library.Note = null;
            library.Empty = host.OlderLibrary ? "Your library runs an older Study Stash. It files your lectures, but update it to browse, search and ask here."
                : host.Library switch
            {
                LibraryState.NotSetUp => "Connect to your library in Settings to see your lectures here.",
                LibraryState.WrongPassword => "Your library's password changed. Sign in again in Settings.",
                _ => "Can't reach your library. Lectures you record wait on this computer until it's back.",
            };
            return;
        }
        if ((dueOpen || dueSelection is not null) && library.Classes.FirstOrDefault(c => c.IsDue) is not null)
        {
            await ShowDueAsync();
            return;
        }
        string pick = openClass ?? library.Classes.FirstOrDefault(c => c.Count > 0 && !c.IsDue)?.Name ?? library.Classes.FirstOrDefault(c => !c.IsDue)?.Name ?? Configs.Unsorted;
        await ShowClassAsync(pick);
    }

    static async Task ShowClassAsync(string name)
    {
        int turn = ++libraryTurn;
        if (openClass != name) allLectures = false;
        openClass = name;
        dueOpen = false;
        dueSelection = null;
        foreach (var c in library.Classes) c.Selected = c.Name == name && !c.IsDue;
        library.Unsorted.Selected = name == Configs.Unsorted;
        library.ClassTitle = name;
        if (host.Remote() is not { } lib) return;
        JsonArray list;
        try
        {
            list = await lib.LecturesAsync(name, 200, null);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or LibraryRefusedException)
        {
            if (turn == libraryTurn) library.Empty = "Can't reach your library right now.";
            return;
        }
        if (turn != libraryTurn) return; // a later class (or a whole reload) has already taken over
        library.ClassCount = $"{list.Count} lecture{(list.Count == 1 ? "" : "s")}";
        library.Groups.Clear();
        var lectures = list.OfType<JsonObject>().ToList();
        library.Empty = lectures.Count == 0 ? $"No lectures in {name} yet. Record one and it lands here." : null;
        var today = DateTime.Today;
        var weekStart = today.AddDays(-(((int)today.DayOfWeek + 6) % 7)); // Monday
        string GroupOf(JsonObject l)
        {
            var d = Date(S(l["date"]))?.LocalDateTime.Date ?? today;
            if (d >= weekStart) return "This week";
            if (d >= weekStart.AddDays(-7)) return "Last week";
            return d.ToString("MMMM yyyy", CultureInfo.InvariantCulture) is var m && d.Year == today.Year ? d.ToString("MMMM", CultureInfo.InvariantCulture) : m;
        }
        foreach (var g in lectures.GroupBy(GroupOf))
        {
            var group = new LectureGroup { Label = g.Key, First = library.Groups.Count == 0 };
            var items = g.ToList();
            for (int i = 0; i < items.Count; i++)
            {
                var l = items[i];
                bool writing = S(l["status"]) is "queued" or "working";
                group.Items.Add(new LectureCard
                {
                    Id = S(l["id"]), Title = S(l["title"]), Meta = CardMeta(l), Summary = writing ? "Writing notes…" : S(l["summary"]), ClassName = S(l["class"]),
                    Last = i == items.Count - 1 && ReferenceEquals(g.Key, lectures.GroupBy(GroupOf).Last().Key),
                });
            }
            library.Groups.Add(group);
        }
        // A class linked to Canvas gets its own page: its lectures, what's to hand in, modules, files, announcements.
        if (CanvasClassRow(name) is { } row && !allLectures)
        {
            var page = library.CanvasClass is { } open && openCanvasClass == name ? open : NewCanvasClass();
            openCanvasClass = name;
            var ctx = Canvas();
            page.SetLectures([.. lectures.Take(3).Select(l =>
            {
                string id = S(l["id"]);
                return new LectureRow(S(l["title"]), Date(S(l["date"])) is { } d ? CanvasWords.ShortDay(d, ctx.Clock.Zone) : "", () => OpenFromList(() => ShowLectureAsync(id)));
            })], lectures.Count);
            library.CanvasClass = page;
            library.List = LibraryList.CanvasClass;
            try
            {
                await page.LoadAsync(row);
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or CanvasLibraryException or System.Text.Json.JsonException or InvalidOperationException)
            {
            }
            if (turn != libraryTurn) return;
        }
        else ShowLectureList();
        string? pick = openLecture is not null && lectures.Any(l => S(l["id"]) == openLecture) ? openLecture : lectures.Select(l => S(l["id"])).FirstOrDefault();
        if (pick is not null) await ShowLectureAsync(pick);
        else
        {
            ClearDetail();
            library.Note = null;
        }
    }

    /// <summary>The class page's "All 12 lectures": this class's lectures by week, until the class is picked again.</summary>
    static bool allLectures;
    static string? openCanvasClass;

    static CanvasClassModel NewCanvasClass() => new(Canvas())
    {
        OnAssignment = (cls, id) => OpenFromList(() => ShowAssignmentAsync(cls, id)),
        OnReader = reader => OpenFromList(() =>
        {
            library.Assignment = null;
            library.Reader = reader;
            return Task.CompletedTask;
        }),
        OnAllLectures = () =>
        {
            allLectures = true;
            ShowLectureList();
        },
    };

    /// <summary>The middle column goes back to plain lectures by week (no Canvas list over it).</summary>
    static void ShowLectureList()
    {
        library.List = LibraryList.Lectures;
        library.CanvasClass = null;
        openCanvasClass = null;
    }

    /// <summary>The right column goes back to the lecture: no assignment or Canvas page over it.</summary>
    static void ClearDetail()
    {
        library.Assignment = null;
        library.Reader = null;
    }

    /// <summary>Something the student picked from a list (not the page's own first pick): in a narrow window it takes
    /// the list's place.</summary>
    static void OpenFromList(Func<Task> open)
    {
        library.Opened();
        _ = open();
    }

    static async Task ShowLectureAsync(string id, bool transcript = false)
    {
        openLecture = id;
        foreach (var g in library.Groups)
            foreach (var c in g.Items) c.Selected = c.Id == id;
        if (host.Remote() is not { } lib) return;
        ClearDetail();
        JsonObject? l;
        try
        {
            l = await lib.LectureAsync(id);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or LibraryRefusedException)
        {
            return;
        }
        if (l is null) return;
        var date = Date(S(l["date"]))?.LocalDateTime;
        string meta = string.Join(" · ", new[]
        {
            S(l["class"]), date?.ToString("dddd d MMMM", CultureInfo.InvariantCulture) ?? "",
            l["seconds"] is JsonValue v && v.TryGetValue(out double s) ? TimedText.Length(s) : "",
        }.Where(x => x.Length > 0));
        string status = S(l["status"]);
        string notes = S(l["notes"]);
        var note = new NoteModel
        {
            Id = id, ClassName = S(l["class"]), Dot = DotFor(S(l["class"])), Meta = meta, Title = S(l["title"]), Markdown = notes,
            Pending = status is "queued" or "working" ? "The library is writing the notes for this lecture. They show here when they're done."
                : notes.Length == 0 ? S(l["error"]) is { Length: > 0 } err ? err : "There are no notes for this lecture." : null,
            ShowTranscript = transcript,
        };
        foreach (var line in TimedText.HasTimes(S(l["transcript"])) ? TimedText.Parse(S(l["transcript"]))
                     : S(l["transcript"]) is { Length: > 0 } plain ? [new Spoken(0, 0, plain)] : [])
            note.Transcript.Add(new HeardLine { Time = TimedText.HasTimes(S(l["transcript"])) ? TimedText.Clock(line.Start) : "", Text = line.Text });
        library.Note = note;
        ShowLectureAi(l, note);
    }

    /// <summary>The lecture's notes, which another engine can rewrite (while they aren't still being written), and a
    /// fresh ask bar for it: a new lecture starts a new conversation.</summary>
    static void ShowLectureAi(JsonObject l, NoteModel note)
    {
        var ai = Ai();
        library.Notes?.Dispose();
        library.Notes = null;
        if (!note.HasPending)
        {
            var notes = new AiNotesModel(ai);
            notes.NotesChanged += () => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (openLecture == note.Id) _ = ShowLectureAsync(note.Id, library.Note?.ShowTranscript == true);
            });
            library.Notes = notes;
            _ = notes.Load(note.Id, note.Markdown, S(l["notes_model"]), S(l["updated"]));
        }
        var ask = new AiAskModel(ai)
        {
            LectureId = note.Id, ClassName = note.ClassName,
            OpenSettings = () => ShowSettings("AI"),
            OnSource = s => PlaySource(s.Id ?? note.Id, s.At),
        };
        library.Ask = ask;
        _ = ask.Load();
    }

    /// <summary>The connected library's AI: engines, asking, rewriting notes.</summary>
    static Core.Ai.IAiLibrary Ai()
    {
        var cc = host.Client();
        return new Core.Ai.AiRemote(cc.ServerUrl, cc.PoolKey);
    }

    /// <summary>A source an answer points to: its moment in the recording plays (when the audio's here), and the lecture
    /// opens at its transcript.</summary>
    static void PlaySource(string id, double? at)
    {
        if (at is double t && File.Exists(host.Lectures.AudioPath(id))) Play(id, t);
        if (id != openLecture) OpenLecture(id, transcript: at is not null);
        else if (library.Note is { } n && at is not null) n.ShowTranscript = true;
    }

    // --- Canvas: the Due page, and an assignment's own page ---------------------------------------------------------

    static bool dueOpen;
    /// <summary>The Due row to show (the last one picked, or one a notification or the dropdown asked for).</summary>
    static (string Class, string Id)? dueSelection;

    static async Task ShowDueAsync()
    {
        int turn = ++libraryTurn;
        dueOpen = true;
        foreach (var c in library.Classes) c.Selected = c.IsDue;
        library.Unsorted.Selected = false;
        library.ClassTitle = "Due";
        library.Empty = null;
        library.Groups.Clear();
        library.CanvasClass = null;
        openCanvasClass = null;
        // No lecture behind the Due page: its notes stop following a rewrite, and the ask bar goes with it.
        library.Notes?.Dispose();
        library.Notes = null;
        library.Note = null;
        var list = library.DueList is { } open && dueListFor == canvasFor ? open : NewDueList();
        library.DueList = list;
        library.List = LibraryList.Due;
        await LoadCanvasAsync();
        if (turn != libraryTurn) return;
        UpdateDueItem();
        UpdateNextDue();
        UpdateDueStatus();
        if (canvasDue is { } due) list.Show(due);
        var rows = list.Groups.SelectMany(g => g.Rows).ToList();
        var pick = dueSelection is var (cls, id) ? rows.FirstOrDefault(r => r.Class == cls && r.Id == id) : null;
        pick ??= rows.FirstOrDefault();
        if (pick is null)
        {
            ClearDetail();
            library.Note = null;
            return;
        }
        bool opened = dueSelection is not null && pick.Class == dueSelection.Value.Class && pick.Id == dueSelection.Value.Id;
        picking = !opened;
        list.SelectItem(pick.Class, pick.Id);
        picking = false;
    }

    static string dueListFor = "";
    /// <summary>The page is picking its own first row, not the student: a narrow window stays on the list.</summary>
    static bool picking;

    static CanvasDueModel NewDueList()
    {
        dueListFor = canvasFor;
        return new CanvasDueModel(Canvas())
        {
            OnSelect = (cls, id) =>
            {
                dueSelection = (cls, id);
                if (!picking) library.Opened();
                _ = ShowAssignmentAsync(cls, id);
            },
        };
    }

    /// <summary>An assignment's own page on the right: what to do, the rubric, and what was handed in.</summary>
    static async Task ShowAssignmentAsync(string cls, string id)
    {
        var page = new AssignmentModel(Canvas()) { OnSearch = ToggleQuick };
        try
        {
            await page.LoadAsync(cls, id);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or CanvasLibraryException or System.Text.Json.JsonException or InvalidOperationException)
        {
            return;
        }
        library.Reader = null;
        library.Assignment = page;
    }

    static void OpenLecture(string id, bool transcript = false)
    {
        ShowLibrary();
        _ = OpenLectureAsync(id, transcript);
    }

    static async Task OpenLectureAsync(string id, bool transcript)
    {
        if (host.Remote() is not { } lib) return;
        try
        {
            if (await lib.LectureAsync(id) is { } l && S(l["class"]) is { Length: > 0 } cls && cls != openClass)
            {
                openLecture = id;
                await ShowClassAsync(cls);
            }
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or LibraryRefusedException)
        {
            return;
        }
        await ShowLectureAsync(id, transcript);
    }

    /// <summary>The ••• menu: open the class in a terminal with the AI (when the library is on this computer), the
    /// library's web page (chat, capture, history), and Settings.</summary>
    static void MoreMenu()
    {
        if (mainWindow?.Content is not Control anchor) return;
        var menu = Menu();
        string? cls = dueOpen ? null : openClass is { } o && o != Configs.Unsorted ? o : null;
        if (host.Remote() is { } lib && Uri.TryCreate(lib.ServerUrl, UriKind.Absolute, out var u) && (u.IsLoopback || host.Settings.LibraryHere))
        {
            var term = new MenuItem { Header = cls is null ? "Open the library in Claude Code" : $"Open {cls} in Claude Code" };
            term.Click += async (_, _) =>
            {
                try
                {
                    Toast("Study Stash", await lib.TerminalAsync(cls), null, null);
                }
                catch (Exception e) when (e is HttpRequestException or TaskCanceledException or LibraryRefusedException)
                {
                    Toast("Couldn't open it", e.Message, null, null);
                }
            };
            menu.Items.Add(term);
        }
        foreach (var (label, path) in new[] { ("Chat in the browser", "/chat"), ("Capture…", "/inbox"), ("What the AI changed", "/history") })
        {
            var item = new MenuItem { Header = label };
            item.Click += (_, _) =>
            {
                if (host.Remote() is { } l) Machine.Open(l.ServerUrl + path + (path == "/chat" && cls is not null ? "?class=" + Uri.EscapeDataString(cls) : ""));
            };
            menu.Items.Add(item);
        }
        menu.Items.Add(new Separator());
        var settings = new MenuItem { Header = "Settings" };
        settings.Click += (_, _) => ShowSettings();
        menu.Items.Add(settings);
        menu.Open(anchor);
    }

    static void MoveLecture()
    {
        if (library.Note is not { } note || mainWindow?.Content is not Control anchor) return;
        var menu = Menu();
        foreach (var (name, color, _) in host.Classes().Where(c => c.Name != note.ClassName).Append((Configs.Unsorted, -1, 0)))
        {
            var item = new MenuItem { Header = name };
            if (color >= 0) item.Icon = new Avalonia.Controls.Shapes.Ellipse { Width = 8, Height = 8, Fill = Skin.ClassDot(color) };
            item.Click += async (_, _) =>
            {
                if (host.Remote() is not { } lib) return;
                try
                {
                    await lib.MoveAsync(note.Id, name);
                    openLecture = note.Id;
                    await LoadLibraryAsync();
                }
                catch (Exception e) when (e is HttpRequestException or TaskCanceledException or LibraryRefusedException)
                {
                    Toast("Couldn't move it", e.Message, null, null);
                }
            };
            menu.Items.Add(item);
        }
        menu.Open(anchor);
    }

    /// <summary>Deletes a lecture on the library, the same from a laptop as on the library itself: its notes,
    /// transcript and search passages go at once, and the library keeps it in its trash a few minutes for Undo. The
    /// window has already taken it out of the list; if the library refuses, the list is read again.</summary>
    static async Task<bool> DeleteLectureAsync(LectureDeletion d)
    {
        if (openLecture == d.Id) openLecture = null;
        try
        {
            if (host.Remote() is not { } lib) throw new HttpRequestException("Can't reach your library right now.");
            await lib.DeleteAsync(d.Id);
            Program.Log($"[library] deleted {d.Id}");
            _ = host.CheckLibraryAsync();
            return true;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or LibraryRefusedException)
        {
            Toast("Couldn't delete it", e.Message, null, null);
            _ = LoadLibraryAsync();
            return false;
        }
    }

    /// <summary>"Undo": the lecture comes back from the library's trash, and opens where it was.</summary>
    static async Task UndoDeleteAsync(LectureDeletion d)
    {
        try
        {
            if (host.Remote() is not { } lib) throw new HttpRequestException("Can't reach your library right now.");
            if (await lib.RestoreAsync(d.Id) is null)
            {
                Toast("Couldn't bring it back", "It's no longer in your library's trash.", null, null);
                return;
            }
            Program.Log($"[library] brought back {d.Id}");
            openLecture = d.Id;
            await LoadLibraryAsync();
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or LibraryRefusedException)
        {
            Toast("Couldn't bring it back", e.Message, null, null);
        }
    }

    /// <summary>The share/Export button's menu: download this lecture, download the whole open class, and whether a
    /// download includes the transcript.</summary>
    static async Task ExportAsync()
    {
        await Task.Yield(); // opening the menu is itself synchronous; a pick below does the real, awaited work
        if (library.Note is not { } note || mainWindow?.Content is not Control anchor || mainWindow is null || host.Remote() is not { } lib) return;
        string? cls = dueOpen ? null : openClass is { } o && o != Configs.Unsorted ? o : null;
        DownloadMenu.Build(cls, host.Settings.DownloadTranscripts,
            download: () => _ = NotesDownload.LectureAsync(mainWindow, lib, note.Id, Notes.Slugify(note.Title) + ".md", host.Settings.DownloadTranscripts, MermaidSvg()),
            downloadClass: () => _ = NotesDownload.ClassAsync(mainWindow, lib, cls!, host.Settings.DownloadTranscripts, MermaidSvg()),
            toggleTranscripts: () => host.Save(s => s.DownloadTranscripts = !s.DownloadTranscripts)).Open(anchor);
    }

    /// <summary>Draws a Mermaid flowchart that already parses, in the app's own font, as a standalone SVG for a
    /// download; null for one that can't be laid out. The download calls it away from the UI thread, and a chart the
    /// notes already showed is drawn from the same scene.</summary>
    static Func<string, string?> MermaidSvg()
    {
        var font = Application.Current?.TryFindResource("TextFont", out var v) == true && v is FontFamily f ? f : FontFamily.Default;
        return source =>
        {
            try
            {
                return SceneCache.Laid(Mermaid.Parse(source), font) is { } scene ? DiagramSvg.Render(scene) : null;
            }
            catch (MermaidException)
            {
                return null;
            }
        };
    }

    static async Task CaptureAsync(string text)
    {
        if (host.Remote() is not { } lib) return;
        try
        {
            var r = await lib.CaptureAsync(text);
            Toast("Kept", r is null ? "Your library runs an older Study Stash." : "It's filed under its class in a minute.", null, null);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or LibraryRefusedException)
        {
            Toast("Couldn't keep it", e.Message, null, null);
        }
    }

    // --- the quick panel ---------------------------------------------------------------------------------------------

    static async Task SearchAsync(string query)
    {
        int turn = ++searchTurn;
        await Task.Delay(120); // wait for the typing to pause
        if (turn != searchTurn || quick.Answering) return;
        var rows = new List<QuickRow>();
        string rec = Skin.Current == SkinKind.Mac ? "⌥⇧R" : "Ctrl+Alt+R";
        string cls = RecordClass();
        void Actions()
        {
            rows.Add(new QuickRow { Kind = QuickKind.Header, Title = "Actions", First = rows.Count == 0 });
            rows.Add(host.Recorder.Current is null
                ? new QuickRow { Kind = QuickKind.Action, Title = cls.Length > 0 ? $"Record {cls}" : "Record", Meta = rec, Glyph = "mic", Run = () => { quickWindow?.Hide(); ToggleRecording(); } }
                : new QuickRow { Kind = QuickKind.Action, Title = "Stop recording", Meta = rec, Glyph = "stop", Run = () => { quickWindow?.Hide(); StopRecording(); } });
            rows.Add(new QuickRow { Kind = QuickKind.Action, Title = "Open library", Glyph = "book_2", Run = () => { quickWindow?.Hide(); ShowLibrary(); } });
            if (CanvasLinked && !CanvasQuick.Matches(canvasDue, query))
                rows.Add(new QuickRow { Kind = QuickKind.Action, Title = "What's due", Glyph = "schedule", Run = OpenDue });
            // What's typed, kept: the library's AI files it under its class.
            if (query.Trim().Length > 0)
                rows.Add(new QuickRow
                {
                    Kind = QuickKind.Action, Title = $"Capture “{Py.Head(query.Trim(), 60)}”", Glyph = "inbox",
                    Run = () =>
                    {
                        quickWindow?.Hide();
                        _ = CaptureAsync(query.Trim());
                    },
                });
        }
        quick.Note = null;
        if (query.Trim().Length > 0 && host.Remote() is { } lib)
        {
            try
            {
                var r = await lib.SearchAsync(query, null, 6);
                if (turn != searchTurn) return;
                var lectures = (r["lectures"] as JsonArray ?? []).OfType<JsonObject>().Take(4).ToList();
                var passages = (r["passages"] as JsonArray ?? []).OfType<JsonObject>().Take(4).ToList();
                var classes = (r["classes"] as JsonArray ?? []).OfType<JsonObject>().Take(3).ToList();
                if (lectures.Count > 0)
                {
                    rows.Add(new QuickRow { Kind = QuickKind.Header, Title = "Lectures", First = rows.Count == 0 });
                    foreach (var l in lectures)
                        rows.Add(new QuickRow
                        {
                            Kind = QuickKind.Lecture, Title = S(l["title"]), Dot = DotFor(S(l["class"])), LectureId = S(l["id"]),
                            Meta = $"{S(l["class"])} · {Date(S(l["date"]))?.LocalDateTime.ToString("ddd d MMM", CultureInfo.InvariantCulture)}",
                        });
                }
                if (passages.Count > 0)
                {
                    rows.Add(new QuickRow { Kind = QuickKind.Header, Title = "Passages from notes", First = rows.Count == 0 });
                    foreach (var p in passages)
                    {
                        string where = p["at"] is JsonValue v && v.TryGetValue(out double at) ? TimedText.Clock(at) : S(p["section"]) is { Length: > 0 } sec ? sec : "Transcript";
                        rows.Add(new QuickRow
                        {
                            Kind = QuickKind.Passage, Title = "…" + Excerpt(S(p["text"]), query) + "…", Sub = $"{S(p["title"])} · {where}", Dot = DotFor(S(p["class"])),
                            LectureId = S(p["id"]), At = p["at"] is JsonValue va && va.TryGetValue(out double a2) ? a2 : null,
                        });
                    }
                }
                if (classes.Count > 0)
                {
                    rows.Add(new QuickRow { Kind = QuickKind.Header, Title = "Classes", First = rows.Count == 0 });
                    foreach (var c in classes)
                    {
                        int n = c["lectures"]?.GetValue<int>() ?? 0;
                        rows.Add(new QuickRow { Kind = QuickKind.Class, Title = S(c["name"]), ClassName = S(c["name"]), Dot = Skin.ClassDot(c["color"]?.GetValue<int>() ?? 0), Meta = $"{n} lecture{(n == 1 ? "" : "s")}" });
                    }
                }
                if (rows.Count == 0) quick.Note = $"Nothing matches “{query.Trim()}”. {(Skin.Current == SkinKind.Mac ? "⌘" + ViewModels.QuickModel.Return : "Ctrl+Enter")} asks your notes instead.";
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or LibraryRefusedException)
            {
                quick.Note = "Can't reach your library to search it.";
            }
        }
        // What's due, when the search is about it: Canvas's own rows, and its Sync and Open Due actions.
        if (CanvasLinked && CanvasQuick.Matches(canvasDue, query))
        {
            var ctx = Canvas();
            rows.AddRange(CanvasQuick.Rows(canvasDue, canvasWatch?.State, query, ctx.Clock.Zone, ctx.Clock.Now(), ctx.DotOf,
                item => OpenDueItem(item.Class, item.Id), SyncCanvas, OpenDue));
            if (rows.Count > 0) quick.Note = null;
        }
        Actions();
        quick.Rows.Clear();
        foreach (var r in rows) quick.Rows.Add(r);
        quick.SelectFirst();
    }

    static void OpenDue()
    {
        quickWindow?.Hide();
        dueOpen = true;
        ShowLibrary();
    }

    /// <summary>Canvas syncs on Chrome's next check, within a minute.</summary>
    static void SyncCanvas()
    {
        quickWindow?.Hide();
        if (Canvas().Client is not { } client) return;
        _ = Task.Run(async () =>
        {
            try
            {
                await client.SaveAsync(sync: true);
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or CanvasLibraryException or System.Text.Json.JsonException)
            {
            }
        });
        Toast("Syncing Canvas", "On Chrome's next check, within a minute.", null, null);
    }

    /// <summary>A passage cut down to the part round what was searched for.</summary>
    static string Excerpt(string text, string query)
    {
        text = text.Replace('\n', ' ').Trim();
        int at = Controls.Marked.Find(text, query).FirstOrDefault() is { Length: > 0 } f ? f.Start : 0;
        int from = Math.Max(0, at - 40);
        if (from > 0) from = text.IndexOf(' ', from) is int sp and >= 0 && sp < at ? sp + 1 : from;
        string cut = text[from..];
        return cut.Length > 110 ? cut[..110].TrimEnd() : cut;
    }

    /// <summary>⌘Return in the quick panel: the library's AI answers as it writes; a library too old for that
    /// answers the old way, once, at the end.</summary>
    static async Task AskQuick(string question)
    {
        if (host.Remote() is not null && await quick.AnswerAsync(Ai(), question)) return;
        await AskQuickAtOnce(question);
    }

    static async Task AskQuickAtOnce(string question)
    {
        quick.Answering = true;
        quick.Thinking = true;
        quick.Answer = "";
        quick.Rows.Clear();
        quick.Note = null;
        var into = new ChatMessage();
        await Answer(into, lib => lib.AskAsync(question));
        quick.Thinking = false;
        quick.Answer = into.Text;
        if (host.Remote() is { } lib && into.Sources.Count > 0)
        {
            quick.Rows.Add(new QuickRow { Kind = QuickKind.Header, Title = "Sources", First = true });
            foreach (var s in into.Sources)
            {
                string title = s.LectureId ?? "";
                try
                {
                    if (s.LectureId is { } id && await lib.LectureAsync(id) is { } l) title = S(l["title"]);
                }
                catch (Exception e) when (e is HttpRequestException or TaskCanceledException or LibraryRefusedException)
                {
                }
                quick.Rows.Add(new QuickRow { Kind = QuickKind.Source, Title = title, Meta = s.Label, LectureId = s.LectureId, At = s.At });
            }
            quick.SelectFirst();
        }
    }

    static void OpenQuickRow(QuickRow row)
    {
        quickWindow?.Hide();
        switch (row.Kind)
        {
            case QuickKind.Class when row.ClassName is { } c:
                openClass = c;
                openLecture = null;
                ShowLibrary();
                break;
            case QuickKind.Source or QuickKind.Passage when row.LectureId is { } id && row.At is double at:
                if (File.Exists(host.Lectures.AudioPath(id))) Play(id, at);
                OpenLecture(id, transcript: row.Kind == QuickKind.Source);
                break;
            case QuickKind.Lecture or QuickKind.Passage or QuickKind.Source when row.LectureId is { } id:
                OpenLecture(id);
                break;
        }
    }
}
