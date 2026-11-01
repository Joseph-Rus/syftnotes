using System.Text;
using StudyStash.Core.Ai;

namespace StudyStash.Core.Tests;

/// <summary>
/// The terminal nobody sees that every AI sign-in runs in (<see cref="HiddenTerminal"/>): a program in it finds a real
/// terminal (the thing Claude Code's sign-in stops without), its own and wide, takes the keys typed into it, and stops
/// with everything it started. Only the system's own shell runs here, never an AI's CLI.
/// </summary>
public class HiddenTerminalTests
{
    static Launch Shell(string unix, string windows) => OperatingSystem.IsWindows()
        ? new Launch("powershell.exe", ["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", windows])
        : new Launch("/bin/sh", ["-c", unix]);

    static async Task<string> UntilShownAsync(StringBuilder shown, string words)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (true)
        {
            lock (shown)
                if (shown.ToString().Contains(words, StringComparison.Ordinal)) return shown.ToString();
            await Task.Delay(20, cts.Token);
        }
    }

    [Fact]
    public async Task A_program_in_it_has_a_wide_terminal_of_its_own_and_takes_the_keys_typed()
    {
        var launch = Shell(
            "[ -t 0 ] && [ -t 1 ] && echo has-a-terminal; (exec 3</dev/tty) 2>/dev/null && echo its-own; echo \"size $(stty size)\"; read -r x; echo \"got:$x\"",
            "if (-not [Console]::IsInputRedirected -and -not [Console]::IsOutputRedirected) { 'has-a-terminal'; 'its-own' }; \"size $([Console]::WindowHeight) $([Console]::WindowWidth)\"; $x = [Console]::ReadLine(); \"got:$x\"");
        var shown = new StringBuilder();
        using var t = HiddenTerminal.Start(launch, HiddenTerminal.Environment([]));
        t.Read(s =>
        {
            lock (shown) shown.Append(s);
        });
        await UntilShownAsync(shown, $"size {HiddenTerminal.Rows} {HiddenTerminal.Columns}");
        t.Type("hello\r");
        string all = await UntilShownAsync(shown, "got:hello");
        Assert.Contains("has-a-terminal", all);
        Assert.Contains("its-own", all);
        Assert.Equal(0, await t.Exit.WaitAsync(TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public async Task Stopping_it_ends_the_program_and_what_it_started()
    {
        using var dir = new TempDir();
        string pids = dir["pids"];
        var launch = Shell(
            $"sleep 300 & echo $! > '{pids}'; echo started; wait",
            $"$c = Start-Process -FilePath ping.exe -ArgumentList '-n','300','127.0.0.1' -NoNewWindow -PassThru; Set-Content '{pids}' $c.Id; 'started'; $c.WaitForExit()");
        var shown = new StringBuilder();
        var t = HiddenTerminal.Start(launch, HiddenTerminal.Environment([]));
        t.Read(s =>
        {
            lock (shown) shown.Append(s);
        });
        await UntilShownAsync(shown, "started");
        int child = int.Parse(File.ReadAllText(pids).Trim(), System.Globalization.CultureInfo.InvariantCulture);
        t.Dispose();
        await t.Exit.WaitAsync(TimeSpan.FromSeconds(30));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (Alive(child)) await Task.Delay(50, cts.Token);
    }

    static bool Alive(int pid)
    {
        try
        {
            using var p = System.Diagnostics.Process.GetProcessById(pid);
            return !p.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    [Fact]
    public void A_program_that_isnt_there_doesnt_start()
    {
        Assert.Throws<IOException>(() => HiddenTerminal.Start(new Launch(Path.Combine(Path.GetTempPath(), "no-such-program-" + Guid.NewGuid().ToString("N")), []), HiddenTerminal.Environment([])));
    }
}
