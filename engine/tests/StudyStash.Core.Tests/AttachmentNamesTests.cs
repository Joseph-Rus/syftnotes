namespace StudyStash.Core.Tests;

/// <summary>Naming an attachment safely, telling what it really is, and cutting its words to fit a prompt.</summary>
public class AttachmentNamesTests
{
    [Theory]
    [InlineData("Week 3 slides.pdf", "Week 3 slides.pdf")]
    [InlineData("../../etc/passwd", "passwd")]
    [InlineData("..\\..\\Windows\\win.ini", "win.ini")]
    [InlineData("C:\\Users\\sam\\notes.PDF", "notes.pdf")]
    [InlineData("/absolute/path/scan.png", "scan.png")]
    [InlineData(".hidden", "hidden")]
    [InlineData("...", "attachment")]
    [InlineData("", "attachment")]
    [InlineData(null, "attachment")]
    [InlineData("a:b*c?d\"e<f>g|h.txt", "a b c d e f g h.txt")]
    [InlineData("tab\tand\nnewline.md", "tab and newline.md")]
    [InlineData("CON.txt", "CON_.txt")]
    [InlineData("nul", "nul_")]
    [InlineData("trailing dots...", "trailing dots")]
    public void A_name_is_only_ever_a_safe_file_name(string? sent, string expected) => Assert.Equal(expected, Attachments.SafeName(sent));

    [Fact]
    public void A_long_name_is_cut_but_keeps_its_extension()
    {
        string name = Attachments.SafeName(new string('x', 300) + ".pptx");
        Assert.EndsWith(".pptx", name);
        Assert.True(name.Length <= 105);
    }

    [Fact]
    public void A_taken_name_gets_a_number()
    {
        using var dir = new TempDir();
        Assert.Equal("slides.pdf", Attachments.FreeName(dir.Path, "slides.pdf"));
        File.WriteAllText(dir["slides.pdf"], "x");
        File.WriteAllText(dir["slides (2).pdf"], "x");
        Assert.Equal("slides (3).pdf", Attachments.FreeName(dir.Path, "slides.pdf"));
    }

    [Theory]
    [InlineData(new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D, 0x31 }, "x.bin", "application/pdf")]
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0 }, "photo.jpg", "image/png")]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }, "page", "image/jpeg")]
    [InlineData(new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61 }, "a.gif", "image/gif")]
    [InlineData(new byte[] { 0, 0, 0, 0x18, 0x66, 0x74, 0x79, 0x70, 0x68, 0x65, 0x69, 0x63 }, "IMG_1.HEIC", "image/heic")]
    [InlineData(new byte[] { 0x50, 0x4B, 3, 4, 0 }, "deck.pptx", "application/vnd.openxmlformats-officedocument.presentationml.presentation")]
    [InlineData(new byte[] { 0x50, 0x4B, 3, 4, 0 }, "archive.zip", "application/zip")]
    [InlineData(new byte[] { 0x3C, 0x68, 0x74, 0x6D, 0x6C, 0x3E }, "page.html", "application/octet-stream")]
    [InlineData(new byte[] { 0x3C, 0x73, 0x76, 0x67, 0x3E }, "evil.svg", "application/octet-stream")]
    [InlineData(new byte[] { 0x68, 0x69, 0x0A }, "notes.txt", "text/plain")]
    [InlineData(new byte[] { 0x68, 0x69, 0x00 }, "notes.txt", "application/octet-stream")]
    [InlineData(new byte[] { 0x25, 0x50 }, "fake.pdf", "application/octet-stream")]
    public void What_a_file_is_comes_from_its_bytes(byte[] head, string name, string expected) => Assert.Equal(expected, Attachments.Sniff(head, name));

    [Fact]
    public void Only_pdfs_and_pictures_open_in_the_browser()
    {
        Assert.True(Attachments.ShowsInline("application/pdf"));
        Assert.True(Attachments.ShowsInline("image/png"));
        Assert.False(Attachments.ShowsInline("image/svg+xml"));
        Assert.False(Attachments.ShowsInline("text/plain"));
        Assert.False(Attachments.ShowsInline("application/octet-stream"));
    }

    [Theory]
    [InlineData("Lecture 4.pptx", "application/zip", Attachments.Slides)]
    [InlineData("IMG_2231.heic", "image/heic", Attachments.OwnNotes)]
    [InlineData("GoodNotes export.pdf", "application/pdf", Attachments.OwnNotes)]
    [InlineData("My handwritten notes.pdf", "application/pdf", Attachments.OwnNotes)]
    [InlineData("Week 3 slides.pdf", "application/pdf", Attachments.Slides)]
    [InlineData("Syllabus.pdf", "application/pdf", Attachments.Handout)]
    public void Each_attachment_is_named_for_what_it_is(string name, string type, string expected) => Assert.Equal(expected, Attachments.KindOf(name, type));

    [Fact]
    public void The_context_labels_each_attachment_and_stays_within_its_size()
    {
        var attached = new List<AttachedText>
        {
            new("a", Attachments.OwnNotes, "notes.pdf", new string('n', 20_000)),
            new("b", Attachments.Slides, "deck.pptx", "Short slide text."),
            new("c", Attachments.Handout, "empty.pdf", "   "),
        };
        string context = Attachments.Context(attached, 3000);
        Assert.True(context.Length <= 3000, context.Length.ToString());
        Assert.Contains("The student's own notes (notes.pdf):\n", context);
        Assert.Contains("Slides (deck.pptx):\nShort slide text.", context);
        Assert.DoesNotContain("empty.pdf", context);
        Assert.Contains("…", context);
    }

    [Fact]
    public void Room_one_attachment_leaves_goes_to_the_next()
    {
        var attached = new List<AttachedText> { new("a", Attachments.Slides, "a.pdf", "tiny"), new("b", Attachments.Handout, "b.pdf", new string('b', 5000)) };
        string context = Attachments.Context(attached, 2000);
        Assert.True(context.Count(c => c == 'b') > 1500);
    }

    [Fact]
    public void No_words_no_context()
    {
        Assert.Equal("", Attachments.Context([]));
        Assert.Equal("", Attachments.Context([new("a", Attachments.Slides, "a.pdf", "")]));
        Assert.Equal("", Attachments.Context([new("a", Attachments.Slides, "a.pdf", "text")], 0));
    }

    [Fact]
    public void Words_are_cut_into_passages_of_a_few_sentences()
    {
        string text = string.Join("\n\n", Enumerable.Range(1, 30).Select(i => $"Paragraph {i} says something. It goes on a little."));
        var pieces = Attachments.Pieces(text, 200);
        Assert.True(pieces.Count > 5);
        Assert.All(pieces, p => Assert.True(p.Length <= 200, p));
        Assert.Contains("Paragraph 30", pieces[^1]);
        var one = Attachments.Pieces(new string('w', 1000), 200);
        Assert.All(one, p => Assert.True(p.Length <= 200));
        Assert.Equal(1000, one.Sum(p => p.Length));
    }

    [Theory]
    [InlineData(12, "12 bytes")]
    [InlineData(2048, "2 KB")]
    [InlineData(2_516_582, "2.4 MB")]
    [InlineData(52_428_800, "50 MB")]
    public void Sizes_read_plainly(long bytes, string expected) => Assert.Equal(expected, Attachments.SizeLabel(bytes));
}
