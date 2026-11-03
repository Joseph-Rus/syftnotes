using System.Diagnostics;

namespace StudyStash.Core.Tests;

/// <summary>Running a command and reading what it printed.</summary>
public class MachineRunTests
{
    /// <summary>A command that leaves something running that took its output along (git's background helpers do, on
    /// Windows): what it printed comes back as soon as it has stopped, not empty after the whole timeout.</summary>
    [Fact]
    public void What_a_command_printed_comes_back_though_something_it_started_still_holds_its_output()
    {
        var (exe, args) = OperatingSystem.IsWindows()
            ? ("cmd.exe", new[] { "/c", "echo printed& start /b ping -n 30 127.0.0.1 >nul" })
            : ("/bin/sh", new[] { "-c", "echo printed; sleep 30 &" });
        var took = Stopwatch.StartNew();
        var r = Machine.Run(exe, args, TimeSpan.FromSeconds(20));
        took.Stop();

        Assert.NotNull(r);
        Assert.Equal(0, r.ExitCode);
        Assert.Equal("printed", r.Stdout.Trim());
        Assert.True(took.Elapsed < TimeSpan.FromSeconds(15), $"took {took.Elapsed.TotalSeconds:0.0} s: it waited for the helper");
    }
}
