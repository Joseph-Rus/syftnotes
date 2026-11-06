using System.Net.Http.Json;
using System.Text.Json.Nodes;
using StudyStash.Core;
using StudyStash.Core.Ai;
using StudyStash.Core.Canvas;
using StudyStash.Core.Filing;
using StudyStash.Library;

namespace StudyStash.Core.Tests;

/// <summary>Submit &amp; Organize, part 1: after a hand-in on Canvas, the file handed in is found in the approved folders
/// (and nowhere else), filed into its class's folder with the files worked from, and every move can be undone. Temp
/// folders only, and a pretend Trash.</summary>
public class SubmitOrganizeTests
{
    /// <summary>A Trash of the test's own: files go into a folder, and come back from it.</summary>
    sealed class FakeTrash(string bin, bool here = true) : ITrash
    {
        public List<string> Sent { get; } = [];

        public string? Send(string path)
        {
            if (!here) return null;
            Directory.CreateDirectory(bin);
            string to = Path.Combine(bin, Guid.NewGuid().ToString("N") + Path.GetExtension(path));
            File.Move(path, to);
            Sent.Add(path);
            return to;
        }

        public bool Restore(string trashed, string to)
        {
            if (!File.Exists(trashed)) return false;
            File.Move(trashed, to, overwrite: false);
            return true;
        }
    }

    static HandedIn Work(params FileRef[] professors) =>
        new("CS 101", new AssignmentInfo { Id = 3, Name = "Lab 3: Recursion traces", UnlockAt = "2026-09-20T00:00:00Z", InstructionFiles = [.. professors] },
            1, "2026-10-01T10:00:00Z", []);

    static string Write(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }

    static string Sha(string path) => LibraryBringIn.Sha(path)!;

    [Fact]
    public async Task The_file_handed_in_is_found_by_name_size_and_bytes_in_the_approved_folders_only()
    {
        using var dir = new TempDir();
        string documents = dir["Documents"], library = Path.Combine(documents, "Syft Notes"), desktop = dir["Desktop"];
        byte[] mine = "my lab report"u8.ToArray();
        // The browser's second download of it in an approved folder; the same bytes in the library, outside the approved
        // folders, and through a link out of one.
        Directory.CreateDirectory(Path.Combine(documents, "School"));
        File.WriteAllBytes(Path.Combine(documents, "School", "LAB3 (1).pdf"), mine);
        Directory.CreateDirectory(desktop);
        File.WriteAllBytes(Path.Combine(desktop, "lab3.pdf"), mine);
        string saved = Path.Combine(library, "CS 101", "Canvas", "assignments", "Lab 3", "submission", "lab3.pdf");
        Directory.CreateDirectory(Path.GetDirectoryName(saved)!);
        File.WriteAllBytes(saved, mine);
        try
        {
            Directory.CreateSymbolicLink(Path.Combine(documents, "Desktop link"), desktop);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Windows without the right to make links: the rest still holds.
        }
        string[] approved = [documents];
        var file = new FileRef { Id = 7, Name = "lab3.pdf", Size = mine.Length, Url = "https://canvas.example.com/files/7/download" };
        string classDir = Path.Combine(library, "CS 101");
        static Task<byte[]?> Gives(byte[]? bytes) => Task.FromResult(bytes);

        // Canvas's copy, already saved by the sync, has the same bytes: it's this one, " (1)" and all.
        var exact = await SubmitMatch.FindAsync(file, approved, library,
            SubmitMatch.CanvasSha(new FileRef { Name = file.Name, Size = file.Size, Local = "Canvas/assignments/Lab 3/submission/lab3.pdf" }, classDir, null));
        Assert.Equal((Found.Exact, Path.Combine(documents, "School", "LAB3 (1).pdf")), (exact.Kind, exact.Path));
        // Read from Canvas instead, other bytes: not it. Canvas couldn't give it: name and size only, so the student says.
        Assert.Equal(Found.None, (await SubmitMatch.FindAsync(file, approved, library, SubmitMatch.CanvasSha(file, classDir, (_, _) => Gives("another report"u8.ToArray())))).Kind);
        Assert.Equal(Found.Unsure, (await SubmitMatch.FindAsync(file, approved, library, SubmitMatch.CanvasSha(file, classDir, (_, _) => Gives(null)))).Kind);
        // Another size: not it, and Canvas isn't asked.
        Assert.Equal(Found.None, (await SubmitMatch.FindAsync(new FileRef { Id = 7, Name = "lab3.pdf", Size = 99 }, approved, library, _ => throw new InvalidOperationException("asked"))).Kind);
        // Canvas's own "-1" for a file handed in again under its name.
        Assert.True(SubmitMatch.SameName("lab3.pdf", "lab3-1.pdf"));
        Assert.False(SubmitMatch.SameName("lab3.docx", "lab3.pdf"));

        // Nothing outside the approved folders is ever listed: not the library in them, not what a link points at.
        Assert.All(SubmitMatch.FilesIn(approved, library), f =>
        {
            Assert.StartsWith(documents + Path.DirectorySeparatorChar, f);
            Assert.DoesNotContain(library, f);
            Assert.DoesNotContain("Desktop link", f);
        });
        Assert.Single(SubmitMatch.FilesIn(approved, library));
    }

    [Fact]
    public void A_hand_in_is_new_once_and_a_second_attempt_is_new_again_with_its_own_files()
    {
        var one = new AttemptInfo { Attempt = 1, SubmittedAt = "2026-10-01T10:00:00Z", Files = [new FileRef { Id = 1, Name = "lab3.pdf", Size = 10 }] };
        var two = new AttemptInfo { Attempt = 2, SubmittedAt = "2026-10-02T09:00:00Z", Files = [new FileRef { Id = 2, Name = "lab3-1.pdf", Size = 12 }] };
        AssignmentInfo Lab(params AttemptInfo[] tries) => new()
        {
            Id = 3, Name = "Lab 3: Recursion traces",
            Submission = new SubmissionInfo { State = "submitted", Attempt = tries[^1].Attempt, SubmittedAt = tries[^1].SubmittedAt, Files = tries[^1].Files, Attempts = [.. tries] },
        };
        CourseIndex Course(params AssignmentInfo[] work) => new() { Class = "CS 101", Assignments = [.. work] };

        Assert.Empty(HandedIn.Since(null, Course(Lab(one)))); // a class's first sync files nothing old
        var first = Assert.Single(HandedIn.Since(Course(), Course(Lab(one))));
        Assert.Equal((1, "lab3.pdf"), (first.Attempt, first.Files[0].Name));
        Assert.Empty(HandedIn.Since(Course(Lab(one)), Course(Lab(one))));
        var again = Assert.Single(HandedIn.Since(Course(Lab(one)), Course(Lab(one, two))));
        Assert.Equal((2, "lab3-1.pdf", "2026-10-02T09:00:00Z"), (again.Attempt, again.Files[0].Name, again.SubmittedAt));
    }

    [Fact]
    public void Materials_are_the_professors_files_and_the_drafts_named_for_or_changed_during_the_assignment()
    {
        using var dir = new TempDir();
        string documents = dir["Documents"], library = dir["Library"];
        var during = new DateTime(2026, 9, 25, 12, 0, 0, DateTimeKind.Utc);
        var before = new DateTime(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc);
        void At(string path, DateTime when) => File.SetLastWriteTimeUtc(path, when);
        At(Write(Path.Combine(documents, "Lab 3 handout (1).pdf"), "handout"), before);
        At(Write(Path.Combine(documents, "recursion notes.md"), "notes"), during);
        At(Write(Path.Combine(documents, "traces.txt"), "traces"), before);
        At(Write(Path.Combine(documents, "groceries.txt"), "milk"), during);
        At(Write(Path.Combine(documents, "old.txt"), "old"), before);
        string submitted = Write(Path.Combine(documents, "recursion lab3.pdf"), "the report");
        At(Write(Path.Combine(library, "CS 101", "recursion.md"), "in the library"), during);
        At(Write(dir["Desktop/recursion.md"], "not approved"), during);

        var found = SubmitMatch.Materials(Work(new FileRef { Name = "Lab 3 handout.pdf" }), null, [documents], library, [submitted]);
        Assert.Equal(
            new[] { ("Lab 3 handout (1).pdf", Sure.High), ("groceries.txt", Sure.Low), ("recursion notes.md", Sure.High), ("traces.txt", Sure.Medium) },
            found.Select(m => (Path.GetFileName(m.Path), m.Sure)).OrderBy(m => m.Item1, StringComparer.Ordinal));
        Assert.All(found, m => Assert.NotEmpty(m.Why));
    }

    [Fact]
    public async Task Organize_files_without_writing_over_anything_and_undo_puts_every_file_back()
    {
        using var dir = new TempDir();
        string documents = dir["Documents"];
        using var store = new Store(dir["state.db"], dir["Library"]);
        Directory.CreateDirectory(dir["home"]);
        var index = new FileIndex(dir["home"], () => [("Library", store.PoolDir, false)], () => []);
        var work = Work();
        string report = Write(Path.Combine(documents, "lab3.md"), "Tail calls unwind the stack frames");
        string draft = Write(Path.Combine(documents, "Drafts", "recursion draft.md"), "first try");
        string reportSha = Sha(report), draftSha = Sha(draft);
        var filer = new SubmitFiler(store, [documents], new FakeTrash(dir["Trash"]), index);
        // A file of the same name is in Submitted/ already: it's kept, and the one handed in is "lab3 (2).md".
        string folder = filer.FolderFor(work);
        string older = Write(Path.Combine(folder, SubmitFiler.SubmittedFolder, "lab3.md"), "an older copy");

        var run = await filer.OrganizeAsync(work, report, [draft]);
        Assert.Empty(run.Left);
        Assert.Equal(
            new[] { Path.Combine(folder, "Submitted", "lab3 (2).md"), Path.Combine(folder, "Materials", "recursion draft.md") },
            run.Moves.Select(m => m.To));
        Assert.Equal(new[] { reportSha, draftSha }, run.Moves.Select(m => m.Hash));
        Assert.Equal("an older copy", File.ReadAllText(older));
        Assert.False(File.Exists(report) || File.Exists(draft));
        // The work handed in is searchable with the class's other files.
        Assert.Contains(index.Search("unwind"), h => Path.GetFileName(h.Path) == "lab3 (2).md");

        // Something new sits where the draft was: the draft comes back beside it, and the new file is left alone.
        Write(draft, "a new file of the same name");
        var undone = filer.Undo(run.Run);
        Assert.Equal(new[] { UndoOutcome.Beside, UndoOutcome.Back }, undone.Select(u => u.How));
        Assert.Equal(reportSha, Sha(report));
        Assert.Equal(draftSha, Sha(Path.Combine(documents, "Drafts", "recursion draft (2).md")));
        Assert.Equal("a new file of the same name", File.ReadAllText(draft));
        Assert.Empty(filer.Undo(run.Run)); // already back
    }

    [Fact]
    public async Task To_another_disk_a_file_is_copied_and_checked_before_the_original_goes()
    {
        using var dir = new TempDir();
        string documents = dir["Documents"];
        using var store = new Store(dir["state.db"], dir["Library"]);
        string report = Write(Path.Combine(documents, "lab3.md"), "the report");
        string sha = Sha(report);
        var filer = new SubmitFiler(store, [documents], new FakeTrash(dir["Trash"])) { SameDisk = (_, _) => false };

        var moved = Assert.Single((await filer.OrganizeAsync(Work(), report, [])).Moves);
        Assert.Equal((sha, sha, false), (moved.Hash, Sha(moved.To), File.Exists(report)));
        // No half copy left beside it.
        Assert.Equal(new[] { "lab3.md" }, Directory.GetFileSystemEntries(Path.GetDirectoryName(moved.To)!).Select(Path.GetFileName));

        Assert.Equal(UndoOutcome.Back, Assert.Single(filer.Undo(moved.Run)).How);
        Assert.Equal(sha, Sha(report));
        Assert.False(File.Exists(moved.To));
    }

    [Fact]
    public async Task Clean_sends_materials_to_the_trash_and_undo_brings_them_back()
    {
        using var dir = new TempDir();
        string documents = dir["Documents"];
        using var store = new Store(dir["state.db"], dir["Library"]);
        string report = Write(Path.Combine(documents, "lab3.md"), "the report");
        string draft = Write(Path.Combine(documents, "recursion draft.md"), "first try");
        string elsewhere = Write(dir["Desktop/recursion notes.md"], "not approved");
        string sha = Sha(draft);
        var trash = new FakeTrash(dir["Trash"]);
        var filer = new SubmitFiler(store, [documents], trash);

        var run = await filer.CleanAsync(Work(), report, [draft, elsewhere]);
        Assert.Equal(new[] { draft }, trash.Sent);
        Assert.Equal(new[] { FiledMove.Moved, FiledMove.Trashed }, run.Moves.Select(m => m.Kind));
        // A file outside the approved folders isn't touched, even when asked.
        Assert.Equal(elsewhere, Assert.Single(run.Left).Path);
        Assert.True(File.Exists(elsewhere));

        Assert.All(filer.Undo(run.Run), u => Assert.Equal(UndoOutcome.Back, u.How));
        Assert.Equal(sha, Sha(draft));
        Assert.True(File.Exists(report));

        // A disk with no Trash: the material goes in with the assignment's materials instead.
        var noTrash = new SubmitFiler(store, [documents], new FakeTrash(dir["Trash"], here: false));
        var kept = await noTrash.CleanAsync(Work(), null, [draft]);
        Assert.Equal((FiledMove.Moved, Path.Combine(noTrash.FolderFor(Work()), "Materials", "recursion draft.md")), (kept.Moves[0].Kind, kept.Moves[0].To));
    }

    [Fact]
    public async Task A_recycle_bin_that_wouldnt_keep_a_file_leaves_it_for_the_materials_folder()
    {
        using var dir = new TempDir();
        string documents = dir["Documents"];
        using var store = new Store(dir["state.db"], dir["Library"]);
        string draft = Write(Path.Combine(documents, "recursion draft.md"), "first try");
        string sha = Sha(draft);
        var asked = new List<string>();
        string copy = dir["Bin/$RABC123.md"], record = Write(dir["Bin/$IABC123.md"], "where it came from");
        RecycleBin Bin(BinRules? rules, bool policyOff = false) => new()
        {
            Rules = _ => rules,
            SwitchedOff = () => policyOff,
            Delete = p =>
            {
                asked.Add(p);
                File.Move(p, copy);
                return true;
            },
            Find = _ => copy,
        };

        // Set to delete for good, switched off by a school's policy, too small for the file, or its settings unreadable:
        // Windows is never asked, and Clean files the draft with the assignment's materials instead.
        var roomy = new BinRules(DeleteForGood: false, MaxBytes: 1 << 20);
        foreach (var bin in new[] { Bin(new BinRules(DeleteForGood: true, MaxBytes: null)), Bin(roomy, policyOff: true),
                     Bin(new BinRules(DeleteForGood: false, MaxBytes: 4)), Bin(null) })
        {
            var filer = new SubmitFiler(store, [documents], bin);
            var run = await filer.CleanAsync(Work(), null, [draft]);
            var kept = Assert.Single(run.Moves);
            Assert.Equal((FiledMove.Moved, SubmitFiler.MaterialsFolder), (kept.Kind, Path.GetFileName(Path.GetDirectoryName(kept.To))));
            Assert.Equal(UndoOutcome.Back, Assert.Single(filer.Undo(run.Run)).How);
        }
        Assert.Empty(asked);

        // One that keeps it: it goes there, and Undo brings it back and clears the Recycle Bin's record of it.
        var keeps = new SubmitFiler(store, [documents], Bin(roomy));
        var trashed = await keeps.CleanAsync(Work(), null, [draft]);
        Assert.Equal((FiledMove.Trashed, copy), (Assert.Single(trashed.Moves).Kind, trashed.Moves[0].To));
        Assert.Equal(UndoOutcome.Back, Assert.Single(keeps.Undo(trashed.Run)).How);
        Assert.Equal(sha, Sha(draft));
        Assert.False(File.Exists(record));
    }

    [Fact]
    public void A_move_a_crash_cut_short_is_still_undone_from_where_the_file_really_is()
    {
        using var dir = new TempDir();
        string documents = dir["Documents"];
        using var store = new Store(dir["state.db"], dir["Library"]);
        var filer = new SubmitFiler(store, [documents], new FakeTrash(dir["Trash"]));
        string folder = filer.FolderFor(Work());
        string moved = Write(Path.Combine(documents, "lab3.md"), "the report"), stayed = Write(Path.Combine(documents, "draft.md"), "a draft");
        string movedSha = Sha(moved), stayedSha = Sha(stayed);
        // Both were written down as pending; then the app stopped: one had moved, the other hadn't yet.
        foreach (string f in new[] { moved, stayed })
            store.AddFiledMove(new FiledMove(0, "crash", "2026-10-01T10:05:00Z", "CS 101", "Lab 3: Recursion traces", f,
                Path.Combine(folder, "Submitted", Path.GetFileName(f)), FiledMove.Moved, Sha(f)));
        Directory.CreateDirectory(Path.Combine(folder, "Submitted"));
        File.Move(moved, Path.Combine(folder, "Submitted", "lab3.md"));

        Assert.All(filer.Undo("crash"), u => Assert.Equal(UndoOutcome.Back, u.How));
        Assert.Equal((movedSha, stayedSha), (Sha(moved), Sha(stayed)));
        Assert.False(File.Exists(Path.Combine(folder, "Submitted", "lab3.md")));
    }

    /// <summary>Part 2: what Settings may be given, and what "Filed work" lists. A whole disk, the home folder and the
    /// library (and anything in it) are refused with a reason; the runs come newest first, each with its count.</summary>
    [Fact]
    public void A_whole_disk_the_home_folder_and_the_library_are_refused_and_runs_list_newest_first()
    {
        using var dir = new TempDir();
        string home = dir["home"], library = Path.Combine(home, "Documents", "Syft Notes");
        Assert.Equal(FilingFolders.WholeDisk, FilingFolders.Refuse(Path.GetPathRoot(Path.GetFullPath(home))!, home, library));
        Assert.Equal(FilingFolders.WholeHome, FilingFolders.Refuse(home + Path.DirectorySeparatorChar, home, library));
        Assert.Equal(FilingFolders.Library, FilingFolders.Refuse(library, home, library));
        Assert.Equal(FilingFolders.Library, FilingFolders.Refuse(Path.Combine(library, "CS 101"), home, library));
        Assert.Null(FilingFolders.Refuse(Path.Combine(home, "Documents"), home, library));
        Assert.Equal(FilingFolders.Gone, FilingFolders.Look(Path.Combine(home, "Downloads")));
        using var store = new Store(dir["state.db"], dir["Library"]);
        foreach (var (run, file) in new[] { ("older", "a"), ("older", "b"), ("newer", "c") })
            store.AddFiledMove(new FiledMove(0, run, "2026-10-01T10:00:00Z", "CS 101", run, file, "", FiledMove.Moved, ""));
        Assert.Equal([("newer", 1), ("older", 2)], store.FiledRuns().Select(r => (r.Run, r.Files)));

        // A folder that holds the home folder (/Users, C:\Users) is refused, and so is a mounted disk on a Mac.
        Assert.Equal(FilingFolders.HoldsHome, FilingFolders.Refuse(dir.Path, home, library));
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(FilingFolders.WholeDisk, FilingFolders.Refuse("/Volumes/Backup", home, library));
            Assert.Equal(FilingFolders.WholeDisk, FilingFolders.Refuse("/Volumes", home, library));
        }
        // A link to the home folder or the library, or a folder reached through a link, is refused: it would lead outside.
        Directory.CreateDirectory(Path.Combine(library, "CS 101"));
        Directory.CreateDirectory(Path.Combine(home, "Documents", "School"));
        string links = dir["links"], toHome = Path.Combine(links, "home"), toLibrary = Path.Combine(links, "library"), toDocuments = Path.Combine(links, "documents");
        try
        {
            Directory.CreateDirectory(links);
            Directory.CreateSymbolicLink(toHome, home);
            Directory.CreateSymbolicLink(toLibrary, library);
            Directory.CreateSymbolicLink(toDocuments, Path.Combine(home, "Documents"));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return; // Windows without the right to make links: there's nothing to refuse.
        }
        Assert.Equal(FilingFolders.Shortcut, FilingFolders.Refuse(toHome, home, library));
        Assert.Equal(FilingFolders.Shortcut, FilingFolders.Refuse(toLibrary, home, library));
        Assert.Equal(FilingFolders.Shortcut, FilingFolders.Refuse(Path.Combine(toDocuments, "School"), home, library));
        Assert.Null(FilingFolders.Refuse(Path.Combine(home, "Documents", "School"), home, library));

    }

    /// <summary>Part 2's library routes, as the app calls them: tidying up switched on, a folder added (and one refused),
    /// a hand-in checked, filed, listed in Filed work, and undone. A temp library and folder, no Canvas.</summary>
    [Fact]
    public async Task The_app_switches_it_on_adds_a_folder_files_a_hand_in_and_undoes_it()
    {
        using var dir = new TempDir();
        var cfg = new Config(dir.Path, dir["pool"]) { PoolPassword = "pw", Classes = [new ClassDef("CS 101")] };
        using var store = new Store(cfg.DbPath, cfg.PoolDir);
        var sync = FakeCanvas.Library(dir, () => FakeCanvas.DesignNow);
        string downloads = dir["Downloads"], report = Write(Path.Combine(downloads, "lab3.pdf"), "my lab report");
        new CourseIndex
        {
            Class = "CS 101",
            Assignments = [new AssignmentInfo { Id = 3, Name = "Lab 3", Submission = new SubmissionInfo
            {
                State = "submitted", Attempt = 1, SubmittedAt = "2026-10-01T10:00:00Z", Files = [new FileRef { Id = 7, Name = "lab3.pdf", Size = new FileInfo(report).Length }],
            } }],
        }.Save(cfg.Home);
        await using var site = await TestSite.StartAsync(b => LibraryWeb.Build(b, cfg, store, new Pipeline(cfg, store, log: _ => { }), new LibraryWebOptions
        {
            ListModels = _ => Task.FromResult<List<(string, double)>?>(null), Tailscale = () => new TailscaleInfo(),
            Latest = _ => Task.FromResult<Release?>(null), Canvas = sync,
        }));
        site.Client.DefaultRequestHeaders.Authorization = new("Bearer", "pw");
        async Task<JsonObject> Post(string path, object body)
        {
            using var reply = await site.Client.PostAsJsonAsync(path, body);
            return (JsonObject)JsonNode.Parse(await reply.Content.ReadAsStringAsync())!;
        }

        Assert.Equal(FilingFolders.Library, (string?)(await Post("/api/v2/canvas/filing", new { add = cfg.PoolDir }))["detail"]);
        await Post("/api/v2/canvas/filing", new { on = true });
        var filing = await Post("/api/v2/canvas/filing", new { add = downloads });
        Assert.Equal([downloads], filing["folders"]!.AsArray().Select(f => (string)f!));

        string key = "3:1:2026-10-01T10:00:00Z";
        var check = (JsonObject)JsonNode.Parse(await site.Client.GetStringAsync($"/api/v2/canvas/filing/check?class=CS%20101&key={Uri.EscapeDataString(key)}"))!;
        // No copy of Canvas's to compare bytes with here: the name and size agree, so the student is asked.
        Assert.Equal(("unsure", report), ((string)check["file"]!["found"]!, (string)check["file"]!["path"]!));

        var filed = await Post("/api/v2/canvas/filing/file", new { @class = "CS 101", key, file = report, materials = Array.Empty<string>(), clean = false });
        Assert.Equal(1, (int)filed["filed"]!);
        Assert.False(File.Exists(report));
        var runs = ((JsonObject)JsonNode.Parse(await site.Client.GetStringAsync("/api/v2/canvas/filing"))!)["runs"]!.AsArray();
        Assert.Equal(("Lab 3", 1), ((string)runs.Single()!["assignment"]!, (int)runs[0]!["files"]!));

        var undone = await Post("/api/v2/canvas/filing/undo", new { run = (string)filed["run"]! });
        Assert.Equal("back", (string)undone["items"]![0]!["how"]!);
        Assert.True(File.Exists(report));
    }
}
