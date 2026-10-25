namespace StudyStash.Core.PhoneDrive;

/// <summary>What a phone's request comes to once it's checked against <see cref="PhoneCalls"/>: the path to send the
/// library (each segment checked and encoded again), how its answer is kept, and whether it changes the library.</summary>
public sealed record PhoneCall(string Method, string Path, string Route, PhoneAnswerKind Kind, bool Changes, bool Multipart)
{
    /// <summary>The lecture it's about (a "/lectures/{id}…" route), so the copy of it goes first; null otherwise.</summary>
    public string? Lecture { get; init; }
}

/// <summary>How an answer goes back: as JSON in the answer file, a streamed reply a line at a time (when the library
/// streams it), or the bytes in a blob- file.</summary>
public enum PhoneAnswerKind
{
    Json,
    Stream,
    Blob,
}

/// <summary>
/// Exactly the iPhone app's own calls (ios/StudyStash/API/APIClient.swift), and nothing else: never settings,
/// passwords, AI engines or keys, Canvas tokens, devices, Claude connections, history, files, or anything outside
/// /api/v2. A request whose method and path aren't one of these, or whose path has anything odd in it (a "..", a
/// slash or backslash inside a segment, a fragment), is refused before the library hears of it.
/// </summary>
public static class PhoneCalls
{
    /// <summary>What a refused request is answered (403).</summary>
    public const string Refused = "Your laptop doesn't take that from the phone.";

    static readonly (string Method, string Route, PhoneAnswerKind Kind, bool Multipart)[] List =
    [
        ("GET", "/me", PhoneAnswerKind.Json, false),
        ("GET", "/library", PhoneAnswerKind.Json, false),
        ("GET", "/lectures", PhoneAnswerKind.Json, false),
        ("GET", "/lectures/{}", PhoneAnswerKind.Json, false),
        ("GET", "/lectures/{}/rendered", PhoneAnswerKind.Json, false),
        ("GET", "/search", PhoneAnswerKind.Json, false),
        ("GET", "/chats", PhoneAnswerKind.Json, false),
        ("GET", "/chats/{}", PhoneAnswerKind.Json, false),
        ("GET", "/canvas/due", PhoneAnswerKind.Json, false),
        ("GET", "/canvas/state", PhoneAnswerKind.Json, false),
        ("GET", "/canvas/assignment", PhoneAnswerKind.Json, false),
        ("GET", "/calendar/upcoming", PhoneAnswerKind.Json, false),
        ("GET", "/attachments", PhoneAnswerKind.Json, false),
        ("GET", "/attachments/{}/raw", PhoneAnswerKind.Blob, false),
        ("GET", "/voice-memos", PhoneAnswerKind.Json, false),
        ("POST", "/lectures", PhoneAnswerKind.Json, false),
        ("POST", "/lectures/{}/class", PhoneAnswerKind.Json, false),
        ("POST", "/lectures/{}/title", PhoneAnswerKind.Json, false),
        ("POST", "/lectures/{}/restore", PhoneAnswerKind.Json, false),
        ("POST", "/lectures/{}/rewrite", PhoneAnswerKind.Json, false),
        ("POST", "/ai/ask", PhoneAnswerKind.Stream, false),
        ("POST", "/ai/warm", PhoneAnswerKind.Json, false),
        // The Due tab's "sync now": DriveLink sends only {"sync": true}, never Canvas's address or which course is which class.
        ("POST", "/canvas", PhoneAnswerKind.Json, false),
        ("POST", "/chat", PhoneAnswerKind.Stream, false),
        ("POST", "/classes/alias", PhoneAnswerKind.Json, false),
        ("POST", "/uses", PhoneAnswerKind.Json, false),
        ("POST", "/attachments", PhoneAnswerKind.Json, true),
        ("POST", "/voice-memos", PhoneAnswerKind.Json, true),
        ("POST", "/voice-memos/{}/retry", PhoneAnswerKind.Json, false),
        ("DELETE", "/lectures/{}", PhoneAnswerKind.Json, false),
        ("DELETE", "/chats/{}", PhoneAnswerKind.Json, false),
        ("DELETE", "/attachments/{}", PhoneAnswerKind.Json, false),
        ("DELETE", "/voice-memos/{}", PhoneAnswerKind.Json, false),
    ];

    /// <summary>The call, or null when the laptop doesn't take it from the phone. <paramref name="path"/> is the path
    /// after /api/v2 with its query, as the phone's APIClient makes it ("/lectures/rec-abc", "/search?q=cells").</summary>
    public static PhoneCall? Check(string? method, string? path)
    {
        if (method is null || path is null || path.Length is 0 or > 2048 || path[0] != '/') return null;
        if (path.Contains('#') || path.Any(c => c < 0x21 || c > 0x7e)) return null;
        int q = path.IndexOf('?');
        string query = q >= 0 ? path[(q + 1)..] : "";
        if (query.Any(c => !QueryChar(c))) return null;
        string[] given = (q >= 0 ? path[..q] : path)[1..].Split('/');
        var decoded = new string[given.Length];
        for (int i = 0; i < given.Length; i++)
        {
            string s;
            try
            {
                s = Uri.UnescapeDataString(given[i]);
            }
            catch (UriFormatException)
            {
                return null;
            }
            if (s.Length is 0 or > 300 || s is "." or ".." || s.Any(c => c is '/' or '\\' || char.IsControl(c))) return null;
            decoded[i] = s;
        }
        foreach (var (m, route, kind, multipart) in List)
        {
            if (m != method) continue;
            string[] want = route[1..].Split('/');
            if (want.Length != decoded.Length) continue;
            bool match = true;
            for (int i = 0; i < want.Length && match; i++) match = want[i] == "{}" || want[i] == decoded[i];
            if (!match) continue;
            string clean = "/" + string.Join('/', decoded.Select(Uri.EscapeDataString)) + (q >= 0 ? "?" + query : "");
            bool changes = method != "GET" && route != "/ai/warm";
            return new PhoneCall(method, clean, route, kind, changes, multipart)
            {
                Lecture = want[0] == "lectures" && want.Length >= 2 ? decoded[1] : null,
            };
        }
        return null;
    }

    /// <summary>A character a URL's query may carry as it is (RFC 3986), percent signs included.</summary>
    static bool QueryChar(char c) => char.IsAsciiLetterOrDigit(c) || "-._~%!$&'()*+,;=:@/?".Contains(c);
}
