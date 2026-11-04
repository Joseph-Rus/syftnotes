using StudyStash.Core.Ai;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace StudyStash.Core.Tests;

/// <summary>
/// Text recognition: handwriting in photos, and in PDFs with no typed text (iPad notes). Runs on a Mac, with Vision;
/// elsewhere it passes without running. Fixtures/documents/handwriting.* is three lines in a handwriting font on lined
/// paper, each letter tilted a little, the page a little crooked.
/// </summary>
public class DocumentOcrTests
{
    static readonly DocumentTextOptions NoCache = new();

    internal static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", "documents", name);

    /// <summary>The words the handwriting fixture must be read as saying.</summary>
    internal static void SaysTheNotes(string? text)
    {
        Assert.NotNull(text);
        foreach (string word in new[] { "Midterm", "Tuesday", "Mitochondria", "ATP", "Quick", "sort", "averages", "log" })
            Assert.True(text.Contains(word, StringComparison.OrdinalIgnoreCase), $"{word} not in: {text}");
    }

    [Theory]
    [InlineData("handwriting.png")]
    [InlineData("handwriting.jpg")]
    [InlineData("handwriting.heic")]
    public async Task Handwriting_in_a_photo_is_read_top_to_bottom(string name)
    {
        if (!OperatingSystem.IsMacOS()) return;
        string? text = await DocumentText.ExtractAsync(Fixture(name), NoCache, default);
        // What the name promises: the page is read top to bottom. Three words Vision reads the same in every version
        // and format, in the order they're written down the page; the exact reading of the rest ("n log n" came out as
        // "nogn" from a HEIC on one macOS) is Vision's, not ours.
        Assert.NotNull(text);
        int[] at = new[] { "Midterm", "Mitochondria", "averages" }.Select(w => text.IndexOf(w, StringComparison.OrdinalIgnoreCase)).ToArray();
        Assert.True(at.All(i => i >= 0) && at[0] < at[1] && at[1] < at[2], $"not read top to bottom: {text}");
    }

    /// <summary>A PDF like one an iPad exports: each page the handwriting as a picture, perhaps a typed title above it.</summary>
    internal static void HandwrittenPdf(string path, int pages = 1, string? typed = null)
    {
        var b = new PdfDocumentBuilder();
        var font = b.AddStandard14Font(Standard14Font.Helvetica);
        byte[] png = File.ReadAllBytes(Fixture("handwriting.png"));
        for (int i = 0; i < pages; i++)
        {
            var page = b.AddPage(PageSize.Letter);
            page.AddPng(png, new PdfRectangle(36, 450, 576, 666));
            if (typed is not null) page.AddText(typed, 12, new PdfPoint(36, 740), font);
        }
        File.WriteAllBytes(path, b.Build());
    }

    [Fact]
    public async Task Handwritten_iPad_notes_in_a_PDF_are_read_with_their_typed_title()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var dir = new TempDir();
        HandwrittenPdf(dir["Biology notes.pdf"], typed: "BIO 110 Week 5");

        string? text = await DocumentText.ExtractAsync(dir["Biology notes.pdf"], NoCache, default);

        SaysTheNotes(text);
        Assert.Contains("BIO 110", text);
    }

    [Fact]
    public async Task A_PDF_of_scans_stops_reading_at_its_limits_and_keeps_the_pages_it_read()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var dir = new TempDir();
        HandwrittenPdf(dir["scans.pdf"], pages: 3);

        string? two = await DocumentText.ExtractAsync(dir["scans.pdf"], NoCache with { MaxOcrPages = 2 }, default);
        Assert.Equal(2, two!.Split("Mitochondria", StringSplitOptions.None).Length - 1);
        Assert.Null(await DocumentText.ExtractAsync(dir["scans.pdf"], NoCache with { TimeLimit = TimeSpan.Zero }, default));
    }

    [Fact]
    public async Task A_blank_page_is_read_as_nothing()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var dir = new TempDir();
        DocumentTextTests.TypedPdf(dir["blank.pdf"], [], []);
        var read = MacVision.PdfPageText(dir["blank.pdf"], [0, 1, 7, -1], DateTime.UtcNow.AddMinutes(1), default);
        Assert.Equal([0, 1], read.Keys.Order());
        Assert.All(read.Values, v => Assert.Equal("", v));
        Assert.Null(await DocumentText.ExtractAsync(dir["blank.pdf"], NoCache, default));
    }

    [Fact]
    public async Task A_picture_that_is_not_one_is_null()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var dir = new TempDir();
        File.WriteAllText(dir["photo.png"], "not a picture");
        File.WriteAllBytes(dir["empty.jpg"], []);
        Assert.Null(await DocumentText.ExtractAsync(dir["photo.png"], NoCache, default));
        Assert.Null(await DocumentText.ExtractAsync(dir["empty.jpg"], NoCache, default));
    }
}
