using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Threading;
using StudyStash.App.Controls;
using StudyStash.App.Platform;
using StudyStash.App.Services;
using StudyStash.App.ViewModels;
using StudyStash.App.Views;
using StudyStash.App.Windows;
using StudyStash.Core;

namespace StudyStash.App;

/// <summary>
/// The lecture page's "Get study notes from this lecture": which AI writes the notes, asked once, where it means
/// something. Continue with Google (Gemini, free with a personal Google account), Claude or ChatGPT gets that AI ready
/// in a small window of its own (guided setup's two screens: its maker's installer, then its maker's own sign-in page;
/// no token is ever read or kept here), and once it's ready the library writes with it: this lecture's notes, every
/// lecture filed without notes, and every one after. Google turning down a school or work account, or a used-up
/// allowance, comes back to the card in plain words, with copy and paste offered meanwhile.
/// </summary>
public static partial class Shell
{
    /// <summary>The small window getting an AI ready, while it's open.</summary>
    static Window? readyWindow;

    /// <summary>The card's buttons: <paramref name="engine"/> ("gemini", "claude", "codex") is got ready, then writes
    /// <paramref name="lectureId"/>'s notes (Home's "Notes need an AI" card has no lecture: null).</summary>
    static void GetNotesWith(string engine, string? lectureId)
    {
        if (readyWindow is { IsVisible: true } open)
        {
            AppWindows.Raise(open);
            return;
        }
        // Guided setup's model, for its install and sign-in screens alone (no chat, no setup tools' door): over a setup
        // of its own that nothing else reads.
        var guided = MakeGuided(SetupModel.For(Skin.Current, AppRole.Both));
        bool done = false;
        var view = Skin.Current == SkinKind.Mac ? (Control)new MacGetReady { DataContext = guided, DrawChrome = false } : new WinGetReady { DataContext = guided, DrawChrome = false };
        // Opened from the library, it doesn't bring the app forward by itself.
        AppWindows.Open(new WindowSpec("get-ready", guided.ReadyTitle, view) { AppForward = false }, w =>
        {
            guided.ReadyForNotes = ready =>
            {
                done = true;
                w.Close();
                Run(() => UseForNotesAsync(ready, lectureId), "UseForNotes");
            };
            guided.PropertyChanged += (_, e) =>
            {
                // Back (or "Carry on without it"): the window goes, nothing changed. Ready also passes through here first,
                // which ReadyForNotes, said straight after, tells apart.
                if (e.PropertyName == nameof(GuidedSetupModel.Screen) && guided.Screen == GuidedScreen.Manual)
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (!done) w.Close();
                    });
                if (e.PropertyName == nameof(GuidedSetupModel.Picked)) w.Title = guided.ReadyTitle;
                if (e.PropertyName != nameof(GuidedSetupModel.PlanProblem) || guided.Cli.Id != "gemini" || done) return;
                if (guided.PlanProblem == Core.Ai.ChatProblem.Plan)
                {
                    // Google turned the account down (a school or work one): the card says to use a personal one, and
                    // offers copy and paste.
                    done = true;
                    w.Close();
                    if (lectureId is not null) ShowNotesFallback(lectureId, NotesFallback.GoogleAccount);
                    else Toast("Use a personal Google account", "Google doesn't let school and work accounts use Gemini from other apps. Continue with a personal one (like Gmail), or connect Claude or ChatGPT in Settings.", null, null);
                }
                else if (guided.PlanProblem == Core.Ai.ChatProblem.Limit)
                {
                    // Signed in, with this week's allowance used up: Gemini writes the notes all the same, once it's back
                    // (the lecture says when).
                    done = true;
                    w.Close();
                    Run(() => UseForNotesAsync("gemini", lectureId), "UseForNotes");
                }
            };
            w.Closed += (_, _) =>
            {
                guided.Dispose();
                if (readyWindow == w) readyWindow = null;
            };
            readyWindow = w;
        });
        Run(() => guided.GetReadyForNotesAsync(engine), "guided.GetReadyForNotes");
    }

    /// <summary>
    /// <paramref name="engine"/> is ready on this computer: the library writes the notes with it from now on and answers
    /// questions with it, notes and sorting turned on, and every lecture filed without notes is queued to have them;
    /// <paramref name="lectureId"/>'s page is read again (its notes on their way).
    /// </summary>
    static async Task UseForNotesAsync(string engine, string? lectureId)
    {
        if (!await UseAiAsync(engine))
        {
            Toast("Your library didn't answer", "Nothing changed. Try again in a moment.", null, null);
            return;
        }
        if (lectureId is not null && pageState.Lecture == lectureId) Run(() => ShowLectureAsync(lectureId, library.Note?.ShowTranscript == true), "ShowLecture");
        else RequestRefresh(); // Home's "Notes need an AI" goes
    }

    /// <summary>
    /// <paramref name="engine"/> writes the notes and answers questions from now on (the first run's AI step, a lecture's
    /// card, Home's): notes and sorting on, and every lecture filed without notes queued to have them. False when the
    /// library didn't take it (it didn't answer, or runs an older Syft Notes).
    /// </summary>
    static async Task<bool> UseAiAsync(string engine)
    {
        if (host.Remote() is not { } lib) return false;
        try
        {
            if (await host.AiStatus.Library.DefaultsAsync(notes: engine, ask: engine) is not { } now)
            {
                host.Log("[notes] the library runs an older Syft Notes: it can't be told which AI writes the notes");
                return false;
            }
            host.AiStatus.Take(now); // every screen says who writes the notes now
            await WriteNotesAsync(true);
            host.Save(s => s.SetupAi = engine);
            var queued = await lib.SettingsAsync(HttpMethod.Post, "/write-missing");
            int n = queued?["queued"] is JsonValue v && v.TryGetValue(out int count) ? count : 0;
            host.Log($"[notes] {Core.Ai.Engines.Name(engine)} writes the notes now: {n} lecture(s) queued for theirs");
            return true;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or LibraryRefusedException)
        {
            host.Log($"[notes] couldn't switch the notes to {engine}: {e.Message}");
            return false;
        }
    }

    /// <summary>The card on <paramref name="lectureId"/>'s page says why it can't just offer the AIs.</summary>
    static void ShowNotesFallback(string lectureId, NotesFallback why)
    {
        if (library.FreeAi is { } free && free.LectureId == lectureId) free.Fallback = why;
    }
}
