using StudyStash.App.Controls;

namespace StudyStash.App.ViewModels.Kit;

/// <summary>
/// A question with two answers, asked over the window the student is in: "Delete this lecture?" with Delete and Cancel.
/// Say what happens in plain words and name the buttons for what they do ("Delete", "Remove them"), never "OK" or "Yes".
/// <paramref name="No"/> is the safe answer: Escape, a click outside and the key focus all land on it.
/// </summary>
/// <param name="Title">The question, as a student would ask it.</param>
/// <param name="Text">What will happen if they say yes, and what won't (a way back, if there is one).</param>
/// <param name="Yes">The button that goes ahead.</param>
/// <param name="No">The button that doesn't.</param>
public sealed record ConfirmAsk(string Title, string Text, string Yes, string No = "Cancel")
{
    /// <summary>What will change, one line each (the classes a rename touches), under the text.</summary>
    public IReadOnlyList<string> Lines { get; init; } = [];

    public bool HasText => Text.Length > 0;
    public bool HasLines => Lines.Count > 0;
}

/// <summary>Asks the student a <see cref="ConfirmAsk"/>. View models take this in their constructor, so a test gives them
/// an answer instead of a window.</summary>
public interface IConfirm
{
    /// <summary>True if they said yes; false for no, Escape, a click outside, or when nothing could be asked.</summary>
    Task<bool> AskAsync(ConfirmAsk ask);
}

/// <summary>
/// The one "Are you sure?" (DialogHost.Avalonia underneath). Every window opened through <c>AppWindows</c> carries a
/// dialog layer (<see cref="ConfirmLayer"/>); the question appears over the window the student is in, dims the page
/// behind it and takes the keyboard: focus starts on the safe button, Escape answers no, and Tab stays inside it.
/// Only one question is open per window; asking a second while one is showing answers no to the second.
/// </summary>
public sealed class Confirm : IConfirm
{
    /// <summary>The one the app uses (registered in <c>AppServices</c>); a view model made without one gets this.</summary>
    public static Confirm Default { get; } = new();

    public async Task<bool> AskAsync(ConfirmAsk ask)
    {
        if (ConfirmLayer.Pick() is not { } layer)
        {
            Program.Log($"[confirm] nothing to ask \"{ask.Title}\" over: no window is open");
            return false;
        }
        return await layer.AskAsync(ask);
    }
}
