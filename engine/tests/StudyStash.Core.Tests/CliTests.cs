using System.Net;
using System.Net.Sockets;
using StudyStash.Library;

namespace StudyStash.Core.Tests;

/// <summary>The engine's commands: the app runs as the engine only for commands that exist, and ones that don't (an
/// old sign-in and sync, the laptop's old watcher, a config check) open the app or print the usage instead.</summary>
public class CliTests
{
    [Theory]
    [InlineData("run")]
    [InlineData("serve")]
    [InlineData("--home", "x", "run")]
    [InlineData("mcp")]
    [InlineData("ai", "test")]
    [InlineData("doctor")]
    [InlineData("version")]
    public void A_command_that_exists_runs_the_engine(params string[] args) => Assert.True(Cli.IsCommand(args));

    [Theory]
    [InlineData("login")]
    [InlineData("logout")]
    [InlineData("sync")]
    [InlineData("tools")]
    [InlineData("client", "run")]
    [InlineData("--home", "x", "client", "open")]
    [InlineData("config-check")]
    [InlineData]
    public void A_command_that_is_gone_opens_the_app(params string[] args) => Assert.False(Cli.IsCommand(args));

    [Theory]
    [InlineData("login")]
    [InlineData("sync")]
    [InlineData("tools")]
    [InlineData("config-check")]
    public async Task A_command_that_is_gone_prints_the_usage(string command)
    {
        using var dir = new TempDir();
        Assert.Equal(2, await Cli.RunAsync(["--home", dir["home"], command]));
        Assert.Empty(Directory.GetFileSystemEntries(dir["home"]));
    }

    [Fact]
    public void The_usage_names_every_command_and_only_studystash_ones()
    {
        Assert.StartsWith("usage: studystash ", Cli.Usage);
        foreach (string command in Cli.Commands) Assert.Contains(command, Cli.Usage);
        foreach (string gone in (string[])["login", "logout", "sync", "tools", "client run", "client open", "config-check"])
            Assert.DoesNotContain(gone, Cli.Usage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_background_library_stopped_by_an_error_says_why_in_one_line()
    {
        // As the web server reports a port in use: under a Mac's launchd, nothing else would say so (0.23.1's update).
        var e = Assert.Throws<IOException>(FailToBind);
        string line = Cli.CrashLine(e);
        Assert.DoesNotContain('\n', line);
        Assert.StartsWith("System.IO.IOException: Failed to bind to address http://0.0.0.0:18787: address already in use. second line "
            + "(from System.Net.Sockets.SocketException: ", line);
        Assert.Contains("| at StudyStash.Core.Tests.CliTests.FailToBind()", line);
    }

    static void FailToBind() =>
        throw new IOException("Failed to bind to address http://0.0.0.0:18787: address already in use.\nsecond line", new SocketException((int)SocketError.AddressAlreadyInUse));

    [Fact]
    public async Task The_background_library_waits_for_a_port_another_copy_is_letting_go()
    {
        var held = new TcpListener(IPAddress.Loopback, 0);
        held.Start();
        int port = ((IPEndPoint)held.LocalEndpoint).Port;
        // Kept: it steps aside.
        Assert.False(await HostInfo.WaitPortFreeAsync(port, TimeSpan.FromMilliseconds(500), TestContext.Current.CancellationToken));
        // Let go while it waits: it goes on.
        var waiting = HostInfo.WaitPortFreeAsync(port, TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
        held.Stop();
        Assert.True(await waiting);
    }
}
