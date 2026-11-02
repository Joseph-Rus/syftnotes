using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using StudyStash.App.Services;
using StudyStash.Core.Setup;

namespace StudyStash.App.Views;

/// <summary>Settings for both looks in one view, in the library window's look in each: <see cref="StudyStash.App.Skin.Current"/>
/// decides once, at construction, which of the "mac"/"win" root classes the little that differs (the window's own
/// frame, its title) keys off.</summary>
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
        // The library window's look: no accent anywhere, so whatever inside asks for it (a ticked box, a progress bar,
        // a pane's own switches and choices) gets ink, and only the swatches show the colour themes.
        Resources.MergedDictionaries.Add(Skin.NoAccent());
        Header.Title = mac ? "Settings" : "Syft Notes settings";
        // Fixed, like the design's canvas: set here (not by a style on the root matching itself) so it always wins.
        Width = 900;
        Height = mac ? 780 : 860;
        // The library's fields save when they're left, or on Return.
        LibraryName.LostFocus += (_, _) => Run(m => m.Lib.SaveNameCommand);
        OnReturn(LibraryName, m => m.Lib.SaveNameCommand);
        OnReturn(NewPassword, m => m.Lib.SavePasswordCommand);
        OnReturn(NewLibraryClass, m => m.Lib.AddClassCommand);
        OnReturn(NewFolder, m => m.Lib.AddFolderCommand);
        DataContextChanged += (_, _) =>
        {
            if (watched is not null) watched.Spotlit -= Spotlight;
            watched = DataContext as SettingsModel;
            if (watched is not null) watched.Spotlit += Spotlight;
        };
        // ⌘F / Ctrl+F: the search field.
        KeyDown += (_, e) =>
        {
            if (e.Key != Key.F || !e.KeyModifiers.HasFlag(mac ? KeyModifiers.Meta : KeyModifiers.Control)) return;
            e.Handled = true;
            Search.Focus();
            Search.SelectAll();
        };
    }

    SettingsModel? watched;

    /// <summary>How long a found setting stays lit.</summary>
    public static readonly TimeSpan SpotFor = TimeSpan.FromSeconds(1.6);

    /// <summary>The setting search (or the helper) went to: once its page is showing (a library page may still be
    /// asking its library), scrolls to the line that names it and lights its row up for a moment.</summary>
    void Spotlight(SettingEntry entry)
    {
        int tries = 0;
        void Look()
        {
            if (Find(entry.Title) is { } line)
            {
                line.BringIntoView();
                Control row = Row(line);
                row.Classes.Add("spot");
                DispatcherTimer.RunOnce(() => row.Classes.Remove("spot"), SpotFor);
            }
            else if (++tries < 20) DispatcherTimer.RunOnce(Look, TimeSpan.FromMilliseconds(150));
        }
        Dispatcher.UIThread.Post(Look, DispatcherPriority.Loaded);
    }

    /// <summary>The words on the page now showing (not the sidebar's) that are <paramref name="title"/>.</summary>
    public TextBlock? Find(string title) =>
        this.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault(t => t.Text == title && t.IsEffectivelyVisible && t.Bounds.Height > 0
            && !t.GetVisualAncestors().Contains(Side));

    /// <summary>What lights up for a line: the row it's in (the nearest grid on the page), else the line itself (a
    /// section's heading).</summary>
    static Control Row(TextBlock line)
    {
        foreach (var a in line.GetVisualAncestors())
        {
            if (a is ScrollViewer or ScrollContentPresenter or UserControl || a is StackPanel { Classes: var c } && c.Contains("pagebody")) break;
            if (a is Grid g) return g;
        }
        return line;
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
