using StudyStash.Core.Ai;

namespace StudyStash.Core.Tests;

/// <summary>Searching files that aren't lectures, and the folders Study Stash may read.</summary>
public class FileIndexTests
{
    [Fact]
    public async Task Files_are_found_by_their_words_lectures_are_left_to_their_own_search_and_private_folders_by_name_only()
    {
        using var dir = new TempDir();
        string lib = dir["library"], mine = dir["mine"], secret = dir["secret"];
        foreach (string d in new[] { Path.Combine(lib, "CS 101", "Canvas", "assignments", "Lab 1"), mine, secret }) Directory.CreateDirectory(d);
        File.WriteAllText(Path.Combine(lib, "CS 101", "Canvas", "assignments", "Lab 1", "spec.md"), "Implement quicksort recursively.");
        string lecture = Path.Combine(lib, "CS 101", "2026-09-24 Sorting.md");
        File.WriteAllText(lecture, "quicksort in the lecture");
        File.WriteAllText(Path.Combine(mine, "study plan.md"), "Review quicksort before the midterm.");
        File.WriteAllText(Path.Combine(secret, "quicksort taxes.txt"), "quicksort appears in private text");
        var index = new FileIndex(dir.Path, () => [("Library", lib, false), ("Mine", mine, false), ("Secret", secret, true)], () => [lecture]);
        await index.UpdateAsync();

        var hits = index.Search("quicksort");
        Assert.Contains(hits, h => h.Title == "Lab 1 (spec)" && h.Snippet.Contains("<mark>quicksort</mark>", StringComparison.Ordinal));
        Assert.Contains(hits, h => h.Root == "Mine");
        Assert.DoesNotContain(hits, h => h.Path == lecture);
        var priv = Assert.Single(hits, h => h.Root == "Secret");
        Assert.DoesNotContain("private text", priv.Snippet);
        Assert.Single(index.Search("midte")); // the last word may be half typed

        File.Delete(Path.Combine(mine, "study plan.md"));
        await index.UpdateAsync();
        Assert.Empty(index.Search("midterm"));
    }

    [Fact]
    public async Task PDFs_Word_PowerPoint_and_the_library_s_photos_are_searched_by_their_words_and_kept_read()
    {
        using var dir = new TempDir();
        string lib = dir["library"], mine = dir["mine"];
        Directory.CreateDirectory(Path.Combine(lib, "BIO 110", "Attachments"));
        Directory.CreateDirectory(mine);
        DocumentTextTests.TypedPdf(Path.Combine(lib, "BIO 110", "syllabus.pdf"), ["Lab safety quiz Friday"]);
        DocumentTextTests.Zip(Path.Combine(lib, "BIO 110", "reading.docx"), ("word/document.xml", DocumentTextTests.WordXml("Osmosis moves water")));
        DocumentTextTests.Zip(Path.Combine(mine, "slides.pptx"), ("ppt/slides/slide1.xml", DocumentTextTests.SlideXml("Glycolysis steps")));
        File.Copy(DocumentOcrTests.Fixture("handwriting.png"), Path.Combine(lib, "BIO 110", "Attachments", "board.png"));
        File.Copy(DocumentOcrTests.Fixture("handwriting.png"), Path.Combine(mine, "holiday.png"));
        var index = new FileIndex(dir.Path, () => [("Library", lib, false), ("Mine", mine, false)], () => []);
        await index.UpdateAsync();

        Assert.Equal("syllabus", Assert.Single(index.Search("quiz")).Title);
        Assert.Equal("reading", Assert.Single(index.Search("osmosis")).Title);
        Assert.Equal("slides", Assert.Single(index.Search("glycolysis")).Title);
        Assert.Equal(4, index.Files); // the photo outside the library isn't read, or even listed
        Assert.True(Directory.GetFiles(Path.Combine(dir.Path, "cache", "text")).Length >= 3);
        if (OperatingSystem.IsMacOS())
            Assert.Equal("board", Assert.Single(index.Search("mitochondria")).Title);
    }
}
