using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using StudyStash.Core.Ai;
using StudyStash.Library;

namespace StudyStash.Core.Tests;

/// <summary>Ask about one lecture hands back the words of its notes the answer was built on (to highlight there): they
/// are asked for only for a lecture, come out of the AI's reply and never into the answer's text or its stream, and are
/// kept in the chat so a chat opened again highlights again.</summary>
public class AskQuotesTests
{
    static JsonArray Items(params string[] quotes) => new([.. quotes.Select(q => (JsonNode)q)]);

    [Fact]
    public void Odd_quotes_are_tidied_and_the_rest_of_the_reply_is_left_alone()
    {
        var kept = AskQuotes.Clean(Items("  “Capillaries are one cell\n thick”  ", "Arteries carry blood away", "capillaries are ONE cell thick", "veins", "", "x y",
            string.Join(' ', Enumerable.Repeat("word", 70)), "one two three", "four five six", "seven eight nine"));
        // Quote marks and line breaks go, the same words twice are one, a lone word or a speech-length run isn't a quote, four at most.
        Assert.Equal(["Capillaries are one cell thick", "Arteries carry blood away", "one two three", "four five six"], kept);
        Assert.Empty(AskQuotes.Clean(null));
        Assert.Empty(AskQuotes.Clean(JsonNode.Parse("\"just a string\"")));
        Assert.Empty(AskQuotes.Clean(JsonNode.Parse("[1, null, {\"a\": 2}]")));
    }

    [Theory]
    [InlineData(@"used to weight the values: $$\text{Attention}(Q,K,V)=\text{softmax}\left(\frac{QK^T}{\sqrt{d_k}}\right)V$$", "used to weight the values: Attention(Q,K,V)=softmax((QKT)/(√(dk)))V")]
    [InlineData(@"the cost is $\mathrm{O}(n^2 \cdot d)$ for each layer", "the cost is O(n² · d) for each layer")]
    [InlineData("Arteries carry blood away from the heart", "Arteries carry blood away from the heart")]
    [InlineData("it costs $5 and then $6 more", "it costs $5 and then $6 more")]
    public void A_chip_says_a_quotes_formula_as_plain_symbols(string quote, string label) => Assert.Equal(label, AskQuotes.Label(quote));

    [Theory]
    [InlineData(@"$$\frac{QK^T}{\sqrt{d_k}} = \sum_{i=1}^{n} x_i$$")]
    [InlineData(@"\frac{QK^T}{\sqrt{d_k}}\right)V$$")]
    public void A_quote_that_is_only_a_formula_makes_no_chip(string quote) => Assert.Null(AskQuotes.Label(quote));

    [Fact]
    public void A_half_written_reply_shows_the_answer_and_never_its_quotes()
    {
        string reply = """{"answer": "Arteries carry blood away.", "sources": [1], "quotes": ["Arteries carry blood away from the heart"]}""";
        for (int cut = 1; cut <= reply.Length; cut++)
            Assert.DoesNotContain("quotes", AskAnswer.SoFar(reply[..cut]) ?? "");
        Assert.Equal("Arteries carry blood away.", AskAnswer.SoFar(reply));
    }

    const string Reply = """{"answer":"Arteries carry blood away from the heart.","sources":[1],"quotes":["Arteries carry blood away from the heart under high pressure","not in the notes at all, but the library doesn't judge that"]}""";

    sealed class Library : IAsyncDisposable
    {
        readonly TempDir dir = new();
        readonly Store store;
        public ScriptedAi Claude { get; } = new("claude", "Claude") { Script = [new AiEvent("final", Reply)] };
        public TestSite Site { get; private set; } = null!;
        public AiRemote Remote => new("http://localhost", "pw", Site.Client);

        Library(Config cfg, Store store)
        {
            this.store = store;
        }

        public static async Task<Library> StartAsync()
        {
            var dir = new TempDir();
            var cfg = new Config(dir["home"], dir["pool"]) { PoolPassword = "pw", OllamaEnabled = true, Classes = [new ClassDef("BIO 110")] };
            var store = new Store(cfg.DbPath, cfg.PoolDir);
            store.Save(new Meeting("v1") { Title = "Blood vessels", Date = "2026-10-08", Transcript = "Arteries, capillaries and veins." },
                new Classification("BIO 110", 1, "folder"),
                "## Three kinds of vessel\n- **Arteries** carry blood away from the heart under high pressure.\n- Capillaries are one cell thick.", "Claude");
            var lib = new Library(cfg, store);
            var ai = new AiJobs(cfg.Home) { Providers = id => id == "claude" ? lib.Claude : new ScriptedAi(id), Checks = new FakeChecks().Installed("claude").Build() };
            lib.Site = await TestSite.StartAsync(b => LibraryWeb.Build(b, cfg, store, new Pipeline(cfg, store, log: _ => { }), new LibraryWebOptions
            {
                ListModels = _ => Task.FromResult<List<(string, double)>?>(null), Tailscale = () => new TailscaleInfo(),
                Latest = _ => Task.FromResult<Release?>(null), Ai = ai,
            }));
            lib.keep = dir;
            return lib;
        }

        TempDir? keep;

        public Task<AskReply?> AskAsync(string question, string? lecture, string? className = null, string chat = "new") =>
            Remote.AskAsync(new AskRequest(question) { Engine = "claude", Chat = chat, Place = new AskPlace(lecture is null ? "home" : "lecture", lecture, "BIO 110"), Lecture = lecture, Class = className },
                _ => { }, CancellationToken.None);

        public async ValueTask DisposeAsync()
        {
            await Site.DisposeAsync();
            store.Dispose();
            keep?.Dispose();
        }
    }

    [Fact]
    public async Task A_lecture_question_asks_for_quotes_and_they_are_kept_in_its_chat()
    {
        await using var lib = await Library.StartAsync();

        var reply = await lib.AskAsync("Which vessels carry blood away?", "v1");

        // Asked for among the instructions, with the notes' passages marked, and the answer text has none of it.
        string prompt = lib.Claude.Prompts[^1];
        Assert.Contains("Also return quotes", prompt);
        Assert.Contains("(notes)", prompt);
        Assert.Equal("Arteries carry blood away from the heart.", reply!.Answer);
        Assert.Equal(2, reply.Quotes.Count);
        Assert.Equal("Arteries carry blood away from the heart under high pressure", reply.Quotes[0]);
        // Kept with the answer: the chat opened again has them.
        var chat = (await lib.Remote.ChatAsync(reply.Chat!))!;
        Assert.Equal(reply.Quotes, chat.Messages![1].Quotes);
        Assert.Empty(chat.Messages[0].Quotes);
    }

    [Fact]
    public async Task A_question_about_a_class_or_everything_asks_for_none()
    {
        await using var lib = await Library.StartAsync();

        await lib.AskAsync("What kinds of vessel are there?", null, className: "BIO 110");

        Assert.DoesNotContain("Also return quotes", lib.Claude.Prompts[^1]);
    }

    [Fact]
    public async Task An_answer_from_an_AI_that_ignored_the_quotes_has_none()
    {
        await using var lib = await Library.StartAsync();
        lib.Claude.Script = [new AiEvent("final", """{"answer":"Arteries carry blood away.","sources":[1]}""")];

        var reply = await lib.AskAsync("Which vessels carry blood away?", "v1");

        Assert.Equal("Arteries carry blood away.", reply!.Answer);
        Assert.Empty(reply.Quotes);
    }
}
