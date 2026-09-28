namespace StudyStash.Core.Rich;

/// <summary>
/// An answer still being written, as it can be shown: what's finished reads as it will in the end, and what's half
/// written doesn't flash its raw source. An inline formula waits for its closing <c>$</c>; a diagram or <c>$$</c>
/// formula still open at the end is left for the note to show as being drawn (see the app's NoteView).
/// </summary>
public static class PartialText
{
    /// <summary>How long an unclosed inline formula may run before it's taken for a lone $ (a price) and shown.</summary>
    const int LongestOpenFormula = 300;

    /// <summary>The text so far, without the inline formula its last paragraph is in the middle of.</summary>
    public static string Showable(string text)
    {
        if (NoteBlocks.Find(text) is [.., { Closed: false }]) return text; // inside a block the note shows as it comes
        int paragraph = text.LastIndexOf("\n\n", StringComparison.Ordinal);
        int open = -1;
        bool openDouble = false;
        for (int i = paragraph < 0 ? 0 : paragraph + 2; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\\')
            {
                i++;
                continue;
            }
            if (c == '`')
            {
                // A code span's $ is just a character; one still open holds nothing back.
                int close = text.IndexOf('`', i + 1);
                if (close < 0) return text;
                i = close;
                continue;
            }
            if (c != '$') continue;
            bool isDouble = i + 1 < text.Length && text[i + 1] == '$';
            if (open < 0)
            {
                open = i;
                openDouble = isDouble;
            }
            else if (isDouble == openDouble) open = -1;
            if (isDouble) i++;
        }
        return open >= 0 && text.Length - open <= LongestOpenFormula ? text[..open] : text;
    }

    /// <summary>What stays of an answer that stopped partway: <see cref="Showable"/>'s text, without a diagram or
    /// <c>$$</c> formula at the end that will now never be finished.</summary>
    public static string Ended(string text)
    {
        var lines = NoteBlocks.Lines(text);
        if (NoteBlocks.Find(lines) is [.., { Closed: false } open] && (open.IsDiagram || open.Kind == NoteBlockKind.Math))
            return string.Join("\n", lines[..open.First]).TrimEnd();
        return Showable(text).TrimEnd();
    }
}
