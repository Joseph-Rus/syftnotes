using Avalonia.Headless.XUnit;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using StudyStash.App.ViewModels;
using StudyStash.Core;
using StudyStash.Core.Ai;
using StudyStash.Core.Tests;
using StudyStash.Library;

namespace StudyStash.App.Tests;

/// <summary>The Ask bar with chat history, against a library run here (its AI a stand-in): a lecture's page opens on
/// its latest conversation and the next question carries it on, the card closed or not; New chat starts another and the
/// old stays listed; a chat deleted from the list comes back with Undo.</summary>
public class AskChatsTests
{
    sealed class Library : IAsyncDisposable
    {
        public readonly TempDir Dir = new();
        public readonly ScriptedAi Claude = new("claude", "Claude") { Script = [new AiEvent("final", """{"answer":"Arteries, capillaries and veins.","sources":[1]}""")] };
        public Store Store = null!;
        public WebApplication Site = null!;
        readonly HttpClient http = new();

        public static async Task<Library> StartAsync()
        {
            var lib = new Library();
            var cfg = new Config(lib.Dir["home"], lib.Dir["pool"]) { PoolPassword = "pw", OllamaEnabled = true, Classes = [new ClassDef("BIO 110")] };
            lib.Store = new Store(cfg.DbPath, cfg.PoolDir);
            lib.Store.Save(new Meeting("v1") { Title = "Blood vessels", Date = "2026-10-08", Transcript = "Arteries, capillaries and veins." },
                new Classification("BIO 110", 1, "folder"), "## Three kinds of vessel\n- Arteries.\n- Capillaries.\n- Veins.", "Claude");
            var ai = new AiJobs(cfg.Home)
            {
                Providers = id => id == "claude" ? lib.Claude : new ScriptedAi(id), Checks = new FakeChecks().Installed("claude").Build(),
            };
            // On this computer, over a real socket on a port of its own.
            var builder = WebApplication.CreateSlimBuilder();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Logging.ClearProviders();
            lib.Site = LibraryWeb.Build(builder, cfg, lib.Store, new Pipeline(cfg, lib.Store, log: _ => { }), new LibraryWebOptions
            {
                ListModels = _ => Task.FromResult<List<(string, double)>?>(null), Tailscale = () => new TailscaleInfo(),
                Latest = _ => Task.FromResult<Release?>(null), Ai = ai,
            });
            await lib.Site.StartAsync();
            return lib;
        }

        /// <summary>The lecture's page, opened (again): a new Ask bar over the same library.</summary>
        public async Task<AiAskModel> PageAsync()
        {
            var ask = new AiAskModel(new AiRemote(Site.Urls.Single(), "pw", http))
            {
                LectureId = "v1", ClassName = "BIO 110", Place = new AskPlace("lecture", "v1", "BIO 110"),
            };
            await ask.Load();
            ask.Engine = "claude";
            return ask;
        }

        public async ValueTask DisposeAsync()
        {
            await Site.StopAsync();
            await Site.DisposeAsync();
            http.Dispose();
            Store.Dispose();
            Dir.Dispose();
        }
    }

    static async Task Ask(AiAskModel model, string question)
    {
        model.Question = question;
        await model.AskCommand.ExecuteAsync(null);
    }

    [AvaloniaFact]
    public async Task A_lectures_page_opens_on_its_latest_chat_and_the_next_question_carries_it_on()
    {
        await using var lib = await Library.StartAsync();
        var page = await lib.PageAsync();
        await Ask(page, "What are the three kinds of blood vessel?");
        string kept = page.ChatId!;
        Assert.Equal("What are the three kinds of blood vessel?", Assert.Single(page.Chats).Title);
        page.CloseAnswerCommand.Execute(null); // closing it keeps it

        // The page opened again (or the app restarted): the latest conversation is there, the card closed until asked in.
        var again = await lib.PageAsync();
        Assert.Equal(kept, again.ChatId);
        Assert.Equal(["What are the three kinds of blood vessel?"], again.Turns.Select(t => t.Question));
        Assert.Equal("Arteries, capillaries and veins.", again.Turns[0].Answer);
        Assert.False(again.HasLatest);
        await Ask(again, "And the second one?");
        Assert.True(again.HasLatest);
        Assert.Contains("Student: What are the three kinds of blood vessel?", lib.Claude.Prompts[^1]);
        Assert.Equal(kept, again.ChatId);
        Assert.Single(again.Chats);

        // New chat starts another; the first stays in the list, and picking it opens it to carry on.
        again.NewChatCommand.Execute(null);
        await Ask(again, "How is blood pressure written?");
        Assert.DoesNotContain("Student:", lib.Claude.Prompts[^1]);
        Assert.Equal(["How is blood pressure written?", "What are the three kinds of blood vessel?"], again.Chats.Select(c => c.Title));
        await again.Chats[1].Open.ExecuteAsync(null);
        Assert.Equal(kept, again.ChatId);
        Assert.Equal(2, again.Turns.Count);
        Assert.True(again.HasLatest);
        Assert.True(again.Chats[1].Current);
    }

    [AvaloniaFact]
    public async Task A_chat_is_renamed_and_deleted_from_the_list_and_Undo_brings_it_back()
    {
        await using var lib = await Library.StartAsync();
        var page = await lib.PageAsync();
        await Ask(page, "What are the three kinds of blood vessel?");
        var row = Assert.Single(page.Chats);

        row.Rename.Execute(null);
        await row.CommitRename.ExecuteAsync(null); // unchanged: nothing asked of the library
        row.Rename.Execute(null);
        row.NewTitle = "Vessels";
        await row.CommitRename.ExecuteAsync(null);
        Assert.Equal(("Vessels", false), (row.Title, row.Renaming));

        (string Title, Func<Task> Undo)? told = null;
        page.ToldDeleted = (title, undo) => told = (title, undo);
        await row.Delete.ExecuteAsync(null);
        Assert.Empty(page.Chats);
        Assert.Empty(page.Turns); // it was the one open: the card has nothing to show
        Assert.Null(page.ChatId);
        Assert.Equal("Vessels", told!.Value.Title);
        Assert.Empty((await lib.PageAsync()).Chats);

        await told.Value.Undo();
        Assert.Equal(["Vessels"], page.Chats.Select(c => c.Title));
        Assert.Single((await lib.PageAsync()).Turns);
    }
}
