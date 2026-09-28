using System.Text;
using System.Text.RegularExpressions;

namespace StudyStash.Core.Ai;

/// <summary>What to start for a command: the program, and either its arguments one by one (each reaches it exactly as
/// written) or, for cmd.exe, the whole command line as it must be typed.</summary>
public sealed record Launch(string FileName, IReadOnlyList<string> Arguments, string? CommandLine = null);

/// <summary>
/// How a command-line AI is started on Windows so every argument reaches it exactly as written. npm installs Claude
/// Code and Codex as claude.cmd and codex.cmd, and a .cmd only runs through cmd.exe, which cuts its command line at the
/// first line break and reads % ^ &amp; | &lt; &gt; and quotes as its own: a system prompt of several lines arrives as
/// its first line, the arguments after it never arrive, and a class called "R&amp;D" could start a program of its own.
/// So an npm shim is seen through: the Node.js it would use runs its script directly (or its program, for a shim of a
/// program), with no cmd.exe in between. A .cmd that isn't an npm shim runs through cmd.exe with every argument
/// escaped for it, line breaks made spaces (nothing that needs its lines is put there: see
/// <see cref="AiProvider.PromptOnInput"/>).
/// </summary>
public static partial class WindowsCommand
{
    /// <summary>A batch file: what Windows only runs through cmd.exe.</summary>
    public static bool IsBatch(string program) =>
        program.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) || program.EndsWith(".bat", StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether <paramref name="program"/> would run through cmd.exe: a batch file that isn't an npm shim this
    /// can see through.</summary>
    public static bool ThroughCmd(string program, Func<string, string?>? which = null) =>
        IsBatch(program) && SeeThrough(program, which ?? AiProvider.Which) is null;

    /// <summary>How to start <paramref name="cmd"/> (the program, then its arguments) on Windows: as it is; an npm
    /// shim's own Node.js and script; or cmd.exe with a command line escaped for it. <paramref name="which"/> finds
    /// node for a shim with none of its own beside it.</summary>
    public static Launch For(IReadOnlyList<string> cmd, Func<string, string?>? which = null)
    {
        string program = cmd[0];
        var args = cmd.Skip(1).ToList();
        if (!IsBatch(program)) return new Launch(program, args);
        if (SeeThrough(program, which ?? AiProvider.Which) is { } direct) return new Launch(direct[0], [.. direct.Skip(1), .. args]);
        string comspec = Environment.GetEnvironmentVariable("ComSpec") is { Length: > 0 } c ? c : "cmd.exe";
        return new Launch(comspec, [], CmdLine(program, args));
    }

    /// <summary>
    /// What an npm shim (npm's own cmd-shim, old or new, or pnpm's) runs, without its cmd.exe: the Node.js beside it
    /// or on the PATH and its script, or the program it wraps. Null when <paramref name="shim"/> isn't one, or what it
    /// points at isn't there.
    /// </summary>
    public static List<string>? SeeThrough(string shim, Func<string, string?> which)
    {
        string text;
        try
        {
            var info = new FileInfo(shim);
            if (!info.Exists || info.Length > 64 * 1024) return null;
            text = File.ReadAllText(shim);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
        string dir = Path.GetDirectoryName(Path.GetFullPath(shim)) ?? ".";
        // The line that passes the arguments on (%*) names the target last, relative to the shim's own folder.
        foreach (string line in text.ReplaceLineEndings("\n").Split('\n').Reverse())
        {
            if (!line.Contains("%*", StringComparison.Ordinal)) continue;
            var targets = ShimPath().Matches(line).Select(m => m.Groups[1].Value)
                .Where(p => !p.EndsWith("node.exe", StringComparison.OrdinalIgnoreCase) && !p.EndsWith("node", StringComparison.OrdinalIgnoreCase)).ToList();
            if (targets.Count == 0) continue;
            string target = Path.GetFullPath(Path.Combine(dir, targets[^1].TrimStart('\\', '/').Replace('\\', Path.DirectorySeparatorChar)));
            if (!File.Exists(target)) return null;
            if (target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return [target];
            // A script npm runs with Node.js: a .js one, or one with no extension that the shim starts with node.
            bool script = Path.GetExtension(target).ToLowerInvariant() is ".js" or ".cjs" or ".mjs"
                || (Path.GetExtension(target).Length == 0 && text.Contains("node", StringComparison.OrdinalIgnoreCase));
            if (!script) return null;
            string beside = Path.Combine(dir, "node.exe");
            string? node = File.Exists(beside) ? beside : which("node");
            return node is null ? null : [node, target];
        }
        return null;
    }

    /// <summary>A path in quotes, relative to the shim's own folder: "%dp0%\…" (npm's cmd-shim) or "%~dp0\…" (older
    /// npm, and pnpm).</summary>
    [GeneratedRegex("\"(?:%dp0%|%~dp0)([^\"%]*)\"")]
    private static partial Regex ShimPath();

    /// <summary>
    /// cmd.exe's own command line for a batch file and its arguments: <c>/d /s /c "…"</c> (no AutoRun, the outer
    /// quotes taken off as they are), each piece quoted the way the program behind the batch file will read it, then
    /// every character cmd.exe gives a meaning to escaped with ^, twice over for the arguments, since the batch file
    /// hands them on with %* and cmd.exe reads that line again. Line breaks can't be escaped: they become spaces.
    /// </summary>
    public static string CmdLine(string program, IEnumerable<string> args)
    {
        var line = new StringBuilder(Meta(program));
        foreach (string a in args) line.Append(' ').Append(Meta(Meta(Quote(a.ReplaceLineEndings(" ")))));
        return $"/d /s /c \"{line}\"";
    }

    /// <summary>One argument quoted as a Windows program splits its command line: backslashes doubled only where they
    /// come before a quote (or the closing quote), and each quote escaped.</summary>
    public static string Quote(string arg)
    {
        var sb = new StringBuilder("\"");
        int slashes = 0;
        foreach (char c in arg)
        {
            if (c == '\\')
            {
                slashes++;
                continue;
            }
            sb.Append('\\', c == '"' ? slashes * 2 + 1 : slashes);
            slashes = 0;
            sb.Append(c);
        }
        sb.Append('\\', slashes * 2);
        return sb.Append('"').ToString();
    }

    static string Meta(string s) => CmdMeta().Replace(s, "^$1");

    [GeneratedRegex("([()\\][%!^\"`<>&|;, *?])")]
    private static partial Regex CmdMeta();
}
