using Avalonia.Controls;
using Avalonia.Input;
using StudyStash.App.ViewModels;

namespace StudyStash.App.Views;

public partial class WinLibrary : UserControl
{
    public WinLibrary()
    {
        InitializeComponent();
        // The library window has no accent colour: what's inside that asks for one gets ink.
        Resources.MergedDictionaries.Add(Skin.NoAccent());
        // Below 900 px a page draws its margins in, and a class's Canvas lists show one column, not two.
        SizeChanged += (_, e) =>
        {
            if (DataContext is LibraryModel m) m.Narrow = e.NewSize.Width < LibraryModel.NarrowBelow;
        };
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
