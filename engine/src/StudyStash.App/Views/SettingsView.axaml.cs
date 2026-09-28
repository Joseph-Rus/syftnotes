using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using CommunityToolkit.Mvvm.Input;
using StudyStash.App.Services;

namespace StudyStash.App.Views;

/// <summary>Settings for both looks in one view: <see cref="StudyStash.App.Skin.Current"/> decides once, at
/// construction, which of the "mac"/"win" root classes every look-specific style in the markup keys off.</summary>
public partial class SettingsView : UserControl
{
    bool drawChrome;

    public SettingsView()
    {
        InitializeComponent();
        bool mac = Skin.Current == SkinKind.Mac;
        // A plain if, not a "Mac ? ... : ..." ternary: ThemeTests.Every_token_a_view_asks_for_exists reads that
        // shape as a by-look DynamicResource pair and would otherwise treat "mac"/"win" as token names.
        if (mac) Classes.Add("mac"); else Classes.Add("win");
        Cols.ColumnDefinitions[0].Width = new GridLength(mac ? 220 : 240);
        Header.Title = mac ? "Settings" : "Study Stash settings";
        // Fixed, like the design's canvas: set here (not by a style on the root matching itself) so it always wins.
        Width = 900;
        Height = mac ? 780 : 860;
        // The library's fields save when they're left, or on Return.
        LibraryName.LostFocus += (_, _) => Run(m => m.Lib.SaveNameCommand);
        OnReturn(LibraryName, m => m.Lib.SaveNameCommand);
        OnReturn(NewPassword, m => m.Lib.SavePasswordCommand);
        OnReturn(NewLibraryClass, m => m.Lib.AddClassCommand);
        OnReturn(NewFolder, m => m.Lib.AddFolderCommand);
    }

    void Run(Func<SettingsModel, IAsyncRelayCommand> command)
    {
        if (DataContext is SettingsModel m) command(m).Execute(null);
    }

    void OnReturn(TextBox box, Func<SettingsModel, IAsyncRelayCommand> command) => box.KeyDown += (_, e) =>
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        Run(command);
    };

    void OnClassLostFocus(object? sender, RoutedEventArgs e) => Run(m => m.Lib.SaveClassesCommand);

    /// <summary>A real window has the system's traffic lights (Mac) or caption buttons (Windows) in its title bar;
    /// screenshots draw their own there instead.</summary>
    public bool DrawChrome
    {
        get => drawChrome;
        set
        {
            drawChrome = value;
            Header.DrawChrome = value;
            // The frame's radius, edge and shadow are for a screenshot only (a real window has the system's).
            Chrome.Classes.Set("chrome", value);
        }
    }
}
