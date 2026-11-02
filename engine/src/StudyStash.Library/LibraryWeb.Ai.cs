using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using StudyStash.Core;
using StudyStash.Core.Ai;

namespace StudyStash.Library;

/// <summary>
/// /api/v2/ai/*: each engine's state, who does what, and asking a question with the engine you pick. Rewrite and
/// tool-access join this in later tasks. Test injection goes through <see cref="LibraryWebOptions.Ai"/>: with it
/// set, every action below runs through the fakes it carries, never a real account, terminal, or Ollama.
/// </summary>
public sealed partial class LibraryWeb
{
    AiJobs? aiJobs;
    Rewrites? rewrites;

    AiJobs Jobs => options.Ai ?? (aiJobs ??= new AiJobs(cfg.Home, () => cfg.OllamaHost));
    Rewrites Rewrites => rewrites ??= new Rewrites(cfg, store, Jobs, diagrams: options.Diagrams);

    static IResult RewriteResult(Func<RewriteInfo> run)
    {
        try
        {
            return AiJson(run());
        }
        catch (RewriteRefusedException e)
        {
            return Http.Detail(e.Status, e.Message);
        }
    }

    async Task<AiOverview> AiOverviewAsync() =>
        Jobs.SignIns.Annotate((await Engines.StatusAsync(AiSettings.Load(cfg.Home), cfg, Jobs.Checks)) with { Pulling = Jobs.Pulling });

    static IResult AiJson<T>(T value) => Http.Json(JsonSerializer.SerializeToNode(value, AiRemote.Options));

    /// <summary>
    /// An answer as it's written, as newline-delimited JSON, each line sent the moment it's ready: <c>{"kind":
    /// "text", "text"}</c> for more of the answer, <c>{"kind": "answer", "text"}</c> for all of it so far (when what
    /// came before changed rather than grew), then <c>{"kind": "done", "reply"}</c> with the whole reply, or
    /// <c>{"kind": "error", "status", "detail"}</c> saying why it stopped. Leaving stops the engine too. A question in a
    /// kept chat says which first, <c>{"kind": "chat", "id"}</c> (a new one's id too), so the app knows it whatever
    /// comes of the answer: one stopped or cut off is kept there as well.
    /// </summary>
    async Task StreamAnswerAsync(HttpContext ctx, AskRequest request, Chat? chat, object? turn)
    {
        ctx.Response.ContentType = "application/x-ndjson; charset=utf-8";
        ctx.Response.Headers.CacheControl = "no-store";
        ctx.Response.Headers["X-Accel-Buffering"] = "no"; // a proxy in front of the library passes each line straight on
        ctx.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();
        await ctx.Response.StartAsync(ctx.RequestAborted);
        var lines = Channel.CreateUnbounded<JsonObject>(new UnboundedChannelOptions { SingleReader = true });
        if (chat is not null) lines.Writer.TryWrite(new JsonObject { ["kind"] = "chat", ["id"] = chat.Id });
        string sent = "";
        // The turn ends once: answered, cut off, or stopped. Stopped, what was written is kept and the chat is free
        // at once (the AI can take a moment longer to stop), so asking again straight away isn't told it's busy.
        var ending = new Lock();
        bool over = false;
        bool EndsNow()
        {
            lock (ending)
            {
                if (over) return false;
                over = true;
                return true;
            }
        }
        void KeepStopped()
        {
            if (chat is null || !EndsNow()) return;
            if (sent.Length > 0) LibraryChats.AddAskTurn(chat, request.Question, sent, stopped: true);
            if (turn is not null) LibraryChats.End(chat.Id, turn);
        }
        void SoFar(string answer)
        {
            lines.Writer.TryWrite(answer.StartsWith(sent, StringComparison.Ordinal)
                ? new JsonObject { ["kind"] = "text", ["text"] = answer[sent.Length..] }
                : new JsonObject { ["kind"] = "answer", ["text"] = answer });
            sent = answer;
        }
        var asking = Task.Run(async () =>
        {
            try
            {
                var reply = await Jobs.AskAsync(request, Reader, cfg, ctx.RequestAborted, SoFar);
                if (EndsNow()) reply = Kept(chat, request.Question, reply);
                lines.Writer.TryWrite(new JsonObject { ["kind"] = "done", ["reply"] = JsonSerializer.SerializeToNode(reply, AiRemote.Options) });
            }
            catch (Exception e) when (e is HttpRequestException or TimeoutException or InvalidOperationException or InvalidDataException)
            {
                // What came before it stopped is kept, as the app shows it.
                if (chat is not null && sent.Length > 0 && EndsNow()) LibraryChats.AddAskTurn(chat, request.Question, sent, stopped: true);
                lines.Writer.TryWrite(new JsonObject { ["kind"] = "error", ["status"] = 503, ["detail"] = e.Message });
            }
            catch (OperationCanceledException) when (ctx.RequestAborted.IsCancellationRequested)
            {
                KeepStopped(); // they stopped it, or went away: what was written by then is kept
            }
            finally
            {
                lines.Writer.Complete();
            }
        });
        try
        {
            // Whatever has queued up while the last lines went out goes out together, in one write.
            var batch = new StringBuilder();
            while (await lines.Reader.WaitToReadAsync(ctx.RequestAborted))
            {
                batch.Clear();
                while (lines.Reader.TryRead(out var line)) batch.Append(line.ToJsonString()).Append('\n');
                await ctx.Response.WriteAsync(batch.ToString(), Encoding.UTF8, ctx.RequestAborted);
                await ctx.Response.Body.FlushAsync(ctx.RequestAborted);
            }
        }
        catch (Exception e) when (e is OperationCanceledException or IOException)
        {
            // they went away: the answer stops with them, and the chat is theirs again at once
            KeepStopped();
        }
        await asking;
    }

    /// <summary>A turn answered, kept in its chat (when it's in one), and the reply saying which.</summary>
    AskReply Kept(Chat? chat, string question, AskReply reply)
    {
        if (chat is null) return reply;
        LibraryChats.AddAskTurn(chat, question, reply.Answer, reply);
        return reply with { Chat = chat.Id };
    }

    /// <summary>The kept chat a question goes on ("new": a new one where it says it's asked), or null when it isn't
    /// kept. A chat deleted meanwhile starts again as a new one there.</summary>
    Chat? AskChatFor(JsonObject? body)
    {
        if (Str(body, "chat") is not { } id) return null;
        AskPlace? place = body?["place"] is JsonObject p
            ? new AskPlace(Str(p, "scope") ?? "home", Str(p, "lecture"), Str(p, "class"))
            : null;
        return id != "new" && LibraryChats.Get(id) is { Kind: Chats.AskKind } kept ? kept : LibraryChats.NewAsk(place);
    }

    /// <summary>A follow-up's conversation so far, as the app sends it; null when there's none, or none that can be
    /// read (the question is then asked on its own, as a library before this did).</summary>
    static List<AskTurn>? AskHistory(JsonNode? history)
    {
        if (history is not JsonArray) return null;
        try
        {
            return history.Deserialize<List<AskTurn?>>(AiRemote.Options) is { } turns ? AskMemory.Last(turns, int.MaxValue, int.MaxValue) : null;
        }
        catch (Exception e) when (e is JsonException or NotSupportedException or InvalidOperationException)
        {
            return null;
        }
    }

    void MapAi(WebApplication app)
    {
        app.MapGet("/api/v2/ai/engines", Http.Handle(ctx => ApiAsync(ctx, async () => AiJson(await AiOverviewAsync()))));

        app.MapPost("/api/v2/ai/defaults", Http.Handle(ctx => ApiAsync(ctx, async () =>
        {
            var body = await Http.JsonBodyAsync(ctx.Request);
            string? notes = Str(body, "notes"), ask = Str(body, "ask"), diagrams = Str(body, "diagrams");
            bool? fallback = body?["fallback"] is JsonValue fv && fv.TryGetValue(out bool f) ? f : null;
            bool? Switch(string key) => body?[key] is JsonValue sv && sv.TryGetValue(out bool b) ? b : null;
            bool? rich = Switch("rich"), richDiagrams = Switch("rich_diagrams"), richPlots = Switch("rich_plots"), richDrawings = Switch("rich_drawings");
            string? speed = Str(body, "speed");
            if (speed is not null && !AiSpeed.Choices.Contains(speed)) return Http.Detail(400, $"there's no speed called {speed}");
            foreach (string? id in new[] { notes, ask })
                if (id is not null && !Engines.Order.Contains(id)) return Http.Detail(400, $"there's no AI called {id}");
            if (diagrams is not null && !DiagramEngines.Choices.Contains(diagrams)) return Http.Detail(400, $"there's no AI called {diagrams}");
            var overview = await AiOverviewAsync();
            foreach (string? id in new[] { notes, ask, diagrams })
                if (id is not null && overview.Engines.FirstOrDefault(e => e.Id == id) is { Installed: false } row)
                    return Http.Detail(409, $"{row.Name} isn't set up on your library's computer.");
            // The free AI picked for a library nobody chose a model for, whose built-in one isn't here: the model it
            // writes with now (one already on this computer) becomes the library's own.
            if (notes == "ollama" && !Engines.FreeModelChosen(cfg) && await Jobs.Checks.OllamaModels(cfg.OllamaHost) is { } have
                && Engines.UsableFreeModel(cfg, have) is { } usable && usable != cfg.EffectiveSummaryModel)
            {
                cfg.OllamaModel = usable;
                Configs.Save(cfg);
            }
            var settings = AiSettings.Load(cfg.Home);
            bool hadNotesAi = settings.NotesChosen;
            if (notes is not null) settings.ByJob["notes"] = new AiChoice(notes);
            if (ask is not null)
            {
                settings.ByJob["ask"] = new AiChoice(ask);
                settings.ByJob["agent"] = new AiChoice(ask); // the chat that answers questions follows "ask"
            }
            if (fallback is not null) settings.Fallback = fallback.Value;
            if (diagrams is not null) settings.Diagrams = diagrams;
            settings.SetRich(rich, richDiagrams, richPlots, richDrawings);
            if (speed is not null) settings.Speed = speed;
            settings.Save(cfg.Home);
            if (!hadNotesAi && settings.NotesChosen) WriteWaitingNotes();
            return AiJson(await AiOverviewAsync());
        })));

        app.MapPost("/api/v2/ai/engines/{id}/start", Http.Handle(ctx => ApiAsync(ctx, async () =>
        {
            string id = (string)ctx.Request.RouteValues["id"]!;
            if (id != "ollama") return Http.Detail(400, $"{Engines.Name(id)} doesn't start from here.");
            bool ok = await Jobs.Checks.StartOllama(cfg.OllamaHost);
            return AiJson(new AiSaid(ok ? "Ollama is starting." : "Ollama didn't start. Open the Ollama app and try again.", await AiOverviewAsync()));
        })));

        app.MapPost("/api/v2/ai/engines/{id}/download", Http.Handle(ctx => ApiAsync(ctx, async () =>
        {
            string id = (string)ctx.Request.RouteValues["id"]!;
            if (id != "ollama") return Http.Detail(400, $"{Engines.Name(id)} doesn't download a model here.");
            string model = cfg.EffectiveSummaryModel;
            _ = Jobs.DownloadAsync(model, cfg.OllamaHost); // runs in the background; AiOverview.Pulling shows it
            return AiJson(new AiSaid($"Downloading {model}…", await AiOverviewAsync()));
        })));

        // The free AI in one go: whatever it still needs on this computer (its app, starting it, its model).
        app.MapPost("/api/v2/ai/engines/{id}/set-up", Http.Handle(ctx => ApiAsync(ctx, async () =>
        {
            string id = (string)ctx.Request.RouteValues["id"]!;
            if (id != "ollama") return Http.Detail(400, $"{Engines.Name(id)} isn't set up from here.");
            // A library nobody chose a model for gets the one this computer can run, and keeps it.
            string model = Engines.FreeModel(cfg, Jobs.Checks);
            if (model != cfg.EffectiveSummaryModel)
            {
                cfg.OllamaModel = model;
                Configs.Save(cfg);
            }
            _ = Jobs.SetUpOllamaAsync(model, cfg.OllamaHost); // in the background; AiOverview.Pulling shows it
            return AiJson(new AiSaid("Getting the free AI ready…", await AiOverviewAsync()));
        })));

        app.MapPost("/api/v2/ai/engines/{id}/sign-in", Http.Handle(ctx => ApiAsync(ctx, async () =>
        {
            string id = (string)ctx.Request.RouteValues["id"]!;
            if (id == "ollama") return Http.Detail(400, "Ollama doesn't need to sign in: it's private, on this computer.");
            if (!IsLocal(ctx))
                return Http.Detail(409, $"Sign in on your library's computer: open {(OperatingSystem.IsWindows() ? "PowerShell" : "Terminal")} there and run {Engines.SignInWords(id)}");
            var body = await Http.JsonBodyAsync(ctx.Request);
            bool Asked(string key) => body?[key] is JsonValue v && v.TryGetValue(out bool b) && b;
            string said;
            try
            {
                // The last resort, once the hidden sign-in couldn't start or stopped at once: a terminal, as it always was.
                if (Asked("terminal")) said = Jobs.Checks.OpenSignIn(cfg.Home, AiSettings.Load(cfg.Home).Terminal, id);
                // Otherwise the AI's own sign-in, hidden: it opens the student's browser itself, and the engine's row says
                // "waiting" until its status command says signed in (then it's ready by itself).
                else if (Jobs.SignIns.Can(id)) said = Asked("again") ? Jobs.SignIns.Again(id) : Jobs.SignIns.Start(id);
                else throw new InvalidOperationException($"{Engines.Tool(id)} isn't installed on this computer.");
            }
            catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
            {
                return Http.Detail(409, e.Message);
            }
            return AiJson(new AiSaid(said, await AiOverviewAsync()));
        })));

        app.MapPost("/api/v2/ai/engines/{id}/check", Http.Handle(ctx => ApiAsync(ctx, async () =>
        {
            string id = (string)ctx.Request.RouteValues["id"]!;
            if (!Engines.Order.Contains(id)) return Http.Detail(400, $"there's no AI called {id}");
            string? model = Str(await Http.JsonBodyAsync(ctx.Request), "model");
            var (ok, why) = await Jobs.TestAsync(id, model ?? "");
            return AiJson(new AiSaid(ok ? $"{Engines.Name(id)} is ready." + Answered(why) : why, await AiOverviewAsync()));
        })));

        app.MapPost("/api/v2/ai/engines/{id}/key", Http.Handle(ctx => ApiAsync(ctx, async () =>
        {
            string id = (string)ctx.Request.RouteValues["id"]!;
            if (!ApiKeys.Engines.Contains(id)) return Http.Detail(400, $"{Engines.Name(id)} doesn't take an API key.");
            string key = Str(await Http.JsonBodyAsync(ctx.Request), "key") ?? "";
            ApiKeys.Set(cfg.Home, id, key);
            // What the last try said was about the command (or the old key): try again with what it has now.
            var settings = AiSettings.Load(cfg.Home);
            settings.Tests.Remove(id);
            settings.Limits.Remove(id);
            settings.Save(cfg.Home);
            if (key.Trim().Length == 0) return AiJson(new AiSaid($"{Engines.Name(id)}'s API key is gone.", await AiOverviewAsync()));
            var (ok, why) = await Jobs.TestAsync(id);
            return AiJson(new AiSaid(ok ? $"{Engines.Name(id)} works with your API key." + Answered(why) : $"The key was saved, but {Engines.Name(id)} said: {why}",
                await AiOverviewAsync()));
        })));

        // A check that worked with another model than the one asked: which answered, and why.
        static string Answered(string note) => note.Length > 0 ? $" It answered with {note}." : "";

        app.MapPost("/api/v2/ai/engines/{id}/model", Http.Handle(ctx => ApiAsync(ctx, async () =>
        {
            string id = (string)ctx.Request.RouteValues["id"]!;
            if (!Engines.Order.Contains(id)) return Http.Detail(400, $"there's no AI called {id}");
            string? model = Str(await Http.JsonBodyAsync(ctx.Request), "model");
            if (model is null) return Http.Detail(400, "which model?");
            if (id == "ollama")
            {
                cfg.SummaryModel = model;
                Configs.Save(cfg);
            }
            else
            {
                var settings = AiSettings.Load(cfg.Home);
                settings.Models[id] = model;
                settings.Save(cfg.Home);
            }
            return AiJson(await AiOverviewAsync());
        })));

        app.MapPost("/api/v2/ai/problems/{id}/dismiss", Http.Handle(ctx => ApiAsync(ctx, async () =>
        {
            string id = (string)ctx.Request.RouteValues["id"]!;
            var settings = AiSettings.Load(cfg.Home);
            if (!settings.Dismissed.Contains(id))
            {
                settings.Dismissed.Add(id);
                settings.Save(cfg.Home);
            }
            return AiJson(await AiOverviewAsync());
        })));

        app.MapPost("/api/v2/ai/ask", Http.Handle(ctx => ApiAsync(ctx, async () =>
        {
            var body = await Http.JsonBodyAsync(ctx.Request);
            string? question = Str(body, "question");
            if (question is null) return Http.Detail(400, "what's the question?");
            var request = new AskRequest(question)
            {
                Lecture = Str(body, "lecture"), Class = Str(body, "class"), Engine = Str(body, "engine"),
                Live = Str(body, "live"), LiveTitle = Str(body, "live_title"), History = AskHistory(body?["history"]),
                Agenda = AskAgenda(Str(body, "class")),
            };
            // A kept chat: the turns before this one come from it, and this one is kept in it. One turn at a time: a
            // question in a chat that's still answering one (asked on another device) waits its turn.
            var chat = AskChatFor(body);
            var turn = chat is null ? null : LibraryChats.TryBegin(chat.Id);
            if (chat is not null && turn is null)
                return Http.Detail(409, "This chat is still answering a question asked on another device. Ask again once that answer is done.");
            try
            {
                if (chat is not null) request = request with { History = Chats.AskTurns(chat) };
                if (body?["stream"] is JsonValue sv && sv.TryGetValue(out bool stream) && stream)
                {
                    await StreamAnswerAsync(ctx, request, chat, turn);
                    return Results.Empty;
                }
                try
                {
                    return AiJson(Kept(chat, question, await Jobs.AskAsync(request, Reader, cfg, ctx.RequestAborted)));
                }
                catch (Exception e) when (e is HttpRequestException or TimeoutException or InvalidOperationException or InvalidDataException)
                {
                    return Http.Detail(503, e.Message);
                }
            }
            finally
            {
                if (chat is not null && turn is not null) LibraryChats.End(chat.Id, turn);
            }
        })));

        // A student has started typing a question: the engine that will answer it gets ready (a local model loads).
        app.MapPost("/api/v2/ai/warm", Http.Handle(ctx => ApiAsync(ctx, async () =>
            Http.Json(new JsonObject { ["warming"] = Jobs.Warm(Str(await Http.JsonBodyAsync(ctx.Request), "engine"), cfg) }))));

        // Recipes: the questions kept to ask again (/study-guide), Syft Notes' own and the student's.
        app.MapGet("/api/v2/ai/recipes", Http.Handle(ctx => ApiAsync(ctx, () => Task.FromResult(AiJson(Recipes.All(cfg.Home))))));
        app.MapPost("/api/v2/ai/recipes", Http.Handle(ctx => ApiAsync(ctx, async () =>
        {
            var body = await Http.JsonBodyAsync(ctx.Request);
            if (Recipes.Save(cfg.Home, Str(body, "name") ?? "", Str(body, "prompt") ?? "") is not { } saved)
                return Http.Detail(400, "a recipe needs a name and what to ask");
            return AiJson(saved);
        })));
        app.MapDelete("/api/v2/ai/recipes/{id}", (HttpContext ctx, string id) => ApiAsync(ctx, () => Task.FromResult(
            Recipes.Delete(cfg.Home, id) ? AiJson(Recipes.All(cfg.Home)) : Http.Detail(404, "no recipe of yours is called that"))));

        app.MapGet("/api/v2/ai/rewrite/{lecture}", Http.Handle(ctx => ApiAsync(ctx, () =>
            Task.FromResult(RewriteResult(() => Rewrites.Get((string)ctx.Request.RouteValues["lecture"]!))))));

        app.MapPost("/api/v2/ai/rewrite/{lecture}", Http.Handle(ctx => ApiAsync(ctx, async () =>
        {
            string? engine = Str(await Http.JsonBodyAsync(ctx.Request), "engine");
            if (engine is null || !Engines.Order.Contains(engine)) return Http.Detail(400, $"there's no AI called {engine}");
            return RewriteResult(() => Rewrites.Start((string)ctx.Request.RouteValues["lecture"]!, engine));
        })));

        app.MapPost("/api/v2/ai/rewrite/{lecture}/cancel", Http.Handle(ctx => ApiAsync(ctx, () =>
            Task.FromResult(RewriteResult(() => Rewrites.Cancel((string)ctx.Request.RouteValues["lecture"]!))))));

        app.MapPost("/api/v2/ai/rewrite/{lecture}/keep", Http.Handle(ctx => ApiAsync(ctx, () =>
            Task.FromResult(RewriteResult(() => Rewrites.Keep((string)ctx.Request.RouteValues["lecture"]!))))));

        app.MapPost("/api/v2/ai/rewrite/{lecture}/use", Http.Handle(ctx => ApiAsync(ctx, () =>
            Task.FromResult(RewriteResult(() => Rewrites.Use((string)ctx.Request.RouteValues["lecture"]!))))));

        // The student's own edit of the notes: based_on is the fingerprint of the notes it started from (none: save
        // over whatever is there).
        app.MapPost("/api/v2/ai/rewrite/{lecture}/edit", Http.Handle(ctx => ApiAsync(ctx, async () =>
        {
            var body = await Http.JsonBodyAsync(ctx.Request);
            string? markdown = body?["markdown"] is JsonValue mv && mv.TryGetValue(out string? text) ? text : null;
            if (markdown is null) return Http.Detail(400, "what are the notes?");
            return RewriteResult(() => Rewrites.Edit((string)ctx.Request.RouteValues["lecture"]!, markdown, Str(body, "based_on")));
        })));

        // Notes an AI the library doesn't run wrote: an AI app through Syft Notes' connection (from "app", which the
        // student can switch off in Settings → AI apps, checked here on every save), or a chat the student pasted them
        // from ("paste"). replace: the lecture's own notes may be replaced (they're kept, to put back).
        app.MapPost("/api/v2/ai/rewrite/{lecture}/save", Http.Handle(ctx => ApiAsync(ctx, async () =>
        {
            var body = await Http.JsonBodyAsync(ctx.Request);
            string? markdown = body?["markdown"] is JsonValue mv && mv.TryGetValue(out string? text) ? text : null;
            if (markdown is null) return Http.Detail(400, "what are the notes?");
            bool fromApp = Str(body, "from") == "app";
            // Not a barrier: "from" is the caller's own word, and the library's password (which Syft Notes' own MCP
            // server holds) already lets anyone change notes through /edit. This only makes the switch in Settings → AI
            // apps hold at once for the connection that says it's an AI app, whatever its server last heard.
            if (fromApp && !Claude.ToolsOn) return Http.Detail(403, ToolAccess.Off);
            if (fromApp && !Claude.Reading.SaveNotes) return Http.Detail(403, ToolAccess.Refused("save"));
            bool replace = body?["replace"] is JsonValue rv && rv.TryGetValue(out bool r) && r;
            string writer = fromApp ? WriterSaid(Str(body, "writer")) : Engines.PastedWriter;
            return RewriteResult(() => Rewrites.Save((string)ctx.Request.RouteValues["lecture"]!, markdown, replace, writer, Str(body, "based_on"),
                fromApp ? NotesChangeKind.App : NotesChangeKind.Paste));
        })));

        // The notes' versions (History): every change, whatever made it, newest first; one version's words; putting one
        // back (version, or the one before the notes now) as a new version; Undo and Redo, each a version too.
        app.MapPost("/api/v2/ai/rewrite/{lecture}/restore", Http.Handle(ctx => ApiAsync(ctx, async () =>
        {
            var body = await Http.JsonBodyAsync(ctx.Request);
            return RewriteResult(() => Rewrites.Restore((string)ctx.Request.RouteValues["lecture"]!, Str(body, "version")));
        })));
        // expect: the version the app shows (none from an older app): changed elsewhere since, 412 and nothing undone.
        app.MapPost("/api/v2/ai/rewrite/{lecture}/undo", Http.Handle(ctx => ApiAsync(ctx, async () =>
        {
            var body = await Http.JsonBodyAsync(ctx.Request);
            return RewriteResult(() => Rewrites.Undo((string)ctx.Request.RouteValues["lecture"]!, Str(body, "expect")));
        })));
        app.MapPost("/api/v2/ai/rewrite/{lecture}/redo", Http.Handle(ctx => ApiAsync(ctx, async () =>
        {
            var body = await Http.JsonBodyAsync(ctx.Request);
            return RewriteResult(() => Rewrites.Redo((string)ctx.Request.RouteValues["lecture"]!, Str(body, "expect")));
        })));
        app.MapGet("/api/v2/ai/rewrite/{lecture}/versions", Http.Handle(ctx => ApiAsync(ctx, () =>
        {
            try
            {
                return Task.FromResult(AiJson(new { Versions = Rewrites.Versions((string)ctx.Request.RouteValues["lecture"]!) }));
            }
            catch (RewriteRefusedException e)
            {
                return Task.FromResult(Http.Detail(e.Status, e.Message));
            }
        })));
        app.MapGet("/api/v2/ai/rewrite/{lecture}/versions/{version}", Http.Handle(ctx => ApiAsync(ctx, () =>
        {
            try
            {
                return Task.FromResult(AiJson(Rewrites.Version((string)ctx.Request.RouteValues["lecture"]!, (string)ctx.Request.RouteValues["version"]!)));
            }
            catch (RewriteRefusedException e)
            {
                return Task.FromResult(Http.Detail(e.Status, e.Message));
            }
        })));

        // How the library writes this lecture's notes, for an AI it doesn't run: for=app (an AI app, through the
        // connection), or for=chat, with the whole request to copy into a free chat.
        app.MapGet("/api/v2/ai/rewrite/{lecture}/brief", Http.Handle(ctx => ApiAsync(ctx, () =>
        {
            bool chat = ctx.Request.Query["for"].ToString() != "app";
            try
            {
                return Task.FromResult(AiJson(Rewrites.Brief((string)ctx.Request.RouteValues["lecture"]!, chat)));
            }
            catch (RewriteRefusedException e)
            {
                return Task.FromResult(Http.Detail(e.Status, e.Message));
            }
        })));

        app.MapGet("/api/v2/ai/access", Http.Handle(ctx => ApiAsync(ctx, async () =>
        {
            await NoticeFunnelOffAsync(force: false);
            await NoticeTailscaleAsync(force: false);
            return AiJson(ToolAccessJson());
        })));
        app.MapPost("/api/v2/ai/access", Http.Handle(ctx => ApiAsync(ctx, async () =>
        {
            var body = await Http.JsonBodyAsync(ctx.Request);
            if (body?["on"] is JsonValue ov && ov.TryGetValue(out bool on)) Claude.ToolsOn = on;
            if (body?["reading"] is JsonObject r)
            {
                bool Flag(string key, bool was) => r[key] is JsonValue fv && fv.TryGetValue(out bool f) ? f : was;
                var was = Claude.Reading;
                Claude.Reading = new ReadingScopes(Flag("lectures", was.Lectures), Flag("notes", was.Notes), Flag("canvas", was.Canvas), Flag("audio", was.Audio),
                    Flag("save_notes", was.SaveNotes));
            }
            return AiJson(ToolAccessJson());
        })));

        // Claude and ChatGPT on the web: Funnel on or off through the library, so the laptop and the library both can,
        // and a Tailscale problem is part of the answer (shown where the switch is), not a refusal.
        app.MapPost("/api/v2/ai/access/web", Http.Handle(ctx => ApiAsync(ctx, async () =>
        {
            var body = await Http.JsonBodyAsync(ctx.Request);
            if (body?["on"] is not JsonValue ov || !ov.TryGetValue(out bool on)) return Http.Detail(400, "on or off?");
            if (on && cfg.PoolPassword.Length == 0) return Http.Detail(400, "Set a library password first, so only you can let an AI app in.");
            var (url, problem) = await Task.Run(() => options.Reach.Set(ClaudeWeb.PortFor(cfg), internet: true, on));
            // Off with no Tailscale at all: nothing is on the internet, so there's nothing left to turn off.
            if (!on && problem?.Kind == ReachKind.NotInstalled) problem = null;
            lock (web)
            {
                web.Problem = problem;
                web.StatusAt = Environment.TickCount64;
            }
            if (problem is null) Claude.PublicUrl = on ? url ?? "" : "";
            if (problem is null && on) await CheckWebAsync(ctx.RequestAborted);
            return AiJson(ToolAccessJson());
        })));
        app.MapPost("/api/v2/ai/access/web/check", Http.Handle(ctx => ApiAsync(ctx, async () =>
        {
            await NoticeFunnelOffAsync(force: true);
            await NoticeTailscaleAsync(force: true);
            await CheckWebAsync(ctx.RequestAborted);
            return AiJson(ToolAccessJson());
        })));
    }

    /// <summary>What the library knows about reaching it from the internet, beyond the address kept in claude.json:
    /// the last Tailscale problem and the last check, in memory only.</summary>
    sealed class WebState
    {
        public ReachProblem? Problem;
        public (string Url, bool Reachable, string Words, double At)? Check;
        public long StatusAt = long.MinValue / 2;
        /// <summary>What Tailscale still needs before Claude's address can go on (null: ready), and when it was asked.</summary>
        public ReachProblem? Needs;
        public long NeedsAt = long.MinValue / 2;
    }

    readonly WebState web = new();

    /// <summary>Funnel turned off outside the app leaves an address that goes nowhere: asks Tailscale (at most once a
    /// minute unless forced) and forgets the address when nothing is funnelled to the Claude port any more.</summary>
    async Task NoticeFunnelOffAsync(bool force)
    {
        if (Claude.PublicUrl.Length == 0) return;
        lock (web)
        {
            if (!force && Environment.TickCount64 - web.StatusAt < 60_000) return;
            web.StatusAt = Environment.TickCount64;
        }
        if (await Task.Run(() => options.Reach.Status(ClaudeWeb.PortFor(cfg))) == false) Claude.PublicUrl = "";
    }

    /// <summary>While Claude's address is off, what Tailscale still needs on this computer (asked at most once a minute
    /// unless forced), so Settings can say so before the switch is tried: a computer on its own, without Tailscale,
    /// is told what the web needs rather than shown an address that goes nowhere.</summary>
    async Task NoticeTailscaleAsync(bool force)
    {
        if (Claude.PublicUrl.Length > 0) return;
        lock (web)
        {
            if (!force && Environment.TickCount64 - web.NeedsAt < 60_000) return;
            web.NeedsAt = Environment.TickCount64;
        }
        ReachProblem? needs;
        try
        {
            needs = await Task.Run(options.Reach.Missing);
        }
        catch (InvalidOperationException)
        {
            needs = null;
        }
        lock (web) web.Needs = needs;
    }

    /// <summary>Checks the address from the internet (10 seconds at most) and keeps what it found.</summary>
    async Task CheckWebAsync(CancellationToken ct)
    {
        string url = Claude.PublicUrl;
        if (url.Length == 0) return;
        var (reachable, words) = await options.WebCheck.RunAsync(url, ct);
        lock (web) web.Check = (url, reachable, words, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0);
    }

    WebReach WebJson()
    {
        string url = Claude.PublicUrl;
        lock (web)
        {
            var check = web.Check is { } c && c.Url == url && url.Length > 0 ? c : ((string, bool, string, double)?)null;
            var needs = url.Length == 0 ? web.Needs : null;
            return new WebReach(url.Length > 0, "Syft Notes", url.Length > 0 ? url + ClaudeWeb.McpPath : null, web.Problem?.Words, web.Problem?.FixUrl,
                check?.Item2, check?.Item3, check?.Item4, cfg.PoolPassword.Length > 0)
            { Needs = needs?.Words, NeedsUrl = needs?.FixUrl };
        }
    }

    /// <summary>Who an AI app says wrote the notes it saves, as the notes will name them: one short line, or "your AI"
    /// when it says nothing.</summary>
    static string WriterSaid(string? writer)
    {
        string said = string.Join(" ", (writer ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return said.Length == 0 ? Engines.YourAi + " (through an AI app)" : Py.Head(said, 60);
    }

    ToolAccessInfo ToolAccessJson() => new(Claude.ToolsOn, Claude.Reading,
        [.. Claude.Grants().Select(g => new ToolConnection(g.Id, g.Name, g.Kind) { ClientHost = g.ClientHost, Created = g.Created, LastUsed = g.LastUsed > 0 ? g.LastUsed : null })])
    {
        PublicUrl = Claude.PublicUrl.Length > 0 ? Claude.PublicUrl : Claude.TailnetUrl,
        HasPassword = cfg.PoolPassword.Length > 0,
        Web = WebJson(),
    };
}
