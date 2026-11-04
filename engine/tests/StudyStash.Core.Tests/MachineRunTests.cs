using System.Diagnostics;

namespace StudyStash.Core.Tests;

/// <summary>Running a command and reading what it printed.</summary>
public class MachineRunTests
{
    /// <summary>A command that leaves something running that took its output along (git's background helpers do, on
    /// Windows): what it printed comes back as soon as it has stopped and the pipe holds nothing more, not empty, and
    /// not after the helper (which outlives the whole timeout) or the timeout.</summary>
    [Fact]
    public void What_a_command_printed_comes_back_though_something_it_started_still_holds_its_output()
    {
        var (exe, args) = OperatingSystem.IsWindows()
            ? ("cmd.exe", new[] { "/c", "echo printed& start /b ping -n 90 127.0.0.1 >nul" })
            : ("/bin/sh", new[] { "-c", "echo printed; sleep 90 &" });
        var took = Stopwatch.StartNew();
        var r = Machine.Run(exe, args, TimeSpan.FromSeconds(60));
        took.Stop();

        Assert.NotNull(r);
        Assert.Equal(0, r.ExitCode);
        Assert.Equal("printed", r.Stdout.Trim());
        // Taken because the pipe was empty, not because the timeout ran out (that's a minute; this is moments).
        Assert.True(took.Elapsed < TimeSpan.FromSeconds(45), $"took {took.Elapsed.TotalSeconds:0.0} s: it waited for the helper or the timeout");
    }

    /// <summary>A command that prints a lot: all of it comes back, and quickly (reading it a little at a time, waiting
    /// between, would take minutes and run into the timeout, and come back as nothing).</summary>
    [Fact]
    public void A_command_that_prints_a_lot_comes_back_whole()
    {
        const int size = 4 * 1024 * 1024;
        var (exe, args) = OperatingSystem.IsWindows()
            ? ("powershell.exe", new[] { "-NoProfile", "-Command", $"[Console]::Out.Write('a' * {size})" })
            : ("/bin/sh", new[] { "-c", $"head -c {size} /dev/zero | tr '\\0' a" });
        var r = Machine.Run(exe, args, TimeSpan.FromSeconds(20));

        Assert.NotNull(r);
        Assert.Equal(size, r.Stdout.Trim().Length);
    }
}
