using StudyStash.Core.Ai;

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
        SaysTheNotes(text);
        Assert.True(text!.IndexOf("Midterm", StringComparison.OrdinalIgnoreCase) < text.IndexOf("averages", StringComparison.OrdinalIgnoreCase));
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
