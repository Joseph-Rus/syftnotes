using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace StudyStash.Core;

/// <summary>One stretch of speech: when it starts and ends in the recording (seconds), what was said, and whose voice it
/// was: 1, 2, 3… by how much each spoke (see <see cref="Speakers"/>), or 0 when voices weren't told apart.</summary>
public sealed record Spoken(double Start, double End, string Text, [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] int Speaker = 0);

/// <summary>
/// A recording's transcript as the library keeps it: one "[18:05] what was said" line per stretch of speech, so a
/// note, an answer, or Claude can point at the moment something was said.
/// </summary>
public static partial class TimedText
{
    [GeneratedRegex(@"^\[(?:(\d+):)?(\d{1,2}):(\d{2})\]\s?(.*)$")]
    private static partial Regex Line();

    /// <summary>"18:05", or "1:02:40" past the first hour.</summary>
    public static string Clock(double seconds)
    {
        long s = (long)Math.Max(0, Math.Floor(seconds));
        return s >= 3600
            ? string.Create(CultureInfo.InvariantCulture, $"{s / 3600}:{s / 60 % 60:00}:{s % 60:00}")
            : string.Create(CultureInfo.InvariantCulture, $"{s / 60:00}:{s % 60:00}");
    }

    /// <summary>"1 h 12 min", "48 min", "under a minute".</summary>
    public static string Length(double seconds)
    {
        long m = (long)Math.Round(seconds / 60);
        if (m < 1) return "under a minute";
        return m >= 60 ? $"{m / 60} h {m % 60:00} min" : $"{m} min";
    }

    /// <summary>The lines of a transcript. Where voices were told apart, a line says "Speaker 2: " when the voice
    /// changes from the line before, and only then.</summary>
    public static string Format(IEnumerable<Spoken> segments)
    {
        var sb = new StringBuilder();
        int voice = 0;
        foreach (var s in segments)
        {
            string text = Tidy(s.Text);
            if (text.Length == 0) continue;
            if (sb.Length > 0) sb.Append('\n');
            sb.Append('[').Append(Clock(s.Start)).Append("] ");
            if (s.Speaker > 0 && s.Speaker != voice) sb.Append("Speaker ").Append(s.Speaker.ToString(CultureInfo.InvariantCulture)).Append(": ");
            voice = s.Speaker;
            sb.Append(text);
        }
        return sb.ToString();
    }

    /// <summary>Format's lines back as segments; each ends where the next begins. A line without a time joins the
    /// one before it, so a transcript someone edited by hand still reads.</summary>
    public static List<Spoken> Parse(string transcript)
    {
        var parts = new List<(double Start, StringBuilder Text)>();
        foreach (string raw in transcript.ReplaceLineEndings("\n").Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0) continue;
            var m = Line().Match(line);
            if (m.Success)
            {
                double h = m.Groups[1].Success ? double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
                double start = h * 3600 + double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) * 60
                    + double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
                parts.Add((start, new StringBuilder(m.Groups[4].Value.Trim())));
            }
            else if (parts.Count > 0)
            {
                parts[^1].Text.Append(' ').Append(line);
            }
            else
            {
                parts.Add((0, new StringBuilder(line)));
            }
        }
        var result = new List<Spoken>(parts.Count);
        for (int i = 0; i < parts.Count; i++)
        {
            double end = i + 1 < parts.Count ? Math.Max(parts[i].Start, parts[i + 1].Start) : parts[i].Start;
            result.Add(new Spoken(parts[i].Start, end, parts[i].Text.ToString()));
        }
        return result;
    }

    /// <summary>True when the transcript has Format's times (a recording's), not a plain one.</summary>
    public static bool HasTimes(string transcript)
    {
        foreach (string raw in transcript.ReplaceLineEndings("\n").Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length > 0) return Line().IsMatch(line);
        }
        return false;
    }

    /// <summary>The words without their times: what the note-writing model reads.</summary>
    public static string Plain(string transcript) =>
        HasTimes(transcript) ? string.Join("\n", Parse(transcript).Select(s => s.Text)) : transcript;

    [GeneratedRegex(@"^\s*[\[\(][^\]\)]*[\]\)]\s*$")]
    private static partial Regex OnlyAnnotation();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    /// <summary>Whisper's words, with what isn't speech dropped: "[BLANK_AUDIO]", "(music)", "[Applause]".</summary>
    public static string Tidy(string text)
    {
        string t = Spaces().Replace(text, " ").Trim();
        return OnlyAnnotation().IsMatch(t) ? "" : t;
    }
}
