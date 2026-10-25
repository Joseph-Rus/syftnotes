using System.Globalization;
using System.Net.Http.Headers;
using System.Text;

namespace StudyStash.Core.PhoneDrive;

/// <summary>
/// The library on this computer, over its own /api/v2 with its password, the way the app and a laptop reach it: the
/// phone link's requests are replayed through here. <c>where</c> says its address and password at each request (null
/// while there's no library here). A streamed answer (Ask) can run for minutes, so the client has no time limit of
/// its own; each call brings its own.
/// </summary>
public sealed class LibraryCalls(Func<(string Url, string Key)?> where, HttpClient? http = null)
{
    static readonly HttpClient Shared = new(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) }) { Timeout = Timeout.InfiniteTimeSpan };
    readonly HttpClient http = http ?? Shared;

    /// <summary>The library's own header for who's asking: the phone's name, where the library records who did
    /// something (a voice memo's <c>by</c>).</summary>
    public const string WhoHeader = "X-Study-Stash-Computer";

    /// <summary>A request to the library: <paramref name="path"/> is after /api/v2. The answer's headers are in once
    /// this returns (a stream's lines follow). Throws <see cref="HttpRequestException"/> when there's no library here
    /// or it doesn't answer.</summary>
    public async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, HttpContent? content, string? who, CancellationToken ct)
    {
        var (url, key) = where() ?? throw new HttpRequestException("There's no library on this computer.");
        var request = new HttpRequestMessage(method, url.TrimEnd('/') + "/api/v2" + path) { Content = content };
        if (key.Length > 0) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        if (who is { Length: > 0 }) request.Headers.TryAddWithoutValidation(WhoHeader, Ascii(who));
        try
        {
            return await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        finally
        {
            // The content (a temporary file's stream) is the caller's to close; only the message goes.
            request.Content = null;
            request.Dispose();
        }
    }

    /// <summary>A GET's body as the library gave it, or null on a 404. Anything else that isn't a success throws.</summary>
    public async Task<byte[]?> GetAsync(string path, CancellationToken ct)
    {
        using var r = await SendAsync(HttpMethod.Get, path, null, null, ct);
        if (r.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        if (!r.IsSuccessStatusCode) throw new HttpRequestException($"the library said {(int)r.StatusCode} to GET {path}", null, r.StatusCode);
        return await r.Content.ReadAsByteArrayAsync(ct);
    }

    /// <summary>A header can only carry plain ASCII: "Joey’s iPhone" goes as "Joey's iPhone", "José" as "Jose".</summary>
    public static string Ascii(string s)
    {
        var b = new StringBuilder();
        foreach (char c in s.Replace('’', '\'').Replace('‘', '\'').Replace('“', '"').Replace('”', '"').Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            if (c >= 0x20 && c < 0x7f) b.Append(c);
        }
        return b.ToString().Trim() is { Length: > 0 } t ? t : "Phone";
    }
}
