using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using StudyStash.Core.Ai;
using System.Xml.Linq;
using StudyStash.Core.Rich;
using StudyStash.Library;

namespace StudyStash.Core.Tests;

/// <summary>
/// Illustrations: a lecture that describes a thing gets a labelled drawing of it, planned by the designer and drawn by
/// an illustrator, its parts named so they can be pointed at; one untrue to the lecture, or no drawing at all, is left
/// out and the notes stay as written; crowded callouts are spaced out; what's wrong is said in words. Every engine is a
/// fake.
/// </summary>
public class IllustrationTests
{
    /// <summary>A small drone, drawn by the house rules: two labels on top of each other, one running off the right.</summary>
    const string Toy = """
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 400 300">
          <title>A small drone</title>
          <defs><linearGradient id="g-metal" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#F3F5F7"/><stop offset="1" stop-color="#AEB6BE"/></linearGradient></defs>
          <g id="art">
            <g id="frame" transform="translate(200 150)"><path d="M-80 -60 L80 60 M80 -60 L-80 60" stroke="#24272B" stroke-width="8"/></g>
            <g id="motor"><circle cx="120" cy="90" r="18" fill="url(#g-metal)" stroke="#6B747D"/></g>
            <g id="battery"><rect x="180" y="135" width="40" height="30" rx="4" fill="#4A9AD1" stroke="#174C75"/></g>
          </g>
          <g id="labels" font-size="14">
            <g id="label-frame"><polyline points="150,113 60,40" fill="none" stroke="#6E6E73"/><text x="56" y="44" text-anchor="end" fill="#1D1D1F">Frame</text></g>
            <g id="label-motor"><polyline points="110,80 60,50" fill="none" stroke="#6E6E73"/><text x="56" y="54" text-anchor="end" fill="#1D1D1F">Motor</text></g>
            <g id="label-battery"><polyline points="220,150 300,150" fill="none" stroke="#6E6E73"/><text x="304" y="155" fill="#1D1D1F">Battery pack and strap</text></g>
          </g>
        </svg>
        """;

    static readonly Illustration.Planned[] Parts =
    [
        new("frame", "Frame", "X-shaped carbon fibre frame"),
        new("motor", "Motor", "Brushless outrunner, spins clockwise"),
        new("battery", "Battery pack and strap", "4S LiPo strapped on top"),
    ];

    [Fact]
    public void The_planned_parts_are_named_in_the_drawing_and_read_back_as_parts()
    {
        string named = Illustration.Name(Toy, [.. Parts, new("gps", "GPS", "On a mast at the back")]);
        var drawing = SafeSvg.Clean(named);
        Assert.Null(drawing.Problem);
        Assert.True(drawing.Illustrated);
        Assert.Equal(Parts.Select(p => (p.Id, p.Name, p.Note)), drawing.Parts.Select(p => (p.Id, p.Name, p.Note)));
        // The drawing's own title and its callouts' words are still what search and screen readers get.
        Assert.Equal("A small drone", drawing.Title);
        Assert.Equal(["Frame", "Motor", "Battery pack and strap"], drawing.Texts);
        // Named again (a second pass), the names are replaced, never doubled.
        var twice = XDocument.Parse(Illustration.Name(named, [new("motor", "Front motor", "")])).Descendants().Single(e => (string?)e.Attribute("id") == "motor");
        Assert.Equal(["Front motor"], twice.Elements().Where(e => e.Name.LocalName == "title").Select(t => t.Value));
        Assert.DoesNotContain(twice.Elements(), e => e.Name.LocalName == "desc");
    }

    [Fact]
    public void A_part_or_a_callout_can_be_drawn_alone_and_the_drawing_without_its_callouts()
    {
        var motor = XDocument.Parse(Illustration.Part(Toy, "motor")).Root!;
        var drawn = motor.Descendants().Select(e => e.Name.LocalName).ToList();
        Assert.Contains("circle", drawn);
        Assert.Contains("linearGradient", drawn); // its gradient comes with it
        Assert.DoesNotContain("rect", drawn);
        Assert.DoesNotContain("text", drawn);
        Assert.Contains(motor.Descendants(), e => (string?)e.Attribute("id") == "art"); // where the whole drawing has it

        var label = XDocument.Parse(Illustration.CalloutOf(Toy, "battery")).Root!;
        Assert.Equal(["Battery pack and strap"], label.Descendants().Where(e => e.Name.LocalName == "text").Select(t => t.Value));
        Assert.DoesNotContain(label.Descendants(), e => e.Name.LocalName is "rect" or "circle" or "path");

        Assert.DoesNotContain(XDocument.Parse(Illustration.Bare(Toy)).Descendants(), e => e.Name.LocalName == "text");
        var leaders = XDocument.Parse(Illustration.Leaders(Toy)).Root!;
        Assert.DoesNotContain(leaders.Descendants(), e => e.Name.LocalName == "text");
        Assert.Equal(3, leaders.Descendants().Count(e => e.Name.LocalName == "polyline"));
    }

    [Fact]
    public void What_would_look_wrong_is_said_and_crowded_callouts_are_spaced_out()
    {
        var problems = SvgLint.Problems(Toy, [.. Parts, new("gps", "GPS", "")]);
        Assert.Contains(problems, p => p.Contains("drawn too simply"));
        Assert.Contains(problems, p => p.Contains("labels “Frame” and “Motor” overlap"));
        Assert.Contains(problems, p => p.Contains("“Battery pack and strap” runs off the right edge"));
        Assert.Contains(problems, p => p.Contains("no group with id=\"gps\""));
        Assert.DoesNotContain(problems, p => p.Contains("leader line for"));

        string tidy = Callouts.Tidy(Toy);
        var after = SvgLint.Problems(tidy, Parts);
        Assert.DoesNotContain(after, p => p.Contains("overlap") || p.Contains("runs off"));
        Assert.DoesNotContain(after, p => p.Contains("leader line")); // each moved label still points at its part
        Assert.Equal(Callouts.Tidy(tidy), tidy); // and a tidy drawing is left exactly as it is

        // A leader that stops short of its part is said; one off the drawing's edge too.
        string short_ = Toy.Replace("points=\"110,80 60,50\"", "points=\"80,60 60,50\"");
        Assert.Contains(SvgLint.Problems(short_, Parts), p => p.Contains("leader line for “Motor” ends"));
    }

    [Fact]
    public void A_revision_sends_only_what_changes_and_each_group_goes_in_place_of_its_namesake()
    {
        const string fix = """
            <svg xmlns="http://www.w3.org/2000/svg">
              <defs><linearGradient id="g-gps"><stop offset="0" stop-color="#3B4045"/></linearGradient></defs>
              <g id="label-motor"><polyline points="120,90 60,72" fill="none" stroke="#6E6E73"/><text x="56" y="76" text-anchor="end" fill="#1D1D1F">Motor</text></g>
              <g id="gps"><circle cx="300" cy="60" r="10" fill="url(#g-gps)"/></g>
            </svg>
            """;
        var root = XDocument.Parse(IllustrationDesign.Patch(Toy, fix)!).Root!;
        XElement Id(string id) => root.Descendants().Single(e => (string?)e.Attribute("id") == id);
        Assert.Equal("120,90 60,72", (string?)Id("label-motor").Elements().First().Attribute("points"));
        Assert.Equal("labels", (string?)Id("label-motor").Parent!.Attribute("id"));
        Assert.Equal("art", (string?)Id("gps").Parent!.Attribute("id")); // a new part goes with the others
        Assert.Equal("defs", Id("g-gps").Parent!.Name.LocalName);
        Assert.Equal(1, root.Descendants().Count(e => (string?)e.Attribute("id") == "label-motor"));
        Assert.Contains(root.Descendants(), e => (string?)e.Attribute("id") == "battery"); // the rest as it was
        // A whole drawing sent back is the drawing; one that can't be read changes nothing.
        Assert.Equal(Toy, IllustrationDesign.Patch(Toy, Toy));
        Assert.Null(IllustrationDesign.Patch(Toy, "<svg><g id="));
    }

    [Fact]
    public void The_phone_and_a_markdown_download_list_an_illustrations_parts()
    {
        string notes = "## The drone\n\n```svg\n" + Illustration.Name(Toy, Parts) + "\n```\n";
        string html = PhoneNotes.Render(notes);
        Assert.Contains("<details class=\"parts\"><summary>Parts</summary><dl><dt>Frame</dt><dd>X-shaped carbon fibre frame</dd>", html);
        Assert.Contains("<title>Motor</title>", html); // and a browser names each part under the pointer

        using var dir = new TempDir();
        var cfg = new Config(dir["home"], dir["pool"]) { Classes = [new ClassDef("ENGR 3350", [])] };
        Directory.CreateDirectory(cfg.Home);
        var store = new Store(cfg.DbPath, cfg.PoolDir);
        var m = new Meeting("lec-drone") { Title = "Quadcopters", Date = "2026-10-06" };
        store.Save(m, new Classification("ENGR 3350", 0.9, "folder"), summaryMd: notes);
        var file = NoteExport.Lecture(new LibraryReader(new Config("", ""), store).Lecture(m.Id)!, transcript: false, _ => null);
        Assert.Contains("![Diagram: A small drone](", file.Markdown);
        Assert.Contains("- **Battery pack and strap**: 4S LiPo strapped on top", file.Markdown);
        Assert.Contains("<title>Frame</title>", Assert.Single(file.Assets).Text);

        // A part whose name looks like markup is written as words, in the file and on the phone.
        string sly = "## The drone\n\n```svg\n" + Illustration.Name(Toy, [new("motor", "<img src=x onerror=alert(1)>", "")]) + "\n```\n";
        Assert.DoesNotContain("<img", PhoneNotes.Render(sly));
        var m2 = new Meeting("lec-sly") { Title = "Sly", Date = "2026-10-07" };
        store.Save(m2, new Classification("ENGR 3350", 0.9, "folder"), summaryMd: sly);
        var file2 = NoteExport.Lecture(new LibraryReader(new Config("", ""), store).Lecture(m2.Id)!, transcript: false, _ => null);
        Assert.Contains("- **&lt;img src=x onerror=alert(1)&gt;**", file2.Markdown);
    }

    // --- the designer, end to end ----------------------------------------------------------------------------------

    const string Said = "Today's drone: the X frame is carbon fibre, each motor is a brushless outrunner that spins clockwise or counter-clockwise, "
        + "and the battery pack is a 4S LiPo held by a velcro strap on top of the frame, its XT60 plug into the power board.";

    static readonly Meeting Lecture = new("lec-drone")
    {
        Title = "Anatomy of a quadcopter", Date = "2026-10-06T10:00:00", Folder = "ENGR 3350",
        Transcript = string.Join("\n", Enumerable.Range(0, 24).Select(i => $"[{TimedText.Clock(i * 20)}] {Said}")),
    };

    const string Notes = "## Summary\nA quadcopter, part by part.\n\n## The frame\n- An X frame of carbon fibre.\n\n## Questions to review\n1. Which motors spin clockwise?";

    static JsonObject Plan(string after = "The frame", int parts = 3) => new()
    {
        ["title"] = "The quadcopter from above", ["after"] = after, ["at"] = "00:20", ["caption"] = "The frame, a motor and the battery strapped on top.",
        ["subject"] = "A quadcopter seen from above, front at the top.", ["layout"] = "The X frame in the middle, the battery on top of it.",
        ["parts"] = new JsonArray([.. Parts.Take(parts).Select(p => (JsonNode)new JsonObject { ["id"] = p.Id, ["name"] = p.Name, ["note"] = p.Note })]),
    };

    static string Designer(params JsonObject[] plans) =>
        new JsonObject { ["reason"] = "The lecture describes the drone part by part.", ["diagrams"] = new JsonArray(), ["illustrations"] = new JsonArray([.. plans]) }.ToJsonString();

    static Func<string, bool, Task<string>> Engine(string drawing, List<string>? asked = null) => (prompt, json) =>
    {
        asked?.Add(prompt);
        return Task.FromResult(prompt.StartsWith("You design the diagrams", StringComparison.Ordinal) ? Designer(Plan(), Plan(after: "Physics")) : drawing);
    };

    [Fact]
    public async Task A_planned_illustration_is_drawn_named_and_placed_in_its_section()
    {
        var asked = new List<string>();
        var result = await DiagramDesign.DesignAsync(Lecture, Notes, Drawings.FlowchartsAndSvg, Engine("Here it is:\n```svg\n" + Toy + "\n```", asked));
        Assert.Contains("## Illustrations", asked[0]); // the designer is told about illustrations
        Assert.StartsWith("You are a scientific illustrator", asked[1]);
        Assert.Contains("id \"battery\": Battery pack and strap", asked[1]);
        Assert.Contains(result.Dropped, d => d.Contains("illustration 2") && d.Contains("isn't a heading"));

        var d = Assert.Single(result.Drawn);
        Assert.Equal(NoteBlockKind.Svg, d.Kind);
        var block = NoteBlocks.Find(result.Notes).Single();
        Assert.StartsWith("<!-- Study Stash diagram, from 00:20 -->", block.Text);
        var drawing = SafeSvg.Clean(block.Text);
        Assert.Equal(["frame", "motor", "battery"], drawing.Parts.Select(p => p.Id));
        // Its callouts were spaced out on the way in.
        Assert.DoesNotContain(SvgLint.Problems(block.Text, Parts), p => p.Contains("overlap") || p.Contains("runs off"));
        Assert.True(result.Notes.IndexOf("**The quadcopter from above**", StringComparison.Ordinal) < result.Notes.IndexOf("## Questions to review", StringComparison.Ordinal));
        // A second pass replaces it rather than stacking another.
        Assert.Equal(Notes, DiagramDesign.Strip(result.Notes));
    }

    /// <summary>An engine that answers each prompt its own way, and keeps every request it was sent.</summary>
    sealed class Answering(Func<string, CancellationToken, Task<string>> answer) : AiProvider
    {
        public override string Id => "claude";
        public override string Name => "Claude";
        public override string Binary => "claude";
        public override string Site => "https://example.test/claude";
        public override bool Available() => true;
        public List<AiRequest> Requests { get; } = [];
        public override List<string> Command(AiRequest req, bool stream) => [];
        public override IEnumerable<AiEvent> Parse(string line) => [];

        public override async IAsyncEnumerable<AiEvent> RunAsync(AiRequest req, bool stream = true, [EnumeratorCancellation] CancellationToken ct = default)
        {
            lock (Requests) Requests.Add(req);
            yield return new AiEvent("final", await answer(req.Prompt, ct));
        }
    }

    /// <summary>Through the notes as the library writes them: the drawing takes longer than the diagrams' time and is
    /// still kept, drawn by the designer's engine and model at low effort, with time enough to finish.</summary>
    [Fact]
    public async Task Through_the_notes_an_illustration_may_take_longer_than_the_diagrams_and_is_drawn_at_low_effort()
    {
        using var dir = new TempDir();
        var cfg = new Config(dir["home"], dir["pool"]) { OllamaEnabled = true, OllamaModel = "qwen3:8b" };
        new AiSettings { Provider = "claude", Fallback = false }.Save(cfg.Home);
        var claude = new Answering(async (prompt, ct) =>
        {
            if (prompt.StartsWith("You design the diagrams", StringComparison.Ordinal)) return Designer(Plan());
            if (!prompt.StartsWith("You are a scientific illustrator", StringComparison.Ordinal)) return Notes;
            await Task.Delay(TimeSpan.FromSeconds(3), ct);
            return "```svg\n" + Toy + "\n```";
        });
        var log = new List<string>();
        var ai = new AiJobs(cfg.Home) { Providers = _ => claude, Checks = new FakeChecks().Installed("claude").Build(), Log = log.Add, DiagramTimeout = TimeSpan.FromSeconds(2) };

        string filed = await ai.SummarizeAsync(Lecture, cfg);
        Assert.DoesNotContain("```svg", filed); // filed first; the illustration follows
        var pick = await ai.DesignerAsync(cfg, ai.TakeDiagramsFollow(Lecture.Id)!);
        string notes = (await ai.DesignDiagramsAsync(Lecture, cfg, filed, pick!, CancellationToken.None))!.Notes;
        Assert.Contains("```svg", notes);
        Assert.Contains("drew 1 (The quadcopter from above)", log[^1]);
        var drawing = claude.Requests.Single(r => r.Prompt.StartsWith("You are a scientific illustrator", StringComparison.Ordinal));
        Assert.Equal(("opus", "low", IllustrationDesign.Timeout), (drawing.Model, drawing.Effort, drawing.Timeout));
    }

    [Fact]
    public async Task No_drawing_or_one_the_lecture_never_said_leaves_the_notes_as_written()
    {
        var none = await DiagramDesign.DesignAsync(Lecture, Notes, Drawings.FlowchartsAndSvg, Engine("I'd rather describe it in words."));
        Assert.Same(Notes, none.Notes);
        Assert.Contains(none.Dropped, d => d.Contains("held no drawing"));

        string untrue = Toy.Replace(">Frame<", ">Crankshaft<").Replace(">Motor<", ">Turbocharger<").Replace(">Battery pack and strap<", ">Piston rings<");
        var invented = await DiagramDesign.DesignAsync(Lecture, Notes, Drawings.FlowchartsAndSvg, (prompt, json) => Task.FromResult(
            prompt.StartsWith("You design the diagrams", StringComparison.Ordinal)
                ? Designer(new JsonObject
                {
                    ["title"] = "The engine", ["after"] = "The frame", ["at"] = "", ["caption"] = "An engine.", ["subject"] = "An engine from the side.", ["layout"] = "",
                    ["parts"] = new JsonArray([.. new[] { "Crankshaft", "Turbocharger", "Piston rings" }.Select(n => (JsonNode)new JsonObject { ["name"] = n, ["note"] = "spins inside the block" })]),
                })
                : "```svg\n" + untrue.Replace("id=\"frame\"", "id=\"crankshaft\"").Replace("id=\"motor\"", "id=\"turbocharger\"").Replace("id=\"battery\"", "id=\"piston-rings\"") + "\n```"));
        Assert.Same(Notes, invented.Notes);
        Assert.Contains(invented.Dropped, d => d.Contains("aren't what the lecture said"));

        // An engine that draws only flowcharts never plans or draws one.
        var local = DiagramDesign.Read(Designer(Plan()), Notes, Lecture.Transcript, Drawings.Flowcharts);
        Assert.Empty(local.Illustrations);
        var few = DiagramDesign.Read(Designer(Plan(parts: 2)), Notes, Lecture.Transcript, Drawings.FlowchartsAndSvg);
        Assert.Empty(few.Illustrations);
        Assert.Contains(few.Dropped, d => d.Contains("too few"));
    }
}
