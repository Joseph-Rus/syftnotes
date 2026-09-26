using Avalonia.Headless.XUnit;
using StudyStash.App.ViewModels;
using StudyStash.Core;
using StudyStash.Core.Ai;

namespace StudyStash.App.Tests;

public class AiWordsTests
{
    [Theory]
    [InlineData("ready", "Ready", "Options")]
    [InlineData("unchecked", "Installed", "Check")]
    [InlineData("not_signed_in", "Not signed in", "Sign in")]
    [InlineData("not_running", "Not running", "Start")]
    [InlineData("model_missing", "Needs its model", "Download")]
    [InlineData("limited", "Limit reached", "Options")]
    [InlineData("not_installed", "Not installed", "Get it")]
    [InlineData("failed", "Didn't work", "Options")]
    public void Every_state_has_its_word_and_its_row_action(string state, string words, string action)
    {
        Assert.Equal(words, AiWords.EngineStateWords(state));
        Assert.Equal(action, AiWords.RowActionWords(state));
    }

    [Fact]
    public void Only_sign_in_is_the_primary_button()
    {
        Assert.True(AiWords.RowActionPrimary("not_signed_in"));
        foreach (string s in new[] { "ready", "unchecked", "not_running", "model_missing", "limited", "not_installed", "failed" })
            Assert.False(AiWords.RowActionPrimary(s));
    }

    [Fact]
    public void Only_ready_limited_and_failed_open_the_options_menu()
    {
        Assert.True(AiWords.NeedsOptions("ready"));
        Assert.True(AiWords.NeedsOptions("limited"));
        Assert.True(AiWords.NeedsOptions("failed"));
        foreach (string s in new[] { "unchecked", "not_signed_in", "not_running", "model_missing", "not_installed" })
            Assert.False(AiWords.NeedsOptions(s));
    }

    [Fact]
    public void Ollama_never_leaves_this_computer_a_signed_in_cli_says_so()
    {
        Assert.Equal("Runs on your library. Nothing leaves it.", AiWords.EngineAbout("ollama", "ready"));
        Assert.Equal("Runs on your library. Signed in.", AiWords.EngineAbout("claude", "ready"));
        Assert.Equal("Runs on your library.", AiWords.EngineAbout("codex", "not_signed_in"));
    }

    [Fact]
    public void Setup_words_are_phrased_for_this_computer()
    {
        Assert.Equal("Private. Runs here, nothing leaves this computer.", AiWords.SetupAbout("ollama", "ready"));
        Assert.Equal("Signed in on this computer.", AiWords.SetupAbout("claude", "ready"));
        Assert.Equal("Sign in first.", AiWords.SetupAbout("codex", "not_signed_in"));
        Assert.Equal("Installed on this computer.", AiWords.SetupAbout("gemini", "unchecked"));
    }

    [Fact]
    public void Only_ready_and_unchecked_engines_are_worth_asking_or_writing_with()
    {
        Assert.True(AiWords.EngineUsable("ready"));
        Assert.True(AiWords.EngineUsable("unchecked"));
        foreach (string s in new[] { "not_signed_in", "not_running", "model_missing", "limited", "not_installed", "failed" })
            Assert.False(AiWords.EngineUsable(s));
    }

    [Fact]
    public void The_ask_menus_subtitle_says_default_private_or_signed_out()
    {
        Assert.Equal("Default for questions", AiWords.AskEngineSubtitle("claude", "ready", isDefault: true));
        Assert.Equal("Private, on your library", AiWords.AskEngineSubtitle("ollama", "ready", isDefault: false));
        Assert.Equal("Not signed in", AiWords.AskEngineSubtitle("codex", "not_signed_in", isDefault: false));
        Assert.Equal("", AiWords.AskEngineSubtitle("codex", "ready", isDefault: false));
    }

    [Fact]
    public void The_ask_fields_placeholder_follows_the_scope()
    {
        Assert.Equal("Ask about this lecture", AiWords.AskPlaceholder("lecture"));
        Assert.Equal("Ask about this class", AiWords.AskPlaceholder("class"));
        Assert.Equal("Ask about all your classes", AiWords.AskPlaceholder("all"));
    }

    [Fact]
    public void An_answers_byline_names_the_engine_and_its_moments_or_just_the_engine()
    {
        Assert.Equal("Ollama · 18:05, 18:40", AiWords.AskByline("Ollama", [new AskSource(null, "t", null, null, 18 * 60 + 5, ""), new AskSource(null, "t", null, null, 18 * 60 + 40, "")]));
        Assert.Equal("Claude Code", AiWords.AskByline("Claude Code", [new AskSource(null, "t", null, null, null, "Intro")]));
    }

    [Fact]
    public void A_fallback_note_names_who_answered_and_why_the_asked_engine_didnt()
    {
        Assert.Equal("This answer came from Ollama. Claude Code didn't respond in time.", AiWords.FellBackNote("Ollama", "Claude Code didn't respond in time."));
    }
}

public class AiEnginesModelTests
{
    static (AiEnginesModel Model, FakeAiLibrary Library) Loaded()
    {
        var lib = new FakeAiLibrary { Overview = AiTestData.MixedOverview() };
        var model = new AiEnginesModel(lib);
        return (model, lib);
    }

    [AvaloniaFact]
    public async Task Loading_fills_the_rows_and_the_defaults_without_posting_anything()
    {
        var (model, lib) = Loaded();

        await model.Load();

        Assert.Equal(["Ollama", "Claude Code", "Codex"], model.Engines.Select(r => r.Name));
        Assert.Single(model.AddChoices);
        Assert.Equal("Gemini", model.AddChoices[0].Name);
        Assert.Equal("ollama", model.SelectedNotes);
        Assert.Equal("claude", model.SelectedAsk);
        Assert.True(model.Fallback);
        Assert.False(model.OlderLibrary);
        Assert.False(model.Offline);
        Assert.DoesNotContain("defaults", lib.Calls);

        var codex = model.Engines.Single(r => r.Id == "codex");
        Assert.Equal("Not signed in", codex.StateWords);
        Assert.True(codex.Warn);
        Assert.Equal("Sign in", codex.ActionWords);
        Assert.True(codex.IsPrimary);
    }

    [AvaloniaFact]
    public async Task Picking_who_writes_notes_posts_the_new_default()
    {
        var (model, lib) = Loaded();
        await model.Load();

        model.SelectedNotes = "claude";

        Assert.Equal(("claude", (string?)null, (bool?)null), (lib.DefaultsCalls[0].Notes, lib.DefaultsCalls[0].Ask, lib.DefaultsCalls[0].Fallback));
        Assert.Equal("claude", lib.Overview!.Notes);
    }

    [AvaloniaFact]
    public async Task Turning_the_fallback_off_posts_it()
    {
        var (model, lib) = Loaded();
        await model.Load();

        model.Fallback = false;

        Assert.Single(lib.DefaultsCalls);
        Assert.False(lib.Overview!.Fallback);
    }

    [AvaloniaFact]
    public async Task Signing_in_calls_the_engine_and_says_what_happened()
    {
        var (model, lib) = Loaded();
        await model.Load();
        var codex = model.Engines.Single(r => r.Id == "codex");

        await codex.ActCommand.ExecuteAsync(null);

        Assert.Contains("sign-in:codex", lib.Calls);
        Assert.Equal("Ready", model.Engines.Single(r => r.Id == "codex").StateWords);
        Assert.NotNull(model.Say);
    }

    [AvaloniaFact]
    public async Task Checking_a_not_yet_tested_engine_calls_check()
    {
        var overview = AiTestData.MixedOverview();
        var lib = new FakeAiLibrary { Overview = overview with { Engines = [.. overview.Engines.Select(e => e.Id == "codex" ? e with { State = "unchecked" } : e)] } };
        var model = new AiEnginesModel(lib);
        await model.Load();
        var codex = model.Engines.Single(r => r.Id == "codex");
        Assert.Equal("Check", codex.ActionWords);

        await codex.ActCommand.ExecuteAsync(null);

        Assert.Contains("check:codex", lib.Calls);
    }

    [AvaloniaFact]
    public async Task Adding_an_engine_opens_its_site()
    {
        var (model, _) = Loaded();
        await model.Load();
        string? opened = null;
        model.OpenUrl = url => opened = url;
        var gemini = model.AddChoices.Single();

        gemini.AddCommand.Execute(null);

        Assert.Equal("https://gemini.google.com", opened);
        Assert.Equal("Install Gemini on your library's computer.", model.Say);
    }

    [AvaloniaFact]
    public async Task An_older_library_says_so_instead_of_showing_engines()
    {
        var model = new AiEnginesModel(new FakeAiLibrary { Overview = null });

        await model.Load();

        Assert.True(model.OlderLibrary);
        Assert.Empty(model.Engines);
    }

    [AvaloniaFact]
    public async Task A_library_that_cant_be_reached_says_offline()
    {
        var lib = new FakeAiLibrary { OnEngines = () => throw new HttpRequestException("down") };
        var model = new AiEnginesModel(lib);

        await model.Load();

        Assert.True(model.Offline);
    }
}

public class AiSetupModelTests
{
    static (AiSetupModel Model, FakeAiLibrary Library) Loaded()
    {
        var lib = new FakeAiLibrary { Overview = AiTestData.MixedOverview() with { Ask = "ollama" } };
        var model = new AiSetupModel(lib);
        return (model, lib);
    }

    [AvaloniaFact]
    public async Task Loading_shows_the_computers_engines_ollama_recommended()
    {
        var (model, _) = Loaded();

        await model.Load();

        Assert.Equal(["ollama", "claude", "codex"], model.Engines.Select(r => r.Id));
        var ollama = model.Engines.Single(r => r.Id == "ollama");
        Assert.True(ollama.Recommended);
        Assert.True(ollama.Selected);
        var codex = model.Engines.Single(r => r.Id == "codex");
        Assert.True(codex.ShowSignIn);
        Assert.Equal("Sign in first.", codex.About);
        // ask == notes on the library: the select starts on "Same as notes".
        Assert.Equal(AiSetupModel.SameAsNotes, model.SelectedAsk);
        Assert.Equal("Same as notes", model.SelectedAskName);
    }

    [AvaloniaFact]
    public async Task Picking_a_row_moves_the_radio_without_posting_anything()
    {
        var (model, lib) = Loaded();
        await model.Load();

        model.Engines.Single(r => r.Id == "claude").SelectCommand.Execute(null);

        Assert.Equal("claude", model.SelectedNotes);
        Assert.True(model.Engines.Single(r => r.Id == "claude").Selected);
        Assert.False(model.Engines.Single(r => r.Id == "ollama").Selected);
        Assert.Empty(lib.DefaultsCalls);
    }

    [AvaloniaFact]
    public async Task Signing_in_from_the_setup_row_calls_the_engine()
    {
        var (model, lib) = Loaded();
        await model.Load();

        await model.Engines.Single(r => r.Id == "codex").SignInCommand.ExecuteAsync(null);

        Assert.Contains("sign-in:codex", lib.Calls);
        Assert.False(model.Engines.Single(r => r.Id == "codex").ShowSignIn);
    }

    [AvaloniaFact]
    public async Task Continue_saves_notes_and_lets_ask_follow_it_when_left_as_same()
    {
        var (model, lib) = Loaded();
        await model.Load();
        model.Engines.Single(r => r.Id == "claude").SelectCommand.Execute(null);

        bool ok = await model.SaveAsync();

        Assert.True(ok);
        Assert.Equal(("claude", "claude", (bool?)null), (lib.DefaultsCalls[0].Notes, lib.DefaultsCalls[0].Ask, lib.DefaultsCalls[0].Fallback));
    }

    [AvaloniaFact]
    public async Task Continue_saves_a_picked_ask_engine_on_its_own()
    {
        var (model, lib) = Loaded();
        await model.Load();
        model.SelectedAsk = "codex";

        await model.SaveAsync();

        Assert.Equal("codex", lib.DefaultsCalls[0].Ask);
    }

    [AvaloniaFact]
    public async Task An_older_library_refuses_to_save()
    {
        var model = new AiSetupModel(new FakeAiLibrary { Overview = null });

        bool ok = await model.SaveAsync();

        Assert.False(ok);
        Assert.True(model.OlderLibrary);
    }
}

public class AiAskModelTests
{
    static (AiAskModel Model, FakeAiLibrary Library) Loaded()
    {
        var lib = new FakeAiLibrary { Overview = AiTestData.MixedOverview() };
        var model = new AiAskModel(lib);
        return (model, lib);
    }

    [AvaloniaFact]
    public async Task Loading_picks_the_librarys_default_engine_checked_first_gemini_never_shows()
    {
        var (model, _) = Loaded();

        await model.Load();

        Assert.Equal("claude", model.Engine);
        Assert.Equal("Claude Code", model.EngineName);
        Assert.Equal(["claude", "ollama", "codex"], model.Menu.Items.Select(i => i.Id));
        Assert.True(model.Menu.Items.Single(i => i.Id == "claude").Checked);
        Assert.Equal("Default for questions", model.Menu.Items.Single(i => i.Id == "claude").Subtitle);
        Assert.True(model.Menu.Items.Single(i => i.Id == "claude").Selected);
        Assert.False(model.Menu.Items.Single(i => i.Id == "ollama").Selected);
    }

    [AvaloniaFact]
    public async Task A_signed_out_engine_is_disabled_but_still_listed()
    {
        var (model, _) = Loaded();

        await model.Load();

        var codex = model.Menu.Items.Single(i => i.Id == "codex");
        Assert.False(codex.Enabled);
        Assert.Equal("Not signed in", codex.Subtitle);
    }

    [AvaloniaFact]
    public async Task Picking_a_row_selects_it_and_closes_the_menu()
    {
        var (model, _) = Loaded();
        await model.Load();
        bool closed = false;
        model.CloseMenu = () => closed = true;

        model.Menu.Items.Single(i => i.Id == "ollama").Command.Execute(null);

        Assert.Equal("ollama", model.Engine);
        Assert.True(model.Menu.Items.Single(i => i.Id == "ollama").Selected);
        Assert.False(model.Menu.Items.Single(i => i.Id == "claude").Selected);
        Assert.True(closed);
    }

    [AvaloniaFact]
    public async Task Asking_sends_the_engine_thats_picked_and_answers_with_a_byline()
    {
        var (model, lib) = Loaded();
        await model.Load();
        model.Engine = "ollama";
        lib.OnAsk = r => new AskReply("Recursion traces.", [new AskSource(null, "t", null, null, 18 * 60 + 5, "")], r.Engine!, "Ollama");
        model.Question = "What's on the midterm?";

        await model.AskCommand.ExecuteAsync(null);

        Assert.Equal("ollama", lib.AskRequests[0].Engine);
        var turn = Assert.Single(model.Turns);
        Assert.Equal("Recursion traces.", turn.Answer);
        Assert.Equal("Ollama · 18:05", turn.Byline);
        Assert.False(turn.IsThinking);
        Assert.Equal("", model.Question);
    }

    [AvaloniaFact]
    public async Task Scope_picks_which_lecture_or_class_the_question_names()
    {
        var (model, lib) = Loaded();
        await model.Load();
        model.LectureId = "lec-1";
        model.ClassName = "CS 101";
        lib.OnAsk = r => new AskReply("ok", [], r.Engine!, "Ollama");

        model.Question = "q1";
        await model.AskCommand.ExecuteAsync(null);
        Assert.Equal("lec-1", lib.AskRequests[0].Lecture);
        Assert.Null(lib.AskRequests[0].Class);

        model.Scope = "class";
        model.Question = "q2";
        await model.AskCommand.ExecuteAsync(null);
        Assert.Null(lib.AskRequests[1].Lecture);
        Assert.Equal("CS 101", lib.AskRequests[1].Class);

        model.Scope = "all";
        model.Question = "q3";
        await model.AskCommand.ExecuteAsync(null);
        Assert.Null(lib.AskRequests[2].Lecture);
        Assert.Null(lib.AskRequests[2].Class);
    }

    [AvaloniaFact]
    public async Task A_fallen_back_answer_carries_its_note()
    {
        var (model, lib) = Loaded();
        await model.Load();
        lib.OnAsk = r => new AskReply("From Ollama.", [], "ollama", "Ollama") { FellBack = true, Why = "Claude Code didn't respond in time." };
        model.Question = "q";

        await model.AskCommand.ExecuteAsync(null);

        Assert.Equal("This answer came from Ollama. Claude Code didn't respond in time.", model.Turns[0].FellBackNote);
    }

    [AvaloniaFact]
    public async Task A_refusal_fails_the_turn_with_its_own_words()
    {
        var (model, lib) = Loaded();
        await model.Load();
        lib.OnAsk = _ => throw new LibraryRefusedException(503, "Asking needs an engine: turn one on in AI engines.");
        model.Question = "q";

        await model.AskCommand.ExecuteAsync(null);

        Assert.Equal("Asking needs an engine: turn one on in AI engines.", model.Turns[0].Failed);
        Assert.True(model.Turns[0].IsThinking == false);
    }

    [AvaloniaFact]
    public async Task An_older_librarys_null_reply_fails_the_turn_and_says_so()
    {
        var (model, lib) = Loaded();
        await model.Load();
        lib.OnAsk = _ => null;
        model.Question = "q";

        await model.AskCommand.ExecuteAsync(null);

        Assert.Equal(AiWords.OlderLibraryWords, model.Turns[0].Failed);
        Assert.True(model.OlderLibrary);
    }

    [AvaloniaFact]
    public async Task Tapping_the_byline_opens_its_first_source()
    {
        var (model, lib) = Loaded();
        await model.Load();
        var source = new AskSource("lec-1", "t", null, null, 5, "");
        lib.OnAsk = r => new AskReply("a", [source], r.Engine!, "Ollama");
        model.Question = "q";
        await model.AskCommand.ExecuteAsync(null);
        AskSource? opened = null;
        model.OnSource = s => opened = s;

        model.OpenTurnSourceCommand.Execute(model.Turns[0]);

        Assert.Same(source, opened);
    }

    [AvaloniaFact]
    public async Task A_library_that_cant_be_reached_says_offline()
    {
        var lib = new FakeAiLibrary { OnEngines = () => throw new HttpRequestException("down") };
        var model = new AiAskModel(lib);

        await model.Load();

        Assert.True(model.Offline);
    }

    [AvaloniaFact]
    public async Task An_older_library_says_so_instead_of_building_a_menu()
    {
        var model = new AiAskModel(new FakeAiLibrary { Overview = null });

        await model.Load();

        Assert.True(model.OlderLibrary);
        Assert.Empty(model.Menu.Items);
    }
}

public class AiWordsRewriteAndProblemTests
{
    [Fact]
    public void A_leading_summary_heading_is_dropped_but_no_other_one_is()
    {
        Assert.Equal("Body text.", AiWords.DropLeadingSummary("# Summary\nBody text."));
        Assert.Equal("Body text.", AiWords.DropLeadingSummary("## Summary\n\nBody text."));
        Assert.Equal("No heading here.", AiWords.DropLeadingSummary("No heading here."));
        Assert.Equal("# Topics\nBody.\n## Summary\nMore.", AiWords.DropLeadingSummary("# Topics\nBody.\n## Summary\nMore."));
    }

    [Fact]
    public void Bylines_read_the_designs_way()
    {
        Assert.Equal("Written by Ollama · Tue 11:52", AiWords.WrittenByline("Ollama", new DateTime(2025, 9, 23, 11, 52, 0).ToString("o")));
        Assert.Equal("Claude Code · just now", AiWords.DraftByline("Claude Code", DateTime.Now.ToString("o"), DateTime.Now));
    }

    [Theory]
    [InlineData("engine_offline", "Engine offline", "power_off", "Accent", "err")]
    [InlineData("not_signed_in", "Not signed in", "person_off", "Warn", "warn")]
    [InlineData("model_missing", "Model missing", "download", "Warn", "warn")]
    [InlineData("usage_limit", "Usage limit", "hourglass_top", "Fg2", "info")]
    [InlineData("fell_back", "Fell back", "swap_horiz", "Fg2", "info")]
    [InlineData("access_request", "Access request", "key", "Fg2", "info")]
    [InlineData("library_offline", "Library offline", "cloud_off", "Warn", "err")]
    [InlineData("rewrite_failed", "Rewrite failed", "error", "Warn", "err")]
    public void Every_problem_kind_has_its_caption_icon_colour_and_severity(string kind, string caption, string icon, string colour, string severity)
    {
        Assert.Equal(caption, AiWords.ProblemCaption(kind));
        Assert.Equal(icon, AiWords.ProblemIcon(kind));
        Assert.Equal(colour, AiWords.ProblemColorKey(kind));
        Assert.Equal(severity, AiWords.ProblemSeverity(kind));
    }

    [Fact]
    public void Problem_words_match_the_designs_exact_sentences()
    {
        Assert.Equal("Ollama isn't running on your library", AiWords.ProblemTitle("engine_offline", "Ollama", ""));
        Assert.Equal("New lectures wait and get their notes when it's back.", AiWords.ProblemMessage("engine_offline", "Ollama", "", "", 0, ""));

        Assert.Equal("Sign in to Codex on your library", AiWords.ProblemTitle("not_signed_in", "Codex", ""));
        Assert.Equal("Until then, questions go to Claude Code.", AiWords.ProblemMessage("not_signed_in", "Codex", "Claude Code", "", 0, ""));

        Assert.Equal("Ollama needs its notes model", AiWords.ProblemTitle("model_missing", "Ollama", ""));
        Assert.Equal("About 40 GB, downloaded once on your library.", AiWords.ProblemMessage("model_missing", "Ollama", "", "", 40, ""));
        Assert.Equal("Downloaded once on your library.", AiWords.ProblemMessage("model_missing", "Ollama", "", "", 0, ""));

        Assert.Equal("Claude Code hit its usage limit", AiWords.ProblemTitle("usage_limit", "Claude Code", ""));
        var until = DateTime.Today.AddHours(15);
        Assert.Equal("Questions go to Ollama until 3:00 PM.", AiWords.ProblemMessage("usage_limit", "Claude Code", "Ollama", until.ToString("o"), 0, ""));

        Assert.Equal("This answer came from Ollama", AiWords.ProblemTitle("fell_back", "Ollama", ""));

        Assert.Equal("Codex wants to read your library", AiWords.ProblemTitle("access_request", "Codex", ""));
        Assert.Equal("From Eli's MacBook. It can read, not change.", AiWords.ProblemMessage("access_request", "Codex", "", "", 0, "Eli's MacBook"));

        Assert.Equal("Your library isn't answering", AiWords.ProblemTitle("library_offline", "", ""));
        Assert.Equal("Engines run on your library. Check it's on and connected.", AiWords.ProblemMessage("library_offline", "", "", "", 0, ""));

        Assert.Equal("Claude Code couldn't rewrite the notes", AiWords.ProblemTitle("rewrite_failed", "Claude Code", ""));
        Assert.Equal("Claude Code hit its usage limit. Your current notes are unchanged.", AiWords.ProblemMessage("rewrite_failed", "Claude Code", "", "", 0, "Claude Code hit its usage limit."));
    }

    [Fact]
    public void Row_actions_match_the_design()
    {
        Assert.Equal("Start Ollama", AiWords.ProblemAction("engine_offline"));
        Assert.Equal("Sign in", AiWords.ProblemAction("not_signed_in"));
        Assert.Equal("Download", AiWords.ProblemAction("model_missing"));
        Assert.Equal("Dismiss", AiWords.ProblemAction("usage_limit"));
        Assert.Equal("", AiWords.ProblemAction("fell_back"));
        Assert.Equal("Try again", AiWords.ProblemAction("library_offline"));
        Assert.Equal("Try again", AiWords.ProblemAction("rewrite_failed"));
    }
}

public class AiNotesModelTests
{
    static readonly NotesVersion Current = new("# Summary\n\nBody text.", "Ollama", new DateTime(2025, 9, 23, 11, 52, 0).ToString("o"));

    static (AiNotesModel Model, FakeAiLibrary Library) Loaded(RewriteInfo? job = null)
    {
        var lib = new FakeAiLibrary { Overview = AiTestData.MixedOverview(), OnRewrite = _ => job ?? new RewriteInfo("lec-1", "none") { Current = Current } };
        // A delay that never fires: these tests check the state right after one call, not a poll picking up a
        // later change — Polling_picks_up_a_job_that_finishes_while_watching below builds its own instant one.
        var model = new AiNotesModel(lib) { Delay = (_, ct) => Task.Delay(Timeout.InfiniteTimeSpan, ct) };
        return (model, lib);
    }

    [AvaloniaFact]
    public async Task Loading_shows_the_current_notes_and_the_menu_checks_the_writer_last()
    {
        var (model, _) = Loaded();

        await model.Load("lec-1", Current.Markdown, "Ollama", Current.At);

        Assert.Equal("Written by Ollama · Tue 11:52", model.CurrentByline);
        Assert.Equal("Body text.", model.ShownMarkdown);
        Assert.Equal(RewriteState.Idle, model.State);
        Assert.True(model.ShowRewriteButton);
        Assert.Equal(["claude", "codex", "ollama"], model.Menu.Items.Select(i => i.Id));
        Assert.True(model.Menu.Items.Single(i => i.Id == "ollama").Checked);
        Assert.Equal("Wrote the current notes", model.Menu.Items.Single(i => i.Id == "ollama").Subtitle);
        Assert.False(model.Menu.Items.Single(i => i.Id == "codex").Enabled);
    }

    [AvaloniaFact]
    public async Task Picking_an_engine_in_the_menu_starts_a_rewrite_and_keeps_the_old_notes()
    {
        var (model, lib) = Loaded();
        await model.Load("lec-1", Current.Markdown, "Ollama", Current.At);
        lib.OnRewriteStart = (id, engine) => new RewriteInfo(id, "working") { Engine = engine, EngineName = "Claude Code", Current = Current };

        await ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)model.Menu.Items.Single(i => i.Id == "claude").Command).ExecuteAsync(null);

        Assert.Contains("rewrite-start:lec-1:claude", lib.Calls);
        Assert.Equal(RewriteState.Rewriting, model.State);
        Assert.False(model.MenuOpen);
        Assert.Equal("Rewriting with Claude Code…", model.RewritingLead);
        Assert.Equal("Body text.", model.ShownMarkdown); // unchanged: still the current notes
    }

    [AvaloniaFact]
    public async Task Polling_picks_up_a_job_that_finishes_while_watching()
    {
        var draft = new NotesVersion("# Summary\n\nNew body.", "Claude Code", DateTime.Now.ToString("o"));
        int calls = 0;
        var lib = new FakeAiLibrary
        {
            Overview = AiTestData.MixedOverview(),
            OnRewrite = _ => ++calls == 1
                ? new RewriteInfo("lec-1", "working") { Engine = "claude", EngineName = "Claude Code", Current = Current }
                : new RewriteInfo("lec-1", "ready") { Engine = "claude", EngineName = "Claude Code", Current = Current, Draft = draft },
        };
        var model = new AiNotesModel(lib) { Delay = (_, _) => Task.CompletedTask };
        await model.Load("lec-1", Current.Markdown, "Ollama", Current.At);

        for (int i = 0; i < 50 && model.State != RewriteState.Ready; i++) await Task.Delay(10);

        Assert.Equal(RewriteState.Ready, model.State);
        Assert.Equal("New body.", model.ShownMarkdown);
        Assert.Equal("Claude Code · just now", model.ShownByline);
        model.Dispose();
    }

    [AvaloniaFact]
    public async Task Keep_old_drops_the_draft_and_leaves_the_current_notes()
    {
        var draft = new NotesVersion("# Summary\n\nNew body.", "Claude Code", DateTime.Now.ToString("o"));
        var (model, lib) = Loaded(new RewriteInfo("lec-1", "ready") { Engine = "claude", EngineName = "Claude Code", Current = Current, Draft = draft });
        await model.Load("lec-1", Current.Markdown, "Ollama", Current.At);
        lib.OnRewriteKeep = _ => new RewriteInfo("lec-1", "none") { Current = Current };

        await model.KeepOldCommand.ExecuteAsync(null);

        Assert.Contains("rewrite-keep:lec-1", lib.Calls);
        Assert.Equal(RewriteState.Idle, model.State);
        Assert.Equal("Body text.", model.ShownMarkdown);
    }

    [AvaloniaFact]
    public async Task Use_new_saves_the_draft_and_says_so()
    {
        var draft = new NotesVersion("# Summary\n\nNew body.", "Claude Code", DateTime.Now.ToString("o"));
        var (model, lib) = Loaded(new RewriteInfo("lec-1", "ready") { Engine = "claude", EngineName = "Claude Code", Current = Current, Draft = draft });
        await model.Load("lec-1", Current.Markdown, "Ollama", Current.At);
        lib.OnRewriteUse = _ => new RewriteInfo("lec-1", "none") { Current = draft };
        bool changed = false;
        model.NotesChanged += () => changed = true;

        await model.UseNewCommand.ExecuteAsync(null);

        Assert.Contains("rewrite-use:lec-1", lib.Calls);
        Assert.True(changed);
        Assert.Equal("New body.", model.ShownMarkdown);
        Assert.Equal(RewriteState.Idle, model.State);
    }

    [AvaloniaFact]
    public async Task Compare_shows_both_and_the_current_notes_never_change_until_use()
    {
        var draft = new NotesVersion("# Summary\n\nNew body.", "Claude Code", DateTime.Now.ToString("o"));
        var (model, _) = Loaded(new RewriteInfo("lec-1", "ready") { Engine = "claude", EngineName = "Claude Code", Current = Current, Draft = draft });
        await model.Load("lec-1", Current.Markdown, "Ollama", Current.At);

        model.CompareCommand.Execute(null);

        Assert.Equal(RewriteState.Comparing, model.State);
        Assert.Equal("Body text.", model.CurrentBody);
        Assert.Equal("New body.", model.DraftBody);

        model.BackFromCompareCommand.Execute(null);
        Assert.Equal(RewriteState.Ready, model.State);
    }

    [AvaloniaFact]
    public async Task Cancel_stops_a_running_rewrite_and_the_notes_stay()
    {
        var (model, lib) = Loaded();
        await model.Load("lec-1", Current.Markdown, "Ollama", Current.At);
        lib.OnRewriteStart = (id, engine) => new RewriteInfo(id, "working") { Engine = engine, EngineName = "Claude Code", Current = Current };
        await ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)model.Menu.Items.Single(i => i.Id == "claude").Command).ExecuteAsync(null);
        lib.OnRewriteCancel = _ => new RewriteInfo("lec-1", "cancelled") { Current = Current };

        await model.CancelCommand.ExecuteAsync(null);

        Assert.Contains("rewrite-cancel:lec-1", lib.Calls);
        Assert.Equal(RewriteState.Idle, model.State);
    }

    [AvaloniaFact]
    public async Task A_failed_rewrite_says_why_and_try_again_uses_the_same_engine()
    {
        var (model, lib) = Loaded(new RewriteInfo("lec-1", "failed") { Engine = "claude", EngineName = "Claude Code", Error = "Claude Code hit its usage limit.", Current = Current });
        await model.Load("lec-1", Current.Markdown, "Ollama", Current.At);

        Assert.True(model.IsFailed);
        Assert.Equal("Claude Code couldn't rewrite the notes", model.FailedTitle);
        Assert.Equal("Claude Code hit its usage limit. Your current notes are unchanged.", model.FailedMessage);

        lib.OnRewriteStart = (id, engine) => new RewriteInfo(id, "working") { Engine = engine, EngineName = "Claude Code", Current = Current };
        await model.TryAgainCommand.ExecuteAsync(null);

        Assert.Contains("rewrite-start:lec-1:claude", lib.Calls);
        Assert.Equal(RewriteState.Rewriting, model.State);
    }

    [AvaloniaFact]
    public async Task Dismiss_clears_a_failed_state_back_to_idle()
    {
        var (model, _) = Loaded(new RewriteInfo("lec-1", "failed") { Engine = "claude", EngineName = "Claude Code", Error = "boom", Current = Current });
        await model.Load("lec-1", Current.Markdown, "Ollama", Current.At);

        model.DismissCommand.Execute(null);

        Assert.Equal(RewriteState.Idle, model.State);
        Assert.Equal("", model.Error);
    }

    [AvaloniaFact]
    public async Task A_refusal_to_start_is_a_passing_word_not_a_card()
    {
        var (model, lib) = Loaded();
        await model.Load("lec-1", Current.Markdown, "Ollama", Current.At);
        lib.OnRewriteStart = (_, _) => throw new LibraryRefusedException(409, "a rewrite is already running for this lecture.");

        await model.RewriteCommand.ExecuteAsync("claude");

        Assert.Equal("a rewrite is already running for this lecture.", model.Say);
        Assert.Equal(RewriteState.Idle, model.State);
    }

    [AvaloniaFact]
    public async Task An_older_library_says_so()
    {
        var model = new AiNotesModel(new FakeAiLibrary { Overview = null });

        await model.Load("lec-1", Current.Markdown, "Ollama", Current.At);

        Assert.True(model.OlderLibrary);
    }

    [AvaloniaFact]
    public async Task A_library_that_cant_be_reached_says_offline()
    {
        var model = new AiNotesModel(new FakeAiLibrary { OnEngines = () => throw new HttpRequestException("down") });

        await model.Load("lec-1", Current.Markdown, "Ollama", Current.At);

        Assert.True(model.Offline);
    }
}

public class AiProblemsModelTests
{
    [AvaloniaFact]
    public async Task Loading_builds_one_card_per_server_problem()
    {
        var overview = AiTestData.MixedOverview() with
        {
            Problems = [new AiProblemInfo("p1", "engine_offline", "ollama", "Ollama"), new AiProblemInfo("p2", "usage_limit", "claude", "Claude Code") { FallbackTo = "ollama", Until = DateTime.Today.AddHours(15).ToString("o") }],
        };
        var model = new AiProblemsModel(new FakeAiLibrary { Overview = overview });

        await model.Load();

        Assert.Equal(["p1", "p2"], model.Problems.Select(p => p.Id));
        var offline = model.Problems.Single(p => p.Id == "p1");
        Assert.Equal("Engine offline", offline.Caption);
        Assert.Equal("Ollama isn't running on your library", offline.Title);
        Assert.Equal("Start Ollama", offline.PrimaryWords);
        var limit = model.Problems.Single(p => p.Id == "p2");
        Assert.Equal("Questions go to Ollama until 3:00 PM.", limit.Message);
    }

    [AvaloniaFact]
    public async Task An_engine_offline_cards_action_starts_it()
    {
        var overview = AiTestData.MixedOverview() with { Problems = [new AiProblemInfo("p1", "engine_offline", "ollama", "Ollama")] };
        var lib = new FakeAiLibrary { Overview = overview };
        var model = new AiProblemsModel(lib);
        await model.Load();

        await model.Problems.Single().PrimaryCommand!.ExecuteAsync(null);

        Assert.Contains("start:ollama", lib.Calls);
    }

    [AvaloniaFact]
    public async Task Closing_a_server_problem_dismisses_it_on_the_library()
    {
        var overview = AiTestData.MixedOverview() with { Problems = [new AiProblemInfo("p1", "usage_limit", "claude", "Claude Code")] };
        var lib = new FakeAiLibrary { Overview = overview };
        var model = new AiProblemsModel(lib);
        await model.Load();

        await model.Problems.Single().CloseCommand!.ExecuteAsync(null);

        Assert.Contains("dismiss:p1", lib.Calls);
        Assert.Empty(model.Problems);
    }

    [AvaloniaFact]
    public void A_fell_back_answer_has_no_action_only_a_close()
    {
        var model = new AiProblemsModel(new FakeAiLibrary());

        model.AddFellBack("turn-1", "Ollama", "Claude Code didn't respond in time.");

        var p = model.Problems.Single();
        Assert.Equal("This answer came from Ollama", p.Title);
        Assert.Equal("Claude Code didn't respond in time.", p.Message);
        Assert.Equal("", p.PrimaryWords);
        Assert.True(p.CanClose);

        model.AddFellBack("turn-1", "Ollama", "again"); // the same id never duplicates
        Assert.Single(model.Problems);
    }

    [AvaloniaFact]
    public async Task Library_offline_and_rewrite_failed_call_their_own_retry()
    {
        var model = new AiProblemsModel(new FakeAiLibrary());
        bool retriedOffline = false, retriedRewrite = false;

        model.AddLibraryOffline(() => { retriedOffline = true; return Task.CompletedTask; });
        model.AddRewriteFailed("lec-1", "Claude Code", "boom", () => { retriedRewrite = true; return Task.CompletedTask; });

        await model.Problems.Single(p => p.Kind == "library_offline").PrimaryCommand!.ExecuteAsync(null);
        await model.Problems.Single(p => p.Kind == "rewrite_failed").PrimaryCommand!.ExecuteAsync(null);

        Assert.True(retriedOffline);
        Assert.True(retriedRewrite);
        Assert.Equal("Claude Code couldn't rewrite the notes", model.Problems.Single(p => p.Kind == "rewrite_failed").Title);
        Assert.Equal("boom Your current notes are unchanged.", model.Problems.Single(p => p.Kind == "rewrite_failed").Message);
    }

    [AvaloniaFact]
    public void An_access_request_offers_allow_and_deny()
    {
        var model = new AiProblemsModel(new FakeAiLibrary());
        bool allowed = false, denied = false;

        model.AddAccessRequest("req-1", "Codex", "Eli's MacBook", () => { allowed = true; return Task.CompletedTask; }, () => { denied = true; return Task.CompletedTask; });

        var p = model.Problems.Single();
        Assert.Equal("Codex wants to read your library", p.Title);
        Assert.Equal("From Eli's MacBook. It can read, not change.", p.Message);
        Assert.True(p.PrimaryIsAccent);

        p.SecondaryCommand!.Execute(null);
        Assert.True(denied);
        p.PrimaryCommand!.Execute(null);
        Assert.True(allowed);
    }
}
