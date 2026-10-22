using System.Globalization;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace StudyStash.Core.Ai;

/// <summary>One message in a chat: what was said, which tools the AI used, and what it changed (with the change's id
/// for Undo).</summary>
public sealed record ChatMessage(string Role, string Text, string When)
{
    public List<string> Tools { get; init; } = [];
    public List<string> Changed { get; init; } = [];
    public string Change { get; init; } = "";
    public bool Failed { get; init; }
    /// <summary>An Ask answer's: what it drew on (each moment a link in the app), the AI that wrote it, and whether
    /// the student stopped it partway.</summary>
    public List<AskSource> Sources { get; init; } = [];
    public string Engine { get; init; } = "";
    public string EngineName { get; init; } = "";
    public bool Stopped { get; init; }
}

/// <summary>A conversation with the library's AI: about everything, one class, or one lecture.</summary>
public sealed class Chat
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    /// <summary>"ask": a conversation in the app's Ask bar (answers from passages, kept as history); "" one with the
    /// AI that reads the whole library (the browser's chat page).</summary>
    public string Kind { get; set; } = "";
    /// <summary>An Ask chat's place: "lecture" (a note's page, or the recording's chat), "class" (a folder's page) or
    /// "home".</summary>
    public string Scope { get; set; } = "";
    public string ClassName { get; set; } = "";
    public string Lecture { get; set; } = "";
    /// <summary>The AI that holds the conversation, and its own id for it (a follow-up continues it).</summary>
    public string Provider { get; set; } = "";
    public string Session { get; set; } = "";
    public string Updated { get; set; } = "";
    public List<ChatMessage> Messages { get; set; } = [];
}

/// <summary>A chat in a line, as lists show it: kept in chats/.index.json so a list never reads every chat.</summary>
public sealed record ChatEntry(string Id, string Title, string Kind, string Scope, string ClassName, string Lecture, string Updated, int Turns);

/// <summary>
/// Chatting with the library: the AI picked for agent work reads the whole library (lectures, Canvas, the files you
/// let it read) with the library's tools, answers with its sources, and remembers the conversation for follow-ups.
/// With edits allowed it may also write files in the library (a study guide, a cheat sheet), each turn kept as one
/// change in <see cref="History"/> so it can be undone. Chats are kept in chats/ beside config.toml.
/// </summary>
public sealed class Chats(string home, string library, AiJobs ai, History history, Func<IReadOnlyList<string>>? readDirs = null)
{
    static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
    readonly Lock gate = new();

    string Dir => Path.Combine(home, "chats");
    string PathOf(string id) => Path.Combine(Dir, id + ".json");
    /// <summary>Deleted chats, kept a while so Undo can bring one back.</summary>
    string TrashDir => Path.Combine(Dir, ".trash");

    public const string AskKind = "ask";

    public static bool GoodId(string id) => id.Length is > 0 and <= 40 && id.All(c => char.IsAsciiLetterOrDigit(c) || c == '-');

    /// <summary>The list of chats in a line each (<see cref="ChatEntry"/>), so listing them never reads every chat:
    /// chats/.index.json, made again from the chats themselves whenever it's missing.</summary>
    static string IndexIn(string home) => Path.Combine(home, "chats", ".index.json");

    /// <summary>Something changed chat files without this class (a class renamed, chats brought from another library):
    /// the index is made again the next time chats are listed.</summary>
    public static void IndexChanged(string home)
    {
        try
        {
            File.Delete(IndexIn(home));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    static ChatEntry EntryOf(Chat c) => new(c.Id, c.Title, c.Kind, c.Scope, c.ClassName, c.Lecture, c.Updated, c.Messages.Count(m => m.Role == "user"));

    /// <summary>Every chat in a line, newest first.</summary>
    public List<ChatEntry> Entries()
    {
        lock (gate)
        {
            if (File.Exists(IndexIn(home)))
                try
                {
                    if (JsonSerializer.Deserialize<List<ChatEntry>>(File.ReadAllText(IndexIn(home)), Options) is { } kept) return Newest(kept);
                }
                catch (JsonException)
                {
                    // made again below
                }
            var all = Directory.Exists(Dir)
                ? Directory.EnumerateFiles(Dir, "*.json").Select(f => Get(Path.GetFileNameWithoutExtension(f))).OfType<Chat>().Select(EntryOf).ToList()
                : [];
            WriteIndex(all);
            return Newest(all);
        }
    }

    static List<ChatEntry> Newest(IEnumerable<ChatEntry> entries) => [.. entries.OrderByDescending(e => e.Updated, StringComparer.Ordinal)];

    void WriteIndex(List<ChatEntry> entries)
    {
        if (!Directory.Exists(Dir)) return;
        File.WriteAllText(IndexIn(home), JsonSerializer.Serialize(entries, Options));
    }

    void Indexed(Func<List<ChatEntry>, List<ChatEntry>> change)
    {
        var now = change([.. Entries()]);
        WriteIndex(now);
    }

    /// <summary>The Ask chats on a note's page (that lecture's), a folder's (its own and its lectures'), or Home (all
    /// of them), newest first. A lecture's chats go by the folder the lecture is in now (<paramref name="classOf"/>, by
    /// its id): it may have moved since, or been a recording in no folder when the chat began. Each comes with that
    /// class.</summary>
    public List<ChatEntry> AskChats(string? lecture = null, string? className = null, Func<string, string?>? classOf = null)
    {
        lock (gate) EmptyTrash(DateTime.UtcNow - TrashKeeps); // what's waited long enough for Undo goes
        return [.. Entries().Where(e => e.Kind == AskKind)
            .Select(e => e.Lecture.Length > 0 && classOf?.Invoke(e.Lecture) is { } now ? e with { ClassName = now } : e)
            .Where(e => lecture is not null ? e.Lecture == lecture : className is null || e.ClassName == className)];
    }

    public Chat? Get(string id)
    {
        if (!GoodId(id) || !File.Exists(PathOf(id))) return null;
        try
        {
            return JsonSerializer.Deserialize<Chat>(File.ReadAllText(PathOf(id)), Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public void Save(Chat c)
    {
        lock (gate)
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(PathOf(c.Id), JsonSerializer.Serialize(c, Options));
            Indexed(all => [.. all.Where(e => e.Id != c.Id), EntryOf(c)]);
        }
    }

    /// <summary>How long a deleted chat waits, so the app's Undo can bring it back.</summary>
    public static readonly TimeSpan TrashKeeps = TimeSpan.FromDays(1);

    /// <summary>A chat deleted: out of every list at once, and kept a while (<see cref="TrashKeeps"/>) for
    /// <see cref="Restore"/>.</summary>
    public bool Delete(string id)
    {
        lock (gate)
        {
            if (!GoodId(id) || !File.Exists(PathOf(id))) return false;
            Directory.CreateDirectory(TrashDir);
            string to = Path.Combine(TrashDir, id + ".json");
            File.Move(PathOf(id), to, overwrite: true);
            File.SetLastWriteTimeUtc(to, DateTime.UtcNow);
            Indexed(all => [.. all.Where(e => e.Id != id)]);
            EmptyTrash(DateTime.UtcNow - TrashKeeps);
            return true;
        }
    }

    /// <summary>A deleted chat back, as it was; false once it's gone for good.</summary>
    public bool Restore(string id)
    {
        lock (gate)
        {
            string from = Path.Combine(TrashDir, id + ".json");
            if (!GoodId(id) || !File.Exists(from) || File.Exists(PathOf(id))) return false;
            File.Move(from, PathOf(id));
            if (Get(id) is { } back) Indexed(all => [.. all.Where(e => e.Id != id), EntryOf(back)]);
            return true;
        }
    }

    void EmptyTrash(DateTime before)
    {
        if (!Directory.Exists(TrashDir)) return;
        foreach (string f in Directory.EnumerateFiles(TrashDir, "*.json"))
            if (File.GetLastWriteTimeUtc(f) < before) File.Delete(f);
    }

    /// <summary>A chat called something else (its title starts as its first question).</summary>
    public Chat? Rename(string id, string title)
    {
        lock (gate)
        {
            if (Get(id) is not { } c) return null;
            c.Title = title;
            Save(c);
            return c;
        }
    }

    /// <summary>A lecture deleted for good takes its chats with it (deleted ones waiting for Undo too).</summary>
    public int ForgetLecture(string lecture)
    {
        lock (gate)
        {
            var ids = Entries().Where(e => e.Lecture == lecture).Select(e => e.Id).ToList();
            foreach (string id in ids) File.Delete(PathOf(id));
            if (ids.Count > 0) Indexed(all => [.. all.Where(e => e.Lecture != lecture)]);
            if (Directory.Exists(TrashDir))
                foreach (string f in Directory.EnumerateFiles(TrashDir, "*.json"))
                    try
                    {
                        if (JsonSerializer.Deserialize<Chat>(File.ReadAllText(f), Options)?.Lecture == lecture) File.Delete(f);
                    }
                    catch (JsonException)
                    {
                    }
            return ids.Count;
        }
    }

    // --- Ask chats ---------------------------------------------------------------------------------------------------

    /// <summary>A new Ask chat where it was started: a lecture (with its folder), a folder, or Home.</summary>
    public Chat NewAsk(AskPlace? place)
    {
        var c = New(place?.Class ?? "", place?.Lecture ?? "");
        c.Kind = AskKind;
        c.Scope = place?.Scope is "lecture" or "class" ? place.Scope : "home";
        return c;
    }

    /// <summary>Chats answering a question right now, each with the turn that holds it: one turn at a time each.</summary>
    readonly Dictionary<string, object> answering = [];

    /// <summary>Takes a chat's turn, for <see cref="End"/> to give back; null while it's still answering a question
    /// (asked on another device, say).</summary>
    public object? TryBegin(string id)
    {
        lock (answering)
        {
            if (answering.ContainsKey(id)) return null;
            var turn = new object();
            answering[id] = turn;
            return turn;
        }
    }

    /// <summary>A turn over: the chat is free for the next question. Only the turn that holds it gives it back (one
    /// stopped and freed early doesn't free the next one's later).</summary>
    public void End(string id, object turn)
    {
        lock (answering)
            if (answering.TryGetValue(id, out var holding) && ReferenceEquals(holding, turn)) answering.Remove(id);
    }

    /// <summary>The conversation so far, as Ask's memory takes it (<see cref="AskMemory"/>): each question with the answer
    /// it got and what that drew on.</summary>
    public static List<AskTurn> AskTurns(Chat c)
    {
        var turns = new List<AskTurn>();
        for (int i = 0; i + 1 < c.Messages.Count; i++)
            if (c.Messages[i].Role == "user" && c.Messages[i + 1].Role == "assistant" && !c.Messages[i + 1].Failed)
                turns.Add(new AskTurn(c.Messages[i].Text, c.Messages[i + 1].Text) { Sources = c.Messages[i + 1].Sources });
        return turns;
    }

    /// <summary>One Ask turn kept: the question, and its answer (or what came of it before it stopped, or why it
    /// failed), added to the chat as it is now (another turn may have been kept since this one began). The chat is
    /// named for its first question. False, and nothing kept, when the chat was deleted while it answered: a deleted
    /// chat never comes back that way (Undo brings it back).</summary>
    public bool AddAskTurn(Chat c, string question, string answer, AskReply? reply = null, bool stopped = false, bool failed = false)
    {
        lock (gate)
        {
            // Kept before: as it is in its file now; gone from there, it was deleted. New: unless it's been deleted already.
            var now = File.Exists(PathOf(c.Id)) ? Get(c.Id) : c.Messages.Count == 0 && !File.Exists(Path.Combine(TrashDir, c.Id + ".json")) ? c : null;
            if (now is null) return false;
            if (now.Title.Length == 0) now.Title = Py.Head(question.ReplaceLineEndings(" "), 80);
            now.Messages.Add(new ChatMessage("user", question, Now()));
            now.Messages.Add(new ChatMessage("assistant", answer, Now())
            {
                Sources = reply?.Sources ?? [], Engine = reply?.Engine ?? "", EngineName = reply?.EngineName ?? "", Stopped = stopped, Failed = failed,
            });
            now.Updated = Now();
            Save(now);
            return true;
        }
    }

    /// <summary>The chats, most recent first.</summary>
    public List<Chat> List(int n = 50) =>
        Directory.Exists(Dir)
            ? Directory.EnumerateFiles(Dir, "*.json").Select(f => Get(Path.GetFileNameWithoutExtension(f))).OfType<Chat>()
                .OrderByDescending(c => c.Updated, StringComparer.Ordinal).Take(n).ToList()
            : [];

    public Chat New(string className = "", string lecture = "") => new()
    {
        Id = DateTime.Now.ToString("yyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(3)),
        ClassName = className, Lecture = lecture,
    };

    /// <summary>What the AI is told about the library, and how to behave.</summary>
    public string SystemPrompt(Chat c, bool edit)
    {
        var dirs = readDirs?.Invoke() ?? [];
        string focus = c.Lecture.Length > 0 ? $"The user is looking at one lecture (id {c.Lecture}); start there (get_lecture)."
            : c.ClassName.Length > 0 ? $"The user is asking about the class \"{c.ClassName}\"; start with its folder and lectures."
            : "The user may ask about any class.";
        return $"""
            You are the study companion inside Study Stash, the user's own library of lecture notes. Your working folder is that library:
            one folder per class, each with its lectures as Markdown files ("YYYY-MM-DD Title.md": notes written from the recording, then
            its transcript), and, for classes linked to Canvas, a Canvas/ folder (assignments/*/spec.md with instructions and rubric,
            feedback.md with the user's score and the grader's comments, modules/ with course files and pages, announcements.md, and
            canvas-recipe.md, a guide to where that instructor puts things).
            Use the study-stash tools too: search_notes (full-text search of every lecture and transcript), get_lecture, get_transcript,
            due_assignments, and the canvas_* tools to read Canvas itself through the user's browser when the library doesn't have something.
            {(dirs.Count > 0 ? "You may also read these folders of the user's: " + string.Join(", ", dirs) + "." : "")}
            {focus}
            Answer like a classmate who took great notes: clear and direct, in plain prose, with short lists only where they help. Explain
            the idea, don't just quote. Say where an answer comes from (the lecture's title and date, or the file), and say plainly when
            the library doesn't cover something. When quizzing or explaining, check understanding one step at a time.
            {(edit
                ? "You may create and change files in this folder when the user asks (a study guide, a cheat sheet, practice questions): put them in the class's folder with a clear name. Never change the lecture files or anything under Canvas/ except canvas-recipe.md; those are rewritten by Study Stash. Don't run git. Say which files you wrote."
                : "This conversation is read-only: don't create or change files. If the user asks for a file, say they can turn on edits.")}
            """;
    }

    /// <summary>
    /// One turn: the user's message, the AI's answer as it comes (text and tool events), then a <c>changes</c> event
    /// (Text: the change's id; Path: the files, one per line) when it wrote anything. The chat is saved as it ends.
    /// </summary>
    public async IAsyncEnumerable<AiEvent> TurnAsync(Chat c, string message, bool edit, [EnumeratorCancellation] CancellationToken ct = default)
    {
        string provider = AiSettings.Load(home).For("agent").Provider;
        if (c.Provider != provider) c.Session = ""; // another AI can't continue this one's conversation: start its own
        c.Provider = provider;
        if (c.Title.Length == 0) c.Title = Py.Head(message.ReplaceLineEndings(" "), 80);
        c.Messages.Add(new ChatMessage("user", message, Now()));
        c.Updated = Now();
        Save(c);
        // A follow-up on a new AI session: give it the conversation so far.
        string prompt = c.Session.Length == 0 && c.Messages.Count > 1
            ? "The conversation so far:\n\n" + string.Join("\n\n", c.Messages.SkipLast(1).TakeLast(12).Select(m => (m.Role == "user" ? "User: " : "You: ") + Py.Head(m.Text, 4000)))
              + "\n\nThe user now says:\n" + message
            : message;
        Dictionary<string, (long, DateTime)>? before = null;
        if (edit)
        {
            history.Baseline();
            before = history.Snapshot();
        }
        string text = "", final = "", error = "";
        var tools = new List<string>();
        await foreach (var e in ai.AgentAsync(prompt, library, edit, SystemPrompt(c, edit), c.Session, readDirs?.Invoke(), ct: ct))
        {
            switch (e.Kind)
            {
                case "session" when e.Text.Length > 0: c.Session = e.Text; break;
                case "text": text += e.Text; break;
                case "final": final = e.Text; break;
                case "tool": tools.Add(e.Name + (e.Path.Length > 0 ? " " + e.Path : "")); break;
                case "error": error = e.Text; break;
            }
            yield return e;
        }
        var changed = before is null ? [] : history.ChangedSince(before).Where(f => f != ".gitignore").ToList();
        string sha = changed.Count > 0 ? history.Commit(changed, message, AiProviders.Get(provider).Name) : "";
        if (changed.Count > 0) yield return new AiEvent("changes", sha, Path: string.Join("\n", changed));
        string answer = Py.Strip(final.Length > 0 ? final : text);
        c.Messages.Add(new ChatMessage("assistant", error.Length > 0 && answer.Length == 0 ? error : answer, Now())
        {
            Tools = tools.Take(40).ToList(), Changed = changed, Change = sha, Failed = error.Length > 0 && answer.Length == 0,
        });
        c.Updated = Now();
        Save(c);
    }

    static string Now() => DateTimeOffset.Now.ToString("o", CultureInfo.InvariantCulture);
}
