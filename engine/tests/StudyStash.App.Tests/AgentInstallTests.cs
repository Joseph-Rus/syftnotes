using System.Diagnostics;
using StudyStash.App.Services;
using StudyStash.Core.Ai;
using StudyStash.Core.Tests;

namespace StudyStash.App.Tests;

/// <summary>Installing and signing in to an AI's CLI for guided setup, with fake installers and fake CLIs in a test's
/// own folder: never a real installer, account or the network.</summary>
public sealed class AgentInstallTests
{
    /// <summary>A fake CLI staged for the fake installer to copy, the folder it installs into (empty until it runs),
    /// and an installer that finds the CLI only there.</summary>
    static (AgentInstall Install, string Bin) Rig(TempHome home, AgentCliInfo cli, string mode)
    {
        string staged = home["staged"], bin = home["bin"];
        FakeAgents.WriteCli(staged, cli.Id);
        var installer = FakeAgents.Installer(home["installer"], cli.Id, mode, staged, bin);
        var install = new AgentInstall(cli, OperatingSystem.IsWindows(), home["logs/setup-install.log"], installer, FakeAgents.Which(bin))
        {
            Elevated = () => false,
        };
        return (install, bin);
    }

    [Fact]
    public async Task Nothing_is_installed_until_install_runs_and_then_it_works()
    {
        using var home = new TempHome();
        var (install, bin) = Rig(home, AgentCli.Claude, "ok");
        var phases = new List<string>();
        install.Phase += phases.Add;
        Assert.False(install.Find().Works);
        Assert.False(File.Exists(FakeAgents.ExePath(bin, "claude")));

        var done = await install.RunAsync(TestContext.Current.CancellationToken);

        Assert.True(done.Ok, done.Words);
        Assert.Equal("Claude Code 2.1.260 is installed.", done.Words);
        Assert.Equal(new Version(2, 1, 260), done.Found.Version);
        Assert.Equal(["Downloading Claude Code…", "Setting it up…", "Checking it works…"], phases);
        Assert.Contains("Setting up Claude Code...", install.Output);
        Assert.Contains("Setting up Claude Code...", File.ReadAllText(home["logs/setup-install.log"]));
        Assert.True(install.Find().Works);
    }

    [Fact]
    public async Task Codex_installs_the_same_way_and_says_its_own_steps()
    {
        using var home = new TempHome();
        var (install, _) = Rig(home, AgentCli.Codex, "ok");
        var phases = new List<string>();
        install.Phase += phases.Add;
        var done = await install.RunAsync(TestContext.Current.CancellationToken);
        Assert.True(done.Ok, done.Words);
        Assert.Equal("Codex 0.46.0 is installed.", done.Words);
        Assert.Equal("Downloading Codex…", phases[0]);
        Assert.Contains("Setting it up…", phases);
        Assert.Equal("Checking it works…", phases[^1]);
    }

    [Theory]
    [InlineData("offline", InstallFailure.Offline, "Syft Notes couldn't reach claude.ai. Check your internet connection, then try again.")]
    [InlineData("region", InstallFailure.Region, "Claude Code isn't offered in your country yet.")]
    [InlineData("other", InstallFailure.Other, "The installer stopped: Checksum verification failed.")]
    [InlineData("broken", InstallFailure.WontStart, "Claude Code installed but won't start.")]
    public async Task Each_way_an_install_fails_is_said_plainly(string mode, InstallFailure kind, string words)
    {
        using var home = new TempHome();
        var (install, _) = Rig(home, AgentCli.Claude, mode);
        var done = await install.RunAsync(TestContext.Current.CancellationToken);
        Assert.False(done.Ok);
        Assert.Equal(kind, done.Failure);
        Assert.Equal(words, done.Words);
    }

    [Fact]
    public async Task Running_out_of_memory_says_to_close_some_apps()
    {
        using var home = new TempHome();
        var (install, _) = Rig(home, AgentCli.Claude, "memory");
        var done = await install.RunAsync(TestContext.Current.CancellationToken);
        Assert.Equal(InstallFailure.Memory, done.Failure);
        Assert.Equal($"Your {(OperatingSystem.IsWindows() ? "PC" : "Mac")} ran out of memory while installing. Close some apps, then try again.", done.Words);
    }

    [Fact]
    public async Task An_app_running_as_administrator_installs_nothing()
    {
        using var home = new TempHome();
        var (rig, bin) = Rig(home, AgentCli.Claude, "ok");
        var install = new AgentInstall(AgentCli.Claude, OperatingSystem.IsWindows(), null,
            FakeAgents.Installer(home["installer"], "claude", "ok", home["staged"], bin), FakeAgents.Which(bin)) { Elevated = () => true };
        var done = await install.RunAsync(TestContext.Current.CancellationToken);
        Assert.Equal(InstallFailure.Elevated, done.Failure);
        Assert.False(File.Exists(FakeAgents.ExePath(bin, "claude")));
        Assert.False(rig.Find().Works);
    }

    [Fact]
    public async Task The_makers_real_installer_never_runs_in_a_test()
    {
        using var home = new TempHome();
        var real = new AgentInstall(AgentCli.Claude, OperatingSystem.IsWindows(), home["logs/setup-install.log"]) { Elevated = () => false };
        var done = await real.RunAsync(TestContext.Current.CancellationToken);
        Assert.False(done.Ok);
        Assert.Equal("Installing is off here.", done.Words);
        Assert.False(File.Exists(home["logs/setup-install.log"]));
    }

    static bool Alive(string pidFile)
    {
        // A pid file the fake installer is still writing can't be read yet on Windows (or is still empty): not started.
        string text;
        try { text = File.ReadAllText(pidFile).Trim(); }
        catch (IOException) { return false; }
        if (!int.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, out int pid)) return false;
        try
        {
            using var p = Process.GetProcessById(pid);
            return !p.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    [Fact]
    public async Task Cancelling_stops_the_installer_and_everything_it_started()
    {
        using var home = new TempHome();
        var (install, bin) = Rig(home, AgentCli.Claude, "slow");
        using var cts = new CancellationTokenSource();
        var running = install.RunAsync(cts.Token);
        for (int i = 0; i < 200 && !Alive(Path.Combine(bin, "child.pid")); i++) await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.True(Alive(Path.Combine(bin, "child.pid")), "the fake installer's child never started");
        await cts.CancelAsync();
        var done = await running;
        Assert.Equal(InstallFailure.Cancelled, done.Failure);
        for (int i = 0; i < 100 && (Alive(Path.Combine(bin, "child.pid")) || Alive(Path.Combine(bin, "installer.pid"))); i++)
            await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.False(Alive(Path.Combine(bin, "installer.pid")));
        Assert.False(Alive(Path.Combine(bin, "child.pid")));
    }

    [Fact]
    public async Task An_installer_that_takes_too_long_is_stopped_and_says_so()
    {
        using var home = new TempHome();
        var (rig, bin) = Rig(home, AgentCli.Claude, "slow");
        var install = new AgentInstall(AgentCli.Claude, OperatingSystem.IsWindows(), null,
            FakeAgents.Installer(home["installer"], "claude", "slow", home["staged"], bin), FakeAgents.Which(bin))
        {
            Elevated = () => false, Timeout = TimeSpan.FromSeconds(2),
        };
        var done = await install.RunAsync(TestContext.Current.CancellationToken);
        Assert.Equal(InstallFailure.TimedOut, done.Failure);
        Assert.Equal("The installer didn't finish within 0 minutes. Check your internet connection, then try again.", done.Words);
        Assert.False(rig.Find().Works);
    }

    [Fact]
    public async Task Signing_in_is_noticed_when_the_status_command_says_so()
    {
        using var home = new TempHome();
        string exe = FakeAgents.WriteCli(home["bin"], "claude");
        using var signIn = new AgentSignIn(AgentCli.Claude, exe) { Every = TimeSpan.FromMilliseconds(200) };
        Assert.False(signIn.Check().SignedIn);
        Assert.True(signIn.Start());
        var seen = new List<AgentSignedIn>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        Assert.True(await signIn.WaitAsync(cts.Token, seen.Add));
        Assert.Equal(new AgentSignedIn(true, "claude.ai", "pro"), seen[^1]);
        // It found a terminal (the stand-in stops, as Claude Code does, without one) and showed its page.
        Assert.True(signIn.Waiting);
        Assert.False(signIn.WantedTerminal);
        Assert.False(signIn.Stuck);
        // Its printed page ends on a code to paste: never opened.
        Assert.False(signIn.PageFinishesByItself);
        Assert.Equal(0, signIn.Opened);
    }

    [Fact]
    public async Task Opening_the_sign_in_again_runs_it_again_and_the_one_stopped_is_no_failure()
    {
        using var home = new TempHome();
        string exe = FakeAgents.WriteCli(home["bin"], "claude");
        File.WriteAllText(home["bin/login-delay"], "3");
        using var signIn = new AgentSignIn(AgentCli.Claude, exe) { Every = TimeSpan.FromMilliseconds(200) };
        Assert.True(signIn.Start());
        Assert.True(signIn.Restart());
        Assert.True(signIn.Running);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        Assert.True(await signIn.WaitAsync(cts.Token));
        Assert.False(signIn.Stuck);
    }

    [Fact]
    public async Task A_sign_in_that_stops_at_once_is_stuck_and_the_wait_ends_with_no_terminal()
    {
        using var home = new TempHome();
        string exe = FakeAgents.WriteCli(home["bin"], "codex");
        File.WriteAllText(home["bin/login-fails"], "");
        using var signIn = new AgentSignIn(AgentCli.Codex, exe) { Every = TimeSpan.FromMilliseconds(200) };
        signIn.Start();
        // It ends by itself, well before the wait would: nothing is left waiting on a sign-in that isn't going.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        Assert.False(await signIn.WaitAsync(cts.Token));
        Assert.False(cts.IsCancellationRequested);
        Assert.True(signIn.Stuck);
        Assert.False(signIn.GaveUp);
    }

    [Fact]
    public async Task Codexs_page_opens_itself_when_it_says_the_browser_wouldnt_and_again_on_asking()
    {
        using var home = new TempHome();
        string exe = FakeAgents.WriteCli(home["bin"], "codex");
        File.WriteAllText(home["bin/login-delay"], "120");
        File.WriteAllText(home["bin/no-browser"], "");
        var pages = new List<string>();
        using var signIn = new AgentSignIn(AgentCli.Codex, exe) { Every = TimeSpan.FromMilliseconds(200), OpenPage = p => { lock (pages) pages.Add(p); return true; } };
        Assert.True(signIn.Start());
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (signIn.Opened == 0) await Task.Delay(50, cts.Token);
        Assert.True(signIn.PageFinishesByItself);
        Assert.True(signIn.OpenAgain());
        while (true)
        {
            lock (pages) if (pages.Count == 2) break;
            await Task.Delay(50, cts.Token);
        }
        // Opened again without starting another sign-in.
        Assert.Equal(1, FakeAgents.Logins(home["bin"]));
        lock (pages) Assert.All(pages, p => Assert.StartsWith("https://auth.example.com/oauth/authorize?", p));
    }

    [Fact]
    public void Codexs_status_words_are_never_kept_or_logged()
    {
        using var home = new TempHome();
        string exe = FakeAgents.WriteCli(home["bin"], "codex");
        File.WriteAllText(home["bin/signed-in"], "");
        using var signIn = new AgentSignIn(AgentCli.Codex, exe);
        var state = signIn.Check();
        Assert.Equal(new AgentSignedIn(true), state);
        foreach (string log in Directory.EnumerateFiles(Path.Combine(Program.Home, "logs")))
            Assert.DoesNotContain("FAKEKEY42", File.ReadAllText(log));
        Assert.DoesNotContain(Directory.EnumerateFiles(home.Path, "*", SearchOption.AllDirectories),
            f => !f.EndsWith(".ps1", StringComparison.Ordinal) && !f.EndsWith("codex", StringComparison.Ordinal) && File.ReadAllText(f).Contains("FAKEKEY42", StringComparison.Ordinal));
    }
}
