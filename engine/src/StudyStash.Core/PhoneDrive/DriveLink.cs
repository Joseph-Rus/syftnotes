using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace StudyStash.Core.PhoneDrive;

/// <summary>The names of the folder's files (docs/phone-google.md), each after the test prefix when there is one.</summary>
public sealed record DriveNames(string Prefix = "")
{
    public string Laptop => Prefix + "laptop.json";
    public string Copies => Prefix + "copy-";
    public string Blobs => Prefix + "blob-";
    public string Phones => Prefix + "phone-";
    public string Answers => Prefix + "answer-";
    public string Uploads => Prefix + "upload-";

    /// <summary>A copy's file: the first 16 hex characters of its key's SHA-256.</summary>
    public string Copy(string key) => Copies + Sha(Encoding.UTF8.GetBytes(key))[..16] + ".json";
    public string Blob(string sha) => Blobs + sha[..16];
    public string Answer(string request) => Answers + request + ".json";

    /// <summary>The phone's id from its box's name ("phone-&lt;id&gt;.json"), or null for any other file.</summary>
    public string? PhoneOf(string name) =>
        name.StartsWith(Phones, StringComparison.Ordinal) && name.EndsWith(".json", StringComparison.Ordinal) && name.Length > Phones.Length + 5
            ? name[Phones.Length..^5] : null;

    public static string Sha(byte[] data) => Convert.ToHexStringLower(SHA256.HashData(data));
}

/// <summary>How often the link does each thing. The defaults are the contract's; tests make them shorter.</summary>
public sealed record DriveTimes
{
    /// <summary>An open box (the app is in front on the phone) is read this often, by id.</summary>
    public TimeSpan OpenBox { get; init; } = TimeSpan.FromSeconds(1);
    public TimeSpan ClosedBox { get; init; } = TimeSpan.FromSeconds(15);
    /// <summary>How often the folder is looked through for new boxes.</summary>
    public TimeSpan NewBoxes { get; init; } = TimeSpan.FromSeconds(60);
    /// <summary>For the first <see cref="Soon"/> after the link starts (just connected: the phone is being signed in
    /// with the QR code on screen), new boxes are looked for this often instead.</summary>
    public TimeSpan NewBoxesSoon { get; init; } = TimeSpan.FromSeconds(10);
    public TimeSpan Soon { get; init; } = TimeSpan.FromMinutes(15);
    /// <summary>How often the copy is brought up to date when nothing's asked for it sooner.</summary>
    public TimeSpan Copy { get; init; } = TimeSpan.FromSeconds(15);
    /// <summary>The copy's next look after a request that changes something.</summary>
    public TimeSpan AfterChange { get; init; } = TimeSpan.FromMilliseconds(300);
    /// <summary>laptop.json's <c>seen</c> is written at least this often (the contract: at least every 60 s).</summary>
    public TimeSpan Seen { get; init; } = TimeSpan.FromSeconds(45);
    /// <summary>The least time between two writes of laptop.json, or of one streamed answer.</summary>
    public TimeSpan Write { get; init; } = TimeSpan.FromSeconds(1);
    /// <summary>How often the link's own old files (answers, blobs a day old) are looked for.</summary>
    public TimeSpan Clean { get; init; } = TimeSpan.FromMinutes(10);
    /// <summary>The loops' heartbeat.</summary>
    public TimeSpan Tick { get; init; } = TimeSpan.FromMilliseconds(250);
    /// <summary>Lectures looked at again each round whatever their list entry says (a note edited by hand), in turn.</summary>
    public int Sweep { get; init; } = 8;
}

/// <summary>A phone that uses the folder, from its box.</summary>
public sealed record DrivePhone(string Id, string Name, string Model, DateTimeOffset? OpenUntil, DateTimeOffset Changed)
{
    public bool OpenAt(DateTimeOffset now) => OpenUntil is { } u && u > now;
}

/// <summary>
/// The laptop's side of the phone over Google Drive (docs/phone-google.md): it keeps a copy of the library's answers
/// in the folder for the phone to read, and does what the phones ask in their boxes, against this computer's own
/// library, as if they'd asked over Tailscale, writing each answer back. Only the requests in <see cref="PhoneCalls"/>
/// are done; a request id is done once (<c>answered</c>, kept in laptop.json across restarts). Runs until its token
/// is cancelled; while <c>active</c> says no (this computer stopped being its own library) it does nothing.
/// </summary>
public sealed partial class DriveLink
{
    readonly IDrive drive;
    readonly LibraryCalls library;
    readonly Func<(string Id, string Name, string Version)> me;
    readonly Func<bool> active;
    readonly Action<string> log;
    readonly Func<DateTimeOffset> now;

    public DriveNames Names { get; }
    public DriveTimes Times { get; init; } = new();

    readonly Lock gate = new();
    /// <summary>laptop.json's id, once it's found or made.</summary>
    string? laptopFile;
    readonly Dictionary<string, CopyEntry> copy = new(StringComparer.Ordinal);
    readonly LinkedList<string> answered = new();
    readonly HashSet<string> answeredSet = new(StringComparer.Ordinal);
    readonly HashSet<string> removed = new(StringComparer.Ordinal);
    readonly HashSet<string> doing = new(StringComparer.Ordinal);
    /// <summary>Streamed answers being written now, by request id: the phone's Stop takes the request out of its box,
    /// and the laptop stops answering once it's been missing from two reads in a row (one stale read isn't enough).</summary>
    readonly Dictionary<string, Streaming> streams = new(StringComparer.Ordinal);

    sealed class Streaming(string phone, CancellationTokenSource stop)
    {
        public string Phone { get; } = phone;
        public CancellationTokenSource Stop { get; } = stop;
        public int Missing { get; set; }
    }
    readonly Dictionary<string, DateTimeOffset> notBefore = new(StringComparer.Ordinal);
    readonly Dictionary<string, Box> boxes = new(StringComparer.Ordinal);
    /// <summary>Files found in the folder at the start, by name: copies and blobs this laptop wrote before.</summary>
    readonly Dictionary<string, string> found = new(StringComparer.Ordinal);
    readonly Dictionary<string, (string Id, DateTimeOffset Made)> blobs = new(StringComparer.Ordinal);
    bool laptopDirty = true;
    DateTimeOffset laptopWritten = DateTimeOffset.MinValue;
    readonly SemaphoreSlim laptopWriting = new(1, 1);
    readonly SemaphoreSlim requests = new(4, 4);
    readonly List<Task> working = [];

    /// <summary>One copied answer of the library's: its file in the folder, the SHA-256 of its body, and its size.</summary>
    public sealed record CopyEntry(string File, string Sha, long Size);

    sealed class Box
    {
        public required string File;
        public DrivePhone? Phone;
        public DateTimeOffset Read = DateTimeOffset.MinValue;
        /// <summary>Not read yet since the link started: requests in it may have been answered before a restart.</summary>
        public bool First = true;
        /// <summary>Its requests not answered yet, as last read: their answer files stay however old they get.</summary>
        public HashSet<string> Waiting = [];
    }

    /// <param name="drive">The folder.</param>
    /// <param name="library">This computer's library.</param>
    /// <param name="me">This laptop: its install's id, its name ("Joey's MacBook Air") and Egghead Notes' version.</param>
    /// <param name="active">Whether this computer is still its own library (just this computer).</param>
    public DriveLink(IDrive drive, LibraryCalls library, Func<(string Id, string Name, string Version)> me, Func<bool>? active = null,
        DriveNames? names = null, Action<string>? log = null, Func<DateTimeOffset>? clock = null)
    {
        this.drive = drive;
        this.library = library;
        this.me = me;
        this.active = active ?? (() => true);
        this.log = log ?? (_ => { });
        now = clock ?? (() => DateTimeOffset.UtcNow);
        Names = names ?? new DriveNames();
    }

    /// <summary>The phones that use the folder (from their boxes), most recently open first; removed ones aren't.</summary>
    public IReadOnlyList<DrivePhone> Phones
    {
        get
        {
            lock (gate)
                return [.. boxes.Where(b => b.Value.Phone is not null && !removed.Contains(b.Key)).Select(b => b.Value.Phone!)
                    .OrderByDescending(p => p.OpenUntil ?? p.Changed)];
        }
    }

    /// <summary>What the copy is doing or what's wrong, in the student's words; null while all is well.</summary>
    public string? Status { get; private set; }

    /// <summary>Google stopped taking this laptop's sign-in: the link can do nothing until it's connected again.</summary>
    public GoogleTrouble? Trouble { get; private set; }

    /// <summary>The phones or <see cref="Status"/> changed (on a worker thread).</summary>
    public event Action? Changed;

    /// <summary>Files the copy uploaded (made or replaced), by name: tests count them.</summary>
    public Dictionary<string, int> Uploads { get; } = new(StringComparer.Ordinal);

    /// <summary>The copy as it stands, by key.</summary>
    public Dictionary<string, CopyEntry> Copy
    {
        get
        {
            lock (gate) return new(copy, StringComparer.Ordinal);
        }
    }

    /// <summary>The first full copy is in: the phone has everything (tests and the real check wait for it).</summary>
    public TaskCompletionSource FirstCopy { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    void Say(string? status)
    {
        if (Status == status) return;
        Status = status;
        Changed?.Invoke();
    }

    /// <summary>Runs the link until <paramref name="stop"/>: the copy, the boxes and laptop.json, each its own loop.</summary>
    public async Task RunAsync(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                if (active())
                {
                    await StartAsync(stop);
                    break;
                }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e) when (Expected(e))
            {
                Troubled(e);
                // Signed out: nothing here can work until the student connects again (a new link).
                if (Trouble is not null) return;
            }
            if (!await Pause(TimeSpan.FromSeconds(15), stop)) return;
        }
        var loops = new[] { CopyLoop(stop), BoxLoop(stop), LaptopLoop(stop), CleanLoop(stop) };
        await Task.WhenAll(loops);
        Task[] still;
        lock (gate) still = [.. working];
        try
        {
            await Task.WhenAll(still).WaitAsync(TimeSpan.FromSeconds(3));
        }
        catch (Exception e) when (e is TimeoutException or OperationCanceledException)
        {
        }
    }

    static bool Expected(Exception e) => e is DriveException or GoogleException or HttpRequestException or IOException or JsonException
        or TaskCanceledException or InvalidOperationException;

    void Troubled(Exception e)
    {
        if (e is GoogleException { Trouble: GoogleTrouble.SignedOut or GoogleTrouble.NotBuilt } g)
        {
            Trouble = g.Trouble;
            Say(g.Message);
            return;
        }
        log($"[drive] {e.GetType().Name}: {(e is DriveException or GoogleException ? e.Message : "the library or Drive didn't answer")}");
        if (e is DriveException or GoogleException) Say("Can't reach Google Drive right now. Your phone gets your newest notes once it can.");
    }

    static async Task<bool> Pause(TimeSpan wait, CancellationToken stop)
    {
        try
        {
            await Task.Delay(wait, stop);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>What's in the folder already: this laptop's laptop.json (its copy's index, the requests it answered,
    /// the phones it was told to stop answering), its copies and blobs, and the phones' boxes.</summary>
    async Task StartAsync(CancellationToken ct)
    {
        var files = await drive.ListAsync(Names.Prefix, ct);
        var laptop = files.Where(f => f.Name == Names.Laptop).OrderByDescending(f => f.Modified).ToList();
        JsonObject? had = null;
        if (laptop.Count > 0 && await drive.ReadBytesAsync(laptop[0].Id, ct) is { } bytes)
        {
            try
            {
                had = JsonNode.Parse(bytes) as JsonObject;
            }
            catch (JsonException)
            {
            }
        }
        // A second laptop.json (two starts racing) is one too many.
        foreach (var extra in laptop.Skip(1)) await drive.DeleteAsync(extra.Id, ct);
        var ids = files.Select(f => f.Id).ToHashSet(StringComparer.Ordinal);
        lock (gate)
        {
            foreach (var f in files)
            {
                if (f.Name.StartsWith(Names.Copies, StringComparison.Ordinal)) found[f.Name] = f.Id;
                else if (f.Name.StartsWith(Names.Blobs, StringComparison.Ordinal)) blobs[f.Name[Names.Blobs.Length..]] = (f.Id, f.Modified);
                else if (Names.PhoneOf(f.Name) is { } phone && !boxes.ContainsKey(phone)) boxes[phone] = new Box { File = f.Id };
            }
            if (had is not null)
            {
                foreach (var id in (had["answered"] as JsonArray ?? []).Select(Py.AsString).OfType<string>()) Answered(id);
                foreach (var id in (had["removed"] as JsonArray ?? []).Select(Py.AsString).OfType<string>()) removed.Add(id);
                // The index is only this laptop's own, of files that are still there: anything else is uploaded again.
                if (Py.AsString(had["laptop"]?["id"]) == me().Id)
                    foreach (var (key, v) in had["copy"] as JsonObject ?? [])
                        if (v is JsonObject e && Py.AsString(e["file"]) is { } file && ids.Contains(file) && Py.AsString(e["sha"]) is { } sha)
                            copy[key] = new CopyEntry(file, sha, e["size"] is JsonValue sv && sv.TryGetValue(out long size) ? size : 0);
            }
            foreach (string key in copy.Keys) found.Remove(Names.Copy(key));
            laptopFile = laptop.Count > 0 ? laptop[0].Id : null;
        }
        // Made now if it wasn't there: the phone finds the laptop by it.
        await WriteLaptopAsync(ct);
        Trouble = null;
        Say(null);
        log($"[drive] the phone link started: {boxes.Count} phone(s), {copy.Count} copied answer(s) already there");
    }

    void Answered(string id)
    {
        if (!answeredSet.Add(id)) return;
        answered.AddLast(id);
        while (answered.Count > 500)
        {
            answeredSet.Remove(answered.First!.Value);
            answered.RemoveFirst();
        }
    }

    // --- laptop.json ------------------------------------------------------------------------------------------------

    async Task LaptopLoop(CancellationToken stop)
    {
        while (await Pause(Times.Tick, stop))
        {
            bool due;
            lock (gate) due = (laptopDirty && now() - laptopWritten >= Times.Write) || now() - laptopWritten >= Times.Seen;
            if (!due || !active() || Trouble is not null) continue;
            try
            {
                await WriteLaptopAsync(stop);
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e) when (Expected(e))
            {
                Troubled(e);
                await Pause(TimeSpan.FromSeconds(5), stop);
            }
        }
    }

    /// <summary>laptop.json as it stands: who the laptop is, when it was last here, the copy's index, the requests it
    /// has answered (the last 500) and the phones it doesn't answer any more.</summary>
    public JsonObject LaptopJson()
    {
        var (id, name, version) = me();
        lock (gate)
        {
            var index = new JsonObject();
            foreach (var (key, e) in copy.OrderBy(kv => kv.Key, StringComparer.Ordinal))
                index[key] = new JsonObject { ["file"] = e.File, ["sha"] = e.Sha, ["size"] = e.Size };
            return new JsonObject
            {
                ["v"] = 1,
                ["laptop"] = new JsonObject { ["id"] = id, ["name"] = name, ["version"] = version },
                ["seen"] = now().UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
                ["copy"] = index,
                ["answered"] = new JsonArray([.. answered.Select(a => (JsonNode?)a)]),
                ["removed"] = new JsonArray([.. removed.Order(StringComparer.Ordinal).Select(r => (JsonNode?)r)]),
            };
        }
    }

    async Task WriteLaptopAsync(CancellationToken ct)
    {
        await laptopWriting.WaitAsync(ct);
        try
        {
            string? file;
            lock (gate)
            {
                laptopDirty = false;
                laptopWritten = now();
                file = laptopFile;
            }
            byte[] body = Encoding.UTF8.GetBytes(LaptopJson().ToJsonString());
            if (file is null || !await drive.UpdateAsync(file, body, ct: ct))
            {
                string made = await drive.CreateAsync(Names.Laptop, body, ct: ct);
                lock (gate) laptopFile = made;
            }
        }
        catch
        {
            lock (gate) laptopDirty = true;
            throw;
        }
        finally
        {
            laptopWriting.Release();
        }
    }

    void Dirty()
    {
        lock (gate) laptopDirty = true;
    }

    // --- the old files ----------------------------------------------------------------------------------------------

    async Task CleanLoop(CancellationToken stop)
    {
        while (await Pause(Times.Clean, stop))
        {
            if (!active() || Trouble is not null) continue;
            try
            {
                var day = now() - TimeSpan.FromDays(1);
                foreach (var f in await drive.ListAsync(Names.Answers, stop))
                {
                    bool kept;
                    string request = f.Name[Names.Answers.Length..].Replace(".json", "", StringComparison.Ordinal);
                    lock (gate) kept = boxes.Values.Any(b => b.Waiting.Contains(request));
                    if (f.Modified < day && !kept) await drive.DeleteAsync(f.Id, stop);
                }
                foreach (var f in await drive.ListAsync(Names.Blobs, stop))
                    if (f.Modified < day)
                    {
                        await drive.DeleteAsync(f.Id, stop);
                        lock (gate) blobs.Remove(f.Name[Names.Blobs.Length..]);
                    }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e) when (Expected(e))
            {
                Troubled(e);
            }
        }
    }

    /// <summary>Disconnect: every file of this laptop's in the folder goes (laptop.json, the copy, blobs and answers).
    /// The phones' own boxes and uploads are theirs.</summary>
    public async Task DeleteOwnFilesAsync(CancellationToken ct = default)
    {
        foreach (var f in await drive.ListAsync(Names.Prefix, ct))
            if (f.Name == Names.Laptop || f.Name.StartsWith(Names.Copies, StringComparison.Ordinal) || f.Name.StartsWith(Names.Blobs, StringComparison.Ordinal)
                || f.Name.StartsWith(Names.Answers, StringComparison.Ordinal))
                await drive.DeleteAsync(f.Id, ct);
        lock (gate)
        {
            copy.Clear();
            laptopFile = null;
        }
    }

    /// <summary>Settings' Remove: the laptop stops answering this phone (laptop.json's <c>removed</c> says so, so the
    /// phone can too), and its box and the files it was sending go.</summary>
    public async Task RemovePhoneAsync(string phoneId, CancellationToken ct = default)
    {
        string? file;
        lock (gate)
        {
            removed.Add(phoneId);
            laptopDirty = true;
            file = boxes.TryGetValue(phoneId, out var b) ? b.File : null;
        }
        Changed?.Invoke();
        if (file is null) return;
        if (await drive.ReadBytesAsync(file, ct) is { } bytes && Parse(bytes) is { } box)
            foreach (var r in box.Requests)
                if (Py.AsString(r.Upload?["file"]) is { Length: > 0 } upload)
                    await drive.DeleteAsync(upload, ct);
        await drive.DeleteAsync(file, ct);
        lock (gate) boxes.Remove(phoneId);
        Changed?.Invoke();
    }
}
