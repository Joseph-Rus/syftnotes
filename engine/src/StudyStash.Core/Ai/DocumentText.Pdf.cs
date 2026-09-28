using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace StudyStash.Core.Ai;

public static partial class DocumentText
{
    /// <summary>A page with fewer letters than this typed on it is read as a picture too: an iPad page of handwriting
    /// often has only its title or a page number typed.</summary>
    internal const int TypedEnough = 40;

    /// <summary>
    /// A PDF's pages, each page's text in turn, blank pages left out. A page with next to no typed text (a scan, or
    /// iPad notes, whose handwriting is ink rather than letters) is drawn and read by text recognition, up to
    /// <see cref="DocumentTextOptions.MaxOcrPages"/> pages and until <see cref="DocumentTextOptions.TimeLimit"/> is up;
    /// the pages it didn't get to keep what was typed on them.
    /// </summary>
    static string? Pdf(string path, DocumentTextOptions options, CancellationToken ct)
    {
        var until = DateTime.UtcNow + options.TimeLimit;
        var pages = PdfPages(path, options.MaxPages, ct);
        if (pages is null) return null;
        var bare = Enumerable.Range(0, pages.Count).Where(i => Letters(pages[i]) < TypedEnough).Take(options.MaxOcrPages).ToList();
        if (bare.Count > 0 && DateTime.UtcNow < until && (options.PageReader ?? PageReader) is { } read)
            foreach (var (i, text) in read(path, bare, until, ct))
                if (i >= 0 && i < pages.Count && Letters(text) > Letters(pages[i])) pages[i] = text;
        return string.Join("\n\n", pages.Select(Py.Strip).Where(t => t.Length > 0));
    }

    static int Letters(string text) => text.Count(char.IsLetterOrDigit);

    /// <summary>Reads these pages of a PDF (numbered from 0) as pictures, until the time given: this computer's way.</summary>
    internal delegate IReadOnlyDictionary<int, string> PageReaderFn(string path, IReadOnlyList<int> pages, DateTime until, CancellationToken ct);

    /// <summary>This computer's text recognition for a PDF's pages; null where there's none.</summary>
    static PageReaderFn? PageReader => MacVision.Available ? MacVision.PdfPageText : null;

    /// <summary>The typed text on each of a PDF's first <paramref name="max"/> pages ("" for a page with none);
    /// null when it isn't a PDF that opens. A Mac reads it with PDFKit, as Preview does; elsewhere PdfPig reads it.</summary>
    internal static List<string>? PdfPages(string path, int max, CancellationToken ct) =>
        MacVision.Available ? MacVision.PdfPages(path, max, ct) : PdfPigPages(path, max, ct);

    /// <summary>PdfPig's reading of a PDF's text, in the order it would be read.</summary>
    internal static List<string>? PdfPigPages(string path, int max, CancellationToken ct)
    {
        try
        {
            using var pdf = PdfDocument.Open(path);
            var pages = new List<string>();
            for (int i = 1; i <= Math.Min(pdf.NumberOfPages, max); i++)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    pages.Add(ContentOrderTextExtractor.GetText(pdf.GetPage(i)));
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    pages.Add(""); // one damaged page doesn't lose the rest
                }
            }
            return pages;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // PdfPig throws its own kinds (and a few of .NET's) for a file that isn't a PDF, is damaged or locked.
            return null;
        }
    }
}
