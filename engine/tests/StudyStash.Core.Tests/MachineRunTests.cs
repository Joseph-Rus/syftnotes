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

    /// <summary>The background library's keeper waits for its library to end, then starts it again: it must see the end
    /// itself, though something the library started still holds its output (git's helpers do, on Windows: the keeper
    /// never started it again, and the rehearsal's keep-alive test waited in vain).</summary>
    [Fact]
    public async Task A_process_that_has_ended_is_seen_to_end_though_something_it_started_still_holds_its_output()
    {
        var psi = OperatingSystem.IsWindows()
            ? new ProcessStartInfo("cmd.exe") { ArgumentList = { "/c", "echo started& start /b ping -n 30 127.0.0.1 >nul" } }
            : new ProcessStartInfo("/bin/sh") { ArgumentList = { "-c", "echo started; sleep 30 &" } };
        psi.UseShellExecute = false;
        psi.CreateNoWindow = true;
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        using var p = Process.Start(psi)!;
        p.OutputDataReceived += (_, _) => { };
        p.ErrorDataReceived += (_, _) => { };
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        var outputEnds = p.WaitForExitAsync(TestContext.Current.CancellationToken);

        using var within = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        await Machine.ExitedAsync(p, within.Token);

        Assert.True(p.HasExited);
        Assert.False(outputEnds.IsCompleted, "the helper still holds the output, so waiting for that would still be waiting");
    }
}
