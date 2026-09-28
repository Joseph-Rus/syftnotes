using System.Text.Json.Nodes;
using StudyStash.Core.Rich;

namespace StudyStash.Core.Tests;

/// <summary>A lecture written out as Markdown that opens well in Obsidian, Typora and VS Code: front matter, its
/// notes with their diagrams drawn beside the file, and (asked for) its transcript.</summary>
public class NoteExportTests
{
    /// <summary>A fixed-width stand-in for the app's real text measurer, so a diagram lays out the same on every
    /// machine that runs this test.</summary>
    static double Measure(string text, double size, bool bold) =>
        new System.Globalization.StringInfo(text).LengthInTextElements * size * (bold ? 0.58 : 0.55);

    /// <summary>Draws a Mermaid flowchart that parses; null for one that doesn't (the same the app would do, with its
    /// own font instead of this fixed-width one).</summary>
    static string? DrawMermaid(string source)
    {
        try
        {
            var scene = DiagramLayout.Lay(Mermaid.Parse(source), Measure);
            return DiagramSvg.Render(scene);
        }
        catch (MermaidException)
        {
            return null;
        }
    }

    static (Config Cfg, Store Store) Library(TempDir dir)
    {
        var cfg = new Config(dir["home"], dir["pool"]) { Classes = [new ClassDef("BIO 110", [])] };
        Directory.CreateDirectory(cfg.Home);
        return (cfg, new Store(cfg.DbPath, cfg.PoolDir));
    }

    static JsonObject LectureJson(Store store, Meeting m, string notes, List<string>? topics = null)
    {
        store.Save(m, new Classification("BIO 110", 0.9, "folder", topics: topics), summaryMd: notes);
        var reader = new LibraryReader(new Config("", ""), store);
        return reader.Lecture(m.Id)!;
    }

    [Fact]
    public void Front_matter_the_heading_meta_and_notes_come_out_as_written_the_transcript_only_when_asked()
    {
        using var dir = new TempDir();
        var (_, store) = Library(dir);
        var m = new Meeting("lec-1")
        {
            Title = "Preload & afterload: what's the difference?", Date = "2026-09-23T09:00:00-07:00",
            Transcript = "[00:00] Let's start with preload.\n[01:30] Now afterload.",
            Raw = new JsonObject { ["seconds"] = 4320.0 },
        };
        var lecture = LectureJson(store, m, "## Summary\nPreload and afterload set how hard the heart works.", ["cardiac output", "MAP"]);

        var file = NoteExport.Lecture(lecture, transcript: false, DrawMermaid);

        Assert.Equal("2026-09-23 Preload & afterload what's the difference.md", file.FileName);
        Assert.Equal("""
            ---
            title: "Preload & afterload: what's the difference?"
            class: "BIO 110"
            date: 2026-09-23
            duration: "1 h 12 min"
            topics: ["cardiac output", "MAP"]
            source: Study Stash
            ---

            # Preload & afterload: what's the difference?

            *BIO 110 · Wednesday 23 September 2026 · 1 h 12 min*

            ## Summary
            Preload and afterload set how hard the heart works.

            """.ReplaceLineEndings("\n"), file.Markdown);
        Assert.Empty(file.Assets);

        var withTranscript = NoteExport.Lecture(lecture, transcript: true, DrawMermaid);
        Assert.EndsWith("\n\n## Transcript\n\n[00:00] Let's start with preload.\n[01:30] Now afterload.\n", withTranscript.Markdown);
    }

    [Fact]
    public void A_lecture_with_no_notes_yet_still_exports()
    {
        using var dir = new TempDir();
        var (_, store) = Library(dir);
        store.Enqueue(new Meeting("lec-2") { Title = "Not written yet", Date = "2026-09-23" });
        var reader = new LibraryReader(new Config("", ""), store);
        var lecture = reader.Lecture("lec-2")!;

        var file = NoteExport.Lecture(lecture, transcript: false, DrawMermaid);

        Assert.Contains("_No notes yet._", file.Markdown);
    }

    [Fact]
    public void A_mermaid_block_that_parses_is_kept_canonical_with_its_picture_saved_beside_it_and_never_linked()
    {
        using var dir = new TempDir();
        var (_, store) = Library(dir);
        var m = new Meeting("lec-3") { Title = "Blood flow", Date = "2026-09-23", Raw = new JsonObject { ["seconds"] = 600.0 } };
        string notes = "## Details and examples\n```mermaid\n" + Summarize.MermaidExample + "\n```\n";
        var lecture = LectureJson(store, m, notes);

        var file = NoteExport.Lecture(lecture, transcript: false, DrawMermaid);

        var chart = Mermaid.Parse(Summarize.MermaidExample);
        Assert.Contains("```mermaid\n" + chart.ToSource().TrimEnd('\n') + "\n```", file.Markdown);
        Assert.DoesNotContain("](2026-09-23 Blood flow.assets", file.Markdown); // no link to the picture
        var asset = Assert.Single(file.Assets);
        Assert.Equal("2026-09-23 Blood flow.assets/diagram-1.svg", asset.RelativePath);
        Assert.Contains("<svg", asset.Text);
    }

    [Fact]
    public void A_diagram_that_wont_parse_is_kept_exactly_as_it_was_written()
    {
        using var dir = new TempDir();
        var (_, store) = Library(dir);
        var m = new Meeting("lec-4") { Title = "Care pathway", Date = "2026-09-23" };
        const string broken = "```mermaid\nflowchart TD\n  A[Assess pain] -->\n```";
        var lecture = LectureJson(store, m, "## Care\n" + broken + "\n");

        var file = NoteExport.Lecture(lecture, transcript: false, DrawMermaid);

        Assert.Contains(broken, file.Markdown);
        Assert.Empty(file.Assets);
    }

    [Fact]
    public void A_safe_svg_becomes_an_image_link_to_its_cleaned_picture_an_unsafe_one_its_reason_in_italics()
    {
        using var dir = new TempDir();
        var (_, store) = Library(dir);
        var m = new Meeting("lec-5") { Title = "Forces", Date = "2026-09-23" };
        // No viewBox and no width/height: SafeSvg can't tell how big to draw it — a Problem, not a cleaned picture.
        const string sizeless = """<svg xmlns="http://www.w3.org/2000/svg"><rect width="10" height="10"/></svg>""";
        string notes = "## Physics\n```svg\n" + Summarize.SvgExample + "\n```\n\nAnd one that can't be shown:\n\n```svg\n" + sizeless + "\n```\n";
        var lecture = LectureJson(store, m, notes);

        var file = NoteExport.Lecture(lecture, transcript: false, DrawMermaid);

        Assert.Contains("![Diagram: Forces on a block on a slope](2026-09-23%20Forces.assets/diagram-1.svg)", file.Markdown);
        Assert.Single(file.Assets);
        Assert.DoesNotContain("<svg", file.Markdown); // the unsafe one's markup never reaches the file
        Assert.Contains("_This drawing has no size", file.Markdown); // its reason, in italics
    }

    [Fact]
    public void A_script_smuggled_into_an_otherwise_drawable_svg_never_reaches_the_saved_picture()
    {
        using var dir = new TempDir();
        var (_, store) = Library(dir);
        var m = new Meeting("lec-5b") { Title = "Sneaky", Date = "2026-09-23" };
        const string sneaky = """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 100"><script>alert(1)</script><rect width="80" height="80"/></svg>
            """;
        var lecture = LectureJson(store, m, "## Shapes\n```svg\n" + sneaky + "\n```\n");

        var file = NoteExport.Lecture(lecture, transcript: false, DrawMermaid);

        var asset = Assert.Single(file.Assets);
        Assert.DoesNotContain("<script", asset.Text);
        Assert.DoesNotContain("alert", asset.Text);
        Assert.Contains("<rect", asset.Text);
    }

    [Fact]
    public void Backslash_parens_and_brackets_become_dollar_signs_outside_code()
    {
        using var dir = new TempDir();
        var (_, store) = Library(dir);
        var m = new Meeting("lec-6") { Title = "Dosing", Date = "2026-09-23" };
        var lecture = LectureJson(store, m, "The dose is \\(V = 10\\ \\text{mL}\\) for this patient.\n\n```text\nkeep \\(this\\) as is\n```\n");

        var file = NoteExport.Lecture(lecture, transcript: false, DrawMermaid);

        Assert.Contains("The dose is $V = 10\\ \\text{mL}$ for this patient.", file.Markdown);
        Assert.Contains("keep \\(this\\) as is", file.Markdown); // untouched inside a fence
    }

    [Theory]
    [InlineData("Lecture 3: Cells / Systems?", "2026-09-23 Lecture 3 Cells Systems.md")]
    public void Windows_illegal_characters_never_reach_the_file_name(string title, string expected)
    {
        using var dir = new TempDir();
        var (_, store) = Library(dir);
        var m = new Meeting("lec-7") { Title = title, Date = "2026-09-23" };
        var lecture = LectureJson(store, m, "## Summary\nA lecture.");

        var file = NoteExport.Lecture(lecture, transcript: false, DrawMermaid);

        Assert.Equal(expected, file.FileName);
        foreach (char c in "\\/:*?\"<>|") Assert.DoesNotContain(c, file.FileName);
    }

    [Fact]
    public void A_200_character_title_is_cut_down_to_a_usable_file_name()
    {
        using var dir = new TempDir();
        var (_, store) = Library(dir);
        var m = new Meeting("lec-8") { Title = new string('A', 200), Date = "2026-09-23" };
        var lecture = LectureJson(store, m, "## Summary\nA lecture.");

        var file = NoteExport.Lecture(lecture, transcript: false, DrawMermaid);

        Assert.True(file.FileName.Length < 200);
    }

    [Fact]
    public void Two_lectures_whose_names_collide_keep_their_diagrams_in_folders_of_their_own_named_after_their_files()
    {
        using var dir = new TempDir();
        var (_, store) = Library(dir);
        // "Valves: part 1" and "Valves part 1" read the same once ":" can't be in a file name; both on one day.
        string Svg(string title) => $"""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 60"><title>{title}</title><rect width="80" height="40"/></svg>""";
        var first = LectureJson(store, new Meeting("lec-9") { Title = "Valves: part 1", Date = "2026-09-23" },
            "## Details\n```svg\n" + Svg("Mitral valve") + "\n```\n");
        var second = LectureJson(store, new Meeting("lec-10") { Title = "Valves part 1", Date = "2026-09-23" },
            "## Details\n```svg\n" + Svg("Aortic valve") + "\n```\n");
        Assert.Equal(NoteExport.FileName(first), NoteExport.FileName(second));

        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var a = NoteExport.Lecture(first, transcript: false, DrawMermaid, NoteExport.UniqueName(NoteExport.FileName(first), taken));
        var b = NoteExport.Lecture(second, transcript: false, DrawMermaid, NoteExport.UniqueName(NoteExport.FileName(second), taken));

        Assert.Equal("2026-09-23 Valves part 1.md", a.FileName);
        Assert.Equal("2026-09-23 Valves part 1 (2).md", b.FileName);
        Assert.Equal("2026-09-23 Valves part 1.assets/diagram-1.svg", Assert.Single(a.Assets).RelativePath);
        Assert.Equal("2026-09-23 Valves part 1 (2).assets/diagram-1.svg", Assert.Single(b.Assets).RelativePath);
        Assert.Contains("](2026-09-23%20Valves%20part%201.assets/diagram-1.svg)", a.Markdown);
        Assert.Contains("](2026-09-23%20Valves%20part%201%20%282%29.assets/diagram-1.svg)", b.Markdown);
        Assert.Contains("Mitral valve", a.Assets[0].Text);
        Assert.Contains("Aortic valve", b.Assets[0].Text);

        // The same lecture exported again under the same name gives the same paths: a re-download lands in place.
        var again = NoteExport.Lecture(second, transcript: false, DrawMermaid, "2026-09-23 Valves part 1 (2).md");
        Assert.Equal(b.Assets[0].RelativePath, again.Assets[0].RelativePath);
    }

    [Fact]
    public void A_lecture_saved_under_a_name_of_the_students_own_keeps_its_diagrams_beside_that_name()
    {
        using var dir = new TempDir();
        var (_, store) = Library(dir);
        var lecture = LectureJson(store, new Meeting("lec-11") { Title = "Forces", Date = "2026-09-23" },
            "## Physics\n```svg\n" + Summarize.SvgExample + "\n```\n");

        var file = NoteExport.Lecture(lecture, transcript: false, DrawMermaid, "Slope forces.md");

        Assert.Equal("Slope forces.md", file.FileName);
        Assert.Equal("Slope forces.assets/diagram-1.svg", Assert.Single(file.Assets).RelativePath);
        Assert.Contains("](Slope%20forces.assets/diagram-1.svg)", file.Markdown);
    }

    [Fact]
    public void UniqueName_gives_two_same_day_same_title_lectures_a_number()
    {
        var taken = new HashSet<string>();
        Assert.Equal("2026-09-23 Lecture 3.md", NoteExport.UniqueName("2026-09-23 Lecture 3.md", taken));
        Assert.Equal("2026-09-23 Lecture 3 (2).md", NoteExport.UniqueName("2026-09-23 Lecture 3.md", taken));
        Assert.Equal("2026-09-23 Lecture 3 (3).md", NoteExport.UniqueName("2026-09-23 Lecture 3.md", taken));
    }
}
