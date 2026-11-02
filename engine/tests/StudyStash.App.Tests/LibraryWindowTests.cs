using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StudyStash.App.Controls;
using StudyStash.App.ViewModels;
using StudyStash.App.Views;
using StudyStash.Core.Ai;

namespace StudyStash.App.Tests;

/// <summary>The library window as the student uses it: one sidebar (no Unsorted in it, folders under Classes, Clubs and
/// Work, what's coming up as buttons that record), deleting a note, and the conversation that opens beside the notes.</summary>
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
    public void The_one_sidebar_groups_folders_has_no_Unsorted_and_records_what_is_coming_up(SkinKind skin)
    {
        var m = Demo.Semester();
        string? recorded = null;
        m.ComingUp.OnRecord = id => recorded = id;
        var view = Library(skin, m);
        var w = Host(view, skin);
        try
        {
            var side = view.GetVisualDescendants().OfType<SidebarBody>().Single();
            string[] words = [.. side.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text ?? "")];
            // Unsorted isn't a place in the sidebar any more: notes in no folder are on Home.
            Assert.DoesNotContain("Unsorted", words);
            double Top(string text) => Text(side, text).TranslatePoint(default, view)!.Value.Y;
            // Home and Due, then the folders under their groups, then what's coming up, then the library's two lines.
            Assert.True(Top("Home") < Top("Due") && Top("Due") < Top("Classes") && Top("Classes") < Top("Machine Learning"));
            Assert.True(Top("Theory of Computation") < Top("Clubs") && Top("Clubs") < Top("ACM") && Top("ACM") < Top("Work") && Top("Work") < Top("NSWC"));
            Assert.True(Top("NSWC") < Top("Coming up") && Top("Coming up") < Top("Library on this Mac"));

            // Coming up: the next two events, each one button (no Record beside it) that says what a click does.
            var coming = side.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("coming") && b.DataContext is ComingUpRow).ToList();
            Assert.Equal(2, coming.Count);
            Assert.Equal("Record Capstone team sync, starts in 11 minutes", Avalonia.Automation.AutomationProperties.GetName(coming[0]));
            Assert.Equal("Record Officer meeting, today at 6:00 PM", Avalonia.Automation.AutomationProperties.GetName(coming[1]));
            Assert.Equal("Click to record", ToolTip.GetTip(coming[0]));
            Assert.Contains("soon", coming[0].Classes);
            Assert.DoesNotContain("soon", coming[1].Classes);
            Assert.Contains(coming[0].GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "In 11 minutes");
            coming[1].Command!.Execute(coming[1].CommandParameter);
            Assert.Equal(m.ComingUp.Next[1].Id, recorded);

            // A folder opens its page; there is no second column of lectures anywhere.
            ClassItem? picked = null;
            m.OnClass = c => picked = c;
            var ml = Text(side, "Machine Learning").FindAncestorOfType<Button>()!;
            // A screen reader says each row's name (and its count), not what the row is built from.
            Assert.Equal("Home", Avalonia.Automation.AutomationProperties.GetName(Text(side, "Home").FindAncestorOfType<Button>()!));
            Assert.Equal(m.Due!.Said, Avalonia.Automation.AutomationProperties.GetName(Text(side, "Due").FindAncestorOfType<Button>()!));
            Assert.Equal(m.Classes.First(c => c.Name == "Machine Learning").Said, Avalonia.Automation.AutomationProperties.GetName(ml));
            Assert.StartsWith("Machine Learning", Avalonia.Automation.AutomationProperties.GetName(ml));
            ml.Command!.Execute(ml.CommandParameter);
            Assert.Equal("Machine Learning", picked!.Name);
            Assert.Equal(Nouns.Lecture, picked.Noun);
            Assert.Equal(Nouns.Meeting, m.Classes.First(c => c.Name == "ACM").Noun);
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
    public void The_conversation_opens_beside_the_notes_and_Close_brings_the_bar_back(SkinKind skin)
    {
        Platform.Motion.Override = true; // no glide: each change lands at once
        var m = Demo.Library(answered: true);
        m.Ask!.Latest!.Answer = string.Join(" ", Enumerable.Repeat(m.Ask.Latest.Answer, 6));
        var view = Library(skin, m);
        var w = Host(view, skin);
        try
        {
            w.Width = 1180;
            Dispatcher.UIThread.RunJobs();
            w.UpdateLayout();
            Rect Box(Control c) => new(c.TranslatePoint(default, view)!.Value, c.Bounds.Size);
            var split = view.GetVisualDescendants().OfType<AskSplit>().Single(s => s.IsEffectivelyVisible);
            var column = view.GetVisualDescendants().OfType<StackPanel>().First(s => s.Name == "Column" && s.IsEffectivelyVisible);
            var thread = view.GetVisualDescendants().OfType<ScrollViewer>().First(s => s.Name == "Thread");
            var bar = view.GetVisualDescendants().OfType<UserControl>().First(u => u is MacAiAskBar or WinAiAskBar);
            var answer = thread.GetVisualDescendants().OfType<NoteView>().First(n => n.IsEffectivelyVisible);

            // Split: the notes on the left, the conversation down the right, nothing of one under the other.
            Assert.True(split.Beside);
            var panel = new Rect(Box(split).Right - AskSplit.PanelWidth(split.Bounds.Width), Box(split).Top, AskSplit.PanelWidth(split.Bounds.Width), split.Bounds.Height);
            Assert.False(Box(column).Intersects(panel), $"the notes {Box(column)} run under the panel {panel}");
            Assert.True(Box(column).Width >= 420, $"the notes keep a column to read: {Box(column).Width}");
            // The bar is docked at the panel's foot, and the thread ends above it.
            Assert.True(panel.Contains(Box(bar)), $"the bar {Box(bar)} isn't in the panel {panel}");
            Assert.True(Box(thread).Bottom <= Box(bar).Top + 0.5, $"the thread {Box(thread)} runs under the bar {Box(bar)}");
            // An answer has the panel's width, and a long one scrolls in the panel.
            Assert.True(Box(answer).Width >= panel.Width - 64, $"the answer is {Box(answer).Width} in a panel {panel.Width} wide");
            Assert.True(thread.Extent.Height > thread.Viewport.Height + 1, "the long answer should scroll inside the panel");

            // Close: the notes get the page back, and the bar is at its foot in the middle again.
            var close = view.GetVisualDescendants().OfType<Button>().First(b => b.Name == "Close" && b.IsEffectivelyVisible);
            close.Command!.Execute(close.CommandParameter);
            Dispatcher.UIThread.RunJobs();
            w.UpdateLayout();
            Assert.False(m.HasAnswer);
            Assert.False(split.Beside);
            Assert.False(thread.IsEffectivelyVisible);
            Assert.True(Math.Abs(Box(bar).Center.X - Box(split).Center.X) < 1, $"the bar {Box(bar)} is back in the middle of {Box(split)}");
            Assert.True(Box(bar).Bottom > Box(split).Bottom - 40);

            // Open again, then Esc in the bar closes it too.
            m.Ask.AnswerClosed = false;
            Dispatcher.UIThread.RunJobs();
            w.UpdateLayout();
            Assert.True(split.Beside);
            var field = bar.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "AskInput");
            field.Focus();
            w.KeyPress(Avalonia.Input.Key.Escape, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.Escape, null);
            Dispatcher.UIThread.RunJobs();
            Assert.False(m.HasAnswer);
            Assert.False(split.Beside);
        }
        finally
        {
            w.Close();
            Platform.Motion.Override = null;
        }
    }

    [AvaloniaTheory]
    [InlineData(SkinKind.Mac)]
    [InlineData(SkinKind.Win)]
    public void In_a_narrow_window_the_conversation_covers_the_notes_with_a_way_back(SkinKind skin)
    {
        Platform.Motion.Override = true;
        var m = Demo.Library(answered: true);
        var view = Library(skin, m);
        var w = Host(view, skin);
        try
        {
            w.Width = 900; // with the sidebar, under AskSplit.SheetBelow for the page area
            Dispatcher.UIThread.RunJobs();
            w.UpdateLayout();
            Rect Box(Control c) => new(c.TranslatePoint(default, view)!.Value, c.Bounds.Size);
            var split = view.GetVisualDescendants().OfType<AskSplit>().Single(s => s.IsEffectivelyVisible);
            var thread = view.GetVisualDescendants().OfType<ScrollViewer>().First(s => s.Name == "Thread");
            Assert.True(split.Sheet);
            Assert.False(split.Beside);
            // The sheet has the whole page area: none of the notes shows beside it, half covered.
            var sheet = thread.GetVisualAncestors().OfType<Border>().First(b => b.Parent == split);
            Assert.Equal(Box(split), Box(sheet));
            var back = view.GetVisualDescendants().OfType<Button>().First(b => b.Name == "BackToPage");
            Assert.True(back.IsEffectivelyVisible);
            Assert.False(view.GetVisualDescendants().OfType<Button>().First(b => b.Name == "Close").IsEffectivelyVisible);
            back.Command!.Execute(back.CommandParameter);
            Dispatcher.UIThread.RunJobs();
            w.UpdateLayout();
            Assert.False(split.Sheet);
            Assert.False(sheet.IsVisible);
        }
        finally
        {
            w.Close();
            Platform.Motion.Override = null;
        }
    }

    static readonly TimeZoneInfo Zone = TimeZoneInfo.Utc;
    static readonly DateTimeOffset Now = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);

    /// <summary>CS 101's page, three lectures in two weeks, the first of them also open on its own page when
    /// <paramref name="open"/>.</summary>
    static LibraryModel Lectures(bool open = false)
    {
        var m = new LibraryModel { UndoShows = TimeSpan.FromMilliseconds(50), DeletedShows = TimeSpan.FromMilliseconds(50) };
        m.Classes.Add(new ClassItem { Name = "CS 101", Count = 3, Selected = true });
        OverviewModel.LectureFacts L(string id, string title, int daysAgo) => new(id, title, "CS 101", Now.AddDays(-daysAgo), 3000, "", false);
        var sources = new OverviewModel.Sources
        {
            Now = Now, Zone = Zone, Classes = [("CS 101", Brushes.Gray, 3)],
            Lectures = [L("a", "Recursion and the call stack", 0), L("b", "Stack frames and scope", 1), L("c", "Loops and invariants", 8)],
        };
        var page = OverviewModel.ForClass(new ClassHomeFacts("CS 101", Brushes.Gray, 3, null, null, null, null), sources);
        page.OnDelete = row => m.DeleteCommand.Execute(row);
        if (open) m.Note = new NoteModel { Id = "a", Title = "Recursion and the call stack", ClassName = "CS 101" }.Titled();
        else m.Overview = page;
        return m;
    }

    static List<NoteItem> Rows(LibraryModel m) => [.. m.Overview!.Groups.SelectMany(g => g.Notes)];

    [Fact]
    public void A_note_filed_while_the_window_is_open_changes_the_sidebars_counts_and_nothing_else()
    {
        var m = Lectures();
        m.Classes.Add(new ClassItem { Name = "BIO 110", Count = 1 });
        m.Classes.Add(new ClassItem { Name = "Due", IsDue = true, Count = 4 });

        m.ShowCounts([("CS 101", 4), ("BIO 110", 1), ("HIST 210", 7)], unsorted: 2);

        Assert.Equal([4, 1, 4], m.Classes.Select(c => c.Count)); // the due item isn't a folder; one the sidebar doesn't list is skipped
        Assert.Equal(2, m.UnsortedCount);
        Assert.True(m.Classes[0].Selected);
        Assert.Equal(3, Rows(m).Count); // the page showing is its own to redraw
    }

    [Fact]
    public async Task Deleting_asks_first_then_the_note_leaves_its_page_at_once()
    {
        var m = Lectures();
        var deleted = new List<LectureDeletion>();
        m.OnDelete = d =>
        {
            deleted.Add(d);
            return Task.FromResult(true);
        };
        var rows = Rows(m);

        // A row's Delete asks about that lecture; Cancel leaves everything as it was.
        m.DeleteCommand.Execute(rows[0]);
        Assert.True(m.AskingDelete);
        Assert.Equal(new LectureDeletion("a", "Recursion and the call stack", "CS 101"), m.Deleting);
        Assert.Equal("Delete this lecture?", m.Deleting!.Ask);
        m.CancelDeleteCommand.Execute(null);
        Assert.False(m.AskingDelete);
        Assert.Empty(deleted);
        Assert.Equal(3, Rows(m).Count);

        m.DeleteCommand.Execute(rows[0]);
        await m.ConfirmDeleteCommand.ExecuteAsync(null);
        Assert.Equal(["a"], deleted.Select(d => d.Id));
        Assert.DoesNotContain(Rows(m), n => n.Id == "a");
        Assert.True(Rows(m)[0].First); // the row that's first now has no hairline over it
        Assert.Equal(2, m.Classes[0].Count);
        Assert.True(m.HasDeleted && m.HasToast);
        Assert.Equal("Deleted “Recursion and the call stack”", m.ToastSaid);

        // "Deleted · Undo" goes by itself once its minutes are up.
        await Task.Delay(400, TestContext.Current.CancellationToken);
        Assert.False(m.HasToast);

        // The last one under its heading: the heading goes too.
        m.DeleteCommand.Execute(rows[2]);
        await m.ConfirmDeleteCommand.ExecuteAsync(null);
        Assert.Equal(["a", "c"], deleted.Select(d => d.Id));
        Assert.Single(m.Overview!.Groups);

        // From a note's own page (its toolbar): the page gives way to where the student came from.
        var on = Lectures(open: true);
        int left = 0;
        on.OnLeaveNote = () => left++;
        on.OnDelete = _ => Task.FromResult(true);
        on.DeleteCommand.Execute(null);
        Assert.Equal("a", on.Deleting!.Id);
        await on.ConfirmDeleteCommand.ExecuteAsync(null);
        Assert.Null(on.Note);
        Assert.Equal(1, left);
        // One that's still recording has no Delete: there's nothing in the library to delete yet.
        var live = new LibraryModel { Note = new NoteModel { Id = "rec-1", Live = true } };
        live.DeleteCommand.Execute(null);
        Assert.False(live.AskingDelete);
        Assert.False(live.ShowLectureTools);
    }

    [Fact]
    public async Task Undo_brings_the_note_back_through_the_library_and_a_refusal_says_nothing_was_deleted()
    {
        var m = Lectures();
        // The question says "You can undo it for a few minutes": Undo stays that long, and inside the time the library
        // keeps a deleted note in its trash.
        Assert.Contains("You can undo it for a few minutes.", new LectureDeletion("a", "Recursion", "CS 101").Warning);
        Assert.InRange(new LibraryModel().DeletedShows, TimeSpan.FromMinutes(3), StudyStash.Library.LibraryWeb.TrashKeeps - TimeSpan.FromMinutes(1));
        m.DeletedShows = TimeSpan.FromMinutes(1);
        var undone = new List<string>();
        m.OnDelete = _ => Task.FromResult(true);
        m.OnUndo = d =>
        {
            undone.Add(d.Id);
            return Task.CompletedTask;
        };
        m.DeleteCommand.Execute(Rows(m)[0]);
        await m.ConfirmDeleteCommand.ExecuteAsync(null);
        // A second one deleted, and a note moved, while the first can still be undone: neither takes its Undo away.
        m.DeleteCommand.Execute(Rows(m)[0]);
        await m.ConfirmDeleteCommand.ExecuteAsync(null);
        m.SayMoved(new NoteMove("c", "CS 101", ""));
        Assert.Equal("Moved to Unsorted.", m.ToastSaid);
        Assert.False(m.CanDismissToast);
        await Task.Delay(400, TestContext.Current.CancellationToken); // the move's own few seconds
        Assert.Equal("Deleted “Stack frames and scope”", m.ToastSaid);
        Assert.True(m.CanDismissToast);
        await m.UndoToastCommand.ExecuteAsync(null);
        Assert.Equal(["b"], undone);
        Assert.Equal("Deleted “Recursion and the call stack”", m.ToastSaid);
        // The toast's × puts it away.
        m.DismissDeletedCommand.Execute(null);
        Assert.False(m.HasToast);

        // The library refuses: no "Deleted · Undo" (the host reads the page again and says why).
        m.OnDelete = _ => Task.FromResult(false);
        m.DeleteCommand.Execute(Rows(m)[0]);
        await m.ConfirmDeleteCommand.ExecuteAsync(null);
        Assert.False(m.HasToast);
    }

    [Fact]
    public async Task A_note_deleted_from_its_own_page_is_out_of_Homes_list_at_once_and_back_on_Undo()
    {
        // Home as the library answers while "a" is still there: the page the note gives way to is asked for in the same
        // moment as the delete, so its answer lists the note still.
        OverviewModel Home(params string[] ids) => OverviewModel.Home(new OverviewModel.Sources
        {
            Now = Now, Zone = Zone, Classes = [("CS 101", Brushes.Gray, ids.Length)],
            Lectures = [.. ids.Select((id, n) => new OverviewModel.LectureFacts(id, $"Lecture {id}", "CS 101", Now.AddDays(-n), 3000, "", false))],
        });
        var m = Lectures(open: true);
        var library = new TaskCompletionSource<bool>();
        m.OnLeaveNote = () => m.Overview = Home("a", "b", "c");
        m.OnDelete = _ => library.Task;
        m.OnUndo = _ =>
        {
            m.Overview = Home("a", "b", "c");
            return Task.CompletedTask;
        };

        m.DeleteCommand.Execute(null);
        var deleting = m.ConfirmDeleteCommand.ExecuteAsync(null);
        Assert.Equal(["b", "c"], m.Overview!.Notes.Select(n => n.Id)); // gone before the library has answered
        Assert.True(m.Overview.Notes[0].First);
        library.SetResult(true);
        await deleting;
        // An answer that was on its way when the note went, arriving late.
        m.Overview = Home("a", "b", "c");
        Assert.Equal(["b", "c"], m.Overview!.Notes.Select(n => n.Id));

        await m.UndoToastCommand.ExecuteAsync(null);
        Assert.Equal(["a", "b", "c"], m.Overview!.Notes.Select(n => n.Id));
    }

    [Fact]
    public async Task A_moved_note_says_where_it_went_and_Undo_moves_it_back()
    {
        var m = new LibraryModel { UndoShows = TimeSpan.FromMinutes(1) };
        var back = new List<NoteMove>();
        m.OnUndoMove = move =>
        {
            back.Add(move);
            return Task.CompletedTask;
        };
        m.SayMoved(new NoteMove("a", "", "Machine Learning(CSCI4220.A)"));
        Assert.True(m.HasToast);
        Assert.Equal("Moved to Machine Learning.", m.ToastSaid);
        await m.UndoToastCommand.ExecuteAsync(null);
        Assert.Equal([new NoteMove("a", "", "Machine Learning(CSCI4220.A)")], back);
        Assert.False(m.HasToast);
        // Out of every folder: it says Unsorted.
        m.SayMoved(new NoteMove("a", "ACM", ""));
        Assert.Equal("Moved to Unsorted.", m.ToastSaid);
    }

    [AvaloniaTheory]
    [InlineData(SkinKind.Mac)]
    [InlineData(SkinKind.Win)]
    public async Task A_note_is_deleted_from_its_page_or_its_rows_menu_with_a_confirmation_in_the_window(SkinKind skin)
    {
        var m = Lectures(open: true);
        var deleted = new List<string>();
        m.OnDelete = d =>
        {
            deleted.Add(d.Id);
            return Task.FromResult(true);
        };
        m.DeletedShows = TimeSpan.FromMinutes(1);
        var view = Library(skin, m);
        var w = Host(view, skin);
        try
        {
            T Named<T>(string name) where T : Control => view.GetVisualDescendants().OfType<T>().First(c => c.Name == name);

            // The page's own tools, for the open note: Move to folder, Share, Delete.
            foreach (string tool in new[] { "Move", "Share", "DeleteLecture" }) Assert.True(Named<Button>(tool).IsEffectivelyVisible, tool);
            var delete = Named<Button>("DeleteLecture");
            Assert.False(Named<Panel>("DeleteAsk").IsVisible);
            delete.Command!.Execute(delete.CommandParameter);
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

            // "Deleted · Undo" at the page's foot.
            var toast = Named<Border>("UndoToast");
            Assert.True(toast.IsEffectivelyVisible);
            Assert.Contains(toast.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Undo");
            Assert.True(Named<Button>("DismissToast").IsEffectivelyVisible); // it stays for minutes, so it can be put away

            // On the folder's page, a row's context menu offers Delete too.
            var folder = Lectures();
            m.Overview = folder.Overview;
            m.Overview!.OnDelete = row => m.DeleteCommand.Execute(row);
            Dispatcher.UIThread.RunJobs();
            var row = view.GetVisualDescendants().OfType<Button>().First(b => b.Name == "Row" && b.DataContext is NoteItem { Id: "c" });
            var item = Assert.Single(row.ContextMenu!.Items.OfType<MenuItem>());
            Assert.Equal("Delete…", item.Header);
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
