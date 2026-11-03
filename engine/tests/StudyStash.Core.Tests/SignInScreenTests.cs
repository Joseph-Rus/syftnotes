using StudyStash.Core.Ai;

namespace StudyStash.Core.Tests;

/// <summary>
/// Reading an AI's sign-in the way a person reads the screen (<see cref="SignInScreen"/>), from recordings of what the
/// real Claude Code, Codex and Antigravity CLI showed in the hidden terminal on CI's Mac, Windows and Linux runners
/// (the "Real sign-ins, hidden" job), written out by hand with every address, client id and code replaced. Each is fed
/// in pieces cut in awkward places, as a terminal hands it over.
/// </summary>
public class SignInScreenTests
{
    const string ClaudePage = "https://claude.example.com/oauth/authorize?code=true&client_id=00000000-0000-0000-0000-000000000000&response_type=code"
        + "&redirect_uri=https%3A%2F%2Fplatform.example.com%2Foauth%2Fcode%2Fcallback&scope=user%3Aprofile+user%3Ainference&code_challenge=CHALLENGE&code_challenge_method=S256&state=STATE";
    const string CodexPage = "https://auth.example.com/oauth/authorize?response_type=code&client_id=app_CLIENT&redirect_uri=http%3A%2F%2Flocalhost%3A1455%2Fauth%2Fcallback"
        + "&scope=openid%20profile%20email%20offline_access&code_challenge=CHALLENGE&code_challenge_method=S256&id_token_add_organizations=true&codex_cli_simplified_flow=true&state=STATE";
    // On CI its page didn't send the browser back to the computer (it shows a code to paste): one Syft Notes never opens.
    const string GooglePage = "https://accounts.google.com/o/oauth2/v2/auth?client_id=CLIENT.apps.googleusercontent.com&redirect_uri=https%3A%2F%2Fcodeassist.example.com%2Fauthcode"
        + "&response_type=code&scope=openid%20email&code_challenge=CHALLENGE&code_challenge_method=S256&state=STATE";

    /// <summary>Claude Code's `claude auth login`, as it showed on the Mac and Linux runners: it opened the browser itself
    /// and printed the page that ends on a code to paste.</summary>
    static readonly string Claude = "\x1b[?25l\x1b[2mOpening browser to sign in…\x1b[22m\r\n"
        + $"If the browser didn't open, visit: \x1b[4m{ClaudePage}\x1b[24m\r\n";

    /// <summary>Codex's `codex login`, the same on all three.</summary>
    static readonly string Codex = "Starting local login server on http://localhost:1455.\r\n"
        + "If your browser did not open, navigate to this URL to authenticate:\r\n\r\n"
        + CodexPage + "\r\n\r\n"
        + "On a remote or headless machine? Use `codex login --device-auth` instead.\r\n";

    /// <summary>Antigravity's `agy -p=/usage`, signed out, the same on all three: it opened the browser itself (it listens
    /// for it to come back), and ends on a question that's never answered (a code to paste); it gives up after a minute.</summary>
    static readonly string Agy = "Authentication required. Please visit the URL to log in:\r\n"
        + $"  {GooglePage}\r\n"
        + "Waiting for authentication (timeout 60s)...\r\n"
        + "Or, paste the authorization code here and press Enter: ";

    // Plain agy's page, as agy 1.4.12 opened it: Google's own sign-in, sent back to a page on antigravity.google that
    // shows the code to paste ("Paste this code into your application"), never to this computer.
    const string AgyPage = "https://accounts.google.com/o/oauth2/auth?access_type=offline&client_id=CLIENT.apps.googleusercontent.com&code_challenge=CHALLENGE"
        + "&code_challenge_method=S256&prompt=consent&redirect_uri=https%3A%2F%2Fagy.example.com%2Foauth-callback&response_type=code&scope=openid&state=STATE";

    /// <summary>Plain `agy` (Antigravity CLI 1.4.12), signed out, in a pseudo-terminal on a Mac: its login method asked,
    /// then, once Enter takes Google, the page as a link (OSC 8) wrapped over lines, and a field for the code. It waits
    /// there as long as it's left open.</summary>
    static readonly string AgyInteractive = "\x1b[?2026$p\x1b[?2027$p\x1b[>4m\r\n\n Welcome to the \x1b[38;5;69mAntigravity CLI\x1b[m. You are currently not signed in.\r\n\n"
        + " \u28f7  Signing in...\r Select login method:\r\n > 1. Google OAuth\n\x1b[4G2. Use a Google Cloud project\n\n\x1b[4G\x1b[94;1m\u2191/\u2193\x1b[90;22m Navigate \u00b7 \x1b[94;1menter\x1b[90;22m Select\x1b[m"
        + "\x1b[8;2HYour browser should open automatically. If not:\x1b[K\r\n\n"
        + $" \x1b[34;4m\x1b]8;id=jk1;{AgyPage}\a{AgyPage[..60]}\x1b[m\x1b]8;;\a\r\n"
        + $" \x1b[34;4m\x1b]8;id=jk1;{AgyPage}\a{AgyPage[60..]}\x1b[m\x1b]8;;\a\r\n\n"
        + " \x1b[90mCopy and paste the URL or click on the link below:\x1b[m\r\n"
        + $" \x1b[34;1;4m\x1b]8;id=jk2;{AgyPage}\a\u2192\x1b[22m \x1b[1mClick\x1b[22m \x1b[1mhere\x1b[22m \x1b[1mto\x1b[22m \x1b[1mauthenticate\x1b[m\x1b]8;;\a\r\n\n"
        + " If you aren't automatically redirected, paste the authorization code below:\r\n\n"
        + " \x1b[37;7ma\x1b[38;5;240;27muthorization code...\x1b[m\r\n\n\n  \x1b[94;1mshift+up/down\x1b[90;22m Navigate\x1b[m\r\x1b[3A";

    /// <summary>Codex on the Windows runner: the console host draws the screen, with its own title, colours, cursor moves
    /// and runs of spaces as cursor-right.</summary>
    static readonly string CodexOnWindows = "\x1b[?9001h\x1b[?1004h\x1b[?25l\x1b[2J\x1b[m\x1b[H\x1b]0;C:\\Users\\runneradmin\\AppData\\Local\\Programs\\OpenAI\\Codex\\bin\\codex.exe\a\x1b[?25h"
        + "Starting\x1b[1Clocal\x1b[1Clogin\x1b[1Cserver\x1b[1Con\x1b[1Chttp://localhost:1455.\x1b[K\r\n"
        + "If your browser did not open, navigate to this URL to authenticate:\x1b[K\r\n\x1b[K\r\n"
        + CodexPage + "\x1b[K\r\n\x1b[K\r\n"
        + "On a remote or headless machine? Use `codex login --device-auth` instead.\x1b[K\r\n";

    /// <summary>Feeds <paramref name="shown"/> in pieces of <paramref name="size"/>; everything typed back.</summary>
    static string Feed(SignInScreen screen, string shown, int size = 7)
    {
        var typed = new System.Text.StringBuilder();
        for (int at = 0; at < shown.Length; at += size) typed.Append(screen.Read(shown.Substring(at, Math.Min(size, shown.Length - at))));
        return typed.ToString();
    }

    [Theory]
    [InlineData("claude", false)]
    [InlineData("codex", true)]
    [InlineData("agy", false)]
    [InlineData("codex-windows", true)]
    public void Each_real_sign_in_reads_as_waiting_for_the_browser_with_nothing_typed(string which, bool finishesByItself)
    {
        string shown = which switch { "claude" => Claude, "codex" => Codex, "agy" => Agy, _ => CodexOnWindows };
        foreach (int size in new[] { 1, 7, 4096 })
        {
            var screen = new SignInScreen { AnswersQueries = true };
            Assert.Equal("", Feed(screen, shown, size));
            Assert.True(screen.Waiting);
            Assert.False(screen.WantsTerminal);
            Assert.False(screen.NoBrowser);
            Assert.Empty(screen.Answered);
            Assert.Equal(which switch { "claude" => ClaudePage, "agy" => GooglePage, _ => CodexPage }, screen.Page);
            // Only a page that sends the browser back to this computer may be opened by Syft Notes.
            Assert.Equal(finishesByItself, screen.PageFinishesByItself);
        }
    }

    [Fact]
    public void Plain_agy_is_answered_with_Google_once_and_then_asks_for_the_code_its_page_ends_on()
    {
        foreach (int size in new[] { 1, 7, 4096 })
        {
            var screen = new SignInScreen { AnswersQueries = true };
            // One Enter, for "Google OAuth" (highlighted), and nothing else, however the screen comes.
            Assert.Equal("\r", Feed(screen, AgyInteractive, size));
            Assert.Equal(["which login method: Google account"], screen.Answered);
            Assert.True(screen.Waiting);
            Assert.True(screen.AsksForCode);
            Assert.False(screen.CodeRefused);
            Assert.False(screen.WantsTerminal);
            // Its page, whole, from the link (it's wrapped on the screen); one that doesn't come back to this computer.
            Assert.Equal(AgyPage, screen.Page);
            Assert.False(screen.PageFinishesByItself);

            // A code Google turned down: it says so, and waits for a key that nobody presses.
            Assert.Equal("", Feed(screen, "\r\x1b[7d\x1b[17M\x1b[K\n \u28df  Signing in...\r \u287f \r\n\x1b[2L\x1bM \x1b[31mGot an error:\x1b[m token exchange failed: "
                + "oauth2: \"invalid_grant\" \"Bad Request\"\r\n\n Press any key to go back.", size));
            Assert.True(screen.CodeRefused);
        }
        // The other login method highlighted instead: nothing is pressed.
        var other = new SignInScreen();
        Assert.Equal("", Feed(other, " Select login method:\r\n   1. Google OAuth\r\n > 2. Use a Google Cloud project\r\n"));
    }

    [Theory]
    [InlineData("4/0AbCdEf-123_xyz", "4/0AbCdEf-123_xyz")]
    [InlineData("  4/0AbCdEf\n-123_xyz \r\n", "4/0AbCdEf-123_xyz")]
    [InlineData("", null)]
    [InlineData("abc", null)]
    [InlineData("4/0Ab\u0007Cd", null)]
    public void A_pasted_code_is_cleaned_of_spaces_and_line_breaks_and_anything_else_is_refused(string pasted, string? code) =>
        Assert.Equal(code, AgentSignIn.CleanCode(pasted));

    [Fact]
    public void Claude_Codes_kind_of_account_is_answered_with_the_subscription_only_while_it_is_highlighted()
    {
        const string Menu = "\x1b[1m Select login method:\x1b[22m\r\n\r\n"
            + " \x1b[36m❯ 1. Claude account with subscription · Pro, Max, Team, or Enterprise\x1b[39m\r\n"
            + "   2. Anthropic Console account · API usage billing\r\n"
            + "   3. 3rd-party platform · Amazon Bedrock, Microsoft Foundry, or Vertex AI\r\n";
        var screen = new SignInScreen();
        Assert.Equal("\r", Feed(screen, Menu));
        Assert.Equal(["which kind of account: Claude subscription"], screen.Answered);
        // Drawn again (Ink moves up and wipes each line first): still only the one Enter.
        Assert.Equal("", Feed(screen, "\x1b[2K\x1b[1A\x1b[2K\x1b[1A\x1b[2K\x1b[G" + Menu));

        // The Console's billing highlighted instead: nothing is pressed.
        var other = new SignInScreen();
        Assert.Equal("", Feed(other, Menu.Replace("❯ 1.", "  1.", StringComparison.Ordinal).Replace("   2.", " ❯ 2.", StringComparison.Ordinal)));
        Assert.Empty(other.Answered);
    }

    [Fact]
    public void Press_Enter_and_a_yes_to_the_browser_are_answered_and_nothing_else_ever_is()
    {
        var screen = new SignInScreen();
        Assert.Equal("\r", Feed(screen, "Press Enter to open your browser…"));
        Assert.Equal("\r", Feed(new SignInScreen(), "Open the browser to sign in? (Y/n) "));
        // A question whose default is no, a code, terms: never answered.
        Assert.Equal("", Feed(new SignInScreen(), "Open the browser? (y/N) "));
        Assert.Equal("", Feed(new SignInScreen(), "Paste code here if prompted > "));
        Assert.Equal("", Feed(new SignInScreen(), "Do you accept the Terms of Service? (y/n) "));
    }

    [Fact]
    public void A_sign_in_that_wants_a_terminal_or_couldnt_open_the_browser_or_isnt_offered_here_says_so()
    {
        var raw = new SignInScreen();
        Feed(raw, "\x1b[31mError: Raw mode is not supported on the current process.stdin, which Ink uses as input stream by default.\x1b[39m\r\n");
        Assert.True(raw.WantsTerminal);

        var noBrowser = new SignInScreen();
        Feed(noBrowser, "Starting local login server on http://localhost:1455.\r\nCouldn't open the browser.\r\n" + CodexPage + "\r\n");
        Assert.True(noBrowser.NoBrowser);
        Assert.True(noBrowser.PageFinishesByItself);

        var region = new SignInScreen();
        Feed(region, "Error: User location is not supported for the API use.\r\n");
        Assert.True(region.Region);
        Assert.False(region.Waiting);
    }

    [Fact]
    public void The_terminals_own_questions_are_answered_as_a_terminal_would_on_a_Mac_and_Linux_only()
    {
        Assert.Equal("\x1b[1;1R\x1b[?1;2c", Feed(new SignInScreen { AnswersQueries = true }, "\x1b[6n\x1b[c", size: 1));
        Assert.Equal("\x1b]11;rgb:ffff/ffff/ffff\x1b\\", Feed(new SignInScreen { AnswersQueries = true }, "\x1b]11;?\x1b\\"));
        // Windows' console host answers them itself.
        Assert.Equal("", Feed(new SignInScreen { AnswersQueries = false }, "\x1b[6n\x1b[c\x1b]11;?\a"));
    }

    [Fact]
    public void A_help_page_it_mentions_is_never_taken_for_its_sign_in_page()
    {
        var screen = new SignInScreen();
        Feed(screen, "See https://docs.example.com/claude-code/setup for help.\r\n" + Codex);
        Assert.Equal(CodexPage, screen.Page);
    }
}
