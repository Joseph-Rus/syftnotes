using System.Text.Json.Nodes;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using StudyStash.App.Services;
using StudyStash.App.ViewModels;
using StudyStash.Core;
using StudyStash.Core.Calendar;

namespace StudyStash.App;

/// <summary>The library window's pages beside its one sidebar: Home (what's up next, and every note), Due (a calendar of
/// what's to hand in), a folder's page (its lectures or meetings by week, with what's due and its Canvas lists in a
/// rail), and a class's lists on Canvas. A note's own page is in Shell.Library.cs.</summary>
public static partial class Shell
{
    /// <summary>The overview showing: "" for Home, <see cref="DueHome"/> for Due, a folder's name for its page, null
    /// for none (a note, an assignment or a class's Canvas lists is showing instead).</summary>
    static string? overviewOf;

    /// <summary>What <see cref="overviewOf"/> holds while Due shows (no folder can be called this).</summary>
    const string DueHome = "\u0001due";

    /// <summary>Due's calendar is for one class (the folder page's "See these on the calendar"); null for every class.</summary>
    static string? dueOnly;
    /// <summary>Home lists only the notes in no folder yet (its filter's Unsorted).</summary>
    static bool homeUnsorted;
    /// <summary>How many notes Home has brought so far (it brings as many again when it's read again).</summary>
    static int homeShown = OverviewModel.PageSize;
    /// <summary>Every page shown takes the next turn: one that's no longer the latest by the time its answer is back
    /// stops instead of drawing over what a later one already drew.</summary>
    static int pageTurn;

    /// <summary>Home (""), Due, or a folder's page.</summary>
    static Task ShowOverviewAsync(string of) => of.Length == 0 ? ShowHomeAsync() : of == DueHome ? ShowDuePageAsync() : ShowFolderAsync(of);

    /// <summary>Nothing is open any more: whatever is shown next says what is.</summary>
    static void LeavePage()
    {
        overviewOf = null;
        openLecture = null;
        openCanvasClass = null;
        openAssignment = null;
    }

    /// <summary>The sidebar's selected row: Home, Due, a folder, or (a note in no folder, say) none.</summary>
    static void SelectInSidebar(string? cls, bool home = false, bool due = false)
    {
        library.Home.Selected = home;
        foreach (var c in library.Classes) c.Selected = c.IsDue ? due : !home && !due && cls is not null && c.Name == cls;
    }

    /// <summary>The pane makes way for a page: no assignment, Canvas list or note is left under it. (A lecture's
    /// unsaved typing is kept for its next opening, and what's typed in a recording's notes is saved.)</summary>
    static void ClearPane()
    {
        FlushTyped();
        library.NarrowDetail = false;
        library.Assignment = null;
        library.Reader = null;
        library.CanvasClass = null;
        LeaveNotes(library.Notes);
        library.Notes = null;
        library.Note = null;
        // An answer about the note being left isn't wanted any more (the recorder's own ask goes on with the recording).
        if (!ReferenceEquals(library.Ask, recorder.Ask)) library.Ask?.Stop();
        library.Ask = null;
    }

    // --- Home ---------------------------------------------------------------------------------------------------------

    static async Task ShowHomeAsync()
    {
        int turn = ++pageTurn;
        LeavePage();
        overviewOf = "";
        SelectInSidebar(null, home: true);
        if (host.Remote() is not { } lib) return;
        int want = Math.Clamp(homeShown, OverviewModel.PageSize, 480);
        var lectures = await NotesAsync(lib, homeUnsorted ? Configs.Unsorted : null, want + 1, null);
        if (turn != pageTurn) return;
        var sources = OverviewSources(WithLocal([.. lectures.Take(want)], null, homeUnsorted, all: lectures.Count <= want));
        var home = OverviewModel.Home(sources, homeUnsorted);
        home.CanLoadMore = lectures.Count > want;
        home.OnFilter = unsorted =>
        {
            if (homeUnsorted == unsorted) return;
            homeUnsorted = unsorted;
            homeShown = OverviewModel.PageSize;
            _ = ShowHomeAsync();
        };
        // The next page: the notes from before the last one showing.
        home.OnMore = async () =>
        {
            if (home.Notes.LastOrDefault() is not { Date.Length: > 0 } last) return;
            var more = await NotesAsync(lib, homeUnsorted ? Configs.Unsorted : null, OverviewModel.PageSize + 1, last.Date);
            if (!ReferenceEquals(library.Overview, home)) return;
            var dot = OverviewModel.DotsOf(sources);
            home.Add(more.Take(OverviewModel.PageSize).Where(l => home.Notes.All(n => n.Id != l.Id)).Select(l => OverviewModel.Row(l, sources, dot, showFolder: true)),
                more.Count > OverviewModel.PageSize);
            homeShown = home.Notes.Count;
        };
        home.PickerFor = row => PickerFor(row.Id, row.Folder);
        home.OnDelete = row => library.DeleteCommand.Execute(row);
        Equip(home, null);
        ClearPane();
        library.Overview = home;
    }

    // --- a folder's page ----------------------------------------------------------------------------------------------

    static async Task ShowFolderAsync(string name)
    {
        // Unsorted is no folder: its notes are Home's, filtered.
        if (name == Configs.Unsorted || name.Length == 0)
        {
            homeUnsorted = true;
            await ShowHomeAsync();
            return;
        }
        int turn = ++pageTurn;
        LeavePage();
        overviewOf = name;
        openClass = name;
        SelectInSidebar(name);
        if (host.Remote() is not { } lib) return;
        var lectures = WithLocal(await NotesAsync(lib, name, 500, null), name, false, all: true);
        if (turn != pageTurn) return;
        var row = CanvasClassRow(name);
        var info = (host.Overview?["classes"] as JsonArray ?? []).OfType<JsonObject>().FirstOrDefault(c => S(c["name"]) == name);
        var known = host.Classes().FirstOrDefault(c => c.Name == name);
        var facts = new ClassHomeFacts(name, DotFor(name), Math.Max(known.Lectures, lectures.Count), row?.Canvas?.ShortCode is { Length: > 0 } code ? code : null,
            row?.Canvas?.Title is { Length: > 0 } title ? title : null, info is null ? null : S(info["description"]), row?.Counts, host.GroupOf(name));
        var page = OverviewModel.ForClass(facts, OverviewSources(lectures));
        page.AddLinks(facts, row is null ? null : tab =>
        {
            Remember();
            _ = ShowCanvasListsAsync(name, tab);
        });
        page.SetFolders(LinkedFolders(lib, name, info), host.Settings.LibraryHere ? () => _ = LinkFolderAsync(lib, name) : null);
        page.OnDelete = note => library.DeleteCommand.Execute(note);
        Equip(page, name);
        var files = new AttachmentsModel(lib, name, null);
        page.Files = files;
        _ = files.LoadAsync();
        ClearPane();
        library.Overview = page;
    }

    // --- Due ----------------------------------------------------------------------------------------------------------

    /// <summary>Due: a calendar of what's to hand in. The month it was on, the class it was for and month-or-week stay
    /// as they were when it's only being read again.</summary>
    static async Task ShowDuePageAsync()
    {
        int turn = ++pageTurn;
        LeavePage();
        overviewOf = DueHome;
        SelectInSidebar(null, due: true);
        if (host.Remote() is not { } lib) return;
        await LoadCanvasAsync();
        if (turn != pageTurn) return;
        UpdateDueItem();
        UpdateNextDue();
        var term = await TermAsync();
        if (turn != pageTurn) return;
        // The week's planner says which class meetings were recorded. The calendars hold this week's meetings (from
        // Monday on), and the newest hundred notes reach back well past those.
        var recorded = Events() is null ? [] : await NotesAsync(lib, null, 100, null);
        if (turn != pageTurn) return;
        var page = OverviewModel.ForDue(OverviewSources(recorded, WithTerm(CanvasLinked ? canvasDue : null, term)), dueOnly);
        if (library.Overview is { IsDuePage: true, Calendar: { } before } && page.Calendar is { } now) now.Resume(before);
        if (page.Calendar is { } calendar)
            calendar.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(DueCalendarModel.Class) && ReferenceEquals(library.Overview, page)) dueOnly = calendar.Class;
            };
        page.CanvasStatus = DueStatus();
        ClearPane();
        library.Overview = page;
    }

    /// <summary>Each linked class's whole list of assignments, as Canvas has it: the calendar shows what was handed in
    /// weeks ago and what's due months off, which the Due list (the days round now) leaves out. Kept until Canvas is
    /// read again.</summary>
    static List<CanvasApi.AssignmentsResponse>? termAssignments;
    static string termFor = "";

    static async Task<List<CanvasApi.AssignmentsResponse>> TermAsync()
    {
        if (termAssignments is { } kept && termFor == canvasFor) return kept;
        if (Canvas().Client is not { } client) return [];
        string asked = canvasFor;
        var answers = await Task.WhenAll(canvasClasses.Where(c => c.Linked).Select(async c =>
        {
            try
            {
                return await client.AssignmentsAsync(c.Class) is { } a ? a with { Class = a.Class.Length > 0 ? a.Class : c.Class } : null;
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or CanvasLibraryException or System.Text.Json.JsonException or InvalidOperationException)
            {
                return null;
            }
        }));
        var term = answers.OfType<CanvasApi.AssignmentsResponse>().ToList();
        if (asked == canvasFor)
        {
            termAssignments = term;
            termFor = asked;
        }
        return term;
    }

    /// <summary>What's due, with the rest of each class's term added: everything handed in, and anything to hand in
    /// that the Due list didn't reach.</summary>
    internal static CanvasApi.DueResponse? WithTerm(CanvasApi.DueResponse? due, IReadOnlyList<CanvasApi.AssignmentsResponse> term)
    {
        if (due is null || term.Count == 0) return due;
        var seen = due.Groups.SelectMany(g => g.Items).Select(i => (i.Class, i.Id)).ToHashSet();
        CanvasApi.Item Of(CanvasApi.AssignmentsResponse t, CanvasApi.Item i) => i.Class.Length > 0 ? i : i with { Class = t.Class };
        var done = term.SelectMany(t => t.Done.Select(i => Of(t, i))).Where(i => i.DueAt is not null && seen.Add((i.Class, i.Id))).ToList();
        var open = term.SelectMany(t => t.ToHandIn.Select(i => Of(t, i))).Where(i => seen.Add((i.Class, i.Id))).ToList();
        if (done.Count == 0 && open.Count == 0) return due;
        return due with
        {
            Groups = [.. due.Groups, new CanvasApi.Group { Key = "later", Items = open }, new CanvasApi.Group { Key = "handed_in", Items = done }],
        };
    }

    // --- a class's lists on Canvas ------------------------------------------------------------------------------------

    /// <summary>The class whose Canvas lists are open (Assignments, Modules, Announcements); null when they aren't.</summary>
    static string? openCanvasClass;
    /// <summary>The list it's on.</summary>
    static ClassTab canvasTab = ClassTab.Assignments;

    /// <summary>A class's lists on Canvas: the list on the left, and what's picked from it (an assignment, a page, an
    /// announcement) beside it. A class that isn't linked has none, so its own page shows.</summary>
    static async Task ShowCanvasListsAsync(string name, ClassTab tab)
    {
        if (CanvasClassRow(name) is not { } row)
        {
            await ShowFolderAsync(name);
            return;
        }
        int turn = ++pageTurn;
        LeavePage();
        openCanvasClass = name;
        openClass = name;
        canvasTab = tab == ClassTab.Lectures ? ClassTab.Assignments : tab;
        SelectInSidebar(name);
        bool kept = library.CanvasClass is not null && library.CanvasOf == name && library.Overview is null;
        var page = kept ? library.CanvasClass! : NewCanvasClass();
        page.Tab = canvasTab;
        if (!kept)
        {
            ClearPane();
            library.CanvasOf = name;
            library.CanvasDot = DotFor(name);
            library.CanvasClass = page;
        }
        library.Overview = null;
        try
        {
            await page.LoadAsync(row);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or CanvasLibraryException or System.Text.Json.JsonException or InvalidOperationException)
        {
        }
        if (turn != pageTurn) return;
        // A notification's announcement: its own reader, beside the list.
        if (announcementToOpen is { } announcement)
        {
            announcementToOpen = null;
            page.OpenAnnouncement(announcement.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    static CanvasClassModel NewCanvasClass()
    {
        var page = new CanvasClassModel(Canvas())
        {
            OnAssignment = (cls, id) =>
            {
                Remember();
                library.Opened();
                _ = ShowAssignmentAsync(cls, id);
            },
            OnReader = reader =>
            {
                Remember();
                library.Opened();
                openAssignment = null;
                library.Assignment = null;
                library.Reader = reader;
            },
        };
        page.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(CanvasClassModel.Tab) && ReferenceEquals(library.CanvasClass, page) && page.Tab != ClassTab.Lectures) canvasTab = page.Tab;
        };
        return page;
    }

    // --- an assignment ------------------------------------------------------------------------------------------------

    /// <summary>An assignment as a page of its own (from Due's calendar, a class's rail, a notification): what to do,
    /// the rubric, and what was handed in.</summary>
    static async Task ShowAssignmentPageAsync(string cls, string id)
    {
        int turn = ++pageTurn;
        LeavePage();
        openAssignment = (cls, id);
        openClass = cls;
        SelectInSidebar(cls);
        var row = canvasDue?.Groups.SelectMany(g => g.Items).FirstOrDefault(i => i.Class == cls && i.Id == id);
        if (await AssignmentAsync(cls, id, row) is not { } page || turn != pageTurn)
        {
            // It's gone from Canvas (or the library can't be reached): Due is where it would have been.
            if (turn == pageTurn && library.Overview is null && library.Note is null) await ShowDuePageAsync();
            return;
        }
        ClearPane();
        library.Overview = null;
        library.Assignment = page;
    }

    /// <summary>An assignment beside the Canvas list it was picked from.</summary>
    static async Task ShowAssignmentAsync(string cls, string id, CanvasApi.Item? row = null)
    {
        if (await AssignmentAsync(cls, id, row) is not { } page) return;
        library.Reader = null;
        openAssignment = (cls, id);
        library.Assignment = page;
    }

    /// <summary>A planner to-do (<paramref name="row"/>, from the Due list) has no assignment behind it, so it gets a
    /// page of its own; anything else the library no longer knows opens nothing rather than an empty page.</summary>
    static async Task<AssignmentModel?> AssignmentAsync(string cls, string id, CanvasApi.Item? row)
    {
        var page = new AssignmentModel(Canvas()) { OnSearch = ToggleQuick };
        try
        {
            if (row is { Kind: "todo" or "to-do" }) page.ShowTodo(row);
            else if (!await page.LoadAsync(cls, id))
            {
                if (row is null) return null;
                page.ShowTodo(row);
            }
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or CanvasLibraryException or System.Text.Json.JsonException or InvalidOperationException)
        {
            return null;
        }
        return page;
    }

    // --- what the pages share ------------------------------------------------------------------------------------------

    /// <summary>Home's and a folder's page's Record and ask bar: everything (Home) or that folder. And Home's "Buy us
    /// a coffee". A page that's only being read again keeps the ask bar it had, with what's typed and answered in it.</summary>
    static void Equip(OverviewModel page, string? cls)
    {
        if (host.Settings.Role != AppRole.Library)
        {
            ShowRecording(page);
            // Record starts one (and opens its page). While one runs (its dot a stop square, the folder, the level and
            // the time beside it) a click opens its page, and only the stop square stops it.
            page.OnRecord = () =>
            {
                if (host.Recorder.Current is not null) OpenRecording();
                else _ = StartRecordingAsync(cls ?? "", null, openPage: true);
            };
            page.OnStop = () => StopRecording();
        }
        // Home's "Buy us a coffee": the Ko-fi page, and the library window's own ask for a tip goes for good (as it
        // does from the ask itself, which answers here if it's showing).
        if (page.IsHome)
            page.OnSupport = () =>
            {
                if (library.Support is { } asking) asking.TipCommand.Execute(null);
                else SupportAsk.Answer(host, SupportAnswer.Tip, url => Dialogs.OpenUrl(url), DateTimeOffset.Now);
            };
        if (library.Overview is { Ask: { } kept } before && before.IsHome == page.IsHome && before.ClassName == page.ClassName && !before.IsDuePage)
        {
            before.Ask = null;
            page.Ask = kept;
            return;
        }
        var ask = new AiAskModel(Ai()) { ClassName = cls, OpenSettings = () => ShowSettings("AI") };
        if (cls is null) ask.OnlyScopes("all");
        else ask.OnlyScopes("class", "all");
        ask.NameScopes(page.Noun, cls is null ? null : ClassNames.Display(cls));
        page.Ask = ask;
        _ = ask.Load();
    }

    /// <summary>The folders linked to a class, as the library's overview lists them.</summary>
    static IEnumerable<LinkedFolder> LinkedFolders(RemoteLibrary lib, string cls, JsonObject? info) =>
        (info?["folders"] as JsonArray ?? []).OfType<JsonObject>().Select(f =>
        {
            string path = S(f["path"]);
            return new LinkedFolder(S(f["name"]), path, f["here"]?.GetValue<bool>() ?? true,
                () => Dialogs.OpenUrl(path),
                () => _ = ChangeFolderAsync(lib, cls, path, remove: true));
        });

    /// <summary>Link a folder: the folder dialog, then the library lists it on the class's page.</summary>
    static async Task LinkFolderAsync(RemoteLibrary lib, string cls)
    {
        if (mainWindow is null) return;
        var picked = await mainWindow.StorageProvider.OpenFolderPickerAsync(new Avalonia.Platform.Storage.FolderPickerOpenOptions
        {
            Title = $"Link a folder to {cls}",
        });
        if (picked.Count == 0 || picked[0].TryGetLocalPath() is not { } path) return;
        await ChangeFolderAsync(lib, cls, path, remove: false);
    }

    static async Task ChangeFolderAsync(RemoteLibrary lib, string cls, string path, bool remove)
    {
        try
        {
            await lib.ClassFolderAsync(cls, path, remove);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or LibraryRefusedException)
        {
            Toast("Couldn't link the folder", e.Message, null, null);
            return;
        }
        await host.CheckLibraryAsync();
        if (overviewOf == cls) await ShowFolderAsync(cls);
    }

    /// <summary>Notes as the library lists them, newest first: a folder's, or (null) every folder's; from before a
    /// date, for the next page of a list.</summary>
    static async Task<List<OverviewModel.LectureFacts>> NotesAsync(ILibrarySource lib, string? cls, int limit, string? before)
    {
        JsonArray list;
        try
        {
            list = await lib.LecturesAsync(cls, limit, before);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or LibraryRefusedException)
        {
            return [];
        }
        return [.. list.OfType<JsonObject>().Select(l => new OverviewModel.LectureFacts(S(l["id"]), S(l["title"]), S(l["class"]), Date(S(l["date"])),
            l["seconds"] is JsonValue v && v.TryGetValue(out double sec) ? sec : null, S(l["summary"]), S(l["status"]) is "queued" or "working", S(l["date"])))];
    }

    /// <summary>
    /// The notes a list shows, with the recordings this computer has that aren't filed yet among them: one recording
    /// now, or being written down, or on its way to the library is a "New note" (or the title typed for it) from the
    /// moment it starts, saying where it is in the summary's place, and opens as it is. One the library lists already
    /// (picked up again, or having its notes written) keeps its row and says the same. <paramref name="all"/>: the
    /// list is the whole of it, so older ones belong in it too, not only those as new as its last row.
    /// </summary>
    static List<OverviewModel.LectureFacts> WithLocal(List<OverviewModel.LectureFacts> listed, string? cls, bool unsorted, bool all)
    {
        var mine = LocalNotes().Where(l => cls is not null ? l.Class == cls : !unsorted || l.Class.Length == 0 || l.Class == Configs.Unsorted)
            .ToDictionary(l => l.Id);
        if (mine.Count == 0) return listed;
        var notes = listed.Select(f => mine.Remove(f.Id, out var here) ? f with { Status = here.Status } : f).ToList();
        var oldest = all || notes.Count == 0 ? null : notes[^1].Date;
        notes.AddRange(mine.Values.Where(l => oldest is null || l.Date >= oldest));
        return [.. notes.OrderByDescending(n => n.Date ?? DateTimeOffset.MinValue)];
    }

    /// <summary>The recordings this computer has that the library hasn't filed yet, as a list's rows.</summary>
    static IEnumerable<OverviewModel.LectureFacts> LocalNotes()
    {
        if (host.Settings.Role == AppRole.Library) yield break;
        foreach (var l in host.Lectures.All())
        {
            if (LocalStatus(l) is not { } status) continue;
            string title = (l.TitleTyped || l.State == LectureState.Planned) && l.Title.Length > 0 ? l.Title : l.FiledTitle.Length > 0 ? l.FiledTitle : "New note";
            yield return new OverviewModel.LectureFacts(l.Id, title, l.ClassName, l.StartedAt, l.Seconds > 0 ? l.Seconds : null, "", false, l.Started, status);
        }
    }

    /// <summary>Where a recording is on its way, as its row says it; null once it's filed.</summary>
    static string? LocalStatus(Lecture l) => l.State switch
    {
        LectureState.Recording => "Recording now",
        LectureState.Paused => "Recording, paused",
        LectureState.Transcribing => l.Progress > 0 ? $"Being written down ({Math.Round(l.Progress * 100)}%)" : "Being written down",
        LectureState.Sending => host.Library == LibraryState.Connected ? "On its way to your library" : "Waiting for your library",
        LectureState.Writing => "Writing notes…",
        LectureState.Failed => l.Error.Length > 0 ? l.Error : "Something went wrong",
        LectureState.Planned => AppHost.PlanStarts(l) is { } at && at > DateTimeOffset.Now
            ? $"Records at {at.ToLocalTime().ToString("h:mm tt", System.Globalization.CultureInfo.InvariantCulture)}" : "Not recorded",
        _ => null,
    };

    /// <summary>What the lists last drew of this computer's recordings: when it reads differently (one started,
    /// stopped, was sent or filed), the list showing is drawn again.</summary>
    static string localNotesDrawn = "";

    static string LocalNotesStamp() =>
        string.Join("\u0001", host.Lectures.All().Where(l => l.State != LectureState.Filed).Select(l => $"{l.Id}:{l.State}:{l.Title}:{l.ClassName}"));

    /// <summary>The page showing says whether something is recording, and reads the folder, the level and the time
    /// from the recorder while it is.</summary>
    static void ShowRecording(OverviewModel page)
    {
        bool recording = host.Recorder.Current is not null;
        page.Recorder = recording ? recorder : null;
        page.Recording = recording;
    }

    /// <summary>The events on the student's calendars, as far as the app keeps them (this week from Monday, and the
    /// week ahead), each with the folder it's clearly for. Null without calendars, and on a computer that only keeps
    /// the library.</summary>
    static List<OverviewModel.Event>? Events()
    {
        if (host.Settings.Role == AppRole.Library || !host.HasCalendars) return null;
        var classes = host.CalendarClasses();
        var known = host.Classes().Select(c => c.Name).ToHashSet();
        return [.. host.Calendars.Events
            .Where(e => !e.AllDay && !e.Cancelled && e.End > e.Start)
            .OrderBy(e => e.Start)
            .Select(e => new OverviewModel.Event(e.Id, e.Title, e.Start, e.End, EventClass.For(e.Title, classes) is { } c && known.Contains(c) ? c : null))];
    }

    static OverviewModel.Sources OverviewSources(IReadOnlyList<OverviewModel.LectureFacts> lectures) =>
        OverviewSources(lectures, CanvasLinked ? canvasDue : null);

    static OverviewModel.Sources OverviewSources(IReadOnlyList<OverviewModel.LectureFacts> lectures, CanvasApi.DueResponse? due)
    {
        var ctx = Canvas();
        return new OverviewModel.Sources
        {
            Now = ctx.Clock.Now(),
            Zone = ctx.Clock.Zone,
            Classes = [.. host.Classes().Select(c => (c.Name, (IBrush)Skin.ClassDot(c.Color), c.Lectures))],
            GroupOf = host.GroupOf,
            Lectures = lectures,
            Due = due,
            Canvas = canvasClasses,
            Events = Events(),
            Unsorted = host.Overview?["unsorted"] is JsonValue u && u.TryGetValue(out int unsorted) ? unsorted : 0,
            Writing = host.Overview?["writing"] is JsonValue w && w.TryGetValue(out int writing) ? writing : 0,
            OpenClass = cls =>
            {
                Remember();
                _ = ShowFolderAsync(cls);
            },
            OpenLecture = (cls, id) =>
            {
                Remember();
                _ = ShowLectureAsync(id);
            },
            OpenAssignment = (cls, id) =>
            {
                Remember();
                _ = ShowAssignmentPageAsync(cls, id);
            },
            OpenDue = cls =>
            {
                Remember();
                dueOnly = cls;
                _ = ShowDuePageAsync();
            },
            RecordEvent = id => _ = RecordEventAsync(id),
        };
    }

    // --- moving a note --------------------------------------------------------------------------------------------------

    /// <summary>The folder picker for a note: every folder under its group, with its swatch and how many notes it
    /// holds; a pick moves the note there at once, and New folder makes one for it.</summary>
    static FolderPickerModel? PickerFor(string id, string folder)
    {
        if (host.Remote() is null) return null;
        return new FolderPickerModel(host.Classes().Select(c => (c.Name, host.GroupOf(c.Name), (IBrush)Skin.ClassDot(c.Color), c.Lectures)), folder)
        {
            OnPick = to => _ = MoveNoteAsync(id, folder, to, say: true),
            OnNew = name => _ = MoveToNewFolderAsync(id, folder, name),
        };
    }

    /// <summary>Moves a note to another folder (or out of every folder, to Unsorted): the page showing follows where it
    /// is (Home's row takes its new label, a folder's page lets it go, its own page takes its new place and name for
    /// what it is), and a toast says where it went, with Undo.</summary>
    static async Task MoveNoteAsync(string id, string from, string to, bool say)
    {
        if (host.Remote() is not { } lib) return;
        bool unsorted = to.Length == 0 || to == Configs.Unsorted;
        try
        {
            await lib.MoveAsync(id, unsorted ? Configs.Unsorted : to);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or LibraryRefusedException)
        {
            Toast("Couldn't move it", e.Message, null, null);
            return;
        }
        string folder = unsorted ? "" : to;
        if (library.Overview is { IsHome: true } home && home.Notes.FirstOrDefault(n => n.Id == id) is { } row)
        {
            if (homeUnsorted && !unsorted) home.Remove(id);
            else
            {
                row.Folder = folder;
                row.Dot = DotFor(folder);
                row.Noun = Nouns.For(folder, unsorted ? "" : host.GroupOf(folder));
            }
        }
        else if (library.Overview is { IsFolder: true } page && page.ClassName != folder) page.Remove(id);
        if (openLecture == id && overviewOf is null) await ShowLectureAsync(id, library.Note?.ShowTranscript == true);
        _ = RefreshCountsAsync();
        if (say) library.SayMoved(new NoteMove(id, from, folder));
    }

    /// <summary>New folder, from the picker: the folder is made (under Classes; its group can be changed from the
    /// sidebar), the sidebar lists it, and the note moves in.</summary>
    static async Task MoveToNewFolderAsync(string id, string from, string name)
    {
        if (host.Remote() is not { } lib) return;
        try
        {
            await lib.AddClassAsync(name);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or LibraryRefusedException)
        {
            Toast("Couldn't make the folder", e.Message, null, null);
            return;
        }
        await host.CheckLibraryAsync();
        ShowSidebar();
        UpdateDueItem();
        await MoveNoteAsync(id, from, name, say: true);
    }
}
