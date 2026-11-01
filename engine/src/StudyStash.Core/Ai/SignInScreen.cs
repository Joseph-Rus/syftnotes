using System.Text;
using System.Text.RegularExpressions;

namespace StudyStash.Core.Ai;

/// <summary>
/// What an AI's sign-in shows in its hidden terminal (<see cref="HiddenTerminal"/>), read the way a person at that
/// terminal would read the screen: colours, cursor moves and the terminal's own questions taken out, line by line. From
/// it <see cref="AgentSignIn"/> learns where the sign-in has got to (waiting for the browser, couldn't open one, wants
/// a terminal it can't have, not offered in this country, signed in) and answers the few questions a sign-in asks, the
/// safe way, which a person would otherwise answer with a key:
/// <list type="bullet">
/// <item>Claude Code asking which kind of account: the first choice, "Claude account with subscription", already
/// highlighted, is taken with Enter. It's the one a student's Pro or Max plan signs in with; the others (the Anthropic
/// Console's pay-per-use billing, a company's cloud platform) are for developers and companies, and would leave a
/// student billed by the token or unable to sign in.</item>
/// <item>"Press Enter to open the browser", "Press Enter to continue": Enter, as any person would.</item>
/// <item>"Open the browser? (Y/n)": Enter, the default, yes.</item>
/// </list>
/// Nothing else is ever typed: no code, no address, no password, no "yes" to terms. A question that isn't one of these
/// is left unanswered (the sign-in gives up by itself, or Syft Notes stops waiting).
/// <para>
/// The sign-in page's address is kept (<see cref="Page"/>) only for <see cref="AgentSignIn"/> to hand to the system's
/// browser opener: it is never shown, logged or saved. Nothing else of what was shown is kept beyond the last few lines.
/// </para>
/// </summary>
public sealed partial class SignInScreen
{
    readonly StringBuilder line = new();
    readonly Queue<string> recent = new();
    readonly Dictionary<string, int> answers = [];
    readonly List<string> answered = [];
    readonly StringBuilder escape = new();
    EscapeState state;

    /// <summary>Whether the terminal answers the program's questions about itself (where the cursor is, what it is, its
    /// colours), as a terminal does: on a Mac and Linux. On Windows the console host answers them itself.</summary>
    public bool AnswersQueries { get; init; } = !OperatingSystem.IsWindows();

    /// <summary>Each line as it's finished, plain (a test's record; the app keeps none).</summary>
    public event Action<string>? Line;

    /// <summary>The first sign-in address it showed (a page of the AI's maker, never a help page), or null.</summary>
    internal string? Page { get; private set; }
    /// <summary>It showed its sign-in page or said it's waiting for the student in the browser.</summary>
    public bool Waiting { get; private set; }
    /// <summary>It said it couldn't open the browser itself.</summary>
    public bool NoBrowser { get; private set; }
    /// <summary>It said it needs a real terminal (it stops then).</summary>
    public bool WantsTerminal { get; private set; }
    /// <summary>It said its maker doesn't offer it in this country.</summary>
    public bool Region { get; private set; }
    /// <summary>It said the sign-in worked (its status command still decides).</summary>
    public bool SaysSignedIn { get; private set; }
    /// <summary>The questions it asked that were answered, in plain words ("which kind of account: Claude subscription").</summary>
    public IReadOnlyList<string> Answered => answered;
    /// <summary>Whether the page it showed finishes signing in by itself (it sends the browser back to this computer),
    /// rather than ending on a code to paste: only such a page is ever opened by Syft Notes.</summary>
    public bool PageFinishesByItself => Page is { } p && FinishesByItself(p);

    enum EscapeState { None, Esc, Csi, Osc, OscEsc, Str, StrEsc, Charset }

    /// <summary>Reads more of what it showed; what to type back ("" for nothing).</summary>
    public string Read(string chunk)
    {
        var reply = new StringBuilder();
        foreach (char c in chunk)
        {
            switch (state)
            {
                case EscapeState.Esc:
                    state = c switch
                    {
                        '[' => EscapeState.Csi,
                        ']' => EscapeState.Osc,
                        'P' or '_' or '^' or 'X' => EscapeState.Str,
                        '(' or ')' or '*' or '+' or '#' or '%' => EscapeState.Charset,
                        _ => EscapeState.None,
                    };
                    escape.Clear();
                    continue;
                case EscapeState.Charset:
                    state = EscapeState.None;
                    continue;
                case EscapeState.Csi:
                    if (c is >= '@' and <= '~')
                    {
                        state = EscapeState.None;
                        Csi(escape.ToString(), c, reply);
                    }
                    else if (escape.Length < 64) escape.Append(c);
                    continue;
                case EscapeState.Osc:
                    if (c == '\a') Osc(reply);
                    else if (c == '\x1b') state = EscapeState.OscEsc;
                    else if (escape.Length < 4096) escape.Append(c);
                    continue;
                case EscapeState.OscEsc:
                    if (c == '\\') Osc(reply);
                    else state = EscapeState.None;
                    continue;
                case EscapeState.Str:
                    if (c == '\x1b') state = EscapeState.StrEsc;
                    else if (c == '\a') state = EscapeState.None;
                    continue;
                case EscapeState.StrEsc:
                    state = c == '\\' ? EscapeState.None : EscapeState.Str;
                    continue;
            }
            switch (c)
            {
                case '\x1b':
                    state = EscapeState.Esc;
                    break;
                case '\n' or '\r':
                    EndLine(reply);
                    break;
                case '\b':
                    if (line.Length > 0) line.Length--;
                    break;
                case '\t':
                    line.Append(' ');
                    break;
                case < ' ' or '\x7f':
                    break;
                default:
                    if (line.Length < 8192) line.Append(c);
                    break;
            }
        }
        // A question waits on a line of its own, with no line end after it.
        if (line.Length > 0) Look(line.ToString(), finished: false, reply);
        return reply.ToString();
    }

    void Csi(string body, char final, StringBuilder reply)
    {
        switch (final)
        {
            // Cursor right: a console host draws runs of spaces this way.
            case 'C' when int.TryParse(body.Length == 0 ? "1" : body, out int n) && n is > 0 and < 400:
                line.Append(' ', n);
                break;
            // Cursor put somewhere else (up, down, to a column, to a place), or the screen or line wiped: what was on
            // the line is finished (a program that draws a menu again moves up and wipes each line first).
            case 'H' or 'f' or 'J' or 'K' or 'A' or 'B' or 'E' or 'F' or 'G' or 'd':
                EndLine(reply);
                break;
            // Where's the cursor? What terminal is this? Answered as a terminal answers, or a program could wait.
            case 'n' when body == "6" && AnswersQueries:
                reply.Append("\x1b[1;1R");
                break;
            case 'c' when body is "" or "0" && AnswersQueries:
                reply.Append("\x1b[?1;2c");
                break;
        }
    }

    void Osc(StringBuilder reply)
    {
        state = EscapeState.None;
        string body = escape.ToString();
        // Its colours, asked for its theme: a light terminal's.
        if (AnswersQueries && body is "10;?" or "11;?")
            reply.Append($"\x1b]{body[..2]};rgb:{(body[1] == '0' ? "0000/0000/0000" : "ffff/ffff/ffff")}\x1b\\");
        // A link's address (OSC 8): the sign-in page, when it's one, as if it was shown.
        else if (body.StartsWith("8;", StringComparison.Ordinal) && body.IndexOf(';', 2) is var at and > 0) Address(body[(at + 1)..]);
    }

    void EndLine(StringBuilder reply)
    {
        if (line.Length == 0) return;
        string done = line.ToString().TrimEnd();
        line.Clear();
        if (done.Trim().Length == 0) return;
        Look(done, finished: true, reply);
        recent.Enqueue(done);
        while (recent.Count > 40) recent.Dequeue();
        Line?.Invoke(done);
    }

    void Look(string text, bool finished, StringBuilder reply)
    {
        if (Page is null && text.Contains("https://", StringComparison.Ordinal))
            foreach (Match m in AddressIn().Matches(text)) Address(m.Value);
        if (Page is not null || WaitingWords().IsMatch(text)) Waiting = true;
        if (NoBrowserWords().IsMatch(text)) NoBrowser = true;
        if (TerminalWords().IsMatch(text)) WantsTerminal = true;
        if (AgentCli.LooksLikeRegion(text)) Region = true;
        if (SignedInWords().IsMatch(text)) SaysSignedIn = true;

        // Claude Code's "Select login method": Enter only while the Claude subscription is the highlighted choice.
        if (Highlighted().IsMatch(text) && SubscriptionChoice().IsMatch(text) && recent.Any(r => LoginMethodWords().IsMatch(r)))
            Answer("which kind of account: Claude subscription", "\r", reply);
        else if (PressEnterWords().IsMatch(text))
            Answer("press Enter", "\r", reply, times: 2);
        else if (!finished && OpenBrowserQuestion().IsMatch(text))
            Answer("open the browser: yes", "\r", reply);
    }

    void Answer(string what, string keys, StringBuilder reply, int times = 1)
    {
        int n = answers.GetValueOrDefault(what);
        if (n >= times) return;
        answers[what] = n + 1;
        if (n == 0) answered.Add(what);
        reply.Append(keys);
    }

    void Address(string found)
    {
        if (Page is not null) return;
        string url = found.TrimEnd('.', ',', ')', '"', '\'', '>', ']');
        if (Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == "https" && SignInAddress().IsMatch(url)) Page = url;
    }

    /// <summary>Whether a sign-in page sends the browser back to this computer when it's done (its redirect_uri is
    /// localhost), so it finishes signing in by itself. Claude Code prints a page that ends on a code to paste, which
    /// Syft Notes never relays, so that one is never opened.</summary>
    public static bool FinishesByItself(string page)
    {
        if (!Uri.TryCreate(page, UriKind.Absolute, out var u)) return false;
        foreach (string part in u.Query.TrimStart('?').Split('&'))
        {
            int eq = part.IndexOf('=');
            if (eq <= 0 || !part[..eq].Equals("redirect_uri", StringComparison.OrdinalIgnoreCase)) continue;
            string back = Uri.UnescapeDataString(part[(eq + 1)..].Replace('+', ' '));
            return Uri.TryCreate(back, UriKind.Absolute, out var b) && (b.IsLoopback || b.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase));
        }
        return false;
    }

    [GeneratedRegex(@"https://[^\s""'<>\x00-\x1f]+")]
    private static partial Regex AddressIn();

    // A page of the maker's own to sign in on, never a help or pricing page it mentions.
    [GeneratedRegex(@"oauth|authori[sz]e|/auth\b|/auth/|accounts\.google\.com|login|log-in|signin|sign-in|device", RegexOptions.IgnoreCase)]
    private static partial Regex SignInAddress();

    [GeneratedRegex(@"waiting for (you|the browser|your browser|authentication|authori[sz]ation|sign[- ]?in|login)|"
        + @"(complete|finish|continue) (the |your )?(sign[- ]?in|log ?in|login|authentication) in (the |your )?browser|"
        + @"opening (the |your )?(default )?browser|browser didn't open|if your browser (did not|didn't|does not) open|navigate to this url|"
        + @"visit (the|this) url|open (this|the following) (url|link)|paste code here",
        RegexOptions.IgnoreCase)]
    private static partial Regex WaitingWords();

    [GeneratedRegex(@"(couldn't|could not|can't|cannot|unable to|failed to|not able to) (automatically )?(open|launch|start) (the |a |your )?(web |default )?browser|"
        + @"no (web )?browser (was )?(found|available|detected)",
        RegexOptions.IgnoreCase)]
    private static partial Regex NoBrowserWords();

    [GeneratedRegex(@"raw mode is not supported|not a tty|stdin is not a (tty|terminal)|input is not a terminal|isn't a terminal|"
        + @"requires an? (interactive )?(terminal|tty)|must be run (in|from) an? (interactive )?(terminal|tty)|no tty",
        RegexOptions.IgnoreCase)]
    private static partial Regex TerminalWords();

    [GeneratedRegex(@"login successful|logged in successfully|successfully (logged|signed) in|signed in successfully|you are (now )?(logged|signed) in|"
        + @"authentication (complete|completed|successful|succeeded)",
        RegexOptions.IgnoreCase)]
    private static partial Regex SignedInWords();

    [GeneratedRegex(@"select (a |your )?login method|how (would|do) you (like to )?(log|sign) ?in|choose (a |your )?(login|sign[- ]in) method", RegexOptions.IgnoreCase)]
    private static partial Regex LoginMethodWords();

    [GeneratedRegex(@"^\s*[❯›>▶►]")]
    private static partial Regex Highlighted();

    [GeneratedRegex(@"Claude account with subscription|Claude (Pro|Max)? ?subscription", RegexOptions.IgnoreCase)]
    private static partial Regex SubscriptionChoice();

    [GeneratedRegex(@"press (enter|return)( key)? to (continue|open|sign in|log ?in|launch|authenticate)", RegexOptions.IgnoreCase)]
    private static partial Regex PressEnterWords();

    // Only a question whose default is yes ("(Y/n)"): Enter takes the default, and nothing is typed for any other.
    [GeneratedRegex(@"open (the |your |a )?(default )?(web )?browser.*\(Y/n\)")]
    private static partial Regex OpenBrowserQuestion();
}
