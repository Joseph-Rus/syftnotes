using StudyStash.App.ViewModels;
using StudyStash.Core;
using StudyStash.Core.Ai;

namespace StudyStash.App.Services;

/// <summary>
/// A first run's Get started, after the library is made (the welcome's one button, <see cref="SetupStep.Start"/>):
/// the model for this computer starts downloading, the microphone is asked for (a Mac's own question, under the
/// welcome's sentence that it comes next), and an AI already signed in on this computer with a plan writes the notes.
/// With none, the library writes no notes yet and the first lecture's page asks which AI (Continue with Google, Claude,
/// ChatGPT). Nothing here is installed, signed in to or asked of an account: an AI's own status command is all it runs,
/// and only its exit code, and for Claude Code how it's signed in, are read.
/// </summary>
public static class FirstRun
{
    /// <summary>The AI to write the notes with, as found on this computer: the first of Claude, ChatGPT and Gemini (the
    /// order setup has always offered them) that's installed and signed in on a plan, or "" for none.
    /// <paramref name="status"/> runs an AI's status command (<see cref="AgentCliInfo.StatusArgs"/>).</summary>
    public static string Detect(Func<AgentCliInfo, AgentFound> find, Func<AgentCliInfo, string, ProcResult?> status)
    {
        foreach (var cli in AgentCli.All)
        {
            AgentFound found;
            try
            {
                found = find(cli);
            }
            catch (Exception e) when (e is IOException or InvalidOperationException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
            {
                continue;
            }
            if (!found.Works) continue;
            if (OnPlan(cli.Id, status(cli, found.Exe!))) return cli.Id;
        }
        return "";
    }

    /// <summary>
    /// Signed in on a plan, as <paramref name="id"/>'s status command said it: never with a key that's paid for as it's
    /// used, which would spend the student's money on every lecture without their asking. Claude Code says how it's
    /// signed in (<c>authMethod</c> "claude.ai" is a Pro or Max sign-in). Codex says "Logged in using ChatGPT" for a
    /// ChatGPT plan and names an API key otherwise: only those two phrases are looked for, and nothing it printed is kept.
    /// Antigravity runs in Syft Notes' own home for it, signed in with a Google account (free, or Google AI Pro).
    /// </summary>
    public static bool OnPlan(string id, ProcResult? result)
    {
        if (result is not { ExitCode: 0 }) return false;
        switch (id)
        {
            case "claude":
                var s = AgentCli.ReadStatus("claude", result);
                return s.SignedIn && (s.AuthMethod.Equals("claude.ai", StringComparison.OrdinalIgnoreCase) || s.AuthMethod.Length == 0 && s.Plan.Length > 0);
            case "codex":
                string said = result.Stdout;
                return said.Contains("ChatGPT", StringComparison.OrdinalIgnoreCase) && !said.Contains("API key", StringComparison.OrdinalIgnoreCase);
            case "gemini":
                return true;
            default:
                return false;
        }
    }

    /// <summary>Runs an AI's status command with its own environment (agy in Syft Notes' own home), for
    /// <see cref="Detect"/>: its exit code, and what it printed (Codex's on both outputs: it says how it's signed in on
    /// its error output). Nothing it printed is logged or kept; null when it didn't run or took over 20 seconds.</summary>
    public static ProcResult? Status(AgentCliInfo cli, string exe)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo(exe)
            {
                RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true, UseShellExecute = false, CreateNoWindow = true,
            };
            foreach (string a in cli.StatusArgs) psi.ArgumentList.Add(a);
            psi.Environment["PATH"] = AiProvider.SearchPath();
            psi.Environment.Remove("CLAUDECODE");
            foreach (var (k, v) in cli.Env()) psi.Environment[k] = v;
            using var p = System.Diagnostics.Process.Start(psi);
            if (p is null) return null;
            p.StandardInput.Close();
            var output = p.StandardOutput.ReadToEndAsync();
            var errors = p.StandardError.ReadToEndAsync();
            var limit = TimeSpan.FromSeconds(20);
            if (!p.WaitForExit(limit))
            {
                try { p.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                return null;
            }
            string said = output.Wait(limit) ? output.Result : "";
            // Claude Code's JSON is on its standard output alone; Codex's line is on its error output.
            if (cli.Id == "codex") said += "\n" + (errors.Wait(limit) ? errors.Result : "");
            return new ProcResult(p.ExitCode, said);
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return null;
        }
    }

    /// <summary>
    /// What Get started does once the library is made. <paramref name="detect"/> finds the AI already signed in here
    /// (it runs while the Mac asks for the microphone, so the student never waits for it); <paramref name="writeNotes"/>
    /// turns the library's notes and sorting on or off. Any of it that fails is left for later: the first lecture's
    /// page asks which AI, and Record asks for the microphone again.
    /// </summary>
    public static async Task AfterLibraryAsync(SetupModel m, AppHost host, IAiLibrary ai, Func<string> detect, Func<bool, Task<bool>> writeNotes)
    {
        var finding = Task.Run(() =>
        {
            try
            {
                return detect();
            }
            catch (Exception e) when (e is IOException or InvalidOperationException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
            {
                host.Log($"[setup] looking for an AI here: {e.Message}");
                return "";
            }
        });
        // The model for this computer: nothing to pick, it downloads while the student gets going (Record works before
        // it's here; the lecture waits for it).
        if (!host.ModelReady) _ = host.DownloadModelAsync();
        // The Mac's own question now, under the welcome's sentence that it comes next; Windows asks nothing (its
        // desktop-app switch is only read).
        try
        {
            var mic = await host.AskMicAsync();
            host.Log($"[setup] the microphone: {mic}");
        }
        catch (Exception e) when (e is InvalidOperationException or PlatformNotSupportedException or DllNotFoundException)
        {
            host.Log($"[setup] couldn't ask for the microphone: {e.Message}");
        }
        string id = await finding;
        try
        {
            if (id.Length > 0)
            {
                // The AI already signed in here writes the notes and answers questions, with no question asked; its plan
                // is proved by the first notes it writes (a failure says so on the lecture's page).
                if (await ai.DefaultsAsync(notes: id, ask: id) is not null && await writeNotes(true))
                {
                    host.Save(s => s.SetupAi = id);
                    m.NotesSummary = $"Written by {Engines.Name(id)}";
                    host.Log($"[setup] {Engines.Name(id)} is signed in here: it writes the notes");
                    return;
                }
            }
            // No AI here yet: the library files lectures with their transcripts, and the first lecture's page asks which
            // AI writes the notes (a new library's own pick, the free AI on this computer, isn't here to write them).
            await writeNotes(false);
            host.Log("[setup] no AI signed in here: the first lecture's page asks which writes the notes");
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or LibraryRefusedException)
        {
            host.Log($"[setup] who writes the notes: {e.Message}");
        }
    }
}
