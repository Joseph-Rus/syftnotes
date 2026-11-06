using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StudyStash.Core;
using StudyStash.Core.Ai;
using StudyStash.Core.Study;

namespace StudyStash.App.ViewModels;

/// <summary>
/// What a call to the library may fail with when the library is just away or answers badly: the study pages meet that
/// with their plain "isn't answering" words. Anything else is a fault in the app, and goes to the log.
/// </summary>
internal static class LibraryTrouble
{
    public static bool IsExpected(Exception e) =>
        e is HttpRequestException or TaskCanceledException or LibraryRefusedException or JsonException
            or IOException or UnauthorizedAccessException or InvalidOperationException or UriFormatException;

    /// <summary>
    /// For a catch filter: always lets the page show its plain words, and writes down the failures nobody expected
    /// (so a fault in the app doesn't vanish into "isn't answering").
    /// </summary>
    public static bool Handled(Exception e, [CallerMemberName] string? where = null)
    {
        if (!IsExpected(e)) Program.Log($"[study] unexpected {e.GetType().Name} in {where}: {e.Message}");
        return true;
    }
}

/// <summary>
/// A lecture's study tools, under its exam flags: its flashcards in Review (made by the library's AI from its notes on
/// a press, then "12 cards in Review" and a way to review just them), and Listen.
/// </summary>
public sealed partial class LectureStudyModel(IStudyLibrary library, string lectureId, string noun = "lecture") : ObservableObject
{
    public string LectureId { get; } = lectureId;
    [ObservableProperty] public partial int Cards { get; set; }
    [ObservableProperty] public partial bool Making { get; set; }
    [ObservableProperty] public partial string? Problem { get; set; }
    /// <summary>The library is too old for Review: nothing shows.</summary>
    [ObservableProperty] public partial bool Available { get; set; }

    public bool HasCards => Cards > 0;
    public bool CanMake => Available && !Making && Cards == 0;
    public bool HasProblem => !string.IsNullOrEmpty(Problem);
    public string CardsSaid => Cards == 1 ? "1 card in Review" : $"{Cards} cards in Review";
    public string MakeSaid => Making ? "Making cards…" : "Make review cards";
    public string MakeTip => $"Your AI writes flashcards from this {noun}'s notes, and Review brings each back just before you'd forget it.";

    /// <summary>Cards were made: the sidebar's count follows (the host wires it).</summary>
    public Action? OnMade { get; set; }
    /// <summary>Opens Review on just this lecture's cards (the host wires it).</summary>
    public Action<string>? OnReview { get; set; }

    partial void OnCardsChanged(int value) => Changed();
    partial void OnMakingChanged(bool value) => Changed();
    partial void OnAvailableChanged(bool value) => Changed();
    partial void OnProblemChanged(string? value) => OnPropertyChanged(nameof(HasProblem));

    void Changed()
    {
        OnPropertyChanged(nameof(HasCards));
        OnPropertyChanged(nameof(CanMake));
        OnPropertyChanged(nameof(CardsSaid));
        OnPropertyChanged(nameof(MakeSaid));
        MakeCardsCommand.NotifyCanExecuteChanged();
    }

    public async Task LoadAsync()
    {
        try
        {
            if (await library.LectureCardsAsync(LectureId) is { } cards)
            {
                Cards = cards.Count;
                Available = true;
            }
        }
        catch (Exception e) when (LibraryTrouble.Handled(e))
        {
            Available = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanMake))]
    async Task MakeCards()
    {
        Making = true;
        Problem = null;
        try
        {
            if (await library.MakeCardsAsync(LectureId) is { } made) Cards = made.Cards.Count;
            if (Cards == 0) Problem = "Your AI didn't write any cards for this. Try again in a moment.";
            else OnMade?.Invoke();
        }
        catch (LibraryRefusedException e)
        {
            Problem = e.Message;
        }
        catch (Exception e) when (LibraryTrouble.Handled(e))
        {
            Problem = "Your library isn't answering. Try again in a moment.";
        }
        finally
        {
            Making = false;
        }
    }

    [RelayCommand] void Review() => OnReview?.Invoke(LectureId);

    /// <summary>Listening to the lecture as a short conversation; null when the library can't make them.</summary>
    [ObservableProperty] public partial ListenModel? Listen { get; set; }
    partial void OnListenChanged(ListenModel? oldValue, ListenModel? newValue)
    {
        if (!ReferenceEquals(oldValue, newValue)) oldValue?.Dispose();
    }
}

/// <summary>One grade button under a card: its word, when it brings the card back, and what it does.</summary>
public sealed class GradeChoice(Grade grade, string after, IRelayCommand pick)
{
    public Grade Grade { get; } = grade;
    public string Word { get; } = grade switch { Grade.Again => "Again", Grade.Hard => "Hard", Grade.Good => "Good", _ => "Easy" };
    public string After { get; } = after;
    public string Key { get; } = ((int)grade + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
    public string Said => $"{Word}: again in {After} ({Key})";
    public bool IsAgain => Grade == Grade.Again;
    public IRelayCommand Pick { get; } = pick;
}

/// <summary>
/// Reviewing: the cards due, one at a time. The question, then (Space, or Show answer) the answer, then how it went
/// (1 to 4: Again, Hard, Good, Easy), each saying when the card comes back. One answered Again comes round again
/// before the end. At the end, how many were reviewed and when the next are due.
/// </summary>
public sealed partial class ReviewSessionModel(IStudyLibrary library, Func<DateTimeOffset>? now = null) : ObservableObject
{
    readonly Func<DateTimeOffset> clock = now ?? (() => DateTimeOffset.Now);
    readonly Queue<ReviewCard> queue = new();

    /// <summary>One class's cards, or one lecture's; null for every card due.</summary>
    public string? ClassName { get; private set; }
    public string? LectureId { get; private set; }
    public string Title => LectureId is not null ? "Review this lecture" : ClassName is { Length: > 0 } c ? $"Review {ClassNames.Display(c)}" : "Review";

    [ObservableProperty] public partial ReviewCard? Card { get; set; }
    [ObservableProperty] public partial bool ShowingBack { get; set; }
    [ObservableProperty] public partial bool Loading { get; set; } = true;
    [ObservableProperty] public partial string? Problem { get; set; }
    [ObservableProperty] public partial int Done { get; set; }
    [ObservableProperty] public partial int Left { get; set; }
    [ObservableProperty] public partial string NextDue { get; set; } = "";
    public ObservableCollection<GradeChoice> Grades { get; } = [];

    public bool HasCard => Card is not null;
    public bool Finished => !Loading && Card is null && Problem is null;
    public bool HasProblem => Problem is not null;
    public string Front => Card?.Front ?? "";
    public string Back => Card?.Back ?? "";
    public string Source => Card is null ? "" : Card.At is { } at && Card.LectureTitle.Length > 0
        ? $"{Card.LectureTitle} · {Core.TimedText.Clock(at)}" : Card.LectureTitle;
    public bool HasSource => Source.Length > 0;
    public string Progress => Left == 1 ? "1 card left" : $"{Left} cards left";
    public string FinishedTitle => Done == 0 ? "Nothing to review right now" : Done == 1 ? "You reviewed 1 card" : $"You reviewed {Done} cards";
    public string FinishedMore => NextDue.Length > 0 ? NextDue : "Make cards from a lecture's page, and they'll show up here when they're due.";

    /// <summary>Opens a card's lecture at its moment (the host wires it).</summary>
    public Action<string, double?>? OnOpenLecture { get; set; }
    /// <summary>Back to the Study page.</summary>
    public Action? OnDone { get; set; }

    partial void OnCardChanged(ReviewCard? value)
    {
        OnPropertyChanged(nameof(HasCard));
        OnPropertyChanged(nameof(Finished));
        OnPropertyChanged(nameof(Front));
        OnPropertyChanged(nameof(Back));
        OnPropertyChanged(nameof(Source));
        OnPropertyChanged(nameof(HasSource));
        Grades.Clear();
        if (value is null) return;
        var t = clock();
        foreach (var g in Enum.GetValues<Grade>())
            Grades.Add(new GradeChoice(g, Review.After(value, g, t), new AsyncRelayCommand(() => GradeAsync(g))));
    }

    partial void OnLoadingChanged(bool value) => OnPropertyChanged(nameof(Finished));
    partial void OnProblemChanged(string? value)
    {
        OnPropertyChanged(nameof(HasProblem));
        OnPropertyChanged(nameof(Finished));
    }
    partial void OnLeftChanged(int value) => OnPropertyChanged(nameof(Progress));
    partial void OnDoneChanged(int value) => OnPropertyChanged(nameof(FinishedTitle));
    partial void OnNextDueChanged(string value) => OnPropertyChanged(nameof(FinishedMore));

    /// <summary>Reads what's due (for a class or a lecture, or all), and shows the first card.</summary>
    public async Task StartAsync(string? className = null, string? lectureId = null)
    {
        ClassName = className;
        LectureId = lectureId;
        OnPropertyChanged(nameof(Title));
        Loading = true;
        Problem = null;
        queue.Clear();
        try
        {
            if (lectureId is not null)
            {
                var t = clock();
                foreach (var c in (await library.LectureCardsAsync(lectureId) ?? []).Where(c => c.Due <= t).OrderBy(c => c.Due)) queue.Enqueue(c);
            }
            else if (await library.ReviewAsync(className) is { } state)
                foreach (var c in state.Due) queue.Enqueue(c);
        }
        catch (Exception e) when (LibraryTrouble.Handled(e))
        {
            Problem = "Your library isn't answering. Try again in a moment.";
        }
        Left = queue.Count;
        Loading = false;
        await NextAsync();
    }

    async Task NextAsync()
    {
        ShowingBack = false;
        Card = queue.Count > 0 ? queue.Dequeue() : null;
        Left = queue.Count + (Card is null ? 0 : 1);
        if (Card is null) await SayNextDueAsync();
    }

    async Task SayNextDueAsync()
    {
        try
        {
            if (await library.ReviewAsync(ClassName, 1) is { } state)
                NextDue = state.Counts.Total == 0 ? "" : state.Counts.Today > 0 ? $"{state.Counts.Today} more due later today."
                    : "Nothing else is due today. Come back tomorrow.";
        }
        catch (Exception e) when (LibraryTrouble.Handled(e))
        {
        }
    }

    [RelayCommand]
    void ShowBack()
    {
        if (Card is not null) ShowingBack = true;
    }

    /// <summary>1 to 4 once the answer shows (Space shows it): the grade with that number.</summary>
    public bool Key(int number)
    {
        if (Card is null || !ShowingBack || number < 1 || number > Grades.Count) return false;
        Grades[number - 1].Pick.Execute(null);
        return true;
    }

    async Task GradeAsync(Grade grade)
    {
        if (Card is not { } card || !ShowingBack) return;
        try
        {
            var graded = await library.GradeAsync(card.Id, grade);
            // Forgotten: it comes round again before the end, as it's due again in minutes.
            if (grade == Grade.Again && graded is not null) queue.Enqueue(graded);
        }
        catch (Exception e) when (LibraryTrouble.Handled(e))
        {
            Problem = "Your library isn't answering, so that card wasn't saved. Try again in a moment.";
            return;
        }
        Done++;
        await NextAsync();
    }

    [RelayCommand]
    async Task DeleteCard()
    {
        if (Card is not { } card) return;
        try
        {
            await library.DeleteCardAsync(card.Id);
        }
        catch (Exception e) when (LibraryTrouble.Handled(e))
        {
            return;
        }
        await NextAsync();
    }

    [RelayCommand] void OpenLecture()
    {
        if (Card is { Lecture.Length: > 0 } c) OnOpenLecture?.Invoke(c.Lecture, c.At);
    }

    [RelayCommand] void Close() => OnDone?.Invoke();
}

/// <summary>One class's line on the Study page: its name and swatch, its cards due, and a way to review just them.</summary>
public sealed class StudyClassRow(string name, int due, IRelayCommand review, IBrush dot)
{
    public string Name { get; } = name;
    public IBrush Dot { get; } = dot;
    public string Display => ClassNames.Display(Name);
    public int Due { get; } = due;
    public string DueSaid => Due == 1 ? "1 due" : $"{Due} due";
    public IRelayCommand Review { get; } = review;
}

/// <summary>
/// The Study page, from the sidebar: Review (how many cards are due, by class, and Start), exam prep (the exams coming
/// up, from Canvas and the calendar, and the prep made for them), and while a review or a prep is open, it in its place.
/// </summary>
public sealed partial class StudyModel(IStudyLibrary library, Func<DateTimeOffset>? now = null) : ObservableObject
{
    readonly Func<DateTimeOffset> clock = now ?? (() => DateTimeOffset.Now);

    [ObservableProperty] public partial int Due { get; set; }
    [ObservableProperty] public partial int Total { get; set; }
    [ObservableProperty] public partial bool Loading { get; set; } = true;
    [ObservableProperty] public partial bool Available { get; set; } = true;
    public ObservableCollection<StudyClassRow> ByClass { get; } = [];

    /// <summary>A review under way, in the page's place.</summary>
    [ObservableProperty] public partial ReviewSessionModel? Session { get; set; }
    public bool Reviewing => Session is not null;
    public bool ShowHub => Session is null && Prep is null;

    /// <summary>An exam's prep open, in the page's place.</summary>
    [ObservableProperty] public partial ExamPrepModel? Prep { get; set; }
    public bool Prepping => Prep is not null;
    /// <summary>The exams coming up (Canvas, the calendar) and the ones prepped for, soonest first.</summary>
    public ObservableCollection<ExamRow> Exams { get; } = [];
    public bool HasExams => Exams.Count > 0;
    public bool ExamPrepAvailable { get; private set; }

    /// <summary>Prepping for an exam Canvas and the calendar don't know of: its class, name and (if known) date.</summary>
    public ObservableCollection<ExamClassChoice> ClassChoices { get; } = [];
    public bool HasClassChoices => ClassChoices.Count > 0;
    [ObservableProperty] public partial string? NewClass { get; set; }
    [ObservableProperty] public partial string NewTitle { get; set; } = "";
    [ObservableProperty] public partial DateTime? NewWhen { get; set; }
    [ObservableProperty] public partial bool MakingNew { get; set; }
    [ObservableProperty] public partial string? NewProblem { get; set; }
    public bool HasNewProblem => !string.IsNullOrEmpty(NewProblem);
    public bool CanMakeNew => !MakingNew && NewClass is { Length: > 0 } && NewTitle.Trim().Length > 0;
    public string MakeNewSaid => MakingNew ? "Writing your prep…" : "Write the prep";

    public bool HasDue => Due > 0;
    public bool NoCards => !Loading && Total == 0;
    /// <summary>Review's card, with cards in it; or, with none yet, the dashed one that says how to make some. Both
    /// wait for the first load only: a reload (back from a review) keeps the card where it is.</summary>
    public bool ShowReview => Available && Loaded && Total > 0;
    public bool ShowNoCards => Available && Loaded && Total == 0;
    /// <summary>The first load has answered.</summary>
    [ObservableProperty] public partial bool Loaded { get; set; }
    partial void OnLoadedChanged(bool value) => Said();
    public string DueTitle => Due == 0 ? (Total == 0 ? "No cards yet" : "All caught up") : Due == 1 ? "1 card to review" : $"{Due} cards to review";
    public string DueMore => Total == 0
        ? "Open a lecture and press Make review cards: they come back here just before you'd forget them."
        : Due == 0 ? (Total == 1 ? "1 card in Review. It comes back when it's due." : $"{Total} cards in Review. The next ones come back when they're due.")
        : "A few minutes now keeps them for the exam.";

    public Action<string, double?>? OnOpenLecture { get; set; }
    /// <summary>The sidebar's count followed what's due (the host wires it).</summary>
    public Action<int>? OnCount { get; set; }

    partial void OnDueChanged(int value) => Said();
    partial void OnTotalChanged(int value) => Said();
    partial void OnLoadingChanged(bool value) => Said();
    partial void OnAvailableChanged(bool value) => Said();
    partial void OnSessionChanged(ReviewSessionModel? value) => Shown();
    partial void OnPrepChanged(ExamPrepModel? value) => Shown();
    partial void OnNewClassChanged(string? value)
    {
        foreach (var c in ClassChoices) c.Picked = c.Name == value;
        OnPropertyChanged(nameof(NewClassShown));
        OnPropertyChanged(nameof(NewClassDot));
        OnPropertyChanged(nameof(NewCovers));
        NewChanged();
    }

    /// <summary>The class the new prep is for, as its picker shows it.</summary>
    public string NewClassShown => NewClass is { } c ? ClassNames.Display(c) : "";
    public IBrush NewClassDot => NewClass is { } c ? DotOf(c) : Brushes.Gray;

    /// <summary>"Another exam" pressed: its row (the class, a name, Write the prep) shows. With no exams listed it
    /// shows straight away.</summary>
    [ObservableProperty] public partial bool AddingAnother { get; set; }
    public bool ShowAnother => HasClassChoices && (AddingAnother || !HasExams);
    public bool ShowAnotherButton => HasClassChoices && HasExams && !AddingAnother;
    /// <summary>The exams' card: the exams, and Another exam (or its row).</summary>
    public bool HasExamCard => HasExams || HasClassChoices;
    public string ExamsSaid => HasExams ? "Coming up" : "None coming up in Canvas or your calendar";
    /// <summary>Under the new prep's row: what it's written from.</summary>
    public string NewCovers => NewClass is null ? "" : $"From {NewClassShown}'s lectures since its last exam.";
    partial void OnAddingAnotherChanged(bool value) => Another();

    void Another()
    {
        OnPropertyChanged(nameof(ShowAnother));
        OnPropertyChanged(nameof(ShowAnotherButton));
        OnPropertyChanged(nameof(HasExamCard));
        OnPropertyChanged(nameof(ExamsSaid));
    }

    [RelayCommand] void AddAnother() => AddingAnother = true;
    partial void OnNewTitleChanged(string value) => NewChanged();
    partial void OnMakingNewChanged(bool value) => NewChanged();
    partial void OnNewProblemChanged(string? value) => OnPropertyChanged(nameof(HasNewProblem));

    void NewChanged()
    {
        OnPropertyChanged(nameof(CanMakeNew));
        OnPropertyChanged(nameof(MakeNewSaid));
        MakeNewPrepCommand.NotifyCanExecuteChanged();
    }

    void Shown()
    {
        OnPropertyChanged(nameof(Reviewing));
        OnPropertyChanged(nameof(Prepping));
        OnPropertyChanged(nameof(ShowHub));
    }

    void Said()
    {
        OnPropertyChanged(nameof(HasDue));
        OnPropertyChanged(nameof(NoCards));
        OnPropertyChanged(nameof(ShowReview));
        OnPropertyChanged(nameof(ShowNoCards));
        OnPropertyChanged(nameof(DueTitle));
        OnPropertyChanged(nameof(DueMore));
    }

    /// <summary>Reads what's due, and how much by class.</summary>
    public async Task LoadAsync()
    {
        Loading = true;
        try
        {
            if (await library.ReviewAsync(limit: 500) is { } state)
            {
                Available = true;
                Due = state.Counts.Due;
                Total = state.Counts.Total;
                ByClass.Clear();
                foreach (var g in state.Due.Where(c => c.Class.Length > 0).GroupBy(c => c.Class).OrderByDescending(g => g.Count()))
                {
                    string cls = g.Key;
                    ByClass.Add(new StudyClassRow(cls, g.Count(), new AsyncRelayCommand(() => ReviewAsync(cls)), DotOf(cls)));
                }
                OnCount?.Invoke(Due);
            }
            else Available = false;
        }
        catch (Exception e) when (LibraryTrouble.Handled(e))
        {
            Available = false;
        }
        await LoadExamsAsync();
        Loading = false;
        Loaded = true;
    }

    /// <summary>The classes a new exam prep can be for (the host gives the library's, each with its colour).</summary>
    public void Classes(IEnumerable<string> names) => Classes(names.Select(n => (n, (IBrush)Brushes.Gray)));

    public void Classes(IEnumerable<(string Name, IBrush Dot)> classes)
    {
        ClassChoices.Clear();
        dots.Clear();
        foreach (var (n, dot) in classes)
        {
            dots[n] = dot;
            ClassChoices.Add(new ExamClassChoice(n, new RelayCommand(() => NewClass = n), dot) { Picked = n == NewClass });
        }
        NewClass ??= ClassChoices.FirstOrDefault()?.Name;
        OnPropertyChanged(nameof(HasClassChoices));
        OnPropertyChanged(nameof(NewClassShown));
        OnPropertyChanged(nameof(NewClassDot));
        OnPropertyChanged(nameof(NewCovers));
        Another();
    }

    readonly Dictionary<string, IBrush> dots = [];

    /// <summary>A class's colour, grey for one the library doesn't list.</summary>
    IBrush DotOf(string cls) => dots.GetValueOrDefault(cls, Brushes.Gray);

    /// <summary>The exams coming up and the prep made: each exam once, its prep beside it when it has one.</summary>
    public async Task LoadExamsAsync()
    {
        ExamsNow? now;
        try
        {
            now = await library.ExamsAsync();
        }
        catch (Exception e) when (LibraryTrouble.Handled(e))
        {
            now = null;
        }
        ExamPrepAvailable = now is not null;
        OnPropertyChanged(nameof(ExamPrepAvailable));
        Exams.Clear();
        if (now is null)
        {
            OnPropertyChanged(nameof(HasExams));
            Another();
            return;
        }
        var t = clock();
        var rows = new List<ExamRow>();
        foreach (var e in now.Upcoming) rows.Add(Row(e.Class, e.Title, e.When, t));
        foreach (var p in now.Preps)
        {
            var row = rows.FirstOrDefault(r => r.Class == p.Class && ExamPrep.Same(r.Title) == ExamPrep.Same(p.Title));
            if (row is null) rows.Add(row = Row(p.Class, p.Title, p.When, t));
            if (row.PrepId is not null) continue; // the newest prep (they come newest first)
            row.PrepId = p.Id;
            row.Score = p.LastRight is { } right ? $"Last practice test: {right} of {p.Questions}" : $"{p.Questions} practice questions";
        }
        foreach (var r in rows.OrderBy(r => r.When is null).ThenBy(r => r.When)) Exams.Add(r);
        OnPropertyChanged(nameof(HasExams));
        Another();
    }

    ExamRow Row(string cls, string title, DateTimeOffset? when, DateTimeOffset now)
    {
        ExamRow? row = null;
        row = new ExamRow
        {
            Class = cls, Title = title, When = when, WhenSaid = ExamWhen.Say(when, now), Soon = ExamWhen.Soon(when, now), Dot = DotOf(cls),
            Go = new AsyncRelayCommand(() => row!.PrepId is { } id ? OpenPrepAsync(id) : MakePrepAsync(row)),
        };
        return row;
    }

    async Task MakePrepAsync(ExamRow row)
    {
        if (row.Making) return;
        row.Making = true;
        row.Problem = null;
        try
        {
            if (await library.MakePrepAsync(row.Class, row.Title, row.When) is { } prep)
            {
                row.PrepId = prep.Id;
                row.Score = $"{prep.Questions.Count} practice questions";
                Show(prep);
            }
        }
        catch (LibraryRefusedException e)
        {
            row.Problem = e.Message;
        }
        catch (Exception e) when (LibraryTrouble.Handled(e))
        {
            row.Problem = "Your library isn't answering. Try again in a moment.";
        }
        finally
        {
            row.Making = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanMakeNew))]
    async Task MakeNewPrep()
    {
        if (NewClass is not { } cls) return;
        MakingNew = true;
        NewProblem = null;
        try
        {
            DateTimeOffset? when = NewWhen is { } d ? new DateTimeOffset(d.Date.AddHours(9)) : null;
            if (await library.MakePrepAsync(cls, NewTitle.Trim(), when) is { } prep)
            {
                NewTitle = "";
                NewWhen = null;
                await LoadExamsAsync();
                Show(prep);
            }
        }
        catch (LibraryRefusedException e)
        {
            NewProblem = e.Message;
        }
        catch (Exception e) when (LibraryTrouble.Handled(e))
        {
            NewProblem = "Your library isn't answering. Try again in a moment.";
        }
        finally
        {
            MakingNew = false;
        }
    }

    public async Task OpenPrepAsync(string id)
    {
        try
        {
            if (await library.PrepAsync(id) is { } prep) Show(prep);
        }
        catch (Exception e) when (LibraryTrouble.Handled(e))
        {
        }
    }

    void Show(ExamPrepDoc prep)
    {
        var model = new ExamPrepModel(library, prep, clock(), OnOpenLecture);
        model.OnClose = () =>
        {
            Prep = null;
            _ = LoadExamsAsync();
        };
        model.OnDeleted = model.OnClose;
        Prep = model;
    }

    [RelayCommand(CanExecute = nameof(HasDue))]
    Task StartReview() => ReviewAsync(null);

    partial void OnDueChanged(int oldValue, int newValue) => StartReviewCommand.NotifyCanExecuteChanged();

    public async Task ReviewAsync(string? className, string? lectureId = null)
    {
        var session = new ReviewSessionModel(library, clock) { OnOpenLecture = OnOpenLecture };
        session.OnDone = () =>
        {
            Session = null;
            _ = LoadAsync();
        };
        Session = session;
        await session.StartAsync(className, lectureId);
    }
}

/// <summary>A class a new exam prep can be for, as a row of its menu: picked or not.</summary>
public sealed partial class ExamClassChoice(string name, IRelayCommand pick, IBrush dot) : ObservableObject
{
    public string Name { get; } = name;
    public IBrush Dot { get; } = dot;
    public string Display => ClassNames.Display(Name);
    public IRelayCommand Pick { get; } = pick;
    [ObservableProperty] public partial bool Picked { get; set; }
}
