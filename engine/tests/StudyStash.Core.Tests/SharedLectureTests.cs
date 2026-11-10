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

    /// <summary>A zip whose entries all point at the same packed bytes: 1,000 empty deflate blocks, then one holding
    /// <paramref name="text"/>. Each entry unpacks to the text, and together they say they hold more than the file.</summary>
    static byte[] SharingBytes(string text, params string[] names)
    {
        const int Empty = 1000;
        byte[] plain = Encoding.UTF8.GetBytes(text), empty = [0x00, 0x00, 0x00, 0xFF, 0xFF]; // a block, not the last, of no bytes
        uint crc = new ZipArchive(new MemoryStream(Zip(("x", text)))).Entries[0].Crc32, packed = Empty * 5 + 5 + (uint)plain.Length;
        var file = new MemoryStream();
        var w = new BinaryWriter(file);
        void Header(bool central, string name)
        {
            w.Write(central ? 0x02014B50u : 0x04034B50u);
            if (central) w.Write((ushort)20);
            w.Write((ushort)20); w.Write((ushort)0); w.Write((ushort)8); w.Write(0u); // version, flags, deflate, time and day
            w.Write(crc); w.Write(packed); w.Write((uint)plain.Length);
            w.Write((ushort)name.Length); w.Write((ushort)0);
            if (central) { w.Write((ushort)0); w.Write((ushort)0); w.Write((ushort)0); w.Write(0u); w.Write(0u); } // its header at 0
            w.Write(Encoding.ASCII.GetBytes(name));
        }
        Header(central: false, names[0]);
        for (int b = 0; b < Empty; b++) w.Write(empty);
        w.Write((byte)1); w.Write((ushort)plain.Length); w.Write((ushort)~plain.Length); w.Write(plain); // the last, stored
        long directory = file.Position;
        foreach (string name in names) Header(central: true, name);
        long end = file.Position;
        w.Write(0x06054B50u); w.Write(0u); w.Write((ushort)names.Length); w.Write((ushort)names.Length);
        w.Write((uint)(end - directory)); w.Write((uint)directory); w.Write((ushort)0);
        return file.ToArray();
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

    /// <summary>A small file made to tie up the computer, or to crash it, is refused in plain words before it costs
    /// anything.</summary>
    [Fact]
    public void A_file_made_to_tie_up_the_computer_is_refused()
    {
        // Entries sharing their packed bytes (200 sharing 16 MB would each unpack all of it) are a damaged file, even when
        // each one unpacks to a lecture.
        Assert.Equal(SyftFile.NotOne, Assert.Throws<SyftFileException>(() => SyftFile.Read(new MemoryStream(
            SharingBytes("""{"format": 1, "title": "x"}""", "manifest.json", "notes.md", "transcript.txt")))).Message);

        // 1,000 spaces before one link to a picture of 2,000 short lines: 2 million characters, refused before they're made.
        Assert.Null(Rich.NoteExport.Undrawn(new string(' ', 1000) + "![Diagram: x](a.svg)",
            new Dictionary<string, string> { ["a.svg"] = string.Concat(Enumerable.Repeat("a\n", 2000)) }, SharedLecture.MaxNotesChars));

        // Half a surrogate pair in a key or the title is a damaged file; in what nothing reads, it opens as before.
        foreach (string bad in (string[])[Manifest.Replace("\"title\"", "\"\\uD800\": 1, \"title\""), Manifest.Replace("Lecture 4", "\\uD800")])
            Assert.Equal(SyftFile.NotOne, Assert.Throws<SyftFileException>(() => SyftFile.Read(new MemoryStream(Zip(("manifest.json", bad),
                ("notes.md", NotesFile))))).Message);
        Assert.Equal("Lecture 4", SyftFile.Read(new MemoryStream(Zip(("manifest.json", Manifest.Replace("0.24.0", "\\uD800")), ("notes.md", NotesFile)))).Title);
    }

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
        Assert.Equal(SyftFile.NotOne, Assert.Throws<SyftFileException>(() =>
            SyftFile.Read(new MemoryStream(Zip(("manifest.json", """{"format": 1, "format": 1, "title": "x"}"""), ("notes.md", NotesFile))))).Message);
        // A tiny file with one big picture linked over and over can't grow into a huge one: refused, and quickly.
        string big = "<svg xmlns=\"http://www.w3.org/2000/svg\">" + new string(' ', 150_000) + "</svg>";
        string links = string.Concat(Enumerable.Repeat("![Diagram: x](notes.assets/diagram-1.svg)\n", 1500));
        var started = System.Diagnostics.Stopwatch.StartNew();
        Assert.Equal(SyftFile.TooBig, Assert.Throws<SyftFileException>(() => SyftFile.Read(new MemoryStream(Zip(("manifest.json", Manifest),
            ("notes.md", "# Lecture 4\n\n" + links), ("notes.assets/diagram-1.svg", big))))).Message);
        Assert.True(started.Elapsed < TimeSpan.FromSeconds(2), $"took {started.Elapsed}");
        // A line break in the title can't start a heading in the note file.
        Assert.Equal("Lecture 4 # Owned", SyftFile.Read(new MemoryStream(Zip(("manifest.json", Manifest.Replace("Lecture 4", "Lecture 4\\n# Owned")),
            ("notes.md", NotesFile)))).Title);
        Assert.Equal(SyftFile.Newer, Assert.Throws<SyftFileException>(() =>
            SyftFile.Read(new MemoryStream(Zip(("manifest.json", Manifest.Replace("\"format\": 1", "\"format\": 2")), ("notes.md", NotesFile))))).Message);
    }
}
