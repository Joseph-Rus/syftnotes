using StudyStash.Core.Ai;

namespace StudyStash.Core.Tests;

/// <summary>Guided setup's table of AIs: their makers' installers, their versions and flags, whether they're signed
/// in, and what an installer's words mean.</summary>
public class AgentCliTests
{
    [Fact]
    public void Each_installer_is_its_makers_documented_one_liner_over_https_for_this_account_alone()
    {
        Assert.Equal("curl -fsSL https://claude.ai/install.sh | bash", AgentCli.Claude.MacInstaller.Line);
        Assert.Equal("irm https://claude.ai/install.ps1 | iex", AgentCli.Claude.WindowsInstaller.Line);
        Assert.Equal("curl -fsSL https://chatgpt.com/codex/install.sh | sh", AgentCli.Codex.MacInstaller.Line);
        Assert.Equal("irm https://chatgpt.com/codex/install.ps1 | iex", AgentCli.Codex.WindowsInstaller.Line);
        foreach (var cli in AgentCli.All)
            foreach (var i in new[] { cli.MacInstaller, cli.WindowsInstaller })
            {
                // The documented line is run exactly as written (a Mac adds only pipefail in front of it).
                Assert.EndsWith(i.Line, i.Args[^1]);
                Assert.DoesNotContain(i.Args, a => a.Contains("sudo", StringComparison.OrdinalIgnoreCase) || a.Contains("RunAs", StringComparison.OrdinalIgnoreCase));
                Assert.Contains($"https://{cli.Site}/", i.Line);
                Assert.StartsWith("https://", i.DocUrl);
            }
        Assert.Equal("set -o pipefail; curl -fsSL https://claude.ai/install.sh | bash", AgentCli.Claude.MacInstaller.Args[^1]);
        Assert.Equal("1", AgentCli.Codex.MacInstaller.Env["CODEX_NON_INTERACTIVE"]);
        Assert.Equal("1", AgentCli.Codex.WindowsInstaller.Env["CODEX_NON_INTERACTIVE"]);
        Assert.Empty(AgentCli.Claude.MacInstaller.Env);
    }

    [Fact]
    public void The_plans_and_sign_in_commands_are_the_providers_own()
    {
        Assert.Equal(("Pro or Max", "https://claude.com/pricing"), (AgentCli.Claude.Plans, AgentCli.Claude.PlansUrl));
        Assert.Equal(("Plus or higher", "https://chatgpt.com/pricing"), (AgentCli.Codex.Plans, AgentCli.Codex.PlansUrl));
        Assert.Equal("claude auth login", AgentCli.Claude.SignInLine);
        Assert.Equal("codex login", AgentCli.Codex.SignInLine);
        Assert.Equal(["auth", "status"], AgentCli.Claude.StatusArgs);
        Assert.Equal(["login", "status"], AgentCli.Codex.StatusArgs);
        Assert.Same(AgentCli.Codex, AgentCli.Get("codex"));
        Assert.Same(AgentCli.Claude, AgentCli.Get("claude"));
    }

    [Fact]
    public void Versions_are_read_from_what_each_cli_prints_and_newer_flags_wait_for_theirs()
    {
        Assert.Equal(new Version(2, 1, 211), AgentCli.ParseVersion("2.1.211 (Claude Code)"));
        Assert.Equal(new Version(0, 46, 0), AgentCli.ParseVersion("codex-cli 0.46.0"));
        Assert.Null(AgentCli.ParseVersion("command not found"));
        Assert.False(AgentCli.Has(new Version(2, 1, 247), AgentCli.RestrictedSince));
        Assert.True(AgentCli.Has(new Version(2, 1, 248), AgentCli.RestrictedSince));
        Assert.False(AgentCli.Has(new Version(2, 1, 258), AgentCli.NoPromptsSince));
        Assert.True(AgentCli.Has(new Version(2, 2, 0), AgentCli.NoPromptsSince));
        Assert.False(AgentCli.Has(null, AgentCli.RestrictedSince));
    }

    [Fact]
    public void Signed_in_is_the_status_commands_exit_code_and_nothing_personal_is_kept()
    {
        var claude = AgentCli.ReadStatus("claude", new ProcResult(0,
            """{"loggedIn":true,"authMethod":"claude.ai","subscriptionType":"max","email":"someone@example.com","orgId":"org-1"}"""));
        Assert.Equal(new AgentSignedIn(true, "claude.ai", "max"), claude);
        Assert.DoesNotContain("example.com", claude.ToString());
        Assert.False(AgentCli.ReadStatus("claude", new ProcResult(1, """{"loggedIn":false}""")).SignedIn);
        Assert.False(AgentCli.ReadStatus("claude", new ProcResult(0, """{"loggedIn":false}""")).SignedIn);
        Assert.True(AgentCli.ReadStatus("claude", new ProcResult(0, "Logged in")).SignedIn);
        Assert.False(AgentCli.ReadStatus("claude", null).SignedIn);
        // Codex: only the exit code; its words (which can show part of a key) aren't kept.
        var codex = AgentCli.ReadStatus("codex", new ProcResult(0, "Logged in using an API key - sk-proj-***abc"));
        Assert.Equal(new AgentSignedIn(true), codex);
        Assert.False(AgentCli.ReadStatus("codex", new ProcResult(1, "Not logged in")).SignedIn);
    }

    [Fact]
    public void An_installers_failure_is_sorted_into_offline_region_memory_or_other()
    {
        Assert.Equal(InstallFailure.Offline, AgentCli.Classify(6, ["curl: (6) Could not resolve host: claude.ai"]));
        Assert.Equal(InstallFailure.Offline, AgentCli.Classify(1, ["irm : The remote name could not be resolved: 'claude.ai'"]));
        Assert.Equal(InstallFailure.Offline, AgentCli.Classify(1, ["irm : No such host is known."]));
        Assert.Equal(InstallFailure.Region, AgentCli.Classify(1,
            ["Failed to get a valid version from downloads.claude.ai (got unexpected content).",
             "This can happen if the download service is unreachable or not available in your region - see https://www.anthropic.com/supported-countries"]));
        Assert.Equal(InstallFailure.Memory, AgentCli.Classify(137, ["Setting up Claude Code..."]));
        Assert.Equal(InstallFailure.Memory, AgentCli.Classify(1,
            ["\u001b[0;31mInstallation was killed before it could finish (exit code 137). This usually means the system ran out of memory.\u001b[0m"]));
        Assert.Equal(InstallFailure.Other, AgentCli.Classify(1, ["Checksum verification failed"]));
        Assert.Equal("Checksum verification failed", AgentCli.LastWords(["Downloading", "Checksum verification failed.", ""]));
        Assert.Equal("", AgentCli.LastWords([]));
    }

    [Fact]
    public void The_installers_own_lines_become_setups_words_for_where_it_has_got_to()
    {
        Assert.Equal("Setting it up…", AgentCli.PhaseOf(AgentCli.Claude, "Setting up Claude Code..."));
        Assert.Null(AgentCli.PhaseOf(AgentCli.Claude, "✅ Installation complete!"));
        Assert.Equal("Downloading Codex…", AgentCli.PhaseOf(AgentCli.Codex, "==> Downloading Codex CLI"));
        Assert.Equal("Setting it up…", AgentCli.PhaseOf(AgentCli.Codex, "==> Installing standalone package to /x"));
        Assert.Null(AgentCli.PhaseOf(AgentCli.Codex, "==> Resolved version: 0.46.0"));
        Assert.Equal("red words", AgentCli.Plain("\u001b[0;31mred words\u001b[0m\r"));
    }

    [Fact]
    public void Codexs_own_windows_folder_is_searched()
    {
        if (!OperatingSystem.IsWindows()) return;
        string codexBin = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "OpenAI", "Codex", "bin");
        Assert.Contains(codexBin, AiProvider.SearchPath().Split(Path.PathSeparator), StringComparer.OrdinalIgnoreCase);
    }
}
