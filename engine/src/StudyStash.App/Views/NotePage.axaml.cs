using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using StudyStash.App.ViewModels;

namespace StudyStash.App.Views;

public partial class NotePage : UserControl
{
    public NotePage()
    {
        InitializeComponent();
        // Files dropped on the note are attached to it.
        AttachFiles.AcceptDrops(NoteScroll, () => (DataContext as LibraryModel)?.LectureFiles);
        // The notes' diagrams ask about their boxes in this note's Ask bar, and find them in its transcript.
        LectureDiagramsView.Wire(this, NoteScroll, TranscriptLines);
        Fades.Over(TopFade, "Surface", 0.3);
        // (At its top the page has nothing above it to fade: the line that says where it is stays whole.)
        TopFade.IsVisible = false;
        NoteScroll.ScrollChanged += (_, _) => TopFade.IsVisible = NoteScroll.Offset.Y > 1;
        Fades.Under(Fade, "Surface", 0.55);
        // Another note opens at its top; a recording's page opens with the caret in its notes.
        string? showing = null;
        LibraryModel? model = null;
        void NoteChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(LibraryModel.Note) || model?.Note is not { } note) return;
            string key = note.Id + (note.Live ? "\u0001live" : "");
            if (key == showing) return;
            showing = key;
            NoteScroll.Offset = default;
            if (note.Live) Dispatcher.UIThread.Post(Typed.FocusEnd, DispatcherPriority.Input);
        }
        DataContextChanged += (_, _) =>
        {
            if (model is not null) model.PropertyChanged -= NoteChanged;
            model = DataContext as LibraryModel;
            if (model is not null) model.PropertyChanged += NoteChanged;
            NoteChanged(null, new System.ComponentModel.PropertyChangedEventArgs(nameof(LibraryModel.Note)));
        };
    }

    NoteModel? Note => (DataContext as LibraryModel)?.Note;

    /// <summary>The chevron beside the folder: the folder picker, under it.</summary>
    void OnPickFolder(object? sender, RoutedEventArgs e) => OpenPicker(ChangeFolder);

    /// <summary>The folder picker for the open note, under <paramref name="anchor"/> (the breadcrumb's chevron, or the
    /// toolbar's Move).</summary>
    public void OpenPicker(Control anchor)
    {
        if ((DataContext as LibraryModel)?.PickerForNote?.Invoke() is { } picker) FolderPickerFlyout.Open(anchor, picker);
    }

    /// <summary>The caret leaves the title: a new name is kept.</summary>
    void OnTitleLeft(object? sender, RoutedEventArgs e) => Note?.CommitTitleCommand.Execute(null);

    /// <summary>Return keeps the title (and the caret goes to the notes); Esc puts back what it was.</summary>
    void OnTitleKey(object? sender, KeyEventArgs e)
    {
        if (Note is not { } note) return;
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            note.CommitTitleCommand.Execute(null);
            if (note.Live) Typed.FocusEnd();
            else NoteScroll.Focus();
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            note.TitleText = note.Title;
            note.CommitTitleCommand.Execute(null);
            NoteScroll.Focus();
        }
    }

    /// <summary>A transcript line's time: the recording plays from there, when it's on this computer.</summary>
    void OnTime(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: HeardLine { Start: double at } } && DataContext is LibraryModel { Diagrams: { CanPlay: true } host }) host.Play(at);
    }
}
