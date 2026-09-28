using System.Text;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using StudyStash.App.Controls.Rich;
using StudyStash.App.Services;
using StudyStash.Core;
using StudyStash.Core.Rich;
using StudyStash.Core.Tests;

namespace StudyStash.App.Tests;

/// <summary>Downloading a lecture, or a whole class, as Markdown — against a real library (<see cref="LibraryRig"/>),
/// not hand-written JSON: the files it writes, the pictures beside them, and what it says when a folder or a
/// lecture can't be reached.</summary>
public class NotesDownloadTests
{
    /// <summary>Draws a Mermaid flowchart that parses, in a real (if headless) font, the way the app's Export menu
    /// does; null for one that doesn't.</summary>
    static string? DrawMermaid(string source)
    {
        try
        {
            return DiagramSvg.Render(DiagramLayout.Lay(Mermaid.Parse(source), SceneCache.Measurer(FontFamily.Default)));
        }
        catch (MermaidException)
        {
            return null;
        }
    }

    static async Task<LibraryRig> Bio110WithThreeLecturesAsync()
    {
        var rig = await LibraryRig.StartAsync();
        rig.AddLecture(new Meeting("l1")
        {
            Title = "The cardiac cycle", Date = "2026-09-22", Raw = new System.Text.Json.Nodes.JsonObject { ["seconds"] = 1800.0 },
        }, "BIO 110", "## Details and examples\nCardiac output is $\\text{CO} = \\text{HR} \\times \\text{SV}$.\n\n```mermaid\n" + Summarize.MermaidExample + "\n```\n");
        rig.AddLecture(new Meeting("l2") { Title = "Blood pressure", Date = "2026-09-24", Transcript = "[00:00] Let's talk about blood pressure." },
            "BIO 110", "## Summary\nMAP matters.");
        rig.AddLecture(new Meeting("l3") { Title = "The kidneys", Date = "2026-09-26" }, "BIO 110", "## Summary\nFiltration.");
        return rig;
    }

    [AvaloniaFact]
    public async Task Saving_the_class_writes_every_lecture_and_the_diagrams_picture_beside_it()
    {
        await using var rig = await Bio110WithThreeLecturesAsync();
        var lib = new RemoteLibrary(rig.Url, LibraryRig.Password);
        using var dir = new TempDir();

        await NotesDownload.ClassAsync(() => Task.FromResult<string?>(dir.Path), lib, "BIO 110", transcript: false, DrawMermaid);

        string classDir = Path.Combine(dir.Path, "BIO 110");
        var files = Directory.GetFiles(classDir, "*.md");
        Assert.Equal(3, files.Length);
        Assert.Contains(files, f => f.Contains("cardiac cycle"));
        Assert.Contains(files, f => f.Contains("Blood pressure"));
        Assert.Contains(files, f => f.Contains("kidneys"));
        string cardiac = await File.ReadAllTextAsync(files.Single(f => f.Contains("cardiac cycle")), TestContext.Current.CancellationToken);
        Assert.Contains("$\\text{CO} = \\text{HR} \\times \\text{SV}$", cardiac); // its formula survives too
        var assetDirs = Directory.GetDirectories(classDir, "*.assets");
        var asset = Assert.Single(assetDirs);
        Assert.Single(Directory.GetFiles(asset, "*.svg"));
    }

    [AvaloniaFact]
    public async Task Two_lectures_with_the_same_title_on_one_day_keep_their_own_diagrams_and_each_links_its_own()
    {
        await using var rig = await LibraryRig.StartAsync();
        string Notes(string title) => "## Details\n```svg\n" +
            $"""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 60"><title>{title}</title><rect width="80" height="40"/></svg>""" +
            "\n```\n\n```mermaid\nflowchart TD\n  A[" + title + "] --> B[Next]\n```\n";
        rig.AddLecture(new Meeting("v1") { Title = "Lecture 3", Date = "2026-09-23" }, "BIO 110", Notes("Mitral valve"));
        rig.AddLecture(new Meeting("v2") { Title = "Lecture 3", Date = "2026-09-23" }, "BIO 110", Notes("Aortic valve"));
        var lib = new RemoteLibrary(rig.Url, LibraryRig.Password);
        using var dir = new TempDir();

        await NotesDownload.ClassAsync(() => Task.FromResult<string?>(dir.Path), lib, "BIO 110", transcript: false, DrawMermaid);

        string classDir = Path.Combine(dir.Path, "BIO 110");
        var files = Directory.GetFiles(classDir, "*.md");
        Assert.Equal(2, files.Length);
        Assert.Equal(2, Directory.GetDirectories(classDir, "*.assets").Length);
        var seen = new List<string>();
        foreach (string file in files)
        {
            string md = await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken);
            var link = System.Text.RegularExpressions.Regex.Match(md, @"!\[Diagram: ([^\]]+)\]\(([^)]+)\)");
            Assert.True(link.Success);
            string picture = Path.Combine(classDir, Uri.UnescapeDataString(link.Groups[2].Value));
            Assert.Contains(link.Groups[1].Value, await File.ReadAllTextAsync(picture, TestContext.Current.CancellationToken)); // its own, not the other's
            string folder = Path.GetDirectoryName(picture)!;
            Assert.Equal(Path.GetFileNameWithoutExtension(file) + ".assets", Path.GetFileName(folder));
            Assert.Equal(2, Directory.GetFiles(folder, "*.svg").Length); // its SVG and its flowchart's picture, both there
            Assert.Contains(link.Groups[1].Value, await File.ReadAllTextAsync(Path.Combine(folder, "diagram-2.svg"), TestContext.Current.CancellationToken));
            seen.Add(link.Groups[1].Value);
        }
        Assert.Equal(["Aortic valve", "Mitral valve"], seen.Order());
    }

    /// <summary>The files open as UTF-8 everywhere with no byte-order mark in front of their front matter, and a
    /// formula's backslashes and a note's signs come out byte for byte.</summary>
    [AvaloniaFact]
    public async Task Every_file_is_utf8_with_no_byte_order_mark()
    {
        await using var rig = await Bio110WithThreeLecturesAsync();
        var lib = new RemoteLibrary(rig.Url, LibraryRig.Password);
        using var dir = new TempDir();

        await NotesDownload.ClassAsync(() => Task.FromResult<string?>(dir.Path), lib, "BIO 110", transcript: false, DrawMermaid);

        var files = Directory.GetFiles(Path.Combine(dir.Path, "BIO 110"), "*", SearchOption.AllDirectories);
        Assert.Equal(4, files.Length); // three lectures and one picture
        foreach (string file in files)
        {
            byte[] bytes = await File.ReadAllBytesAsync(file, TestContext.Current.CancellationToken);
            Assert.False(bytes is [0xEF, 0xBB, 0xBF, ..], $"{Path.GetFileName(file)} starts with a byte-order mark");
            Assert.Equal(bytes, new UTF8Encoding(false, true).GetBytes(new UTF8Encoding(false, true).GetString(bytes)));
        }
        string cardiac = await File.ReadAllTextAsync(files.Single(f => f.Contains("cardiac cycle") && f.EndsWith(".md")), TestContext.Current.CancellationToken);
        Assert.StartsWith("---\ntitle: ", cardiac);
        Assert.Contains("$\\text{CO} = \\text{HR} \\times \\text{SV}$", cardiac);
    }

    /// <summary>A class named as Windows won't keep a folder (a dot at its end, a device's name) is saved in a folder
    /// named so it will, and a second download finds it's taken. Listed back from the disk, so on Windows this is
    /// what Windows really made.</summary>
    [AvaloniaFact]
    public async Task A_class_windows_cant_name_a_folder_after_gets_one_it_keeps_exactly()
    {
        await using var rig = await LibraryRig.StartAsync();
        var lib = new RemoteLibrary(rig.Url, LibraryRig.Password);
        using var dir = new TempDir();

        foreach (string cls in new[] { "Chem.", "CON", "Lab: week 3?", "Chem." })
            await NotesDownload.ClassAsync(() => Task.FromResult<string?>(dir.Path), lib, cls, transcript: false, DrawMermaid);

        var made = Directory.GetDirectories(dir.Path).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToList();
        Assert.Equal(["CON_", "Chem", "Lab week 3"], made); // the second "Chem." found its (empty) folder and reused it
    }

    /// <summary>A folder far deeper than Windows' old 260-character limit on a path still takes a whole class, its
    /// pictures too.</summary>
    [AvaloniaFact]
    public async Task A_class_saves_into_a_folder_past_windows_old_path_limit()
    {
        await using var rig = await Bio110WithThreeLecturesAsync();
        var lib = new RemoteLibrary(rig.Url, LibraryRig.Password);
        using var dir = new TempDir();
        string deep = dir.Path;
        for (int i = 0; deep.Length < 300; i++) deep = Path.Combine(deep, $"Semester notes, part {i} of the long folder");
        Directory.CreateDirectory(deep);

        await NotesDownload.ClassAsync(() => Task.FromResult<string?>(deep), lib, "BIO 110", transcript: false, DrawMermaid);

        var files = Directory.GetFiles(Path.Combine(deep, "BIO 110"), "*", SearchOption.AllDirectories);
        Assert.Equal(4, files.Length);
        Assert.All(files, f => Assert.True(f.Length > 300));
    }

    /// <summary>Saved over a longer file, through a stream that doesn't start it empty, the file holds the new text
    /// alone, as UTF-8 with no byte-order mark.</summary>
    [Fact]
    public async Task Saving_over_a_longer_file_leaves_nothing_of_the_old_one()
    {
        using var dir = new TempDir();
        string path = dir["lecture.md"];
        await File.WriteAllTextAsync(path, new string('x', 5000), TestContext.Current.CancellationToken);

        await NotesDownload.WriteTextAsync(() => Task.FromResult<Stream>(new FileStream(path, FileMode.OpenOrCreate, FileAccess.Write)), "---\ntitle: \"Buffers\"\n---\n\nCO₂ → H₂CO₃\n");

        Assert.Equal(Encoding.UTF8.GetBytes("---\ntitle: \"Buffers\"\n---\n\nCO₂ → H₂CO₃\n"), await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
    }

    [AvaloniaFact]
    public async Task Cancelling_the_folder_picker_saves_nothing()
    {
        await using var rig = await Bio110WithThreeLecturesAsync();
        var lib = new RemoteLibrary(rig.Url, LibraryRig.Password);
        using var dir = new TempDir();

        await NotesDownload.ClassAsync(() => Task.FromResult<string?>(null), lib, "BIO 110", transcript: false, DrawMermaid);

        Assert.Empty(Directory.GetFileSystemEntries(dir.Path));
    }

    [AvaloniaFact]
    public async Task A_library_that_cant_be_reached_says_so_and_saves_nothing()
    {
        var lib = new RemoteLibrary("http://127.0.0.1:1", "x");
        using var dir = new TempDir();

        await NotesDownload.ClassAsync(() => Task.FromResult<string?>(dir.Path), lib, "BIO 110", transcript: false, DrawMermaid);

        Assert.Empty(Directory.GetFileSystemEntries(dir.Path));
    }

    [AvaloniaFact]
    public async Task A_second_download_into_the_same_place_gets_its_own_numbered_folder()
    {
        await using var rig = await Bio110WithThreeLecturesAsync();
        var lib = new RemoteLibrary(rig.Url, LibraryRig.Password);
        using var dir = new TempDir();

        await NotesDownload.ClassAsync(() => Task.FromResult<string?>(dir.Path), lib, "BIO 110", transcript: false, DrawMermaid);
        await NotesDownload.ClassAsync(() => Task.FromResult<string?>(dir.Path), lib, "BIO 110", transcript: false, DrawMermaid);

        Assert.True(Directory.Exists(Path.Combine(dir.Path, "BIO 110")));
        Assert.True(Directory.Exists(Path.Combine(dir.Path, "BIO 110 (2)")));
    }

    [Fact]
    public async Task Saving_one_lecture_with_a_faked_picker_path_writes_it_with_and_without_the_transcript()
    {
        await using var rig = await Bio110WithThreeLecturesAsync();
        var lib = new RemoteLibrary(rig.Url, LibraryRig.Password);
        using var dir = new TempDir();
        string withoutPath = dir["without.md"], withPath = dir["with.md"];
        Task<(Func<string, Task>, string?)?> PickAt(string path) =>
            Task.FromResult<(Func<string, Task>, string?)?>((text => File.WriteAllTextAsync(path, text), path));

        await NotesDownload.LectureAsync(() => PickAt(withoutPath), lib, "l2", transcript: false, DrawMermaid);
        await NotesDownload.LectureAsync(() => PickAt(withPath), lib, "l2", transcript: true, DrawMermaid);

        string without = await File.ReadAllTextAsync(withoutPath, TestContext.Current.CancellationToken);
        string with = await File.ReadAllTextAsync(withPath, TestContext.Current.CancellationToken);
        Assert.DoesNotContain("## Transcript", without);
        Assert.Contains("## Transcript", with);
    }

    [Fact]
    public async Task Cancelling_the_save_dialog_downloads_nothing()
    {
        await using var rig = await Bio110WithThreeLecturesAsync();
        var lib = new RemoteLibrary(rig.Url, LibraryRig.Password);
        bool asked = false;

        await NotesDownload.LectureAsync(() =>
        {
            asked = true;
            return Task.FromResult<(Func<string, Task>, string?)?>(null);
        }, lib, "l1", transcript: false, DrawMermaid);

        Assert.True(asked); // the dialog really was shown; it was cancelled, nothing written
    }

    [Fact]
    public async Task An_unreachable_library_saves_nothing_for_one_lecture_either()
    {
        var lib = new RemoteLibrary("http://127.0.0.1:1", "x");
        using var dir = new TempDir();
        string path = dir["lecture.md"];

        await NotesDownload.LectureAsync(() => Task.FromResult<(Func<string, Task>, string?)?>((text => File.WriteAllTextAsync(path, text), path)),
            lib, "l1", transcript: false, DrawMermaid);

        Assert.False(File.Exists(path));
    }
}
