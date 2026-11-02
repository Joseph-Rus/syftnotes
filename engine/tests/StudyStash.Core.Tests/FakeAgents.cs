using System.Text.Json.Nodes;
using StudyStash.Core.Ai;

namespace StudyStash.Core.Tests;

/// <summary>
/// Stand-ins for Claude Code, Codex and Antigravity's agy, and for their makers' installers, as small scripts in a test's own folder (sh
/// on a Mac, a .cmd with PowerShell behind it on Windows): nothing here ever reaches a real installer, account or the
/// network. A fake CLI answers <c>--version</c>, its sign-in and status commands (a <c>signed-in</c> file beside it
/// is the sign-in), and runs a "turn" the way <c>claude -p</c> / <c>codex exec</c> would: it writes down its
/// arguments, the names (never the values) of its environment variables and its input, then prints
/// <c>replay-N.jsonl</c> (or <c>replay.jsonl</c>) line by line. Files beside it change what it does: <c>broken</c>
/// (its --version fails), <c>login-fails</c>, <c>sleep</c> (seconds to wait before answering), <c>exit</c>,
/// <c>refuse</c> (an argument it doesn't know, as an older version: a turn given it stops with clap's error). The fake
/// agy also answers <c>models</c> (its status) and <c>-p=/usage</c> (its sign-in, then <c>usage.json</c>; with a
/// <c>region</c> file, Google's refusal for the country), and writes
/// down the HOME each turn had (<c>home-N.txt</c>).
/// </summary>
public static class FakeAgents
{
    public static bool Windows => OperatingSystem.IsWindows();

    /// <summary>The fake's own file name in <paramref name="bin"/>.</summary>
    public static string ExePath(string bin, string id) => Path.Combine(bin, Windows ? id + ".cmd" : id);

    /// <summary>Finds a command only in <paramref name="bin"/>.</summary>
    public static Func<string, string?> Which(string bin) => name => File.Exists(ExePath(bin, name)) ? ExePath(bin, name) : null;

    public static string VersionOf(string id) => id switch { "codex" => "codex-cli 0.46.0", "gemini" => "1.4.4", _ => "2.1.260 (Claude Code)" };

    /// <summary>The command an AI's CLI is: agy for Gemini, else its id.</summary>
    public static string BinaryOf(string id) => id == "gemini" ? "agy" : id;

    /// <summary>Writes a fake <paramref name="id"/> ("claude", "codex" or "gemini", which is agy) into
    /// <paramref name="bin"/>; its path.</summary>
    public static string WriteCli(string bin, string id, string? version = null)
    {
        Directory.CreateDirectory(bin);
        version ??= VersionOf(id);
        string name = BinaryOf(id);
        if (OperatingSystem.IsWindows())
        {
            File.WriteAllText(Path.Combine(bin, name + ".ps1"), PowerShellCli(id, version));
            // Its arguments go to PowerShell in the environment, as the command line cmd.exe was given, and are split there
            // the way a Windows program splits its own: PowerShell's -File would read a bare "-" (Codex's "the prompt is on
            // the input") as a parameter with no name, and refuse to start.
            File.WriteAllText(ExePath(bin, name),
                $"@echo off\r\nset FAKE_ARGS=%*\r\npowershell -NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"%~dp0{name}.ps1\"\r\nexit /b %ERRORLEVEL%\r\n");
        }
        else
        {
            File.WriteAllText(ExePath(bin, name), ShCli(id, version));
            File.SetUnixFileMode(ExePath(bin, name), UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        return ExePath(bin, name);
    }

    /// <summary>What the fake agy's <c>-p=/usage</c> says when the test gives none (<c>usage.json</c>).</summary>
    public const string Usage = """{"status":"SUCCESS","command":{"name":"usage","data":{"groups":[{"name":"Gemini Models","buckets":[{"id":"gemini-weekly","window":"weekly","remaining_fraction":0.8,"reset_time":"2026-10-29T05:47:53Z"}]},{"name":"Claude and GPT models","buckets":[{"id":"3p-weekly","window":"weekly","remaining_fraction":0,"reset_time":"2026-10-29T05:47:53Z"}]}]}}}""";

    /// <summary>Scripted output for turn <paramref name="turn"/> (0: every turn without its own).</summary>
    public static void Replay(string bin, IEnumerable<string> lines, int turn = 0) =>
        File.WriteAllLines(Path.Combine(bin, turn == 0 ? "replay.jsonl" : $"replay-{turn}.jsonl"), lines);

    public static string[] Argv(string bin, int turn) => File.ReadAllLines(Path.Combine(bin, $"argv-{turn}.txt"));
    public static string[] EnvNames(string bin, int turn) => File.ReadAllLines(Path.Combine(bin, $"env-{turn}.txt")).Select(l => l.Trim()).ToArray();
    public static string Input(string bin, int turn) => File.ReadAllText(Path.Combine(bin, $"stdin-{turn}.txt"));
    /// <summary>Whether turn <paramref name="turn"/>'s process is still running.</summary>
    public static bool Running(string bin, int turn)
    {
        string f = Path.Combine(bin, $"pid-{turn}");
        if (!File.Exists(f)) return false;
        try
        {
            using var p = System.Diagnostics.Process.GetProcessById(int.Parse(File.ReadAllText(f).Trim(), System.Globalization.CultureInfo.InvariantCulture));
            return !p.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>How many times the fake's sign-in was started (<c>logins</c>, a line each).</summary>
    public static int Logins(string bin)
    {
        string path = Path.Combine(bin, "logins");
        // On Windows the fake's Add-Content may hold the file this moment: it's read sharing every way, and looked at
        // again until it's free (a few seconds at most).
        var until = DateTime.UtcNow.AddSeconds(5);
        while (true)
        {
            try
            {
                if (!File.Exists(path)) return 0;
                using var reader = new StreamReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete));
                return reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length;
            }
            catch (IOException) when (DateTime.UtcNow < until)
            {
                Thread.Yield();
            }
        }
    }

    public static int Turns(string bin) => File.Exists(Path.Combine(bin, "turns")) ? int.Parse(File.ReadAllText(Path.Combine(bin, "turns")).Trim(), System.Globalization.CultureInfo.InvariantCulture) : 0;

    static string ShCli(string id, string version) => $$"""
        #!/bin/sh
        D="$(cd "$(dirname "$0")" && pwd)"
        if [ "$1" = "--version" ]; then
          echo "$HOME" > "$D/version-home.txt"
          [ -f "$D/broken" ] && { echo "dyld: Library not loaded" >&2; exit 1; }
          echo "{{version}}"; exit 0
        fi
        if [ "{{id}}" = claude ] && [ "$1" = auth ] && [ "$2" = status ]; then
          if [ -f "$D/signed-in" ]; then echo '{"loggedIn":true,"authMethod":"claude.ai","subscriptionType":"pro","email":"someone@example.com","orgId":"org-fake"}'; exit 0; fi
          echo '{"loggedIn":false}'; exit 1
        fi
        if [ "{{id}}" = codex ] && [ "$1" = login ] && [ "$2" = status ]; then
          if [ -f "$D/signed-in" ]; then echo "Logged in using an API key - sk-proj-***FAKEKEY42"; exit 0; fi
          echo "Not logged in"; exit 1
        fi
        if [ "{{id}}" = gemini ] && [ "$1" = models ]; then
          if [ -f "$D/signed-in" ]; then echo "gemini-3.8-flash"; exit 0; fi
          echo "Error: Please sign in to view available models. Launch the CLI without arguments to sign in." >&2; exit 1
        fi
        if [ "{{id}}" = gemini ] && [ "$1" = "-p=/usage" ]; then
          echo "$HOME" > "$D/usage-home.txt"
          if [ -f "$D/region" ]; then
            echo '{"conversation_id":"","status":"ERROR","response":"","error":"User location is not supported for the API use."}'
            echo "Error: User location is not supported for the API use." >&2; exit 1
          fi
          if [ ! -f "$D/signed-in" ]; then
            [ -t 0 ] || { echo "Error: authentication required. Run 'agy' to log in, then retry." >&2; exit 1; }
            echo "Authentication required. Please visit the URL to log in:" >&2
            echo "  https://accounts.google.com/o/oauth2/auth?fake=1" >&2
            [ -f "$D/login-fails" ] && { echo "Error: authentication required. Run 'agy' to log in, then retry." >&2; exit 1; }
            sleep "$(cat "$D/login-delay" 2>/dev/null || echo 1)"
            touch "$D/signed-in"
          fi
          if [ -f "$D/usage.json" ]; then cat "$D/usage.json"; else echo '{{Usage}}'; fi
          exit 0
        fi
        if { [ "{{id}}" = claude ] && [ "$1" = auth ] && [ "$2" = login ]; } || { [ "{{id}}" = codex ] && [ "$1" = login ]; }; then
          echo x >> "$D/logins"
          # As Claude Code does, it stops at once without a terminal.
          { [ -t 0 ] && [ ! -f "$D/login-fails" ]; } || { echo "Raw mode is not supported on the current process.stdin" >&2; exit 1; }
          if [ "{{id}}" = claude ]; then
            printf ' Select login method:\n\n \342\235\257 1. Claude account with subscription \302\267 Pro, Max, Team, or Enterprise\n   2. Anthropic Console account \302\267 API usage billing\n'
            read -r choice || exit 1
            echo yes > "$D/login-method"
            printf 'Opening browser to sign in\342\200\246\n'
            echo "Browser didn't open? Use the url below to sign in:"
            echo "https://claude.example.com/oauth/authorize?code=true&redirect_uri=https%3A%2F%2Fplatform.example.com%2Foauth%2Fcode%2Fcallback&state=fake"
            printf 'Paste code here if prompted > '
          else
            echo "Starting local login server on http://localhost:1455."
            [ -f "$D/no-browser" ] && echo "Couldn't open the browser."
            echo "If your browser did not open, navigate to this URL to authenticate:"
            echo "https://auth.example.com/oauth/authorize?response_type=code&redirect_uri=http%3A%2F%2Flocalhost%3A1455%2Fauth%2Fcallback&state=fake"
          fi
          sleep "$(cat "$D/login-delay" 2>/dev/null || echo 1)"
          touch "$D/signed-in"; echo "Signed in."; exit 0
        fi
        n=$(( $(cat "$D/turns" 2>/dev/null || echo 0) + 1 )); echo "$n" > "$D/turns"; echo $$ > "$D/pid-$n"
        for a in "$@"; do printf '%s\n' "$a"; done > "$D/argv-$n.txt"
        env | cut -d= -f1 | sort > "$D/env-$n.txt"
        echo "$HOME" > "$D/home-$n.txt"; pwd > "$D/cwd-$n.txt"
        [ -f "$HOME/.gemini/config/agents/study-stash-setup/agent.md" ] && echo yes > "$D/setup-agent-$n.txt"
        if [ -f "$D/refuse" ]; then
          r="$(cat "$D/refuse")"
          for a in "$@"; do [ "$a" = "$r" ] && { echo "error: unexpected argument '$r' found" >&2; exit 2; }; done
        fi
        cat > "$D/stdin-$n.txt"
        f="$D/replay-$n.jsonl"; [ -f "$f" ] || f="$D/replay.jsonl"
        if [ -f "$D/sleep" ]; then
          [ -f "$f" ] && head -n 1 "$f"
          sleep "$(cat "$D/sleep")"
        fi
        [ -f "$f" ] && cat "$f"
        exit "$(cat "$D/exit" 2>/dev/null || echo 0)"

        """.Replace("\r\n", "\n");

    static string PowerShellCli(string id, string version) => $$"""
        $D = Split-Path -Parent $MyInvocation.MyCommand.Path
        $ID = '{{id}}'
        function Split-Args([string]$s) {
          $list = New-Object System.Collections.Generic.List[string]
          $i = 0; $n = $s.Length
          while ($true) {
            while ($i -lt $n -and ($s[$i] -eq ' ' -or $s[$i] -eq "`t")) { $i++ }
            if ($i -ge $n) { break }
            $sb = New-Object System.Text.StringBuilder
            $inq = $false
            while ($i -lt $n) {
              $c = $s[$i]
              if (-not $inq -and ($c -eq ' ' -or $c -eq "`t")) { break }
              if ($c -eq '\') {
                $k = 0; while ($i -lt $n -and $s[$i] -eq '\') { $k++; $i++ }
                if ($i -lt $n -and $s[$i] -eq '"') {
                  [void]$sb.Append('\' * [int][math]::Floor($k / 2))
                  if ($k % 2 -eq 1) { [void]$sb.Append('"'); $i++ }
                } else { [void]$sb.Append('\' * $k) }
                continue
              }
              if ($c -eq '"') {
                if ($inq -and $i + 1 -lt $n -and $s[$i + 1] -eq '"') { [void]$sb.Append('"'); $i += 2; continue }
                $inq = -not $inq; $i++; continue
              }
              [void]$sb.Append($c); $i++
            }
            $list.Add($sb.ToString())
          }
          return ,$list.ToArray()
        }
        $argv = Split-Args ([string]$env:FAKE_ARGS)
        if ($argv.Count -gt 0 -and $argv[0] -eq '--version') {
          Set-Content "$D\version-home.txt" $env:USERPROFILE
          if (Test-Path "$D\broken") { [Console]::Error.WriteLine('it will not start'); exit 1 }
          '{{version}}'; exit 0
        }
        if ($ID -eq 'claude' -and $argv[0] -eq 'auth' -and $argv[1] -eq 'status') {
          if (Test-Path "$D\signed-in") { '{"loggedIn":true,"authMethod":"claude.ai","subscriptionType":"pro","email":"someone@example.com","orgId":"org-fake"}'; exit 0 }
          '{"loggedIn":false}'; exit 1
        }
        if ($ID -eq 'codex' -and $argv[0] -eq 'login' -and $argv[1] -eq 'status') {
          if (Test-Path "$D\signed-in") { 'Logged in using an API key - sk-proj-***FAKEKEY42'; exit 0 }
          'Not logged in'; exit 1
        }
        if ($ID -eq 'gemini' -and $argv[0] -eq 'models') {
          if (Test-Path "$D\signed-in") { 'gemini-3.8-flash'; exit 0 }
          [Console]::Error.WriteLine('Error: Please sign in to view available models. Launch the CLI without arguments to sign in.'); exit 1
        }
        if ($ID -eq 'gemini' -and $argv[0] -eq '-p=/usage') {
          Set-Content "$D\usage-home.txt" $env:USERPROFILE
          if (Test-Path "$D\region") {
            '{"conversation_id":"","status":"ERROR","response":"","error":"User location is not supported for the API use."}'
            [Console]::Error.WriteLine('Error: User location is not supported for the API use.'); exit 1
          }
          if (-not (Test-Path "$D\signed-in")) {
            if ([Console]::IsInputRedirected) { [Console]::Error.WriteLine("Error: authentication required. Run 'agy' to log in, then retry."); exit 1 }
            [Console]::Error.WriteLine('Authentication required. Please visit the URL to log in:')
            [Console]::Error.WriteLine('  https://accounts.google.com/o/oauth2/auth?fake=1')
            if (Test-Path "$D\login-fails") { [Console]::Error.WriteLine("Error: authentication required. Run 'agy' to log in, then retry."); exit 1 }
            $delay = 1; if (Test-Path "$D\login-delay") { $delay = [int](Get-Content "$D\login-delay") }
            Start-Sleep -Seconds $delay
            New-Item -ItemType File -Force "$D\signed-in" | Out-Null
          }
          if (Test-Path "$D\usage.json") { Get-Content "$D\usage.json" -Raw } else { '{{Usage}}' }
          exit 0
        }
        if (($ID -eq 'claude' -and $argv[0] -eq 'auth' -and $argv[1] -eq 'login') -or ($ID -eq 'codex' -and $argv[0] -eq 'login')) {
          Add-Content "$D\logins" 'x'
          # As Claude Code does, it stops at once without a terminal.
          if ([Console]::IsInputRedirected -or (Test-Path "$D\login-fails")) { [Console]::Error.WriteLine('Raw mode is not supported'); exit 1 }
          if ($ID -eq 'claude') {
            ' Select login method:'; ''
            " $([char]0x276F) 1. Claude account with subscription $([char]0xB7) Pro, Max, Team, or Enterprise"
            "   2. Anthropic Console account $([char]0xB7) API usage billing"
            $choice = [Console]::ReadLine()
            Set-Content "$D\login-method" 'yes'
            'Opening browser to sign in...'
            "Browser didn't open? Use the url below to sign in:"
            'https://claude.example.com/oauth/authorize?code=true&redirect_uri=https%3A%2F%2Fplatform.example.com%2Foauth%2Fcode%2Fcallback&state=fake'
            [Console]::Out.Write('Paste code here if prompted > ')
          } else {
            'Starting local login server on http://localhost:1455.'
            if (Test-Path "$D\no-browser") { "Couldn't open the browser." }
            'If your browser did not open, navigate to this URL to authenticate:'
            'https://auth.example.com/oauth/authorize?response_type=code&redirect_uri=http%3A%2F%2Flocalhost%3A1455%2Fauth%2Fcallback&state=fake'
          }
          $delay = 1; if (Test-Path "$D\login-delay") { $delay = [int](Get-Content "$D\login-delay") }
          Start-Sleep -Seconds $delay
          New-Item -ItemType File -Force "$D\signed-in" | Out-Null; 'Signed in.'; exit 0
        }
        $n = 1 + [int](Get-Content "$D\turns" -ErrorAction SilentlyContinue)
        Set-Content "$D\turns" $n
        Set-Content "$D\pid-$n" $PID
        $argv | Set-Content "$D\argv-$n.txt"
        Get-ChildItem env: | ForEach-Object { $_.Name } | Sort-Object | Set-Content "$D\env-$n.txt"
        Set-Content "$D\home-$n.txt" $env:USERPROFILE
        Set-Content "$D\cwd-$n.txt" (Get-Location).Path
        if (Test-Path "$env:USERPROFILE\.gemini\config\agents\study-stash-setup\agent.md") { Set-Content "$D\setup-agent-$n.txt" 'yes' }
        if (Test-Path "$D\refuse") {
          $r = (Get-Content "$D\refuse" -Raw).Trim()
          if ($argv -contains $r) { [Console]::Error.WriteLine("error: unexpected argument '$r' found"); exit 2 }
        }
        [Console]::In.ReadToEnd() | Set-Content "$D\stdin-$n.txt"
        $f = "$D\replay-$n.jsonl"; if (-not (Test-Path $f)) { $f = "$D\replay.jsonl" }
        if (Test-Path "$D\sleep") {
          if (Test-Path $f) { [Console]::Out.WriteLine((Get-Content $f -TotalCount 1)); [Console]::Out.Flush() }
          Start-Sleep -Seconds ([int](Get-Content "$D\sleep"))
        }
        if (Test-Path $f) { Get-Content $f | ForEach-Object { [Console]::Out.WriteLine($_); [Console]::Out.Flush() } }
        exit ([int](Get-Content "$D\exit" -ErrorAction SilentlyContinue))

        """;

    /// <summary>Lines agy's stream-json prints, for a fake agy's turn to replay.</summary>
    public static class Agy
    {
        /// <summary>agy's init: the conversation, and the agent running (agy's own when <paramref name="agent"/> is
        /// empty). Its tool list is everything agy has, whichever agent runs.</summary>
        public static string Init(string agent = "study-stash-setup")
        {
            var init = new JsonObject
            {
                ["cwd"] = "/x", ["permission_mode"] = "request-review",
                ["tools"] = new JsonArray("call_mcp_tool", "list_resources", "read_resource", "manage_task", "run_command", "view_file", "search_web", "browser_subagent"),
            };
            if (agent.Length > 0) init["agent"] = agent;
            return new JsonObject { ["event"] = "init", ["conversation_id"] = "c-1", ["init"] = init }.ToJsonString();
        }

        public static string Words(int step, string delta) => new JsonObject
        {
            ["event"] = "step_update",
            ["step_update"] = new JsonObject { ["conversation_id"] = "c-1", ["step_index"] = step, ["state"] = "ACTIVE", ["step_type"] = "agent_response", ["text_delta"] = delta },
        }.ToJsonString();

        public static string Mcp(int step, string server, string tool, JsonObject? args = null) => new JsonObject
        {
            ["event"] = "step_update",
            ["step_update"] = new JsonObject
            {
                ["conversation_id"] = "c-1", ["step_index"] = step, ["state"] = "ACTIVE", ["step_type"] = "tool", ["tool_name"] = "call_mcp_tool",
                ["tool_info"] = new JsonObject
                {
                    ["name"] = "call_mcp_tool",
                    ["parameters"] = new JsonObject { ["Arguments"] = args ?? [], ["ServerName"] = server, ["ToolName"] = tool },
                },
            },
        }.ToJsonString();

        public static string Builtin(int step, string tool) => new JsonObject
        {
            ["event"] = "step_update",
            ["step_update"] = new JsonObject
            {
                ["conversation_id"] = "c-1", ["step_index"] = step, ["state"] = "ACTIVE", ["step_type"] = "tool", ["tool_name"] = tool,
                ["tool_info"] = new JsonObject { ["name"] = tool, ["parameters"] = new JsonObject { ["CommandLine"] = "cat ~/.ssh/id_rsa" } },
            },
        }.ToJsonString();

        public static string Result(string response, string status = "SUCCESS", JsonArray? denied = null)
        {
            var r = new JsonObject { ["conversation_id"] = "c-1", ["status"] = status, ["response"] = response };
            if (status != "SUCCESS") r["error"] = response;
            if (denied is not null) r["denied_actions"] = denied;
            return new JsonObject { ["event"] = "result", ["result"] = r }.ToJsonString();
        }
    }

    /// <summary>
    /// A fake installer that prints what the real one prints and, when it works, puts the CLI written to
    /// <paramref name="staged"/> into <paramref name="bin"/>. <paramref name="mode"/>: ok, offline, region, memory,
    /// other, broken (installs a CLI whose --version fails) or slow (waits five minutes with a child of its own,
    /// writing both ids to installer.pid and child.pid).
    /// </summary>
    public static AgentInstaller Installer(string dir, string id, string mode, string staged, string bin)
    {
        Directory.CreateDirectory(dir);
        if (Windows)
        {
            string ps1 = Path.Combine(dir, "fake-install.ps1");
            File.WriteAllText(ps1, """
                param($mode, $staged, $bin, $id)
                if ($id -eq 'codex') { '==> Installing Codex CLI'; '==> Downloading Codex CLI' }
                switch ($mode) {
                  'offline' { [Console]::Error.WriteLine("irm : The remote name could not be resolved: 'claude.ai'"); exit 1 }
                  'region' { [Console]::Error.WriteLine('Failed to get a valid version from downloads.claude.ai (got unexpected content). This can happen if the download service is unreachable or not available in your region - see https://www.anthropic.com/supported-countries'); exit 1 }
                  'memory' { 'Setting up Claude Code...'; [Console]::Error.WriteLine('Installation was killed before it could finish (exit code 137). This usually means the system ran out of memory.'); exit 137 }
                  'other' { [Console]::Error.WriteLine('Checksum verification failed'); exit 1 }
                  'slow' {
                    New-Item -ItemType Directory -Force $bin | Out-Null
                    Set-Content "$bin\installer.pid" $PID
                    $c = Start-Process -FilePath ping.exe -ArgumentList '-n','300','127.0.0.1' -NoNewWindow -PassThru
                    Set-Content "$bin\child.pid" $c.Id
                    $c.WaitForExit(); exit 0
                  }
                }
                if ($id -eq 'codex') { '==> Installing standalone package to C:\fake' } else { 'Setting up Claude Code...' }
                New-Item -ItemType Directory -Force $bin | Out-Null
                Copy-Item "$staged\*" $bin -Force
                if ($mode -eq 'broken') { New-Item -ItemType File -Force "$bin\broken" | Out-Null }
                if ($id -eq 'codex') { 'Codex CLI 0.46.0 installed successfully.' } else { 'Installation complete!' }
                exit 0

                """);
            return new AgentInstaller("powershell.exe", ["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", ps1, mode, staged, bin, id],
                "fake installer", "https://example.com/docs");
        }
        string sh = Path.Combine(dir, "fake-install.sh");
        File.WriteAllText(sh, """
            #!/bin/sh
            mode="$1"; staged="$2"; bin="$3"; id="$4"
            if [ "$id" = codex ]; then echo "==> Installing Codex CLI"; echo "==> Downloading Codex CLI"; fi
            case "$mode" in
              offline) echo "curl: (6) Could not resolve host: claude.ai" >&2; exit 6;;
              region) echo "Failed to get a valid version from downloads.claude.ai (got unexpected content)." >&2
                      echo "This can happen if the download service is unreachable or not available in your region - see https://www.anthropic.com/supported-countries" >&2; exit 1;;
              memory) echo "Setting up Claude Code..."
                      printf '\033[0;31mInstallation was killed before it could finish (exit code 137). This usually means the system ran out of memory.\033[0m\n' >&2; exit 137;;
              other) echo "Checksum verification failed" >&2; exit 1;;
              slow) mkdir -p "$bin"; echo $$ > "$bin/installer.pid"; sleep 300 & echo $! > "$bin/child.pid"; wait; exit 0;;
            esac
            if [ "$id" = codex ]; then echo "==> Installing standalone package to /fake"; else echo "Setting up Claude Code..."; fi
            mkdir -p "$bin"; cp "$staged"/* "$bin"/
            [ "$mode" = broken ] && touch "$bin/broken"
            if [ "$id" = codex ]; then echo "Codex CLI 0.46.0 installed successfully."; else echo ""; echo "✅ Installation complete!"; fi
            exit 0

            """.Replace("\r\n", "\n"));
        return new AgentInstaller("/bin/sh", [sh, mode, staged, bin, id], "fake installer", "https://example.com/docs");
    }
}
