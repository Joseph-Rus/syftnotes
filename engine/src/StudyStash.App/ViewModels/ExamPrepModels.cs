using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StudyStash.Core;
using StudyStash.Core.Study;

namespace StudyStash.App.ViewModels;

/// <summary>"Thursday 29 October · in 9 days", "Tomorrow", "Today", or "" without a date.</summary>
static class ExamWhen
{
    public static string Say(DateTimeOffset? when, DateTimeOffset now)
    {
        if (when is not { } w) return "";
        var local = w.ToLocalTime();
        int days = (local.Date - now.ToLocalTime().Date).Days;
        string date = local.ToString("dddd d MMMM", CultureInfo.InvariantCulture);
        return days switch
        {
            < 0 => date,
            0 => $"Today, {local.ToString("h:mm tt", CultureInfo.InvariantCulture)}",
            1 => $"Tomorrow, {date}",
            _ => $"{date} · in {days} days",
        };
    }
}

/// <summary>An exam on the Study page: one coming up (from Canvas or the calendar) or one prepped for, with its prep
/// when there is one, and Prep (or Open prep).</summary>
public sealed partial class ExamRow : ObservableObject
{
    public required string Class { get; init; }
    public required string Title { get; init; }
    public DateTimeOffset? When { get; init; }
    public string WhenSaid { get; init; } = "";
    public string ClassShown => ClassNames.Display(Class);
    public string Line => WhenSaid.Length > 0 ? $"{ClassShown} · {WhenSaid}" : ClassShown;
    [ObservableProperty] public partial string? PrepId { get; set; }
    [ObservableProperty] public partial string Score { get; set; } = "";
    [ObservableProperty] public partial bool Making { get; set; }
    [ObservableProperty] public partial string? Problem { get; set; }
    public bool HasPrep => PrepId is not null;
    public bool HasScore => Score.Length > 0;
    public bool HasProblem => !string.IsNullOrEmpty(Problem);
    public string ActionSaid => Making ? "Writing your prep…" : HasPrep ? "Open prep" : "Prep for it";
    public IAsyncRelayCommand Go { get; init; } = null!;

    partial void OnPrepIdChanged(string? value)
    {
        OnPropertyChanged(nameof(HasPrep));
        OnPropertyChanged(nameof(ActionSaid));
    }
    partial void OnMakingChanged(bool value) => OnPropertyChanged(nameof(ActionSaid));
    partial void OnScoreChanged(string value) => OnPropertyChanged(nameof(HasScore));
    partial void OnProblemChanged(string? value) => OnPropertyChanged(nameof(HasProblem));
}

/// <summary>One choice of a multiple-choice question: picked, and once picked whether it's the right one.</summary>
public sealed partial class ChoiceRow(string letter, string text, bool right, PrepQuestionModel question) : ObservableObject
{
    public string Letter { get; } = letter;
    public string Text { get; } = text;
    public bool Right { get; } = right;
    [ObservableProperty] public partial bool Picked { get; set; }
    /// <summary>Answered: the right one shows as right, a wrong one picked as wrong.</summary>
    public bool ShowRight => question.Answered && Right;
    public bool ShowWrong => question.Answered && Picked && !Right;
    public IRelayCommand Pick { get; } = new RelayCommand(() => question.PickChoice(letter));
    internal void Shown()
    {
        OnPropertyChanged(nameof(ShowRight));
        OnPropertyChanged(nameof(ShowWrong));
    }
}

/// <summary>A practice question as the test shows it: a choice to pick, or an answer to type and mark against the model
/// answer; once answered, whether it was right and why, with the moment it comes from.</summary>
public sealed partial class PrepQuestionModel : ObservableObject
{
    public PrepQuestion Question { get; }
    public int Number { get; }
    public PrepQuestionModel(PrepQuestion q, int number, Action changed, Action<string, double?>? open)
    {
        Question = q;
        Number = number;
        this.changed = changed;
        this.open = open;
        for (int i = 0; i < q.Choices.Count; i++) Choices.Add(new ChoiceRow(((char)('A' + i)).ToString(), q.Choices[i], i == q.Answer, this));
    }

    readonly Action changed;
    readonly Action<string, double?>? open;

    public string Text => $"{Number}. {Question.Question}";
    public bool IsChoice => Question.Kind == "choice";
    public bool IsShort => !IsChoice;
    public ObservableCollection<ChoiceRow> Choices { get; } = [];
    [ObservableProperty] public partial string Typed { get; set; } = "";
    /// <summary>A short answer's model answer is showing, for the student to mark theirs against.</summary>
    [ObservableProperty] public partial bool Revealed { get; set; }
    /// <summary>Right, wrong, or not answered yet.</summary>
    [ObservableProperty] public partial bool? Right { get; set; }
    public bool Answered => Right is not null;
    public bool CanMark => IsShort && Revealed && !Answered;
    public bool ShowModel => IsShort && Revealed;
    public string ModelAnswer => Question.ModelAnswer;
    public string Why => Question.Why;
    public bool ShowWhy => Answered && Why.Length > 0;
    public string Verdict => Right switch { true => "Right", false => IsChoice ? $"The answer is {Question.ModelAnswer}" : "Marked wrong", _ => "" };
    public bool HasMoment => Question.Lecture.Length > 0;
    public string MomentSaid => Question.At is { } at ? $"Where this was taught · {TimedText.Clock(at)}" : "Where this was taught";

    partial void OnRightChanged(bool? value)
    {
        OnPropertyChanged(nameof(Answered));
        OnPropertyChanged(nameof(CanMark));
        OnPropertyChanged(nameof(ShowWhy));
        OnPropertyChanged(nameof(Verdict));
        foreach (var c in Choices) c.Shown();
        changed();
    }

    partial void OnRevealedChanged(bool value)
    {
        OnPropertyChanged(nameof(CanMark));
        OnPropertyChanged(nameof(ShowModel));
    }

    public void PickChoice(string letter)
    {
        if (Answered) return;
        foreach (var c in Choices) c.Picked = c.Letter == letter;
        Right = Choices.First(c => c.Letter == letter).Right;
    }

    [RelayCommand] void Reveal() => Revealed = true;
    [RelayCommand] void MarkRight() => Right = true;
    [RelayCommand] void MarkWrong() => Right = false;
    [RelayCommand] void OpenMoment() => open?.Invoke(Question.Lecture, Question.At);

    public void Reset()
    {
        foreach (var c in Choices) c.Picked = false;
        Typed = "";
        Revealed = false;
        Right = null;
    }
}

/// <summary>
/// An exam's prep, in the Study page's place: what it's for and the lectures it covers, the study guide, and the
/// practice test, a question at a time answered and marked, the score kept at the end and what was missed put into
/// Review.
/// </summary>
public sealed partial class ExamPrepModel : ObservableObject
{
    readonly IStudyLibrary library;
    public ExamPrepDoc Prep { get; }

    public ExamPrepModel(IStudyLibrary library, ExamPrepDoc prep, DateTimeOffset now, Action<string, double?>? open = null)
    {
        this.library = library;
        Prep = prep;
        WhenSaid = ExamWhen.Say(prep.When, now);
        int n = 0;
        foreach (var q in prep.Questions) Questions.Add(new PrepQuestionModel(q, ++n, Scored, open));
        LastScore = prep.Attempts.Count > 0 ? $"Last time: {prep.Attempts[^1].Right} of {prep.Attempts[^1].Marks.Count}" : "";
    }

    public string Title => Prep.Title;
    public string ClassShown => ClassNames.Display(Prep.Class);
    public string WhenSaid { get; }
    public string Line => WhenSaid.Length > 0 ? $"{ClassShown} · {WhenSaid}" : ClassShown;
    public string Covers => Prep.Lectures.Count == 1 ? $"From 1 lecture: {Prep.Lectures[0].Title}"
        : $"From {Prep.Lectures.Count} lectures: {string.Join(", ", Prep.Lectures.Select(l => l.Title))}";
    public string By => Prep.By.Length > 0 ? $"Written by {Prep.By}" : "";
    public string Guide => Prep.Guide;
    public ObservableCollection<PrepQuestionModel> Questions { get; } = [];
    public bool HasQuestions => Questions.Count > 0;

    [ObservableProperty] public partial bool ShowTest { get; set; }
    public bool ShowGuide => !ShowTest;
    partial void OnShowTestChanged(bool value) => OnPropertyChanged(nameof(ShowGuide));
    [RelayCommand] void GuideTab() => ShowTest = false;
    [RelayCommand] void TestTab() => ShowTest = true;

    public int AnsweredCount => Questions.Count(q => q.Answered);
    public int RightCount => Questions.Count(q => q.Right == true);
    public string Score => $"{RightCount} of {AnsweredCount} right · {Questions.Count - AnsweredCount} to go";
    public bool AllAnswered => Questions.Count > 0 && AnsweredCount == Questions.Count;
    public int MissedCount => Questions.Count(q => q.Right == false);
    [ObservableProperty] public partial string LastScore { get; set; } = "";
    public bool HasLastScore => LastScore.Length > 0;
    partial void OnLastScoreChanged(string value) => OnPropertyChanged(nameof(HasLastScore));
    [ObservableProperty] public partial string? Saved { get; set; }
    public bool HasSaved => Saved is not null;
    partial void OnSavedChanged(string? value) => OnPropertyChanged(nameof(HasSaved));
    [ObservableProperty] public partial bool Saving { get; set; }

    void Scored()
    {
        OnPropertyChanged(nameof(AnsweredCount));
        OnPropertyChanged(nameof(RightCount));
        OnPropertyChanged(nameof(Score));
        OnPropertyChanged(nameof(AllAnswered));
        OnPropertyChanged(nameof(MissedCount));
        FinishCommand.NotifyCanExecuteChanged();
    }

    /// <summary>The test's done: its score kept, and the questions missed put into Review.</summary>
    [RelayCommand(CanExecute = nameof(AllAnswered))]
    async Task Finish()
    {
        Saving = true;
        try
        {
            var marks = Questions.Where(q => q.Answered).ToDictionary(q => q.Question.Id, q => q.Right == true);
            if (await library.AttemptAsync(Prep.Id, marks, reviewMissed: true) is { } done)
            {
                LastScore = $"Last time: {RightCount} of {marks.Count}";
                Saved = done.AddedToReview switch
                {
                    0 when MissedCount == 0 => $"{RightCount} of {marks.Count}: every one right.",
                    0 => $"{RightCount} of {marks.Count}. The ones you missed were in Review already.",
                    1 => $"{RightCount} of {marks.Count}. The one you missed is in Review now.",
                    int n => $"{RightCount} of {marks.Count}. The {n} you missed are in Review now.",
                };
            }
        }
        catch
        {
            Saved = "Your library isn't answering, so this go wasn't kept.";
        }
        finally
        {
            Saving = false;
        }
    }

    [RelayCommand]
    void Again()
    {
        foreach (var q in Questions) q.Reset();
        Saved = null;
    }

    /// <summary>Back to the Study page; <see cref="OnDeleted"/> after Delete.</summary>
    public Action? OnClose { get; set; }
    public Action? OnDeleted { get; set; }
    [RelayCommand] void Close() => OnClose?.Invoke();

    [RelayCommand]
    async Task Delete()
    {
        try
        {
            await library.DeletePrepAsync(Prep.Id);
        }
        catch
        {
            return;
        }
        OnDeleted?.Invoke();
    }
}
