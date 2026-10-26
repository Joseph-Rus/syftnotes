using System.Text;
using System.Text.RegularExpressions;
using StudyStash.Core.Rich;

namespace StudyStash.Core;

/// <summary>
/// Notes from an AI the library doesn't run itself: the Claude app or ChatGPT writing them through Egghead Notes'
/// connection (<c>save_notes</c>), or any free chat on the web, with the student carrying the lecture there and the
/// notes back ("Copy for your AI", "Paste notes"). What the AI is asked is the library's own notes prompt
/// (<see cref="Summarize.Brief"/>), and what comes back is tidied of what chats put round notes before it's saved.
/// </summary>
public static partial class ChatNotes
{
    /// <summary>The most a copy for a free chat holds in one go, the request included: about 17,000 tokens, which leaves
    /// room for the notes in free ChatGPT's window (about 27,000), and is most lectures of an hour.</summary>
    public const int OneMessage = 60_000;

    /// <summary>How much of the transcript each part of a longer lecture's copy holds.</summary>
    public const int PartChars = 30_000;

    /// <summary>The words that end what an AI app is told before it writes a lecture's notes for the library.</summary>
    public const string SaveWith = "When the notes are written, save them with save_notes: the lecture's id, and the notes as Markdown, nothing else.";

    /// <summary>
    /// What "Copy for your AI" puts on the clipboard: the library's notes prompt (<paramref name="brief"/>), then what
    /// was said. A lecture too long for one message of a free chat comes in parts, each a message of its own: the first
    /// says how many there are and asks the chat to wait, the last asks for the notes. The transcript goes without its
    /// times, as it does to the library's own AI.
    /// </summary>
    public static List<string> Request(string brief, string transcript, bool forSchool = true)
    {
        string text = Py.Strip(TimedText.Plain(transcript));
        string hint = Speakers.HasLabels(text) ? Speakers.NotesHint + "\n\n" : "";
        string Said(string s) => forSchool ? s : Summarize.ForMeetings(s);
        if (brief.Length + hint.Length + text.Length + 16 <= OneMessage) return [Said($"{brief}\n\nTranscript:\n{hint}{text}")];
        var parts = Summarize.SplitTranscript(text, PartChars);
        int n = parts.Count;
        var copies = new List<string>();
        for (int i = 0; i < n; i++)
        {
            int k = i + 1;
            var sb = new StringBuilder();
            if (i == 0)
                sb.Append($"I'm sending you a lecture's transcript in {n} parts, because it's long. Read each part, and until the last one reply only \"Got part {k} of {n}.\"\n\n")
                    .Append(brief).Append("\n\n").Append(hint);
            sb.Append($"Transcript, part {k} of {n}:\n").Append(parts[i]);
            sb.Append(k < n
                ? $"\n\n(Reply only \"Got part {k} of {n}.\" and wait for part {k + 1}.)"
                : "\n\nThat's the whole transcript. Now write the study notes for the whole lecture, as asked in my first message.");
            copies.Add(Said(sb.ToString()));
        }
        return copies;
    }

    // --- what chats put round notes ---------------------------------------------------------------------------------

    /// <summary>A chat's own words before the notes: "Sure! Here are your study notes:", "Certainly. Below is…".</summary>
    [GeneratedRegex(@"^\s*(?:\*\*)?(?:sure|certainly|of course|absolutely|great|okay|ok|alright|got it|perfect|here(?:'s|’s| is| are)|below (?:is|are)|i(?:'ve|’ve| have) (?:written|put|made|created|turned)|i(?:'ll|’ll| will)|happy to|thanks|thank you)\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex ChatOpener();

    /// <summary>A chat's own words after the notes: offers, wishes and questions back.</summary>
    [GeneratedRegex(@"^\s*(?:\*\*)?(?:let me know|i hope|hope (?:this|that|these)|would you like|do you want|want me to|feel free|if you(?:'d| would|’d) like|if you (?:want|wish|need)|want\b|happy studying|good luck|shall i|should i|i can (?:also )?(?:make|turn|create|add|quiz|help)|i could also|need anything else|anything else)\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex ChatCloser();

    /// <summary>Citations some chats leave in what's copied: 【4:0†source】, :contentReference[oaicite:0]{index=0}.</summary>
    [GeneratedRegex(@"【[^】\n]{0,60}】|:contentReference\[oaicite:\d+\]\{index=\d+\}")]
    private static partial Regex Citation();

    [GeneratedRegex(@"^(\s*)[•◦▪●‣∙·]\s+")]
    private static partial Regex OddBullet();

    [GeneratedRegex(@"^\s{0,3}(?:-\s*){3,}$|^\s{0,3}(?:\*\s*){3,}$|^\s{0,3}(?:_\s*){3,}$")]
    private static partial Regex Rule();

    [GeneratedRegex(@"\\\((.+?)\\\)")]
    private static partial Regex InlineMaths();

    [GeneratedRegex(@"\\\[(.+?)\\\]", RegexOptions.Singleline)]
    private static partial Regex BlockMaths();

    [GeneratedRegex(@"^(#{1,6})(\s+.*)$")]
    private static partial Regex HeadingLine();

    /// <summary>A line that's only bold words naming one of the notes' own sections ("**Key points**", "**Summary:**").</summary>
    [GeneratedRegex(@"^\s*\*\*(Summary|Key points|Key ideas|Definitions|Key terms|Details and examples|Examples|Announcements|Questions to review|Review questions|Decisions|Action items|Discussion|Open questions)\s*:?\s*\*\*\s*:?\s*$",
        RegexOptions.IgnoreCase)]
    private static partial Regex BoldSection();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex Blanks();

    /// <summary>A line notes start with: a heading, a bullet, a numbered line, a quote, a table, a fence or maths.</summary>
    static bool LooksLikeNotes(string line)
    {
        string t = line.TrimStart();
        return t.StartsWith('#') || t.StartsWith("- ", StringComparison.Ordinal) || t.StartsWith("* ", StringComparison.Ordinal)
            || t.StartsWith("```", StringComparison.Ordinal) || t.StartsWith('|') || t.StartsWith('>') || t.StartsWith("$$", StringComparison.Ordinal)
            || OddBullet().IsMatch(t) || (t.Length > 2 && char.IsDigit(t[0]) && t.IndexOf(". ", StringComparison.Ordinal) is > 0 and < 4)
            || BoldSection().IsMatch(t);
    }

    /// <summary>
    /// Notes as a chat wrote them, tidied for the library: the chat's own words before and after them ("Here are your
    /// notes:", "Let me know if…") and a fence round the whole of them come off; its citations go; its maths in
    /// \( \) and \[ \] becomes $ and $$, as the library writes it; odd bullets (•) become Markdown's, rules between
    /// sections go, a bold line naming a section becomes its heading; a title over the notes goes (the page has
    /// one), and the headings start at ##, as the library's do. What's inside a block of code is left as it is.
    /// "" when nothing's left.
    /// </summary>
    public static string Clean(string? pasted)
    {
        string t = (pasted ?? "").ReplaceLineEndings("\n").Replace('\u00A0', ' ').Replace("\u200B", "").Replace("\uFEFF", "");
        t = Py.Strip(Summarize.Thinking().Replace(t, ""));
        if (t.Length == 0) return "";
        t = Py.Strip(Summarize.Unwrapped(Py.Strip(DropTail(DropLead(t)))));
        t = Citation().Replace(t, "");

        // Line by line, outside blocks of code.
        var lines = t.Split('\n').ToList();
        var output = new List<string>();
        bool fenced = false;
        var prose = new List<string>();
        void Flush()
        {
            if (prose.Count == 0) return;
            string chunk = string.Join("\n", prose);
            chunk = BlockMaths().Replace(chunk, m => "$$" + string.Join(" ", m.Groups[1].Value.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0)) + "$$");
            chunk = InlineMaths().Replace(chunk, m => "$" + m.Groups[1].Value.Trim() + "$");
            output.AddRange(chunk.Split('\n'));
            prose.Clear();
        }
        foreach (string raw in lines)
        {
            string line = raw.TrimEnd();
            if (NoteBlocks.Fence(line) is not null)
            {
                Flush();
                fenced = !fenced;
                output.Add(line);
                continue;
            }
            if (fenced)
            {
                output.Add(raw);
                continue;
            }
            if (Rule().IsMatch(line)) continue;
            line = OddBullet().Replace(line, "$1- ");
            if (BoldSection().Match(line) is { Success: true } bold) line = "## " + bold.Groups[1].Value;
            prose.Add(line);
        }
        Flush();

        // Headings: a title over the notes goes, and the rest start at ## (a chat's "# Summary" or "### Summary").
        var heads = new List<(int At, int Level)>();
        fenced = false;
        for (int i = 0; i < output.Count; i++)
        {
            if (NoteBlocks.Fence(output[i]) is not null) fenced = !fenced;
            else if (!fenced && HeadingLine().Match(output[i]) is { Success: true } h) heads.Add((i, h.Groups[1].Length));
        }
        int firstText = output.FindIndex(l => l.Trim().Length > 0);
        if (heads.Count > 1 && heads[0].At == firstText && heads[0].Level == 1 && heads.Skip(1).All(h => h.Level > 1))
        {
            output[heads[0].At] = "\u0000";
            heads.RemoveAt(0);
        }
        if (heads.Count > 0)
        {
            int shift = 2 - heads.Min(h => h.Level);
            if (shift != 0)
                foreach (var (at, level) in heads)
                {
                    var h = HeadingLine().Match(output[at]);
                    output[at] = new string('#', Math.Clamp(level + shift, 1, 6)) + h.Groups[2].Value;
                }
        }
        t = string.Join("\n", output.Where(l => l != "\u0000"));
        return Py.Strip(Blanks().Replace(t, "\n\n"));
    }

    /// <summary>The notes without the chat's words before them: up to three lines of talk before the first line
    /// that reads as notes, when there are notes after them.</summary>
    static string DropLead(string t)
    {
        var lines = t.Split('\n');
        int first = Array.FindIndex(lines, l => l.Trim().Length > 0 && LooksLikeNotes(l));
        if (first <= 0) return t;
        var before = lines[..first].Where(l => l.Trim().Length > 0).ToList();
        if (before.Count > 3 || before.Any(l => l.Length > 240)) return t;
        // The first line before the notes is the chat speaking ("Sure!"), or every line before them ends as a lead-in does.
        bool chat = ChatOpener().IsMatch(before[0]) || before.All(l => l.TrimEnd().EndsWith(':'));
        return chat ? string.Join("\n", lines[first..]) : t;
    }

    /// <summary>The notes without the chat's words after them: its last lines while they offer more, wish luck or ask
    /// something back (and a rule or a closing fence's blank lines before them).</summary>
    static string DropTail(string t)
    {
        var lines = t.TrimEnd().Split('\n').ToList();
        int end = lines.Count;
        int kept = end;
        while (end > 0)
        {
            string line = lines[end - 1].Trim();
            if (line.Length == 0 || Rule().IsMatch(line)) end--;
            else if (line.Length <= 400 && ChatCloser().IsMatch(line) && !LooksLikeNotes(line)) kept = --end;
            else break;
        }
        // Only the chat's own words go: blank lines and rules before them stay unless something was taken.
        return kept == lines.Count ? t.TrimEnd() : string.Join("\n", lines.Take(end));
    }
}
