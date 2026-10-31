using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Threading;
using StudyStash.App.Controls;
using StudyStash.App.Platform;
using StudyStash.App.Services;
using StudyStash.App.ViewModels;
using StudyStash.App.Views;
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
    /// <paramref name="lectureId"/>'s notes.</summary>
    static void GetNotesWith(string engine, string lectureId)
    {
        if (readyWindow is { IsVisible: true } open)
        {
            Raise(open);
            return;
        }
        // Guided setup's model, for its install and sign-in screens alone (no chat, no setup tools' door): over a setup
        // of its own that nothing else reads.
        var guided = MakeGuided(SetupModel.For(Skin.Current, AppRole.Both));
        bool done = false;
        var view = Skin.Current == SkinKind.Mac ? (Control)new MacGetReady { DataContext = guided, DrawChrome = false } : new WinGetReady { DataContext = guided, DrawChrome = false };
        var w = new Window
        {
            Title = guided.ReadyTitle, CanResize = false, CanMaximize = false, Content = view,
            ExtendClientAreaToDecorationsHint = true, ExtendClientAreaTitleBarHeightHint = Skin.Current == SkinKind.Mac ? WindowHeader.MacHeight : 32,
        };
        Look.Apply(w);
        if (Skin.Current == SkinKind.Mac) MacTitleBar.Attach(w);
        WinChrome.Apply(w);
        guided.ReadyForNotes = ready =>
        {
            done = true;
            w.Close();
            _ = UseForNotesAsync(ready, lectureId);
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
                ShowNotesFallback(lectureId, NotesFallback.GoogleAccount);
            }
            else if (guided.PlanProblem == Core.Ai.ChatProblem.Limit)
            {
                // Signed in, with this week's allowance used up: Gemini writes the notes all the same, once it's back
                // (the lecture says when).
                done = true;
                w.Close();
                _ = UseForNotesAsync("gemini", lectureId);
            }
        };
        w.Closed += (_, _) =>
        {
            guided.Dispose();
            if (readyWindow == w) readyWindow = null;
            UpdateDock();
        };
        FollowView(w, view);
        readyWindow = w;
        w.Show();
        UpdateDock();
        w.Activate();
        _ = guided.GetReadyForNotesAsync(engine);
    }

    /// <summary>
    /// <paramref name="engine"/> is ready on this computer: the library writes the notes with it from now on and answers
    /// questions with it, notes and sorting turned on, and every lecture filed without notes is queued to have them;
    /// <paramref name="lectureId"/>'s page is read again (its notes on their way).
    /// </summary>
    static async Task UseForNotesAsync(string engine, string lectureId)
    {
        if (host.Remote() is not { } lib) return;
        try
        {
            if (await Ai().DefaultsAsync(notes: engine, ask: engine) is null)
            {
                Toast("Update your library", "Your library runs an older Syft Notes. Update it to have notes written.", null, null);
                return;
            }
            await WriteNotesAsync(true);
            host.Save(s => s.SetupAi = engine);
            var queued = await lib.SettingsAsync(HttpMethod.Post, "/write-missing");
            int n = queued?["queued"] is JsonValue v && v.TryGetValue(out int count) ? count : 0;
            host.Log($"[notes] {Core.Ai.Engines.Name(engine)} writes the notes now: {n} lecture(s) queued for theirs");
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or LibraryRefusedException)
        {
            host.Log($"[notes] couldn't switch the notes to {engine}: {e.Message}");
            Toast("Your library didn't answer", "Nothing changed. Try again in a moment.", null, null);
            return;
        }
        if (openLecture == lectureId) _ = ShowLectureAsync(lectureId, library.Note?.ShowTranscript == true);
    }

    /// <summary>The card on <paramref name="lectureId"/>'s page says why it can't just offer the AIs.</summary>
    static void ShowNotesFallback(string lectureId, NotesFallback why)
    {
        if (library.FreeAi is { } free && free.LectureId == lectureId) free.Fallback = why;
    }
}
