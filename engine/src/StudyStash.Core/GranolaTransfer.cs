using System.Text.Json.Nodes;

namespace StudyStash.Core;

/// <summary>
/// What Study Stash remembers about the lectures in someone's Granola: the ones it has brought over, and the ones they
/// said to leave where they are, so neither is offered again. One small JSON file beside the app's settings.
/// </summary>
public sealed class GranolaLedger
{
    public const string Transferred = "transferred", Left = "left";

    readonly string path;
    readonly Dictionary<string, string> lectures = new(StringComparer.Ordinal);

    public GranolaLedger(string path)
    {
        this.path = path;
        try
        {
            if (File.Exists(path) && Py.JsonLoads(Py.ReadText(path)) is JsonObject saved)
            {
                Account = Py.AsString(saved["account"]) ?? "";
                foreach (var (id, what) in saved["lectures"] as JsonObject ?? [])
                    if (Py.AsString(what) is { Length: > 0 } w) lectures[id] = w;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            // A file that can't be read is one that says nothing: lectures are offered again, never lost.
        }
    }

    /// <summary>Who is signed in to Granola, as Granola names them (an email), for Settings to say.</summary>
    public string Account { get; private set; } = "";

    /// <summary>What became of a lecture in Granola: <see cref="Transferred"/>, <see cref="Left"/>, or null (not seen).</summary>
    public string? Of(string granolaId) => lectures.GetValueOrDefault(granolaId);

    public void Mark(string granolaId, string what)
    {
        if (lectures.GetValueOrDefault(granolaId) == what) return;
        lectures[granolaId] = what;
        Save();
    }

    public void SignedInAs(string account)
    {
        if (Account == account) return;
        Account = account;
        Save();
    }

    /// <summary>Signed out: who it was is forgotten. What was brought over stays known, so signing in again doesn't
    /// offer the same lectures twice.</summary>
    public void SignedOut() => SignedInAs("");

    void Save()
    {
        var all = new JsonObject();
        foreach (var (id, what) in lectures.OrderBy(kv => kv.Key, StringComparer.Ordinal)) all[id] = what;
        Directory.CreateDirectory(Py.Parent(path));
        Py.WriteText(path, PyJson.Dumps(new JsonObject { ["account"] = Account, ["lectures"] = all }, indent: 2));
    }
}

/// <summary>
/// Bringing lectures over from Granola, through Granola's own connection for apps (its MCP server, signed in to as
/// the student): which of the lectures there Study Stash doesn't have, and one of them as the library takes it. A
/// free Granola plan lists the last 30 days and gives each lecture's notes (its summary, and what was typed) but
/// never its transcript; a paid one gives the transcript too, and then the library writes its own notes from that.
/// </summary>
public static class GranolaTransfer
{
    /// <summary>Granola's MCP server.</summary>
    public const string McpUrl = "https://mcp.granola.ai/mcp";

    /// <summary>The port Granola sends the browser back to after sign-in (http://localhost:3334/callback).</summary>
    public const int CallbackPort = 3334;

    /// <summary>A lecture from Granola keeps Granola's id behind this, so it's never taken for a recording's.</summary>
    public const string IdPrefix = "granola-";

    public static string LectureId(string granolaId) => IdPrefix + granolaId;

    public static bool IsFromGranola(string lectureId) => lectureId.StartsWith(IdPrefix, StringComparison.Ordinal);

    /// <summary>A lecture in Granola that Study Stash could bring over.</summary>
    public sealed record Waiting(string GranolaId, string Title, string Date);

    /// <summary>
    /// The lectures in Granola (the last 30 days: all a free plan lists) that Study Stash hasn't brought over and
    /// wasn't told to leave, newest first. One the library has already (brought over from another computer) is
    /// remembered as brought over and not offered. A library that can't be asked right now counts as not having it.
    /// </summary>
    public static async Task<List<Waiting>> WaitingAsync(ILectureSource granola, GranolaLedger ledger, Func<string, Task<bool>> inLibrary,
        CancellationToken ct = default)
    {
        await using var session = await granola.SessionAsync(ct);
        var waiting = new List<Waiting>();
        foreach (var m in await granola.ListMeetingsAsync(session, ct: ct))
        {
            if (ledger.Of(m.Id) is not null) continue;
            bool have;
            try
            {
                have = await inLibrary(LectureId(m.Id));
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException && !ct.IsCancellationRequested)
            {
                have = false;
            }
            if (have) ledger.Mark(m.Id, GranolaLedger.Transferred);
            else waiting.Add(new Waiting(m.Id, m.Title, m.Date));
        }
        return [.. waiting.OrderByDescending(w => w.Date, StringComparer.Ordinal)];
    }

    /// <summary>
    /// One lecture as the library takes it: its title and date, Granola's notes, and its transcript where the Granola
    /// plan gives it. Null while Granola has nothing written for it yet (it may still be recording): it's offered
    /// again later.
    /// </summary>
    public static async Task<Meeting?> FetchAsync(ILectureSource granola, string granolaId, CancellationToken ct = default)
    {
        await using var session = await granola.SessionAsync(ct);
        var m = (await granola.GetMeetingsAsync(session, [granolaId], ct)).FirstOrDefault(x => x.Id == granolaId);
        if (m is null) return null;
        if (m.Transcript.Length == 0) m.Transcript = await granola.GetTranscriptAsync(session, granolaId, ct);
        if (Py.Strip(m.NotesMarkdown).Length == 0 && Py.Strip(m.PrivateNotes).Length == 0 && Py.Strip(m.Transcript).Length == 0) return null;
        return new Meeting(LectureId(granolaId))
        {
            Title = m.Title, Date = m.Date, Owner = m.Owner, Attendees = m.Attendees,
            // Granola's folder is a place to file things, not a class: the library sorts the lecture by what's in it.
            Folder = "",
            NotesMarkdown = m.NotesMarkdown, PrivateNotes = m.PrivateNotes, Transcript = m.Transcript,
            Raw = new JsonObject { ["source"] = "granola", ["granola_id"] = granolaId },
        };
    }
}
