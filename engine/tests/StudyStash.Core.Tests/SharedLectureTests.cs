using System.IO.Compression;
using System.Text;

namespace StudyStash.Core.Tests;

/// <summary>A lecture a friend shared as a .syft file: a good one comes in once, its notes and transcript as they were,
/// and a second time says it's there already; a file with anything else in it (a path out of the folder) is refused.</summary>
public sealed class SharedLectureTests
{
    static byte[] Zip(params (string Name, string Text)[] entries)
    {
        var bytes = new MemoryStream();
        using (var zip = new ZipArchive(bytes, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var (name, text) in entries)
                using (var w = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false)))
                    w.Write(text);
        return bytes.ToArray();
    }

    const string Manifest = """{"format": 1, "title": "Lecture 4", "class": "Cell Biology", "date": "2026-11-03T09:30:00-05:00", "app": "0.24.0"}""";
    const string Svg = """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 10 10"><title>Cell</title><circle cx="5" cy="5" r="4"/></svg>""";
    const string NotesFile = """
        ---
        title: "Lecture 4"
        class: "Cell Biology"
        source: Syft Notes
        ---

        # Lecture 4

        *Cell Biology · Tuesday 3 November 2026 · 52 min*

        ## Summary

        The membrane keeps the cell's inside apart.

        ![Diagram: Cell](notes.assets/diagram-1.svg)
        """;

    [Fact]
    public void A_good_file_comes_in_once_and_one_reaching_outside_is_refused()
    {
        using var dir = new TempDir();
        using var store = new Store(Path.Combine(dir["home"], "state.db"), dir["pool"]);
        var file = Zip(("manifest.json", Manifest), ("notes.md", NotesFile), ("notes.assets/diagram-1.svg", Svg),
            ("transcript.txt", "[00:05] Today, the cell membrane.\n[00:40] It is a lipid bilayer."));

        var lecture = SyftFile.Read(new MemoryStream(file));
        Assert.Equal("Lecture 4", lecture.Title);
        Assert.Equal("Cell Biology", lecture.ClassName);
        // The notes as the library keeps them: the download's header gone, the drawing its SVG again.
        Assert.StartsWith("## Summary", lecture.Notes);
        Assert.Contains("```svg\n" + Svg + "\n```", lecture.Notes);
        string? id = lecture.Add(store, new Classification("Cell Biology", 1.0, "human"));
        Assert.NotNull(id);
        var row = store.Get(id!)!;
        Assert.Equal("Cell Biology", row.ClassName);
        Assert.Equal(lecture.Notes, row.SummaryMd);
        Assert.Contains("lipid bilayer", store.Meeting(row).Transcript);

        // The same file again: it's there already, and nothing changes.
        Assert.Null(SyftFile.Read(new MemoryStream(file)).Add(store, new Classification("Unsorted", 1.0, "human")));
        Assert.Equal("Cell Biology", store.Get(id!)!.ClassName);

        // A zip-slip entry, or anything a shared lecture never holds, is refused before anything is read from it.
        foreach (string bad in (string[])["../../.bashrc", "/etc/passwd", "notes.assets/../../x.svg", "notes.assets\\diagram-1.svg", "run.sh"])
        {
            var refused = Assert.Throws<SyftFileException>(() => SyftFile.Read(new MemoryStream(Zip(("manifest.json", Manifest), ("notes.md", NotesFile), (bad, "x")))));
            Assert.Equal(SyftFile.Unexpected, refused.Message);
        }
        Assert.Equal(SyftFile.NotOne, Assert.Throws<SyftFileException>(() => SyftFile.Read(new MemoryStream("not a zip"u8.ToArray()))).Message);
        Assert.Equal(SyftFile.Newer, Assert.Throws<SyftFileException>(() =>
            SyftFile.Read(new MemoryStream(Zip(("manifest.json", Manifest.Replace("\"format\": 1", "\"format\": 2")), ("notes.md", NotesFile))))).Message);
    }

    /// <summary>What Send to a Syft Notes user writes is what opening it reads: the title, class, day, notes (a drawing
    /// as a drawing again) and transcript, and nothing of who sent it.</summary>
    [Fact]
    public void A_written_file_reads_back()
    {
        const string notes = "## Summary\n\nThe membrane keeps the cell's inside apart.\n\n- lipids\n- proteins\n\n```svg\n" + Svg + "\n```\n\n## Proteins\n\nChannels and pumps.";
        var lecture = new System.Text.Json.Nodes.JsonObject
        {
            ["id"] = "rec-20261103-093000-abc123", ["owner"] = "Sender's MacBook", ["title"] = "Lecture 4", ["class"] = "Cell Biology",
            ["date"] = "2026-11-03T09:30:00-05:00", ["seconds"] = 3120, ["notes"] = notes,
            ["transcript"] = "[00:05] Today, the cell membrane.\n[00:40] It is a lipid bilayer.",
        };
        var file = new MemoryStream();
        SyftFile.Write(file, lecture, "0.24.0");

        file.Position = 0;
        using (var zip = new ZipArchive(file, ZipArchiveMode.Read, leaveOpen: true))
        {
            Assert.Equal(["manifest.json", "notes.md", "notes.assets/diagram-1.svg", "transcript.txt"], zip.Entries.Select(e => e.FullName));
            string all = string.Concat(zip.Entries.Select(e => new StreamReader(e.Open()).ReadToEnd()));
            Assert.DoesNotContain("rec-20261103", all);
            Assert.DoesNotContain("MacBook", all);
        }
        file.Position = 0;
        var back = SyftFile.Read(file);
        Assert.Equal(("Lecture 4", "Cell Biology", "2026-11-03T09:30:00-05:00"), (back.Title, back.ClassName, back.Date));
        Assert.Equal(lecture["transcript"]!.GetValue<string>(), back.Transcript);
        Assert.Equal(notes.Replace(Svg, SafeSvgOf(Svg)), back.Notes);
        Assert.Equal("Cell Biology - Lecture 4.syft", SyftFile.SaveName("Cell Biology", "Lecture 4"));
    }

    static string SafeSvgOf(string svg) => Rich.SafeSvg.Clean(svg).Svg!;
}
