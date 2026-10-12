using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using StudyStash.App.ViewModels;

namespace StudyStash.App.Views;

public partial class NoteRowView : UserControl
{
    public NoteRowView() => InitializeComponent();

    OverviewModel? Page => this.FindAncestorOfType<Overview>()?.DataContext as OverviewModel;

    void OnOpen(object? sender, RoutedEventArgs e)
    {
        // (The folder's own button is inside the row: its click is its own, not the row's.)
        if (ReferenceEquals(e.Source, Row) && DataContext is NoteItem note) note.Open?.Invoke();
    }

    /// <summary>The folder under the title: the folder picker opens under it, and a pick moves the note.</summary>
    void OnPick(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (DataContext is not NoteItem note || Page?.PickerFor?.Invoke(note) is not { } picker) return;
        note.Picking = true;
        FolderPickerFlyout.Open(Folder, picker, () => note.Picking = false);
    }

    void OnDelete(object? sender, RoutedEventArgs e)
    {
        if (DataContext is NoteItem note) Page?.DeleteNoteCommand.Execute(note);
    }
}
