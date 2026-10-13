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
using StudyStash.App.Platform;

namespace StudyStash.App;

/// <summary>The shell's library side: the sidebar, a note's page (its notes, its transcript and its ask bar; while it
/// records, the student's own notes and what's been heard), search, and deleting and exporting notes. The pages
/// beside it (Home, Due, a folder's page, Canvas's lists) are in Shell.Overview.cs.</summary>
public static partial class Shell
{
    /// <summary>The folder in mind: the one whose page showed last, or whose note is open.</summary>
    static string? openClass;
    /// <summary>The note whose page is open; null when another page is.</summary>
    static string? openLecture;
    static int searchTurn;
    /// <summary>Every whole load of the library window takes the next turn: one that's no longer the latest by the
    /// time it's back stops instead of clobbering what a later one already drew.</summary>
    static int libraryTurn;
    static bool libraryReloadQueued;

    /// <summary>A lecture was filed, or the library just came back: if its window is open, the class showing gets its
    /// new note within a second (not on every change in that second, just the last one).</summary>
    static void RequestLibraryReload(bool whole = false)
    {
        libraryReloadWhole |= whole;
        if (libraryReloadQueued || mainWindow?.IsVisible != true) return;
        libraryReloadQueued = true;
        Avalonia.Threading.DispatcherTimer.RunOnce(() =>
        {
            libraryReloadQueued = false;
            bool all = libraryReloadWhole;
            libraryReloadWhole = false;
            if (quitting || mainWindow?.IsVisible != true) return;
            // Back after being away (the window opened before the library was up, say): its classes too, not just the
            // page showing, which may be "Can't reach your library" with no classes beside it.
            _ = all ? LoadLibraryAsync() : ReshowAsync();
            // The page showing has its new note; the sidebar's counts beside it follow (a whole load does its own).
            if (!all) _ = RefreshCountsAsync();
        }, TimeSpan.FromSeconds(1));
    }

    /// <summary>The sidebar's lecture counts, as the library says them now: only the numbers change, not the page showing.</summary>
    static async Task RefreshCountsAsync()
    {
        await host.CheckLibraryAsync();
        if (quitting || host.Library != LibraryState.Connected) return;
        library.ShowCounts(host.Classes().Select(c => (c.Name, c.Lectures)), host.Overview?["unsorted"]?.GetValue<int>() ?? 0);
    }

    static bool libraryReloadWhole;

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

    /// <summary>Puts a folder in a sidebar group (Clubs, Work, or one of the student's own; "" back to Classes), then
    /// shows the sidebar again. What its recordings are called follows: lectures in Classes, meetings anywhere else.</summary>
    static async Task MoveToFolderAsync(string cls, string group)
    {
        if (host.Remote() is not { } lib) return;
        try
        {
            await lib.ClassGroupAsync(cls, group);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or LibraryRefusedException)
        {
            Toast("Couldn't move it", e.Message, null, null);
            return;
        }
        await LoadLibraryAsync();
    }

    /// <summary>The groups every sidebar offers a folder, whether or not anything is in them yet.</summary>
    static readonly string[] StandardGroups = ["Clubs", "Work"];

    /// <summary>The sidebar's folders, as the library has them now: Classes first, then each other group (Clubs, Work,
    /// one of the student's own) under its name, in the order they were made.</summary>
    static void ShowSidebar()
    {
        var due = library.Classes.FirstOrDefault(c => c.IsDue);
        library.Classes.Clear();
        if (due is not null) library.Classes.Add(due);
        var all = host.Classes().Select(c => (c.Name, c.Color, c.Lectures, Group: host.GroupOf(c.Name))).ToList();
        var groups = all.Select(c => c.Group).Where(g => g.Length > 0).Distinct().ToList();
        var offered = StandardGroups.Concat(groups).Distinct().ToList();
        foreach (var group in new[] { "" }.Concat(groups))
        {
            bool first = true;
            foreach (var (name, color, count, _) in all.Where(c => c.Group == group))
            {
                var item = new ClassItem { Name = name, Dot = Skin.ClassDot(color), Count = count, Group = group, Section = first && group.Length > 0 ? group : "" };
                item.MoveChoices = [.. new[] { "" }.Concat(offered).Where(g => g != group)
                    .Select(g => new FolderChoice(g.Length == 0 ? "Classes" : g, new CommunityToolkit.Mvvm.Input.RelayCommand(() => _ = MoveToFolderAsync(name, g)))),
                    new FolderChoice("New group…", new CommunityToolkit.Mvvm.Input.RelayCommand(() => library.NamingFolder = new NewFolderAsk(name)))];
                library.Classes.Add(item);
                first = false;
            }
        }
        // A folder removed (in Settings, say) isn't left in mind.
        if (openClass is not null && library.Classes.All(c => c.Name != openClass)) openClass = null;
        if (overviewOf is { Length: > 0 } over && over != DueHome && library.Classes.All(c => c.Name != over)) overviewOf = "";
        if (openCanvasClass is { } canvas && library.Classes.All(c => c.Name != canvas)) openCanvasClass = null;
    }

    static async Task LoadLibraryAsync()
    {
        int turn = ++libraryTurn;
        await host.CheckLibraryAsync();
        if (turn != libraryTurn) return; // a later load has already taken over
        ShowSidebar();
        if (host.Library == LibraryState.Connected && !host.OlderLibrary && canvasDue is null) await LoadCanvasAsync();
        if (turn != libraryTurn) return;
        UpdateDueItem();
        UpdateNextDue();
        int unsorted = host.Overview?["unsorted"]?.GetValue<int>() ?? 0;
        library.UnsortedCount = unsorted;
        if (host.Library != LibraryState.Connected || host.OlderLibrary)
        {
            // A recording can still be taken (and its notes typed) with the library away: its page stays.
            if (library.Note is { Live: true }) return;
            ++pageTurn;
            ClearPane();
            library.Overview = null;
            SelectInSidebar(null, home: true);
            library.Empty = host.OlderLibrary ? "Your library runs an older Study Stash. It files your lectures, but update it to browse, search and ask here."
                : host.Library switch
            {
                LibraryState.NotSetUp => "Connect to your library in Settings to see your notes here.",
                LibraryState.WrongPassword => "Your library's password changed. Sign in again in Settings.",
                _ => "Can't reach your library. What you record waits on this computer until it's back.",
            };
            return;
        }
        library.Empty = null;
        AskForSupportIfDue(host.Classes().Sum(c => c.Lectures) + unsorted);
        _ = HearNotesWriterAsync();
        await ReshowAsync();
    }

    /// <summary>The page the window is on, read again: a note filed, a class linked, the library back. No step in
    /// Back and Forward.</summary>
    static async Task ReshowAsync()
    {
        bool was = restoring;
        restoring = true;
        try
        {
            await GoToAsync(Here());
        }
        finally
        {
            restoring = was;
        }
    }

    /// <summary>The library window asks for a tip atop the lecture list when <see cref="SupportAsk.Due"/> says it's
    /// time (the library holds a few lectures, nothing is recording, setup isn't open), and puts the ask away when not.</summary>
    static void AskForSupportIfDue(int lectures)
    {
        bool due = SupportAsk.Due(host.Settings, lectures, recording: host.Recorder.Current is not null,
            settingUp: setupWindow?.IsVisible == true, DateTimeOffset.Now);
        if (due) library.AskForSupport(last: SupportAsk.IsLast(host.Settings));
        else library.Support = null;
    }

    /// <summary>A note's page: a lecture, a meeting or a recording, filling the pane. One that's being recorded shows
    /// as that (its bar, the student's own notes, what's been heard); one this computer hasn't sent to the library yet
    /// shows what it has; any other is the library's.</summary>
    static async Task ShowLectureAsync(string id, bool transcript = false)
    {
        if (host.Recorder.Current is { } live && live.Id == id)
        {
            ShowRecordingPage();
            return;
        }
        int turn = ++pageTurn;
        LeavePage();
        openLecture = id;
        bool showing = library.Overview is null && library.Assignment is null && library.CanvasClass is null && library.Note is { Live: false } on && on.Id == id;
        // Its notes are being edited: the page stays as it is under the student's typing, however it came to be shown
        // again (a new note was filed, a file was attached).
        if (showing && library.Notes is { Editing: true } edited && edited.LectureId == id)
        {
            if (transcript) library.Note!.ShowTranscript = true;
            return;
        }
        JsonObject? l = null;
        bool reached = false;
        if (host.Remote() is { } lib)
        {
            try
            {
                l = await lib.LectureAsync(id);
                reached = true;
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or LibraryRefusedException)
            {
            }
        }
        // Another page was asked for while this one loaded (a notification's Open note, a click): that one shows.
        if (turn != pageTurn) return;
        if (l is null)
        {
            // Not in the library (yet): one recorded here that's still being written down or sent shows what it has.
            if (host.Lectures.Get(id) is { } local) ShowLocalLecture(local, transcript);
            else if (reached && !showing) await ShowHomeAsync(); // it's gone (deleted on another computer): nothing to open
            return;
        }
        string cls = S(l["class"]) is { } c && c != Configs.Unsorted ? c : "";
        openClass = cls.Length > 0 ? cls : null;
        SelectInSidebar(openClass);
        string status = S(l["status"]), notes = S(l["notes"]), typed = S(l["typed"]), noun = Nouns.For(cls, cls.Length > 0 ? host.GroupOf(cls) : "");
        bool writing = status is "queued" or "working";
        string stamp = string.Join("\u0001", S(l["title"]), cls, status, notes, typed, S(l["error"]), S(l["transcript"]).Length, S(l["updated"]));
        // Read again and nothing changed: the page stays as it is (where it's scrolled to, what's typed in Ask).
        if (showing && library.Note!.Stamp == stamp)
        {
            if (transcript) library.Note.ShowTranscript = true;
            return;
        }
        // No notes written (yet): what the student typed while it recorded is all there is to read, and it's theirs.
        bool mine = notes.Length == 0 && typed.Trim().Length > 0;
        var note = new NoteModel
        {
            Id = id, ClassName = cls, Dot = DotFor(cls), Noun = noun, When = NoteWhen(Date(S(l["date"])), l["seconds"] is JsonValue v && v.TryGetValue(out double s) ? s : null),
            Title = S(l["title"]), Markdown = mine ? TypedNotes.AllMine(typed) : notes, Mine = mine, Stamp = stamp,
            Pending = writing ? $"Your library is writing the notes for this {noun}. They show here when they're done."
                : notes.Length == 0 ? S(l["error"]) is { Length: > 0 } err ? err : mine ? null : $"There are no notes for this {noun}." : null,
            ShowTranscript = transcript || (showing && library.Note!.ShowTranscript),
            // The library doesn't take a new name while it's still writing the notes (it would write its own over it).
            OnRename = host.Remote() is { } remote && !host.OlderLibrary ? title => RenameAsync(remote, id, title) : null,
            OnContinue = ContinueFor(id),
        }.Titled();
        // A transcript without times (one pasted from another app) reads a paragraph per line it came with (each
        // speaker's turn), not as one wall of text.
        bool timed = TimedText.HasTimes(S(l["transcript"]));
        foreach (var line in timed ? TimedText.Parse(S(l["transcript"]))
                     : S(l["transcript"]).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                         .SelectMany(p => Paragraphs(p)).Select(p => new Spoken(0, 0, p)))
        {
            note.Transcript.Add(new HeardLine { Time = timed ? TimedText.Clock(line.Start) : "", Text = line.Text, Start = timed ? line.Start : null });
            if (timed) note.Spoken.Add(line);
        }
        FlushTyped();
        library.NarrowDetail = false;
        library.Assignment = null;
        library.Reader = null;
        library.CanvasClass = null;
        library.Overview = null;
        library.Note = note;
        ShowLectureAi(l, note);
        ShowLectureFiles(note);
    }

    /// <summary>When a note was, and how long: "Monday 12 October at 9:30 AM, 52 min" (with its year once that isn't
    /// this one).</summary>
    internal static string NoteWhen(DateTimeOffset? date, double? seconds)
    {
        string when = date?.LocalDateTime is { } d
            ? d.ToString(d.Year == DateTime.Today.Year ? "dddd d MMMM 'at' h:mm tt" : "dddd d MMMM yyyy 'at' h:mm tt", CultureInfo.InvariantCulture) : "";
        return string.Join(", ", new[] { when, seconds is { } s ? TimedText.Length(s) : "" }.Where(x => x.Length > 0));
    }

    /// <summary>The title a recording goes by until it has one of its own: its event's or its folder's name and the
    /// day ("Machine Learning, Mon 12 Oct"), or with neither "Recording, Mon 12 Oct at 1:45 PM".</summary>
    internal static string WorkingTitle(Lecture l)
    {
        var at = l.StartedAt.LocalDateTime;
        string day = at.ToString("ddd d MMM", CultureInfo.InvariantCulture);
        if (l.TitleTyped && l.Title.Length > 0) return l.Title;
        if (l.Title.Length > 0) return $"{ClassNames.Display(l.Title)}, {day}";
        return l.ClassName.Length > 0 ? $"{ClassNames.Display(l.ClassName)}, {day}" : $"Recording, {day} at {at.ToString("h:mm tt", CultureInfo.InvariantCulture)}";
    }

    static async Task<bool> RenameAsync(RemoteLibrary lib, string id, string title)
    {
        try
        {
            await lib.RenameAsync(id, title);
            return true;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or LibraryRefusedException)
        {
            Toast("Couldn't rename it", e is LibraryRefusedException ? e.Message : "Your library didn't answer. Try again in a moment.", null, null);
            return false;
        }
    }

    /// <summary>Keep recording, for a note this computer recorded (it keeps the transcript to add to); null for one
    /// recorded elsewhere, on the computer that only keeps the library, or the one recording now.</summary>
    static Action? ContinueFor(string id) =>
        host.Settings.Role != AppRole.Library && host.Lectures.Get(id) is { State: not (LectureState.Recording or LectureState.Paused) }
            ? () => ContinueRecording(id) : null;

    /// <summary>What a recording's page on this computer was drawn from: when it reads differently, the page is drawn again.</summary>
    static string LocalStamp(Lecture l) => string.Join("\u0001", "local", l.State, l.Title, l.ClassName, l.Notes, l.Error, l.Segments.Count);
    static DateTime lastLocalLook = DateTime.MinValue;

    /// <summary>A recording this computer made that the library doesn't have yet (it's still being written down here,
    /// or waiting to be sent): its page shows what there is (what was typed while it recorded, what's been written
    /// down so far) and says where it is on its way.</summary>
    static void ShowLocalLecture(Lecture l, bool transcript)
    {
        string cls = l.ClassName, noun = Nouns.For(cls, cls.Length > 0 ? host.GroupOf(cls) : "");
        string stamp = LocalStamp(l);
        bool showing = library.Overview is null && library.Assignment is null && library.CanvasClass is null && library.Note is { Live: false } on && on.Id == l.Id;
        if (showing && library.Note!.Stamp == stamp) return;
        openClass = cls.Length > 0 ? cls : null;
        SelectInSidebar(openClass);
        string id = l.Id;
        var note = new NoteModel
        {
            Id = id, ClassName = cls, Dot = DotFor(cls), Noun = noun, When = NoteWhen(l.StartedAt, l.Seconds > 0 ? l.Seconds : null), Title = WorkingTitle(l),
            Markdown = l.Notes.Trim().Length > 0 ? TypedNotes.AllMine(l.Notes.Trim()) : "", Mine = true, Stamp = stamp, Local = true,
            Pending = l.State switch
            {
                LectureState.Failed => l.Error.Length > 0 ? l.Error : $"This {noun} couldn't be written down.",
                LectureState.Transcribing => $"This {noun} is being written down on this computer. Its notes come once that's done and your library has it.",
                _ => host.Library == LibraryState.Connected ? $"On its way to your library, which writes the notes for this {noun}. They show here when they're done."
                    : $"Waiting for your library. The notes for this {noun} are written once it's back.",
            },
            ShowTranscript = transcript || (showing && library.Note!.ShowTranscript),
            // Until it's sent, a name typed here goes with it; after that the library's copy is the one to rename.
            OnRename = l.State is LectureState.Transcribing ? title =>
            {
                host.TitleLecture(id, title);
                return Task.FromResult(true);
            } : null,
            OnContinue = ContinueFor(id),
        }.Titled();
        foreach (var line in l.Segments)
        {
            note.Transcript.Add(new HeardLine { Time = TimedText.Clock(line.Start), Text = line.Text, Start = line.Start });
            note.Spoken.Add(line);
        }
        ClearPane();
        library.Overview = null;
        library.Note = note;
    }

    /// <summary>A long run of speech as paragraphs of a few sentences each (about <paramref name="size"/> characters),
    /// broken where a sentence ends: a whole lecture copied as one line otherwise reads as one wall of text.</summary>
    internal static IEnumerable<string> Paragraphs(string text, int size = 600)
    {
        int start = 0;
        while (text.Length - start > size * 3 / 2)
        {
            int end = -1;
            for (int i = start + size; i < Math.Min(text.Length - 1, start + size * 2); i++)
                if (text[i] is '.' or '?' or '!' && text[i + 1] == ' ')
                {
                    end = i + 1;
                    break;
                }
            if (end < 0) break;
            yield return text[start..end].Trim();
            start = end;
        }
        yield return text[start..].Trim();
    }

    /// <summary>The lecture's notes, which another engine can rewrite (while they aren't still being written), and a
    /// fresh ask bar for it: a new lecture starts a new conversation.</summary>
    static void ShowLectureAi(JsonObject l, NoteModel note)
    {
        var ai = Ai();
        // The new notes replace the old ones in one step: with none in between, the page never shows its plain Markdown
        // (a view of the same notes that's only there for a lecture whose notes aren't written yet) for a moment, which
        // built every diagram and plot in the lecture a second time just to hide it again.
        var before = library.Notes;
        AiNotesModel? notes = null;
        bool fresh = false;
        if (!note.HasPending)
        {
            // Notes with an edit in them come back as they were left: the ones on screen (Edit pressed while this
            // lecture was loading), or ones kept from before another lecture was opened.
            if (before is { Editing: true } && before.LectureId == note.Id) notes = before;
            else if (!unsavedNotes.Remove(note.Id, out notes))
            {
                fresh = true;
                notes = new AiNotesModel(ai);
                FollowEdits(notes);
                notes.NotesChanged += () => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    if (openLecture == note.Id) _ = ShowLectureAsync(note.Id, library.Note?.ShowTranscript == true);
                });
            }
        }
        if (!ReferenceEquals(before, notes))
        {
            library.Notes = notes;
            LeaveNotes(before);
        }
        if (fresh) _ = notes!.Load(note.Id, note.Markdown, S(l["notes_model"]), S(l["updated"]));
        // Typing that was in this lecture's editor when the app last quit goes back into it.
        if (fresh && EarlierDrafts().Remove(note.Id, out var draft)) notes!.RestoreEdit(draft.From, draft.Text);
        else if (notes is not null && !ReferenceEquals(before, notes)) _ = notes.Refresh(); // kept: the library says where they stand now
        // The same note read again keeps its ask bar, with what's typed and answered in it.
        if (library.Ask is { } kept && kept.LectureId == note.Id && kept.Live is null) return;
        var ask = new AiAskModel(ai)
        {
            LectureId = note.Id, ClassName = note.HasClass ? note.ClassName : null,
            OpenSettings = () => ShowSettings("AI"),
            OnSource = s => PlaySource(s.Id ?? note.Id, s.At),
        };
        // A note in no folder has no folder to ask about.
        if (!note.HasClass) ask.OnlyScopes("lecture", "all");
        ask.NameScopes(note.Noun, note.HasClass ? note.ClassShown : null);
        if (!ReferenceEquals(library.Ask, recorder.Ask)) library.Ask?.Stop(); // an answer about the note being left isn't wanted any more
        library.Ask = ask;
        _ = ask.Load();
    }

    /// <summary>Notes a student had typed in and not saved when they opened something else, by lecture: back in their
    /// editor, as they were, when that lecture is opened again. They're written down too (<see cref="NoteDrafts"/>),
    /// so they're still there after the app quits, updates itself or is restarted.</summary>
    static readonly Dictionary<string, AiNotesModel> unsavedNotes = [];
    /// <summary>Unsaved typing from before the app last quit, whose lecture hasn't been opened since.</summary>
    static Dictionary<string, NoteDraft>? earlierDrafts;
    static Avalonia.Threading.DispatcherTimer? draftsTimer;
    static long lastNoteTyping;

    static Dictionary<string, NoteDraft> EarlierDrafts() => earlierDrafts ??= NoteDrafts.Load(host.Home);

    /// <summary>The student typed in a lecture's notes in the last few minutes: the app doesn't restart itself for an
    /// update under their hands. (What they typed is kept either way; a forgotten edit doesn't hold an update up.)</summary>
    public static bool TypingNotes => DateTime.UtcNow.Ticks - Interlocked.Read(ref lastNoteTyping) < TimeSpan.FromMinutes(5).Ticks;

    /// <summary>Follows a lecture's editor: what's typed is written down a moment after the last key, and taken back
    /// out once it's saved or cancelled.</summary>
    static void FollowEdits(AiNotesModel notes) => notes.PropertyChanged += (_, e) =>
    {
        if (e.PropertyName == nameof(AiNotesModel.EditText) && notes.EditDirty) Interlocked.Exchange(ref lastNoteTyping, DateTime.UtcNow.Ticks);
        if (e.PropertyName is not (nameof(AiNotesModel.EditText) or nameof(AiNotesModel.Editing)) || quitting) return;
        if (draftsTimer is null)
        {
            draftsTimer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            draftsTimer.Tick += (_, _) =>
            {
                draftsTimer.Stop();
                KeepDrafts();
            };
        }
        draftsTimer.Stop();
        draftsTimer.Start();
    };

    /// <summary>Writes down every lecture's unsaved typing as it stands now: the one on the page, the ones left for
    /// another lecture, and the ones from before the app last quit that haven't been opened since.</summary>
    static void KeepDrafts()
    {
        try
        {
            string at = DateTimeOffset.UtcNow.ToString("o", System.Globalization.CultureInfo.InvariantCulture);
            var drafts = new Dictionary<string, NoteDraft>(EarlierDrafts());
            foreach (var notes in unsavedNotes.Values.Append(library?.Notes))
                if (notes?.Unsaved is { } typed) drafts[notes.LectureId] = new NoteDraft(typed.From, typed.Text, at);
            if (!NoteDrafts.Save(host.Home, drafts)) Program.Log("[notes] unsaved typing couldn't be written down (the disk may be full)");
        }
        catch (Exception e)
        {
            Program.Log($"[error] keeping unsaved notes: {e.Message}");
        }
    }

    /// <summary>Notes that leave the page stop following the library; ones with unsaved typing are kept for their
    /// lecture's next opening.</summary>
    static void LeaveNotes(AiNotesModel? notes)
    {
        if (notes is null) return;
        notes.Dispose();
        if (notes.EditDirty)
        {
            unsavedNotes[notes.LectureId] = notes;
            KeepDrafts();
        }
    }

    /// <summary>The connected library's AI: engines, asking, rewriting notes.</summary>
    static Core.Ai.IAiLibrary Ai()
    {
        var cc = host.Client();
        return new Core.Ai.AiRemote(cc.ServerUrl, cc.PoolKey);
    }

    /// <summary>A source an answer points to: its moment in the recording plays (when the audio's here), and the lecture
    /// opens at its transcript, at the line said then: marked, and the page brought to it.</summary>
    static void PlaySource(string id, double? at)
    {
        if (at is not double t)
        {
            if (id != openLecture) OpenLecture(id);
            return;
        }
        if (File.Exists(host.Lectures.AudioPath(id))) Play(id, t);
        if (id != openLecture) OpenAt(id, t);
        else library.Note?.ShowAt(t);
    }

    /// <summary>Another note's page, opened at a moment in its transcript: once the page is up (whichever read of it
    /// gets there first), the line said then is marked and the page brought to it.</summary>
    static void OpenAt(string id, double at)
    {
        void Shown(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(LibraryModel.Note)) return;
            if (openLecture == id && (library.Note is not { Live: false } n || n.Id != id)) return; // still on its way
            library.PropertyChanged -= Shown;
            // (Somewhere else was opened meanwhile: nothing to show.) After the page has finished being put up.
            if (openLecture == id) Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (openLecture == id && library.Note is { Live: false } now && now.Id == id) now.ShowAt(at);
            });
        }
        library.PropertyChanged += Shown;
        OpenLecture(id, transcript: true);
    }

    /// <summary>Opens the library at a note's page (search, a notification, an answer's source).</summary>
    static void OpenLecture(string id, bool transcript = false)
    {
        Remember();
        LeavePage();
        openLecture = id;
        bool loaded = mainWindow?.IsVisible == true && host.Library == LibraryState.Connected && library.Classes.Count > 0;
        ShowLibrary();
        // The window is already up and read: the note opens now, not after the whole window is read again.
        if (loaded || transcript) _ = ShowLectureAsync(id, transcript);
    }

    /// <summary>The ••• menu: open the class in a terminal with the AI (when the library is on this computer), the
    /// library's web page (chat, capture, history), and Settings.</summary>
    static void MoreMenu()
    {
        if (mainWindow?.Content is not Control anchor) return;
        var menu = Menu();
        string? cls = overviewOf == DueHome || overviewOf == "" ? null : openClass is { } o && o != Configs.Unsorted ? o : null;
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

    /// <summary>Deletes a lecture on the library, the same from a laptop as on the library itself: its notes,
    /// transcript and search passages go at once, and the library keeps it in its trash a few minutes for Undo. The
    /// window has already taken it out of the list; if the library refuses, the list is read again.</summary>
    static async Task<bool> DeleteLectureAsync(LectureDeletion d)
    {
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
            await LoadLibraryAsync();
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or LibraryRefusedException)
        {
            Toast("Couldn't bring it back", e.Message, null, null);
        }
    }

    /// <summary>The share/Export button's menu: download this lecture (as Markdown or a PDF), download the whole open
    /// class, and whether a download includes the transcript.</summary>
    static async Task ExportAsync()
    {
        await Task.Yield(); // opening the menu is itself synchronous; a pick below does the real, awaited work
        if (library.Note is not { } note || mainWindow?.Content is not Control anchor || mainWindow is null || host.Remote() is not { } lib) return;
        string? cls = note.HasClass ? note.ClassName : null;
        DownloadMenu.Build(cls, host.Settings.DownloadTranscripts,
            download: () => _ = NotesDownload.LectureAsync(mainWindow, lib, note.Id, NoteExport.SaveName(note.Title), host.Settings.DownloadTranscripts, MermaidSvg()),
            downloadPdf: () => _ = NotesDownload.LecturePdfAsync(mainWindow, lib, note.Id, NoteExport.SaveName(note.Title, ".pdf"), host.Settings.DownloadTranscripts,
                note.Dot is ISolidColorBrush dot ? dot.Color : Colors.Gray),
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
        string rec = Keybindings.Show(KeyAction.Record);
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
                    rows.Add(new QuickRow { Kind = QuickKind.Header, Title = "Notes", First = rows.Count == 0 });
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
                    rows.Add(new QuickRow { Kind = QuickKind.Header, Title = "Folders", First = rows.Count == 0 });
                    foreach (var c in classes)
                    {
                        int n = c["lectures"]?.GetValue<int>() ?? 0;
                        rows.Add(new QuickRow { Kind = QuickKind.Class, Title = S(c["name"]), ClassName = S(c["name"]), Dot = Skin.ClassDot(c["color"]?.GetValue<int>() ?? 0), Meta = $"{n} note{(n == 1 ? "" : "s")}" });
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
        Remember();
        LeavePage();
        overviewOf = DueHome;
        dueOnly = null;
        ShowLibrary();
    }

    /// <summary>Canvas syncs on the browser's next check, within a minute.</summary>
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
        Toast("Syncing Canvas", $"On {CanvasWords.Browser(canvasWatch?.State)}'s next check, within a minute.", null, null);
    }

    /// <summary>A passage cut down to the part round what was searched for.</summary>
    static string Excerpt(string text, string query)
    {
        text = PlainPassage(text);
        int at = Controls.Marked.Find(text, query).FirstOrDefault() is { Length: > 0 } f ? f.Start : 0;
        int from = Math.Max(0, at - 40);
        if (from > 0) from = text.IndexOf(' ', from) is int sp and >= 0 && sp < at ? sp + 1 : from;
        string cut = text[from..];
        return cut.Length > 110 ? cut[..110].TrimEnd() : cut;
    }

    /// <summary>A passage of notes as it reads, not as it's written: no Markdown's **, `, # or list markers, no
    /// link addresses, one line.</summary>
    internal static string PlainPassage(string text)
    {
        text = System.Text.RegularExpressions.Regex.Replace(text, @"!?\[([^\]]*)\]\([^)]*\)", "$1");
        text = System.Text.RegularExpressions.Regex.Replace(text, @"(?m)^\s*(#{1,6}\s+|[-*+]\s+|\d+\.\s+|>\s?)", "");
        text = System.Text.RegularExpressions.Regex.Replace(text, @"(\*\*|__|\*|`|~~)", "");
        return System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();
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
                Remember();
                LeavePage();
                // Unsorted is no folder: its notes are Home's, filtered.
                homeUnsorted = c == Configs.Unsorted;
                overviewOf = c == Configs.Unsorted ? "" : c;
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
