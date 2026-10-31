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
        Shot.Take(Name("exam-flags", skin), skin, dark ? ThemeVariant.Dark : ThemeVariant.Light,
            () => new NotePage { DataContext = new LibraryModel { Note = note }, Width = 900, Height = 760 }, size: new Size(1028, 888));
    }
}
