using System.IO.Compression;
using StudyStash.Core.Ai;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace StudyStash.Core.Tests;

/// <summary>Reading the words in the files a student hands the library: PDFs, Word, PowerPoint, plain text.</summary>
public class DocumentTextTests
{
    static readonly DocumentTextOptions NoCache = new();

    /// <summary>A zip holding these files: the smallest Word document or PowerPoint that is still one.</summary>
    internal static void Zip(string path, params (string Name, string Text)[] entries)
    {
        using var z = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (name, text) in entries)
        {
            using var w = new StreamWriter(z.CreateEntry(name).Open());
            w.Write(text);
        }
    }

    internal static string WordXml(params string[] paragraphs) =>
        """<?xml version="1.0" encoding="UTF-8"?><w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body>"""
        + string.Concat(paragraphs.Select(p => $"<w:p><w:r><w:t>{p}</w:t></w:r></w:p>")) + "</w:body></w:document>";

    internal static string SlideXml(params string[] paragraphs) =>
        """<?xml version="1.0" encoding="UTF-8"?><p:sld xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main"><p:cSld><p:spTree><p:sp><p:txBody>"""
        + string.Concat(paragraphs.Select(p => $"<a:p><a:r><a:t>{p}</a:t></a:r></a:p>")) + "</p:txBody></p:sp></p:spTree></p:cSld></p:sld>";

    /// <summary>A PDF of typed text: each page its lines, an empty page for none.</summary>
    internal static void TypedPdf(string path, params string[][] pages)
    {
        var b = new PdfDocumentBuilder();
        var font = b.AddStandard14Font(Standard14Font.Helvetica);
        foreach (var lines in pages)
        {
            var page = b.AddPage(PageSize.Letter);
            for (int i = 0; i < lines.Length; i++) page.AddText(lines[i], 14, new PdfPoint(72, 700 - i * 24), font);
        }
        File.WriteAllBytes(path, b.Build());
    }

    [Fact]
    public async Task A_PDF_reads_page_by_page_and_leaves_blank_pages_out()
    {
        using var dir = new TempDir();
        TypedPdf(dir["syllabus.pdf"], ["CS 101 Syllabus", "Midterm: October 14"], [], ["Final project due December 5"]);

        string? text = await DocumentText.ExtractAsync(dir["syllabus.pdf"], NoCache, default);

        Assert.NotNull(text);
        Assert.Matches(@"^CS 101 Syllabus\s+Midterm: October 14\n\nFinal project due December 5$", text);
    }

    [Fact]
    public void PDFKit_on_a_Mac_and_PdfPig_elsewhere_read_the_same_pages()
    {
        using var dir = new TempDir();
        TypedPdf(dir["lab.pdf"], ["Lab 2: Titration"], [], ["Record the endpoint volume"]);

        var pig = DocumentText.PdfPigPages(dir["lab.pdf"], 10, default)!;
        Assert.Equal(["Lab 2: Titration", "", "Record the endpoint volume"], pig.Select(Py.Strip));
        Assert.Null(DocumentText.PdfPigPages(dir["missing.pdf"], 10, default));
        if (!OperatingSystem.IsMacOS()) return;
        Assert.True(MacVision.Available);
        Assert.Equal(pig.Select(Py.Strip), MacVision.PdfPages(dir["lab.pdf"], 10, default)!.Select(Py.Strip));
        Assert.Equal(["Lab 2: Titration"], MacVision.PdfPages(dir["lab.pdf"], 1, default)!.Select(Py.Strip));
        Assert.Null(MacVision.PdfPages(dir["missing.pdf"], 10, default));
        File.WriteAllText(dir["fake.pdf"], "not a PDF");
        Assert.Null(MacVision.PdfPages(dir["fake.pdf"], 10, default));
    }

    [Fact]
    public async Task A_long_PDF_is_read_only_as_far_as_its_page_limit()
    {
        using var dir = new TempDir();
        TypedPdf(dir["textbook.pdf"], [.. Enumerable.Range(1, 12).Select(n => new[] { $"Chapter {n} begins" })]);

        string? text = await DocumentText.ExtractAsync(dir["textbook.pdf"], NoCache with { MaxPages = 3 }, default);

        Assert.Contains("Chapter 3 begins", text);
        Assert.DoesNotContain("Chapter 4", text);
    }

    [Fact]
    public async Task Only_pages_with_next_to_no_typed_text_are_read_as_pictures_and_only_so_many()
    {
        using var dir = new TempDir();
        string typed = "This page has plenty of typed words on it already.";
        TypedPdf(dir["mixed.pdf"], [typed], ["Page 2"], [], [typed], [], []);
        var asked = new List<int>();
        var options = NoCache with
        {
            MaxOcrPages = 3,
            PageReader = (_, pages, _, _) =>
            {
                asked.AddRange(pages);
                return pages.ToDictionary(p => p, p => p == 2 ? "" : $"handwritten page {p + 1}");
            },
        };

        string? text = await DocumentText.ExtractAsync(dir["mixed.pdf"], options, default);

        Assert.Equal([1, 2, 4], asked); // page 6 is past the limit
        Assert.Equal($"{typed}\n\nhandwritten page 2\n\n{typed}\n\nhandwritten page 5", text);
    }

    [Fact]
    public async Task Recognition_that_reads_less_than_was_typed_keeps_the_typed_text()
    {
        using var dir = new TempDir();
        TypedPdf(dir["title.pdf"], ["Week 5 notes"]);
        var options = NoCache with { PageReader = (_, pages, _, _) => new Dictionary<int, string> { [0] = "Wk5", [9] = "not a page" } };

        Assert.Equal("Week 5 notes", await DocumentText.ExtractAsync(dir["title.pdf"], options, default));
    }

    [Fact]
    public async Task Recognition_is_given_the_time_left_and_not_asked_once_the_time_is_up()
    {
        using var dir = new TempDir();
        TypedPdf(dir["scan.pdf"], [[]]); // one page, nothing typed
        DateTime? given = null;
        DocumentText.PageReaderFn reader = (_, pages, until, _) =>
        {
            given = until;
            return pages.ToDictionary(p => p, _ => "read");
        };

        Assert.Equal("read", await DocumentText.ExtractAsync(dir["scan.pdf"], NoCache with { PageReader = reader, TimeLimit = TimeSpan.FromMinutes(5) }, default));
        Assert.InRange(given!.Value, DateTime.UtcNow.AddMinutes(4), DateTime.UtcNow.AddMinutes(5));
        given = null;
        Assert.Null(await DocumentText.ExtractAsync(dir["scan.pdf"], NoCache with { PageReader = reader, TimeLimit = TimeSpan.Zero }, default));
        Assert.Null(given);
    }

    [Fact]
    public async Task Files_are_recognized_one_at_a_time_and_waiting_a_turn_uses_none_of_the_time()
    {
        using var dir = new TempDir();
        int now = 0, most = 0;
        var options = NoCache with
        {
            TimeLimit = TimeSpan.FromSeconds(1),
            PageReader = (_, pages, until, _) =>
            {
                // The whole second is left once it's this file's turn, less the time it took to open the PDF (on a busy
                // runner, running cold, that has been over 300 ms). Counting the wait instead would leave a third file
                // 200 ms and the last none, so 250 still tells the two apart.
                Assert.True(until > DateTime.UtcNow.AddMilliseconds(250));
                int at = Interlocked.Increment(ref now);
                InterlockedMax(ref most, at);
                Thread.Sleep(400);
                Interlocked.Decrement(ref now);
                return pages.ToDictionary(p => p, _ => "read");
            },
        };
        var files = Enumerable.Range(0, 4).Select(i => dir[$"scan{i}.pdf"]).ToList();
        foreach (string f in files) TypedPdf(f, [[]]);

        var texts = await Task.WhenAll(files.Select(f => DocumentText.ExtractAsync(f, options, default)));

        // The last in line waited longer than its whole second, and still had it when its turn came.
        Assert.All(texts, t => Assert.Equal("read", t));
        Assert.Equal(1, most);
    }

    static void InterlockedMax(ref int target, int value)
    {
        int seen;
        while ((seen = Volatile.Read(ref target)) < value && Interlocked.CompareExchange(ref target, value, seen) != seen)
        {
        }
    }

    [Fact]
    public async Task Stopping_stops_the_reading_and_keeps_nothing()
    {
        using var dir = new TempDir();
        TypedPdf(dir["scan.pdf"], [[]]); // one page, nothing typed
        using var stop = new CancellationTokenSource();
        var options = new DocumentTextOptions
        {
            CacheDir = dir["cache"],
            PageReader = (_, _, _, ct) =>
            {
                stop.Cancel();
                ct.ThrowIfCancellationRequested();
                return new Dictionary<int, string>();
            },
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => DocumentText.ExtractAsync(dir["scan.pdf"], options, stop.Token));
        Assert.False(Directory.Exists(dir["cache"]));
    }

    [Fact]
    public async Task A_PDF_that_is_not_one_is_null()
    {
        using var dir = new TempDir();
        File.WriteAllText(dir["fake.pdf"], "%PDF-1.4 and then nothing a PDF should have");
        File.WriteAllBytes(dir["empty.pdf"], []);

        Assert.Null(await DocumentText.ExtractAsync(dir["fake.pdf"], NoCache, default));
        Assert.Null(await DocumentText.ExtractAsync(dir["empty.pdf"], NoCache, default));
    }

    [Fact]
    public async Task A_Word_document_reads_paragraph_by_paragraph_with_its_footnotes()
    {
        using var dir = new TempDir();
        string docx = dir["essay.docx"];
        Zip(docx, ("word/document.xml", WordXml("Mitochondria &amp; ATP", "The powerhouse of the cell.")),
            ("word/footnotes.xml", WordXml("Campbell Biology, ch. 9")));

        string? text = await DocumentText.ExtractAsync(docx, NoCache, default);

        Assert.Equal("Mitochondria & ATP\nThe powerhouse of the cell.\n\nCampbell Biology, ch. 9", text);
    }

    [Fact]
    public async Task A_PowerPoint_reads_its_slides_in_order_each_followed_by_its_speaker_notes()
    {
        using var dir = new TempDir();
        string pptx = dir["week 3.pptx"];
        // Written out of order, and slide10 after slide2 as a string sort would put it.
        Zip(pptx, ("ppt/slides/slide10.xml", SlideXml("Ten: recap")), ("ppt/slides/slide2.xml", SlideXml("Two: Big O", "O(n log n)")),
            ("ppt/notesSlides/notesSlide2.xml", SlideXml("Say merge sort is stable")), ("ppt/slides/slide1.xml", SlideXml("One: sorting")),
            ("ppt/slides/slide3.xml", SlideXml("")), ("ppt/slideLayouts/slideLayout1.xml", SlideXml("Click to add title")));

        string? text = await DocumentText.ExtractAsync(pptx, NoCache, default);

        Assert.Equal("One: sorting\n\nTwo: Big O\nO(n log n)\n\nSay merge sort is stable\n\nTen: recap", text);
    }

    [Fact]
    public async Task Plain_text_is_read_as_it_is_tidied()
    {
        using var dir = new TempDir();
        File.WriteAllText(dir["todo.md"], "\n\n# Midterm\r\n   \r\n\n\n\n\n- review recursion  \n");

        Assert.Equal("# Midterm\n\n\n- review recursion", await DocumentText.ExtractAsync(dir["todo.md"], NoCache, default));
    }

    [Fact]
    public async Task What_cannot_be_read_is_null_never_an_error()
    {
        using var dir = new TempDir();
        File.WriteAllText(dir["broken.docx"], "not a zip at all");
        Zip(dir["no body.docx"], ("word/styles.xml", "<w:styles/>"));
        Zip(dir["empty.pptx"], ("ppt/slides/slide1.xml", SlideXml("   ")));
        File.WriteAllText(dir["blank.txt"], " \n\t\n");
        File.WriteAllText(dir["song.mp3"], "ID3");

        Assert.Null(await DocumentText.ExtractAsync(dir["missing.pdf"], NoCache, default));
        Assert.Null(await DocumentText.ExtractAsync(dir["broken.docx"], NoCache, default));
        Assert.Null(await DocumentText.ExtractAsync(dir["no body.docx"], NoCache, default));
        Assert.Null(await DocumentText.ExtractAsync(dir["empty.pptx"], NoCache, default));
        Assert.Null(await DocumentText.ExtractAsync(dir["blank.txt"], NoCache, default));
        Assert.Null(await DocumentText.ExtractAsync(dir["song.mp3"], NoCache, default));
        Assert.False(DocumentText.CanRead("song.mp3"));
        Assert.True(DocumentText.CanRead("Scan.HEIC"));
    }

    [Fact]
    public async Task A_file_read_once_is_not_read_again_until_it_changes()
    {
        using var dir = new TempDir();
        var cached = new DocumentTextOptions { CacheDir = dir["cache"] };
        string notes = dir["notes.txt"];
        File.WriteAllText(notes, "Photosynthesis happens in the chloroplast.");
        Assert.Equal("Photosynthesis happens in the chloroplast.", await DocumentText.ExtractAsync(notes, cached, default));

        // What's kept is what's answered: the file isn't opened again.
        string kept = Assert.Single(Directory.GetFiles(dir["cache"]));
        string[] lines = File.ReadAllLines(kept);
        File.WriteAllText(kept, lines[0] + "\nfrom the cache");
        Assert.Equal("from the cache", await DocumentText.ExtractAsync(notes, cached, default));
        Assert.Equal("Photosynthesis happens in the chloroplast.", await DocumentText.ExtractAsync(notes, NoCache, default));

        // A changed file (a new time, the same size) is read again, and the new reading replaces the old.
        File.SetLastWriteTimeUtc(notes, DateTime.UtcNow.AddMinutes(1));
        Assert.Equal("Photosynthesis happens in the chloroplast.", await DocumentText.ExtractAsync(notes, cached, default));
        File.WriteAllText(notes, "Respiration happens in the mitochondria.");
        Assert.Equal("Respiration happens in the mitochondria.", await DocumentText.ExtractAsync(notes, cached, default));
        Assert.Single(Directory.GetFiles(dir["cache"]));
    }

    [Fact]
    public async Task A_file_with_no_words_is_remembered_as_having_none()
    {
        using var dir = new TempDir();
        var cached = new DocumentTextOptions { CacheDir = dir["cache"] };
        File.WriteAllText(dir["blank.txt"], "   ");
        Assert.Null(await DocumentText.ExtractAsync(dir["blank.txt"], cached, default));
        string kept = Assert.Single(Directory.GetFiles(dir["cache"]));
        Assert.Single(File.ReadAllLines(kept)); // the stamp, and no text
        Assert.Null(await DocumentText.ExtractAsync(dir["blank.txt"], cached, default));
    }

    [Fact]
    public async Task A_cache_that_cannot_be_written_only_means_reading_again()
    {
        using var dir = new TempDir();
        File.WriteAllText(dir["in the way"], "a file where the cache's folder would go");
        File.WriteAllText(dir["notes.txt"], "Ohm's law: V = IR");
        var cached = new DocumentTextOptions { CacheDir = Path.Combine(dir["in the way"], "cache") };
        Assert.Equal("Ohm's law: V = IR", await DocumentText.ExtractAsync(dir["notes.txt"], cached, default));
        Assert.Equal("Ohm's law: V = IR", await DocumentText.ExtractAsync(dir["notes.txt"], cached, default));
    }

    [Fact]
    public void Tidy_cuts_long_text_and_keeps_emoji_whole()
    {
        Assert.Null(DocumentText.Tidy(null));
        Assert.Null(DocumentText.Tidy(" \n "));
        string cut = DocumentText.Tidy(new string('a', DocumentText.MaxText - 1) + "😀 and more")!;
        Assert.Equal(DocumentText.MaxText - 1, cut.Length);
    }
}
