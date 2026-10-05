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
    [InlineData("gemini", "", "gemini-2.5-flash")]
    [InlineData("gemini", "gemini-3.1-pro-high", "gemini-2.5-pro")]
    public void The_CLIs_model_names_map_to_API_models(string id, string picked, string model) =>
        Assert.Equal(model, ApiKeys.Model(id, picked));

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
