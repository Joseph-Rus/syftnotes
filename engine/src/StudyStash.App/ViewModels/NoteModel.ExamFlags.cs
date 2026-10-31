using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StudyStash.Core;

namespace StudyStash.App.ViewModels;

/// <summary>One moment the lecturer said what's on the exam, as a lecture's page lists it: the moment (pressed, the
/// transcript opens there) and the sentence it was said in.</summary>
public sealed class ExamFlagRow(ExamHint hint, NoteModel note)
{
    public string Moment => hint.Moment;
    public bool HasMoment => hint.At is not null;
    public string Said => hint.Said;
    public string MomentSaid => $"Open the transcript at {hint.Moment}";
    public IRelayCommand Open { get; } = new RelayCommand(() => { if (hint.At is { } at) note.ShowAt(at); });
}

public sealed partial class NoteModel
{
    /// <summary>The moments the lecturer said something will be on the exam (or to know it, or write it down).</summary>
    public ObservableCollection<ExamFlagRow> ExamFlags { get; } = [];
    /// <summary>The moments they said something won't be.</summary>
    public ObservableCollection<ExamFlagRow> NotOnExam { get; } = [];
    public bool HasExamFlags => ExamFlags.Count > 0 || NotOnExam.Count > 0;
    public bool HasNotOnExam => NotOnExam.Count > 0;
    /// <summary>"Said to know for the exam · 3".</summary>
    public string ExamFlagsTitle => ExamFlags.Count == 0 ? "Nothing flagged for the exam" : $"Flagged for the exam · {ExamFlags.Count}";
    /// <summary>Shut, it's one line with its count; open, every moment.</summary>
    [ObservableProperty] public partial bool ExamFlagsOpen { get; set; } = true;
    public string ExamFlagsToggleSaid => ExamFlagsOpen ? "Hide the exam flags" : "Show the exam flags";

    partial void OnExamFlagsOpenChanged(bool value) => OnPropertyChanged(nameof(ExamFlagsToggleSaid));

    [RelayCommand] void ToggleExamFlags() => ExamFlagsOpen = !ExamFlagsOpen;

    /// <summary>Finds the exam flags in the transcript the page was given (<see cref="Spoken"/>, or the untimed lines).</summary>
    public NoteModel FlagExam()
    {
        var hints = Spoken.Count > 0 ? ExamHints.Find(Spoken) : ExamHints.Find(string.Join('\n', Transcript.Select(l => l.Text)));
        ExamFlags.Clear();
        NotOnExam.Clear();
        foreach (var h in hints) (h.Not ? NotOnExam : ExamFlags).Add(new ExamFlagRow(h, this));
        OnPropertyChanged(nameof(HasExamFlags));
        OnPropertyChanged(nameof(HasNotOnExam));
        OnPropertyChanged(nameof(ExamFlagsTitle));
        return this;
    }
}
