using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace StudyStash.Core.PhoneDrive;

/// <summary>The phones' boxes: read, and what they ask done against the library, each answer written back.</summary>
public sealed partial class DriveLink
{
    /// <summary>A phone's box as read: who it is, whether it's open, and what it asks.</summary>
    internal sealed record ParsedBox(string Name, string Model, DateTimeOffset? OpenUntil, List<PhoneRequest> Requests);

    /// <summary>One request in a box (docs/phone-google.md, "Requests").</summary>
    internal sealed record PhoneRequest(string Id, string Method, string Path, JsonNode? Json, JsonObject? Upload, string? Answer);

    [GeneratedRegex("^[A-Za-z0-9_-]{1,100}$")]
    private static partial Regex RequestId();

    internal static ParsedBox? Parse(byte[] bytes)
    {
        JsonObject? o;
        try
        {
            o = JsonNode.Parse(bytes) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
        if (o is null) return null;
        var phone = o["phone"] as JsonObject;
        string name = Py.Strip(Py.AsString(phone?["name"]) ?? "").ReplaceLineEndings(" ");
        var requests = new List<PhoneRequest>();
        foreach (var r in (o["requests"] as JsonArray ?? []).OfType<JsonObject>())
        {
            string id = Py.AsString(r["id"]) ?? "";
            if (!RequestId().IsMatch(id)) continue;
            requests.Add(new PhoneRequest(id, Py.AsString(r["method"]) ?? "", Py.AsString(r["path"]) ?? "", r["json"],
                r["upload"] as JsonObject, Py.AsString(r["answer"]) is { Length: > 0 } a ? a : null));
        }
        return new ParsedBox(name.Length > 0 ? Py.Head(name, 60) : "Phone", Py.AsString(phone?["model"]) ?? "", When(o["open_until"]), requests);
    }

    static DateTimeOffset? When(JsonNode? n) =>
        DateTimeOffset.TryParse(Py.AsString(n), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d) ? d : null;

    async Task BoxLoop(CancellationToken stop)
    {
        var nextScan = DateTimeOffset.MinValue;
        var started = now();
        while (await Pause(Times.Tick, stop))
        {
            if (!active() || Trouble is not null) continue;
            try
            {
                if (now() >= nextScan)
                {
                    await ScanBoxesAsync(stop);
                    nextScan = now() + (now() - started < Times.Soon ? Times.NewBoxesSoon : Times.NewBoxes);
                }
                List<(string Phone, Box Box)> due;
                lock (gate)
                    due = [.. boxes.Where(b => !removed.Contains(b.Key)
                        && now() - b.Value.Read >= (b.Value.Phone?.OpenAt(now()) == true ? Times.OpenBox : Times.ClosedBox)).Select(b => (b.Key, b.Value))];
                await Task.WhenAll(due.Select(d => ReadBoxAsync(d.Phone, d.Box, stop)));
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

    /// <summary>New boxes in the folder, and boxes that went (a phone signed out, or was removed).</summary>
    async Task ScanBoxesAsync(CancellationToken ct)
    {
        var files = await drive.ListAsync(Names.Phones, ct);
        bool changed = false;
        lock (gate)
        {
            var there = new HashSet<string>(StringComparer.Ordinal);
            foreach (var f in files)
            {
                if (Names.PhoneOf(f.Name) is not { } phone) continue;
                there.Add(phone);
                if (boxes.TryGetValue(phone, out var had) && had.File == f.Id) continue;
                boxes[phone] = new Box { File = f.Id };
                changed = true;
            }
            foreach (var gone in boxes.Keys.Where(k => !there.Contains(k)).ToList())
            {
                boxes.Remove(gone);
                changed = true;
            }
        }
        if (changed) Changed?.Invoke();
    }

    async Task ReadBoxAsync(string phone, Box box, CancellationToken ct)
    {
        box.Read = now();
        var bytes = await drive.ReadBytesAsync(box.File, ct);
        if (bytes is null)
        {
            lock (gate) boxes.Remove(phone);
            Changed?.Invoke();
            return;
        }
        if (Parse(bytes) is not { } parsed) return;
        var seen = new DrivePhone(phone, parsed.Name, parsed.Model, parsed.OpenUntil, now());
        bool changed = box.Phone is not { } was || was.Name != seen.Name || was.OpenUntil != seen.OpenUntil;
        if (changed) box.Phone = seen;
        else if (box.Phone is { } p && p.OpenAt(now()) != seen.OpenAt(now())) changed = true;
        bool first = box.First;
        box.First = false;
        if (changed) Changed?.Invoke();
        lock (gate) box.Waiting = [.. parsed.Requests.Select(r => r.Id).Where(id => !answeredSet.Contains(id))];
        // A phone's changes are done one at a time, in the order it asked (a recording, then a page attached to it),
        // as they are when it asks over Tailscale; reads and streamed answers don't wait for them.
        bool writeAhead = false;
        foreach (var r in parsed.Requests)
        {
            bool write = PhoneCalls.Check(r.Method, r.Path) is { Changes: true, Kind: not PhoneAnswerKind.Stream };
            bool answered;
            lock (gate) answered = answeredSet.Contains(r.Id);
            if (write && !answered)
            {
                if (writeAhead) continue;
                writeAhead = true;
            }
            Consider(phone, parsed.Name, r, first, ct);
        }
    }

    /// <summary>Starts a request, unless it's been done, is being done, or its phone was removed.</summary>
    void Consider(string phone, string phoneName, PhoneRequest r, bool afterStart, CancellationToken ct)
    {
        lock (gate)
        {
            if (removed.Contains(phone) || answeredSet.Contains(r.Id) || doing.Contains(r.Id)) return;
            if (notBefore.TryGetValue(r.Id, out var at) && at > now()) return;
            doing.Add(r.Id);
        }
        var task = Task.Run(() => DoAsync(phone, phoneName, r, afterStart, ct), CancellationToken.None);
        lock (gate) working.Add(task);
        _ = task.ContinueWith(t =>
        {
            lock (gate) working.Remove(t);
        }, TaskScheduler.Default);
    }

    async Task DoAsync(string phone, string phoneName, PhoneRequest r, bool afterStart, CancellationToken ct)
    {
        bool done = false;
        PhoneCall? call = null;
        try
        {
            await requests.WaitAsync(ct);
            try
            {
                // Seen for the first time since the link started: it may have been answered just before a restart.
                if (afterStart && r.Answer is { } prior && await drive.ReadBytesAsync(prior, ct) is { } was && IsDone(was))
                {
                    done = true;
                    return;
                }
                string answer = r.Answer ?? await drive.CreateAsync(Names.Answer(r.Id), "{}"u8.ToArray(), ct: ct);
                call = PhoneCalls.Check(r.Method, r.Path);
                if (call is null)
                {
                    log($"[drive] refused {Py.Head(r.Method, 10)} from {LibraryCalls.Ascii(phoneName)}: not one of the phone's calls");
                    await WriteAnswerAsync(answer, Answer(r.Id, 403, "application/json", Detail(PhoneCalls.Refused)), ct);
                    done = true;
                    return;
                }
                int status = await ReplayAsync(call, r, phoneName, answer, ct);
                log($"[drive] {call.Method} {call.Route} from {LibraryCalls.Ascii(phoneName)}: {status}");
                done = true;
            }
            finally
            {
                requests.Release();
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception e) when (Expected(e))
        {
            // The library here isn't up (starting, or stopped) or Drive is away: tried again in a moment.
            log($"[drive] {call?.Route ?? "a request"}: not done yet ({e.GetType().Name}); trying again soon");
            lock (gate) notBefore[r.Id] = now() + TimeSpan.FromSeconds(5);
        }
        finally
        {
            lock (gate)
            {
                doing.Remove(r.Id);
                if (done)
                {
                    Answered(r.Id);
                    notBefore.Remove(r.Id);
                    laptopDirty = true;
                    // The phone's next change may be waiting behind this one: its box is read again straight away.
                    if (call is { Changes: true } && boxes.TryGetValue(phone, out var box)) box.Read = DateTimeOffset.MinValue;
                }
            }
            if (done && call is { Changes: true }) CopySoon(call.Lecture);
        }
    }

    static bool IsDone(byte[] answer)
    {
        try
        {
            return JsonNode.Parse(answer) is JsonObject o && o["done"] is JsonValue v && v.TryGetValue(out bool d) && d;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    static JsonObject Detail(string words) => new() { ["detail"] = words };

    static JsonObject Answer(string id, int status, string type, JsonNode? body) =>
        new() { ["id"] = id, ["status"] = status, ["type"] = type, ["body"] = body, ["done"] = true };

    async Task WriteAnswerAsync(string file, JsonObject answer, CancellationToken ct)
    {
        // A phone that gave up on it deleted its answer file: there's nobody to tell.
        await drive.UpdateAsync(file, Encoding.UTF8.GetBytes(answer.ToJsonString()), ct: ct);
    }

    /// <summary>
    /// The request, sent to the library as the phone would have sent it (its JSON, or the file it uploaded as the
    /// multipart form the iPhone app makes), as that phone; the answer written back as it comes. The library's status.
    /// </summary>
    async Task<int> ReplayAsync(PhoneCall call, PhoneRequest r, string phoneName, string answer, CancellationToken ct)
    {
        HttpContent? content = null;
        FileStream? upload = null;
        string? uploadFile = null;
        try
        {
            if (call.Multipart)
            {
                uploadFile = Py.AsString(r.Upload?["file"]);
                upload = TempFile();
                if (uploadFile is null || !await drive.ReadAsync(uploadFile, upload, ct))
                {
                    await WriteAnswerAsync(answer, Answer(r.Id, 400, "application/json", Detail("The file didn't arrive. Send it again.")), ct);
                    return 400;
                }
                upload.Position = 0;
                var form = new MultipartFormDataContent("StudyStash-" + Guid.NewGuid().ToString("N"));
                foreach (var (k, v) in r.Upload!["fields"] as JsonObject ?? [])
                    if (Py.AsString(v) is { Length: > 0 } text) form.Add(new StringContent(text, Encoding.UTF8), k);
                var file = new StreamContent(upload);
                file.Headers.ContentType = MediaTypeHeaderValue.TryParse(Py.AsString(r.Upload["type"]), out var t) ? t : new MediaTypeHeaderValue("application/octet-stream");
                form.Add(file, "file", Py.AsString(r.Upload["name"]) is { Length: > 0 } n ? n : "file");
                content = form;
            }
            else if (call.Method == "POST")
            {
                var body = r.Json?.DeepClone() ?? new JsonObject();
                // A lecture recorded on the phone is that phone's, as it is when it comes over Tailscale.
                if (call.Route == "/lectures" && body is JsonObject o && o["owner"] is null) o["owner"] = phoneName;
                content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
            }
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
            limit.CancelAfter(call.Kind == PhoneAnswerKind.Stream || call.Multipart ? TimeSpan.FromMinutes(15) : TimeSpan.FromMinutes(2));
            using var response = await library.SendAsync(new HttpMethod(call.Method), call.Path, content, phoneName, limit.Token);
            int status = (int)response.StatusCode;
            string type = response.Content.Headers.ContentType?.MediaType ?? "application/json";
            if (call.Kind == PhoneAnswerKind.Stream && response.IsSuccessStatusCode && type == "application/x-ndjson")
                await StreamAsync(r.Id, status, type, response, answer, limit.Token);
            else if (call.Kind == PhoneAnswerKind.Blob && response.IsSuccessStatusCode)
                await BlobAsync(r.Id, status, type, response, answer, limit.Token);
            else
            {
                byte[] bytes = await response.Content.ReadAsByteArrayAsync(limit.Token);
                JsonNode? body;
                try
                {
                    body = bytes.Length == 0 ? null : JsonNode.Parse(bytes);
                }
                catch (JsonException)
                {
                    body = response.IsSuccessStatusCode ? JsonValue.Create(Encoding.UTF8.GetString(bytes)) : Detail(Encoding.UTF8.GetString(bytes));
                }
                await WriteAnswerAsync(answer, Answer(r.Id, status, type, body), limit.Token);
            }
            // Used: the phone's upload goes (a refused one too, as the phone sends it again with a new request).
            if (uploadFile is not null) await drive.DeleteAsync(uploadFile, ct);
            return status;
        }
        finally
        {
            content?.Dispose();
            upload?.Dispose();
        }
    }

    static FileStream TempFile() => new(Path.Combine(Path.GetTempPath(), "studystash-drive-" + Guid.NewGuid().ToString("N")),
        FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920, FileOptions.DeleteOnClose | FileOptions.Asynchronous);

    /// <summary>A streamed reply (one JSON object a line), written as it comes: the lines so far at most once a
    /// second, and all of them the moment it ends, <c>done</c>.</summary>
    async Task StreamAsync(string id, int status, string type, HttpResponseMessage response, string answer, CancellationToken ct)
    {
        var lines = new List<string>();
        bool ended = false;
        var writer = Task.Run(async () =>
        {
            int written = 0;
            var last = DateTimeOffset.MinValue;
            while (true)
            {
                string[]? now_;
                lock (lines)
                {
                    if (ended) return;
                    now_ = lines.Count > written && now() - last >= Times.Write ? [.. lines] : null;
                }
                if (now_ is null)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(50), ct);
                    continue;
                }
                last = now();
                written = now_.Length;
                await WriteAnswerAsync(answer, Lines(id, status, type, now_, done: false), ct);
            }
        }, ct);
        try
        {
            using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(ct), Encoding.UTF8);
            while (await reader.ReadLineAsync(ct) is { } line)
                if (line.Trim().Length > 0)
                    lock (lines) lines.Add(line);
        }
        catch (IOException)
        {
            // The library stopped part-way: what came is the answer.
        }
        finally
        {
            lock (lines) ended = true;
        }
        await writer;
        string[] all;
        lock (lines) all = [.. lines];
        await WriteAnswerAsync(answer, Lines(id, status, type, all, done: true), ct);
    }

    static JsonObject Lines(string id, int status, string type, string[] lines, bool done) => new()
    {
        ["id"] = id, ["status"] = status, ["type"] = type, ["lines"] = new JsonArray([.. lines.Select(l => (JsonNode?)l)]), ["done"] = done,
    };

    /// <summary>A file's bytes (an attachment's raw) into a blob- file, named by their SHA-256, so asking again for
    /// the same file the same day sends nothing new.</summary>
    async Task BlobAsync(string id, int status, string type, HttpResponseMessage response, string answer, CancellationToken ct)
    {
        await using var temp = TempFile();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using (var from = await response.Content.ReadAsStreamAsync(ct))
        {
            var buffer = new byte[81920];
            int n;
            while ((n = await from.ReadAsync(buffer, ct)) > 0)
            {
                hash.AppendData(buffer, 0, n);
                await temp.WriteAsync(buffer.AsMemory(0, n), ct);
            }
        }
        string sha = Convert.ToHexStringLower(hash.GetHashAndReset());
        string key = sha[..16];
        string? file;
        lock (gate) file = blobs.TryGetValue(key, out var had) && now() - had.Made < TimeSpan.FromHours(12) ? had.Id : null;
        if (file is null)
        {
            temp.Position = 0;
            file = await drive.CreateAsync(Names.Blob(sha), temp, type, ct);
            lock (gate) blobs[key] = (file, now());
        }
        await WriteAnswerAsync(answer, new JsonObject { ["id"] = id, ["status"] = status, ["type"] = type, ["blob"] = file, ["done"] = true }, ct);
    }
}
