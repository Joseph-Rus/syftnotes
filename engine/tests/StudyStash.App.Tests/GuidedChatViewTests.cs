using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StudyStash.App.Services;
using StudyStash.App.ViewModels;
using StudyStash.App.Views;
using StudyStash.Core.Ai;

namespace StudyStash.App.Tests;

/// <summary>Guided setup's chat as the student uses it, in both looks, with the mouse and keyboard: a quick reply
/// sends, Enter sends, the field waits while a turn runs, and a checklist line asks the AI to go there.</summary>
public sealed class GuidedChatViewTests
{
    /// <summary>Turns that only write down what they were sent; <see cref="Hold"/> keeps one running.</summary>
    sealed class Turns
    {
        public List<string> Sent { get; } = [];
        public TaskCompletionSource? Hold { get; set; }

        public async IAsyncEnumerable<SetupChatEvent> Run(SetupTurn turn, [EnumeratorCancellation] CancellationToken ct)
        {
            Sent.Add(turn.Prompt);
            yield return new SetupChatEvent("Session", "s-1");
            if (Hold is { } hold) await hold.Task.WaitAsync(ct);
            yield return new SetupChatEvent("Text", "OK.");
            yield return new SetupChatEvent("Final", "OK.");
        }
    }

    static (Window Window, GuidedSetupModel Guided, Turns Turns) Open(SkinKind skin)
    {
        ((App)Application.Current!).UseSkin(skin);
        var turns = new Turns();
        string home = Path.Combine(Path.GetTempPath(), "studystash-app-tests", "guided-chat-" + Guid.NewGuid().ToString("N")[..8]);
        var services = new GuidedServices
        {
            Home = home, Windows = skin == SkinKind.Win, Find = _ => AgentFound.None, OpenUrl = _ => { }, Turn = turns.Run,
        };
        var g = new GuidedSetupModel(SetupModel.For(skin), services, () => new AppSettings(), _ => { })
        {
            Door = new SetupDoor("http://127.0.0.1:9/setup-mcp", "not-a-real-token", () => { }),
        };
        g.ClaudeFound = new AgentFound("claude", new Version(2, 1, 260), "2.1.260 (Claude Code)");
        g.Picked = "claude";
        g.AiReady = true;
        g.Screen = GuidedScreen.Chat;
        g.Refresh();
        Control view = skin == SkinKind.Mac ? new MacGuidedSetup { DataContext = g } : new WinGuidedSetup { DataContext = g };
        var window = new Window { Width = 1000, Height = 700, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, g, turns);
    }

    static T Find<T>(Window w, string name) where T : Control =>
        w.GetVisualDescendants().OfType<T>().First(c => c.Name == name);

    static void Click(Window w, Control c)
    {
        var at = c.TranslatePoint(new Point(c.Bounds.Width / 2, c.Bounds.Height / 2), w) ?? throw new InvalidOperationException("not on screen");
        w.MouseDown(at, MouseButton.Left);
        w.MouseUp(at, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    static async Task Until(Func<bool> done)
    {
        for (int i = 0; i < 200 && !done(); i++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
        Assert.True(done());
    }

    [AvaloniaTheory]
    [InlineData(SkinKind.Mac)]
    [InlineData(SkinKind.Win)]
    public async Task Enter_sends_and_the_field_waits_while_a_turn_runs(SkinKind skin)
    {
        var (w, g, turns) = Open(skin);
        var field = Find<TextBox>(w, "Field");
        Assert.True(field.IsEnabled);
        turns.Hold = new TaskCompletionSource();
        field.Focus();
        w.KeyTextInput("Just this computer");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Just this computer", g.Draft);
        w.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        await Until(() => turns.Sent.Count == 1);
        Assert.Equal("Just this computer", turns.Sent[0]);
        Assert.Equal("", g.Draft);

        // While it runs, the field waits.
        Assert.False(field.IsEnabled);
        Assert.False(Find<Button>(w, "Send").IsEnabled);
        turns.Hold.SetResult();
        await Until(() => !g.Busy);
        Assert.True(field.IsEnabled);
        Assert.Contains(g.Thread.OfType<AiEntry>(), a => a.Text == "OK.");
        w.Close();
    }

    [AvaloniaTheory]
    [InlineData(SkinKind.Mac)]
    [InlineData(SkinKind.Win)]
    public async Task A_quick_reply_is_sent_as_the_students_answer(SkinKind skin)
    {
        var (w, g, turns) = Open(skin);
        g.ShowQuestion("Does your school use Canvas?", ["Yes", "No", "Not sure"]);
        Dispatcher.UIThread.RunJobs();
        var replies = Find<ItemsControl>(w, "QuickReplies");
        Assert.True(replies.IsVisible);
        var no = replies.GetVisualDescendants().OfType<Button>().ElementAt(1);
        Click(w, no);
        await Until(() => turns.Sent.Count == 1);
        Assert.Equal("No", turns.Sent[0]);
        Assert.Equal("No", g.Thread.OfType<StudentEntry>().Single().Text);
        Assert.Empty(g.QuickReplies);
        w.Close();
    }

    [AvaloniaTheory]
    [InlineData(SkinKind.Mac)]
    [InlineData(SkinKind.Win)]
    public async Task A_checklist_line_asks_the_ai_to_go_there(SkinKind skin)
    {
        var (w, g, turns) = Open(skin);
        var list = Find<ItemsControl>(w, "Checklist");
        var canvas = list.GetVisualDescendants().OfType<Button>().First(b => b.DataContext is ChecklistRow { Id: "canvas" });
        Click(w, canvas);
        await Until(() => turns.Sent.Count == 1);
        Assert.Equal("[Study Stash] The student clicked Canvas in the checklist.", turns.Sent[0]);

        // The AI's own line, and anything done, isn't a button to press.
        var ai = list.GetVisualDescendants().OfType<Button>().First(b => b.DataContext is ChecklistRow { Id: "ai" });
        Assert.False(ai.IsHitTestVisible);
        w.Close();
    }

    [AvaloniaTheory]
    [InlineData(SkinKind.Mac)]
    [InlineData(SkinKind.Win)]
    public void A_card_folds_to_its_outcome_when_the_next_opens(SkinKind skin)
    {
        var (w, g, _) = Open(skin);
        var first = g.ShowCard(new Core.Setup.SetupCard("start_at_login"));
        Dispatcher.UIThread.RunJobs();
        Assert.True(first.Open);
        first.Outcome = "Starts when you log in";
        var second = g.ShowCard(new Core.Setup.SetupCard("microphone_check"));
        Dispatcher.UIThread.RunJobs();
        Assert.False(first.Open);
        Assert.Equal("Starts when you log in", first.FoldedLine);
        Assert.True(second.Open);
        Assert.Contains(w.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Starts when you log in" && t.IsEffectivelyVisible);
        w.Close();
    }
}
