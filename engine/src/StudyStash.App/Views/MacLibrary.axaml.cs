using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using StudyStash.App.Controls;
using StudyStash.App.ViewModels;

namespace StudyStash.App.Views;

public partial class MacLibrary : UserControl
{
    public MacLibrary()
    {
        InitializeComponent();
        // The library window has no accent colour: what's inside that asks for one gets ink.
        Resources.MergedDictionaries.Add(Skin.NoAccent());
        // Below 900 px a page draws its margins in, and a class's Canvas lists show one column, not two.
        SizeChanged += (_, e) =>
        {
            if (DataContext is LibraryModel m) m.Narrow = e.NewSize.Width < LibraryModel.NarrowBelow;
        };
        AttachedToVisualTree += (_, _) =>
        {
            window = TopLevel.GetTopLevel(this) as Window;
            if (window is not null) window.PropertyChanged += OnWindowChanged;
            PlaceNav();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            if (window is not null) window.PropertyChanged -= OnWindowChanged;
            window = null;
        };
    }

    /// <summary>How far in from the window's edge the toolbars' first button sits in full screen: where the sidebar's
    /// own rows start.</summary>
    public const double FullScreenInset = 14;

    /// <summary>The room the traffic lights take in the window's top left corner, from the edge of the sidebar (or of
    /// the page, with the sidebar folded away), each of which starts 8 in from the window's.</summary>
    const double LightsRoom = WindowHeader.LightsRoom - 8;

    Window? window;

    void OnWindowChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Window.WindowStateProperty) PlaceNav();
    }

    /// <summary>The sidebar's button starts right after the lights (and, with the sidebar folded away, so do it and
    /// Back and Forward at the page's top left); full screen shows no lights at rest, so there they take the corner.</summary>
    void PlaceNav()
    {
        bool full = window?.WindowState == WindowState.FullScreen;
        SideTools.Margin = new Thickness(full ? FullScreenInset - 8 : LightsRoom, 2, 6, 22);
        PaneNav.Margin = new Thickness(full ? 0 : LightsRoom - 12, 0, 0, 0);
    }

    /// <summary>A click beside "Delete this lecture?" is Cancel.</summary>
    void OnDeleteAskPressed(object? sender, PointerPressedEventArgs e)
    {
        if (ReferenceEquals(e.Source, sender) && DataContext is LibraryModel m) m.CancelDeleteCommand.Execute(null);
    }

    /// <summary>A click beside "Stop recording … and start …?" is Cancel.</summary>
    void OnConfirmAskPressed(object? sender, PointerPressedEventArgs e)
    {
        if (ReferenceEquals(e.Source, sender) && DataContext is LibraryModel m) m.CancelConfirmCommand.Execute(null);
    }
}
