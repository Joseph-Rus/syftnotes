using System.Net;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using StudyStash.Core.Setup;
using StudyStash.Library;

namespace StudyStash.Core.Tests;

/// <summary>Guided setup's tools, called the way Claude Code or Codex calls them: over HTTP to the setup window's own
/// door on 127.0.0.1, with its token, against a setup window that only writes down what it was asked.</summary>
public sealed class SetupToolsTests : IAsyncLifetime
{
    readonly FakeSetupDriver driver = new();
    SetupTools tools = null!;
    SetupMcpHost host = null!;

    public async Task InitializeAsync()
    {
        tools = new SetupTools(driver);
        host = await SetupMcpHost.StartAsync(tools);
    }

    public async Task DisposeAsync() => await host.DisposeAsync();

    Task<McpClient> Connect(string? token = null) =>
        McpClient.CreateAsync(new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri(host.Url), TransportMode = HttpTransportMode.StreamableHttp,
            AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = "Bearer " + (token ?? host.Token) },
        }));

    static string Text(CallToolResult r) => string.Concat(r.Content.OfType<TextContentBlock>().Select(c => c.Text));

    static async Task<(bool Error, string Text)> Call(McpClient mcp, string tool, Dictionary<string, object?>? args = null)
    {
        var r = await mcp.CallToolAsync(tool, args ?? []);
        return (r.IsError == true, Text(r));
    }

    [Fact]
    public async Task The_door_is_on_this_computer_alone_with_a_long_random_token()
    {
        Assert.StartsWith("http://127.0.0.1:", host.Url);
        Assert.EndsWith("/setup-mcp", host.Url);
        Assert.True(host.Token.Length >= 43, "32 random bytes");
        await using var other = await SetupMcpHost.StartAsync(new SetupTools(new FakeSetupDriver()));
        Assert.NotEqual(host.Token, other.Token);
        Assert.NotEqual(host.Port, other.Port);
    }

    [Fact]
    public async Task Every_tool_is_there_and_each_is_read_card_or_direct()
    {
        await using var mcp = await Connect();
        Assert.Equal("study_stash_setup", mcp.ServerInfo.Name);
        var names = (await mcp.ListToolsAsync()).Select(t => t.Name).ToList();
        Assert.Equal(18, names.Count);
        foreach (string n in names) Assert.True(SetupTools.KindOf(n) is "read" or "card" or "direct", $"{n} isn't classified as read, card or direct");
        Assert.All(names, n => Assert.Matches("^[a-z_]+$", n));
        Assert.Contains("get_setup_status", names);
        Assert.Contains("offer_finish", names);
        Assert.Equal(10, names.Count(n => n.StartsWith("offer_", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task The_status_is_the_truth_in_plain_words_with_no_secrets()
    {
        driver.Status = driver.Status with { Addresses = ["http://mac-mini.local:8787"] };
        await using var mcp = await Connect();
        var (error, text) = await Call(mcp, "get_setup_status");
        Assert.False(error);
        Assert.Contains("Computer: Mac", text);
        Assert.Contains("How it's used: just this Mac", text);
        Assert.Contains("- microphone · Microphone: to do", text);
        Assert.Contains("- canvas · Canvas: to do (optional)", text);
        Assert.Contains("- start_at_login · Start at login: to do (recommended)", text);
        Assert.Contains("ready_to_finish: no", text);
        Assert.Contains("Still to do: Microphone, Transcription model, Start at login", text);
        Assert.Contains("http://mac-mini.local:8787", text);
    }

    [Fact]
    public async Task Each_offer_only_shows_its_card_and_changes_nothing()
    {
        driver.Status = driver.Status with { Windows = true, ReadyToFinish = true, Items = [] };
        driver.Courses = new SetupCourses(true, [("Intro to Biology", "BIO 110"), ("CS 101", "CS 101")]);
        await using var mcp = await Connect();
        string[] offers = ["offer_computer_setup", "offer_microphone_check", "offer_course_picker", "offer_start_at_login", "offer_taskbar_tip", "offer_finish"];
        foreach (string o in offers)
        {
            tools.NewTurn();
            var (error, text) = await Call(mcp, o);
            Assert.False(error, $"{o}: {text}");
            Assert.Contains("wait for the student", text);
        }
        tools.NewTurn();
        Assert.False((await Call(mcp, "offer_model_download", new() { ["model_id"] = "large-v3-turbo-q5" })).Error);
        Assert.False((await Call(mcp, "offer_chrome_helper", new() { ["school_address"] = "school.instructure.com" })).Error);

        Assert.Equal(["computer_setup", "microphone_check", "course_picker", "start_at_login", "taskbar_tip", "finish", "model_download", "chrome_helper"],
            driver.Cards.Select(c => c.Kind));
        Assert.Equal("large-v3-turbo-q5", driver.Cards[6].Arg);
        Assert.Equal("school.instructure.com", driver.Cards[7].Arg);
        Assert.Equal(0, driver.MachineChanges);
    }

    [Fact]
    public async Task A_laptop_and_a_library_get_their_own_cards_only()
    {
        await using var mcp = await Connect();
        Assert.True((await Call(mcp, "offer_library_password")).Error);
        Assert.True((await Call(mcp, "offer_library_connection")).Error);
        driver.Status = driver.Status with { Role = "laptop" };
        Assert.False((await Call(mcp, "offer_library_connection", new() { ["address"] = "mac-mini:8787" })).Error);
        Assert.Equal(("library_connection", "mac-mini:8787"), (driver.Cards[^1].Kind, driver.Cards[^1].Arg));
        Assert.Contains("A laptop doesn't need", (await Call(mcp, "offer_start_at_login")).Text);
        driver.Status = driver.Status with { Role = "library" };
        Assert.False((await Call(mcp, "offer_library_password")).Error);
        Assert.Contains("doesn't record", (await Call(mcp, "offer_microphone_check")).Text);
        Assert.Contains("doesn't need a model", (await Call(mcp, "offer_model_download", new() { ["model_id"] = "large-v3-turbo-q5" })).Text);
        Assert.Contains("only for a Windows PC", (await Call(mcp, "offer_taskbar_tip")).Text);
        driver.Status = driver.Status with { Role = "" };
        Assert.Contains("offer_computer_setup", (await Call(mcp, "offer_microphone_check")).Text);
    }

    [Fact]
    public async Task Run_again_never_changes_what_the_computer_is_for()
    {
        driver.Status = driver.Status with { Again = true };
        await using var mcp = await Connect();
        var (error, text) = await Call(mcp, "offer_computer_setup", new() { ["suggested"] = "laptop" });
        Assert.True(error);
        Assert.Contains("Settings → Connection", text);
        Assert.Empty(driver.Cards);
    }

    [Fact]
    public async Task An_unknown_model_or_finishing_too_early_is_refused_in_words()
    {
        await using var mcp = await Connect();
        var model = await Call(mcp, "offer_model_download", new() { ["model_id"] = "whisper-9000" });
        Assert.True(model.Error);
        Assert.Contains("large-v3-turbo-q5", model.Text);
        var finish = await Call(mcp, "offer_finish");
        Assert.True(finish.Error);
        Assert.Contains("Microphone, Transcription model, Start at login", finish.Text);
        Assert.Empty(driver.Cards);
    }

    [Fact]
    public async Task A_card_mid_action_holds_off_the_next()
    {
        driver.Status = driver.Status with { Busy = "connecting to the library" };
        await using var mcp = await Connect();
        var (error, text) = await Call(mcp, "offer_microphone_check");
        Assert.True(error);
        Assert.Contains("connecting to the library", text);
        Assert.Empty(driver.Cards);
    }

    [Fact]
    public async Task Courses_wait_for_the_browser()
    {
        await using var mcp = await Connect();
        Assert.Contains("browser isn't connected yet", (await Call(mcp, "get_canvas_courses")).Text);
        Assert.True((await Call(mcp, "offer_course_picker")).Error);
        driver.Courses = new SetupCourses(true, [("Intro to Biology", "BIO 110")]);
        Assert.Contains("- Intro to Biology (BIO 110)", (await Call(mcp, "get_canvas_courses")).Text);
    }

    [Fact]
    public async Task Twelve_calls_a_turn_then_it_says_to_ask_the_student()
    {
        await using var mcp = await Connect();
        for (int i = 0; i < SetupTools.MaxCallsPerTurn; i++) Assert.False((await Call(mcp, "get_setup_status")).Error);
        var (error, text) = await Call(mcp, "get_setup_status");
        Assert.True(error);
        Assert.Equal("Too many steps at once: ask the student first.", text);
        tools.NewTurn();
        Assert.False((await Call(mcp, "get_setup_status")).Error);
    }

    [Fact]
    public async Task Arguments_past_their_limits_are_refused_before_anything_shows()
    {
        await using var mcp = await Connect();
        Assert.True((await Call(mcp, "ask_student", new() { ["question"] = new string('a', 301), ["choices"] = new[] { "Yes", "No" } })).Error);
        Assert.True((await Call(mcp, "ask_student", new() { ["question"] = "Canvas?", ["choices"] = new[] { "Yes" } })).Error);
        Assert.True((await Call(mcp, "ask_student", new() { ["question"] = "Canvas?", ["choices"] = new[] { "Yes", new string('b', 41) } })).Error);
        Assert.Empty(driver.Questions);
        Assert.False((await Call(mcp, "ask_student", new() { ["question"] = "Does your school use Canvas?", ["choices"] = new[] { "Yes", "No", "Not sure" } })).Error);
        Assert.Equal(["Yes", "No", "Not sure"], driver.Questions.Single().Choices);

        tools.NewTurn();
        Assert.True((await Call(mcp, "add_class", new() { ["name"] = new string('c', 81) })).Error);
        Assert.True((await Call(mcp, "add_class", new() { ["name"] = "BIO 110", ["about"] = new string('d', 301) })).Error);
        Assert.True((await Call(mcp, "offer_chrome_helper", new() { ["school_address"] = "rm -rf ~" })).Error);
        Assert.True((await Call(mcp, "offer_computer_setup", new() { ["suggested"] = "server" })).Error);
        Assert.True((await Call(mcp, "set_notes_writer", new() { ["engine"] = "gemini" })).Error);
        Assert.True((await Call(mcp, "skip_step", new() { ["step"] = "microphone" })).Error);
        Assert.Empty(driver.Classes);
        Assert.Empty(driver.Cards);
        Assert.Empty(driver.Writers);
        Assert.Empty(driver.Skipped);
    }

    [Fact]
    public async Task Classes_writers_and_skips_go_straight_to_the_library()
    {
        await using var mcp = await Connect();
        Assert.Contains("Added BIO 110", (await Call(mcp, "add_class", new() { ["name"] = "BIO 110", ["about"] = "Cells, genetics and evolution" })).Text);
        Assert.Contains("Claude writes the notes", (await Call(mcp, "set_notes_writer", new() { ["engine"] = "claude" })).Text);
        Assert.False((await Call(mcp, "skip_step", new() { ["step"] = "canvas" })).Error);
        Assert.False((await Call(mcp, "open_manual_setup", new() { ["step"] = "microphone" })).Error);
        Assert.Equal(("BIO 110", "Cells, genetics and evolution"), driver.Classes.Single());
        Assert.Equal(["claude"], driver.Writers);
        Assert.Equal(["canvas"], driver.Skipped);
        Assert.Equal(["microphone"], driver.Manual);

        driver.RefuseWriter = "Codex isn't signed in on this Mac.";
        var (error, text) = await Call(mcp, "set_notes_writer", new() { ["engine"] = "codex" });
        Assert.True(error);
        Assert.Equal("Codex isn't signed in on this Mac.", text);
    }

    [Fact]
    public async Task No_more_than_thirty_classes_in_one_setup()
    {
        await using var mcp = await Connect();
        for (int i = 0; i < SetupTools.MaxClasses; i++)
        {
            if (i % 10 == 0) tools.NewTurn();
            Assert.False((await Call(mcp, "add_class", new() { ["name"] = $"Class {i}" })).Error);
        }
        tools.NewTurn();
        Assert.Contains("Settings → Classes", (await Call(mcp, "add_class", new() { ["name"] = "One more" })).Text);
        Assert.Equal(SetupTools.MaxClasses, driver.Classes.Count);
    }

    // --- the door ---------------------------------------------------------------------------------------------------

    HttpRequestMessage Post(string? token, string? origin = null, string? hostHeader = null)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, host.Url)
        {
            Content = new StringContent("""{"jsonrpc":"2.0","id":1,"method":"tools/list"}""", System.Text.Encoding.UTF8, "application/json"),
        };
        req.Headers.Accept.ParseAdd("application/json");
        req.Headers.Accept.ParseAdd("text/event-stream");
        if (token is not null) req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        if (origin is not null) req.Headers.Add("Origin", origin);
        if (hostHeader is not null) req.Headers.Host = hostHeader;
        return req;
    }

    [Fact]
    public async Task Only_this_windows_token_from_this_computer_gets_in()
    {
        using var http = new HttpClient();
        Assert.Equal(HttpStatusCode.OK, (await http.SendAsync(Post(host.Token))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.SendAsync(Post(null))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.SendAsync(Post("not-the-token"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.SendAsync(Post(host.Token[..^1]))).StatusCode);
        // A web page (it always says where it came from), or a name that isn't 127.0.0.1 (a rebinding trick).
        Assert.Equal(HttpStatusCode.Forbidden, (await http.SendAsync(Post(host.Token, origin: "https://evil.example"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await http.SendAsync(Post(host.Token, origin: "null"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await http.SendAsync(Post(host.Token, hostHeader: $"localhost:{host.Port}"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await http.SendAsync(Post(host.Token, hostHeader: $"evil.example:{host.Port}"))).StatusCode);
        await Assert.ThrowsAnyAsync<Exception>(() => Connect("wrong"));
        Assert.Empty(driver.Cards);
    }

    [Fact]
    public async Task The_door_closes_with_the_window()
    {
        var own = await SetupMcpHost.StartAsync(new SetupTools(new FakeSetupDriver()));
        string url = own.Url;
        await own.DisposeAsync();
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        await Assert.ThrowsAnyAsync<HttpRequestException>(() => http.PostAsync(url, new StringContent("{}")));
    }
}
