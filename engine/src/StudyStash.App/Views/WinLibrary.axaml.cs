using Avalonia.Controls;
using Avalonia.Input;
using StudyStash.App.ViewModels;

namespace StudyStash.App.Views;

public partial class WinLibrary : UserControl
{
    public WinLibrary()
    {
        InitializeComponent();
        // Below 900 px a page draws its margins in, and a class's Canvas lists show one column, not two.
        SizeChanged += (_, e) =>
        {
            if (DataContext is LibraryModel m) m.Narrow = e.NewSize.Width < LibraryModel.NarrowBelow;
        };
    }
}
