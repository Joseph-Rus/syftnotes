using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
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
    Rewrites Rewrites => rewrites ??= new Rewrites(cfg, store, Jobs);

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
        (await Engines.StatusAsync(AiSettings.Load(cfg.Home), cfg, Jobs.Checks)) with { Pulling = Jobs.Pulling };

    static IResult AiJson<T>(T value) => Http.Json(JsonSerializer.SerializeToNode(value, AiRemote.Options));

    void MapAi(WebApplication app)
    {
        app.MapGet("/api/v2/ai/engines", Http.Handle(ctx => ApiAsync(ctx, async () => AiJson(await AiOverviewAsync()))));

        app.MapPost("/api/v2/ai/defaults", Http.Handle(ctx => ApiAsync(ctx, async () =>
        {
            var body = await Http.JsonBodyAsync(ctx.Request);
            string? notes = Str(body, "notes"), ask = Str(body, "ask");
            bool? fallback = body?["fallback"] is JsonValue fv && fv.TryGetValue(out bool f) ? f : null;
            foreach (string? id in new[] { notes, ask })
                if (id is not null && !Engines.Order.Contains(id)) return Http.Detail(400, $"there's no AI called {id}");
            var overview = await AiOverviewAsync();
            foreach (string? id in new[] { notes, ask })
                if (id is not null && overview.Engines.First(e => e.Id == id) is { Installed: false } row)
                    return Http.Detail(409, $"{row.Name} isn't installed on your library's computer.");
            var settings = AiSettings.Load(cfg.Home);
            if (notes is not null) settings.ByJob["notes"] = new AiChoice(notes);
            if (ask is not null)
            {
                settings.ByJob["ask"] = new AiChoice(ask);
                settings.ByJob["agent"] = new AiChoice(ask); // the chat that answers questions follows "ask"
            }
            if (fallback is not null) settings.Fallback = fallback.Value;
            settings.Save(cfg.Home);
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

        app.MapPost("/api/v2/ai/engines/{id}/sign-in", Http.Handle(ctx => ApiAsync(ctx, async () =>
        {
            string id = (string)ctx.Request.RouteValues["id"]!;
            if (id == "ollama") return Http.Detail(400, "Ollama doesn't need to sign in: it's private, on this computer.");
            if (!IsLocal(ctx))
                return Http.Detail(409, $"Sign in on your library's computer: open Terminal there and run {Engines.SignInWords(id)}.");
            string said;
            try
            {
                said = Jobs.Checks.OpenSignIn(cfg.Home, AiSettings.Load(cfg.Home).Terminal, id);
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
            return AiJson(new AiSaid(ok ? $"{Engines.Name(id)} is ready." : why, await AiOverviewAsync()));
        })));

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
                Live = Str(body, "live"), LiveTitle = Str(body, "live_title"),
            };
            try
            {
                return AiJson(await Jobs.AskAsync(request, Reader, cfg, ctx.RequestAborted));
            }
            catch (Exception e) when (e is HttpRequestException or TimeoutException or InvalidOperationException or InvalidDataException)
            {
                return Http.Detail(503, e.Message);
            }
        })));

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

        app.MapGet("/api/v2/ai/access", Http.Handle(ctx => ApiAsync(ctx, async () =>
        {
            await NoticeFunnelOffAsync(force: false);
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
                Claude.Reading = new ReadingScopes(Flag("lectures", was.Lectures), Flag("notes", was.Notes), Flag("canvas", was.Canvas), Flag("audio", was.Audio));
            }
            return AiJson(ToolAccessJson());
        })));

        // Claude on the web: Funnel on or off through the library, so the laptop and the library both can, and a
        // Tailscale problem is part of the answer (shown where the switch is), not a refusal.
        app.MapPost("/api/v2/ai/access/web", Http.Handle(ctx => ApiAsync(ctx, async () =>
        {
            var body = await Http.JsonBodyAsync(ctx.Request);
            if (body?["on"] is not JsonValue ov || !ov.TryGetValue(out bool on)) return Http.Detail(400, "on or off?");
            if (on && cfg.PoolPassword.Length == 0) return Http.Detail(400, "Set a library password first, so only you can let Claude in.");
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
            return new WebReach(url.Length > 0, "Study Stash", url.Length > 0 ? url + ClaudeWeb.McpPath : null, web.Problem?.Words, web.Problem?.FixUrl,
                check?.Item2, check?.Item3, check?.Item4, cfg.PoolPassword.Length > 0);
        }
    }

    ToolAccessInfo ToolAccessJson() => new(Claude.ToolsOn, Claude.Reading,
        [.. Claude.Grants().Select(g => new ToolConnection(g.Id, g.Name, g.Kind) { ClientHost = g.ClientHost, Created = g.Created, LastUsed = g.LastUsed > 0 ? g.LastUsed : null })])
    {
        PublicUrl = Claude.PublicUrl.Length > 0 ? Claude.PublicUrl : Claude.TailnetUrl,
        HasPassword = cfg.PoolPassword.Length > 0,
        Web = WebJson(),
    };
}
