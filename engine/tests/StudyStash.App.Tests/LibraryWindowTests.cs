using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StudyStash.App.ViewModels;
using StudyStash.App.Views;
using StudyStash.Core.Ai;

namespace StudyStash.App.Tests;

/// <summary>The library window as the student uses it: Unsorted always in the sidebar, deleting a lecture, and an
/// answer that has its own room above the ask bar.</summary>
public class LibraryWindowTests
{
    static Window Host(Control content, SkinKind skin)
    {
        ((App)Application.Current!).UseSkin(skin);
        var w = new Window { Width = 1280, Height = 800, RequestedThemeVariant = ThemeVariant.Light, Content = content };
        w.Show();
        Dispatcher.UIThread.RunJobs();
        return w;
    }

    static Control Library(SkinKind skin, LibraryModel m) => skin == SkinKind.Mac ? new MacLibrary { DataContext = m } : new WinLibrary { DataContext = m };

    static TextBlock Text(Control root, string text) =>
        root.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == text && t.IsEffectivelyVisible);

    [AvaloniaTheory]
    [InlineData(SkinKind.Mac)]
    [InlineData(SkinKind.Win)]
    public void Unsorted_is_always_in_the_sidebar_under_the_classes_even_empty(SkinKind skin)
    {
        var m = new LibraryModel();
        m.Classes.Add(new ClassItem { Name = "CS 101", Count = 3 });
        var view = Library(skin, m);
        var w = Host(view, skin);
        try
        {
            var unsorted = Text(view, "Unsorted");
            var cs = Text(view, "CS 101");
            Assert.True(unsorted.TranslatePoint(default, view)!.Value.Y > cs.TranslatePoint(default, view)!.Value.Y, "Unsorted sits under the classes");
            var row = unsorted.FindAncestorOfType<Button>()!;
            Assert.Contains(row.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "0");

            // Lectures the library couldn't place: their count shows, and one click opens them.
            m.Unsorted.Count = 2;
            Dispatcher.UIThread.RunJobs();
            Assert.Contains(row.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "2");
            ClassItem? picked = null;
            m.OnClass = c => picked = c;
            row.Command!.Execute(row.CommandParameter);
            Assert.Same(m.Unsorted, picked);
            Assert.True(picked!.IsUnsorted);
        }
        finally
        {
            w.Close();
        }
    }

    [Fact]
    public async Task An_answer_shows_until_closed_and_the_next_question_opens_it_again()
    {
        var ai = new FakeAiLibrary { Overview = AiDemo.Overview(), OnAsk = r => new AskReply("Recursion traces.", [], "claude", "Claude Code") };
        var ask = new AiAskModel(ai) { LectureId = "l1" };
        await ask.Load();
        var m = new LibraryModel { Note = new NoteModel { Id = "l1", Title = "Recursion" }, Ask = ask };
        Assert.False(m.HasAnswer);

        var said = new List<string?>();
        m.PropertyChanged += (_, e) => said.Add(e.PropertyName);
        ask.Question = "What's on the midterm?";
        await ask.AskCommand.ExecuteAsync(null);
        Assert.True(m.HasAnswer);
        Assert.Contains(nameof(LibraryModel.HasAnswer), said);

        ask.CloseAnswerCommand.Execute(null);
        Assert.False(ask.HasLatest);
        Assert.False(m.HasAnswer);

        ask.Question = "And the final?";
        await ask.AskCommand.ExecuteAsync(null);
        Assert.True(m.HasAnswer);
        Assert.Equal("And the final?", ask.Latest!.Question);

        // An assignment over the lecture takes its ask bar, and the answer with it.
        m.Assignment = new AssignmentModel(CanvasFixtures.Context());
        Assert.False(m.HasAnswer);
    }

    [AvaloniaTheory]
    [InlineData(SkinKind.Mac)]
    [InlineData(SkinKind.Win)]
    public void A_long_answer_has_its_own_solid_room_above_the_ask_bar_and_scrolls_inside(SkinKind skin)
    {
        var m = Demo.Library(answered: true);
        m.Ask!.Latest!.Answer = string.Join(" ", Enumerable.Repeat(m.Ask.Latest.Answer, 4));
        var view = Library(skin, m);
        var w = Host(view, skin);
        try
        {
            Rect Box(Control c) => new(c.TranslatePoint(default, view)!.Value, c.Bounds.Size);
            var notes = view.GetVisualDescendants().OfType<ScrollViewer>().First(s => s.Name == "NoteScroll");
            var card = view.GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("ai-answer-card"));
            var bar = view.GetVisualDescendants().OfType<UserControl>().First(u => u is MacAiAskBar or WinAiAskBar);

            // The notes end above the answer, and the answer above the bar: nothing readable lies under it.
            Assert.True(Box(notes).Bottom <= Box(card).Top + 0.5, $"notes {Box(notes)} run under the answer {Box(card)}");
            Assert.True(Box(card).Bottom <= Box(bar).Top + 0.5, $"answer {Box(card)} runs under the bar {Box(bar)}");
            Assert.True(Box(notes).Height > 200, "the notes keep room to read");

            // A solid card, not glass: the notes could never show through it.
            var fill = Assert.IsAssignableFrom<ISolidColorBrush>(card.Background);
            Assert.Equal(255, fill.Color.A);

            // A long answer scrolls inside its card.
            var inside = card.GetVisualDescendants().OfType<ScrollViewer>().Single();
            Assert.True(inside.Extent.Height > inside.Viewport.Height + 1, "the long answer should scroll inside the card");

            // Close gives the notes their room back.
            double before = Box(notes).Height;
            var close = card.GetVisualDescendants().OfType<Button>().First(b => b.Classes.Contains("ai-close"));
            close.Command!.Execute(close.CommandParameter);
            Dispatcher.UIThread.RunJobs();
            Assert.False(m.HasAnswer);
            Assert.True(Box(notes).Height > before + 100, "the notes grow back once the answer is closed");
        }
        finally
        {
            w.Close();
        }
    }
}
