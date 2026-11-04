namespace StudyStash.App.ViewModels.Kit;

/// <summary>
/// The student's own Quit (⌘Q, the app menu's or the Dock's Quit, the menu bar's or the tray's) while a lecture records:
/// asked first, as closing the window never stops a recording. One question at a time: Quit pressed again while it
/// shows neither asks again nor quits round it, but brings the question back to the front (its window may have been
/// hidden meanwhile, which would leave Quit doing nothing the student can see).
/// </summary>
/// <param name="recording">Whether a lecture is recording now.</param>
/// <param name="ask">The one "Are you sure?" (<see cref="IConfirm.AskAsync"/>).</param>
/// <param name="showQuestion">Brings the window with the question to the front.</param>
public sealed class QuitWhileRecording(Func<bool> recording, Func<ConfirmAsk, Task<bool>> ask, Action showQuestion)
{
    public static readonly ConfirmAsk Question = new("You're recording", "Quit and stop the recording? What's recorded so far is saved.", "Quit", "Keep recording");

    bool asking;

    /// <summary>True when the app may quit now: nothing records, or the student said Quit. False when they said no
    /// (the recording carries on) or the question is already showing (it comes to the front).</summary>
    public async Task<bool> MayQuitAsync()
    {
        if (!recording()) return true;
        if (asking)
        {
            showQuestion();
            return false;
        }
        asking = true;
        try
        {
            return await ask(Question);
        }
        finally
        {
            asking = false;
        }
    }
}
