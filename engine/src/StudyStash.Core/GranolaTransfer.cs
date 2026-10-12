using System.Text.Json.Nodes;

namespace StudyStash.Core;

/// <summary>
/// What Study Stash remembers about the lectures in someone's Granola: the ones it has brought over, and the ones they
/// said to leave where they are, so neither is offered again. One small JSON file beside the app's settings.
/// </summary>
public sealed class GranolaLedger
{
    /// <summary>Brought over; told to stay in Granola; or offered once in a notification and still waiting.</summary>
    public const string Transferred = "transferred", Left = "left", Offered = "offered";

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

    /// <summary>What became of a lecture in Granola: <see cref="Transferred"/>, <see cref="Left"/>, <see cref="Offered"/>,
    /// or null (not seen).</summary>
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

    /// <summary>A lecture this old is over, whether or not Granola's notes for it can be seen.</summary>
    public static readonly TimeSpan SurelyOver = TimeSpan.FromHours(3);

    /// <summary>
    /// The lectures in Granola (the last 30 days: all a free plan lists) that Study Stash hasn't brought over and
    /// wasn't told to leave, newest first. One the library has already (brought over from another computer) is
    /// remembered as brought over and not offered. A library that can't be asked right now counts as not having it.
    /// A lecture seen for the first time is only offered once Granola has written its notes (it writes them when the
    /// lecture ends), or it started more than <see cref="SurelyOver"/> ago: one still being recorded waits.
    /// </summary>
    public static async Task<List<Waiting>> WaitingAsync(ILectureSource granola, GranolaLedger ledger, Func<string, Task<bool>> inLibrary,
        DateTimeOffset? now = null, CancellationToken ct = default)
    {
        await using var session = await granola.SessionAsync(ct);
        var listed = await granola.ListMeetingsAsync(session, ct: ct);
        // The ones not seen before, asked after in tens (all Granola answers for at once): which have notes yet.
        var unseen = listed.Where(m => ledger.Of(m.Id) is null).Select(m => m.Id).ToList();
        var written = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < unseen.Count; i += 10)
            foreach (var whole in await granola.GetMeetingsAsync(session, [.. unseen.Skip(i).Take(10)], ct))
                if (Py.Strip(whole.NotesMarkdown).Length > 0 || Py.Strip(whole.Transcript).Length > 0) written.Add(whole.Id);
        var at = now ?? DateTimeOffset.Now;
        bool Over(Meeting m) => !DateTimeOffset.TryParse(m.Date, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeLocal, out var started) || at - started >= SurelyOver;
        var waiting = new List<Waiting>();
        foreach (var m in listed)
        {
            if (ledger.Of(m.Id) is GranolaLedger.Transferred or GranolaLedger.Left) continue;
            if (ledger.Of(m.Id) is null && !written.Contains(m.Id) && !Over(m)) continue;
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
    public static async Task<Meeting?> FetchAsync(ILectureSource granola, string granolaId, CancellationToken ct = default, Action<string>? log = null)
    {
        await using var session = await granola.SessionAsync(ct);
        var m = (await granola.GetMeetingsAsync(session, [granolaId], ct)).FirstOrDefault(x => x.Id == granolaId);
        if (m is null) return null;
        if (m.Transcript.Length == 0) m.Transcript = await granola.GetTranscriptAsync(session, granolaId, ct);
        if (Py.Strip(m.NotesMarkdown).Length == 0 && Py.Strip(m.PrivateNotes).Length == 0 && Py.Strip(m.Transcript).Length == 0)
        {
            // The names of what Granola did send (never its words): if its notes are there under a name Study Stash
            // doesn't know, this is how that's found.
            log?.Invoke($"[granola] {granolaId} came with no notes; Granola sent: {string.Join(", ", m.Raw.Select(kv => kv.Key))}");
            return null;
        }
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

/// <summary>
/// The student's Granola, as the app keeps up with it: signed in to once (in the browser, on Granola's own page; Study
/// Stash never sees the password), then looked at every few minutes for lectures Study Stash doesn't have. A new one
/// is announced once (<see cref="Found"/>, the app's notification with Transfer), and every waiting one is listed for
/// Settings. Transfer sends it to the library the way a recording goes (/api/ingest), where its notes are written.
/// </summary>
public sealed class GranolaImport(string tokensPath, string clientPath, GranolaLedger ledger, Func<ClientConfig> config, LaptopHost? net = null,
    Func<GranolaOAuth, ILectureSource>? source = null, Action<string>? log = null)
{
    /// <summary>How often Granola is looked at while the app runs.</summary>
    public static readonly TimeSpan Every = TimeSpan.FromMinutes(5);

    readonly GranolaOAuth oauth = new(GranolaTransfer.McpUrl, GranolaTransfer.CallbackPort, "login", tokensPath, clientPath);
    readonly LaptopHost net = net ?? new LaptopHost();
    readonly Action<string> say = log ?? (_ => { });
    readonly SemaphoreSlim one = new(1, 1);

    ILectureSource Granola() => source?.Invoke(oauth) ?? GranolaClient.For(GranolaTransfer.McpUrl, oauth);

    public bool SignedIn => oauth.IsLoggedIn();

    /// <summary>Who is signed in, as Granola names them; empty when Granola didn't say.</summary>
    public string Account => ledger.Account;

    /// <summary>The lectures in Granola that Study Stash could bring over, newest first, as of the last look.</summary>
    public IReadOnlyList<GranolaTransfer.Waiting> Waiting { get; private set; } = [];

    /// <summary>Why the last look or transfer didn't work, in words for the student; null when it did.</summary>
    public string? Problem { get; private set; }

    /// <summary>Signed in or out, the list of waiting lectures, or <see cref="Problem"/> changed.</summary>
    public event Action? Changed;

    /// <summary>Lectures seen in Granola for the first time: said once, for the app's notification.</summary>
    public event Action<IReadOnlyList<GranolaTransfer.Waiting>>? Found;

    /// <summary>Opens Granola's sign-in page in the browser and waits (five minutes at most) for the student to allow
    /// it. Throws <see cref="OAuthException"/> with why when it doesn't happen.</summary>
    public async Task SignInAsync(Func<string, Task>? openUrl = null, CancellationToken ct = default)
    {
        await oauth.LoginAsync(log: _ => { }, openUrl: openUrl, ct: ct);
        say("[granola] signed in");
        try
        {
            var granola = Granola();
            await using var session = await granola.SessionAsync(ct);
            if (granola is GranolaClient client && await client.GetAccountInfoAsync(session, ct) is { Email.Length: > 0 } who) ledger.SignedInAs(who.Email);
        }
        catch (Exception e) when (e is HttpRequestException or OAuthException or GranolaToolException or IOException && !ct.IsCancellationRequested)
        {
            // Signed in all the same: who as is only for Settings to say.
        }
        Problem = null;
        Changed?.Invoke();
    }

    /// <summary>Forgets the sign-in on this computer. What was brought over stays in Study Stash.</summary>
    public void SignOut()
    {
        oauth.Logout();
        ledger.SignedOut();
        Waiting = [];
        Problem = null;
        say("[granola] signed out");
        Changed?.Invoke();
    }

    /// <summary>Looks at Granola now. Never throws but for cancelling: what went wrong is <see cref="Problem"/>.</summary>
    public async Task CheckAsync(CancellationToken ct = default)
    {
        if (!SignedIn) return;
        await one.WaitAsync(ct);
        try
        {
            var cc = config();
            var waiting = await GranolaTransfer.WaitingAsync(Granola(), ledger, id => InLibraryAsync(cc, id), ct: ct);
            var fresh = waiting.Where(w => ledger.Of(w.GranolaId) is null).ToList();
            foreach (var w in fresh) ledger.Mark(w.GranolaId, GranolaLedger.Offered);
            bool changed = Problem is not null || !waiting.SequenceEqual(Waiting);
            Waiting = waiting;
            Problem = null;
            if (changed) Changed?.Invoke();
            if (fresh.Count > 0)
            {
                say($"[granola] {fresh.Count} new lecture(s) in Granola");
                Found?.Invoke(fresh);
            }
        }
        catch (Exception e) when (Words(e) is { } why && !ct.IsCancellationRequested)
        {
            say($"[granola] couldn't look: {e.Message.Split('\n')[0]}");
            if (Problem != why)
            {
                Problem = why;
                Changed?.Invoke();
            }
        }
        finally
        {
            one.Release();
        }
    }

    /// <summary>Whether the library has this lecture already (brought over from another computer).</summary>
    async Task<bool> InLibraryAsync(ClientConfig cc, string lectureId)
    {
        if (cc.ServerUrl.Length == 0) return false;
        return await net.Get($"{cc.ServerUrl.TrimEnd('/')}/api/v2/lectures/{Uri.EscapeDataString(lectureId)}", cc.PoolKey) is not null;
    }

    /// <summary>
    /// Brings one lecture over: fetched whole from Granola and sent to the library, which writes its notes and files
    /// it. True when it went; false, with <see cref="Problem"/> saying why, when it didn't (it stays in the list).
    /// </summary>
    public async Task<bool> TransferAsync(string granolaId, CancellationToken ct = default)
    {
        await one.WaitAsync(ct);
        try
        {
            var cc = config();
            if (cc.ServerUrl.Length == 0)
            {
                Problem = "Study Stash isn't connected to a library yet, so there's nowhere to bring it.";
                return false;
            }
            var m = await GranolaTransfer.FetchAsync(Granola(), granolaId, ct, say);
            if (m is null)
            {
                Problem = "Granola hasn't written anything for that lecture yet. Try again once it has finished it.";
                return false;
            }
            if (m.Owner.Length == 0) m.Owner = cc.DisplayName;
            await net.Post($"{cc.ServerUrl.TrimEnd('/')}/api/ingest", Wire.MeetingJson(m), cc.PoolKey);
            ledger.Mark(granolaId, GranolaLedger.Transferred);
            Waiting = [.. Waiting.Where(w => w.GranolaId != granolaId)];
            Problem = null;
            say($"[granola] brought over {m.Id} ({(Py.Strip(m.Transcript).Length > 0 ? "with its transcript" : "notes only")})");
            return true;
        }
        catch (Exception e) when (Words(e) is { } why && !ct.IsCancellationRequested)
        {
            say($"[granola] couldn't bring {granolaId} over: {e.Message.Split('\n')[0]}");
            Problem = why;
            return false;
        }
        finally
        {
            one.Release();
            Changed?.Invoke();
        }
    }

    /// <summary>Leaves a lecture in Granola: it isn't offered again.</summary>
    public void Leave(string granolaId)
    {
        ledger.Mark(granolaId, GranolaLedger.Left);
        Waiting = [.. Waiting.Where(w => w.GranolaId != granolaId)];
        Changed?.Invoke();
    }

    /// <summary>What went wrong, for the student. Null only for a cancel: anything else Granola or the library does
    /// (an answer in a shape Study Stash doesn't know included) is said, and tried again at the next look.</summary>
    static string? Words(Exception e) => e switch
    {
        OperationCanceledException and not TaskCanceledException => null,
        OAuthException => "Granola wants you to sign in again. Disconnect, then Connect.",
        LibraryRefusedException => "Your library didn't take it. Check Study Stash is connected to your library.",
        HttpRequestException or TaskCanceledException or IOException or TimeoutException => "Couldn't reach Granola or your library. It will try again.",
        _ => "Granola didn't answer the way Study Stash expected. It will try again.",
    };

    /// <summary>Looks at Granola now and every <see cref="Every"/> until stopped.</summary>
    public async Task RunAsync(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                await CheckAsync(stop);
                await Task.Delay(Every, stop);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
