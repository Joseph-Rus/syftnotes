using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using StudyStash.App.Controls.Rich;
using StudyStash.App.ViewModels;
using StudyStash.Core;

namespace StudyStash.App.Views;

/// <summary>
/// The library page's side of its notes' diagrams (either look): the notes' diagrams find the lecture's
/// <see cref="LectureDiagrams"/> through the page, and when one shows the transcript at a moment, the page scrolls to
/// that line (a little below the top, so what led up to it shows too).
/// </summary>
static class LectureDiagramsView
{
    public static void Wire(UserControl page, ScrollViewer notes, ItemsControl transcript)
    {
        DiagramHost.SetHost(notes, new Forward(() => page.DataContext as LibraryModel));
        LibraryModel? model = null;
        NoteModel? note = null;
        // Where the notes were read up to when a diagram sent the page to the transcript: Notes brings the student back
        // there, to the diagram, not to wherever the transcript was scrolled.
        Vector? reading = null;
        Vector? notesAt = null;
        void Jumped(int index) => ScrollTo(notes, transcript, index, 6);
        void Showing(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(NoteModel.ShowTranscript) || note is null) return;
            if (note.ShowTranscript) reading ??= notesAt;
            else if (reading is { } back)
            {
                reading = null;
                Dispatcher.UIThread.Post(() => notes.Offset = back, DispatcherPriority.Background);
            }
        }
        notes.ScrollChanged += (_, _) =>
        {
            if (note is { ShowTranscript: false }) notesAt = notes.Offset;
        };
        void NoteChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(LibraryModel.Note)) return;
            if (note is not null)
            {
                note.Jumped -= Jumped;
                note.PropertyChanged -= Showing;
            }
            note = model?.Note;
            reading = notesAt = null;
            if (note is not null)
            {
                note.Jumped += Jumped;
                note.PropertyChanged += Showing;
            }
        }
        page.DataContextChanged += (_, _) =>
        {
            if (model is not null) model.PropertyChanged -= NoteChanged;
            model = page.DataContext as LibraryModel;
            if (model is not null) model.PropertyChanged += NoteChanged;
            NoteChanged(null, new PropertyChangedEventArgs(nameof(LibraryModel.Note)));
        };
    }

    /// <summary>Scrolls the page to the transcript's line <paramref name="index"/> once it's laid out (it may have only
    /// just been shown).</summary>
    static void ScrollTo(ScrollViewer notes, ItemsControl transcript, int index, int tries) => Dispatcher.UIThread.Post(() =>
    {
        if (transcript.ContainerFromIndex(index) is not Control line || notes.Content is not Visual content || line.Bounds.Height <= 0
            || line.TranslatePoint(default, content) is not { } at)
        {
            if (tries > 0) ScrollTo(notes, transcript, index, tries - 1);
            return;
        }
        notes.Offset = new Vector(notes.Offset.X, Math.Max(0, at.Y - 120));
    }, DispatcherPriority.Background);

    /// <summary>The page's lecture's host, asked for each time (the page's model may come after the diagrams).</summary>
    sealed class Forward(Func<LibraryModel?> model) : IDiagramHost
    {
        IDiagramHost? Host => model()?.Diagrams;

        public bool CanAsk => Host?.CanAsk == true;

        public void Ask(string question) => Host?.Ask(question);

        public IReadOnlyList<Spoken> Transcript => Host?.Transcript ?? [];

        public void ShowTranscript(double seconds) => Host?.ShowTranscript(seconds);

        public bool CanPlay => Host?.CanPlay == true;

        public void Play(double seconds) => Host?.Play(seconds);
    }
}
