using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StudyStash.App.Controls;
using StudyStash.App.ViewModels;
using StudyStash.App.Views;
using StudyStash.Core.Ai;

namespace StudyStash.App.Tests;

/// <summary>Ask remembers the conversation it's in: each question goes with the turns before it (the last few, cut
/// short), New chat starts afresh, an older library still answers and says once that follow-ups start fresh there, and
/// the answer card shows the whole conversation in a height of its own.</summary>
public class AskRemembersTests
{
    static async Task<(AiAskModel Model, FakeAiLibrary Library)> Loaded()
    {
        var lib = new FakeAiLibrary { Overview = AiTestData.MixedOverview() };
        var model = new AiAskModel(lib) { LectureId = "lec-vessels" };
        await model.Load();
        // As the library does now: every earlier turn it was sent went to the AI.
        lib.OnAsk = r => new AskReply($"Answer {lib.AskRequests.Count}: " + new string('x', 5000), [new AskSource("lec-vessels", "Vessels", null, null, 61, "")],
            r.Engine!, r.Engine == "ollama" ? "Ollama" : "Claude") { Remembered = r.History?.Count ?? 0 };
        return (model, lib);
    }

    static async Task Ask(AiAskModel model, string question)
    {
        model.Question = question;
        await model.AskCommand.ExecuteAsync(null);
    }

    [AvaloniaFact]
    public async Task A_follow_up_carries_the_conversation_cut_short_and_says_when_it_went_to_another_AI()
    {
        var (model, lib) = await Loaded();
        model.Engine = "ollama";
        for (int i = 1; i <= 8; i++) await Ask(model, $"Question {i}?");

        // The first question goes on its own; the second with the first, its answer and what it drew on.
        Assert.Null(lib.AskRequests[0].History);
        var first = Assert.Single(lib.AskRequests[1].History!);
        Assert.Equal("Question 1?", first.Question);
        Assert.StartsWith("Answer 1: xxx", first.Answer);
        Assert.Equal(61, Assert.Single(first.Sources).At);
        // Later ones with the last six, each cut to about 4,000 characters.
        var sent = lib.AskRequests[7].History!;
        Assert.Equal(["Question 2?", "Question 3?", "Question 4?", "Question 5?", "Question 6?", "Question 7?"], sent.Select(t => t.Question));
        Assert.All(sent, t => Assert.Equal(AskMemory.TurnChars, t.Question.Length + t.Answer.Length));
        // The free AI kept the chat on the computer: nothing to say.
        Assert.DoesNotContain(model.Turns, t => t.Byline!.Contains(AiWords.ReadChat, StringComparison.Ordinal));

        // Switched to Claude, the chat above goes to Claude with the question, and its byline says so.
        model.Engine = "claude";
        await Ask(model, "And the second one?");
        Assert.Equal(6, lib.AskRequests[8].History!.Count);
        Assert.Equal("Claude · from 01:01" + AiWords.ReadChat, model.Turns[^1].Byline);
        Assert.Equal(AiWords.ReadChat, model.Turns[^1].BylineParts[^1].Text);
    }

    [AvaloniaFact]
    public async Task New_chat_starts_afresh()
    {
        var (model, lib) = await Loaded();
        await Ask(model, "What are the three kinds of blood vessel?");
        await Ask(model, "And the second one?");
        Assert.True(model.NewChatCommand.CanExecute(null));
        bool started = false;
        model.Started = () => started = true;

        model.NewChatCommand.Execute(null);

        Assert.Empty(model.Turns);
        Assert.False(model.HasLatest);
        Assert.False(model.NewChatCommand.CanExecute(null));
        Assert.True(started);
        await Ask(model, "What's on the midterm?");
        Assert.Null(lib.AskRequests[^1].History);
        Assert.Single(model.Turns);
    }

    [AvaloniaFact]
    public async Task An_older_library_still_answers_and_says_once_that_follow_ups_start_fresh()
    {
        var (model, lib) = await Loaded();
        // A library from before: it reads the question, never the turns, and its reply doesn't say what it remembered.
        lib.OnAsk = r => new AskReply($"Answer to {r.Question}", [], "claude", "Claude");

        await Ask(model, "What are the three kinds of blood vessel?");
        await Ask(model, "And the second one?");
        await Ask(model, "Explain that more simply");

        Assert.Equal(["Answer to What are the three kinds of blood vessel?", "Answer to And the second one?", "Answer to Explain that more simply"],
            model.Turns.Select(t => t.Answer));
        Assert.Equal([null, AiWords.ForgetsOnOlderLibrary, null], model.Turns.Select(t => t.MemoryNote));
        Assert.Equal("Claude", model.Turns[1].Byline);
    }

    static Window Show(Control content, SkinKind skin, double height)
    {
        Skin.UseTheme(ColourThemes.Default);
        ((App)Application.Current!).UseSkin(skin);
        var window = new Window { Width = 760, Height = height, RequestedThemeVariant = ThemeVariant.Light, Content = content };
        window.Show();
        DiagramsReady.Wait(window);
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    static string Words(NoteText t) => t.Inlines is { Count: > 0 } inlines ? inlines.Text ?? "" : t.Text ?? "";

    static Point At(Control root, string words, string at)
    {
        var text = root.GetVisualDescendants().OfType<NoteText>().First(t => t.IsEffectivelyVisible && Words(t).StartsWith(words, StringComparison.Ordinal));
        var box = text.TextLayout.HitTestTextPosition(Words(text).IndexOf(at, StringComparison.Ordinal));
        return text.TranslatePoint(new Point(box.X + 1 + text.Padding.Left, box.Y + box.Height / 2 + text.Padding.Top), TopLevel.GetTopLevel(text)!)!.Value;
    }

    [AvaloniaTheory]
    [InlineData(SkinKind.Mac)]
    [InlineData(SkinKind.Win)]
    public void The_answer_cards_conversation_is_capped_in_height_and_selects_from_a_question_into_its_answer(SkinKind skin)
    {
        var model = new AiAskModel(new FakeAiLibrary { Overview = AiTestData.MixedOverview() });
        for (int i = 1; i <= 6; i++)
            model.Turns.Add(new AiTurn($"Question {i}: what does the lecture say about vessel {i}?", "Claude")
            {
                Answer = $"Answer {i}. " + string.Concat(Enumerable.Repeat("Capillaries are one cell thick, so gases cross them easily. ", 6)),
                Byline = "Claude",
            });

        foreach (double height in new[] { 900.0, 560.0 })
        {
            // As the page places it: just above the ask bar, as tall as it needs.
            Control placed = skin == SkinKind.Mac ? new MacAiAnswer { DataContext = model } : new WinAiAnswer { DataContext = model };
            placed.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom;
            var window = Show(placed, skin, height);
            try
            {
                var card = placed.GetVisualDescendants().OfType<Border>().First(b => b.Name == "Card");
                var thread = card.GetVisualDescendants().OfType<ScrollViewer>().Single(s => s.Name == "Thread");
                // Every turn is in it, and it scrolls within its own height, a third of the window at most, the newest showing.
                Assert.Equal(6, card.GetVisualDescendants().OfType<NoteView>().Count());
                Assert.Equal(AnswerHeight.For(height), thread.MaxHeight);
                Assert.True(thread.Bounds.Height <= AnswerHeight.For(height) + 0.5, $"{thread.Bounds.Height} in a window {height} tall");
                Assert.True(card.Bounds.Height <= AnswerHeight.For(height) + 48, $"the card is {card.Bounds.Height}");
                Assert.True(thread.Extent.Height > thread.Viewport.Height);
                Assert.True(thread.Offset.Y > 0);
            }
            finally
            {
                window.Close();
            }
        }

        // One selection runs from a question on into its answer.
        Control view = skin == SkinKind.Mac ? new MacAiAnswer { DataContext = model } : new WinAiAnswer { DataContext = model };
        var w = Show(view, skin, 900);
        try
        {
            var from = At(view, "Question 6:", "vessel 6");
            w.MouseMove(from);
            w.MouseDown(from, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            var to = At(view, "Answer 6.", "are one"); // (where it is now the press has settled the thread)
            w.MouseMove(new Point((from.X + to.X) / 2, (from.Y + to.Y) / 2), RawInputModifiers.LeftMouseButton);
            w.MouseMove(to, RawInputModifiers.LeftMouseButton);
            w.MouseUp(to, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            var question = view.GetVisualDescendants().OfType<NoteText>().First(t => Words(t).StartsWith("Question 6:", StringComparison.Ordinal));
            string selected = question.GetSelfAndVisualAncestors().OfType<Control>().Reverse()
                .Select(TextSelection.Of).OfType<TextSelection>().Select(s => s.SelectedText).FirstOrDefault(t => t is { Length: > 0 }) ?? "";
            Assert.Equal("vessel 6?\nAnswer 6. Capillaries ", selected);
        }
        finally
        {
            w.Close();
        }
    }
}
