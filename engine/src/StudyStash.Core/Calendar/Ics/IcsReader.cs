using System.Text;

namespace StudyStash.Core.Calendar.Ics;

/// <summary>One line of an iCalendar file: its name ("DTSTART"), its parameters ("TZID" → "America/New_York"), and
/// its value as written (escapes and all; <see cref="IcsText.Unescape"/> reads a text value).</summary>
public sealed class IcsProperty(string name, IReadOnlyDictionary<string, string> parameters, string value)
{
    public string Name { get; } = name;
    public IReadOnlyDictionary<string, string> Parameters { get; } = parameters;
    public string Value { get; } = value;

    /// <summary>A parameter's value, without its quotes; null when it isn't there.</summary>
    public string? Param(string name) => Parameters.TryGetValue(name, out string? v) ? v : null;

    /// <summary>The value as text: escapes undone ("\n" a new line, "\," a comma).</summary>
    public string Text => IcsText.Unescape(Value);
}

/// <summary>A BEGIN…END block (VCALENDAR, VEVENT, VTIMEZONE, STANDARD…): its lines, and the blocks inside it.</summary>
public sealed class IcsComponent(string name)
{
    public string Name { get; } = name;
    public List<IcsProperty> Properties { get; } = [];
    public List<IcsComponent> Children { get; } = [];

    /// <summary>The first line with this name, or null.</summary>
    public IcsProperty? First(string name) => Properties.FirstOrDefault(p => p.Name == name);

    /// <summary>Every line with this name, in order.</summary>
    public IEnumerable<IcsProperty> All(string name) => Properties.Where(p => p.Name == name);

    /// <summary>The blocks inside with this name, in order.</summary>
    public IEnumerable<IcsComponent> Blocks(string name) => Children.Where(c => c.Name == name);

    /// <summary>A text line's value (unescaped), or null when there's none.</summary>
    public string? Text(string name) => First(name)?.Text;
}

/// <summary>
/// Reads iCalendar (RFC 5545) as schools, Google, iCloud, Canvas and Outlook write it: long lines folded onto the next
/// with a leading space or tab, any line ending, a byte-order mark, parameters quoted or not, and whatever is broken
/// (a line without a colon, an END that doesn't match) skipped rather than failing the whole feed.
/// </summary>
public static class IcsReader
{
    /// <summary>Every VCALENDAR in the text (a feed is almost always one), with the blocks inside each.</summary>
    public static List<IcsComponent> Parse(string text)
    {
        var root = new IcsComponent("");
        var open = new Stack<IcsComponent>();
        open.Push(root);
        foreach (string line in Unfold(text))
        {
            if (ParseLine(line) is not { } p) continue;
            if (p.Name == "BEGIN")
            {
                var c = new IcsComponent(p.Value.Trim().ToUpperInvariant());
                open.Peek().Children.Add(c);
                open.Push(c);
            }
            else if (p.Name == "END")
            {
                string name = p.Value.Trim().ToUpperInvariant();
                // An END for a block that isn't the innermost closes back to it; one for no open block is ignored.
                if (open.Any(c => c.Name == name && c != root))
                    while (open.Count > 1 && open.Pop().Name != name) { }
            }
            else if (open.Count > 1)
            {
                open.Peek().Properties.Add(p);
            }
        }
        return [.. root.Blocks("VCALENDAR")];
    }

    /// <summary>The file's logical lines: folded lines joined back (the fold's one space or tab dropped), line endings
    /// of any kind, blank lines skipped.</summary>
    public static IEnumerable<string> Unfold(string text)
    {
        if (text.Length > 0 && text[0] == '﻿') text = text[1..];
        var current = new StringBuilder();
        bool any = false;
        int i = 0;
        while (i <= text.Length)
        {
            int end = i;
            while (end < text.Length && text[end] != '\r' && text[end] != '\n') end++;
            string line = text[i..end];
            if (end < text.Length && text[end] == '\r' && end + 1 < text.Length && text[end + 1] == '\n') end++;
            i = end + 1;
            if (line.Length > 0 && (line[0] == ' ' || line[0] == '\t') && any)
            {
                current.Append(line, 1, line.Length - 1);
                continue;
            }
            if (any && current.Length > 0) yield return current.ToString();
            current.Clear();
            current.Append(line);
            any = true;
        }
        if (current.Length > 0) yield return current.ToString();
    }

    /// <summary>"NAME;PARAM=value;PARAM="quoted:value":the value" → its parts; null for a line with no colon.</summary>
    public static IcsProperty? ParseLine(string line)
    {
        int i = 0;
        while (i < line.Length && line[i] != ';' && line[i] != ':') i++;
        if (i == line.Length) return null;
        string name = line[..i].Trim().ToUpperInvariant();
        if (name.Length == 0) return null;
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        while (i < line.Length && line[i] == ';')
        {
            i++;
            int eq = i;
            while (eq < line.Length && line[eq] != '=' && line[eq] != ';' && line[eq] != ':') eq++;
            string key = line[i..eq].Trim().ToUpperInvariant();
            i = eq;
            var value = new StringBuilder();
            if (i < line.Length && line[i] == '=')
            {
                i++;
                bool quoted = false;
                while (i < line.Length)
                {
                    char ch = line[i];
                    if (ch == '"') quoted = !quoted;
                    else if (!quoted && (ch == ';' || ch == ':')) break;
                    else value.Append(ch);
                    i++;
                }
            }
            if (key.Length > 0) parameters[key] = value.ToString();
        }
        if (i >= line.Length || line[i] != ':') return null;
        return new IcsProperty(name, parameters, line[(i + 1)..]);
    }
}

/// <summary>iCalendar's text escapes, and splitting a list on commas that aren't escaped.</summary>
public static class IcsText
{
    /// <summary>"\n" or "\N" a new line, "\," "\;" "\\" the character itself.</summary>
    public static string Unescape(string value)
    {
        if (!value.Contains('\\')) return value;
        var sb = new StringBuilder(value.Length);
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (c == '\\' && i + 1 < value.Length)
            {
                char n = value[++i];
                sb.Append(n is 'n' or 'N' ? '\n' : n);
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }

    /// <summary>A list value's items ("20260901,20260908"), split on commas that aren't escaped; blanks dropped.</summary>
    public static List<string> Split(string value)
    {
        var items = new List<string>();
        var sb = new StringBuilder();
        for (int i = 0; i < value.Length; i++)
        {
            if (value[i] == '\\' && i + 1 < value.Length)
            {
                sb.Append(value[i]).Append(value[++i]);
                continue;
            }
            if (value[i] == ',')
            {
                if (sb.ToString().Trim() is { Length: > 0 } item) items.Add(item);
                sb.Clear();
                continue;
            }
            sb.Append(value[i]);
        }
        if (sb.ToString().Trim() is { Length: > 0 } last) items.Add(last);
        return items;
    }
}
