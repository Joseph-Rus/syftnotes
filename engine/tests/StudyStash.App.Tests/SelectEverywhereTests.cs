using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StudyStash.App.Controls;
using StudyStash.App.Services;
using StudyStash.App.ViewModels;
using StudyStash.App.Views;

namespace StudyStash.App.Tests;

/// <summary>Text a student would copy, beyond a lecture's notes, selected and copied the way the notes are: a
/// transcript from line to line (each line copied with its time), a chat from a question on into its answer, an error
/// on its own. Buttons and links among them still take their clicks.</summary>
public class SelectEverywhereTests
{
    static Window Show(Control content, SkinKind skin, double width = 760, double height = 900)
    {
        Skin.UseTheme(ColourThemes.Default);
        ((App)Application.Current!).UseSkin(skin);
        var window = new Window { Width = width, Height = height, RequestedThemeVariant = ThemeVariant.Light, Content = content };
        window.Show();
        DiagramsReady.Wait(window);
        window.UpdateLayout();
        return window;
    }

    static string Words(NoteText t) => t.Inlines is { Count: > 0 } inlines ? inlines.Text ?? "" : t.Text ?? "";

    static NoteText Piece(Control root, string starting) =>
        root.GetVisualDescendants().OfType<NoteText>().First(t => t.IsEffectivelyVisible && Words(t).StartsWith(starting, StringComparison.Ordinal));

    /// <summary>Where in the window the character <paramref name="index"/> of <paramref name="text"/> begins, half way
    /// down its line.</summary>
    static Point At(NoteText text, int index)
    {
        var box = text.TextLayout.HitTestTextPosition(index);
        return text.TranslatePoint(new Point(box.X + 1 + text.Padding.Left, box.Y + box.Height / 2 + text.Padding.Top), TopLevel.GetTopLevel(text)!)!.Value;
    }

    static Point At(NoteText text, string word) => At(text, Words(text).IndexOf(word, StringComparison.Ordinal));

    static void Drag(Window window, Point from, Point to)
    {
        window.MouseMove(from);
        window.MouseDown(from, MouseButton.Left);
        window.MouseMove(new Point((from.X + to.X) / 2, (from.Y + to.Y) / 2), RawInputModifiers.LeftMouseButton);
        window.MouseMove(to, RawInputModifiers.LeftMouseButton);
        window.MouseUp(to, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    static void Click(Window window, Point at)
    {
        window.MouseMove(at);
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    static void Press(Window window, KeyGesture gesture)
    {
        window.KeyPress(gesture.Key, (RawInputModifiers)gesture.KeyModifiers, PhysicalKey.None, null);
        Dispatcher.UIThread.RunJobs();
    }

    static Point Middle(Control c) => c.TranslatePoint(new Point(c.Bounds.Width / 2, c.Bounds.Height / 2), TopLevel.GetTopLevel(c)!)!.Value;

    /// <summary>What's selected now in the selection that <paramref name="part"/> belongs to.</summary>
    static string Selected(Control part)
    {
        foreach (var c in part.GetSelfAndVisualAncestors().OfType<Control>().Reverse())
            if (TextSelection.Of(c) is { } selection && selection.SelectedText is { Length: > 0 } text) return text;
        return "";
    }

    static async Task<string?> Copied(Window window)
    {
        Press(window, Application.Current!.PlatformSettings!.HotkeyConfiguration.Copy[0]);
        return await window.Clipboard!.TryGetTextAsync();
    }

    static LibraryModel Lecture()
    {
        var note = new NoteModel { Id = "lec-recursion", Title = "Recursion and the call stack", ShowTranscript = true };
        note.Transcript.Add(new HeardLine { Time = "18:05", Start = 1085, Text = "Okay, the midterm. Recursion traces will be on it, the stack diagrams from last week." });
        note.Transcript.Add(new HeardLine { Time = "19:30", Start = 1170, Text = "Think of each call as a plate on a stack. You can only take the top one off." });
        note.Transcript.Add(new HeardLine { Time = "21:52", Start = 1312, Text = "When factorial of three calls factorial of two, the first call is paused, waiting." });
        return new LibraryModel { Note = note };
    }

    [AvaloniaTheory]
    [InlineData(SkinKind.Mac)]
    [InlineData(SkinKind.Win)]
    public async Task A_drag_selects_down_a_transcript_and_copy_keeps_each_lines_time(SkinKind skin)
    {
        var page = new NotePage { DataContext = Lecture() };
        var window = Show(page, skin);
        try
        {
            var first = Piece(page, "Okay, the midterm");
            var third = Piece(page, "When factorial");

            // From the middle of one line down into the third: the lines between whole, each taken from its start with
            // its time, as a downloaded transcript writes it.
            Drag(window, At(first, "Recursion"), At(third, " of two"));
            Assert.Equal("Recursion traces will be on it, the stack diagrams from last week.\n[19:30] Think of each call as a plate on a stack. "
                + "You can only take the top one off.\n[21:52] When factorial of three calls factorial", Selected(first));
            Assert.True(Piece(page, "Think of each").SelectionEnd > 0);
            Assert.Equal(Selected(first), await Copied(window));

            // A triple click takes one line, its time with it.
            var second = Piece(page, "Think of each");
            var on = At(second, "plate");
            window.MouseDown(on, MouseButton.Left);
            window.MouseUp(on, MouseButton.Left);
            window.MouseDown(on, MouseButton.Left);
            window.MouseUp(on, MouseButton.Left);
            window.MouseDown(on, MouseButton.Left);
            window.MouseUp(on, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("[19:30] Think of each call as a plate on a stack. You can only take the top one off.", Selected(second));

            // A line's time is still a button that plays the recording from there, and pressing it isn't a selection.
            int played = 0;
            var time = page.GetVisualDescendants().OfType<Button>().First(b => b.Classes.Contains("heard-time") && Equals(b.Content, "21:52"));
            time.Click += (_, _) => played++;
            Click(window, Middle(time));
            Assert.Equal(1, played);
            Assert.Equal("", Selected(first));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(SkinKind.Mac)]
    [InlineData(SkinKind.Win)]
    public async Task A_drag_runs_from_a_chat_question_on_into_its_answer(SkinKind skin)
    {
        var ask = AiDemo.Chat();
        ask.Turns.Add(new AiTurn("Which proofs are left off?", "Ollama") { Failed = "Ollama stopped before it answered: the model isn't loaded." });
        Control chat = skin == SkinKind.Mac ? new MacAiAskChat { DataContext = ask } : new WinAiAskChat { DataContext = ask };
        var window = Show(chat, skin, 420, 640);
        try
        {
            var question = Piece(chat, "What did she say");
            var answer = Piece(chat, "Recursion traces");
            Drag(window, At(question, "say"), At(answer, " and call-stack"));
            Assert.Equal("say is on the midterm?\nRecursion traces", Selected(question));
            Assert.Equal(Selected(question), await Copied(window));

            // Copy in the right-click menu over the answer copies the whole selection, the question's part too.
            await window.Clipboard!.SetTextAsync("");
            var menu = answer.GetVisualAncestors().OfType<NoteView>().First().ContextMenu!;
            menu.Items.OfType<MenuItem>().First(i => Equals(i.Header, "Copy")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("say is on the midterm?\nRecursion traces", await window.Clipboard!.TryGetTextAsync());

            // ⌘A (Ctrl+A) takes the whole conversation, a failed answer's reason too.
            Press(window, Application.Current!.PlatformSettings!.HotkeyConfiguration.SelectAll[0]);
            var all = Selected(question);
            Assert.StartsWith("What did she say is on the midterm?\nRecursion traces and call-stack diagrams.", all);
            Assert.EndsWith("Which proofs are left off?\nOllama stopped before it answered: the model isn't loaded.", all);

            // A click into the field ends the selection (the field's is the one now), and what was copied pastes
            // there whole, its lines as one: the field takes no Return, and kept only the first line.
            var field = chat.GetVisualDescendants().OfType<TextBox>().First();
            Click(window, Middle(field));
            Assert.True(field.IsFocused);
            Assert.Equal("", Selected(question));
            Press(window, Application.Current!.PlatformSettings!.HotkeyConfiguration.Paste[0]);
            Assert.Equal("say is on the midterm? Recursion traces", field.Text);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(SkinKind.Mac)]
    [InlineData(SkinKind.Win)]
    public void A_drag_on_the_recorders_words_selects_them_and_its_background_still_moves_it(SkinKind skin)
    {
        var model = Demo.Recorder(expanded: true);
        Control view = skin == SkinKind.Mac ? new MacRecorder { DataContext = model } : new WinRecorder { DataContext = model };
        // What the recorder's window moves by (Shell.MakeRecorderWindow): a press that reaches the view itself.
        int grabbed = 0;
        view.PointerPressed += (_, _) => grabbed++;
        var window = Show(view, skin, 420, 600);
        try
        {
            var first = Piece(view, "Okay, the midterm");
            var second = Piece(view, "Think of each");
            Drag(window, At(first, "Recursion"), At(second, " a plate"));
            Assert.Equal("Recursion traces will be on it, the stack diagrams from last week.\n[19:30] Think of each call as", Selected(first));
            Assert.Equal(0, grabbed);

            // The room between two lines, and the recorder's top, still take hold of the window.
            var gap = first.TranslatePoint(new Point(first.Bounds.Width / 2, first.Bounds.Height + 6), window)!.Value;
            Click(window, gap);
            Assert.Equal(1, grabbed);
            var name = view.GetVisualDescendants().OfType<TextBlock>().First(t => t.IsEffectivelyVisible && t.Text == model.DisplayClass);
            Click(window, Middle(name));
            Assert.Equal(2, grabbed);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(SkinKind.Mac)]
    [InlineData(SkinKind.Win)]
    public void An_assignment_selects_as_one_page_from_its_title_through_its_instructions(SkinKind skin)
    {
        var model = new AssignmentModel(CanvasFixtures.Context());
        model.Show(CanvasFixtures.Load<CanvasApi.AssignmentDetail>("assignment-9001"));
        Control view = skin == SkinKind.Mac ? new MacAssignment { DataContext = model } : new WinAssignment { DataContext = model };
        var window = Show(view, skin, 760, 1000);
        try
        {
            var title = Piece(view, "Lab 3");
            var instructions = Piece(view, "Trace factorial");
            Drag(window, At(title, "recursion"), At(instructions, " and fib"));
            // Each tile's value after its name; the instructions, a lecture's notes of their own, run on from the rest.
            Assert.Equal("recursion traces\nDue: Tue 30 Sep, 11:59 PM\nPoints: 20\nStatus: To do\nInstructions\nTrace factorial(4)", Selected(title));
            Assert.True(title.SelectionStart > 0 && instructions.SelectionEnd > 0, "both ends are drawn selected");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task A_line_on_its_own_selects_and_copies_by_itself()
    {
        var error = new PageText { Text = "Canvas said 401 Unauthorized for school.instructure.com.", TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        var other = new PageText { Text = "Another line, on its own too." };
        var panel = new StackPanel { Spacing = 20, Margin = new Thickness(20), Children = { error, other, new Button { Content = "Try again" } } };
        var window = Show(panel, SkinKind.Mac, 480, 300);
        try
        {
            // A double click takes a word, ⌘C copies it.
            var on = At(error, "Unauthorized");
            window.MouseDown(on, MouseButton.Left);
            window.MouseUp(on, MouseButton.Left);
            window.MouseDown(on, MouseButton.Left);
            window.MouseUp(on, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Unauthorized", Selected(error));
            Assert.Equal("Unauthorized", await Copied(window));

            // A press on the button leaves it selected; selecting the other line ends it, one selection to a window.
            Click(window, Middle(panel.Children[2]));
            Assert.Equal("Unauthorized", Selected(error));
            Drag(window, At(other, 0), At(other, "line"));
            Assert.Equal("Another ", Selected(other));
            Assert.Equal("", Selected(error));
            Assert.Equal(0, error.SelectionEnd);
        }
        finally
        {
            window.Close();
        }
    }
}
