using Avalonia.Controls;
using StudyStash.App.ViewModels;

namespace StudyStash.App.Views;

public partial class WinAiNotes : UserControl
{
    public WinAiNotes()
    {
        InitializeComponent();
        AiNotesEditor.Attach(this, Editor);
        DataContextChanged += (_, _) =>
        {
            if (DataContext is AiNotesModel m) m.CloseMenu = () => RewriteButton.Flyout?.Hide();
        };
    }

    // The "Rewrite notes with" flyout dims the notes behind it (17); a Flyout never renders into a shot, so nothing
    // here runs during a screenshot — AiShots sets AiNotesModel.MenuOpen directly for that picture instead.
    void OnMenuOpened(object? sender, EventArgs e)
    {
        if (DataContext is AiNotesModel m) m.MenuOpen = true;
    }

    void OnMenuClosed(object? sender, EventArgs e)
    {
        if (DataContext is AiNotesModel m) m.MenuOpen = false;
    }
}
