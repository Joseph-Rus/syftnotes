using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StudyStash.App.Controls;
using StudyStash.App.ViewModels;
using StudyStash.App.Views;
using StudyStash.Core.Study;
using StudyStash.Core.Tests;

namespace StudyStash.App.Tests;

/// <summary>Review in the app, against a library run here (its AI a stand-in): a lecture's page makes its cards, the
/// Study page says how many are due and by class, and a review goes through them a card at a time, by buttons or by
/// keys (Space, then 1 to 4), one forgotten coming round again before the end.</summary>
public class ReviewPageTests
{
    const string Cards = """
        {"cards":[
          {"front":"What are the three layers of an artery wall?","back":"Intima, media and externa.","moment":"02:30"},
          {"front":"How thick is a capillary wall?","back":"One cell thick.","moment":"04:00"},
          {"front":"What stops blood flowing backwards in veins?","back":"Valves."}
        ]}
        """;

    static async Task<StudyLibrary> WithCardsAsync()
    {
        var lib = await StudyLibrary.StartAsync();
        lib.Answers.Add(Cards);
        return lib;
    }

    [AvaloniaFact]
    public async Task A_lectures_page_makes_its_cards_once_and_then_offers_to_review_them()
    {
        await using var lib = await WithCardsAsync();
        var study = new LectureStudyModel(lib.Study, "v1");
        await study.LoadAsync();
        Assert.True(study.Available);
        Assert.True(study.CanMake);
        bool made = false;
        study.OnMade = () => made = true;

        await study.MakeCardsCommand.ExecuteAsync(null);

        Assert.Equal((3, "3 cards in Review", false), (study.Cards, study.CardsSaid, study.CanMake));
        Assert.True(made);
        string? reviewed = null;
        study.OnReview = id => reviewed = id;
        study.ReviewCommand.Execute(null);
        Assert.Equal("v1", reviewed);
    }

    [AvaloniaFact]
    public async Task An_AI_that_writes_no_cards_says_so_on_the_lecture()
    {
        await using var lib = await StudyLibrary.StartAsync();
        lib.Answers.Add("I can't.");
        var study = new LectureStudyModel(lib.Study, "v1");
        await study.LoadAsync();
        await study.MakeCardsCommand.ExecuteAsync(null);
        Assert.Contains("couldn't be made", study.Problem);
        Assert.True(study.CanMake);
    }

    [AvaloniaFact]
    public async Task A_library_too_old_for_Review_shows_no_study_tools()
    {
        var old = new OldLibrary();
        var study = new LectureStudyModel(old, "v1");
        await study.LoadAsync();
        Assert.False(study.Available);
        var page = new StudyModel(old);
        await page.LoadAsync();
        Assert.False(page.Available);
    }

    [AvaloniaFact]
    public async Task The_Study_page_counts_whats_due_and_a_review_goes_through_it()
    {
        await using var lib = await WithCardsAsync();
        await lib.Study.MakeCardsAsync("v1");
        var page = new StudyModel(lib.Study, () => lib.Now);
        int counted = -1;
        page.OnCount = n => counted = n;
        await page.LoadAsync();

        Assert.Equal(("3 cards to review", 3), (page.DueTitle, counted));
        Assert.Equal("BIO 110", page.ByClass.Single().Name);
        Assert.Equal("3 due", page.ByClass.Single().DueSaid);

        await page.StartReviewCommand.ExecuteAsync(null);
        var s = page.Session!;
        Assert.True(page.Reviewing);
        Assert.Equal(("What are the three layers of an artery wall?", "3 cards left", false), (s.Front, s.Progress, s.ShowingBack));
        Assert.Equal("Blood vessels · 02:30", s.Source);
        Assert.False(s.Key(3)); // the answer isn't showing yet

        s.ShowBackCommand.Execute(null);
        Assert.Equal(["10 min", "1 day", "1 day", "4 days"], s.Grades.Select(g => g.After));
        Assert.True(s.Key(1)); // Again: it comes round again
        await Until(() => s.Front == "How thick is a capillary wall?");
        Assert.Equal("3 cards left", s.Progress);
        s.ShowBackCommand.Execute(null);
        Assert.True(s.Key(3));
        await Until(() => s.Front == "What stops blood flowing backwards in veins?");
        s.ShowBackCommand.Execute(null);
        Assert.True(s.Key(4));
        await Until(() => s.Front == "What are the three layers of an artery wall?");
        Assert.Equal("1 card left", s.Progress);
        s.ShowBackCommand.Execute(null);
        Assert.True(s.Key(3));
        await Until(() => s.Finished);

        Assert.Equal("You reviewed 4 cards", s.FinishedTitle);
        var cards = (await lib.Study.LectureCardsAsync("v1"))!;
        Assert.Equal([1d, 1d, 4d], cards.Select(c => c.Interval).Order());

        s.CloseCommand.Execute(null);
        await Until(() => !page.Loading);
        Assert.False(page.Reviewing);
        Assert.Equal(("All caught up", 0), (page.DueTitle, counted));
    }

    [AvaloniaFact]
    public async Task A_lectures_review_is_just_its_cards_and_a_card_opens_its_lecture_at_its_moment()
    {
        await using var lib = await WithCardsAsync();
        await lib.Study.MakeCardsAsync("v1");
        await lib.Study.AddCardAsync("c1", "What is a mole?", "6.02e23 things.");
        var page = new StudyModel(lib.Study, () => lib.Now);
        (string, double?)? opened = null;
        page.OnOpenLecture = (id, at) => opened = (id, at);
        await page.LoadAsync();
        Assert.Equal(4, page.Due);

        await page.ReviewAsync(null, "v1");
        Assert.Equal(("Review this lecture", "3 cards left"), (page.Session!.Title, page.Session.Progress));
        page.Session.OpenLectureCommand.Execute(null);
        Assert.Equal(("v1", 150d), opened);

        await page.ReviewAsync("CHEM 121");
        Assert.Equal(("What is a mole?", "1 card left"), (page.Session!.Front, page.Session.Progress));
        await page.Session.DeleteCardCommand.ExecuteAsync(null);
        Assert.True(page.Session.Finished);
        Assert.Equal(3, (await lib.Study.ReviewAsync())!.Counts.Total);
    }

    [AvaloniaTheory]
    [InlineData(SkinKind.Mac)]
    [InlineData(SkinKind.Win)]
    public async Task The_page_reviews_by_keys(SkinKind skin)
    {
        await using var lib = await WithCardsAsync();
        await lib.Study.MakeCardsAsync("v1");
        var model = new StudyModel(lib.Study, () => lib.Now);
        await model.LoadAsync();
        var page = new StudyPage { DataContext = model };
        var w = MenuPickTests.Host(page, skin);
        try
        {
            Assert.True(page.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "StartReview").IsEffectivelyVisible);
            await model.StartReviewCommand.ExecuteAsync(null);
            Dispatcher.UIThread.RunJobs();
            var front = page.GetVisualDescendants().OfType<PageText>().Single(t => t.Name == "Front");
            Assert.Equal("What are the three layers of an artery wall?", front.Text);
            var back = page.GetVisualDescendants().OfType<PageText>().Single(t => t.Name == "Back");
            Assert.False(back.IsEffectivelyVisible);

            page.Focus();
            w.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Assert.True(back.IsEffectivelyVisible);
            Assert.Contains(page.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "4 days" && t.IsEffectivelyVisible);

            w.KeyPressQwerty(PhysicalKey.Digit3, RawInputModifiers.None);
            await Until(() => model.Session!.Front == "How thick is a capillary wall?");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("How thick is a capillary wall?", front.Text);
            Assert.False(back.IsEffectivelyVisible);
        }
        finally
        {
            w.Close();
        }
    }

    static async Task Until(Func<bool> done)
    {
        for (int i = 0; i < 300 && !done(); i++)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
            Dispatcher.UIThread.RunJobs();
        }
        Assert.True(done());
    }

    /// <summary>A library from before Review: every route is a plain 404.</summary>
    sealed class OldLibrary : IStudyLibrary
    {
        public Task<ReviewState?> ReviewAsync(string? className = null, int limit = 100) => Task.FromResult<ReviewState?>(null);
        public Task<List<ReviewCard>?> LectureCardsAsync(string lecture) => Task.FromResult<List<ReviewCard>?>(null);
        public Task<MadeCards?> MakeCardsAsync(string lecture, bool more = false) => Task.FromResult<MadeCards?>(null);
        public Task<ReviewCard?> AddCardAsync(string lecture, string front, string back, double? at = null, string from = "own", string? className = null) => Task.FromResult<ReviewCard?>(null);
        public Task<ReviewCard?> GradeAsync(string card, Grade grade) => Task.FromResult<ReviewCard?>(null);
        public Task<ReviewCard?> EditCardAsync(string card, string front, string back) => Task.FromResult<ReviewCard?>(null);
        public Task<bool> DeleteCardAsync(string card) => Task.FromResult(false);
        public Task<List<Core.ExamHint>?> ExamFlagsAsync(string lecture) => Task.FromResult<List<Core.ExamHint>?>(null);
    }
}
