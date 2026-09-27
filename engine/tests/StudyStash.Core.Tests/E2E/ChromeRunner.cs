using System.Diagnostics;
using System.Text;

namespace StudyStash.Core.Tests.E2E;

/// <summary>
/// A real, headless Chrome (Chrome for Testing: branded Chrome no longer loads an extension from the command line)
/// with one unpacked extension loaded, on a throwaway profile, never the person's own. What it prints goes to a log
/// whose end <see cref="Tail"/> gives a failing test. Disposing it ends Chrome and every process it started.
/// </summary>
public sealed class ChromeRunner : IDisposable
{
    /// <summary>The Chrome binary the end-to-end tests use; empty when they're skipped.</summary>
    public static string Binary => Environment.GetEnvironmentVariable("STUDYSTASH_E2E_CHROME") ?? "";

    readonly Process process;
    readonly StringBuilder log = new();
    readonly string profile;

    ChromeRunner(Process process, string profile)
    {
        this.process = process;
        this.profile = profile;
    }

    /// <summary>
    /// Start Chrome with the extension in <paramref name="extensionDir"/>, each host in <paramref name="hosts"/>
    /// resolving to 127.0.0.1, on <paramref name="startUrl"/>. <paramref name="profile"/> is an empty folder for its
    /// profile.
    /// </summary>
    public static ChromeRunner Start(string extensionDir, string profile, string startUrl, params string[] hosts)
    {
        Directory.CreateDirectory(profile);
        var info = new ProcessStartInfo(Binary)
        {
            UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true,
        };
        foreach (string arg in new[]
        {
            "--headless=new", $"--user-data-dir={profile}", "--no-first-run", "--no-default-browser-check", "--disable-sync",
            "--use-mock-keychain", "--password-store=basic", "--disable-search-engine-choice-screen",
            // Chrome no longer loads an extension from the command line, and disables an unpacked one that reloads
            // itself while developer mode is off (a fresh profile's): a student's Chrome has developer mode on.
            "--disable-features=DisableLoadExtensionCommandLineSwitch,ExtensionDisableUnsupportedDeveloper",
            // Nothing but the pretend Canvas and the library: no Google services, updates or metrics.
            "--disable-background-networking", "--disable-component-update", "--disable-domain-reliability",
            $"--load-extension={extensionDir}", $"--disable-extensions-except={extensionDir}",
            "--host-resolver-rules=" + string.Join(", ", hosts.Select(h => $"MAP {h} 127.0.0.1")),
            "--enable-logging=stderr", "--v=0",
            startUrl,
        }) info.ArgumentList.Add(arg);
        var p = new Process { StartInfo = info, EnableRaisingEvents = true };
        var runner = new ChromeRunner(p, profile);
        p.ErrorDataReceived += (_, e) => runner.Append(e.Data);
        p.OutputDataReceived += (_, e) => runner.Append(e.Data);
        p.Start();
        p.BeginErrorReadLine();
        p.BeginOutputReadLine();
        return runner;
    }

    void Append(string? line)
    {
        if (line is null) return;
        lock (log) log.AppendLine(line);
    }

    public bool Exited => process.HasExited;

    /// <summary>Everything Chrome printed that mentions the extension or a console message, then the last lines.</summary>
    public string Tail(int lines = 40)
    {
        string[] all;
        lock (log) all = log.ToString().Split('\n');
        var console = all.Where(l => l.Contains("CONSOLE", StringComparison.Ordinal) || l.Contains("xtension", StringComparison.Ordinal)).TakeLast(lines);
        return string.Join('\n', console.Concat(["--- last lines ---"]).Concat(all.TakeLast(lines)));
    }

    public void Dispose()
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(10_000);
            }
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
        }
        process.Dispose();
        try
        {
            Directory.Delete(profile, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }
}
