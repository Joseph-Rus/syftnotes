using StudyStash.App.ViewModels;
using StudyStash.Core;
using StudyStash.Core.Study;

namespace StudyStash.App.Tests.Snapshots;

/// <summary>The Study page from a stand-in library (nothing is asked of a real one): twelve cards due in three classes,
/// two exams coming up (one already prepped, with a score), and the classes a new prep can be for.</summary>
static class StudyScene
{
    static readonly DateTimeOffset Now = new(2026, 10, 12, 13, 34, 0, TimeSpan.Zero);

    public static async Task<StudyModel> Page()
    {
        var model = new StudyModel(new Library(), () => Now);
        // As the app does it: the library's classes and their colours first, then what's due and the exams.
        string[] names = ["BIO 110", "CS 201", "HIST 150", "MATH 221", "PSYC 101", "Capstone Design"];
        model.Classes(names.Select((n, i) => (n, Skin.ClassDot(i))));
        await model.LoadAsync();
        return model;
    }

    static ReviewCard Card(int i, string cls) =>
        new() { Id = $"c{i}", Lecture = $"v{i}", Class = cls, Front = "Which vessel is one cell thick?", Back = "A capillary.", Due = Now.AddHours(-1) };

    sealed class Library : IStudyLibrary
    {
        public Task<ReviewState?> ReviewAsync(string? className = null, int limit = 100) => Task.FromResult<ReviewState?>(new(new ReviewCounts(12, 3, 48),
        [
            .. Enumerable.Range(0, 7).Select(i => Card(i, "BIO 110")),
            .. Enumerable.Range(7, 3).Select(i => Card(i, "CS 201")),
            .. Enumerable.Range(10, 2).Select(i => Card(i, "HIST 150")),
        ]));

        public Task<ExamsNow?> ExamsAsync() => Task.FromResult<ExamsNow?>(new(
            [new ExamDate("BIO 110", "Midterm 2", Now.AddDays(9), "canvas"), new ExamDate("CS 201", "Quiz 4", Now.AddDays(3), "calendar")],
            [new PrepSummary("p1", "CS 201", "Quiz 4", Now.AddDays(3), Now.AddDays(-1), 3, 12, 9)]));

        public Task<List<ReviewCard>?> LectureCardsAsync(string lecture) => Task.FromResult<List<ReviewCard>?>(null);
        public Task<MadeCards?> MakeCardsAsync(string lecture, bool more = false) => Task.FromResult<MadeCards?>(null);
        public Task<ReviewCard?> AddCardAsync(string lecture, string front, string back, double? at = null, string from = "own", string? className = null) => Task.FromResult<ReviewCard?>(null);
        public Task<ReviewCard?> GradeAsync(string card, Grade grade) => Task.FromResult<ReviewCard?>(null);
        public Task<ReviewCard?> EditCardAsync(string card, string front, string back) => Task.FromResult<ReviewCard?>(null);
        public Task<bool> DeleteCardAsync(string card) => Task.FromResult(false);
        public Task<List<ExamHint>?> ExamFlagsAsync(string lecture) => Task.FromResult<List<ExamHint>?>(null);
    }
}
