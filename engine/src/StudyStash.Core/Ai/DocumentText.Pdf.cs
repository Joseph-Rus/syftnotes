using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace StudyStash.Core.Ai;

public static partial class DocumentText
{
    /// <summary>A PDF's pages, each page's text in turn, blank pages left out.</summary>
    static string? Pdf(string path, DocumentTextOptions options, CancellationToken ct)
    {
        var pages = PdfPages(path, options.MaxPages, ct);
        if (pages is null) return null;
        return string.Join("\n\n", pages.Select(Py.Strip).Where(t => t.Length > 0));
    }

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
