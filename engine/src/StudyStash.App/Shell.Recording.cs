using System.Globalization;
using Avalonia.Threading;
using StudyStash.App.Platform;
using StudyStash.App.Services;
using StudyStash.App.ViewModels;
using StudyStash.Core;
using StudyStash.Core.Calendar;

namespace StudyStash.App;

/// <summary>
/// Recording, as the library window shows it: Record (on Home, a folder's page, the shortcut) and a click on an event
/// in Coming up or Up next start one and open its page, where the student types their own notes while it records and
/// reads what's been heard; the small recorder floats only while the window isn't the one in front.
/// </summary>
public static partial class Shell
{
    /// <summary>Record is waiting on macOS's microphone prompt: another press does nothing until it's answered.</summary>
    static bool askingForMic;

    /// <summary>The shortcut, the menu, the dropdown: stops what's recording, or starts (into the folder picked there,
    /// or the event that's on). Its page opens when the library window is the one in front; anywhere else the small
    /// recorder shows, and nothing takes the keyboard from the app being typed in.</summary>
    static void ToggleRecording()
    {
        if (host.Recorder.Current is not null) StopRecording();
        else _ = StartRecordingAsync(RecordClass(), null, openPage: mainWindow is { IsVisible: true, IsActive: true });
    }

    /// <summary>
    /// Starts a recording: into <paramref name="className"/> ("" lets the event that's on, or the library, say where),
    /// or of <paramref name="forEvent"/> (its title, its folder). <paramref name="openPage"/> opens its page in the
    /// library window. False when it couldn't start, which a notice or a toast has said why.
    /// </summary>
    static Task<bool> StartRecordingAsync(string className, CalendarEvent? forEvent, bool openPage) =>
        BeginAsync(() => host.RecordAsync(className, forEvent), openPage);

    /// <summary>Keep recording a note that stopped, minutes or days ago: what's said now goes on the end of its
    /// transcript, its page opens as the recording's, and once it stops its notes are written again from all of it.
    /// With something else recording it asks first, as an event does.</summary>
    static void ContinueRecording(string id)
    {
        if (host.Recorder.Current is { } live)
        {
            if (live.Id == id)
            {
                OpenRecording();
                return;
            }
            string now = live.Title.Length > 0 ? live.Title : live.ClassName.Length > 0 ? ClassNames.Display(live.ClassName) : "this recording";
            library.Confirming = new ConfirmAsk($"Stop recording {now} and keep recording this one?",
                SavedSoFar, "Stop and switch", () =>
                {
                    StopRecording(stay: true);
                    _ = BeginAsync(() => host.ContinueAsync(id), openPage: true);
                });
            return;
        }
        _ = BeginAsync(() => host.ContinueAsync(id), openPage: true);
    }

    static async Task<bool> BeginAsync(Func<Task<RecordStart>> begin, bool openPage)
    {
        if (host.Recorder.Current is not null || askingForMic) return false;
        // What's typed in a planned note's page goes with it into the recording.
        FlushTyped();
        if (host.Settings.Role == AppRole.Library)
        {
            Toast("This computer is your library", "Record on your laptop: this one keeps the lectures and writes the notes.", "Settings", SettingsFromAnywhere);
            return false;
        }
        if (!host.ModelReady)
        {
            var (title, text, action) = NoticeWords.NoModelYet(host.Downloading);
            Toast(title, text, action, () => _ = host.DownloadModelAsync());
            return false;
        }
        RecordStart start;
        askingForMic = true;
        try
        {
            // Not asked yet: macOS's own prompt is all the student sees until they answer; recording starts after.
            start = await begin();
        }
        catch (Exception e) when (e is InvalidOperationException or PlatformNotSupportedException)
        {
            Toast("Couldn't start recording", e.Message, null, null);
            Refresh();
            return false;
        }
        finally
        {
            askingForMic = false;
        }
        if (start.Trouble is { } trouble)
        {
            // The recording didn't start: this stays until it's closed, or until one does start.
            Notify(new Notice
            {
                Title = trouble.Title, Text = trouble.Detail, ActionLabel = trouble.HasAction ? trouble.ActionLabel : null,
                Act = trouble.ActionUrl.Length > 0 ? () => Dialogs.OpenUrl(trouble.ActionUrl) : null,
                UntilClosed = true, StillTrue = () => host.Recorder.Current is null,
            });
            Refresh();
            return false;
        }
        if (start.Lecture is not { } l)
        {
            Refresh();
            return false;
        }
        liveId = l.Id;
        recorder.Lines.Clear();
        recorder.QuickWords = host.LiveWordsOn;
        // Written down after class: nothing to show or ask about until it stops, and the recorder says so.
        recorder.AfterClass = l.AfterClass;
        recorder.Ask = l.AfterClass ? null : LiveAsk();
        panel.LastLine = l.AfterClass ? "Only recording. It's written down after class." : "";
        panelWindow?.Hide();
        Refresh();
        if (openPage)
        {
            Remember();
            ShowRecordingPage();
        }
        ShowRecorderIfAway();
        return true;
    }

    /// <summary>
    /// A click on an event in Coming up or Up next: it starts recording into the event's folder, named for the event,
    /// and its page opens. With something already recording it asks first ("Stop recording Data Mining and start
    /// Capstone team sync?"); a click on the event that's already being recorded just opens its page.
    /// </summary>
    static async Task RecordEventAsync(string eventId)
    {
        if (host.Calendars.Events.FirstOrDefault(e => e.Id == eventId) is not { } pick)
        {
            // It's gone from the calendar since the list was drawn.
            UpdateComingUp();
            if (overviewOf == "") _ = ShowHomeAsync();
            return;
        }
        // Not started yet: its note opens, not recording. Recording starts on its own when the event does.
        if (pick.Start > DateTimeOffset.Now + PlanAhead && host.Settings.Role != AppRole.Library)
        {
            var planned = host.PlanEvent(pick);
            if (host.Recorder.Current?.Id == planned.Id) OpenRecording();
            else OpenLecture(planned.Id);
            Refresh();
            return;
        }
        if (host.Recorder.Current is { } live)
        {
            if (live.Event?.Id == eventId)
            {
                OpenRecording();
                return;
            }
            string now = live.Title.Length > 0 ? live.Title : live.ClassName.Length > 0 ? ClassNames.Display(live.ClassName) : "this recording";
            library.Confirming = new ConfirmAsk($"Stop recording {now} and start {pick.Title}?",
                SavedSoFar, "Stop and start", () =>
                {
                    StopRecording(stay: true);
                    _ = StartRecordingAsync("", pick, openPage: true);
                });
            return;
        }
        await StartRecordingAsync("", pick, openPage: true);
    }

    /// <summary>An event this close to its start records when it's clicked, as one that's on does; further off, its
    /// note opens and waits for it.</summary>
    static readonly TimeSpan PlanAhead = TimeSpan.FromMinutes(1);

    /// <summary>
    /// A planned note's page: the event's name and folder, when it starts and that recording starts on its own then
    /// (with Start now), and the student's own notes, kept as they're typed.
    /// </summary>
    static void ShowPlannedPage(Lecture l)
    {
        string id = l.Id, cls = l.ClassName, noun = Nouns.For(cls, cls.Length > 0 ? host.GroupOf(cls) : "");
        if (library.Note is { Planned: true } shown && shown.Id == id && library.ShowNotePage) return;
        openClass = cls.Length > 0 ? cls : null;
        SelectInSidebar(openClass);
        var starts = Services.AppHost.PlanStarts(l)?.ToLocalTime();
        string at = starts is { } s ? s.ToString(s.Date == DateTime.Today ? "h:mm tt" : "ddd d MMM 'at' h:mm tt", CultureInfo.InvariantCulture) : "";
        bool past = starts is { } st && st <= DateTimeOffset.Now;
        var note = new NoteModel
        {
            Id = id, ClassName = cls, Dot = DotFor(cls), Noun = noun, When = NoteWhen(starts, null), Title = WorkingTitle(l), Local = true,
            Planned = true, Typed = l.Notes, Writer = NotesWriter(), NotesOff = NotesOff, Stamp = LocalStamp(l),
            PlanWords = past ? "Not recorded. Start recording when you're ready." : $"Starts at {at}. Recording starts on its own then.",
            OnStartNow = () => ContinueRecording(id),
            OnRename = title =>
            {
                host.TitleLecture(id, title);
                return Task.FromResult(true);
            },
        }.Titled();
        note.OnTyped = text => KeepTypedSoon(id, text);
        ClearPane();
        library.Overview = null;
        library.Note = note;
    }

    static DateTime lastPlanLook = DateTime.MinValue;
    static readonly HashSet<string> plansTried = [];

    /// <summary>
    /// Every few seconds: a planned note whose event has started records now, unless something else is recording
    /// (that one isn't cut short) or it was tried already and couldn't start. Its page follows if it's the one showing,
    /// and a toast says it started, with Stop. Planned notes whose event ended with nothing in them go.
    /// </summary>
    static void StartPlansOnTime()
    {
        if (DateTime.UtcNow - lastPlanLook < TimeSpan.FromSeconds(5)) return;
        lastPlanLook = DateTime.UtcNow;
        if (host.Settings.Role == AppRole.Library) return;
        var now = DateTimeOffset.Now;
        host.ForgetEndedPlans(now);
        if (host.Recorder.Current is not null || askingForMic || host.DuePlan(now) is not { } due || !plansTried.Add(due.Id)) return;
        bool showing = library.Note is { Planned: true } p && p.Id == due.Id && library.ShowNotePage && mainWindow?.IsVisible == true;
        string name = WorkingTitle(due);
        Program.Log($"[calendar] {due.Id}: its event started: recording");
        _ = StartPlanAsync(due.Id, name, showing);
    }

    static async Task StartPlanAsync(string id, string name, bool openPage)
    {
        if (await BeginAsync(() => host.ContinueAsync(id), openPage)) Toast($"Recording {name}", "It started on time, as planned.", "Stop", () => StopRecording());
    }

    /// <summary>The page of what's recording, from wherever the window is.</summary>
    static void OpenRecording()
    {
        if (host.Recorder.Current is null) return;
        if (library.Note is { Live: true } && library.ShowNotePage) return;
        Remember();
        ShowRecordingPage();
    }

    /// <summary>
    /// The page of the recording under way: its bar (the level, the time, Pause and Stop), its folder and what it's
    /// called, a title that can be typed over, the student's own notes (kept with the recording as they're typed), and
    /// on the other tab what's been heard so far. The ask bar asks about what's been said so far.
    /// </summary>
    static void ShowRecordingPage()
    {
        if (host.Recorder.Current is not { } l) return;
        ++pageTurn;
        LeavePage();
        openLecture = l.Id;
        string cls = l.ClassName;
        openClass = cls.Length > 0 ? cls : null;
        SelectInSidebar(openClass);
        if (library.Note is { Live: true } shown && shown.Id == l.Id)
        {
            // Already its page: only what was over it goes.
            library.NarrowDetail = false;
            library.Assignment = null;
            library.Reader = null;
            library.CanvasClass = null;
            library.Overview = null;
            host.WatchLiveWords(WatchingWords());
            return;
        }
        string id = l.Id;
        string noun = Nouns.For(cls, cls.Length > 0 ? host.GroupOf(cls) : "");
        var note = new NoteModel
        {
            Id = id, ClassName = cls, Dot = DotFor(cls), Noun = noun, When = NoteWhen(l.StartedAt, null), Title = WorkingTitle(l),
            Live = true, Recorder = recorder, Typed = l.Notes, Writer = NotesWriter(), NotesOff = NotesOff,
            OnRename = title =>
            {
                host.TitleLecture(id, title);
                return Task.FromResult(true);
            },
        }.Titled().Following();
        note.OnTyped = text => KeepTypedSoon(id, text);
        ClearPane();
        library.Overview = null;
        library.Note = note;
        library.Ask = recorder.Ask;
        host.WatchLiveWords(WatchingWords());
    }

    /// <summary>Who writes a lecture's notes, as the hint under the student's own names them ("Claude"): the AI the
    /// library is set to write notes with, once the app has heard which; until then, "your library".</summary>
    static string notesWriter = "";

    static string NotesWriter() => notesWriter.Length > 0 ? notesWriter : "your library";

    /// <summary>The library writes no notes (after setup's "No AI for now", or switched off in Settings → Notes and
    /// sorting): what's recorded is filed with its transcript and nothing more, so nothing here says notes are on
    /// their way, whatever AI the library names. False until the library has said.</summary>
    static bool NotesOff => host.Overview is { } said && said.ContainsKey("notes_model") && said["notes_model"] is null;

    /// <summary>What's said of the recording that stops so another can start: it's saved, and (while the library
    /// writes notes) gets its notes like any other.</summary>
    static string SavedSoFar => NotesOff ? "What's recorded so far is saved." : "What's recorded so far is saved, and its notes are written as usual.";

    /// <summary>Asks the library which AI writes its notes (when the window is read: it can change in Settings).</summary>
    static async Task HearNotesWriterAsync()
    {
        try
        {
            if (host.Remote() is not null && await Ai().EnginesAsync() is { Notes.Length: > 0 } ai) notesWriter = Core.Ai.Engines.Name(ai.Notes);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or LibraryRefusedException or System.Text.Json.JsonException)
        {
        }
    }

    // --- what's typed while it records ---------------------------------------------------------------------------------

    static DispatcherTimer? typedTimer;
    static (string Id, string Text)? typedWaiting;

    /// <summary>What's typed in a recording's notes is kept with the recording a moment after the last key (not on
    /// every one: the recording's own record is written each time).</summary>
    static void KeepTypedSoon(string id, string text)
    {
        typedWaiting = (id, text);
        Interlocked.Exchange(ref lastNoteTyping, DateTime.UtcNow.Ticks);
        if (typedTimer is null)
        {
            typedTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.2) };
            typedTimer.Tick += (_, _) => FlushTyped();
        }
        typedTimer.Stop();
        typedTimer.Start();
    }

    /// <summary>What's typed and not yet kept is kept now: before the recording stops, the page is left, or the app quits.</summary>
    static void FlushTyped()
    {
        typedTimer?.Stop();
        if (typedWaiting is not var (id, text)) return;
        typedWaiting = null;
        try
        {
            host.KeepTypedNotes(id, text);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Program.Log($"[notes] what was typed couldn't be kept: {e.Message}");
        }
    }

    // --- the small recorder ----------------------------------------------------------------------------------------------

    /// <summary>The recording's page is on screen in the window in front: everything the small recorder shows is there.</summary>
    static bool RecordingPageInFront => mainWindow is { IsVisible: true, IsActive: true } && library.Note is { Live: true } && library.ShowNotePage;

    /// <summary>The small recorder floats while something records and the library window isn't the one in front. In
    /// front, the window says it all (its Record button, the sidebar, the recording's own page), so the small one
    /// goes, unless the student opened it up themselves.</summary>
    static void ShowRecorderIfAway()
    {
        if (quitting) return;
        if (host.Recorder.Current is null)
        {
            recorderWindow?.Hide();
            UpdateDock();
            return;
        }
        bool front = mainWindow is { IsVisible: true, IsActive: true };
        if (!front) ShowRecorder(recorder.Expanded && recorderWindow?.IsVisible == true);
        else if (recorderWindow?.IsVisible == true && !recorder.Expanded) recorderWindow.Hide();
        UpdateDock();
        host.WatchLiveWords(WatchingWords());
    }
}
