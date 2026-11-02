using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StudyStash.Core;
using StudyStash.Core.Study;

namespace StudyStash.App.ViewModels;

/// <summary>One thing the lecturer flagged, as a lecture's page lists it: the moment (pressed, the transcript opens
/// there) and the idea it was about with what to know, once the library's AI has named it; until then (or with no AI)
/// the sentence it was said in.</summary>
public sealed class ExamFlagRow(ExamHint hint, NoteModel note, KeyIdea? named = null)
{
    public string Moment => hint.Moment;
    public bool HasMoment => hint.At is not null;
    /// <summary>The idea, or the sentence while it has no name.</summary>
    public string Said => named?.Concept ?? hint.Said;
    public bool Named => named is not null;
    public FontWeight Weight => named is null ? FontWeight.Normal : FontWeight.SemiBold;
    public string Detail => named?.Detail ?? "";
    public bool HasDetail => Detail.Length > 0;
    /// <summary>What the lecturer said, word for word, behind a named one.</summary>
    public string? Quote => named is null ? null : $"\u201c{hint.Said}\u201d";
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
    /// <summary>"Important to remember · 3": not every recording has an exam, or is even a class.</summary>
    public string ExamFlagsTitle => ExamFlags.Count == 0 ? "Nothing flagged as important" : $"Important to remember · {ExamFlags.Count}";
    /// <summary>Shut, it's one line with its count; open, every moment.</summary>
    [ObservableProperty] public partial bool ExamFlagsOpen { get; set; } = true;
    public string ExamFlagsToggleSaid => ExamFlagsOpen ? "Hide what's important" : "Show what's important";

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

    /// <summary>The flags as the library's AI named them: the ideas they were about, the ones that weren't about
    /// anything gone.</summary>
    public void NameFlags(IReadOnlyList<KeyIdea> named)
    {
        ExamFlags.Clear();
        NotOnExam.Clear();
        foreach (var r in named) (r.Not ? NotOnExam : ExamFlags).Add(new ExamFlagRow(new ExamHint(r.At, r.Said, r.Not), this, r));
        OnPropertyChanged(nameof(HasExamFlags));
        OnPropertyChanged(nameof(HasNotOnExam));
        OnPropertyChanged(nameof(ExamFlagsTitle));
    }
}
