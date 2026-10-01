using System.ComponentModel;
using StudyStash.App.Controls.Rich;
using StudyStash.Core;

namespace StudyStash.App.ViewModels;

/// <summary>
/// What the diagrams in the open lecture's notes can do with the library's page: a question about a box goes to the
/// lecture's own Ask bar (its answer shows above it, as a typed one would), and "where was this said" or a diagram's
/// moment opens the transcript at that line. The recording plays from a moment where it's still on this computer.
/// </summary>
public sealed class LectureDiagrams(LibraryModel library, Func<string, bool> hasAudio, Action<string, double> play) : IDiagramHost
{
    public bool CanAsk => library.HasAsk;

    public void Ask(string question)
    {
        if (library.Ask is not { } ask) return;
        if (!ask.Busy)
        {
            Send(ask, question);
            return;
        }
        // An answer still being written stops; this question goes as soon as the bar is free.
        void Free(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(AiAskModel.Busy) || ask.Busy) return;
            ask.PropertyChanged -= Free;
            Send(ask, question);
        }
        ask.PropertyChanged += Free;
        ask.Stop();
    }

    static void Send(AiAskModel ask, string question)
    {
        ask.Question = question;
        if (ask.AskCommand.CanExecute(null)) ask.AskCommand.Execute(null);
    }

    public IReadOnlyList<Spoken> Transcript => library.Note?.Spoken ?? [];

    public void ShowTranscript(double seconds) => library.Note?.ShowAt(seconds);

    public bool CanPlay => library.Note is { } n && hasAudio(n.Id);

    public void Play(double seconds)
    {
        if (library.Note is { } n) play(n.Id, seconds);
    }
}
