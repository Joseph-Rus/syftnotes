using System.IO.Compression;
using StudyStash.Core.Ai;

namespace StudyStash.Core.Tests;

/// <summary>Reading the words in the files a student hands the library: Word, PowerPoint, plain text.</summary>
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
    public void Tidy_cuts_long_text_and_keeps_emoji_whole()
    {
        Assert.Null(DocumentText.Tidy(null));
        Assert.Null(DocumentText.Tidy(" \n "));
        string cut = DocumentText.Tidy(new string('a', DocumentText.MaxText - 1) + "😀 and more")!;
        Assert.Equal(DocumentText.MaxText - 1, cut.Length);
    }
}
