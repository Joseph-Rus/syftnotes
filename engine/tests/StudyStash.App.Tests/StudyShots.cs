using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using StudyStash.App.ViewModels;
using StudyStash.App.Views;
using StudyStash.Core;
using StudyStash.Core.Tests;

namespace StudyStash.App.Tests;

/// <summary>The study tools drawn, light and dark, both looks, into STUDYSTASH_SHOTS (or shots/ beside the tests):
/// the exam flags over a lecture's notes, Review, exam prep and Listen. Pictures to look at, not compared.</summary>
public class StudyShots
{
    public static IEnumerable<object[]> Looks() =>
        from skin in new[] { SkinKind.Mac, SkinKind.Win }
        from dark in new[] { false, true }
        select new object[] { skin, dark };

    static string Name(string what, SkinKind skin) => $"study-{what}-{(skin == SkinKind.Mac ? "mac" : "win")}";

    [AvaloniaTheory]
    [MemberData(nameof(Looks))]
    public void Exam_flags_over_a_lectures_notes(SkinKind skin, bool dark)
    {
        var note = new NoteModel { Id = "v1", Title = "Blood vessels", Markdown = "## Three kinds of vessel\n- Arteries carry blood away.\n- Capillaries are one cell thick.\n- Veins carry it back." };
        foreach (var line in TimedText.Parse(StudyLibrary.BloodTranscript))
        {
            note.Transcript.Add(new HeardLine { Time = TimedText.Clock(line.Start), Text = line.Text, Start = line.Start });
            note.Spoken.Add(line);
        }
        note.FlagExam();
        var study = new LectureStudyModel(new StudyLibraryStub(), "v1") { Available = true, Cards = 12 };
        Shot.Take(Name("exam-flags", skin), skin, dark ? ThemeVariant.Dark : ThemeVariant.Light,
            () => new NotePage { DataContext = new LibraryModel { Note = note, LectureStudy = study }, Width = 900, Height = 760 }, size: new Size(1028, 888));
    }

    const string Cards = """
        {"cards":[
          {"front":"What are the three layers of an artery wall?","back":"Tunica intima, tunica media and tunica externa.","moment":"02:30"},
          {"front":"How thick is a capillary wall?","back":"One cell thick.","moment":"04:00"}
        ]}
        """;

    [AvaloniaTheory]
    [MemberData(nameof(Looks))]
    public async Task Study_page_and_a_card_being_reviewed(SkinKind skin, bool dark)
    {
        await using var lib = await StudyLibrary.StartAsync();
        lib.Answers.Add(Cards);
        await lib.Study.MakeCardsAsync("v1");
        await lib.Study.AddCardAsync("c1", "What is a mole?", "6.02 × 10²³ things.");
        var variant = dark ? ThemeVariant.Dark : ThemeVariant.Light;

        var hub = new StudyModel(lib.Study, () => lib.Now);
        await hub.LoadAsync();
        Shot.Take(Name("hub", skin), skin, variant, () => new StudyPage { DataContext = hub, Width = 900, Height = 560 }, size: new Size(1028, 688));

        var review = new StudyModel(lib.Study, () => lib.Now);
        await review.LoadAsync();
        await review.StartReviewCommand.ExecuteAsync(null);
        review.Session!.ShowBackCommand.Execute(null);
        Shot.Take(Name("review", skin), skin, variant, () => new StudyPage { DataContext = review, Width = 900, Height = 620 }, size: new Size(1028, 748));
    }

    /// <summary>A library that's never asked: a picture's models are filled in by hand.</summary>
    sealed class StudyLibraryStub : StudyStash.Core.Study.IStudyLibrary
    {
        public Task<StudyStash.Core.Study.ReviewState?> ReviewAsync(string? className = null, int limit = 100) => Task.FromResult<StudyStash.Core.Study.ReviewState?>(null);
        public Task<List<StudyStash.Core.Study.ReviewCard>?> LectureCardsAsync(string lecture) => Task.FromResult<List<StudyStash.Core.Study.ReviewCard>?>(null);
        public Task<StudyStash.Core.Study.MadeCards?> MakeCardsAsync(string lecture, bool more = false) => Task.FromResult<StudyStash.Core.Study.MadeCards?>(null);
        public Task<StudyStash.Core.Study.ReviewCard?> AddCardAsync(string lecture, string front, string back, double? at = null, string from = "own", string? className = null) => Task.FromResult<StudyStash.Core.Study.ReviewCard?>(null);
        public Task<StudyStash.Core.Study.ReviewCard?> GradeAsync(string card, StudyStash.Core.Study.Grade grade) => Task.FromResult<StudyStash.Core.Study.ReviewCard?>(null);
        public Task<StudyStash.Core.Study.ReviewCard?> EditCardAsync(string card, string front, string back) => Task.FromResult<StudyStash.Core.Study.ReviewCard?>(null);
        public Task<bool> DeleteCardAsync(string card) => Task.FromResult(false);
        public Task<List<ExamHint>?> ExamFlagsAsync(string lecture) => Task.FromResult<List<ExamHint>?>(null);
    }
}
