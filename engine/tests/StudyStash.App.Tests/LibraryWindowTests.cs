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

    static LibraryModel Lectures(out List<LectureCard> cards)
    {
        var m = new LibraryModel { ClassTitle = "CS 101", ClassCount = "3 lectures", UndoShows = TimeSpan.FromMilliseconds(50) };
        m.Classes.Add(new ClassItem { Name = "CS 101", Count = 3, Selected = true });
        var week = new LectureGroup { Label = "This week", First = true };
        var last = new LectureGroup { Label = "Last week" };
        cards =
        [
            new LectureCard { Id = "a", Title = "Recursion and the call stack", ClassName = "CS 101", Selected = true },
            new LectureCard { Id = "b", Title = "Stack frames and scope", ClassName = "CS 101" },
            new LectureCard { Id = "c", Title = "Loops and invariants", ClassName = "CS 101" },
        ];
        week.Items.Add(cards[0]);
        week.Items.Add(cards[1]);
        last.Items.Add(cards[2]);
        m.Groups.Add(week);
        m.Groups.Add(last);
        m.Note = new NoteModel { Id = "a", Title = "Recursion and the call stack", ClassName = "CS 101" };
        return m;
    }

    [Fact]
    public void A_lecture_filed_while_the_window_is_open_changes_the_sidebars_counts_and_nothing_else()
    {
        var m = Lectures(out var cards);
        m.Classes.Add(new ClassItem { Name = "BIO 110", Count = 1 });
        m.Classes.Add(new ClassItem { Name = "Due soon", IsDue = true, Count = 4 });
        m.Unsorted.Count = 0;

        m.ShowCounts([("CS 101", 4), ("BIO 110", 1), ("HIST 210", 7)], unsorted: 2);

        Assert.Equal([4, 1, 4], m.Classes.Select(c => c.Count)); // the due item isn't a class; one the sidebar doesn't list is skipped
        Assert.Equal(2, m.Unsorted.Count);
        Assert.True(m.Classes[0].Selected);
        Assert.Equal("3 lectures", m.ClassCount); // the page showing is the class page's own to redraw
        Assert.Equal(3, m.Groups.Sum(g => g.Items.Count));
    }

    [Fact]
    public async Task Deleting_asks_first_then_the_lecture_leaves_the_list_at_once_and_the_next_one_opens()
    {
        var m = Lectures(out var cards);
        var deleted = new List<LectureDeletion>();
        LectureCard? opened = null;
        m.OnDelete = d =>
        {
            deleted.Add(d);
            return Task.FromResult(true);
        };
        m.OnLecture = c => opened = c;

        // The header's Delete asks about the open lecture; Cancel leaves everything as it was.
        m.DeleteCommand.Execute(null);
        Assert.True(m.AskingDelete);
        Assert.Equal(new LectureDeletion("a", "Recursion and the call stack", "CS 101"), m.Deleting);
        m.CancelDeleteCommand.Execute(null);
        Assert.False(m.AskingDelete);
        Assert.Empty(deleted);
        Assert.Equal(3, m.Groups.Sum(g => g.Items.Count));

        m.DeleteCommand.Execute(null);
        await m.ConfirmDeleteCommand.ExecuteAsync(null);
        Assert.Equal(["a"], deleted.Select(d => d.Id));
        Assert.DoesNotContain(m.Groups.SelectMany(g => g.Items), c => c.Id == "a");
        Assert.Null(m.Note);
        Assert.Same(cards[1], opened); // the next one opens in its place
        Assert.Equal("2 lectures", m.ClassCount);
        Assert.Equal(2, m.Classes[0].Count);
        Assert.True(m.HasDeleted);
        Assert.Equal("Deleted “Recursion and the call stack”", m.Deleted!.Said);

        // "Deleted · Undo" goes by itself after a few seconds.
        await Task.Delay(400, TestContext.Current.CancellationToken);
        Assert.False(m.HasDeleted);

        // A row's own Delete (its context menu), the last in its group: the group goes too.
        opened = null;
        m.DeleteCommand.Execute(cards[2]);
        await m.ConfirmDeleteCommand.ExecuteAsync(null);
        Assert.Equal(["a", "c"], deleted.Select(d => d.Id));
        Assert.Single(m.Groups);
        Assert.Null(opened); // it wasn't the open one
        Assert.Equal("1 lecture", m.ClassCount);
    }

    [Fact]
    public async Task Undo_brings_the_lecture_back_through_the_library_and_a_refusal_puts_the_list_back()
    {
        var m = Lectures(out _);
        m.UndoShows = TimeSpan.FromMinutes(1);
        var undone = new List<string>();
        m.OnDelete = _ => Task.FromResult(true);
        m.OnUndo = d =>
        {
            undone.Add(d.Id);
            return Task.CompletedTask;
        };
        m.DeleteCommand.Execute(null);
        await m.ConfirmDeleteCommand.ExecuteAsync(null);
        await m.UndoCommand.ExecuteAsync(null);
        Assert.Equal(["a"], undone);
        Assert.False(m.HasDeleted);

        // The library refuses: no "Deleted · Undo" (the host lists the class again and says why).
        m.OnDelete = _ => Task.FromResult(false);
        m.DeleteCommand.Execute(m.Groups[0].Items[0]);
        await m.ConfirmDeleteCommand.ExecuteAsync(null);
        Assert.False(m.HasDeleted);
    }

    [AvaloniaTheory]
    [InlineData(SkinKind.Mac)]
    [InlineData(SkinKind.Win)]
    public async Task A_lecture_is_deleted_from_its_rows_menu_or_its_header_with_a_confirmation_in_the_window(SkinKind skin)
    {
        var m = Lectures(out var cards);
        var deleted = new List<string>();
        m.OnDelete = d =>
        {
            deleted.Add(d.Id);
            return Task.FromResult(true);
        };
        m.UndoShows = TimeSpan.FromMinutes(1);
        var view = Library(skin, m);
        var w = Host(view, skin);
        try
        {
            T Named<T>(string name) where T : Control => view.GetVisualDescendants().OfType<T>().First(c => c.Name == name);

            // The header's Delete, for the open lecture.
            var header = Named<Button>("DeleteLecture");
            Assert.True(header.IsEffectivelyVisible);
            Assert.True(header.IsEffectivelyEnabled);
            Assert.False(Named<Panel>("DeleteAsk").IsVisible);
            header.Command!.Execute(header.CommandParameter);
            Dispatcher.UIThread.RunJobs();
            var ask = Named<Panel>("DeleteAsk");
            Assert.True(ask.IsEffectivelyVisible);
            Assert.Contains(ask.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Delete this lecture?");
            Assert.Contains(ask.GetVisualDescendants().OfType<TextBlock>(), t => t.Text?.Contains("“Recursion and the call stack”", StringComparison.Ordinal) == true);
            var cancel = ask.GetVisualDescendants().OfType<Button>().First(b => b.Name == "Cancel");
            var confirm = ask.GetVisualDescendants().OfType<Button>().First(b => b.Name == "Confirm");
            Assert.Equal(cancel.Bounds.Height, confirm.Bounds.Height);
            Assert.Equal(cancel.TranslatePoint(default, view)!.Value.Y, confirm.TranslatePoint(default, view)!.Value.Y, 0.5);
            await Assert.IsAssignableFrom<CommunityToolkit.Mvvm.Input.IAsyncRelayCommand>(confirm.Command).ExecuteAsync(null);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(["a"], deleted);
            Assert.False(Named<Panel>("DeleteAsk").IsVisible);
            Assert.DoesNotContain(view.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Recursion and the call stack" && t.IsEffectivelyVisible);

            // "Deleted · Undo" under the list.
            var toast = Named<Border>("UndoToast");
            Assert.True(toast.IsEffectivelyVisible);
            Assert.Contains(toast.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Undo");

            // A row's context menu offers Delete too.
            var row = view.GetVisualDescendants().OfType<Button>().First(b => b.DataContext == cards[2] && b.Classes.Contains("card"));
            var item = Assert.Single(row.ContextMenu!.Items.OfType<MenuItem>());
            Assert.Equal("Delete lecture…", item.Header);
            row.ContextMenu.Open(row);
            Dispatcher.UIThread.RunJobs();
            item.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
            Assert.Equal(new LectureDeletion("c", "Loops and invariants", "CS 101"), m.Deleting);
            row.ContextMenu.Close();

            // A click beside the question is Cancel.
            Dispatcher.UIThread.RunJobs();
            var scrim = Named<Panel>("DeleteAsk");
            var top = TopLevel.GetTopLevel(scrim)!;
            var corner = scrim.TranslatePoint(new Point(10, 10), top)!.Value;
            Avalonia.Headless.HeadlessWindowExtensions.MouseDown(top, corner, Avalonia.Input.MouseButton.Left);
            Avalonia.Headless.HeadlessWindowExtensions.MouseUp(top, corner, Avalonia.Input.MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.False(m.AskingDelete);
        }
        finally
        {
            w.Close();
        }
    }
}
