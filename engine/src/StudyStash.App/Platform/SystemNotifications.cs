namespace StudyStash.App.Platform;

/// <summary>A button on one of the system's notifications. <paramref name="OpensApp"/>: pressing it brings Study
/// Stash forward (Open note), rather than doing its work out of sight (Later).</summary>
public sealed record NotificationButton(string Id, string Label, bool OpensApp = true);

/// <summary>
/// The computer's own notifications (a Mac's Notification Center): Study Stash hands over the words and the system
/// shows them its way, with its own look, its own place on screen, its own time, Do Not Disturb and Focus. Nothing of
/// Study Stash's is drawn on one: no picture, no sound.
/// </summary>
public interface ISystemNotifications
{
    /// <summary>Whether the system shows Study Stash's notifications: not known until it has been asked (the first
    /// <see cref="Show"/> asks).</summary>
    NotificationState State { get; }

    /// <summary><see cref="State"/> is known now, or changed. May come on any thread.</summary>
    event Action? StateChanged;

    /// <summary>Shows a notification, in place of the one with the same <paramref name="id"/> if it's still there.</summary>
    void Show(string id, string title, string body, IReadOnlyList<NotificationButton> buttons);

    /// <summary>Takes one down, wherever it is (on screen, or in the list of earlier ones).</summary>
    void Remove(string id);

    /// <summary>The student did something with a notification: its id, and what (<see cref="NotificationResponse"/>,
    /// or a button's id). May come on any thread.</summary>
    event Action<string, string>? Responded;
}

public enum NotificationState
{
    /// <summary>Not asked yet, or the system's question is on screen waiting for the student.</summary>
    Unknown,
    Allowed,
    /// <summary>The student said no (the system's question, or its settings): the system shows none, and neither
    /// does Study Stash.</summary>
    Denied,
    /// <summary>The system won't have them from this copy of the app at all (run from a disk image or a temporary
    /// folder, or not signed): Study Stash shows its own cards instead.</summary>
    Unavailable,
}

public static class NotificationResponse
{
    /// <summary>The notification itself was clicked.</summary>
    public const string Clicked = "";
    /// <summary>It was cleared away without being opened.</summary>
    public const string Dismissed = "dismissed";
}
