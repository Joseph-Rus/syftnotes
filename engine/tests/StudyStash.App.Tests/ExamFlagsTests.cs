using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StudyStash.App.Controls;
using StudyStash.App.ViewModels;
using StudyStash.App.Views;
using StudyStash.Core;
using StudyStash.Core.Tests;

namespace StudyStash.App.Tests;

/// <summary>The exam flags over a lecture's notes: what the lecturer said is on the exam, each moment opening the
/// transcript there, what they said isn't under it, and nothing at all for a lecture that flagged nothing.</summary>
public class ExamFlagsTests
{
    static NoteModel Lecture(string transcript)
    {
        var note = new NoteModel { Id = "v1", Title = "Blood vessels", Markdown = "## Vessels\n- Arteries." };
        foreach (var line in TimedText.Parse(transcript))
        {
            note.Transcript.Add(new HeardLine { Time = TimedText.Clock(line.Start), Text = line.Text, Start = line.Start });
            note.Spoken.Add(line);
        }
        return note.FlagExam();
    }

    [AvaloniaFact]
    public void A_lecture_lists_its_flags_and_the_one_said_not_to_be_on_it()
    {
        var note = Lecture(StudyLibrary.BloodTranscript);

        Assert.Equal(["02:30", "06:40"], note.ExamFlags.Select(f => f.Moment));
        Assert.Equal("05:15", note.NotOnExam.Single().Moment);
        Assert.Equal("Flagged for the exam · 2", note.ExamFlagsTitle);
        Assert.True(note.HasExamFlags);

        note.ExamFlags[0].Open.Execute(null);
        Assert.True(note.ShowTranscript);
        Assert.True(note.Transcript[2].Here);
        Assert.True(note.CanGoBack);
    }

    [AvaloniaFact]
    public void A_lecture_that_flagged_nothing_has_no_card()
    {
        var note = Lecture("[00:01] Arteries carry blood away.\n[00:05] Veins carry it back.");
        Assert.False(note.HasExamFlags);
        Assert.Empty(note.ExamFlags);
    }

    [AvaloniaTheory]
    [InlineData(SkinKind.Mac)]
    [InlineData(SkinKind.Win)]
    public void The_card_shows_over_the_notes_and_shuts_to_one_line(SkinKind skin)
    {
        var note = Lecture(StudyLibrary.BloodTranscript);
        var page = new NotePage { DataContext = new LibraryModel { Note = note } };
        var w = MenuPickTests.Host(page, skin);
        try
        {
            var card = page.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "ExamFlags");
            Assert.True(card.IsEffectivelyVisible);
            string shown = string.Join("\n", card.GetVisualDescendants().OfType<PageText>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text));
            Assert.Contains("make sure you know the three layers of an artery wall", shown);
            Assert.Contains("won't be on the exam", shown);
            Assert.Contains(card.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Said not to be on it" && t.IsEffectivelyVisible);

            note.ToggleExamFlagsCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.DoesNotContain(card.GetVisualDescendants().OfType<PageText>(), t => t.IsEffectivelyVisible);
            Assert.Contains(card.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Flagged for the exam · 2" && t.IsEffectivelyVisible);

            // While it records, the page is the student's own typing: no card.
            var live = new NoteModel { Id = "rec", Title = "Now", Live = true };
            live.Transcript.Add(new HeardLine { Time = "00:01", Start = 1, Text = "This will be on the exam." });
            page.DataContext = new LibraryModel { Note = live.FlagExam() };
            Dispatcher.UIThread.RunJobs();
            Assert.False(page.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "ExamFlags").IsEffectivelyVisible);
        }
        finally
        {
            w.Close();
        }
    }
}
