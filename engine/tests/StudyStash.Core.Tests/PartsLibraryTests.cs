using System.Text.Json.Nodes;
using StudyStash.Core.Rich;

namespace StudyStash.Core.Tests;

/// <summary>
/// Illustrations composed from ready-made parts: the library that ships is safe and credited part by part, a hostile
/// part never reaches the page, a composer's scene is read strictly, and a lecture about the heart gets its figure from
/// the parts in one quick answer (and at once the next time), while a subject the library doesn't have is still drawn.
/// Every engine is a fake.
/// </summary>
public class PartsLibraryTests
{
    static readonly HashSet<string> Licences = ["CC0 1.0", "CC BY 3.0", "CC BY 4.0", "MIT", "BSD", "Public domain"];

    [Fact]
    public void Every_part_that_ships_is_safe_to_draw_and_says_who_made_it_under_what_licence()
    {
        var library = PartsLibrary.Default;
        Assert.True(library.Parts.Count >= 250, $"only {library.Parts.Count} parts");
        foreach (var part in library.Parts)
        {
            Assert.Contains(part.Licence, Licences);
            Assert.False(string.IsNullOrWhiteSpace(part.Credit), part.Id);
            var drawing = library.Drawing(part);
            Assert.True(drawing is not null, $"{part.Id} doesn't draw");
            string svg = drawing!.ToString().Replace("http://www.w3.org/2000/svg", "");
            Assert.DoesNotContain("<script", svg);
            Assert.DoesNotContain("http", svg);
            Assert.DoesNotContain("<image", svg);
        }
        // The credits name the sources that ask for them.
        var credits = library.Credits().Select(c => c.Credit).ToList();
        Assert.Contains(credits, c => c.Contains("Servier Medical Art"));
        Assert.Contains(credits, c => c.Contains("DBCLS TogoTV"));
        Assert.Contains(credits, c => c.Contains("Wokwi"));
    }

    [Fact]
    public void A_hostile_part_in_the_library_never_reaches_the_page()
    {
        const string hostile = """
            <parts version="1">
              <part id="trap" name="Trap" category="cell" tags="mitochondrion" licence="CC0 1.0" credit="nobody" x="50" y="50">
                <symbol viewBox="0 0 100 100">
                  <script>alert(1)</script>
                  <foreignObject width="100" height="100"><div xmlns="http://www.w3.org/1999/xhtml">hi</div></foreignObject>
                  <image href="https://example.com/x.png" width="10" height="10"/>
                  <circle cx="50" cy="50" r="40" fill="url(https://example.com/#a)" onload="alert(1)" style="fill:red;behavior:url(x.htc)"/>
                  <g id="bomb"><use href="#bomb"/></g>
                  <rect x="10" y="10" width="80" height="80" fill="#E5867A"/>
                </symbol>
              </part>
              <part id="empty" name="Empty" category="cell" tags="nothing" licence="CC0 1.0" credit="nobody">
                <symbol viewBox="0 0 10 10"><script>alert(2)</script></symbol>
              </part>
            </parts>
            """;
        var library = PartsLibrary.Read(hostile);
        var trap = library.Get("trap")!;
        string drawn = library.Drawing(trap)!.ToString();
        foreach (string bad in new[] { "script", "foreignObject", "image", "onload", "https:", "behavior", "<use" })
            Assert.DoesNotContain(bad, drawn);
        Assert.Contains("<rect", drawn);
        // A part with nothing safe left in it is never drawn.
        Assert.Null(library.Drawing(library.Get("empty")!));
        // A library file that declares entities isn't read at all.
        Assert.Empty(PartsLibrary.Read("<!DOCTYPE parts [<!ENTITY x \"boom\">]><parts>&x;</parts>").Parts);

        // Composed into a figure, the trap is still harmless.
        var plan = Plan([new("trap", "Trap", "")]);
        var scene = PartsScene.Read("""{"compose": true, "parts": [{"as": "t", "use": "trap", "x": 0, "y": 0, "w": 100}], "labels": [{"part": "trap", "to": "t"}]}""",
            new Dictionary<string, LibraryPart> { ["trap"] = trap }, plan.Parts, [])!;
        string svg = PartsScene.Draw(scene, plan, library)!.Svg;
        Assert.Null(SafeSvg.Clean(svg).Problem);
        Assert.DoesNotContain("script", svg);
        Assert.Equal(["trap"], SafeSvg.Clean(svg).Parts.Select(p => p.Id));
    }

    [Fact]
    public void A_composers_scene_is_read_strictly()
    {
        var library = PartsLibrary.Default;
        var heart = library.Get("heart-section")!;
        var allowed = new Dictionary<string, LibraryPart> { ["heart-section"] = heart };
        var plan = HeartPlan();
        var dropped = new List<string>();

        Assert.Null(PartsScene.Read("""{"compose": false, "why": "no lungs in the catalogue"}""", allowed, plan.Parts, dropped));
        Assert.Contains("no lungs", dropped[^1]);
        Assert.Null(PartsScene.Read("I'd rather draw it.", allowed, plan.Parts, dropped));

        dropped.Clear();
        var scene = PartsScene.Read("""
            {"compose": true,
             "parts": [{"as": "h", "use": "heart-section", "x": 0, "y": 0, "w": 500},
                       {"as": "x", "use": "jet-engine", "x": 0, "y": 0, "w": 10},
                       {"as": "y", "use": "heart-section", "x": 1e308, "y": 0, "w": 10},
                       {"as": "H", "use": "heart-section", "x": 0, "y": 0, "w": 10}],
             "shapes": [{"as": "s", "kind": "script", "points": [[0, 0], [1, 1]]}],
             "labels": [{"part": "aorta", "to": "h.pulmonary-trunk"}, {"part": "pulmonary-trunk", "to": "h.pulmonary-trunk"},
                        {"part": "left-ventricle", "to": "h.left-ventricle"}, {"part": "apex", "to": "h.nowhere"}, {"part": "made-up", "to": "h"}]}
            """, allowed, plan.Parts, dropped)!;
        Assert.Equal(["h"], scene.Parts.Select(p => p.As));
        Assert.Empty(scene.Shapes);
        // Two labels on one region: the one it's named for keeps it.
        Assert.Equal(["pulmonary-trunk", "left-ventricle"], scene.Labels.Select(l => l.Part));
        Assert.Contains(dropped, d => d.Contains("jet-engine"));
        Assert.Contains(dropped, d => d.Contains("used once"));
        Assert.Contains(dropped, d => d.Contains("h.nowhere"));
    }

    // --- through the diagram pass -------------------------------------------------------------------------------------

    const string Said = "Here is the heart cut open from the front. Blood comes in through the superior vena cava and the inferior vena cava "
        + "into the right atrium, through the tricuspid valve into the right ventricle, out the pulmonary trunk to the lungs. "
        + "It comes back in the pulmonary veins to the left atrium, through the mitral valve into the left ventricle, whose wall is three times thicker, "
        + "and out through the aortic valve into the aorta. The interventricular septum divides the ventricles and the apex points down and left.";

    static readonly Meeting Lecture = new("lec-heart")
    {
        Title = "The heart", Date = "2026-10-08T10:00:00", Folder = "BIOL 2100",
        Transcript = string.Join("\n", Enumerable.Range(0, 14).Select(i => $"[{TimedText.Clock(i * 30)}] {Said}")),
    };

    const string Notes = "## Summary\nThe heart's four chambers and its valves.\n\n## The chambers\n- Two atria above, two ventricles below.\n\n## Questions to review\n1. Which ventricle has the thicker wall?";

    static readonly string[] HeartParts = ["superior-vena-cava", "inferior-vena-cava", "right-atrium", "tricuspid-valve", "right-ventricle", "pulmonary-trunk",
        "pulmonary-veins", "left-atrium", "mitral-valve", "left-ventricle", "aortic-valve", "aorta", "interventricular-septum", "apex"];

    static IllustrationPlan Plan(IReadOnlyList<Illustration.Planned> parts) =>
        new("The heart cut open", "The chambers", 20, "The four chambers.", "The heart cut open from the front.", "", parts);

    static IllustrationPlan HeartPlan() => Plan([.. HeartParts.Select(id => new Illustration.Planned(id, Name(id), ""))]);

    static string Name(string id) => char.ToUpperInvariant(id[0]) + id[1..].Replace('-', ' ');

    static string Designer(string subject, string[] parts) => new JsonObject
    {
        ["reason"] = "The lecture walks through a thing part by part.", ["diagrams"] = new JsonArray(),
        ["illustrations"] = new JsonArray(new JsonObject
        {
            ["title"] = "The heart cut open", ["after"] = "The chambers", ["at"] = "00:20", ["caption"] = "The four chambers and their valves.",
            ["subject"] = subject, ["layout"] = "Atria above, ventricles below.",
            ["parts"] = new JsonArray([.. parts.Select(id => (JsonNode)new JsonObject { ["id"] = id, ["name"] = Name(id), ["note"] = "" })]),
        }),
    }.ToJsonString();

    static readonly string HeartScene = new JsonObject
    {
        ["compose"] = true,
        ["parts"] = new JsonArray(new JsonObject { ["as"] = "heart", ["use"] = "heart-section", ["x"] = 500, ["y"] = 500, ["w"] = 600 }),
        ["labels"] = new JsonArray([.. HeartParts.Select(id => (JsonNode)new JsonObject { ["part"] = id, ["to"] = "heart." + id })]),
    }.ToJsonString();

    [Fact]
    public async Task A_lecture_about_the_heart_gets_its_figure_from_the_parts_library_and_the_same_plan_comes_back_at_once()
    {
        var asked = new List<string>();
        int composed = 0;
        var extended = new List<TimeSpan>();
        var scenes = new ComposedScenes();
        Func<string, bool, Task<string>> ask = (prompt, json) =>
        {
            asked.Add(prompt);
            return Task.FromResult(Designer("A human heart cut open from the front, all four chambers.", HeartParts));
        };
        Func<string, Task<string>> compose = prompt =>
        {
            composed++;
            Assert.Contains("- heart-section — Heart, cut open", prompt);
            Assert.Contains("left-ventricle", prompt);
            return Task.FromResult(HeartScene);
        };
        Func<string, Task<string>> draw = _ => throw new InvalidOperationException("a composed figure is never drawn");

        var result = await DiagramDesign.DesignAsync(Lecture, Notes, Drawings.FlowchartsAndSvg, ask, draw: draw, longer: extended.Add, compose: compose, scenes: scenes);
        var figure = Assert.Single(result.Drawn);
        Assert.Equal((1, 1), (result.Composed, composed));
        Assert.Empty(extended); // nothing needed the long drawing time
        var drawing = SafeSvg.Clean(figure.Source);
        Assert.Null(drawing.Problem);
        Assert.Contains("left-ventricle", drawing.Parts.Select(p => p.Id));
        Assert.True(drawing.Parts.Count >= 12);
        Assert.Contains("Servier Medical Art", figure.Caption);
        Assert.DoesNotContain(SvgLint.Problems(figure.Source, HeartPlan().Parts), p => p.Contains("overlap") || p.Contains("runs off"));

        // The same plan again: from the cache, no composer asked.
        var again = await DiagramDesign.DesignAsync(Lecture, Notes, Drawings.FlowchartsAndSvg, ask, draw: draw,
            compose: _ => throw new InvalidOperationException("asked again"), scenes: scenes);
        Assert.Equal(figure.Source, Assert.Single(again.Drawn).Source);
    }

    [Fact]
    public async Task A_subject_the_library_has_nothing_for_is_drawn_as_before()
    {
        string[] engine = ["fan", "compressor", "combustor", "turbine", "nozzle"];
        int composed = 0, drawn = 0;
        var extended = new List<TimeSpan>();
        Func<string, bool, Task<string>> ask = (prompt, json) => Task.FromResult(Designer("A turbofan jet engine cut away from the side.", engine));
        var result = await DiagramDesign.DesignAsync(Lecture, Notes, Drawings.FlowchartsAndSvg, ask, longer: extended.Add,
            draw: _ =>
            {
                drawn++;
                return Task.FromResult("I'd rather not.");
            },
            compose: _ =>
            {
                composed++;
                return Task.FromResult("{}");
            });
        Assert.Equal((0, 1), (composed, drawn)); // no parts for it: the composer isn't even asked
        Assert.Equal([IllustrationDesign.Timeout], extended); // and the drawing gets its long time
        Assert.Equal(0, result.Composed);
    }
}
