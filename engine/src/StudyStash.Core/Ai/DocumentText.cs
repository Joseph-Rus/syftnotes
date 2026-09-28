using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace StudyStash.Core.Ai;

/// <summary>
/// The words in a file a student hands the library: a PDF, a photo or scan (png, jpg, heic), a Word document or a
/// PowerPoint. Typed text is read as it is; a page or picture with none (iPad notes from GoodNotes or Notability are
/// handwriting drawn as ink) is read by the computer's own text recognition. Nothing needs installing.
/// </summary>
public static partial class DocumentText
{
    /// <summary>Pictures read by text recognition.</summary>
    public static readonly IReadOnlySet<string> Images = new HashSet<string> { ".png", ".jpg", ".jpeg", ".heic", ".heif", ".tif", ".tiff", ".bmp", ".gif", ".webp" };

    /// <summary>Plain text, read as it is.</summary>
    static readonly HashSet<string> Plain = [".txt", ".md", ".markdown", ".csv", ".tsv"];

    /// <summary>Every kind of file this can read.</summary>
    public static bool CanRead(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".pdf" or ".docx" or ".pptx" || Images.Contains(ext) || Plain.Contains(ext)
            || (ext is ".doc" or ".rtf" && OperatingSystem.IsMacOS());
    }

    /// <summary>At most this much text is kept from one file (a long textbook is cut, not refused).</summary>
    public const int MaxText = 400_000;

    /// <summary>
    /// The text of a PDF, picture, Word document or PowerPoint: its typed text where it has some, text recognition
    /// (handwriting too) where it doesn't. Null when the file isn't there, isn't a kind this reads, or holds no words.
    /// </summary>
    public static Task<string?> ExtractAsync(string path, CancellationToken ct) => ExtractAsync(path, DocumentTextOptions.Default, ct);

    /// <inheritdoc cref="ExtractAsync(string, CancellationToken)"/>
    public static async Task<string?> ExtractAsync(string path, DocumentTextOptions options, CancellationToken ct)
    {
        if (!CanRead(path) || !File.Exists(path)) return null;
        path = Path.GetFullPath(path);
        string? stamp = Stamp(path);
        string? kept = options.CacheDir is { } dir ? Path.Combine(dir, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(path))) + ".txt") : null;
        if (kept is not null && stamp is not null && Cached(kept, stamp) is { } hit) return hit.Length == 0 ? null : hit;
        string? text = Tidy(await Task.Run(() => Read(path, options, ct), ct));
        if (kept is not null && stamp is not null && stamp == Stamp(path)) Keep(kept, stamp, text ?? "");
        return text;
    }

    /// <summary>What a kept reading is checked against: how the text was read, and the file's size and time changed.
    /// Null when the file can't be looked at.</summary>
    static string? Stamp(string path)
    {
        try
        {
            var fi = new FileInfo(path);
            return $"{CacheVersion} {fi.Length} {fi.LastWriteTimeUtc.Ticks}";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Raised when reading gets better, so files read the old way are read again.</summary>
    const int CacheVersion = 1;

    /// <summary>The text kept for a file as it is now ("" for a file with none), or null when there's none or the
    /// file has changed since.</summary>
    static string? Cached(string kept, string stamp)
    {
        try
        {
            if (!File.Exists(kept)) return null;
            string all = File.ReadAllText(kept);
            int nl = all.IndexOf('\n', StringComparison.Ordinal);
            return nl >= 0 && all[..nl] == stamp ? all[(nl + 1)..] : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Keeps a reading, written whole or not at all (two readers of one file can't leave half of one).</summary>
    static void Keep(string kept, string stamp, string text)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(kept)!);
            string temp = $"{kept}.{Guid.NewGuid():N}.tmp";
            File.WriteAllText(temp, stamp + "\n" + text);
            File.Move(temp, kept, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    static string? Read(string path, DocumentTextOptions options, CancellationToken ct)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        try
        {
            return ext switch
            {
                ".pdf" => Pdf(path, options, ct),
                ".docx" => Docx(path),
                ".pptx" => Pptx(path),
                ".doc" or ".rtf" => Machine.Run("textutil", ["-convert", "txt", "-stdout", path], TimeSpan.FromSeconds(25))?.Stdout,
                _ when Plain.Contains(ext) => File.ReadAllText(path),
                _ => null,
            };
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or System.Xml.XmlException)
        {
            return null;
        }
    }

    /// <summary>Trimmed, lines of nothing but spaces made empty, at most three line breaks in a row and
    /// <see cref="MaxText"/> characters; null when nothing is left.</summary>
    internal static string? Tidy(string? text)
    {
        if (text is null) return null;
        text = BlankRun().Replace(SpaceLine().Replace(text.Replace("\r\n", "\n"), ""), "\n\n\n").Trim();
        return text.Length == 0 ? null : Py.Head(text, MaxText);
    }

    [GeneratedRegex(@"(?m)^[ \t ]+$")]
    private static partial Regex SpaceLine();

    [GeneratedRegex(@"\n{4,}")]
    private static partial Regex BlankRun();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex Tag();

    /// <summary>Office XML's words: each paragraph a line, tabs and line breaks kept.</summary>
    static string XmlWords(string xml, string paragraph) =>
        WebUtility.HtmlDecode(Tag().Replace(xml.Replace($"</{paragraph}>", "\n").Replace("<w:tab/>", "\t").Replace("<w:br/>", "\n").Replace("<a:br/>", "\n"), ""));

    static string Entry(ZipArchiveEntry e)
    {
        using var r = new StreamReader(e.Open());
        return r.ReadToEnd();
    }

    /// <summary>A Word document's body, footnotes and endnotes.</summary>
    static string? Docx(string path)
    {
        using var z = ZipFile.OpenRead(path);
        if (z.GetEntry("word/document.xml") is not { } body) return null;
        var parts = new List<string> { Py.Strip(XmlWords(Entry(body), "w:p")) };
        foreach (string extra in new[] { "word/footnotes.xml", "word/endnotes.xml" })
            if (z.GetEntry(extra) is { } e) parts.Add(Py.Strip(XmlWords(Entry(e), "w:p")));
        return string.Join("\n\n", parts);
    }

    [GeneratedRegex(@"^ppt/(slides/slide|notesSlides/notesSlide)(\d+)\.xml$")]
    private static partial Regex SlidePart();

    /// <summary>A PowerPoint's slides in order, each with its speaker notes after it.</summary>
    static string? Pptx(string path)
    {
        using var z = ZipFile.OpenRead(path);
        var parts = z.Entries.Select(e => (Entry: e, Match: SlidePart().Match(e.FullName))).Where(p => p.Match.Success)
            .OrderBy(p => int.Parse(p.Match.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture))
            .ThenBy(p => p.Match.Groups[1].Value.StartsWith("notes", StringComparison.Ordinal) ? 1 : 0)
            .Select(p => Py.Strip(XmlWords(Entry(p.Entry), "a:p"))).Where(t => t.Length > 0).ToList();
        return parts.Count == 0 ? null : string.Join("\n\n", parts);
    }
}

/// <summary>
/// How <see cref="DocumentText"/> reads. <see cref="Default"/> keeps what it read in the home folder, so reading a
/// file again (the file search's next pass, an attachment opened twice) costs nothing until the file changes.
/// </summary>
public sealed record DocumentTextOptions
{
    /// <summary>Where read text is kept, by file path, size and time changed; null keeps nothing.</summary>
    public string? CacheDir { get; init; }

    /// <summary>A PDF's pages past this many aren't read (a 300-page textbook's first chapters are what's asked about).</summary>
    public int MaxPages { get; init; } = 300;

    public static DocumentTextOptions Default => new() { CacheDir = Path.Combine(Configs.DefaultHome, "cache", "text") };
}
