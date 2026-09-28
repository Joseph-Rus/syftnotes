using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace StudyStash.Core.Ai;

/// <summary>
/// The answer inside an Ask reply while the AI is still writing it: <c>{"answer": "Cells have mem</c> already reads
/// "Cells have mem", so the student sees the answer as it's written instead of waiting for the whole reply.
/// </summary>
public static partial class AskAnswer
{
    [GeneratedRegex("\"answer\"\\s*:\\s*\"")]
    private static partial Regex AnswerKey();

    /// <summary>The answer so far in <paramref name="raw"/> (everything the AI has written of its JSON reply), read
    /// the way the finished reply is (JSON's escapes, and LaTeX's backslashes put back); null until it has begun. An
    /// escape cut off at the end waits for the rest of it.</summary>
    public static string? SoFar(string raw)
    {
        var key = AnswerKey().Match(raw);
        if (!key.Success) return null;
        var text = new StringBuilder();
        for (int i = key.Index + key.Length; i < raw.Length; i++)
        {
            char c = raw[i];
            if (c == '"') break;
            if (c != '\\')
            {
                text.Append(c);
                continue;
            }
            if (i + 1 >= raw.Length) break;
            char next = raw[i + 1];
            if (next == 'u')
            {
                if (i + 6 > raw.Length) break;
                if (int.TryParse(raw.AsSpan(i + 2, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out int code))
                {
                    text.Append((char)code);
                    i += 5;
                    continue;
                }
            }
            // As MathText.DoubleLoneBackslashes reads a finished reply: a backslash that starts no JSON escape (LaTeX's
            // \sqrt) is kept as it is.
            text.Append(next switch
            {
                '"' => "\"", '\\' => "\\", '/' => "/", 'b' => "\b", 'f' => "\f", 'n' => "\n", 'r' => "\r", 't' => "\t",
                _ => "\\" + next,
            });
            i++;
        }
        return Rich.MathText.RepairJsonEscapes(text.ToString()).TrimStart();
    }
}
