using System.Net.Http.Json;
using StudyStash.Core.Ai;
using StudyStash.Library;

namespace StudyStash.Core.Tests;

/// <summary>A follow-up question remembers the conversation it's in: the earlier turns go to the AI with it (the last
/// few, cut short, fewer for the small free AI), and its search reads the previous question and the passages the
/// previous answer drew on. Every AI here is a stand-in.</summary>
public class AskMemoryTests
{
    const string Vessels = "## Summary\nHow arteries, capillaries and veins differ.\n\n## Three kinds of vessel\n"
        + "- Arteries carry blood away from the heart under high pressure.\n- Capillaries are one cell thick: gases are exchanged there.\n"
        + "- Veins carry blood back at low pressure, with valves.";

    [Fact]
    public async Task A_follow_up_searches_with_the_previous_question_and_reads_its_sources_again()
    {
        using var dir = new TempDir();
        var cfg = new Config(dir["home"], dir["pool"]) { OllamaEnabled = true, Classes = [new ClassDef("BIO 110")] };
        using var store = new Store(cfg.DbPath, cfg.PoolDir);
        store.Save(new Meeting("v1") { Title = "Blood vessels", Date = "2026-10-08" }, new Classification("BIO 110", 1, "folder"), Vessels, "Claude");
        store.Save(new Meeting("m1") { Title = "Muscles", Date = "2026-10-01" }, new Classification("BIO 110", 1, "folder"),
            "## Summary\nMuscle fibres contract as actin slides over myosin.", "Claude");
        var reader = new LibraryReader(cfg, store);
        const string followUp = "Explain that more simply";

        // On its own, the follow-up's words find nothing in the folder.
        bool asked = false;
        var alone = await reader.AskAsync(followUp, className: "BIO 110", chat: (_, _) =>
        {
            asked = true;
            return Task.FromResult("{}");
        });
        Assert.False(asked);
        Assert.Equal(LibraryReader.NoAnswer, alone["answer"]!.GetValue<string>());

        // In a conversation it finds what the previous question was about, the passage its answer drew on first, and
        // the AI reads the conversation before the question.
        var earlier = new List<AskTurn>
        {
            new("What are the three kinds of blood vessel?", "Arteries, capillaries and veins.")
            {
                Sources = [new AskSource("v1", "Blood vessels", "BIO 110", "2026-10-08", null, "Three kinds of vessel")],
            },
        };
        string prompt = "";
        var answer = await reader.AskAsync(followUp, className: "BIO 110", earlier: earlier, chat: (p, _) =>
        {
            prompt = p;
            return Task.FromResult("""{"answer": "Arteries take blood out, veins bring it back, capillaries swap gases.", "sources": [1]}""");
        });
        Assert.Contains("[1] Blood vessels (BIO 110, 2026-10-08), Three kinds of vessel:\nArteries carry blood away", prompt);
        Assert.Contains("[2] Blood vessels (BIO 110, 2026-10-08), Three kinds of vessel:\nCapillaries are one cell thick", prompt);
        Assert.Contains("[3] Blood vessels (BIO 110, 2026-10-08), Three kinds of vessel:\nVeins carry blood back", prompt);
        Assert.DoesNotContain("Muscle fibres", prompt);
        Assert.EndsWith("Student: What are the three kinds of blood vessel?\nYou: Arteries, capillaries and veins.\n\nQuestion: Explain that more simply", prompt);
        Assert.Equal(1, answer["remembered"]!.GetValue<int>());
        Assert.Equal("Three kinds of vessel", answer["sources"]![0]!["section"]!.GetValue<string>());
    }

    static List<AskTurn> Conversation(int turns, int answerLength) =>
    [
        .. Enumerable.Range(1, turns).Select(i => new AskTurn($"Question {i}?", $"Answer {i}: " + new string('x', answerLength))),
    ];

    [Fact]
    public async Task The_turns_go_to_the_library_cut_short_and_fewer_to_the_small_free_AI()
    {
        using var dir = new TempDir();
        var cfg = new Config(dir["home"], dir["pool"]) { PoolPassword = "pw", OllamaEnabled = true, OllamaModel = Ollama.SmallModel };
        using var store = new Store(cfg.DbPath, cfg.PoolDir);
        var claude = new ScriptedAi("claude", "Claude") { Script = [new AiEvent("final", """{"answer":"Capillaries.","sources":[1]}""")] };
        var ollama = new ScriptedAi("ollama") { Script = [new AiEvent("final", """{"answer":"Capillaries.","sources":[1]}""")] };
        var ai = new AiJobs(cfg.Home)
        {
            Providers = id => id switch { "claude" => claude, "ollama" => ollama, _ => new ScriptedAi(id) },
            Checks = new FakeChecks().Installed("claude").Build(),
        };
        await using var site = await TestSite.StartAsync(b => LibraryWeb.Build(b, cfg, store, new Pipeline(cfg, store, log: _ => { }), new LibraryWebOptions
        {
            ListModels = _ => Task.FromResult<List<(string, double)>?>(null), Tailscale = () => new TailscaleInfo(),
            Latest = _ => Task.FromResult<Release?>(null), Ai = ai,
        }));
        var remote = new AiRemote("http://localhost", "pw", site.Client);
        const string live = "Arteries, capillaries and veins. Capillaries are one cell thick.";
        var history = Conversation(8, 5000);

        // Claude: the last six turns, each cut to about 4,000 characters, from its start.
        var reply = await remote.AskAsync(new AskRequest("And the second one?") { Engine = "claude", Live = live, History = history });
        Assert.Equal(6, reply!.Remembered);
        string sent = Assert.Single(claude.Prompts);
        Assert.DoesNotContain("Question 2?", sent);
        Assert.Contains("Student: Question 3?\nYou: Answer 3: xxx", sent);
        Assert.Contains("Student: Question 8?\nYou: Answer 8: xxx", sent);
        string eighth = sent[(sent.IndexOf("You: Answer 8", StringComparison.Ordinal) + 5)..sent.IndexOf("\n\nQuestion: And the second one?", StringComparison.Ordinal)];
        Assert.Equal(AskMemory.TurnChars - "Question 8?".Length, eighth.Length);
        Assert.EndsWith("…", eighth);

        // The small free AI: the last two, shorter still.
        reply = await remote.AskAsync(new AskRequest("And the second one?") { Engine = "ollama", Live = live, History = history });
        Assert.Equal(2, reply!.Remembered);
        sent = Assert.Single(ollama.Prompts);
        Assert.DoesNotContain("Question 6?", sent);
        Assert.Contains("Student: Question 7?", sent);
        Assert.Contains("Student: Question 8?", sent);
        Assert.DoesNotContain(new string('x', 600), sent);

        // A first question remembers nothing; one sent the old way (no history, or one that can't be read) is answered.
        Assert.Equal(0, (await remote.AskAsync(new AskRequest("What are the kinds?") { Engine = "claude", Live = live }))!.Remembered);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v2/ai/ask")
        {
            Content = JsonContent.Create(new { question = "What are the kinds?", engine = "claude", live, history = "nope" }),
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "pw");
        using var junk = await site.Client.SendAsync(request);
        Assert.True(junk.IsSuccessStatusCode);
        Assert.Contains("\"remembered\":0", await junk.Content.ReadAsStringAsync());
    }
}
