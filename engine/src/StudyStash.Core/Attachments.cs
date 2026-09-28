using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace StudyStash.Core;

/// <summary>
/// A file a student attached to a lecture or a class: handwritten notes from an iPad, slides, a handout. It's kept in
/// the class's folder, in Attachments/, and the words read from it help write that lecture's notes and answer questions.
/// </summary>
/// <param name="File">Its file name in the class's Attachments folder (the name it came with, made safe).</param>
/// <param name="NoteId">The lecture it belongs to, or null for the whole class.</param>
/// <param name="State">"reading" while its words are being read, "read" once they are, "unread" when there were none to read.</param>
/// <param name="Used">The lecture's notes were written with its words.</param>
public sealed record Attachment(string Id, string Name, string File, string ClassName, string? NoteId, long Size, string Type,
    string Added, string Text = "", string State = Attachment.Reading, bool Used = false)
{
    public const string Reading = "reading", Read = "read", Unread = "unread";

    public bool HasText => Text.Length > 0;

    /// <summary>What /api/v2/attachments answers for it: {id, name, class, lecture, size, type, added, hasText}, and
    /// whether its words are still being read.</summary>
    public JsonObject ToJson() => new()
    {
        ["id"] = Id, ["name"] = Name, ["class"] = ClassName, ["lecture"] = NoteId, ["size"] = Size, ["type"] = Type,
        ["added"] = Added, ["hasText"] = HasText, ["reading"] = State == Reading,
    };
}

/// <summary>The words an attachment adds to a lecture's notes: what kind of thing it is, its name, and its text.</summary>
public sealed record AttachedText(string Id, string Label, string Name, string Text);

/// <summary>Naming, recognising and reading attachments.</summary>
public static partial class Attachments
{
    /// <summary>The most one upload may carry (every file in it together).</summary>
    public const long MaxRequestBytes = 200L * 1024 * 1024;

    /// <summary>How many characters of attachments' words go with a lecture's notes, at most.</summary>
    public const int MaxContextChars = 12_000;

    public const string OwnNotes = "The student's own notes", Slides = "Slides", Handout = "Handout";

    [GeneratedRegex("[\\\\/:*?\"<>|\\x00-\\x1f\\x7f]")]
    private static partial Regex BadFileChars();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    /// <summary>
    /// A name that's safe as a file in the Attachments folder: the last part of whatever the browser sent (never a path),
    /// without characters a disk refuses or a leading dot, and not too long (the extension kept). "attachment" when
    /// nothing's left. Windows' reserved names (CON, NUL…) get an underscore.
    /// </summary>
    public static string SafeName(string? sent)
    {
        string name = (sent ?? "").Replace('\\', '/');
        name = name[(name.LastIndexOf('/') + 1)..];
        name = Spaces().Replace(BadFileChars().Replace(name, " "), " ").Trim().TrimStart('.').Trim().TrimEnd('.', ' ');
        string ext = Path.GetExtension(name);
        if (ext.Length > 12 || ext.Length == name.Length) ext = "";
        string stem = name[..^ext.Length].Trim();
        if (stem.Length > 100) stem = stem[..100].Trim();
        if (stem.Length == 0) stem = "attachment";
        if (Reserved.Contains(stem.ToUpperInvariant())) stem += "_";
        return stem + ext.ToLowerInvariant();
    }

    static readonly HashSet<string> Reserved =
        ["CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"];

    /// <summary>A name in <paramref name="dir"/> nothing has yet: "slides.pdf", else "slides (2).pdf", "slides (3).pdf"…</summary>
    public static string FreeName(string dir, string name)
    {
        string stem = Path.GetFileNameWithoutExtension(name), ext = Path.GetExtension(name);
        string candidate = name;
        for (int n = 2; File.Exists(Path.Combine(dir, candidate)); n++) candidate = $"{stem} ({n}){ext}";
        return candidate;
    }

    static readonly Dictionary<string, string> ByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = "application/pdf", [".png"] = "image/png", [".jpg"] = "image/jpeg", [".jpeg"] = "image/jpeg",
        [".heic"] = "image/heic", [".heif"] = "image/heif", [".gif"] = "image/gif", [".webp"] = "image/webp", [".tif"] = "image/tiff", [".tiff"] = "image/tiff",
        [".pptx"] = "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        [".ppt"] = "application/vnd.ms-powerpoint", [".doc"] = "application/msword", [".key"] = "application/x-iwork-keynote-sffkey",
        [".txt"] = "text/plain", [".md"] = "text/markdown", [".rtf"] = "application/rtf", [".csv"] = "text/csv",
    };

    /// <summary>
    /// What a file really is, from its first bytes: a PDF, a picture (PNG, JPEG, GIF, WebP, TIFF, HEIC), an Office
    /// file (a zip, named by its extension), or text. What the browser said it was isn't trusted. Anything else is
    /// application/octet-stream, so it's always downloaded, never shown as a page.
    /// </summary>
    public static string Sniff(ReadOnlySpan<byte> head, string name)
    {
        string ext = Path.GetExtension(name);
        static bool Starts(ReadOnlySpan<byte> h, ReadOnlySpan<byte> sig) => h.Length >= sig.Length && h[..sig.Length].SequenceEqual(sig);
        if (Starts(head, "%PDF-"u8)) return "application/pdf";
        if (Starts(head, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A])) return "image/png";
        if (Starts(head, [0xFF, 0xD8, 0xFF])) return "image/jpeg";
        if (Starts(head, "GIF87a"u8) || Starts(head, "GIF89a"u8)) return "image/gif";
        if (head.Length >= 12 && Starts(head, "RIFF"u8) && head[8..12].SequenceEqual("WEBP"u8)) return "image/webp";
        if (Starts(head, [0x49, 0x49, 0x2A, 0x00]) || Starts(head, [0x4D, 0x4D, 0x00, 0x2A])) return "image/tiff";
        if (head.Length >= 12 && head[4..8].SequenceEqual("ftyp"u8))
        {
            string brand = Encoding.ASCII.GetString(head[8..12]);
            if (brand is "heic" or "heix" or "hevc" or "heim" or "heis") return "image/heic";
            if (brand is "mif1" or "msf1") return "image/heif";
        }
        if (Starts(head, "PK\u0003\u0004"u8))
            return ext.ToLowerInvariant() is ".pptx" or ".docx" or ".xlsx" or ".key" ? ByExtension[ext] : "application/zip";
        if (Starts(head, [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1]))
            return ext.ToLowerInvariant() is ".ppt" or ".doc" ? ByExtension[ext] : "application/octet-stream";
        if (Starts(head, "{\\rtf"u8)) return "application/rtf";
        if (LooksLikeText(head) && ext.ToLowerInvariant() is ".txt" or ".md" or ".csv") return ByExtension[ext];
        return "application/octet-stream";
    }

    static bool LooksLikeText(ReadOnlySpan<byte> head)
    {
        if (head.Length == 0) return false;
        foreach (byte b in head)
            if (b == 0 || (b < 0x09) || (b > 0x0D && b < 0x20 && b != 0x1B)) return false;
        return true;
    }

    /// <summary>Pictures and PDFs open in the browser; everything else is downloaded.</summary>
    public static bool ShowsInline(string type) => type == "application/pdf" || (type.StartsWith("image/", StringComparison.Ordinal) && type != "image/svg+xml");

    [GeneratedRegex(@"slide|deck|lecture|ppt|keynote|week\s*\d|chapter|ch\s*\d", RegexOptions.IgnoreCase)]
    private static partial Regex SlideWords();

    [GeneratedRegex(@"note|handwrit|goodnotes|notability|scan|my\b", RegexOptions.IgnoreCase)]
    private static partial Regex OwnWords();

    /// <summary>
    /// How the notes prompt names it: "Slides" for PowerPoint, Keynote, or a PDF named like a deck; "The student's own
    /// notes" for a photo or scan of a page, or a PDF named like notes (GoodNotes and Notability export PDFs); else a
    /// "Handout".
    /// </summary>
    public static string KindOf(string name, string type)
    {
        string ext = Path.GetExtension(name).ToLowerInvariant();
        if (ext is ".pptx" or ".ppt" or ".key") return Slides;
        if (type.StartsWith("image/", StringComparison.Ordinal)) return OwnNotes;
        string stem = Path.GetFileNameWithoutExtension(name);
        if (OwnWords().IsMatch(stem)) return OwnNotes;
        if (SlideWords().IsMatch(stem)) return Slides;
        return Handout;
    }

    /// <summary>
    /// The attachments' words for a prompt, each under its label and name, all together at most
    /// <paramref name="maxChars"/>: each gets an even share, and what one doesn't use goes to the rest. Empty when none
    /// has words.
    /// </summary>
    public static string Context(IReadOnlyList<AttachedText> attached, int maxChars = MaxContextChars)
    {
        var withText = attached.Where(a => a.Text.Trim().Length > 0).ToList();
        if (withText.Count == 0 || maxChars <= 0) return "";
        var sb = new StringBuilder();
        int left = maxChars;
        for (int i = 0; i < withText.Count; i++)
        {
            var a = withText[i];
            string head = $"{a.Label} ({a.Name}):\n";
            int share = Math.Max(0, left / (withText.Count - i) - head.Length - 2);
            string text = a.Text.Trim();
            if (text.Length > share) text = share > 1 ? text[..(share - 1)].TrimEnd() + "…" : "";
            if (text.Length == 0) continue;
            string part = head + text + "\n\n";
            sb.Append(part);
            left -= part.Length;
        }
        return sb.ToString().TrimEnd();
    }

    /// <summary>An attachment's words cut into passages of a few sentences each, for Ask to pick from.</summary>
    public static List<string> Pieces(string text, int target = 600)
    {
        var result = new List<string>();
        var cur = new StringBuilder();
        foreach (string raw in text.ReplaceLineEndings("\n").Split("\n\n"))
        {
            string para = Spaces().Replace(raw, " ").Trim();
            if (para.Length == 0) continue;
            while (para.Length > target)
            {
                int cut = para.LastIndexOfAny(['.', '!', '?'], target - 1);
                if (cut < target / 2) cut = para.LastIndexOf(' ', target - 1);
                if (cut < target / 2) cut = target - 1;
                if (cur.Length > 0)
                {
                    result.Add(cur.ToString());
                    cur.Clear();
                }
                result.Add(para[..(cut + 1)].Trim());
                para = para[(cut + 1)..].Trim();
            }
            if (cur.Length > 0 && cur.Length + 1 + para.Length > target)
            {
                result.Add(cur.ToString());
                cur.Clear();
            }
            if (para.Length > 0) cur.Append(cur.Length > 0 ? " " : "").Append(para);
        }
        if (cur.Length > 0) result.Add(cur.ToString());
        return result;
    }

    /// <summary>"2.4 MB", "830 KB", "12 bytes".</summary>
    public static string SizeLabel(long bytes) => bytes switch
    {
        >= 1024 * 1024 => (bytes / 1024.0 / 1024.0).ToString(bytes >= 10L * 1024 * 1024 ? "0" : "0.#", CultureInfo.InvariantCulture) + " MB",
        >= 1024 => (bytes / 1024.0).ToString("0", CultureInfo.InvariantCulture) + " KB",
        1 => "1 byte",
        _ => $"{bytes} bytes",
    };
}
