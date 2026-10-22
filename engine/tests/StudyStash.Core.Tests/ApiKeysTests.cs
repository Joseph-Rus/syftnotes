using StudyStash.Core.Ai;

namespace StudyStash.Core.Tests;

/// <summary>API keys: kept for this account only, shown by their last four characters, and enough on their own to make
/// an engine usable on a computer without its command-line tool.</summary>
public class ApiKeysTests
{
    static string NewHome() => Directory.CreateTempSubdirectory("ss-keys-").FullName;

    [Fact]
    public void A_key_is_saved_for_this_account_only_and_shown_by_its_last_four()
    {
        string home = NewHome();
        Assert.False(ApiKeys.Has(home, "gemini"));
        ApiKeys.Set(home, "gemini", "  AIza-secret-1234 ");
        Assert.Equal("AIza-secret-1234", ApiKeys.Get(home, "gemini"));
        Assert.Equal("…1234", ApiKeys.Hint(home, "gemini"));
        if (!OperatingSystem.IsWindows())
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(ApiKeys.PathIn(home)));
        ApiKeys.Set(home, "gemini", "");
        Assert.False(ApiKeys.Has(home, "gemini"));
        Assert.Equal("", ApiKeys.Hint(home, "gemini"));
    }

    [Fact]
    public void Ollama_takes_no_key() => Assert.Throws<ArgumentException>(() => ApiKeys.Set(NewHome(), "ollama", "x"));

    [Theory]
    [InlineData("claude", "", "claude-sonnet-5-5")]
    [InlineData("claude", "opus", "claude-opus-5-5")]
    [InlineData("claude", "claude-haiku-4-5-20251001", "claude-haiku-4-5-20251001")]
    [InlineData("codex", "", "gpt-5")]
    // A new Gemini key can't use the 2.5 models: Flash 3.8 by default, Antigravity's names mapped to the API's.
    [InlineData("gemini", "", "gemini-3.8-flash")]
    [InlineData("gemini", "gemini-3.8-flash-medium", "gemini-3.8-flash")]
    [InlineData("gemini", "gemini-3.1-pro-high", "gemini-3.1-pro")]
    public void The_CLIs_model_names_map_to_API_models(string id, string picked, string model) =>
        Assert.Equal(model, ApiKeys.Model(id, picked));

    /// <summary>Stands in for Google's API: each model answers as told (a status and a body, or a few words of notes).</summary>
    sealed class FakeGemini(Dictionary<string, (int Status, string Body)> refusals) : HttpMessageHandler
    {
        public List<string> Asked { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            string model = request.RequestUri!.AbsolutePath.Split('/').Last().Split(':')[0];
            Asked.Add(model);
            if (refusals.TryGetValue(model, out var no))
                return Task.FromResult(new HttpResponseMessage((System.Net.HttpStatusCode)no.Status) { Content = new StringContent(no.Body) });
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("data: {\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"ready\"}]}}]}\n\n"),
            });
        }
    }

    static async Task<(List<string> Asked, string Text, string Note, string Error)> Ask(string picked, Dictionary<string, (int, string)> refusals)
    {
        var google = new FakeGemini(refusals);
        string text = "", note = "", error = "";
        await foreach (var e in ApiKeys.RunAsync("gemini", "AIza-test", new AiRequest("hi", "/tmp") { Model = picked }, http: new HttpClient(google)))
        {
            if (e.Kind == "text") text += e.Text;
            if (e.Kind == "note") note = e.Text;
            if (e.Kind == "error") error = e.Text;
        }
        return (google.Asked, text, note, error);
    }

    /// <summary>A new student's Gemini key: Flash's free daily limit moves the request to Flash-Lite, Pro without a paid
    /// key to Flash, and a model new keys can't use (2.5) to the default, each said for the byline; a key that's
    /// turned away isn't retried.</summary>
    [Fact]
    public async Task A_gemini_key_moves_to_the_model_it_can_use_and_says_so()
    {
        const string daily = """{"error":{"code":429,"message":"You exceeded your current quota. Quota exceeded for metric: generate_content_free_tier_requests, limit: 250","status":"RESOURCE_EXHAUSTED"}}""";
        var lite = await Ask("", new() { ["gemini-3.8-flash"] = (429, daily) });
        Assert.Equal(["gemini-3.8-flash", "gemini-3.5-flash-lite"], lite.Asked);
        Assert.Equal(("ready", "Gemini 3.5 Flash-Lite (Flash's free daily limit is used up)", ""), (lite.Text, lite.Note, lite.Error));

        var pro = await Ask("gemini-3.1-pro-high", new() { ["gemini-3.1-pro"] = (429, """{"error":{"message":"Quota exceeded: limit: 0. Gemini 3.1 Pro has no free tier: enable billing."}}""") });
        Assert.Equal(["gemini-3.1-pro", "gemini-3.8-flash"], pro.Asked);
        Assert.Equal("Gemini 3.8 Flash (Gemini 3.1 Pro needs a paid key)", pro.Note);

        var gone = await Ask("gemini-2.5-flash", new() { ["gemini-2.5-flash"] = (404, """{"error":{"message":"This model models/gemini-2.5-flash is no longer available to new users."}}""") });
        Assert.Equal(["gemini-2.5-flash", "gemini-3.8-flash"], gone.Asked);
        Assert.Equal(("ready", ""), (gone.Text, gone.Error));
        Assert.False(Engines.LooksLikeAuth(gone.Note)); // the model's problem, never the key's

        var bad = await Ask("", new() { ["gemini-3.8-flash"] = (400, """{"error":{"message":"API key not valid. Please pass a valid API key."}}""") });
        Assert.Equal(["gemini-3.8-flash"], bad.Asked);
        Assert.Equal("Gemini API: 400 API key not valid. Please pass a valid API key.", bad.Error);

        // The notes' byline says which model wrote them, and why.
        Assert.Equal("Gemini 3.5 Flash-Lite (Flash's free daily limit is used up)", Engines.WrittenBy("Gemini · " + lite.Note));
        Assert.Equal(("Gemini", "Ollama"), (Engines.WrittenBy("Gemini"), Engines.WrittenBy("qwen3:8b")));
    }

    [Fact]
    public async Task An_engine_with_a_key_and_no_command_is_installed_and_can_be_tried()
    {
        var cfg = new Config("/lib", "/lib");
        var checks = new FakeChecks().Build() with { HasKey = id => id == "gemini" };
        var row = (await Engines.StatusAsync(new AiSettings(), cfg, checks)).Engines.Single(e => e.Id == "gemini");
        Assert.True(row.Installed);
        Assert.True(row.HasKey);
        Assert.Equal("unchecked", row.State);
        Assert.Null(Engines.KnownUnusableWhy("gemini", new AiSettings(), checks));
    }
}
