using System.Diagnostics;
using System.Security.Cryptography;

namespace StudyStash.Core.Ai;

/// <summary>
/// "Open in Claude Code": a terminal in a class's folder, running the AI picked for agent work (Claude Code, Codex,
/// or Antigravity for Gemini), briefed on where it is, for real work with the class's notes and Canvas material at
/// hand. Ghostty unless another terminal is picked; on Linux, the first of <see cref="Linux"/> that's installed.
/// </summary>
public static class Terminal
{
    public static readonly (string Id, string Name, string App)[] Mac =
    [
        ("ghostty", "Ghostty", "/Applications/Ghostty.app"), ("terminal", "Terminal", "/System/Applications/Utilities/Terminal.app"),
        ("iterm", "iTerm", "/Applications/iTerm.app"), ("warp", "Warp", "/Applications/Warp.app"),
    ];

    /// <summary>Linux's terminals, by the program each puts on the PATH, in the order one is picked when the student
    /// hasn't: the desktop's own first (GNOME's Ptyxis and Terminal, KDE's Konsole, Xfce's), then the rest, then
    /// Debian's x-terminal-emulator and xterm, which nearly every system has.</summary>
    public static readonly (string Id, string Name)[] Linux =
    [
        ("ptyxis", "Ptyxis"), ("gnome-terminal", "GNOME Terminal"), ("konsole", "Konsole"), ("xfce4-terminal", "Xfce Terminal"),
        ("ghostty", "Ghostty"), ("kitty", "kitty"), ("alacritty", "Alacritty"), ("wezterm", "WezTerm"),
        ("x-terminal-emulator", "Terminal"), ("xterm", "XTerm"),
    ];

    /// <summary>The terminals on this computer: (id, name).</summary>
    public static List<(string Id, string Name)> Available() =>
        OperatingSystem.IsMacOS() ? Mac.Where(t => Directory.Exists(t.App)).Select(t => (t.Id, t.Name)).ToList()
        : OperatingSystem.IsWindows() ? [("wt", "Windows Terminal")]
        : Linux.Where(t => Machine.Which(t.Id) is not null).ToList();

    /// <summary>Linux: how <paramref name="terminal"/> is told to open in <paramref name="folder"/> and run
    /// <paramref name="script"/> with bash. Each one spells it its own way.</summary>
    internal static List<string> LinuxArgs(string terminal, string folder, string script) => terminal switch
    {
        "ptyxis" => ["--new-window", $"--working-directory={folder}", "--", "bash", script],
        "gnome-terminal" => [$"--working-directory={folder}", "--", "bash", script],
        "konsole" => ["--workdir", folder, "-e", "bash", script],
        "xfce4-terminal" => [$"--working-directory={folder}", "-x", "bash", script],
        "ghostty" => [$"--working-directory={folder}", "-e", "bash", script],
        "kitty" => ["--directory", folder, "bash", script],
        "alacritty" => ["--working-directory", folder, "-e", "bash", script],
        "wezterm" => ["start", "--cwd", folder, "--", "bash", script],
        _ => ["-e", "bash", script], // x-terminal-emulator, xterm: the script goes into the folder itself
    };

    /// <summary>The script a Linux terminal runs: into the folder, the command (with <paramref name="env"/> set for
    /// it alone), then the student's own shell.</summary>
    internal static string LinuxScript(string folder, string exe, IReadOnlyList<string> args, IReadOnlyDictionary<string, string>? env = null)
    {
        string set = string.Concat((env ?? new Dictionary<string, string>()).Select(kv => $"{kv.Key}={Quote(kv.Value)} "));
        return $"#!/bin/bash\ncd {Quote(folder)} || exit 1\n{set}{Quote(exe)} {string.Join(" ", args.Select(Quote))}\nexec \"${{SHELL:-bash}}\" -l\n";
    }

    /// <summary>The terminal <see cref="Open"/> uses on a computer that has <paramref name="here"/>: the one picked
    /// when it's there, else the first that is ("" with none). The setting starts as a Mac's Ghostty, so a PC, or a Mac
    /// without Ghostty, said "ghostty" in Settings while opening Windows Terminal, or Terminal.</summary>
    public static string Used(string picked, IReadOnlyList<(string Id, string Name)> here) =>
        here.Any(t => t.Id == picked) ? picked : here.Count > 0 ? here[0].Id : "";

    /// <summary>What the AI is told when it opens.</summary>
    public static string Briefing(string library, string folder, string? className) =>
        $"You were opened from Syft Notes, the user's lecture library ({library}: one folder per class, lectures as Markdown, "
        + "and a Canvas/ folder with assignment specs, feedback, module files and announcements). "
        + (className is not null ? $"This is the class \"{className}\" ({folder}). Look at its lectures, Canvas/assignments and Canvas/canvas-recipe.md before starting, and follow any AI-use policy in its syllabus. " : "")
        + "The study-stash MCP tools (search_notes, get_lecture, due_assignments, canvas_*) read the library and Canvas.";

    static string Quote(string s) => "'" + s.Replace("'", "'\\''") + "'";

    /// <summary>Windows Terminal's own arguments for a program started in <paramref name="folder"/>: it reads a ";"
    /// anywhere as the start of another tab, so each one is "\;", which it hands on as ";".</summary>
    internal static List<string> WindowsTerminalArgs(string folder, string program, IEnumerable<string> args) =>
        ["-d", Semicolons(folder), Semicolons(program), .. args.Select(Semicolons)];

    static string Semicolons(string s) => s.Replace(";", "\\;");

    /// <summary>Open it. The terminal's name, or throws InvalidOperationException saying why not.</summary>
    public static string Open(string home, string folder, string library, string? className, string terminal, string provider, IReadOnlyList<string> mcp)
    {
        var (bin, args, env) = Command(folder, library, className, provider, mcp);
        return LaunchInTerminal(home, folder, terminal, bin, args, env);
    }

    /// <summary>
    /// What <see cref="Open"/> runs: the command, its arguments, and what its environment needs. Gemini's agy runs in
    /// Syft Notes' own home for it (signed in there, the library's tools there), never the student's own ~/.gemini,
    /// where it would be signed out, without the library, and would make that folder.
    /// </summary>
    internal static (string Bin, List<string> Args, Dictionary<string, string> Env) Command(string folder, string library, string? className, string provider,
        IReadOnlyList<string> mcp)
    {
        var env = new Dictionary<string, string>();
        if (provider == "gemini")
        {
            new GeminiProvider().Prepare(mcp, [folder, library]);
            var agy = new List<string> { "--agent", Antigravity.LibraryAgent };
            if (!Py.SamePath(folder, library)) agy.AddRange(["--add-dir", library]);
            return ("agy", agy, Antigravity.HomeEnv());
        }
        var (bin, args) = provider switch
        {
            "codex" or "ollama" => ("codex", new List<string>()),
            _ => ("claude", new List<string> { "--append-system-prompt", Briefing(library, folder, className) }),
        };
        if (bin == "claude" && mcp.Count > 0)
            args.AddRange(["--mcp-config", new System.Text.Json.Nodes.JsonObject
            {
                ["mcpServers"] = new System.Text.Json.Nodes.JsonObject
                {
                    [ClaudeTools.ServerName] = new System.Text.Json.Nodes.JsonObject
                    {
                        ["command"] = mcp[0], ["args"] = new System.Text.Json.Nodes.JsonArray(mcp.Skip(1).Select(a => (System.Text.Json.Nodes.JsonNode)a).ToArray()),
                    },
                },
            }.ToJsonString()]);
        if (!Py.SamePath(folder, library)) args.AddRange(bin == "claude" ? ["--add-dir", library] : []);
        return (bin, args, env);
    }

    /// <summary>Open a terminal on this computer running a plain command (a sign-in, e.g. `codex login`), for the
    /// student to finish by hand. The terminal's name, or throws InvalidOperationException saying why not.</summary>
    public static string RunCommand(string home, string terminal, string exe, IReadOnlyList<string> args) =>
        LaunchInTerminal(home, home, terminal, exe, args);

    /// <summary>The script a Mac terminal runs: into the folder, the command (with <paramref name="env"/> set for it
    /// alone), then a shell.</summary>
    internal static string MacScript(string folder, string exe, IReadOnlyList<string> args, IReadOnlyDictionary<string, string>? env = null)
    {
        string set = string.Concat((env ?? new Dictionary<string, string>()).Select(kv => $"{kv.Key}={Quote(kv.Value)} "));
        return $"#!/bin/zsh -l\ncd {Quote(folder)} || exit 1\n{set}{Quote(exe)} {string.Join(" ", args.Select(Quote))}\nexec zsh -l\n";
    }

    /// <summary>Windows: a command with an environment of its own goes through PowerShell, which sets it first (a
    /// terminal window doesn't pass the app's on).</summary>
    internal static (string Program, IReadOnlyList<string> Args) WindowsWithEnv(string exe, IReadOnlyList<string> args, IReadOnlyDictionary<string, string> env)
    {
        static string Q(string s) => "'" + s.Replace("'", "''", StringComparison.Ordinal) + "'";
        string set = string.Concat(env.Select(kv => $"$env:{kv.Key} = {Q(kv.Value)}; "));
        return ("powershell.exe", ["-NoProfile", "-NoExit", "-Command", $"{set}& {Q(exe)} {string.Join(" ", args.Select(Q))}".TrimEnd()]);
    }

    static string LaunchInTerminal(string home, string folder, string terminal, string bin, IReadOnlyList<string> args,
        IReadOnlyDictionary<string, string>? env = null)
    {
        string exe = AiProvider.Which(bin) ?? throw new InvalidOperationException($"{bin} isn't installed on this computer.");
        if (OperatingSystem.IsWindows())
        {
            // An npm-installed claude.cmd is seen through to its Node.js, so the brief reaches it whole: see WindowsCommand.
            var launch = WindowsCommand.For([exe, .. args]);
            var (program, programArgs) = env is { Count: > 0 } ? WindowsWithEnv(exe, args, env)
                : launch.CommandLine is null ? (launch.FileName, launch.Arguments) : (exe, args);
            var wt = new ProcessStartInfo("wt.exe") { UseShellExecute = true };
            foreach (string a in WindowsTerminalArgs(folder, program, programArgs)) wt.ArgumentList.Add(a);
            try
            {
                Process.Start(wt);
                return "Windows Terminal";
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // No Windows Terminal (Windows 10 without it): the command in a console window of its own.
                var console = new ProcessStartInfo(program) { UseShellExecute = true, WorkingDirectory = folder };
                foreach (string a in programArgs) console.ArgumentList.Add(a);
                Process.Start(console);
                return "a console window";
            }
        }
        if (OperatingSystem.IsLinux())
        {
            var here = Available();
            string id = Used(terminal, here);
            if (id.Length == 0) throw new InvalidOperationException("No terminal app found. Install one (GNOME Terminal, Konsole or xterm), then try again.");
            string sh = Script(home, ".sh", LinuxScript(folder, exe, args, env));
            var run = new ProcessStartInfo(id) { UseShellExecute = false, WorkingDirectory = folder };
            foreach (string a in LinuxArgs(id, folder, sh)) run.ArgumentList.Add(a);
            using var started = Process.Start(run);
            return here.First(h => h.Id == id).Name;
        }
        var t = Mac.FirstOrDefault(m => m.Id == terminal);
        if (t.Id is null || !Directory.Exists(t.App)) t = Mac.FirstOrDefault(m => Directory.Exists(m.App));
        if (t.Id is null) throw new InvalidOperationException("No terminal app found.");
        // A small script the terminal runs: into the folder, start the AI, and stay in a shell after.
        string script = Script(home, ".command", MacScript(folder, exe, args, env));
        var open = t.Id == "ghostty"
            ? new ProcessStartInfo("open") { ArgumentList = { "-na", t.App, "--args", $"--working-directory={folder}", "-e", "/bin/zsh", script } }
            : new ProcessStartInfo("open") { ArgumentList = { "-a", t.App, script } };
        open.UseShellExecute = false;
        using var _ = Process.Start(open);
        return t.Name;
    }

    /// <summary>Writes a terminal's script into home/launch (keeping the newest 20 of its kind) and makes it runnable.</summary>
    static string Script(string home, string extension, string text)
    {
        string dir = Path.Combine(home, "launch");
        Directory.CreateDirectory(dir);
        foreach (string old in Directory.EnumerateFiles(dir, "*" + extension).OrderByDescending(File.GetLastWriteTimeUtc).Skip(20)) File.Delete(old);
        string script = Path.Combine(dir, Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(4)) + extension);
        File.WriteAllText(script, text);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return script;
    }
}
