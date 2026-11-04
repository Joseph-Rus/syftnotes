using System.Text.Json.Nodes;
using StudyStash.Core;
using StudyStash.Library;

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
            old.File("con-2", "Con", "Second night", "The new class is counted once, not per lecture.");
            old.File("deleted", "Thermodynamics", "A lecture deleted since", "Gone.");
            old.File("deleted-long-ago", "Thermodynamics", "Deleted for good long ago", "Gone for good.");
        }
        // The old folder's slides: its state.db, which knew their lecture, is gone; they come in with their class.
        string slides = Path.Combine(personal, "Thermodynamics", Store.AttachmentsFolder, "slides.pdf");
        Directory.CreateDirectory(Path.GetDirectoryName(slides)!);
        File.WriteAllBytes(slides, [.. "%PDF-1.7 Carnot cycle slides"u8]);
        // The same slides attached in a second class: one attachment in each.
        string alsoSlides = Path.Combine(personal, "Con_", Store.AttachmentsFolder, "slides.pdf");
        Directory.CreateDirectory(Path.GetDirectoryName(alsoSlides)!);
        File.Copy(slides, alsoSlides);
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

        Assert.Equal((3, 1, 1, 2, 1, 2), (plan.New, plan.Here, plan.Both, plan.Deleted, plan.Classes, plan.Files));
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
        var conSlide = Assert.Single(main.Store.ListAttachments("Con"));
        Assert.NotEqual(slide.Id, conSlide.Id);
        // Removed by the student: it doesn't come back next time.
        Assert.True(main.Store.RemoveAttachment(conSlide.Id));
        Assert.Single(Directory.GetDirectories(school), d => Path.GetFileName(d).Equals("Thermodynamics", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(before, Files(personal));

        // Again: nothing new, nothing changed.
        var mainFiles = Files(school); // after the removal
        int count = main.Store.NoteCount();
        var again = LibraryBringIn.Apply(main.Store, main.Cfg, personal, home);
        Assert.Equal((0, 5, 0, 2, 0, 0), (again.New, again.Here, again.Both, again.Deleted, again.Classes, again.Files));
        Assert.Single(main.Store.ListAttachments("Thermodynamics"));
        Assert.Empty(main.Store.ListAttachments("Con"));
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

        // A lecture in the trash when the folder changed, filed in the old folder.
        using (var before = new Library(dir, "main", personal)) before.File("binned", cls, "Throttling", "Enthalpy stays the same.");
        Assert.True(main.Store.Trash("binned"));
        // What the student wrote by hand there: a capture waiting in the Inbox, and a note filed into the class's Notes.
        string capture = Path.Combine(personal, "Inbox", "2026-09-20 Ask about the exam.md");
        string classNote = Path.Combine(personal, Notes.FolderName(cls), "Notes", "Formula sheet.md");
        foreach (string f in new[] { capture, classNote })
        {
            Directory.CreateDirectory(Path.GetDirectoryName(f)!);
            File.WriteAllText(f, "Written by hand: " + Path.GetFileName(f));
        }
        // The old folder also has a lecture this state.db never listed: "Use this one" alone leaves it there.
        using (var stray = new Library(dir, "stray", personal)) stray.File("stray", cls, "Nozzle efficiency", "Not listed here.");

        var did = LibraryBringIn.Refile(main.Store, main.Cfg, personal, home);

        Assert.Equal((1, 0, 0, 2), (did.New, did.Here, did.Both, did.Files));
        Assert.Null(main.Store.Get("stray"));
        // This folder's own unlisted lecture is listed where it is.
        Assert.True(main.Store.Get("unlisted") is { MdPath: { } p } && Py.SamePath(p, Path.Combine(school, Notes.FolderName(cls), "2026-09-14 Mixing chambers.md")));
        foreach (string f in new[] { capture, classNote })
            Assert.Equal(File.ReadAllText(f), File.ReadAllText(Path.Combine(school, Path.GetRelativePath(personal, f))));
        // The binned lecture comes back into the folder in use, beside (never over) the lecture of the same class, day
        // and title there.
        string mine = main.File("mine", cls, "Throttling", "This folder's own throttling lecture.");
        string mineText = File.ReadAllText(mine);
        Assert.True(main.Store.Restore("binned"));
        string binned = main.Store.Get("binned")!.MdPath!;
        Assert.StartsWith(school, binned);
        Assert.NotEqual(mine, binned);
        Assert.Equal(mineText, File.ReadAllText(mine));
        Assert.Contains("Enthalpy stays the same.", File.ReadAllText(binned));
        // A capture the student filed (or deleted) since, and a class note they edited, don't come back.
        File.Delete(Path.Combine(school, Path.GetRelativePath(personal, capture)));
        File.WriteAllText(Path.Combine(school, Path.GetRelativePath(personal, classNote)), "Edited since.");
        string moved = main.Store.Get("old")!.MdPath!;
        Assert.True(moved.Length > 260, moved);
        Assert.StartsWith(school, moved);
        Assert.Equal(File.ReadAllText(listed), File.ReadAllText(moved));
        Assert.True(File.Exists(listed));
        Assert.Equal((0, 0), LibraryBringIn.Refile(main.Store, main.Cfg, personal, home) is var again ? (again.New, again.Files) : default);
        Assert.False(File.Exists(Path.Combine(school, Path.GetRelativePath(personal, capture))));
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(Path.Combine(school, Path.GetRelativePath(personal, classNote)))!));
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

    /// <summary>The library's job finishes a switch only when no lecture is left filed in the folder it leaves: one
    /// reached through a link is found and copied in; a library still reading the old folder (another copy's, answering
    /// from before the switch) ends the job with an error instead of passing it over.</summary>
    [Fact]
    public async Task A_switch_is_done_only_when_nothing_is_left_in_the_old_folder()
    {
        using var dir = new TempDir();
        string home = dir["home"];
        string school = Path.Combine(home, "OneDrive - Example University", "Documents", "Study Stash");
        string personal = Path.Combine(home, "OneDrive", "Documents", "Study Stash");
        // The old folder, reached by the student's link to it: the rows are written that way.
        string link = Path.Combine(home, "Notes link");
        Directory.CreateDirectory(personal);
        try
        {
            Directory.CreateSymbolicLink(link, Path.GetDirectoryName(personal)!);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            link = Path.GetDirectoryName(personal)!; // Windows without the right to make links: the plain path
        }
        using (var before = new Library(dir, "main", Path.Combine(link, "Study Stash"))) before.File("old", "Thermodynamics", "Heat engines", "Carnot.");

        async Task<BringInStatus> RunAsync(Library lib, string leave)
        {
            await using var site = await TestSite.StartAsync(b => LibraryWeb.Build(b, lib.Cfg, lib.Store, new Pipeline(lib.Cfg, lib.Store, log: _ => { }),
                new LibraryWebOptions { ListModels = _ => Task.FromResult<List<(string, double)>?>(null), Latest = _ => Task.FromResult<Release?>(null) }));
            var remote = new RemoteLibrary("http://localhost", "", site.Client);
            var ask = new JsonObject { ["asked"] = "t", ["kind"] = BringInJob.Bring, ["refile"] = new JsonArray(leave) };
            var job = await remote.StartFolderJobAsync(ask, TestContext.Current.CancellationToken);
            Assert.True(await Waiting.UntilAsync(async () => (job = await remote.FolderJobAsync(TestContext.Current.CancellationToken))!.Running == false));
            return job!;
        }

        // The library answering still reads the old folder: the job says so.
        using (var stale = new Library(dir, "main", personal))
        {
            var refused = await RunAsync(stale, personal);
            Assert.NotNull(refused.Error);
            Assert.Equal(1, LibraryBringIn.PointingInto(stale.Store, personal));
        }
        // The library reads the new folder: the lecture filed through the link is copied in, and nothing is left there.
        using var main = new Library(dir, "main", school);
        var done = await RunAsync(main, personal);
        Assert.Null(done.Error);
        Assert.Equal(0, LibraryBringIn.PointingInto(main.Store, personal));
        Assert.StartsWith(school, main.Store.Get("old")!.MdPath);
    }

    /// <summary>Copying in after a switch never points one lecture or attachment at another's file: a lecture whose file
    /// is gone from the old folder, and an attachment whose name the new folder has for a different file, each get a
    /// free name, so deleting them never deletes the other.</summary>
    [Fact]
    public void A_lecture_or_attachment_never_takes_over_another_file()
    {
        using var dir = new TempDir();
        string home = dir["home"];
        string school = Path.Combine(home, "OneDrive - Example University", "Documents", "Study Stash");
        string personal = Path.Combine(home, "OneDrive", "Documents", "Study Stash");
        // X was filed in the old folder, and its file has gone from there since.
        using (var before = new Library(dir, "main", personal)) File.Delete(before.File("x", "Thermodynamics", "Entropy", "X's notes."));
        using var main = new Library(dir, "main", school);
        // Y: this folder's own lecture of the same class, day and title.
        string y = main.File("y", "Thermodynamics", "Entropy", "Y's notes.");
        string yText = File.ReadAllText(y);
        // Two attachments listed here, each with a different file of its name in each folder; the size each was listed
        // with says whose it is. slides.pdf was added in the old folder; IMG_0001.jpg in this one.
        string theirs = Path.Combine(personal, "Thermodynamics", Store.AttachmentsFolder, "slides.pdf");
        string ours = Path.Combine(school, "Thermodynamics", Store.AttachmentsFolder, "slides.pdf");
        string photoHere = Path.Combine(school, "Thermodynamics", Store.AttachmentsFolder, "IMG_0001.jpg");
        string photoThere = Path.Combine(personal, "Thermodynamics", Store.AttachmentsFolder, "IMG_0001.jpg");
        foreach (var (path, words) in new[] { (theirs, "%PDF old folder's slides"), (ours, "%PDF this folder's own slides"),
                     (photoHere, "this folder's whiteboard photo"), (photoThere, "an old photo of the same name") })
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, words);
        }
        main.Store.AddAttachment(new Attachment("slides", "slides.pdf", "slides.pdf", "Thermodynamics", null, new FileInfo(theirs).Length, "application/pdf", "2026-09-14"));
        main.Store.AddAttachment(new Attachment("photo", "IMG_0001.jpg", "IMG_0001.jpg", "Thermodynamics", null, new FileInfo(photoHere).Length, "image/jpeg", "2026-11-02"));
        // Two listed with one name (a phone's "page 1.jpg", added once while each folder was in use), sizes unknown.
        string pageHere = Path.Combine(school, "Thermodynamics", Store.AttachmentsFolder, "page 1.jpg");
        string pageThere = Path.Combine(personal, "Thermodynamics", Store.AttachmentsFolder, "page 1.jpg");
        File.WriteAllText(pageHere, "this folder's page");
        File.WriteAllText(pageThere, "the old folder's page");
        // A handout listed from the old folder, with nothing of its name in this one.
        string handoutThere = Path.Combine(personal, "Thermodynamics", Store.AttachmentsFolder, "handout.pdf");
        File.WriteAllText(handoutThere, "%PDF the handout");
        main.Store.AddAttachment(new Attachment("handout", "handout.pdf", "handout.pdf", "Thermodynamics", null, new FileInfo(handoutThere).Length, "application/pdf", "2026-09-10"));
        foreach (string id in new[] { "page-a", "page-b" })
            main.Store.AddAttachment(new Attachment(id, "page 1.jpg", "page 1.jpg", "Thermodynamics", null, 0, "image/jpeg", "2026-10-01"));
        string dirHere = Path.Combine(school, "Thermodynamics", Store.AttachmentsFolder), dirThere = Path.Combine(personal, "Thermodynamics", Store.AttachmentsFolder);
        // The same slides on two lectures: two names, the same bytes, only in the old folder.
        foreach (string name in new[] { "week 3.pdf", "week 3 (2).pdf" })
        {
            File.WriteAllText(Path.Combine(dirThere, name), "%PDF week three");
            main.Store.AddAttachment(new Attachment(name, name, name, "Thermodynamics", null, 15, "application/pdf", "2026-09-21"));
        }
        // A sheet added in this folder and annotated since (its listed size is the original's, which the old folder has).
        File.WriteAllText(Path.Combine(dirThere, "sheet.pdf"), "%PDF sheet");
        File.WriteAllText(Path.Combine(dirHere, "sheet.pdf"), "%PDF sheet, annotated here");
        main.Store.AddAttachment(new Attachment("sheet", "sheet.pdf", "sheet.pdf", "Thermodynamics", null, 10, "application/pdf", "2026-10-20"));
        // Two scans of one name: A's file is gone from the old folder, B was uploaded here.
        File.WriteAllText(Path.Combine(dirHere, "scan.jpg"), "B's scan");
        main.Store.AddAttachment(new Attachment("scan-a", "scan.jpg", "scan.jpg", "Thermodynamics", null, 99, "image/jpeg", "2026-09-01"));
        main.Store.AddAttachment(new Attachment("scan-b", "scan.jpg", "scan.jpg", "Thermodynamics", null, 8, "image/jpeg", "2026-10-30"));

        var plan = LibraryBringIn.Plan(main.Store, main.Cfg, school, personal, home);
        var did = LibraryBringIn.Apply(main.Store, main.Cfg, personal, home);

        // slides beside, the handout in, the second page beside, the old IMG_0001.jpg as its own, both weeks in, and the
        // sheet's original beside: what was said, done.
        Assert.Equal(7, did.Files);
        Assert.Equal(plan.Files, did.Files);
        foreach (string name in new[] { "week 3.pdf", "week 3 (2).pdf" })
            Assert.Equal("%PDF week three", File.ReadAllText(main.Store.AttachmentPath(main.Store.GetAttachment(name)!)));
        // The annotated sheet the attachment moved off (to the old original) is listed as the class's own, not hidden.
        Assert.Contains(main.Store.ListAttachments("Thermodynamics"), a => a.Id != "sheet"
            && File.Exists(main.Store.AttachmentPath(a)) && File.ReadAllText(main.Store.AttachmentPath(a)) == "%PDF sheet, annotated here");
        // The scans never share a file: removing A leaves B's.
        Assert.NotEqual(main.Store.AttachmentPath(main.Store.GetAttachment("scan-a")!), main.Store.AttachmentPath(main.Store.GetAttachment("scan-b")!));
        Assert.True(main.Store.RemoveAttachment("scan-a"));
        Assert.Equal("B's scan", File.ReadAllText(main.Store.AttachmentPath(main.Store.GetAttachment("scan-b")!)));
        // X's file was gone: it's written afresh from its row, at a path of its own.
        string x = main.Store.Get("x")!.MdPath!;
        Assert.Contains("X's notes.", File.ReadAllText(x));
        Assert.NotEqual(y, main.Store.Get("x")!.MdPath);
        Assert.True(main.Store.Trash("x"));
        Assert.Equal(yText, File.ReadAllText(y));
        var moved = main.Store.GetAttachment("slides")!;
        Assert.NotEqual("slides.pdf", moved.File);
        Assert.Equal("%PDF old folder's slides", File.ReadAllText(main.Store.AttachmentPath(moved)));
        // The handout came in under its own name. The student annotates it there; running again keeps their file, though
        // its listed size is stale now and the old folder's original matches it.
        string handout = main.Store.AttachmentPath(main.Store.GetAttachment("handout")!);
        Assert.Equal("%PDF the handout", File.ReadAllText(handout));
        File.WriteAllText(handout, "%PDF the handout, annotated by the student");
        Assert.Equal(0, LibraryBringIn.Apply(main.Store, main.Cfg, personal, home).Files);
        Assert.Equal(handout, main.Store.AttachmentPath(main.Store.GetAttachment("handout")!));
        Assert.Equal("%PDF the handout, annotated by the student", File.ReadAllText(handout));
        // The photo added in this folder stays this folder's file.
        Assert.Equal("this folder's whiteboard photo", File.ReadAllText(main.Store.AttachmentPath(main.Store.GetAttachment("photo")!)));
        // The old folder's photo that no listed attachment took comes in as its own.
        Assert.Contains(main.Store.ListAttachments("Thermodynamics"), a => a.Name == "IMG_0001.jpg" && a.Id != "photo"
            && File.ReadAllText(main.Store.AttachmentPath(a)) == "an old photo of the same name");
        // The two pages: each its own file, so removing one leaves the other's.
        string pageA = main.Store.AttachmentPath(main.Store.GetAttachment("page-a")!), pageB = main.Store.AttachmentPath(main.Store.GetAttachment("page-b")!);
        Assert.NotEqual(pageA, pageB, StringComparer.OrdinalIgnoreCase);
        Assert.True(main.Store.RemoveAttachment("page-a"));
        Assert.True(File.Exists(pageB));
        Assert.True(main.Store.RemoveAttachment("slides"));
        Assert.Equal("%PDF this folder's own slides", File.ReadAllText(ours));
        Assert.Equal(0, LibraryBringIn.Apply(main.Store, main.Cfg, personal, home).Files);
    }

    /// <summary>One lecture's path is never another's, even before its file is written: a lecture whose file is gone
    /// from the old folder takes a free name, and the old folder's own lecture of the same class, day and title takes
    /// the next. A file at a lecture's old path that's another lecture's comes in as that one, never as this one's words.</summary>
    [Fact]
    public void Two_lectures_never_share_a_path()
    {
        using var dir = new TempDir();
        string home = dir["home"];
        string school = Path.Combine(home, "OneDrive - Example University", "Documents", "Study Stash");
        string personal = Path.Combine(home, "OneDrive", "Documents", "Study Stash");
        string xOld, zOld;
        using (var before = new Library(dir, "main", personal))
        {
            // X: its file gone from the old folder since. Z: another lecture's file sits at its old path now.
            xOld = before.File("x", "Thermodynamics", "Entropy", "X's notes.");
            zOld = before.File("z", "Thermodynamics", "Enthalpy", "Z's notes.");
            // H: its file edited by hand since, front matter and all taken out.
            File.WriteAllText(before.File("h", "Thermodynamics", "Heat pumps", "H's notes."), "Edited by hand.\n");
            // G: the same, and the folder in use has a file of its own with the same words at the same path.
            string g = before.File("g", "Thermodynamics", "Gas laws", "G's notes.");
            File.WriteAllText(g, "The same words.\n");
            Directory.CreateDirectory(Path.Combine(school, "Thermodynamics"));
            File.WriteAllText(Path.Combine(school, Path.GetRelativePath(personal, g)), "The same words.\n");
        }
        string stray = Path.Combine(school, "Thermodynamics", "2026-09-14 Gas laws.md");
        File.Delete(xOld);
        File.Delete(zOld);
        using (var other = new Library(dir, "other", personal))
        {
            // W: the old folder's own unlisted lecture with X's class, day and title, under another file name.
            File.Move(other.File("w", "Thermodynamics", "Entropy", "W's notes."), Path.Combine(personal, "Thermodynamics", "W's copy.md"));
            Assert.Equal(zOld, other.File("v", "Thermodynamics", "Enthalpy", "V's notes."));
        }
        using var main = new Library(dir, "main", school);
        string y = main.File("y", "Thermodynamics", "Entropy", "Y's notes.");

        LibraryBringIn.Apply(main.Store, main.Cfg, personal, home);

        // Every lecture has a file of its own: one path each, each with its own words. X's and Z's were written afresh
        // from their rows; Z didn't take V's words, and V and H came in as themselves (H with the student's edit).
        string[] ids = ["x", "y", "w", "z", "v", "h", "g"];
        string[] paths = [.. ids.Select(id => main.Store.Get(id)!.MdPath!)];
        Assert.Equal(paths.Length, paths.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        foreach (var (id, words) in new[] { ("x", "X's notes."), ("y", "Y's notes."), ("w", "W's notes."), ("z", "Z's notes."), ("v", "V's notes."), ("h", "Edited by hand."), ("g", "The same words.") })
            Assert.Contains(words, File.ReadAllText(main.Store.Get(id)!.MdPath!));
        // G's words matched a file the folder in use had, but that file isn't proven G's: G's copy is its own.
        Assert.DoesNotContain(stray, paths, StringComparer.OrdinalIgnoreCase);
        Assert.Equal("The same words.\n", File.ReadAllText(stray));
        // A new lecture of X's class, day and title never lands on another's file; nor does a trashed one put back
        // after a new lecture took its path.
        string n = main.File("n", "Thermodynamics", "Entropy", "N's notes.");
        Assert.DoesNotContain(n, paths, StringComparer.OrdinalIgnoreCase);
        Assert.True(main.Store.Trash("n"));
        string n2 = main.File("n2", "Thermodynamics", "Entropy", "N2's notes.");
        Assert.True(main.Store.Restore("n"));
        Assert.NotEqual(n2, main.Store.Get("n")!.MdPath, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("N2's notes.", File.ReadAllText(n2));
        // Deleting X and Z takes none of the others' files.
        Assert.True(main.Store.Trash("x"));
        Assert.True(main.Store.Trash("z"));
        foreach (string id in new[] { "y", "w", "v", "h", "g", "n", "n2" }) Assert.True(File.Exists(main.Store.Get(id)!.MdPath));
    }
}
