namespace StudyStash.App.ViewModels;

/// <summary>Where Study Stash lives, in words for a notification: the S. in the menu bar (Mac) or the taskbar corner
/// (Windows), and what to do when a full menu bar hides it.</summary>
public static class IconWords
{
    /// <summary>The hidden S.'s button: moves it into view.</summary>
    public const string ShowIt = "Show it";

    /// <summary>Whether the pointer to the S. is worth showing: not once the student has opened the dropdown (they
    /// found it, and on Windows the notification would sit over the dropdown in the same corner), unless the menu
    /// bar hides the S. and the notification is the way to show it.</summary>
    public static bool WorthSaying(bool hidden, bool dropdownOpen) => hidden || !dropdownOpen;

    /// <summary>The first run's pointer to the S. (<paramref name="hidden"/>: the menu bar has no room to show it).</summary>
    public static (string Title, string Text) WhereItIs(bool mac, bool hidden) => (mac, hidden) switch
    {
        (true, false) => ("Study Stash is in your menu bar", "Click the S. at the top right of your screen to record, search or open Settings."),
        (true, true) => ("Your menu bar is full", "Study Stash's S. is hidden behind the camera or other icons. Show it moves the S. into view."),
        _ => ("Study Stash is in the taskbar corner", "Click the S. by the clock to record or search. Can't see it? Click ^ and drag it onto the taskbar."),
    };
}
