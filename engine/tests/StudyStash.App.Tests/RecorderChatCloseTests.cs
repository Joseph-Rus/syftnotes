using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StudyStash.App.ViewModels;
using StudyStash.App.Views;
using StudyStash.Core.Ai;

namespace StudyStash.App.Tests;

/// <summary>The recorder's chat closes: a close button in its header (and Esc) gives the transcript its room back and
/// stops an answer still being written; the conversation is kept for the lecture, and asking again or "Show the chat"
/// brings it back. An answer's byline says plainly which moments it drew on.</summary>
public class RecorderChatCloseTests
{
    static async Task<(AiAskModel Model, FakeAiLibrary Library)> Loaded()
    {
        var lib = new FakeAiLibrary { Overview = AiTestData.MixedOverview() };
        var model = new AiAskModel(lib);
        await model.Load();
        return (model, lib);
    }

    static async Task WaitFor(Func<bool> done)
    {
        for (int i = 0; i < 200 && !done(); i++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
        Assert.True(done(), "it didn't happen in time");
    }

    [AvaloniaFact]
    public async Task Closing_the_chat_keeps_the_conversation_and_asking_again_brings_it_back()
    {
        var (model, lib) = await Loaded();
        lib.OnAskStream = (r, _, _) => Task.FromResult<AskReply?>(new AskReply("Recursion traces.", [], r.Engine!, "Claude Code"));
        Assert.False(model.CloseChatCommand.CanExecute(null)); // nothing to close yet
        Assert.False(model.CanShowChat);

        model.Question = "What's on the midterm?";
        await model.AskCommand.ExecuteAsync(null);
        Assert.True(model.HasLatest);
        Assert.True(model.CloseChatCommand.CanExecute(null));

        model.CloseChatCommand.Execute(null);
        Assert.False(model.HasLatest);
        Assert.True(model.CanShowChat);
        Assert.False(model.CloseChatCommand.CanExecute(null)); // Esc again does nothing (and falls through)
        Assert.Single(model.Turns); // kept

        model.ShowChatCommand.Execute(null);
        Assert.True(model.HasLatest);
        Assert.False(model.CanShowChat);

        model.CloseChatCommand.Execute(null);
        model.Question = "And Big-O?";
        await model.AskCommand.ExecuteAsync(null);
        Assert.True(model.HasLatest); // the next question opens it again, with the first one still above
        Assert.Equal(["What's on the midterm?", "And Big-O?"], model.Turns.Select(t => t.Question));
    }

    [AvaloniaFact]
    public async Task Closing_the_chat_stops_an_answer_still_being_written()
    {
        var (model, lib) = await Loaded();
        lib.OnAskStream = async (_, soFar, stop) =>
        {
            soFar("Recursion traces will");
            await Task.Delay(Timeout.InfiniteTimeSpan, stop);
            return null;
        };
        model.Question = "q";
        var asking = model.AskCommand.ExecuteAsync(null);
        await WaitFor(() => model.Turns[0].Answer is not null);
        Assert.True(model.Busy);

        model.CloseChatCommand.Execute(null);
        await asking;
        Assert.False(model.Busy);
        Assert.Equal(AiWords.Stopped, model.Turns[0].Byline);
        Assert.Equal("Recursion traces will", model.Turns[0].Answer);
        Assert.False(model.HasLatest);
    }

    static (Window Window, Control View) Show(SkinKind skin, RecorderModel model)
    {
        Skin.UseTheme(ColourThemes.Default);
        ((App)Application.Current!).UseSkin(skin);
        Control view = skin == SkinKind.Mac ? new MacRecorder { DataContext = model } : new WinRecorder { DataContext = model };
        var window = new Window { Width = 420, Height = 600, RequestedThemeVariant = ThemeVariant.Light, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, view);
    }

    static T Named<T>(Control view, string name) where T : Control =>
        view.GetVisualDescendants().OfType<T>().First(c => c.Name == name);

    static bool Shown(Visual v) => v.IsVisible && v.GetVisualAncestors().All(a => a.IsVisible);

    [AvaloniaTheory]
    [InlineData(SkinKind.Mac)]
    [InlineData(SkinKind.Win)]
    public void The_recorder_s_chat_has_a_close_button_and_Esc_closes_it_too(SkinKind skin)
    {
        try
        {
            var model = Demo.Recorder(expanded: true);
            var ask = model.Ask!;
            var (window, view) = Show(skin, model);

            var close = Named<Button>(view, "CloseChat");
            Assert.True(Shown(close));
            Assert.Equal("Close chat", ToolTip.GetTip(close));
            Assert.Equal("Close chat", AutomationProperties.GetName(close));
            Assert.True(Shown(Named<ScrollViewer>(view, "Thread")));
            Assert.False(Shown(Named<Button>(view, "ShowChat")));

            close.Command!.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.False(Shown(Named<ScrollViewer>(view, "Thread")));
            Assert.False(Shown(close));
            var show = Named<Button>(view, "ShowChat");
            Assert.True(Shown(show));

            show.Command!.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.True(Shown(Named<ScrollViewer>(view, "Thread")));

            // Esc, with the question field focused (where it is after asking), closes it the same way.
            var field = view.GetVisualDescendants().OfType<TextBox>().First(Shown);
            field.Focus();
            Dispatcher.UIThread.RunJobs();
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            Dispatcher.UIThread.RunJobs();
            Assert.False(ask.HasLatest);
            Assert.False(Shown(Named<ScrollViewer>(view, "Thread")));
            Assert.Single(ask.Turns);
            window.Close();
        }
        finally
        {
            ((App)Application.Current!).UseSkin(SkinKind.Mac);
        }
    }

    [Fact]
    public void An_answer_s_byline_says_which_moments_it_drew_on_earliest_first_each_once()
    {
        static AskSource At(double? seconds) => new("lec-1", "t", null, null, seconds, "");
        // The owner's own: the engine listed 02:00 before 01:32, which read as two times of day.
        Assert.Equal("Claude Code · from 01:32 and 02:00", AiWords.AskByline("Claude Code", [At(120), At(92)]));
        Assert.Equal("Ollama · from 18:05", AiWords.AskByline("Ollama", [At(18 * 60 + 5), At(18 * 60 + 5.4)]));
        Assert.Equal("Ollama · from 01:00, 18:05 and 18:40", AiWords.AskByline("Ollama", [At(18 * 60 + 40), At(60), At(null), At(18 * 60 + 5)]));
        Assert.Equal("Claude Code", AiWords.AskByline("Claude Code", [At(null)]));
    }
}
