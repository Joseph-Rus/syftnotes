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
    /// What it read is kept in <paramref name="options"/>' cache folder, if it has one.
    /// </summary>
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
                ".pdf" or ".pptx" => Joined(Pages(ext, path, options, ct)),
                ".docx" => Docx(path),
                ".doc" or ".rtf" => Machine.Run("textutil", ["-convert", "txt", "-stdout", path], TimeSpan.FromSeconds(25))?.Stdout,
                _ when Plain.Contains(ext) => File.ReadAllText(path),
                _ when Images.Contains(ext) => OneAtATime(() => ImageText(path, DateTime.UtcNow + options.TimeLimit), ct),
                _ => null,
            };
        }
        catch (Exception e) when (Unreadable(e))
        {
            return null;
        }
    }

    /// <summary>What a file that is damaged, locked or not what its name says throws, which is "no words", not a fault.</summary>
    static bool Unreadable(Exception e) => e is IOException or UnauthorizedAccessException or InvalidDataException or System.Xml.XmlException;

    /// <summary>A PDF or a PowerPoint (.pptx): the files with pages (<see cref="PagesAsync"/>).</summary>
    public static bool HasPages(string path) => Path.GetExtension(path).ToLowerInvariant() is ".pdf" or ".pptx";

    /// <summary>
    /// A PDF's pages or a PowerPoint's slides, each one's words in turn, in the order of the pages or the deck: a page
    /// with none is "" (so the Nth string is always page N), and a PowerPoint slide's speaker notes come after its words.
    /// Read the way <see cref="ExtractAsync(string, DocumentTextOptions, CancellationToken)"/> reads, with the same
    /// limits and text recognition, but nothing is kept; and a PowerPoint's slides past
    /// <see cref="DocumentTextOptions.MaxPages"/> are left out, as a PDF's pages are. Null when the file isn't there,
    /// isn't a PDF or .pptx, or doesn't open. <see cref="WordsOf"/> makes the file's words from these.
    /// </summary>
    public static async Task<IReadOnlyList<string>?> PagesAsync(string path, DocumentTextOptions options, CancellationToken ct)
    {
        if (!HasPages(path) || !File.Exists(path)) return null;
        string ext = Path.GetExtension(path).ToLowerInvariant();
        try
        {
            var pages = await Task.Run(() => Pages(ext, Path.GetFullPath(path), options, ct), ct);
            return pages?.Take(options.MaxPages).ToList();
        }
        catch (Exception e) when (Unreadable(e))
        {
            return null;
        }
    }

    /// <summary>The words <see cref="ExtractAsync(string, DocumentTextOptions, CancellationToken)"/> gives for a PDF or
    /// PowerPoint, made from its <paramref name="pages"/> (<see cref="PagesAsync"/>) so a file read as pages needn't be
    /// read again for them.</summary>
    public static string? WordsOf(IReadOnlyList<string>? pages) => Tidy(Joined(pages));

    static List<string>? Pages(string ext, string path, DocumentTextOptions options, CancellationToken ct) =>
        ext switch { ".pdf" => Pdf(path, options, ct), ".pptx" => Pptx(path), _ => null };

    /// <summary>Pages made one text: the ones with words, a blank line between.</summary>
    static string? Joined(IEnumerable<string>? pages) => pages is null ? null : string.Join("\n\n", pages.Where(t => t.Length > 0));

    /// <summary>The words in a photo or scan, by the computer's own text recognition; null where there's none.</summary>
    static string? ImageText(string path, DateTime until) =>
        MacVision.Available ? MacVision.ImageText(path) : WindowsOcr.Available ? WindowsOcr.ImageText(path, until) : null;

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
}

/// <summary>
/// How <see cref="DocumentText"/> reads. <see cref="For"/> keeps what it read in a library's own home folder, so reading
/// a file again (the file search's next pass, an attachment opened twice) costs nothing until the file changes. A
/// library keeps its readings in its own home, never another's; with no cache folder nothing is kept.
/// </summary>
public sealed record DocumentTextOptions
{
    /// <summary>Where read text is kept, by file path, size and time changed; null keeps nothing.</summary>
    public string? CacheDir { get; init; }

    /// <summary>A PDF's pages past this many aren't read (a 300-page textbook's first chapters are what's asked about),
    /// and a PowerPoint's slides past it aren't kept as pages.</summary>
    public int MaxPages { get; init; } = 300;

    /// <summary>At most this many of a PDF's pages are read as pictures (about a second each on a Mac).</summary>
    public int MaxOcrPages { get; init; } = 60;

    /// <summary>Text recognition stops after this long on one file; the pages it read are kept.</summary>
    public TimeSpan TimeLimit { get; init; } = TimeSpan.FromMinutes(2);

    /// <summary>Tests: stands in for the computer's text recognition of a PDF's pages.</summary>
    internal DocumentText.PageReaderFn? PageReader { get; init; }

    /// <summary>Keeps what it reads in <paramref name="home"/>'s <c>cache/text</c> folder.</summary>
    public static DocumentTextOptions For(string home) => new() { CacheDir = Path.Combine(home, "cache", "text") };
}
