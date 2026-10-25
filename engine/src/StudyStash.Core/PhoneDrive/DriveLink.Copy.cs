using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace StudyStash.Core.PhoneDrive;

/// <summary>The copy: the library's answers the phone reads at once, offline included, each kept in the folder as it
/// came, uploaded again only when it changed.</summary>
public sealed partial class DriveLink
{
    /// <summary>Every lecture, newest first, under this one key: the phone filters by class, limit and before itself
    /// (docs/phone-google.md says how the library does).</summary>
    public const string LecturesKey = "GET /lectures?limit=1000";

    /// <summary>The lists the copy keeps (key, path), lectures apart.</summary>
    static readonly (string Key, string Path)[] Lists =
    [
        ("GET /library", "/library"),
        ("GET /ai", "/ai"),
        ("GET /chats", "/chats"),
        ("GET /canvas/due", "/canvas/due"),
        ("GET /canvas/state", "/canvas/state"),
        ("GET /calendar/upcoming", "/calendar/upcoming"),
        ("GET /attachments", "/attachments"),
        ("GET /voice-memos", "/voice-memos"),
    ];

    public static string LectureKey(string id) => "GET /lectures/" + Uri.EscapeDataString(id);
    public static string RenderedKey(string id) => LectureKey(id) + "/rendered";
    public static string ChatKey(string id) => "GET /chats/" + Uri.EscapeDataString(id);

    /// <summary>Each lecture's and chat's entry in its list, as last copied: when it changes, so has the lecture.</summary>
    readonly Dictionary<string, string> listed = new(StringComparer.Ordinal);
    readonly HashSet<string> touched = new(StringComparer.Ordinal);
    DateTimeOffset copyAt = DateTimeOffset.MinValue;
    int sweepAt;

    /// <summary>A request changed something: the copy looks again straight away, at <paramref name="lecture"/> first.</summary>
    void CopySoon(string? lecture)
    {
        lock (gate)
        {
            if (lecture is not null) touched.Add(lecture);
            var soon = now() + Times.AfterChange;
            if (soon < copyAt) copyAt = soon;
        }
    }

    async Task CopyLoop(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            bool due;
            lock (gate) due = now() >= copyAt;
            if (!due)
            {
                if (!await Pause(Times.Tick, stop)) return;
                continue;
            }
            lock (gate) copyAt = now() + Times.Copy;
            if (!active() || Trouble is not null) continue;
            HashSet<string> these;
            lock (gate)
            {
                these = [.. touched];
                touched.Clear();
            }
            try
            {
                await CopyRoundAsync(these, stop);
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e) when (Expected(e))
            {
                lock (gate) touched.UnionWith(these);
                Troubled(e);
            }
        }
    }

    /// <summary>One look at everything: the lists, then the lectures (new, changed, asked about, and a few more in
    /// turn), then the chats; what's gone from the library goes from the folder.</summary>
    public async Task CopyRoundAsync(IReadOnlySet<string>? asked = null, CancellationToken ct = default)
    {
        asked ??= new HashSet<string>();
        bool first = !FirstCopy.Task.IsCompleted;
        // The lists first, side by side: the phone opens on them.
        var chatList = new JsonArray();
        var lectureList = new JsonArray();
        await Task.WhenAll(Lists.Select(l => l.Key).Append(LecturesKey).Select(async key =>
        {
            if (key == LecturesKey)
            {
                var (bytes, all) = await AllLecturesAsync(ct);
                await PutAsync(key, bytes, ct);
                lectureList = all;
                return;
            }
            if (await library.GetAsync(Lists.First(l => l.Key == key).Path, ct) is not { } body) return;
            await PutAsync(key, body, ct);
            if (key == "GET /chats") chatList = JsonNode.Parse(body) as JsonArray ?? [];
        }));
        var lectures = lectureList;

        // The lectures: newest first, so the phone has the ones it's likeliest to open soonest.
        var ids = lectures.OfType<JsonObject>().Select(l => (Id: Py.AsString(l["id"]) ?? "", Entry: l.ToJsonString())).Where(l => l.Id.Length > 0).ToList();
        List<string> want;
        lock (gate)
        {
            want = [.. ids.Where(l => !copy.ContainsKey(LectureKey(l.Id)) || !copy.ContainsKey(RenderedKey(l.Id)) || asked.Contains(l.Id)
                || (listed.TryGetValue("l:" + l.Id, out var was) && was != l.Entry)).Select(l => l.Id)];
            // A note edited by hand doesn't always change its list entry: a few lectures are looked at again every round.
            for (int i = 0; i < Math.Min(Times.Sweep, ids.Count); i++)
            {
                string id = ids[(sweepAt + i) % ids.Count].Id;
                if (!want.Contains(id)) want.Add(id);
            }
            if (ids.Count > 0) sweepAt = (sweepAt + Times.Sweep) % ids.Count;
        }
        // A few at a time: Drive takes about a second a file, so a big library's first copy would be slow one by one.
        var wanted = want.ToHashSet(StringComparer.Ordinal);
        int n = 0;
        await Parallel.ForEachAsync(ids.Where(l => wanted.Contains(l.Id)), new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = ct }, async (l, token) =>
        {
            var (id, entry) = l;
            if (await library.GetAsync("/lectures/" + Uri.EscapeDataString(id), token) is not { } detail) return;
            await PutAsync(LectureKey(id), detail, token);
            if (await library.GetAsync("/lectures/" + Uri.EscapeDataString(id) + "/rendered", token) is { } rendered)
                await PutAsync(RenderedKey(id), rendered, token);
            lock (gate) listed["l:" + id] = entry;
            int done = Interlocked.Increment(ref n);
            if (first && wanted.Count > 10 && done % 5 == 0)
                Say($"Putting your notes in your Google Drive for your phone: {done} of {wanted.Count} lectures…");
        });
        // The rest are as they were copied (a restart's copy is the one it left): what their entries say now is the
        // mark a change is seen by from here on.
        lock (gate)
            foreach (var (id, entry) in ids) listed.TryAdd("l:" + id, entry);
        var lectureIds = ids.Select(l => l.Id).ToHashSet(StringComparer.Ordinal);
        await DropGoneAsync("GET /lectures/", lectureIds, ct);

        // The chats: the ones new or changed since the last look.
        var chats = chatList.OfType<JsonObject>().Select(c => (Id: Py.AsString(c["id"]) ?? "", Entry: c.ToJsonString())).Where(c => c.Id.Length > 0).ToList();
        foreach (var (id, entry) in chats)
        {
            bool fetch;
            lock (gate) fetch = !copy.ContainsKey(ChatKey(id)) || !listed.TryGetValue("c:" + id, out var was) || was != entry;
            if (!fetch) continue;
            if (await library.GetAsync("/chats/" + Uri.EscapeDataString(id), ct) is { } chat) await PutAsync(ChatKey(id), chat, ct);
            lock (gate) listed["c:" + id] = entry;
        }
        await DropGoneAsync("GET /chats/", chats.Select(c => c.Id).ToHashSet(StringComparer.Ordinal), ct);

        if (first)
        {
            // Copies left from before that nothing here is any more (another install's, or a lecture gone while away).
            List<string> stray;
            lock (gate)
            {
                stray = [.. found.Values];
                found.Clear();
            }
            foreach (string file in stray) await drive.DeleteAsync(file, ct);
            FirstCopy.TrySetResult();
        }
        // A round that went through: whatever was said about the first copy or Drive being away is over.
        Say(null);
    }

    /// <summary>Copies under <paramref name="prefix"/> ("GET /lectures/") of things the library no longer has.</summary>
    async Task DropGoneAsync(string prefix, HashSet<string> still, CancellationToken ct)
    {
        List<(string Key, string File)> gone;
        lock (gate)
            gone = [.. copy.Where(kv => kv.Key.StartsWith(prefix, StringComparison.Ordinal)
                    && !still.Contains(Uri.UnescapeDataString(kv.Key[prefix.Length..].Split('/')[0])))
                .Select(kv => (kv.Key, kv.Value.File))];
        foreach (var (key, file) in gone)
        {
            await drive.DeleteAsync(file, ct);
            lock (gate)
            {
                copy.Remove(key);
                listed.Remove((prefix.Contains("chats") ? "c:" : "l:") + Uri.UnescapeDataString(key[prefix.Length..].Split('/')[0]));
                laptopDirty = true;
            }
        }
    }

    /// <summary>Every lecture, newest first. The library gives at most 500 at a time, so a bigger library is asked
    /// page by page (each from the last one's date, that date included, as two lectures can share it).</summary>
    async Task<(byte[] Bytes, JsonArray Lectures)> AllLecturesAsync(CancellationToken ct)
    {
        const int Page = 500;
        byte[] first = await library.GetAsync($"/lectures?limit={Page}", ct) ?? "[]"u8.ToArray();
        var all = JsonNode.Parse(first) as JsonArray ?? [];
        if (all.Count < Page) return (first, all);
        var seen = all.OfType<JsonObject>().Select(l => Py.AsString(l["id"]) ?? "").ToHashSet(StringComparer.Ordinal);
        string last = Py.AsString(all[^1]?["date"]) ?? "";
        for (int got = all.Count; got >= Page;)
        {
            byte[]? more = await library.GetAsync($"/lectures?limit={Page}&before={Uri.EscapeDataString(last + "\u0001")}", ct);
            var page = (more is null ? null : JsonNode.Parse(more) as JsonArray) ?? [];
            got = page.Count;
            if (got > 0) last = Py.AsString(page[^1]?["date"]) ?? "";
            int added = 0;
            foreach (var l in page.OfType<JsonObject>().ToList())
                if (seen.Add(Py.AsString(l["id"]) ?? ""))
                {
                    page.Remove(l);
                    all.Add(l);
                    added++;
                }
            if (added == 0) break;
        }
        return (Encoding.UTF8.GetBytes(all.ToJsonString()), all);
    }

    /// <summary>One answer of the library's into the folder, unless the copy there already has exactly it.</summary>
    async Task PutAsync(string key, byte[] body, CancellationToken ct)
    {
        string sha = DriveNames.Sha(body), name = Names.Copy(key);
        string? file;
        lock (gate)
        {
            if (copy.TryGetValue(key, out var had) && had.Sha == sha) return;
            file = had?.File ?? found.GetValueOrDefault(name);
        }
        if (file is not null && !await drive.UpdateAsync(file, body, ct: ct)) file = null;
        file ??= await drive.CreateAsync(name, body, ct: ct);
        lock (gate)
        {
            copy[key] = new CopyEntry(file, sha, body.Length);
            found.Remove(name);
            laptopDirty = true;
            Uploads[name] = Uploads.GetValueOrDefault(name) + 1;
        }
    }
}
