using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace StudyStash.Core.Ai;

// The wire shapes for /api/v2/ai/*, shared by the library (which serializes them) and the app (which reads them
// through AiRemote): one JsonSerializerOptions (AiApi.Options, snake_case) on both sides, so the JSON keys always agree.

public sealed record ModelOption(string Id, string Label);

/// <summary>state: ready | unchecked | not_installed | not_signed_in | not_running | model_missing | limited | failed</summary>
public sealed record EngineInfo(string Id, string Name, string State)
{
    /// <summary>The free AI, while it still needs setting up: about how much there is to download for this computer,
    /// in GB (0 when that isn't known).</summary>
    public double SetUpGb { get; init; }
    /// <summary>The free AI on a computer with little memory: only the small model fits, and its notes are simpler.</summary>
    public bool Small { get; init; }
    public bool Installed { get; init; }
    public string Model { get; init; } = "";
    public List<ModelOption> Models { get; init; } = [];
    public string Site { get; init; } = "";
    public string Until { get; init; } = "";
    public string Why { get; init; } = "";
    /// <summary>The student gave it an API key (it runs on that when its command isn't here, or for plain answers).</summary>
    public bool HasKey { get; init; }
    /// <summary>The key's last four characters ("…a1b2"), to tell which one it is.</summary>
    public string KeyHint { get; init; } = "";
    /// <summary>Where the AI's own sign-in is while the student signs in from Syft Notes (<see cref="SignInPhase"/>:
    /// waiting | gave_up | region | stuck); "" when none is going, and from a library that signs in in a terminal.</summary>
    public string SignIn { get; init; } = "";
}

/// <summary>kind: engine_offline | not_signed_in | model_missing | usage_limit (the app adds fell_back,
/// library_offline, rewrite_failed and access_request itself: those never come from the library).</summary>
public sealed record AiProblemInfo(string Id, string Kind, string Engine, string EngineName)
{
    public string Until { get; init; } = "";
    public string Model { get; init; } = "";
    public double SizeGb { get; init; }
    public string FallbackTo { get; init; } = "";
    /// <summary>For "not signed in": where that AI's sign-in is (<see cref="EngineInfo.SignIn"/>).</summary>
    public string SignIn { get; init; } = "";
}

/// <summary>Something the library is downloading for the free AI on its computer: how far along, or why it stopped.
/// <see cref="Step"/> says which part: "app" (the Ollama app itself), "start" (starting it) or "model" (what it writes
/// notes with, the only step a library from before 0.14 has).</summary>
public sealed record PullInfo(string Model, double Fraction, string Why)
{
    public string Step { get; init; } = "model";
}

/// <summary>Rich notes as Settings shows them: <see cref="On"/> (diagrams, formula plots and drawings are added after the
/// notes; off, the notes are plain and nothing more is asked of the AI) and each kind that's switched on while it is.</summary>
public sealed record RichNotesInfo(bool On, bool Diagrams, bool Plots, bool Drawings);

public sealed record AiOverview(List<EngineInfo> Engines, string Notes, string Ask, bool Fallback, List<AiProblemInfo> Problems)
{
    public PullInfo? Pulling { get; init; }
    /// <summary>Who draws the notes' diagrams: auto | notes | off | an engine's id ("" from a library too old to say).</summary>
    public string Diagrams { get; init; } = "";
    /// <summary>The engine that choice comes to right now on the library ("" when nobody draws them).</summary>
    public string DiagramsBy { get; init; } = "";
    /// <summary>The rich notes switches; null from a library too old to have them.</summary>
    public RichNotesInfo? Rich { get; init; }
    /// <summary>How fast Claude Code writes the notes and designs the rich notes: standard | fast | quick (<see cref="AiSpeed"/>;
    /// "" from a library too old to have it).</summary>
    public string Speed { get; init; } = "";
}

/// <summary>What an action said, and the library's state right after doing it.</summary>
public sealed record AiSaid(string Said, AiOverview? Overview);

public sealed record AskRequest(string Question)
{
    public string? Lecture { get; init; }
    public string? Class { get; init; }
    public string? Engine { get; init; }
    public string? Live { get; init; }
    public string? LiveTitle { get; init; }
    /// <summary>The conversation so far, oldest first, when this is a follow-up and the library doesn't keep it
    /// (<see cref="Chat"/> unset): the last few turns, each cut short (<see cref="AskMemory"/>). A library too old to
    /// know it ignores it.</summary>
    public List<AskTurn>? History { get; init; }
    /// <summary>The kept conversation this question goes on: its id, or "new" to start one where <see cref="Place"/>
    /// says. The library reads the turns before it from the chat itself and keeps this one in it. Null: nothing kept
    /// (the phone, the quick panel). A library too old to keep chats ignores it, and its reply has no
    /// <see cref="AskReply.Chat"/>.</summary>
    public string? Chat { get; init; }
    public AskPlace? Place { get; init; }
    /// <summary>What the library knows that isn't in the notes, said to the AI with every question: today's date, the
    /// Due list and today's and tomorrow's calendar (LibraryWeb.AskAgenda). Never sent by the app; the library sets it.</summary>
    [System.Text.Json.Serialization.JsonIgnore] public string? Agenda { get; init; }
}

/// <summary>Where an Ask chat lives: a lecture's page ("lecture", with its folder), a folder's page ("class") or Home
/// ("home"). The questions in it may still look further (the scope chip): this is where it's listed.</summary>
public sealed record AskPlace(string Scope, string? Lecture = null, string? Class = null);

/// <summary>A kept Ask conversation, as a list shows it (<see cref="Messages"/> null) or whole.</summary>
public sealed record AskChat(string Id, string Title, string Updated)
{
    public string Kind { get; init; } = "";
    public string Scope { get; init; } = "";
    public string Class { get; init; } = "";
    public string Lecture { get; init; } = "";
    /// <summary>Its lecture's title, for a list on a folder's page or Home ("" when it has none, or it's gone).</summary>
    public string LectureTitle { get; init; } = "";
    public int Turns { get; init; }
    public List<AskChatMessage>? Messages { get; init; }
}

/// <summary>One message in a kept Ask conversation: a question ("user") or its answer ("assistant"), with what the
/// answer drew on and the AI that wrote it.</summary>
public sealed record AskChatMessage(string Role, string Text)
{
    public List<AskSource> Sources { get; init; } = [];
    /// <summary>The words of the lecture's notes the answer was built on (<see cref="AskQuotes"/>); empty for an answer
    /// that gave none, or from a library too old to.</summary>
    public List<string> Quotes { get; init; } = [];
    public string Engine { get; init; } = "";
    public string EngineName { get; init; } = "";
    public bool Failed { get; init; }
    public bool Stopped { get; init; }
}

public sealed record AskSource(string? Id, string Title, string? Class, string? Date, double? At, string Section);

/// <summary>An earlier question in the same conversation and the answer it got, sent along with a follow-up so the AI
/// knows what "that" or "the second one" means. Its sources are what the answer drew on: the follow-up reads them
/// again.</summary>
public sealed record AskTurn(string Question, string Answer)
{
    public List<AskSource> Sources { get; init; } = [];
}

public sealed record AskReply(string Answer, List<AskSource> Sources, string Engine, string EngineName)
{
    /// <summary>For a question about one lecture: the words of its notes the answer was built on, to highlight there
    /// (<see cref="AskQuotes"/>). Empty when it gave none; an older library has none.</summary>
    public List<string> Quotes { get; init; } = [];
    public string Asked { get; init; } = "";
    public string AskedName { get; init; } = "";
    public bool FellBack { get; init; }
    public string Why { get; init; } = "";
    /// <summary>How many of the conversation's earlier turns went to the AI with this question (the most recent ones;
    /// 0 for none). Null from a library too old to remember a conversation: it answered the question on its own.</summary>
    public int? Remembered { get; init; }
    /// <summary>The kept conversation this turn is in now (a new one's id when it started one). Null from a library too
    /// old to keep chats.</summary>
    public string? Chat { get; init; }
}

public sealed record NotesVersion(string Markdown, string By, string At);

/// <summary>A version of a lecture's notes (History): its id (to look at it or put it back), what made it (written,
/// rewrite, app, paste, edit, restore, undo, redo, diagrams, file), who wrote the notes ("Claude"; "" for the student's
/// own), when, and whether it's the notes now. <see cref="Markdown"/> only when one version is asked for.</summary>
public sealed record NotesKept(string Id, string Kind, string By, string At)
{
    public bool Current { get; init; }
    public string Markdown { get; init; } = "";
}

/// <summary>The last change to a lecture's notes, as the notes' Undo line says it: what made it, who, when, and
/// whether Undo (back to the notes before) and Redo (forward again, after an undo) can follow.</summary>
public sealed record NotesChange(string Id, string Kind, string By, string At)
{
    public bool CanUndo { get; init; }
    public bool CanRedo { get; init; }
}

/// <summary>state: none | working | ready | failed | cancelled</summary>
public sealed record RewriteInfo(string Lecture, string State)
{
    public string Engine { get; init; } = "";
    public string EngineName { get; init; } = "";
    public string Started { get; init; } = "";
    public string Error { get; init; } = "";
    public int Done { get; init; }
    public int Parts { get; init; }
    public NotesVersion? Current { get; init; }
    public NotesVersion? Draft { get; init; }
    /// <summary>"adding" while the designer adds the current notes' diagrams (they follow the notes, a few minutes
    /// later); "" otherwise, and from a library older than that.</summary>
    public string Diagrams { get; init; } = "";
    /// <summary>The last change to the lecture's notes (what made it, who, when), and whether Undo or Redo can follow
    /// it. Null with none kept, and from a library older than notes' versions.</summary>
    public NotesChange? Change { get; init; }
}

/// <summary>What AI apps may read while tool access is on, and <see cref="SaveNotes"/>: whether they may save the notes
/// a student asks them to write for a lecture (save_notes), on unless the student switches it off.</summary>
public sealed record ReadingScopes(bool Lectures = true, bool Notes = true, bool Canvas = true, bool Audio = false, bool SaveNotes = true);

/// <summary>How Syft Notes writes a lecture's notes (<see cref="Summarize.Brief"/>), for an AI it doesn't run itself,
/// and what "Copy for your AI" puts on the clipboard: the brief and the transcript, in one message or, for a lecture
/// too long for one, in <see cref="Parts"/> of their own.</summary>
public sealed record NotesBrief(string Brief, List<string> Parts);

public sealed record ToolConnection(string Id, string Name, string Kind)
{
    /// <summary>Where the app that signed in lives (e.g. "claude.ai"), when it said: "" for a token made in Settings.</summary>
    public string ClientHost { get; init; } = "";
    public double Created { get; init; }
    public double? LastUsed { get; init; }
}

/// <summary>Whether Claude on the web can reach the library: Funnel on or off, the address to paste into Claude
/// (<see cref="McpUrl"/>), what went wrong turning it on or off (<see cref="Problem"/>, with the page that fixes it),
/// and the last check from the internet (<see cref="Reachable"/>, in <see cref="Words"/>, at
/// <see cref="CheckedAt"/>, Unix seconds).</summary>
public sealed record WebReach(bool On, string Name, string? McpUrl, string? Problem, string? FixUrl,
    bool? Reachable, string? Words, double? CheckedAt, bool HasPassword)
{
    /// <summary>While it's off: what the library's computer needs first (Tailscale, installed and signed in, with a
    /// name), in words, and the page that gets it (<see cref="NeedsUrl"/>). Null when it's ready to turn on, or from
    /// a library that doesn't say.</summary>
    public string? Needs { get; init; }
    public string? NeedsUrl { get; init; }
}

public sealed record ToolAccessInfo(bool On, ReadingScopes Reading, List<ToolConnection> Connections)
{
    public string? PublicUrl { get; init; }
    public bool HasPassword { get; init; }
    /// <summary>Null from a library too old to put itself on the internet from here.</summary>
    public WebReach? Web { get; init; }
}

/// <summary>What the app's AI view models need from the library: engines and their state, the defaults, the
/// per-engine actions, and asking a question. Rewrite and tool-access members join this in later tasks. Every
/// member answers null when the library is too old to have the route (a plain 404, not one of ours).</summary>
public interface IAiLibrary
{
    Task<AiOverview?> EnginesAsync();
    Task<AiOverview?> DefaultsAsync(string? notes = null, string? ask = null, bool? fallback = null, string? diagrams = null);
    /// <summary>The rich notes switches and Claude Code's speed: whatever is given is changed (a library too old to have them
    /// answers null).</summary>
    Task<AiOverview?> RichAsync(bool? on = null, bool? diagrams = null, bool? plots = null, bool? drawings = null, string? speed = null) =>
        Task.FromResult<AiOverview?>(null);
    Task<AiSaid?> StartAsync(string engine);
    Task<AiSaid?> DownloadAsync(string engine);
    /// <summary>Gets an engine ready on the library's computer in one go, whatever it still needs (the free AI: its
    /// app installed, started, its model downloaded), in the background: <see cref="AiOverview.Pulling"/> says how far
    /// along. Null from a library too old to.</summary>
    Task<AiSaid?> SetUpAsync(string engine);
    /// <summary>Starts the AI's own sign-in on the library's computer, hidden: it opens the student's browser itself, and the
    /// engine's <see cref="EngineInfo.SignIn"/> says where it is until it turns ready by itself.</summary>
    Task<AiSaid?> SignInAsync(string engine);
    /// <summary>"Open the page again": the page opened once more, or the sign-in run once more, so the browser opens at a
    /// page that finishes by itself. Null from a library too old to. (The library still opens a terminal for an app
    /// from before every sign-in was hidden; this app never asks it to.)</summary>
    Task<AiSaid?> SignInAgainAsync(string engine) => Task.FromResult<AiSaid?>(null);
    Task<AiSaid?> CheckAsync(string engine, string model = "");
    Task<AiOverview?> ModelAsync(string engine, string model);
    Task<AiOverview?> DismissAsync(string problemId);
    /// <summary>Gives an engine an API key, or with "" takes it away. Null from a library too old to take keys.</summary>
    Task<AiSaid?> KeyAsync(string engine, string key) => Task.FromResult<AiSaid?>(null);
    Task<AskReply?> AskAsync(AskRequest request);
    /// <summary>Asks, with the answer shown as it's written: <paramref name="answerSoFar"/> hears all of it so far
    /// each time more arrives. <paramref name="stop"/> ends it where it is. A library too old to stream (or a
    /// stand-in that doesn't) answers once, at the end.</summary>
    Task<AskReply?> AskAsync(AskRequest request, Action<string> answerSoFar, CancellationToken stop) => AskAsync(request);
    /// <summary>Asks in a kept chat: <paramref name="inChat"/> hears first which one (a new one's id too), whatever
    /// comes of the answer after, so a turn stopped or cut off is carried on in the same chat. A library too old to
    /// keep chats never says.</summary>
    Task<AskReply?> AskAsync(AskRequest request, Action<string> answerSoFar, Action<string> inChat, CancellationToken stop) =>
        AskAsync(request, answerSoFar, stop);
    /// <summary>A student has started typing a question for <paramref name="engine"/> (null: the library's pick):
    /// the library gets that engine ready, so the answer starts sooner. Never fails: it's only a head start.</summary>
    Task WarmAsync(string? engine) => Task.CompletedTask;
    Task<RewriteInfo?> RewriteAsync(string lecture);
    Task<RewriteInfo?> RewriteStartAsync(string lecture, string engine);
    Task<RewriteInfo?> RewriteCancelAsync(string lecture);
    Task<RewriteInfo?> RewriteKeepAsync(string lecture);
    Task<RewriteInfo?> RewriteUseAsync(string lecture);
    /// <summary>Saves the lecture's notes as the student edited them by hand. <paramref name="basedOn"/> is the
    /// <see cref="Notes.Fingerprint"/> of the notes the edit started from: when the library has other notes by now it
    /// refuses (412) rather than save over them; null saves over whatever is there. Null from a library too old to
    /// take edits.</summary>
    Task<RewriteInfo?> EditNotesAsync(string lecture, string markdown, string? basedOn) => Task.FromResult<RewriteInfo?>(null);
    /// <summary>Saves notes the student pasted from a chat as the lecture's notes, tidied of what the chat put round
    /// them, the student's typed lines kept in them. A lecture with notes keeps them unless <paramref name="replace"/>
    /// (409); <paramref name="basedOn"/> as for an edit. Null from a library too old to take them.</summary>
    Task<RewriteInfo?> SaveNotesAsync(string lecture, string markdown, bool replace, string? basedOn) => Task.FromResult<RewriteInfo?>(null);
    /// <summary>Puts a version of the lecture's notes back as a new version (<paramref name="version"/>, a
    /// <see cref="NotesKept.Id"/>, or the one before the notes now). Null from a library too old to.</summary>
    Task<RewriteInfo?> RestoreNotesAsync(string lecture, string? version = null) => Task.FromResult<RewriteInfo?>(null);
    /// <summary>Undo: the notes before the last change, put back. <paramref name="expect"/>: the version the app shows
    /// (<see cref="NotesChange.Id"/>); changed elsewhere since, the library refuses (412) and nothing is undone. Null from
    /// a library too old to.</summary>
    Task<RewriteInfo?> UndoNotesAsync(string lecture, string? expect = null) => Task.FromResult<RewriteInfo?>(null);
    /// <summary>Redo: the notes the last undo went back from, put back (412 as for Undo). Null from a library too old to.</summary>
    Task<RewriteInfo?> RedoNotesAsync(string lecture, string? expect = null) => Task.FromResult<RewriteInfo?>(null);
    /// <summary>The versions of the lecture's notes, newest first (History). Null from a library too old to.</summary>
    Task<List<NotesKept>?> NotesVersionsAsync(string lecture) => Task.FromResult<List<NotesKept>?>(null);
    /// <summary>One version, with its words. Null when there's no such version, or the library is too old.</summary>
    Task<NotesKept?> NotesVersionAsync(string lecture, string version) => Task.FromResult<NotesKept?>(null);
    /// <summary>What to ask a free chat for the lecture's notes, the transcript with it (<see cref="NotesBrief"/>). Null
    /// from a library too old to say.</summary>
    Task<NotesBrief?> NotesBriefAsync(string lecture) => Task.FromResult<NotesBrief?>(null);
    Task<ToolAccessInfo?> AccessAsync();
    Task<ToolAccessInfo?> SetAccessAsync(bool? on = null, ReadingScopes? reading = null);
    /// <summary>Turns Funnel on or off on the library's computer, then checks it from the internet.</summary>
    Task<ToolAccessInfo?> SetWebAsync(bool on);
    /// <summary>Checks again whether the library answers from the internet.</summary>
    Task<ToolAccessInfo?> CheckWebAsync();
    /// <summary>The kept Ask chats on a lecture's page, a folder's (its own and its lectures') or, with neither, Home's
    /// (all of them), newest first. Empty from a library too old to keep them.</summary>
    Task<List<AskChat>> ChatsAsync(string? lecture, string? className) => Task.FromResult(new List<AskChat>());
    /// <summary>One kept chat, whole; null when it's gone.</summary>
    Task<AskChat?> ChatAsync(string id) => Task.FromResult<AskChat?>(null);
    Task<AskChat?> RenameChatAsync(string id, string title) => Task.FromResult<AskChat?>(null);
    /// <summary>Deletes a chat, kept a while for <see cref="RestoreChatAsync"/>; false when it was gone already.</summary>
    Task<bool> DeleteChatAsync(string id) => Task.FromResult(false);
    Task<bool> RestoreChatAsync(string id) => Task.FromResult(false);
    /// <summary>Every recipe the library has (<see cref="Recipes.All"/>). Empty from a library too old to have them,
    /// which then asks "/study-guide" as it's typed.</summary>
    Task<List<Recipe>> RecipesAsync() => Task.FromResult(new List<Recipe>());
    /// <summary>Keeps a recipe of the student's own; null from a library too old to.</summary>
    Task<Recipe?> SaveRecipeAsync(string name, string prompt) => Task.FromResult<Recipe?>(null);
    /// <summary>Deletes one of the student's own recipes: every recipe left, or null when it was gone already.</summary>
    Task<List<Recipe>?> DeleteRecipeAsync(string id) => Task.FromResult<List<Recipe>?>(null);
}

/// <summary>The library's AI over its API (/api/v2/ai), the way the Syft Notes app reads it. Like
/// <see cref="RemoteLibrary"/>: a plain 404 with no JSON detail means an older library without this route (null,
/// not an error); one of our own refusals (400/409/503/401) throws <see cref="LibraryRefusedException"/> with its
/// words.</summary>
public sealed class AiRemote(string serverUrl, Func<string> key, HttpClient? http = null) : IAiLibrary
{
    /// <summary>With a password that stays as it is for as long as this is used. (Settings stays open across a change
    /// of the library's password, and gives the one that says what it is now: with the old one kept, Your AI and AI
    /// apps said the library wasn't answering until Settings was closed and opened again.)</summary>
    public AiRemote(string serverUrl, string key, HttpClient? http = null) : this(serverUrl, () => key, http) { }

    /// <summary>snake_case both ways, so the library and the app agree on every key without translating by hand.</summary>
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    /// <summary>How long the app waits for the library to answer.</summary>
    public static readonly TimeSpan Waits = TimeSpan.FromSeconds(150);

    static readonly HttpClient Shared = new() { Timeout = Waits };
    readonly HttpClient client = http ?? Shared;
    readonly string server = serverUrl.TrimEnd('/');
    string Root => server + "/api/v2/ai";

    /// <summary>A path under /api/v2/ai, or, starting with "~", one of the library's own (~/api/v2/chats).</summary>
    HttpRequestMessage Request(HttpMethod method, string path, JsonNode? body)
    {
        var request = new HttpRequestMessage(method, path.StartsWith('~') ? server + path[1..] : Root + path);
        if (key() is { Length: > 0 } now) request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + now);
        if (body is not null) request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        return request;
    }

    async Task<JsonNode?> SendAsync(HttpMethod method, string path, JsonNode? body = null, CancellationToken ct = default)
    {
        using var request = Request(method, path, body);
        using var r = await client.SendAsync(request, ct);
        return Read(r, await r.Content.ReadAsStringAsync(ct));
    }

    /// <summary>What a reply says: its JSON; null for an older library without the route; our refusal, thrown.</summary>
    static JsonNode? Read(HttpResponseMessage r, string text)
    {
        JsonObject? asObject = text.Length > 0 && text.TrimStart().StartsWith('{') ? JsonNode.Parse(text) as JsonObject : null;
        if (r.StatusCode == HttpStatusCode.NotFound && asObject?["detail"] is null)
            return null; // an older library: this route doesn't exist there, not that nothing matched
        if (!r.IsSuccessStatusCode)
            throw new LibraryRefusedException((int)r.StatusCode, Py.AsString(asObject?["detail"]) ?? r.ReasonPhrase ?? "");
        return text.Length > 0 ? JsonNode.Parse(text) : null;
    }

    static T? As<T>(JsonNode? n) where T : class => n?.Deserialize<T>(Options);

    public async Task<AiOverview?> EnginesAsync() => As<AiOverview>(await SendAsync(HttpMethod.Get, "/engines"));

    public async Task<AiOverview?> DefaultsAsync(string? notes = null, string? ask = null, bool? fallback = null, string? diagrams = null)
    {
        var body = new JsonObject();
        if (notes is not null) body["notes"] = notes;
        if (ask is not null) body["ask"] = ask;
        if (fallback is not null) body["fallback"] = fallback.Value;
        if (diagrams is not null) body["diagrams"] = diagrams;
        return As<AiOverview>(await SendAsync(HttpMethod.Post, "/defaults", body));
    }

    public async Task<AiOverview?> RichAsync(bool? on = null, bool? diagrams = null, bool? plots = null, bool? drawings = null, string? speed = null)
    {
        var body = new JsonObject();
        if (on is not null) body["rich"] = on.Value;
        if (diagrams is not null) body["rich_diagrams"] = diagrams.Value;
        if (plots is not null) body["rich_plots"] = plots.Value;
        if (drawings is not null) body["rich_drawings"] = drawings.Value;
        if (speed is not null) body["speed"] = speed;
        return As<AiOverview>(await SendAsync(HttpMethod.Post, "/defaults", body));
    }

    static string Seg(string s) => Uri.EscapeDataString(s);

    public async Task<AiSaid?> StartAsync(string engine) => As<AiSaid>(await SendAsync(HttpMethod.Post, $"/engines/{Seg(engine)}/start"));

    public async Task<AiSaid?> DownloadAsync(string engine) => As<AiSaid>(await SendAsync(HttpMethod.Post, $"/engines/{Seg(engine)}/download"));

    public async Task<AiSaid?> SetUpAsync(string engine) => As<AiSaid>(await SendAsync(HttpMethod.Post, $"/engines/{Seg(engine)}/set-up"));

    public async Task<AiSaid?> SignInAsync(string engine) => As<AiSaid>(await SendAsync(HttpMethod.Post, $"/engines/{Seg(engine)}/sign-in"));

    public async Task<AiSaid?> SignInAgainAsync(string engine) =>
        As<AiSaid>(await SendAsync(HttpMethod.Post, $"/engines/{Seg(engine)}/sign-in", new JsonObject { ["again"] = true }));

    public async Task<AiSaid?> CheckAsync(string engine, string model = "") => As<AiSaid>(await SendAsync(HttpMethod.Post,
        $"/engines/{Seg(engine)}/check", model.Length > 0 ? new JsonObject { ["model"] = model } : null));

    public async Task<AiOverview?> ModelAsync(string engine, string model) =>
        As<AiOverview>(await SendAsync(HttpMethod.Post, $"/engines/{Seg(engine)}/model", new JsonObject { ["model"] = model }));

    public async Task<AiSaid?> KeyAsync(string engine, string key) =>
        As<AiSaid>(await SendAsync(HttpMethod.Post, $"/engines/{Seg(engine)}/key", new JsonObject { ["key"] = key }));

    public async Task<AiOverview?> DismissAsync(string problemId) =>
        As<AiOverview>(await SendAsync(HttpMethod.Post, $"/problems/{Seg(problemId)}/dismiss"));

    public async Task<AskReply?> AskAsync(AskRequest request) =>
        As<AskReply>(await SendAsync(HttpMethod.Post, "/ask", JsonSerializer.SerializeToNode(request, Options)));

    /// <summary>The answer as the library streams it (see the library's <c>/api/v2/ai/ask</c> with <c>stream</c>),
    /// read a line at a time as each arrives. Stopping closes the connection, which stops the engine too.</summary>
    public Task<AskReply?> AskAsync(AskRequest request, Action<string> answerSoFar, CancellationToken stop) => AskAsync(request, answerSoFar, _ => { }, stop);

    public async Task<AskReply?> AskAsync(AskRequest request, Action<string> answerSoFar, Action<string> inChat, CancellationToken stop)
    {
        var body = JsonSerializer.SerializeToNode(request, Options)!.AsObject();
        body["stream"] = true;
        using var message = Request(HttpMethod.Post, "/ask", body);
        using var r = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, stop);
        if (r.Content.Headers.ContentType?.MediaType != "application/x-ndjson")
            return As<AskReply>(Read(r, await r.Content.ReadAsStringAsync(stop))); // an older library answers once, at the end
        using var lines = new StreamReader(await r.Content.ReadAsStreamAsync(stop), Encoding.UTF8);
        var answer = new StringBuilder();
        while (await lines.ReadLineAsync(stop) is { } line)
        {
            if (line.Length == 0 || JsonNode.Parse(line) is not JsonObject said) continue;
            string text = Py.AsString(said["text"]) ?? "";
            switch (Py.AsString(said["kind"]))
            {
                case "text":
                    answerSoFar(answer.Append(text).ToString());
                    break;
                case "answer":
                    answerSoFar(answer.Clear().Append(text).ToString());
                    break;
                case "chat" when Py.AsString(said["id"]) is { Length: > 0 } id:
                    inChat(id);
                    break;
                case "done":
                    return As<AskReply>(said["reply"]);
                case "error":
                    throw new LibraryRefusedException(said["status"] is JsonValue sv && sv.TryGetValue(out int status) ? status : 503,
                        Py.AsString(said["detail"]) ?? "");
            }
        }
        throw new HttpRequestException("The library stopped answering partway through.");
    }

    public async Task WarmAsync(string? engine)
    {
        try
        {
            await SendAsync(HttpMethod.Post, "/warm", new JsonObject { ["engine"] = engine });
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or LibraryRefusedException or JsonException)
        {
            // only ever a head start: the question itself says what's wrong
        }
    }

    public async Task<RewriteInfo?> RewriteAsync(string lecture) =>
        As<RewriteInfo>(await SendAsync(HttpMethod.Get, $"/rewrite/{Seg(lecture)}"));

    public async Task<RewriteInfo?> RewriteStartAsync(string lecture, string engine) => As<RewriteInfo>(await SendAsync(
        HttpMethod.Post, $"/rewrite/{Seg(lecture)}", new JsonObject { ["engine"] = engine }));

    public async Task<RewriteInfo?> RewriteCancelAsync(string lecture) =>
        As<RewriteInfo>(await SendAsync(HttpMethod.Post, $"/rewrite/{Seg(lecture)}/cancel"));

    public async Task<RewriteInfo?> RewriteKeepAsync(string lecture) =>
        As<RewriteInfo>(await SendAsync(HttpMethod.Post, $"/rewrite/{Seg(lecture)}/keep"));

    public async Task<RewriteInfo?> RewriteUseAsync(string lecture) =>
        As<RewriteInfo>(await SendAsync(HttpMethod.Post, $"/rewrite/{Seg(lecture)}/use"));

    public async Task<RewriteInfo?> EditNotesAsync(string lecture, string markdown, string? basedOn) => As<RewriteInfo>(await SendAsync(
        HttpMethod.Post, $"/rewrite/{Seg(lecture)}/edit", new JsonObject { ["markdown"] = markdown, ["based_on"] = basedOn }));

    public async Task<RewriteInfo?> SaveNotesAsync(string lecture, string markdown, bool replace, string? basedOn) => As<RewriteInfo>(await SendAsync(
        HttpMethod.Post, $"/rewrite/{Seg(lecture)}/save", new JsonObject { ["markdown"] = markdown, ["replace"] = replace, ["based_on"] = basedOn, ["from"] = "paste" }));

    public async Task<RewriteInfo?> RestoreNotesAsync(string lecture, string? version = null) =>
        As<RewriteInfo>(await SendAsync(HttpMethod.Post, $"/rewrite/{Seg(lecture)}/restore", new JsonObject { ["version"] = version }));

    public async Task<RewriteInfo?> UndoNotesAsync(string lecture, string? expect = null) =>
        As<RewriteInfo>(await SendAsync(HttpMethod.Post, $"/rewrite/{Seg(lecture)}/undo", new JsonObject { ["expect"] = expect }));

    public async Task<RewriteInfo?> RedoNotesAsync(string lecture, string? expect = null) =>
        As<RewriteInfo>(await SendAsync(HttpMethod.Post, $"/rewrite/{Seg(lecture)}/redo", new JsonObject { ["expect"] = expect }));

    public async Task<List<NotesKept>?> NotesVersionsAsync(string lecture) =>
        (await SendAsync(HttpMethod.Get, $"/rewrite/{Seg(lecture)}/versions"))?["versions"]?.Deserialize<List<NotesKept>>(Options);

    public async Task<NotesKept?> NotesVersionAsync(string lecture, string version) =>
        As<NotesKept>(await SendAsync(HttpMethod.Get, $"/rewrite/{Seg(lecture)}/versions/{Seg(version)}"));

    public async Task<NotesBrief?> NotesBriefAsync(string lecture) =>
        As<NotesBrief>(await SendAsync(HttpMethod.Get, $"/rewrite/{Seg(lecture)}/brief?for=chat"));

    public async Task<ToolAccessInfo?> AccessAsync() => As<ToolAccessInfo>(await SendAsync(HttpMethod.Get, "/access"));

    public async Task<ToolAccessInfo?> SetAccessAsync(bool? on = null, ReadingScopes? reading = null)
    {
        var body = new JsonObject();
        if (on is not null) body["on"] = on.Value;
        if (reading is not null) body["reading"] = JsonSerializer.SerializeToNode(reading, Options);
        return As<ToolAccessInfo>(await SendAsync(HttpMethod.Post, "/access", body));
    }

    public async Task<ToolAccessInfo?> SetWebAsync(bool on) =>
        As<ToolAccessInfo>(await SendAsync(HttpMethod.Post, "/access/web", new JsonObject { ["on"] = on }));

    public async Task<ToolAccessInfo?> CheckWebAsync() => As<ToolAccessInfo>(await SendAsync(HttpMethod.Post, "/access/web/check"));

    /// <summary>A library too old to keep Ask chats lists only the ones it has (none with kind "ask"): an empty list.</summary>
    public async Task<List<AskChat>> ChatsAsync(string? lecture, string? className)
    {
        string query = "?kind=ask" + (lecture is not null ? "&lecture=" + Seg(lecture) : className is not null ? "&class=" + Seg(className) : "");
        var list = As<List<AskChat>>(await SendAsync(HttpMethod.Get, "~/api/v2/chats" + query)) ?? [];
        return [.. list.Where(c => c.Kind == "ask")];
    }

    public async Task<AskChat?> ChatAsync(string id)
    {
        try
        {
            return As<AskChat>(await SendAsync(HttpMethod.Get, $"~/api/v2/chats/{Seg(id)}"));
        }
        catch (LibraryRefusedException e) when (e.Status == 404)
        {
            return null; // deleted meanwhile
        }
    }

    public async Task<AskChat?> RenameChatAsync(string id, string title) =>
        As<AskChat>(await SendAsync(HttpMethod.Post, $"~/api/v2/chats/{Seg(id)}", new JsonObject { ["title"] = title }));

    public async Task<bool> DeleteChatAsync(string id)
    {
        try
        {
            return await SendAsync(HttpMethod.Delete, $"~/api/v2/chats/{Seg(id)}") is not null;
        }
        catch (LibraryRefusedException e) when (e.Status == 404)
        {
            return false;
        }
    }

    public async Task<bool> RestoreChatAsync(string id)
    {
        try
        {
            return await SendAsync(HttpMethod.Post, $"~/api/v2/chats/{Seg(id)}/restore") is not null;
        }
        catch (LibraryRefusedException e) when (e.Status == 404)
        {
            return false;
        }
    }

    public async Task<List<Recipe>> RecipesAsync() => As<List<Recipe>>(await SendAsync(HttpMethod.Get, "/recipes")) ?? [];

    public async Task<Recipe?> SaveRecipeAsync(string name, string prompt) =>
        As<Recipe>(await SendAsync(HttpMethod.Post, "/recipes", new JsonObject { ["name"] = name, ["prompt"] = prompt }));

    public async Task<List<Recipe>?> DeleteRecipeAsync(string id)
    {
        try
        {
            return As<List<Recipe>>(await SendAsync(HttpMethod.Delete, $"/recipes/{Seg(id)}"));
        }
        catch (LibraryRefusedException e) when (e.Status == 404)
        {
            return null;
        }
    }
}
