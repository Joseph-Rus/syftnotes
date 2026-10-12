namespace StudyStash.App.Views;

/// <summary>What the library window's toolbar buttons say on hover, with the keys that do the same: the Mac's (⌘K) or
/// Windows' (Ctrl+K), whichever look the app is in.</summary>
public static class KeyWords
{
    static bool Mac => Skin.Current == SkinKind.Mac;

    public static string Search => Mac ? "Search (⌘K)" : "Search (Ctrl+K)";
    public static string Settings => Mac ? "Settings (⌘,)" : "Settings (Ctrl+,)";
    public static string HideSidebar => Mac ? "Hide sidebar (⌃⌘S)" : "Hide sidebar (Ctrl+Shift+S)";
    public static string ShowSidebar => Mac ? "Show sidebar (⌃⌘S)" : "Show sidebar (Ctrl+Shift+S)";
    public static string Back => Mac ? "Back (⌘[)" : "Back (Alt+Left)";
    public static string Forward => Mac ? "Forward (⌘])" : "Forward (Alt+Right)";
}
