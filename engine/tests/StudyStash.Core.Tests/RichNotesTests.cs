using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using StudyStash.Core.Ai;
using StudyStash.Core.Rich;

namespace StudyStash.Core.Tests;

/// <summary>
/// Settings → AI engines → Rich notes and Claude Code's speed: the switches are kept in ai.json (and changed through the
/// library's API the way the laptop does it), Claude Code is run as the speed says (its fast mode, or a quicker model),
/// and a kind of rich notes that's switched off is never asked for. Every engine is a fake.
/// </summary>
public class RichNotesTests
{
    const string Said = "A TCP connection opens with a three way handshake: the client sends a SYN with sequence number 100, the server answers "
        + "with a SYN ACK, and the client's ACK makes the connection established on both sides. Closing takes four segments.";

    static Meeting Lecture() => new("tcp") { Title = "TCP", Date = "2026-10-04", Transcript = string.Join("\n", Enumerable.Repeat(Said, 14)) };

    const string Notes = "## Summary\nHow TCP opens and closes.\n\n## Key points\n- SYN, SYN ACK, ACK.\n";

    /// <summary>Claude Code, faked: it remembers every run it's asked for (model, effort, fast mode and prompt), and answers a
    /// design with <see cref="Design"/>.</summary>
    sealed class Recorder : AiProvider
    {
        public override string Id => "claude";
        public override string Name => "Claude";
        public override string Binary => "claude";
        public override string Site => "https://example.test/claude";
        public override bool Available() => true;
        public override List<string> Command(AiRequest req, bool stream) => [];
        public override IEnumerable<AiEvent> Parse(string line) => [];
        public readonly List<AiRequest> Runs = [];
        public string Design = """{"reason": "Nothing to draw.", "diagrams": []}""";

        public override async IAsyncEnumerable<AiEvent> RunAsync(AiRequest req, bool stream = true, [EnumeratorCancellation] CancellationToken ct = default)
        {
            lock (Runs) Runs.Add(req);
            await Task.Yield();
            yield return new AiEvent("final", req.Prompt.StartsWith("You design the diagrams", StringComparison.Ordinal) ? Design : Notes);
        }
    }

    static (AiJobs Ai, Recorder Claude, Config Cfg) Rig(TempDir dir, Action<AiSettings> set)
    {
        var cfg = new Config(dir["home"], dir["pool"]) { OllamaEnabled = false, Classes = [new ClassDef("CS 101")] };
        var settings = new AiSettings { Provider = "claude", Fallback = false };
        set(settings);
        settings.Save(cfg.Home);
        var claude = new Recorder();
        return (new AiJobs(cfg.Home) { Providers = _ => claude, Checks = new FakeChecks().Installed("claude").Build() }, claude, cfg);
    }

    [Fact]
    public void The_switches_and_speed_are_kept_in_ai_json_and_rich_notes_with_no_kind_left_are_off()
    {
        using var dir = new TempDir();
        Assert.Equal((RichKinds.All, AiSpeed.Standard), (new AiSettings().Kinds(), new AiSettings().Speed)); // on, as before

        var s = new AiSettings();
        s.SetRich(diagrams: false);
        s.Speed = AiSpeed.Fast;
        s.Save(dir["home"]);
        var again = AiSettings.Load(dir["home"]);
        Assert.Equal((RichKinds.Plots | RichKinds.Drawings, AiSpeed.Fast), (again.Kinds(), again.Speed));

        again.SetRich(plots: false);
        again.SetRich(drawings: false); // the last kind: nothing is left to add, so rich notes are off, every kind back on for next time
        Assert.Equal((false, RichKinds.None), (again.RichNotes, again.Kinds()));
        Assert.True(again.RichDiagrams && again.RichPlots && again.RichDrawings);
        again.SetRich(on: true);
        Assert.Equal(RichKinds.All, again.Kinds());

        // Diagrams turned off the way they were before the switch: still off, and the switch brings them back (automatic).
        var legacy = new AiSettings { Diagrams = DiagramEngines.Off };
        Assert.Equal(RichKinds.None, legacy.Kinds());
        legacy.SetRich(on: true);
        Assert.Equal((RichKinds.All, DiagramEngines.Auto), (legacy.Kinds(), legacy.Diagrams));
    }

    [Fact]
    public void Claude_Code_is_run_as_the_speed_says()
    {
        var claude = new ClaudeProvider { At = "/x/claude" };
        string Flags(AiSpeed.How how) => string.Join(' ', claude.Command(new AiRequest("p", "/tmp") { Model = how.Model, Effort = how.Effort, Fast = how.Fast }, false).SkipWhile(a => a != "--disallowedTools").Skip(1)
            .SkipWhile(a => !a.StartsWith("--", StringComparison.Ordinal)));

        // Fast mode is the same Opus, turned on for this one run only: its settings on the command line, the model named.
        Assert.Equal("--model opus --settings {\"fastMode\":true}", Flags(AiSpeed.ForNotes("claude", "", AiSpeed.Fast)));
        Assert.Equal("--model opus --effort high --settings {\"fastMode\":true}", Flags(AiSpeed.ForDesign("claude", AiSpeed.Fast)));
        // Standard is what it was: the notes as the model is set up, the designer on Opus at high effort.
        Assert.Equal("", Flags(AiSpeed.ForNotes("claude", "", AiSpeed.Standard)));
        Assert.Equal("--model opus --effort high", Flags(AiSpeed.ForDesign("claude", AiSpeed.Standard)));
        // A quicker model: Sonnet at low effort for the notes, a little more for the designer.
        Assert.Equal("--model sonnet --effort low", Flags(AiSpeed.ForNotes("claude", "", AiSpeed.Quick)));
        Assert.Equal("--model sonnet --effort medium", Flags(AiSpeed.ForDesign("claude", AiSpeed.Quick)));
        // A model picked on purpose isn't Opus's to speed up; and nothing here changes another engine.
        Assert.Equal("--model sonnet", Flags(AiSpeed.ForNotes("claude", "sonnet", AiSpeed.Fast)));
        Assert.Equal(new AiSpeed.How("", "", false), AiSpeed.ForNotes("codex", "", AiSpeed.Fast));
        Assert.Equal(new AiSpeed.How("", "high", false), AiSpeed.ForDesign("codex", AiSpeed.Fast));
    }

    [Theory]
    [InlineData(AiSpeed.Standard, "", "", false, "opus", "high", false)]
    [InlineData(AiSpeed.Fast, "opus", "", true, "opus", "high", true)]
    [InlineData(AiSpeed.Quick, "sonnet", "low", false, "sonnet", "medium", false)]
    public async Task The_notes_and_the_designer_each_run_as_the_speed_says(string speed, string notesModel, string notesEffort, bool notesFast,
        string designModel, string designEffort, bool designFast)
    {
        using var dir = new TempDir();
        var (ai, claude, cfg) = Rig(dir, s => s.Speed = speed);
        var m = Lecture();
        await ai.SummarizeAsync(m, cfg);
        var pick = await ai.DesignerAsync(cfg, "claude");
        Assert.NotNull(pick);
        await ai.DesignDiagramsAsync(m, cfg, Notes, pick!, CancellationToken.None);

        Assert.Equal(2, claude.Runs.Count);
        Assert.Equal((notesModel, notesEffort, notesFast), (claude.Runs[0].Model, claude.Runs[0].Effort, claude.Runs[0].Fast));
        Assert.Equal((designModel, designEffort, designFast), (claude.Runs[1].Model, claude.Runs[1].Effort, claude.Runs[1].Fast));
    }

    /// <summary>What a designer is told when only <paramref name="kinds"/> are switched on, and every sign of the others.</summary>
    static async Task<string> PromptFor(RichKinds kinds)
    {
        using var dir = new TempDir();
        var (ai, claude, cfg) = Rig(dir, s => s.SetRich(diagrams: kinds.HasFlag(RichKinds.Diagrams), plots: kinds.HasFlag(RichKinds.Plots), drawings: kinds.HasFlag(RichKinds.Drawings)));
        var pick = await ai.DesignerAsync(cfg, "claude");
        Assert.Equal(kinds, pick!.Kinds);
        await ai.DesignDiagramsAsync(Lecture(), cfg, Notes, pick, CancellationToken.None);
        return claude.Runs.Single().Prompt;
    }

    [Fact]
    public async Task A_kind_that_is_switched_off_is_never_asked_for()
    {
        string all = await PromptFor(RichKinds.All);
        Assert.Contains("```mermaid", all);
        Assert.Contains("```plot", all);
        Assert.Contains("```svg", all);
        Assert.Contains("## Illustrations", all);
        Assert.Contains("\"mermaid\" (every kind above), \"plot\" or \"svg\"", all);

        string plots = await PromptFor(RichKinds.Plots);
        Assert.Contains("```plot", plots);
        Assert.Contains("- \"kind\": \"plot\".", plots);
        foreach (string gone in new[] { "mermaid", "flowchart", "svg", "## Illustrations", "illustrations" })
            Assert.DoesNotContain(gone, plots, StringComparison.OrdinalIgnoreCase);

        string diagrams = await PromptFor(RichKinds.Diagrams);
        Assert.Contains("```mermaid", diagrams);
        foreach (string gone in new[] { "```plot", "\"plot\"", "svg", "## Illustrations", "illustrations" })
            Assert.DoesNotContain(gone, diagrams, StringComparison.OrdinalIgnoreCase);

        string drawings = await PromptFor(RichKinds.Drawings);
        Assert.Contains("```svg", drawings);
        Assert.Contains("## Illustrations", drawings);
        foreach (string gone in new[] { "```mermaid", "```plot", "\"plot\"", "## Size and shape" })
            Assert.DoesNotContain(gone, drawings, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_diagram_of_a_kind_that_is_switched_off_is_left_out_even_when_the_designer_draws_it()
    {
        static JsonObject One(string kind, string source) => new() { ["title"] = kind + " one", ["after"] = "Key points", ["at"] = "", ["kind"] = kind, ["source"] = source, ["caption"] = "A caption." };
        string reply = new JsonObject
        {
            ["reason"] = "Three ways to show it.",
            ["diagrams"] = new JsonArray(
                One("mermaid", "flowchart LR\n  A[\"SYN\"] --> B[\"SYN ACK\"] --> C[\"ACK\"]"),
                One("plot", "x 0 to 10 \"n\"\nf(n) = 2^n \"2ⁿ\""),
                One("svg", "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 640 100\"><text x=\"20\" y=\"50\">SYN</text></svg>")),
            ["illustrations"] = new JsonArray(),
        }.ToJsonString();

        Assert.Equal([NoteBlockKind.Mermaid, NoteBlockKind.Plot, NoteBlockKind.Svg], DiagramDesign.Read(reply, Notes, "", Drawings.FlowchartsAndSvg).Diagrams.Select(d => d.Kind));
        var onlyPlots = DiagramDesign.Read(reply, Notes, "", Drawings.FlowchartsAndSvg, RichKinds.Plots);
        Assert.Equal([NoteBlockKind.Plot], onlyPlots.Diagrams.Select(d => d.Kind));
        Assert.Equal(2, onlyPlots.Dropped.Count(d => d.Contains("switched off", StringComparison.Ordinal)));
        Assert.Equal([NoteBlockKind.Mermaid, NoteBlockKind.Svg], DiagramDesign.Read(reply, Notes, "", Drawings.FlowchartsAndSvg, RichKinds.Diagrams | RichKinds.Drawings).Diagrams.Select(d => d.Kind));
    }

    [Fact]
    public async Task With_rich_notes_off_nothing_extra_is_asked_of_the_AI_and_the_notes_are_told_to_draw_none()
    {
        using var dir = new TempDir();
        var (ai, claude, cfg) = Rig(dir, s => s.SetRich(on: false));
        Assert.Null(await ai.DesignerAsync(cfg, "claude"));
        await ai.SummarizeAsync(Lecture(), cfg);
        Assert.Contains("Diagrams: draw none", claude.Runs.Single().Prompt);
        Assert.Null(ai.TakeDiagramsFollow("tcp")); // nothing follows the notes: no diagram pass is queued

        // The notes engine drawing as it writes ("same as notes") keeps to what's on: no diagrams, no drawings, flowcharts only.
        Assert.Equal(Drawings.None, AiJobs.NotesDrawings("claude", RichKinds.Plots));
        Assert.Equal(Drawings.Flowcharts, AiJobs.NotesDrawings("claude", RichKinds.Diagrams | RichKinds.Plots));
        Assert.Equal(Drawings.FlowchartsAndSvg, AiJobs.NotesDrawings("claude", RichKinds.All));
    }
}
