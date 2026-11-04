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
