using StudyStash.Core;

namespace StudyStash.Core.Tests;

/// <summary>
/// Bringing in the notes of a student's other library folder (an old one in their personal OneDrive) into the one the
/// library uses (their school OneDrive's): what's only there comes in and is listed in its class, what's the same is
/// left, what differs is kept twice, what they deleted stays deleted, the other folder is never changed, and doing it
/// again changes nothing.
/// </summary>
public sealed class LibraryBringInTests
{
    sealed class Library(TempDir dir, string name, string pool) : IDisposable
    {
        public readonly Config Cfg = new(dir[name + "-home"], pool) { OllamaEnabled = false, Classes = [new ClassDef("Thermodynamics", [], "")] };
        public Store Store { get; } = new(System.IO.Path.Combine(dir[name + "-home"], "state.db"), pool);

        public string File(string id, string cls, string title, string summary, string date = "2026-09-14") =>
            Store.Save(new Meeting(id) { Title = title, Date = date, Transcript = "Today: the first law. " + id },
                new Classification(cls, 1.0, "human", title, ["energy"]), summary, "Claude");

        public void Dispose() => Store.Dispose();
    }

    static Dictionary<string, string> Files(string dir) => Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
        .ToDictionary(f => Path.GetRelativePath(dir, f), f => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(f))));

    [Fact]
    public void The_other_folders_notes_come_in_once_and_it_is_never_changed()
    {
        using var dir = new TempDir();
        string home = dir["home"];
        string school = Path.Combine(home, "OneDrive - Example University", "Documents", "Study Stash");
        string personal = Path.Combine(home, "OneDrive", "Documents", "Study Stash");
        using var main = new Library(dir, "main", school);
        // The old folder, as an older install's library left it: its own state.db is long gone.
        using (var old = new Library(dir, "old", personal))
        {
            old.File("same", "Thermodynamics", "Heat engines", "Carnot's cycle.");
            old.File("edited", "Thermodynamics", "Entropy", "Entropy only grows, as first written.");
            old.File("only-old", "Thermodynamics", "First law", "Energy is conserved.", "2026-09-07");
            old.File("con", "Con", "Opening night", "A class Windows won't name a folder after.");
            old.File("deleted", "Thermodynamics", "A lecture deleted since", "Gone.");
            old.File("deleted-long-ago", "Thermodynamics", "Deleted for good long ago", "Gone for good.");
        }
        // The old folder's slides: its state.db, which knew their lecture, is gone; they come in with their class.
        string slides = Path.Combine(personal, "Thermodynamics", Store.AttachmentsFolder, "slides.pdf");
        Directory.CreateDirectory(Path.GetDirectoryName(slides)!);
        File.WriteAllBytes(slides, [.. "%PDF-1.7 Carnot cycle slides"u8]);
        // Written on Windows: every line ends "\r\n". A copy keeps that, and it's the same lecture as one ending "\n".
        foreach (string f in Directory.EnumerateFiles(personal, "*.md", SearchOption.AllDirectories))
            File.WriteAllText(f, File.ReadAllText(f).Replace("\r\n", "\n").Replace("\n", "\r\n"));
        main.File("same", "Thermodynamics", "Heat engines", "Carnot's cycle.");
        main.File("edited", "Thermodynamics", "Entropy", "Entropy only grows, edited since.");
        main.File("deleted", "Thermodynamics", "A lecture deleted since", "Gone.");
        Assert.True(main.Store.Trash("deleted"));
        // Deleted for good long ago: past the month Gone() remembers, it's still known as deleted.
        main.File("deleted-long-ago", "Thermodynamics", "Deleted for good long ago", "Gone for good.");
        Assert.True(main.Store.Delete("deleted-long-ago"));
        Assert.DoesNotContain("deleted-long-ago", main.Store.Gone());
        var before = Files(personal);
        Assert.True(Directory.Exists(Path.Combine(personal, "Con_")));

        var plan = LibraryBringIn.Plan(main.Store, main.Cfg, school, personal, home);
        var did = LibraryBringIn.Apply(main.Store, main.Cfg, personal, home);

        Assert.Equal((2, 1, 1, 2, 1, 1), (plan.New, plan.Here, plan.Both, plan.Deleted, plan.Classes, plan.Files));
        Assert.Equal(plan, did);
        // Only in the old folder: listed here, in its class, its note file copied exactly into this folder.
        var first = main.Store.Get("only-old")!;
        Assert.Equal("Thermodynamics", first.ClassName);
        Assert.StartsWith(Path.Combine(school, "Thermodynamics"), first.MdPath);
        Assert.Equal(File.ReadAllText(Path.Combine(personal, "Thermodynamics", "2026-09-07 First law.md")), File.ReadAllText(first.MdPath!));
        Assert.Contains("Energy is conserved.", first.SummaryMd);
        // The class Windows dislikes: added, with a folder Windows can make.
        Assert.Contains("Con", Configs.Load(main.Cfg.Home).ClassNames());
        Assert.StartsWith(Path.Combine(school, "Con_"), main.Store.Get("con")!.MdPath);
        // Changed in both: this folder's stays, the other's is beside it with where it came from.
        Assert.Contains("edited since", main.Store.Get("edited")!.SummaryMd);
        var copy = main.Store.ListNotes("Thermodynamics").Single(r => r.Id.StartsWith("edited-", StringComparison.Ordinal));
        Assert.Equal("Entropy (from OneDrive – Personal)", copy.LectureTitle);
        Assert.Contains("as first written", copy.SummaryMd);
        Assert.Contains("id: \"" + copy.Id + "\"", File.ReadAllText(copy.MdPath!));
        // Deleted here: stays deleted. One Thermodynamics, however many folders had it.
        Assert.Null(main.Store.Get("deleted"));
        Assert.Null(main.Store.Get("deleted-long-ago"));
        // The slides: copied into the class's Attachments here, and listed with the class.
        var slide = Assert.Single(main.Store.ListAttachments("Thermodynamics"));
        Assert.Equal(("slides.pdf", "application/pdf", null), (slide.Name, slide.Type, slide.NoteId));
        Assert.Equal(File.ReadAllBytes(slides), File.ReadAllBytes(main.Store.AttachmentPath(slide)));
        Assert.Single(Directory.GetDirectories(school), d => Path.GetFileName(d).Equals("Thermodynamics", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(before, Files(personal));

        // Again: nothing new, nothing changed.
        var mainFiles = Files(school);
        int count = main.Store.NoteCount();
        var again = LibraryBringIn.Apply(main.Store, main.Cfg, personal, home);
        Assert.Equal((0, 4, 0, 2, 0, 0), (again.New, again.Here, again.Both, again.Deleted, again.Classes, again.Files));
        Assert.Single(main.Store.ListAttachments("Thermodynamics"));
        Assert.Equal(mainFiles, Files(school));
        Assert.Equal(count, main.Store.NoteCount());
        Assert.Equal(before, Files(personal));
    }

    [Fact]
    public void Lectures_listed_from_the_old_folder_get_their_files_copied_in()
    {
        using var dir = new TempDir();
        string home = dir["home"];
        // A long OneDrive path, past Windows' old 260-character limit once a class and a title are on it.
        string school = Path.Combine(home, "OneDrive - Example University of the Long Name Department of Mechanical Engineering",
            "Documents", "Study Stash");
        string personal = Path.Combine(home, "OneDrive", "Documents", "Study Stash");
        string cls = "Thermodynamics and Heat Transfer for Mechanical Engineers II";
        string title = "Steady flow energy equation for nozzles diffusers turbines compressors and throttling valves";
        // One state.db, used with the old folder first: its lectures' rows point there.
        string listed;
        using (var before = new Library(dir, "main", personal)) listed = before.File("old", cls, title, "Nozzles speed a flow up.");
        using var main = new Library(dir, "main", school);
        // The new folder has a lecture state.db doesn't list (written by a copy of the app whose state.db was new).
        using (var other = new Library(dir, "other", school)) other.File("unlisted", cls, "Mixing chambers", "Mass in equals mass out.");
        Assert.Equal(1, LibraryBringIn.ListOwn(main.Store, main.Cfg));
        Assert.True(main.Store.Get("unlisted") is { MdPath: { } p } && Py.SamePath(p, Path.Combine(school, Notes.FolderName(cls), "2026-09-14 Mixing chambers.md")));

        // The old folder also has a lecture this state.db never listed: "Use this one" alone leaves it there.
        using (var stray = new Library(dir, "stray", personal)) stray.File("stray", cls, "Nozzle efficiency", "Not listed here.");

        var did = LibraryBringIn.Refile(main.Store, main.Cfg, personal, home);

        Assert.Equal((1, 0, 0), (did.New, did.Here, did.Both));
        Assert.Null(main.Store.Get("stray"));
        string moved = main.Store.Get("old")!.MdPath!;
        Assert.True(moved.Length > 260, moved);
        Assert.StartsWith(school, moved);
        Assert.Equal(File.ReadAllText(listed), File.ReadAllText(moved));
        Assert.True(File.Exists(listed));
        Assert.Equal(0, LibraryBringIn.Refile(main.Store, main.Cfg, personal, home).New);
        Assert.Equal(1, LibraryBringIn.Apply(main.Store, main.Cfg, personal, home).New); // the stray one, when asked

        // A write cut short (a crash, a full disk) leaves no half file to be taken for the lecture next time.
        string half = Path.Combine(school, "Half.md");
        Assert.Throws<IOException>(() => Store.PutWhole(half, temp =>
        {
            File.WriteAllText(temp, "---\nid: \"half\"\n");
            throw new IOException("disk full");
        }, overwrite: false));
        Assert.False(File.Exists(half));
        Assert.Empty(Directory.EnumerateFiles(school, "*.part", SearchOption.AllDirectories));
    }
}
