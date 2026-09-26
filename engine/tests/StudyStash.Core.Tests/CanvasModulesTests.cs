using StudyStash.Core.Canvas;

namespace StudyStash.Core.Tests;

/// <summary>Modules show every kind of item the design draws (a saved file, a page, a linked assignment, a Box or
/// Drive link, a locked item), a module renamed or reordered moves its folder instead of duplicating it, and the
/// course's Files area mirrors what Canvas lets the student see.</summary>
public class CanvasModulesTests
{
    const string AssignmentsPath = "/api/v1/courses/4201/assignments";
    const string ModulesPath = "/api/v1/courses/4201/modules";
    const string FoldersPath = "/api/v1/courses/4201/folders";
    const string FilesPath = "/api/v1/courses/4201/files";

    const string OneAssignment = """
        [{"id": 9001, "name": "HW1", "course_id": 4201, "due_at": null, "points_possible": 10.0, "grading_type": "points",
          "submission_types": ["online_upload"], "allowed_attempts": -1, "html_url": "https://canvas.test/courses/4201/assignments/9001",
          "description": ""}]
        """;

    const string EightItems = """
        [{"id": 401, "name": "Week 1", "position": 1, "unlock_at": null, "items_count": 9, "state": "started",
          "items": [
            {"id": 1, "module_id": 401, "position": 1, "title": "Slides.pdf", "indent": 0, "type": "File",
             "content_id": 901, "html_url": "https://canvas.test/courses/4201/modules/items/1",
             "url": "https://canvas.test/api/v1/courses/4201/files/901"},
            {"id": 2, "module_id": 401, "position": 2, "title": "Overview", "indent": 0, "type": "Page",
             "page_url": "overview", "html_url": "https://canvas.test/courses/4201/modules/items/2",
             "url": "https://canvas.test/api/v1/courses/4201/pages/overview"},
            {"id": 3, "module_id": 401, "position": 3, "title": "HW1", "indent": 0, "type": "Assignment",
             "content_id": 9001, "html_url": "https://canvas.test/courses/4201/assignments/9001"},
            {"id": 4, "module_id": 401, "position": 4, "title": "Quiz A", "indent": 0, "type": "Quiz",
             "content_id": 9002, "html_url": "https://canvas.test/courses/4201/quizzes/9002"},
            {"id": 5, "module_id": 401, "position": 5, "title": "Discuss X", "indent": 0, "type": "Discussion",
             "content_id": 9003, "html_url": "https://canvas.test/courses/4201/discussion_topics/9003"},
            {"id": 6, "module_id": 401, "position": 6, "title": "Tracing worksheet", "indent": 0, "type": "ExternalUrl",
             "external_url": "https://app.box.com/s/xyz", "html_url": "https://canvas.test/courses/4201/modules/items/6"},
            {"id": 7, "module_id": 401, "position": 7, "title": "External tool", "indent": 0, "type": "ExternalTool",
             "external_url": "https://tool.example.com/launch", "html_url": "https://canvas.test/courses/4201/modules/items/7"},
            {"id": 8, "module_id": 401, "position": 8, "title": "Section header", "indent": 0, "type": "SubHeader", "html_url": ""},
            {"id": 9, "module_id": 401, "position": 9, "title": "Locked.pdf", "indent": 0, "type": "File",
             "content_id": 902, "html_url": "https://canvas.test/courses/4201/modules/items/9",
             "url": "https://canvas.test/api/v1/courses/4201/files/902", "content_details": {"locked_for_user": true}}
          ]}]
        """;

    const string File901 = """
        {"id": 901, "display_name": "Slides.pdf", "filename": "Slides.pdf", "content-type": "application/pdf",
         "url": "https://canvas.test/files/901/download?verifier=v901", "size": 2048, "updated_at": "2025-09-19T16:00:00Z", "locked_for_user": false}
        """;

    const string PageOverview = """
        {"title": "Overview", "body": "<p>Hi</p>", "html_url": "https://canvas.test/courses/4201/pages/overview",
         "updated_at": "2025-09-19T16:00:00Z", "locked_for_user": false}
        """;

    [Fact]
    public void A_module_with_every_kind_of_item_builds_a_rich_outline()
    {
        using var dir = new TempDir();
        var sync = FakeCanvas.Library(dir, () => FakeCanvas.DesignNow);
        var canvas = new FakeCanvas()
            .Json(AssignmentsPath, OneAssignment)
            .Json(ModulesPath, EightItems)
            .Json("/api/v1/courses/4201/files/901", File901)
            .Json("/api/v1/courses/4201/pages/overview", PageOverview)
            .Bytes("/files/901/download", "%PDF-1.4 slides"u8.ToArray());
        Assert.True(canvas.Run(sync));

        var items = CourseIndex.Load(dir.Path, "CS 101")!.Modules.Single().Items.ToDictionary(i => i.Title);
        Assert.Equal("file", items["Slides.pdf"].Kind);
        Assert.Equal("PDF", items["Slides.pdf"].Format);
        Assert.Equal("Canvas/modules/01 Week 1/Slides.pdf", items["Slides.pdf"].Local);
        Assert.Equal("page", items["Overview"].Kind);
        Assert.Equal("Canvas/modules/01 Week 1/Overview.md", items["Overview"].Local);
        Assert.Equal("assignment", items["HW1"].Kind);
        Assert.Equal(9001L, items["HW1"].AssignmentId);
        Assert.Equal("quiz", items["Quiz A"].Kind);
        Assert.Equal(9002L, items["Quiz A"].AssignmentId);
        Assert.Equal("discussion", items["Discuss X"].Kind);
        Assert.Equal(9003L, items["Discuss X"].AssignmentId);
        Assert.Equal("link", items["Tracing worksheet"].Kind);
        Assert.Equal("box", items["Tracing worksheet"].Source);
        Assert.False(items["Tracing worksheet"].Saved);
        Assert.Equal("tool", items["External tool"].Kind);
        Assert.Equal("header", items["Section header"].Kind);
        var locked = items["Locked.pdf"];
        Assert.Equal("file", locked.Kind);
        Assert.True(locked.Locked);
        Assert.Equal("locked", locked.Skipped);
        Assert.Null(locked.Local);
        Assert.DoesNotContain(canvas.Requested, u => u.Contains("/files/902", StringComparison.Ordinal));

        string modules = File.ReadAllText(Path.Combine(FakeCanvas.CanvasRoot(dir), "modules.md"));
        Assert.Contains("[Slides.pdf](modules/01%20Week%201/Slides.pdf) (PDF, 2 KB)", modules);
        Assert.Contains("[HW1](assignments/HW1/spec.md)", modules);
        Assert.Contains("[Tracing worksheet](https://app.box.com/s/xyz) (link)", modules);
        Assert.Contains("Locked.pdf (not saved: locked", modules);
    }

    [Fact]
    public void A_modules_items_url_is_fetched_when_items_arent_inline()
    {
        using var dir = new TempDir();
        var sync = FakeCanvas.Library(dir, () => FakeCanvas.DesignNow);
        const string itemsPath = "/api/v1/courses/4201/modules/501/items";
        const string modulesJson = """
            [{"id": 501, "name": "Week 2", "position": 2, "items_count": 2, "state": "started",
              "items_url": "https://canvas.test/api/v1/courses/4201/modules/501/items"}]
            """;
        const string page1 = """
            [{"id": 21, "module_id": 501, "position": 1, "title": "Doc.pdf", "indent": 0, "type": "File",
              "content_id": 910, "html_url": "https://canvas.test/x", "url": "https://canvas.test/api/v1/courses/4201/files/910"}]
            """;
        const string page2 = """
            [{"id": 22, "module_id": 501, "position": 2, "title": "Second page", "indent": 0, "type": "Page",
              "page_url": "p2", "html_url": "https://canvas.test/x", "url": "https://canvas.test/api/v1/courses/4201/pages/p2"}]
            """;
        const string file910 = """
            {"id": 910, "display_name": "Doc.pdf", "filename": "Doc.pdf", "content-type": "application/pdf",
             "url": "https://canvas.test/files/910/download", "size": 100, "updated_at": "2025-09-19T16:00:00Z"}
            """;
        const string pageP2 = """
            {"title": "Second page", "body": "<p>hi</p>", "html_url": "https://canvas.test/x", "updated_at": "2025-09-19T16:00:00Z"}
            """;
        var canvas = new FakeCanvas()
            .Json(ModulesPath, modulesJson)
            .Pages(itemsPath, page1, page2)
            .Json("/api/v1/courses/4201/files/910", file910)
            .Json("/api/v1/courses/4201/pages/p2", pageP2)
            .Bytes("/files/910/download", "%PDF-1.4 doc"u8.ToArray());
        Assert.True(canvas.Run(sync));
        Assert.Contains(canvas.Requested, u => u.Contains("/modules/501/items", StringComparison.Ordinal) && u.Contains("include[]=content_details", StringComparison.Ordinal));

        var mod = CourseIndex.Load(dir.Path, "CS 101")!.Modules.Single();
        Assert.Equal(2, mod.Items.Count);
        Assert.Equal("Doc.pdf", mod.Items[0].Title);
        Assert.Equal("Canvas/modules/02 Week 2/Doc.pdf", mod.Items[0].Local);
        Assert.Equal("Second page", mod.Items[1].Title);
        Assert.Equal("Canvas/modules/02 Week 2/Second page.md", mod.Items[1].Local);
    }

    [Fact]
    public void A_renamed_and_reordered_module_moves_its_folder_without_duplicating_it()
    {
        using var dir = new TempDir();
        var sync = FakeCanvas.Library(dir, () => FakeCanvas.DesignNow);
        const string file920 = """
            {"id": 920, "display_name": "Notes.pdf", "filename": "Notes.pdf", "content-type": "application/pdf",
             "url": "https://canvas.test/files/920/download", "size": 10, "updated_at": "2025-09-19T16:00:00Z"}
            """;
        static string ModulesJson(int position, string name) => $$"""
            [{"id": 601, "name": "{{name}}", "position": {{position}}, "items_count": 1, "state": "started",
              "items": [{"id": 61, "module_id": 601, "position": 1, "title": "Notes.pdf", "indent": 0, "type": "File",
                         "content_id": 920, "html_url": "https://canvas.test/x", "url": "https://canvas.test/api/v1/courses/4201/files/920"}]}]
            """;
        var canvas = new FakeCanvas()
            .Json(ModulesPath, ModulesJson(1, "Week 1"))
            .Json("/api/v1/courses/4201/files/920", file920)
            .Bytes("/files/920/download", "%PDF-1.4 notes"u8.ToArray());
        Assert.True(canvas.Run(sync));
        string root = FakeCanvas.CanvasRoot(dir);
        string oldDir = Path.Combine(root, "modules", "01 Week 1");
        Assert.True(File.Exists(Path.Combine(oldDir, "Notes.pdf")));

        canvas.Json(ModulesPath, ModulesJson(2, "Week One"));
        Assert.True(canvas.Run(sync));
        string newDir = Path.Combine(root, "modules", "02 Week One");
        Assert.False(Directory.Exists(oldDir));
        Assert.True(File.Exists(Path.Combine(newDir, "Notes.pdf")));
        Assert.Single(Directory.GetDirectories(Path.Combine(root, "modules")));
        // The file's bytes weren't asked for again: the manifest followed the move.
        Assert.Equal(1, canvas.Requested.Count(u => u.Contains("/files/920/download", StringComparison.Ordinal)));
    }

    [Fact]
    public void An_external_link_shows_saved_once_a_file_lands_beside_it()
    {
        using var dir = new TempDir();
        var sync = FakeCanvas.Library(dir, () => FakeCanvas.DesignNow);
        const string modulesJson = """
            [{"id": 701, "name": "Week 5", "position": 5, "items_count": 1, "state": "started",
              "items": [{"id": 71, "module_id": 701, "position": 1, "title": "Tracing worksheet", "indent": 0,
                         "type": "ExternalUrl", "external_url": "https://app.box.com/s/abc", "html_url": "https://canvas.test/x"}]}]
            """;
        var canvas = new FakeCanvas().Json(ModulesPath, modulesJson);
        Assert.True(canvas.Run(sync));
        var item = CourseIndex.Load(dir.Path, "CS 101")!.Modules.Single().Items.Single();
        Assert.False(item.Saved);
        Assert.Null(item.Local);

        string folder = Path.Combine(FakeCanvas.CanvasRoot(dir), "modules", "05 Week 5");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "Tracing worksheet.pdf"), "scouted");

        Assert.True(canvas.Run(sync));
        item = CourseIndex.Load(dir.Path, "CS 101")!.Modules.Single().Items.Single();
        Assert.True(item.Saved);
        Assert.Equal("Canvas/modules/05 Week 5/Tracing worksheet.pdf", item.Local);
    }

    [Fact]
    public void The_files_area_mirrors_folders_and_files_over_pages_skipping_whats_hidden()
    {
        using var dir = new TempDir();
        var sync = FakeCanvas.Library(dir, () => FakeCanvas.DesignNow);
        const string foldersPage1 = """[{"id": 1, "full_name": "course files"}]""";
        const string foldersPage2 = """[{"id": 2, "full_name": "course files/Handouts"}]""";
        const string filesPage1 = """
            [{"id": 101, "folder_id": 1, "display_name": "syllabus.pdf", "filename": "syllabus.pdf", "content-type": "application/pdf",
              "url": "https://canvas.test/files/101/download", "size": 10, "updated_at": "2025-09-19T16:00:00Z"}]
            """;
        const string filesPage2 = """
            [{"id": 102, "folder_id": 2, "display_name": "handout1.pdf", "filename": "handout1.pdf", "content-type": "application/pdf",
              "url": "https://canvas.test/files/102/download", "size": 10, "updated_at": "2025-09-19T16:00:00Z"},
             {"id": 103, "folder_id": 2, "display_name": "hidden.pdf", "filename": "hidden.pdf", "content-type": "application/pdf",
              "url": "https://canvas.test/files/103/download", "size": 10, "updated_at": "2025-09-19T16:00:00Z", "hidden_for_user": true}]
            """;
        var canvas = new FakeCanvas()
            .Pages(FoldersPath, foldersPage1, foldersPage2)
            .Pages(FilesPath, filesPage1, filesPage2)
            .Bytes("/files/101/download", "%PDF-1.4 syllabus"u8.ToArray())
            .Bytes("/files/102/download", "%PDF-1.4 handout"u8.ToArray());
        Assert.True(canvas.Run(sync));

        var index = CourseIndex.Load(dir.Path, "CS 101")!;
        Assert.False(index.FilesHidden);
        Assert.Equal(2, index.Files.Count); // the hidden one is dropped
        var syllabus = index.Files.Single(f => f.Name == "syllabus.pdf");
        Assert.Equal("", syllabus.Folder);
        Assert.Equal("Canvas/files/syllabus.pdf", syllabus.Local);
        var handout = index.Files.Single(f => f.Name == "handout1.pdf");
        Assert.Equal("Handouts", handout.Folder);
        Assert.Equal("Canvas/files/Handouts/handout1.pdf", handout.Local);
        Assert.DoesNotContain(index.Files, f => f.Name == "hidden.pdf");
        Assert.Equal("ok", sync.Crawl.Sections["CS 101"]["files"]);
    }

    [Fact]
    public void Files_401_marks_the_area_hidden_without_stopping_the_sync()
    {
        using var dir = new TempDir();
        var sync = FakeCanvas.Library(dir, () => FakeCanvas.DesignNow);
        var canvas = new FakeCanvas().Status(FoldersPath, 401, """{"status":"unauthorized","errors":[{"message":"user not authorized to perform that action"}]}""");
        Assert.True(canvas.Run(sync));
        var index = CourseIndex.Load(dir.Path, "CS 101")!;
        Assert.True(index.FilesHidden);
        Assert.Empty(index.Files);
        Assert.Equal("hidden", sync.Crawl.Sections["CS 101"]["files"]);
        Assert.False(CanvasSettings.Load(dir.Path).NeedsLogin);
    }

    [Fact]
    public void Two_files_sharing_a_name_in_one_folder_keep_separate_copies_stable_across_a_resync()
    {
        using var dir = new TempDir();
        var sync = FakeCanvas.Library(dir, () => FakeCanvas.DesignNow);
        const string foldersJson = """[{"id": 1, "full_name": "course files"}]""";
        const string filesJson = """
            [{"id": 201, "folder_id": 1, "display_name": "notes.pdf", "filename": "notes.pdf", "content-type": "application/pdf",
              "url": "https://canvas.test/files/201/download", "size": 10, "updated_at": "2025-09-19T16:00:00Z"},
             {"id": 202, "folder_id": 1, "display_name": "notes.pdf", "filename": "notes.pdf", "content-type": "application/pdf",
              "url": "https://canvas.test/files/202/download", "size": 10, "updated_at": "2025-09-19T16:00:00Z"}]
            """;
        var canvas = new FakeCanvas()
            .Json(FoldersPath, foldersJson)
            .Json(FilesPath, filesJson)
            .Bytes("/files/201/download", "one"u8.ToArray())
            .Bytes("/files/202/download", "two"u8.ToArray());
        Assert.True(canvas.Run(sync));
        List<string?> Locals() => CourseIndex.Load(dir.Path, "CS 101")!.Files.Select(f => f.Local).OrderBy(s => s, StringComparer.Ordinal).ToList();
        Assert.Equal(["Canvas/files/notes (2).pdf", "Canvas/files/notes.pdf"], Locals());

        Assert.True(canvas.Run(sync));
        Assert.Equal(["Canvas/files/notes (2).pdf", "Canvas/files/notes.pdf"], Locals());
    }

    [Fact]
    public void A_file_too_big_for_the_sync_is_recorded_not_downloaded()
    {
        using var dir = new TempDir();
        var sync = FakeCanvas.Library(dir, () => FakeCanvas.DesignNow);
        const string foldersJson = """[{"id": 1, "full_name": "course files"}]""";
        string filesJson = $$"""
            [{"id": 301, "folder_id": 1, "display_name": "huge.zip", "filename": "huge.zip", "content-type": "application/zip",
              "url": "https://canvas.test/files/301/download", "size": {{60L * 1024 * 1024}}, "updated_at": "2025-09-19T16:00:00Z"}]
            """;
        var canvas = new FakeCanvas().Json(FoldersPath, foldersJson).Json(FilesPath, filesJson);
        Assert.True(canvas.Run(sync));
        var f = CourseIndex.Load(dir.Path, "CS 101")!.Files.Single();
        Assert.Equal("too big", f.Skipped);
        Assert.Null(f.Local);
        Assert.DoesNotContain(canvas.Requested, u => u.Contains("/files/301/download", StringComparison.Ordinal));
    }
}
