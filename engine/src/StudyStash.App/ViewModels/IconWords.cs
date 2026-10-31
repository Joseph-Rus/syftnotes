namespace StudyStash.App.ViewModels;

/// <summary>Where Syft Notes lives, in words for a notification: the s in the menu bar (Mac), the taskbar corner
/// (Windows) or the desktop's tray (Linux), and what to do when a full menu bar hides it.</summary>
public static class IconWords
{
    /// <summary>The hidden S.'s button: moves it into view.</summary>
    public const string ShowIt = "Show it";

    /// <summary>Whether the pointer to the s is worth showing: not once the student has opened the dropdown (they
    /// found it, and on Windows the notification would sit over the dropdown in the same corner), unless the menu
    /// bar hides the s and the notification is the way to show it.</summary>
    public static bool WorthSaying(bool hidden, bool dropdownOpen) => hidden || !dropdownOpen;

    /// <summary>The first run's pointer to the s (<paramref name="hidden"/>: the menu bar has no room to show it).</summary>
    public static (string Title, string Text) WhereItIs(bool mac, bool hidden, bool linux = false) => (mac, hidden) switch
    {
        (false, _) when linux => ("Syft Notes is in your tray", "Click the Syft Notes icon in your top bar or panel to record or search. No icon? Open Syft Notes from your applications menu."),
        (true, false) => ("Syft Notes is in your menu bar", "Click the Syft Notes icon at the top right of your screen to record, search or open Settings."),
        (true, true) => ("Your menu bar is full", "Syft Notes' icon is hidden behind the camera or other icons. Show it moves the icon into view."),
        _ => ("Syft Notes is in the taskbar corner", "Click the Syft Notes icon by the clock to record or search. Can't see it? Click ^ and drag it to the taskbar."),
    };
}
