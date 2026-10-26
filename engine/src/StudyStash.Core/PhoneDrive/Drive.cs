using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;

namespace StudyStash.Core.PhoneDrive;

/// <summary>A file in Egghead Notes' hidden folder of the student's Google Drive.</summary>
public sealed record DriveFile(string Id, string Name, DateTimeOffset Modified, long Size);

/// <summary>Egghead Notes' hidden folder in Google Drive (the appDataFolder): the few things the phone link does with
/// it. <see cref="GoogleDrive"/> is the real one; tests keep one in memory.</summary>
public interface IDrive
{
    /// <summary>Every file whose name starts with <paramref name="prefix"/>.</summary>
    Task<List<DriveFile>> ListAsync(string prefix, CancellationToken ct = default);
    /// <summary>A new file: its id.</summary>
    Task<string> CreateAsync(string name, Stream content, string type, CancellationToken ct = default);
    /// <summary>A file's content, replaced. False when there's no such file any more.</summary>
    Task<bool> UpdateAsync(string id, Stream content, string type, CancellationToken ct = default);
    /// <summary>A file's content, copied into <paramref name="into"/>. False when there's no such file.</summary>
    Task<bool> ReadAsync(string id, Stream into, CancellationToken ct = default);
    /// <summary>A file deleted; one that's already gone is no matter.</summary>
    Task DeleteAsync(string id, CancellationToken ct = default);
}

public static class DriveExtensions
{
    public static Task<string> CreateAsync(this IDrive d, string name, byte[] content, string type = "application/json", CancellationToken ct = default) =>
        d.CreateAsync(name, new MemoryStream(content, writable: false), type, ct);

    public static Task<bool> UpdateAsync(this IDrive d, string id, byte[] content, string type = "application/json", CancellationToken ct = default) =>
        d.UpdateAsync(id, new MemoryStream(content, writable: false), type, ct);

    /// <summary>A small file's bytes, or null when there's no such file.</summary>
    public static async Task<byte[]?> ReadBytesAsync(this IDrive d, string id, CancellationToken ct = default)
    {
        using var into = new MemoryStream();
        return await d.ReadAsync(id, into, ct) ? into.ToArray() : null;
    }
}

/// <summary>Google Drive couldn't do what was asked, after its tries: <see cref="Status"/> is Drive's answer (0: no
/// answer at all).</summary>
public sealed class DriveException(int status, string message) : Exception(message)
{
    public int Status { get; } = status;
}

/// <summary>
/// The Drive REST API (v3) over plain HttpClient, kept to the appDataFolder. A busy Drive (429, 5xx, a rate limit's
/// 403) or a dropped connection is tried again after a growing wait (Retry-After when Drive gives one); a 401 gets a
/// fresh access token once. Its log says what was done and how Drive answered, never a token or a file's content.
/// </summary>
public sealed class GoogleDrive(Func<bool, CancellationToken, Task<string>> accessToken, HttpClient? http = null, Action<string>? log = null) : IDrive
{
    const string Api = "https://www.googleapis.com/drive/v3/files";
    const string Upload = "https://www.googleapis.com/upload/drive/v3/files";
    /// <summary>Above this a file goes up in a resumable upload, as Drive asks; below it in one request.</summary>
    const long OneRequestLimit = 5 * 1024 * 1024;
    static readonly HttpClient Shared = new() { Timeout = TimeSpan.FromMinutes(10) };
    readonly HttpClient http = http ?? Shared;
    readonly Action<string> log = log ?? (_ => { });

    /// <summary>How many times a busy Drive is tried, and the first wait (doubled each time).</summary>
    public int Tries { get; init; } = 6;
    public TimeSpan FirstWait { get; init; } = TimeSpan.FromSeconds(0.5);

    static string Quote(string s) => s.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("'", "\\'", StringComparison.Ordinal);

    public async Task<List<DriveFile>> ListAsync(string prefix, CancellationToken ct = default)
    {
        var files = new List<DriveFile>();
        string? page = null;
        do
        {
            var query = new List<(string, string)>
            {
                ("spaces", "appDataFolder"), ("pageSize", "1000"), ("fields", "nextPageToken,files(id,name,modifiedTime,size)"),
            };
            if (prefix.Length > 0) query.Add(("q", $"name contains '{Quote(prefix)}' and trashed = false"));
            if (page is not null) query.Add(("pageToken", page));
            var json = await JsonAsync(() => new HttpRequestMessage(HttpMethod.Get, Api + "?" + Py.UrlEncode(query)), "list", ct)
                ?? throw new DriveException(404, "Drive's list wasn't there");
            foreach (var f in (json["files"] as JsonArray ?? []).OfType<JsonObject>())
            {
                string id = Py.AsString(f["id"]) ?? "", name = Py.AsString(f["name"]) ?? "";
                if (id.Length == 0 || !name.StartsWith(prefix, StringComparison.Ordinal)) continue;
                var modified = DateTimeOffset.TryParse(Py.AsString(f["modifiedTime"]), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var m) ? m : DateTimeOffset.MinValue;
                long size = long.TryParse(Py.AsString(f["size"]), NumberStyles.Integer, CultureInfo.InvariantCulture, out long s) ? s : 0;
                files.Add(new DriveFile(id, name, modified, size));
            }
            page = Py.AsString(json["nextPageToken"]);
        }
        while (page is { Length: > 0 });
        return files;
    }

    public async Task<string> CreateAsync(string name, Stream content, string type, CancellationToken ct = default)
    {
        var meta = new JsonObject { ["name"] = name, ["parents"] = new JsonArray("appDataFolder"), ["mimeType"] = type }.ToJsonString();
        JsonObject? made;
        if (content.Length <= OneRequestLimit)
        {
            made = await JsonAsync(() =>
            {
                content.Position = 0;
                var body = new MultipartContent("related", "studystash" + Guid.NewGuid().ToString("N"))
                {
                    new StringContent(meta, Encoding.UTF8, "application/json"),
                    Unclosed(content, type),
                };
                return new HttpRequestMessage(HttpMethod.Post, Upload + "?uploadType=multipart&fields=id") { Content = body };
            }, "create", ct);
        }
        else
        {
            string at = await StartResumableAsync(HttpMethod.Post, Upload + "?uploadType=resumable&fields=id", meta, type, content.Length, ct)
                ?? throw new DriveException(404, "Drive didn't start the upload");
            made = await JsonAsync(() =>
            {
                content.Position = 0;
                return new HttpRequestMessage(HttpMethod.Put, at) { Content = Unclosed(content, type) };
            }, "create", ct);
        }
        return Py.AsString(made?["id"]) ?? throw new DriveException(0, "Drive made the file but gave no id");
    }

    public async Task<bool> UpdateAsync(string id, Stream content, string type, CancellationToken ct = default)
    {
        string url = $"{Upload}/{Uri.EscapeDataString(id)}";
        if (content.Length <= OneRequestLimit)
            return await JsonAsync(() =>
            {
                content.Position = 0;
                return new HttpRequestMessage(HttpMethod.Patch, url + "?uploadType=media&fields=id") { Content = Unclosed(content, type) };
            }, "update", ct) is not null;
        string? at = await StartResumableAsync(HttpMethod.Patch, url + "?uploadType=resumable&fields=id", "{}", type, content.Length, ct);
        if (at is null) return false;
        return await JsonAsync(() =>
        {
            content.Position = 0;
            return new HttpRequestMessage(HttpMethod.Put, at) { Content = Unclosed(content, type) };
        }, "update", ct) is not null;
    }

    public async Task<bool> ReadAsync(string id, Stream into, CancellationToken ct = default)
    {
        long start = into.CanSeek ? into.Position : 0;
        using var r = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, $"{Api}/{Uri.EscapeDataString(id)}?alt=media"), "read", ct,
            HttpCompletionOption.ResponseHeadersRead);
        if (r.StatusCode == HttpStatusCode.NotFound) return false;
        if (into.CanSeek)
        {
            into.Position = start;
            into.SetLength(start);
        }
        await r.Content.CopyToAsync(into, ct);
        return true;
    }

    public async Task DeleteAsync(string id, CancellationToken ct = default)
    {
        using var _ = await SendAsync(() => new HttpRequestMessage(HttpMethod.Delete, $"{Api}/{Uri.EscapeDataString(id)}"), "delete", ct);
    }

    /// <summary>A resumable upload's own address (Drive's Location), or null when the file isn't there to update.</summary>
    async Task<string?> StartResumableAsync(HttpMethod method, string url, string meta, string type, long length, CancellationToken ct)
    {
        using var r = await SendAsync(() =>
        {
            var m = new HttpRequestMessage(method, url) { Content = new StringContent(meta, Encoding.UTF8, "application/json") };
            m.Headers.TryAddWithoutValidation("X-Upload-Content-Type", type);
            m.Headers.TryAddWithoutValidation("X-Upload-Content-Length", length.ToString(CultureInfo.InvariantCulture));
            return m;
        }, "start upload", ct);
        if (r.StatusCode == HttpStatusCode.NotFound) return null;
        return r.Headers.Location?.ToString() ?? throw new DriveException((int)r.StatusCode, "Drive didn't say where to upload");
    }

    /// <summary>The stream as a request's content, left open: a request that's tried again sends it again.</summary>
    static StreamContent Unclosed(Stream s, string type)
    {
        var c = new StreamContent(new KeepOpen(s));
        c.Headers.ContentType = MediaTypeHeaderValue.Parse(type);
        c.Headers.ContentLength = s.Length - s.Position;
        return c;
    }

    async Task<JsonObject?> JsonAsync(Func<HttpRequestMessage> make, string what, CancellationToken ct)
    {
        using var r = await SendAsync(make, what, ct);
        if (r.StatusCode == HttpStatusCode.NotFound) return null;
        string text = await r.Content.ReadAsStringAsync(ct);
        return JsonNode.Parse(text.Length > 0 ? text : "{}") as JsonObject ?? new JsonObject();
    }

    /// <summary>Sends, tries again while Drive is busy or away, and gets a fresh token once on a 401. Hands back a
    /// success or a 404; anything else is thrown as <see cref="DriveException"/>.</summary>
    async Task<HttpResponseMessage> SendAsync(Func<HttpRequestMessage> make, string what, CancellationToken ct,
        HttpCompletionOption completion = HttpCompletionOption.ResponseContentRead)
    {
        bool refreshed = false;
        for (int attempt = 1; ; attempt++)
        {
            using var request = make();
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await accessToken(false, ct));
            HttpResponseMessage r;
            try
            {
                r = await http.SendAsync(request, completion, ct);
            }
            catch (Exception e) when ((e is HttpRequestException or IOException || (e is TaskCanceledException && !ct.IsCancellationRequested)) && attempt < Tries)
            {
                log($"[drive] {what}: no answer ({e.GetType().Name}); trying again");
                await Task.Delay(Wait(attempt, null), ct);
                continue;
            }
            catch (Exception e) when (e is HttpRequestException or IOException || (e is TaskCanceledException && !ct.IsCancellationRequested))
            {
                throw new DriveException(0, $"Google Drive didn't answer ({what})");
            }
            int status = (int)r.StatusCode;
            if (r.IsSuccessStatusCode || status == 404) return r;
            if (status == 401 && !refreshed)
            {
                r.Dispose();
                refreshed = true;
                await accessToken(true, ct);
                attempt--;
                continue;
            }
            bool busy = status is 429 or 500 or 502 or 503 or 504 || (status == 403 && await RateLimitedAsync(r, ct));
            if (busy && attempt < Tries)
            {
                var after = r.Headers.RetryAfter?.Delta;
                r.Dispose();
                log($"[drive] {what}: {status}; trying again");
                await Task.Delay(Wait(attempt, after), ct);
                continue;
            }
            r.Dispose();
            log($"[drive] {what}: {status}");
            throw new DriveException(status, $"Google Drive said {status} ({what})");
        }
    }

    TimeSpan Wait(int attempt, TimeSpan? after)
    {
        var grown = FirstWait * Math.Pow(2, attempt - 1) * (0.8 + Random.Shared.NextDouble() * 0.4);
        return after is { } a && a > grown ? (a > TimeSpan.FromMinutes(1) ? TimeSpan.FromMinutes(1) : a) : grown;
    }

    static async Task<bool> RateLimitedAsync(HttpResponseMessage r, CancellationToken ct)
    {
        string text = await r.Content.ReadAsStringAsync(ct);
        return text.Contains("rateLimitExceeded", StringComparison.Ordinal) || text.Contains("userRateLimitExceeded", StringComparison.Ordinal);
    }

    /// <summary>A stream that the request's content can't close.</summary>
    sealed class KeepOpen(Stream inner) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => inner.Position = value; }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => inner.ReadAsync(buffer, cancellationToken);
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { }
    }
}

/// <summary>
/// The access token for <see cref="GoogleDrive"/>: refreshed from the account's refresh token a minute before it runs
/// out, or when Drive says it's no good. One refresh at a time.
/// </summary>
public sealed class GoogleTokens(GoogleSignIn google, string refreshToken)
{
    readonly SemaphoreSlim one = new(1, 1);
    string? token;
    DateTimeOffset until;

    public async Task<string> GetAsync(bool fresh, CancellationToken ct)
    {
        await one.WaitAsync(ct);
        try
        {
            if (fresh || token is null || until - DateTimeOffset.UtcNow < TimeSpan.FromMinutes(1))
                (token, until) = await google.AccessAsync(refreshToken, ct);
            return token;
        }
        finally
        {
            one.Release();
        }
    }
}
