using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StudyStash.App.Controls;
using StudyStash.App.ViewModels;
using StudyStash.App.Views;
using StudyStash.Core.Canvas;
using StudyStash.Core.Tests;

namespace StudyStash.App.Tests;

/// <summary>Exam prep in the app, against a library run here (its AI a stand-in): the Study page lists the exams coming
/// up from Canvas with Prep for it, writes the prep, opens it on its guide, and the practice test marks each answer,
/// keeps the score and puts what was missed into Review.</summary>
public class ExamPrepPageTests
{
    public const string Prep = """
        {"guide":"## What the lecturer said to know\n- The three layers of an artery wall (Lecture 1, 02:30)\n\n## Vessels\nArteries carry blood away from the heart.",
         "questions":[
           {"kind":"choice","question":"Which vessel is one cell thick?","choices":["Artery","Vein","Capillary","Aorta"],"answer":2,"why":"Exchange happens there.","lecture":1,"moment":"04:00"},
           {"kind":"short","question":"Name the three layers of an artery wall.","model_answer":"Intima, media and externa.","why":"Said to know it.","lecture":1,"moment":"02:30"}
         ]}
        """;

    public static async Task<StudyLibrary> LibraryAsync()
    {
        var lib = await StudyLibrary.StartAsync(l => Assignments.Save(l.Cfg.Home,
        [
            new Assignment("BIO 110", 1, "Midterm 1", "", null, "graded", null, "", "", DueAt: "2026-10-05T14:00:00Z", Kind: "quiz"),
            new Assignment("BIO 110", 2, "Midterm 2", "", null, "", null, "", "", DueAt: "2026-10-29T14:00:00Z", Kind: "quiz"),
        ]));
        lib.Answers.Add(Prep);
        return lib;
    }

    [AvaloniaFact]
    public async Task An_exam_coming_up_is_prepped_for_and_its_test_marked_and_kept()
    {
        await using var lib = await LibraryAsync();
        var page = new StudyModel(lib.Study, () => lib.Now);
        (string, double?)? opened = null;
        page.OnOpenLecture = (id, at) => opened = (id, at);
        await page.LoadAsync();

        var exam = page.Exams.Single();
        Assert.Equal(("Midterm 2", "Prep for it", false), (exam.Title, exam.ActionSaid, exam.HasPrep));
        Assert.Contains("in 9 days", exam.Line);

        await exam.Go.ExecuteAsync(null);
        var prep = page.Prep!;
        Assert.True(page.Prepping);
        Assert.False(page.ShowHub);
        Assert.Equal(("Midterm 2", "From 1 lecture: Blood vessels", true), (prep.Title, prep.Covers, prep.ShowGuide));
        Assert.StartsWith("## What the lecturer said to know", prep.Guide);

        prep.TestTabCommand.Execute(null);
        var choice = prep.Questions[0];
        choice.Choices[0].Pick.Execute(null); // Artery: wrong
        Assert.Equal((false, true, true), (choice.Right, choice.Choices[0].ShowWrong, choice.Choices[2].ShowRight));
        Assert.Equal("The answer is Capillary", choice.Verdict);
        choice.Choices[2].Pick.Execute(null); // answered: it stays answered
        Assert.False(choice.Right);
        choice.OpenMomentCommand.Execute(null);
        Assert.Equal(("v1", 240d), opened);

        var written = prep.Questions[1];
        Assert.False(prep.FinishCommand.CanExecute(null));
        written.Typed = "Intima, media, externa";
        written.RevealCommand.Execute(null);
        Assert.True(written.CanMark);
        written.MarkRightCommand.Execute(null);
        Assert.Equal("1 of 2 right · 0 to go", prep.Score);

        await prep.FinishCommand.ExecuteAsync(null);
        Assert.Equal("1 of 2. The one you missed is in Review now.", prep.Saved);
        Assert.Equal("Which vessel is one cell thick?\nA. Artery\nB. Vein\nC. Capillary\nD. Aorta", (await lib.Study.LectureCardsAsync("v1"))!.Single().Front);

        prep.CloseCommand.Execute(null);
        await Until(() => page.Exams.Count == 1 && page.Exams[0].Score.StartsWith("Last"));
        Assert.Equal(("Open prep", "Last practice test: 1 of 2"), (page.Exams[0].ActionSaid, page.Exams[0].Score));
        await page.Exams[0].Go.ExecuteAsync(null);
        Assert.Equal("Last time: 1 of 2", page.Prep!.LastScore);
        Assert.Single(lib.Claude.Prompts); // opened, not written again
    }

    [AvaloniaFact]
    public async Task Another_exam_is_prepped_for_by_its_class_and_name_and_a_prep_can_be_deleted()
    {
        await using var lib = await LibraryAsync();
        var page = new StudyModel(lib.Study, () => lib.Now);
        page.Classes(["BIO 110", "CHEM 121"]);
        await page.LoadAsync();
        Assert.Equal("BIO 110", page.NewClass);
        Assert.False(page.MakeNewPrepCommand.CanExecute(null));

        page.ClassChoices[1].Pick.Execute(null);
        Assert.True(page.ClassChoices[1].Picked);
        page.NewTitle = "Quiz 4";
        await page.MakeNewPrepCommand.ExecuteAsync(null);
        Assert.Equal("Quiz 4", page.Prep!.Title);
        Assert.Contains("Moles", page.Prep.Covers);
        Assert.Equal("", page.NewTitle);

        await page.Prep.DeleteCommand.ExecuteAsync(null);
        await Until(() => page.Prep is null && page.Exams.Count == 1);
        Assert.Empty((await lib.Study.ExamsAsync())!.Preps);
    }

    [AvaloniaFact]
    public async Task A_prep_the_AI_couldnt_write_says_why_on_its_row()
    {
        await using var lib = await LibraryAsync();
        lib.Answers[0] = "no";
        var page = new StudyModel(lib.Study, () => lib.Now);
        await page.LoadAsync();
        await page.Exams[0].Go.ExecuteAsync(null);
        Assert.Contains("couldn't be written", page.Exams[0].Problem);
        Assert.Null(page.Prep);
    }

    [AvaloniaTheory]
    [InlineData(SkinKind.Mac)]
    [InlineData(SkinKind.Win)]
    public async Task The_prep_shows_its_guide_and_its_test(SkinKind skin)
    {
        await using var lib = await LibraryAsync();
        var model = new StudyModel(lib.Study, () => lib.Now);
        model.Classes(["BIO 110"]);
        await model.LoadAsync();
        var page = new StudyPage { DataContext = model };
        var w = MenuPickTests.Host(page, skin);
        try
        {
            Assert.Contains(page.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Midterm 2" && t.IsEffectivelyVisible);
            Assert.True(page.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "NewPrep").IsEffectivelyVisible);
            await model.Exams[0].Go.ExecuteAsync(null);
            Dispatcher.UIThread.RunJobs();
            Assert.True(page.GetVisualDescendants().OfType<NoteView>().Single(v => v.Name == "Guide").IsEffectivelyVisible);
            model.Prep!.TestTabCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.Contains(page.GetVisualDescendants().OfType<PageText>(), t => t.Text == "1. Which vessel is one cell thick?" && t.IsEffectivelyVisible);
            Assert.Contains(page.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Capillary" && t.IsEffectivelyVisible);
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
}
