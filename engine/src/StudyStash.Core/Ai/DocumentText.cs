using System.IO.Compression;
using System.Net;
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
        string? text = await Task.Run(() => Read(path, options, ct), ct);
        return Tidy(text);
    }

    static string? Read(string path, DocumentTextOptions options, CancellationToken ct)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        try
        {
            return ext switch
            {
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

    public static DocumentTextOptions Default => new() { CacheDir = Path.Combine(Configs.DefaultHome, "cache", "text") };
}
