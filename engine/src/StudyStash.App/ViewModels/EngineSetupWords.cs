namespace StudyStash.App.ViewModels;

/// <summary>One step of getting an engine going: what to do, the command to paste (if any) and a line under it.</summary>
public sealed record HowToStep(string Title, string Command = "", string Note = "");

/// <summary>
/// How to get Claude Code, Codex or Ollama going on this computer, in plain steps, for setup's "who writes your
/// notes". The commands are the makers' own, as their install pages give them (Claude Code: code.claude.com/docs
/// setup; Codex: github.com/openai/codex and its sign-in page): the native installer for Terminal on a Mac and for
/// PowerShell on Windows, npm as the other way, and how each signs in with the plan the student already has.
/// </summary>
public static class EngineSetupWords
{
    /// <summary>Where the student pastes a command: Terminal on a Mac, PowerShell on Windows.</summary>
    public static string TerminalName(bool windows) => windows ? "PowerShell" : "Terminal";

    /// <summary>The native installer's one line, or "" for an engine that isn't installed from a terminal.</summary>
    public static string InstallCommand(string id, bool windows) => id switch
    {
        "claude" => windows ? "irm https://claude.ai/install.ps1 | iex" : "curl -fsSL https://claude.ai/install.sh | bash",
        "codex" => windows ? "powershell -ExecutionPolicy ByPass -c \"irm https://chatgpt.com/codex/install.ps1 | iex\"" : "curl -fsSL https://chatgpt.com/codex/install.sh | sh",
        _ => "",
    };

    /// <summary>The same through npm (for a computer that already has Node.js).</summary>
    public static string NpmCommand(string id) => id switch
    {
        "claude" => "npm install -g @anthropic-ai/claude-code",
        "codex" => "npm install -g @openai/codex",
        _ => "",
    };

    /// <summary>What to run to sign in.</summary>
    public static string SignInCommand(string id) => id switch
    {
        "claude" => "claude",
        "codex" => "codex login",
        _ => "",
    };

    /// <summary>Which plan it runs on: the one the student already pays for.</summary>
    public static string Plan(string id) => id switch
    {
        "claude" => "It uses the Claude plan you already have (Pro or Max; the free plan doesn't include Claude Code).",
        "codex" => "It uses the ChatGPT plan you already have.",
        _ => "",
    };

    /// <summary>The steps a row's help shows for an engine in <paramref name="state"/>: install then sign in (not
    /// installed), just sign in (not signed in), or for Ollama, get the app. Empty when there's nothing to do here.</summary>
    public static IReadOnlyList<HowToStep> Steps(string id, string state, bool windows)
    {
        string where = TerminalName(windows);
        HowToStep SignIn() => id == "claude"
            ? new HowToStep("Sign in: run this, then sign in with your Claude account in the browser", SignInCommand(id), Plan(id))
            : new HowToStep("Sign in: run this and choose Sign in with ChatGPT", SignInCommand(id), Plan(id));
        return (id, state) switch
        {
            ("ollama", "not_installed") =>
            [
                new HowToStep("Download Ollama from ollama.com and open it", Note: $"It's free and runs on this {(windows ? "PC" : "Mac")}: nothing you record leaves it."),
            ],
            ("ollama", "not_running") => [new HowToStep("Open the Ollama app, or start it from here")],
            ("ollama", "model_missing") =>
            [
                new HowToStep("Download the model it writes notes with", Note: $"A few gigabytes, once. It runs on this {(windows ? "PC" : "Mac")}: nothing you record leaves it."),
            ],
            ("claude" or "codex", "not_installed") =>
            [
                new HowToStep($"Install it: paste this into {where}", InstallCommand(id, windows), $"Or with npm: {NpmCommand(id)}"),
                SignIn(),
            ],
            ("claude" or "codex", "not_signed_in") => [SignIn()],
            _ => [],
        };
    }
}
