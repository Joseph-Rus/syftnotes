using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using StudyStash.App.Controls;
using StudyStash.App.Controls.Rich;
using StudyStash.App.ViewModels;
using StudyStash.Core;
using StudyStash.Core.Ai;
using StudyStash.Core.Tests;
using StudyStash.Library;

namespace StudyStash.App.Tests;

/// <summary>An answer on screen as it's written: the ask thread, the recorder's chat and the quick panel show the
/// first words at once and the rest at a steady pace; stopping or a failure keeps what came; the notes view lays out
/// only the newest block again. Every library here is a fake or a test server with a fake engine.</summary>
public class AskStreamingTests
{
    [Fact]
    public async Task Pieces_arriving_in_a_burst_are_drawn_at_a_steady_pace_first_at_once_last_for_sure()
    {
        var drawn = new List<string>();
        var paced = new Paced(t =>
        {
            lock (drawn) drawn.Add(t);
        });

        for (int i = 1; i <= 100; i++) paced.Show(new string('a', i));
        lock (drawn) Assert.Equal("a", drawn[0]); // the first words go straight up

        await Task.Delay(Paced.Every * 5, TestContext.Current.CancellationToken);
        lock (drawn)
        {
            Assert.Equal(new string('a', 100), drawn[^1]); // and the latest is always drawn in the end
            Assert.True(drawn.Count < 10, $"{drawn.Count} draws for 100 pieces");
        }
        Assert.Equal(new string('a', 100), paced.End());
        paced.Show("too late");
        await Task.Delay(Paced.Every * 3, TestContext.Current.CancellationToken);
        lock (drawn) Assert.DoesNotContain("too late", drawn);
    }

    static async Task<(AiAskModel Model, FakeAiLibrary Library)> Loaded()
    {
        var lib = new FakeAiLibrary { Overview = AiTestData.MixedOverview() };
        var model = new AiAskModel(lib);
        await model.Load();
        return (model, lib);
    }

    [AvaloniaFact]
    public async Task The_thread_shows_the_answer_as_its_written_then_the_whole_reply()
    {
        var (model, lib) = await Loaded();
        var more = new TaskCompletionSource();
        lib.OnAskStream = async (r, soFar, stop) =>
        {
            await Task.Yield();
            soFar("Recursion traces");
            await more.Task.WaitAsync(stop);
            soFar("Recursion traces and stack diagrams.");
            return new AskReply("Recursion traces and stack diagrams.", [new AskSource("lec-1", "t", null, null, 18 * 60 + 5, "")], r.Engine!, "Claude Code");
        };
        model.Question = "What's on the midterm?";

        var asking = model.AskCommand.ExecuteAsync(null);
        var turn = Assert.Single(model.Turns);
        Assert.True(turn.IsThinking); // said at once, before the library has said anything
        Assert.True(model.Busy);

        await WaitFor(() => turn.Answer == "Recursion traces");
        Assert.False(turn.IsThinking);
        Assert.Null(turn.Byline);
        Assert.True(model.Busy); // still writing: the send button is a stop button

        more.SetResult();
        await asking;
        Assert.Equal("Recursion traces and stack diagrams.", turn.Answer);
        Assert.Equal("Claude Code · from 18:05", turn.Byline);
        Assert.False(model.Busy);
    }

    [AvaloniaFact]
    public async Task Stopping_keeps_what_was_written_and_says_it_stopped()
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
        model.StopCommand.Execute(null);
        await asking;

        var turn = model.Turns[0];
        Assert.Equal("Recursion traces will", turn.Answer);
        Assert.Equal(AiWords.Stopped, turn.Byline);
        Assert.Null(turn.Failed);
        Assert.False(model.Busy);
    }

    [AvaloniaFact]
    public async Task Stopped_before_a_word_came_the_turn_stops_thinking()
    {
        var (model, lib) = await Loaded();
        lib.OnAskStream = async (_, _, stop) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, stop);
            return null;
        };
        model.Question = "q";

        var asking = model.AskCommand.ExecuteAsync(null);
        model.CloseAnswerCommand.Execute(null); // closing the answer stops it too
        await asking;

        Assert.False(model.Turns[0].IsThinking);
        Assert.Equal(AiWords.Stopped, model.Turns[0].Byline);
    }

    [AvaloniaFact]
    public async Task An_engine_failing_partway_keeps_the_answer_and_says_what_stopped_it()
    {
        var (model, lib) = await Loaded();
        lib.OnAskStream = (_, soFar, _) =>
        {
            soFar("Half an answer");
            throw new LibraryRefusedException(503, "Claude Code stopped partway through: rate limit exceeded");
        };
        model.Question = "q";

        await model.AskCommand.ExecuteAsync(null);

        Assert.Equal("Half an answer", model.Turns[0].Answer);
        Assert.Equal("Claude Code stopped partway through: rate limit exceeded", model.Turns[0].Failed);
    }

    [AvaloniaFact]
    public async Task A_library_that_goes_quiet_partway_keeps_the_answer_and_isnt_offline()
    {
        var (model, lib) = await Loaded();
        lib.OnAskStream = (_, soFar, _) =>
        {
            soFar("Half an answer, then $\\frac{a}{");
            throw new HttpRequestException("The library stopped answering partway through.");
        };
        model.Question = "q";

        await model.AskCommand.ExecuteAsync(null);

        Assert.Equal("Half an answer, then", model.Turns[0].Answer); // no formula left half written
        Assert.Equal(AiWords.CutOff, model.Turns[0].Failed);
        Assert.False(model.Offline);
    }

    [AvaloniaFact]
    public async Task Typing_a_question_gets_the_picked_engine_ready_once()
    {
        var (model, lib) = await Loaded();
        model.Engine = "ollama";

        model.Question = "W";
        model.Question = "Wh";
        model.Question = "What";

        Assert.Equal(["ollama"], lib.Warmed);
    }

    [AvaloniaFact]
    public async Task The_quick_panel_shows_its_answer_as_its_written_then_the_sources()
    {
        var lib = new FakeAiLibrary { Overview = AiTestData.MixedOverview() };
        var quick = new QuickModel();
        var more = new TaskCompletionSource();
        lib.OnAskStream = async (_, soFar, stop) =>
        {
            await Task.Yield();
            soFar("Osmosis moves");
            await more.Task.WaitAsync(stop);
            return new AskReply("Osmosis moves water across a membrane.",
                [new AskSource("lec-2", "Membranes and osmosis", "BIO 110", "2026-09-22", 65, "")], "ollama", "Ollama");
        };

        var asking = quick.AnswerAsync(lib, "What is osmosis?");
        Assert.True(quick.Answering);
        Assert.True(quick.Thinking); // the spinner, at once

        await WaitFor(() => quick.Answer == "Osmosis moves");
        Assert.False(quick.Thinking);
        Assert.Empty(quick.Rows);

        more.SetResult();
        Assert.True(await asking);
        Assert.Equal("Osmosis moves water across a membrane.", quick.Answer);
        Assert.Equal(["Sources", "Membranes and osmosis"], quick.Rows.Select(r => r.Title));
        Assert.Equal("01:05", quick.Rows[1].Meta);
    }

    [AvaloniaFact]
    public async Task Searching_again_stops_the_quick_answer()
    {
        var lib = new FakeAiLibrary();
        var quick = new QuickModel();
        bool stopped = false;
        lib.OnAskStream = async (_, soFar, stop) =>
        {
            soFar("Osmosis");
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, stop);
            }
            catch (OperationCanceledException)
            {
                stopped = true;
                throw;
            }
            return null;
        };

        var asking = quick.AnswerAsync(lib, "What is osmosis?");
        quick.Query = "membrane";
        Assert.True(await asking);

        Assert.True(stopped);
        Assert.False(quick.Answering);
    }

    [AvaloniaFact]
    public async Task From_the_library_on_this_computer_the_thread_shows_the_first_words_while_the_engine_is_still_writing()
    {
        using var dir = new TempDir();
        var cfg = new Config(dir["home"], dir["pool"]) { PoolPassword = "pw", OllamaEnabled = true };
        using var store = new Store(cfg.DbPath, cfg.PoolDir);
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var claude = new ScriptedAi("claude", "Claude")
        {
            Script = [new AiEvent("text", "{\"answer\": \"Recursion traces"), new AiEvent("text", " and stack diagrams.\", \"sources\": [1]}")],
            Gate = gate, GateAfter = 1,
        };
        await using var app = await LibraryOnThisComputer(cfg, store, claude);
        using var http = new HttpClient();
        var model = AskingAbout(app, http);

        var asking = model.AskCommand.ExecuteAsync(null);
        await WaitFor(() => model.Turns[0].Answer == "Recursion traces");
        Assert.False(claude.Finished);

        gate.SetResult(true);
        await asking;
        Assert.Equal("Recursion traces and stack diagrams.", model.Turns[0].Answer);
        Assert.Null(model.Turns[0].Failed);
        await app.StopAsync(TestContext.Current.CancellationToken); // as the library stops: nothing of it outlives the test
    }

    [AvaloniaFact]
    public async Task Stop_partway_keeps_the_words_so_far_and_the_library_on_this_computer_stops_the_engine()
    {
        using var dir = new TempDir();
        var cfg = new Config(dir["home"], dir["pool"]) { PoolPassword = "pw", OllamaEnabled = true };
        using var store = new Store(cfg.DbPath, cfg.PoolDir);
        var claude = new ScriptedAi("claude", "Claude")
        {
            Script = [new AiEvent("text", "{\"answer\": \"Recursion traces"), new AiEvent("text", " and stack diagrams.\", \"sources\": [1]}")],
            Gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously), GateAfter = 1,
        };
        await using var app = await LibraryOnThisComputer(cfg, store, claude);
        using var http = new HttpClient();
        var model = AskingAbout(app, http);

        var asking = model.AskCommand.ExecuteAsync(null);
        await WaitFor(() => model.Turns[0].Answer == "Recursion traces");
        model.StopCommand.Execute(null);
        await asking;

        Assert.Equal(("Recursion traces", AiWords.Stopped), (model.Turns[0].Answer, model.Turns[0].Byline));
        Assert.Null(model.Turns[0].Failed);
        Assert.False(model.Busy);
        await claude.StoppedWhileHeld.Task.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken); // the engine, not just the screen
        Assert.False(claude.Finished);
        await app.StopAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>A library on this computer, over a real socket, whose only engine is <paramref name="claude"/>.</summary>
    static async Task<WebApplication> LibraryOnThisComputer(Config cfg, Store store, ScriptedAi claude)
    {
        var ai = new AiJobs(cfg.Home) { Providers = id => id == "claude" ? claude : new ScriptedAi(id), Checks = new FakeChecks().Installed("claude").Build() };
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        var app = LibraryWeb.Build(builder, cfg, store, new Pipeline(cfg, store, log: _ => { }), new LibraryWebOptions
        {
            ListModels = _ => Task.FromResult<List<(string, double)>?>(null), Tailscale = () => new TailscaleInfo(),
            Latest = _ => Task.FromResult<Release?>(null), Ai = ai,
        });
        await app.StartAsync(TestContext.Current.CancellationToken);
        return app;
    }

    /// <summary>The ask bar, talking to <paramref name="app"/> the way the app talks to its library, with a question typed.</summary>
    static AiAskModel AskingAbout(WebApplication app, HttpClient http) => new(new AiRemote(app.Urls.Single(), "pw", http))
    {
        Engine = "claude", Live = () => "Recursion traces will be on the midterm, with the stack diagrams.", Question = "What's on the midterm?",
    };

    [AvaloniaFact]
    public void The_notes_view_lays_out_only_the_newest_block_as_an_answer_grows()
    {
        var note = new NoteView { Compact = true, Markdown = "Recursion is a function calling itself.\n\n- a base case\n- a smaller problem" };
        var first = note.Children[0];
        var list = note.Children[1];

        note.Markdown += " each time";

        Assert.Same(first, note.Children[0]);
        Assert.NotSame(list, note.Children[1]);
        note.Markdown += "\n\nThe stack";
        Assert.Same(first, note.Children[0]);
        Assert.Equal(3, note.Children.Count);
    }

    [AvaloniaFact]
    public void Two_of_the_same_diagram_each_keep_theirs_as_the_answer_grows()
    {
        const string chart = "```mermaid\nflowchart LR\n  A[Assess] --> B[Act]\n```";
        var note = new NoteView { Compact = true, Markdown = $"First:\n\n{chart}\n\nAgain:\n\n{chart}" };
        var drawn = note.Children.OfType<DiagramView>().ToList();
        Assert.Equal(2, drawn.Count);

        note.Markdown += "\n\nThat's all."; // the first is kept as it was; the second, no longer last, is looked at again

        Assert.Equal(drawn, note.Children.OfType<DiagramView>());
    }

    [AvaloniaFact]
    public void A_formula_block_still_being_written_is_a_quiet_line_until_it_closes()
    {
        var note = new NoteView { Compact = true, Markdown = "The mean:\n\n$$\n\\bar{x} = \\frac{1}{n}" };
        Assert.Equal(NoteView.FormulaWords, Assert.IsAssignableFrom<TextBlock>(note.Children[^1]).Text);

        note.Markdown += "\\sum x_i\n$$";
        Assert.IsNotType<TextBlock>(note.Children[^1]);
    }

    [AvaloniaFact]
    public void An_answer_growing_past_its_card_keeps_its_newest_words_in_view_unless_scrolled_up()
    {
        var lines = new StackPanel();
        var scroll = new ScrollViewer { Height = 100, Content = lines };
        FollowEnd.SetIsOn(scroll, true);
        var window = new Window { Width = 300, Height = 200, Content = scroll };
        window.Show();
        void Add(int n)
        {
            for (int i = 0; i < n; i++) lines.Children.Add(new Border { Height = 20 });
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
        }

        Add(12);
        Assert.Equal(scroll.Extent.Height - scroll.Viewport.Height, scroll.Offset.Y, 1);

        scroll.Offset = new Vector(0, 20); // the student scrolls up to reread
        Dispatcher.UIThread.RunJobs();
        Add(3);
        Assert.Equal(20, scroll.Offset.Y, 1);

        scroll.Offset = new Vector(0, scroll.Extent.Height - scroll.Viewport.Height); // and back down to the end
        Dispatcher.UIThread.RunJobs();
        Add(3);
        Assert.Equal(scroll.Extent.Height - scroll.Viewport.Height, scroll.Offset.Y, 1);
        window.Close();
    }

    /// <summary>Lets the UI thread run until <paramref name="done"/> holds (or fails after a few seconds).</summary>
    static async Task WaitFor(Func<bool> done)
    {
        var until = DateTime.UtcNow.AddSeconds(10);
        while (!done())
        {
            Assert.True(DateTime.UtcNow < until, "waited too long");
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }
}
