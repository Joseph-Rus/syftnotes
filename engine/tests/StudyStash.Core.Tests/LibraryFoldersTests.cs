using StudyStash.Core;

namespace StudyStash.Core.Tests;

/// <summary>A student whose Documents moved from a personal OneDrive to a school one, with a library made in each: both
/// are found, an empty folder and a second way to the same folder aren't, and an answer for them stops the asking.</summary>
public sealed class LibraryFoldersTests
{
    static void Note(string library, string cls, string file)
    {
        Directory.CreateDirectory(Path.Combine(library, cls));
        File.WriteAllText(Path.Combine(library, cls, file), "# Notes\n");
    }

    [Fact]
    public void Two_real_libraries_are_found_and_asked_about_once()
    {
        using var tmp = new TempDir();
        string home = tmp["home"];
        string old = Path.Combine(home, "OneDrive", "Documents", "Study Stash");
        string school = Path.Combine(home, "OneDrive - Example University", "Documents", "Study Stash");
        Note(old, "Biology", "2026-09-28 Cells.md");
        Note(old, "Biology", "2026-10-02 Cell division.md");
        Note(old, "Chemistry", "2026-10-01 Bonds.md");
        Note(school, "Biology", "2026-11-01 Genes.md");
        // Documents itself: a library folder with no lecture in it (an empty class, the Inbox's captured notes).
        string empty = Path.Combine(home, "Documents", "Study Stash");
        Directory.CreateDirectory(Path.Combine(empty, "Physics"));
        Note(empty, "Inbox", "captured.md");

        var places = LibraryFolders.Places(school, Path.Combine(home, "OneDrive - Example University", "Documents"), home, "Windows", _ => null);
        // The same folder again: by a link to it, and (where case doesn't matter) in other letters.
        string link = tmp["link"];
        try
        {
            Directory.CreateSymbolicLink(link, school);
            places.Add(link);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Windows without the right to make links: the case check below still runs.
        }
        if (!OperatingSystem.IsLinux()) places.Add(school.ToUpperInvariant());

        var found = LibraryFolders.Find(school, places);

        Assert.Equal(2, found.Count);
        Assert.True(found[0].InUse);
        Assert.Equal((1, new DateOnly(2026, 11, 1)), (found[0].Lectures, found[0].Newest!.Value));
        Assert.False(found[1].InUse);
        Assert.Equal((3, new DateOnly(2026, 10, 2)), (found[1].Lectures, found[1].Newest!.Value));
        Assert.Equal("OneDrive – Example University › Documents", LibraryFolders.Where(found[0].Path, LibraryFolders.Real(home)));
        Assert.Equal("OneDrive – Personal › Documents", LibraryFolders.Where(found[1].Path, LibraryFolders.Real(home)));

        Assert.True(LibraryFolders.ShouldAsk(found, null, null));
        // Answered for these two (kept the school one): not asked again.
        string[] answered = [.. found.Select(f => f.Path)];
        Assert.False(LibraryFolders.ShouldAsk(found, answered, school));
        // A third library turns up: asked again.
        string third = Path.Combine(home, "Documents", "Syft Notes");
        Note(third, "History", "2026-11-02 Rome.md");
        places.Add(third);
        Assert.True(LibraryFolders.ShouldAsk(LibraryFolders.Find(school, places), answered, school));
    }

    /// <summary>On a Mac: what iCloud Drive left of Documents, and a library folder the student renamed, are found too.</summary>
    [Fact]
    public void A_renamed_folder_and_iCloud_s_leftover_are_found()
    {
        using var tmp = new TempDir();
        string home = tmp["home"], docs = Path.Combine(home, "Documents");
        string renamed = Path.Combine(docs, "Study Stash old"), syft = Path.Combine(docs, "Syft Notes");
        string icloud = Path.Combine(home, "Library", "Mobile Documents", "com~apple~CloudDocs", "Documents", "Study Stash");
        foreach (string library in new[] { renamed, syft, icloud }) Note(library, "Biology", "2026-09-28 Cells.md");
        // A folder with the name but no notes isn't a library.
        Directory.CreateDirectory(Path.Combine(docs, "Study Stash empty", "Biology"));

        var found = LibraryFolders.Find(null, LibraryFolders.Places(null, docs, home, "Darwin", _ => null));

        Assert.Equal(3, found.Count);
        Assert.Equal(new[] { renamed, syft, icloud }.Select(LibraryFolders.Real).Order(), found.Select(f => f.Path).Order());
    }
}
