using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using StudyStash.Core;

namespace StudyStash.Library;

/// <summary>A voice memo sent from a phone: the recording, the class and title it was sent with, and how far it's
/// got. <see cref="State"/> is <c>waiting</c> (for a computer that records to take it), <c>transcribing</c> (one
/// has, <see cref="Computer"/>), <c>done</c> (it's a lecture now, <see cref="Lecture"/>) or <c>failed</c>.</summary>
public sealed record VoiceMemo(string Id, string Name, string File, string? Class, string Title, long Size, string Added, string By)
{
    public string State { get; init; } = VoiceMemos.Waiting;
    public string? Computer { get; init; }
    public string? ClaimedAt { get; init; }
    public string? Lecture { get; init; }
    public string? Error { get; init; }

    public JsonObject ToJson() => new()
    {
        ["id"] = Id, ["name"] = Name, ["class"] = Class, ["title"] = Title, ["size"] = Size, ["added"] = Added, ["by"] = By,
        ["state"] = State, ["computer"] = Computer, ["lecture"] = Lecture, ["error"] = Error,
    };
}

/// <summary>
/// The voice memos phones have sent (home/voice-memos/: each recording, and memos.json listing them). The library
/// can't write words down from sound itself (Whisper runs where lectures are recorded), so a computer that records
/// takes each one (<see cref="Claim"/>), fetches the recording, writes it down as a lecture of its own and says which
/// (<see cref="Finish"/>). A memo one took but never finished goes back to waiting after <see cref="ClaimLasts"/>.
/// </summary>
public sealed class VoiceMemos(string home, Func<DateTimeOffset>? clock = null)
{
    public const string Waiting = "waiting", Transcribing = "transcribing", Done = "done", Failed = "failed";

    /// <summary>The longest a memo may be: 500 MB is hours of phone recording.</summary>
    public const long MaxBytes = 500L * 1024 * 1024;

    public static readonly TimeSpan ClaimLasts = TimeSpan.FromHours(2);

    /// <summary>What a phone's recording can be, by its name's ending: Voice Memos saves .m4a.</summary>
    public static readonly string[] Endings = [".m4a", ".mp3", ".wav", ".aac", ".caf", ".mp4", ".aif", ".aiff", ".qta"];

    static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
    readonly Lock gate = new();
    readonly Func<DateTimeOffset> now = clock ?? (() => DateTimeOffset.UtcNow);

    public string Dir => Path.Combine(home, "voice-memos");
    string Index => Path.Combine(Dir, "memos.json");

    public static bool IsAudio(string name) => Endings.Contains(Path.GetExtension(name).ToLowerInvariant());

    List<VoiceMemo> Read()
    {
        try
        {
            return System.IO.File.Exists(Index) ? JsonSerializer.Deserialize<List<VoiceMemo>>(System.IO.File.ReadAllText(Index), Json) ?? [] : [];
        }
        catch (Exception e) when (e is JsonException or IOException)
        {
            return [];
        }
    }

    void Write(List<VoiceMemo> memos)
    {
        Directory.CreateDirectory(Dir);
        string tmp = Index + ".tmp";
        System.IO.File.WriteAllText(tmp, JsonSerializer.Serialize(memos, Json));
        System.IO.File.Move(tmp, Index, overwrite: true);
    }

    string Stamp() => now().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    /// <summary>Every memo, newest first. A claim that ran out is waiting again.</summary>
    public List<VoiceMemo> List()
    {
        lock (gate)
        {
            var memos = Read();
            bool changed = false;
            for (int i = 0; i < memos.Count; i++)
                if (memos[i] is { State: Transcribing, ClaimedAt: { } at } m
                    && DateTimeOffset.TryParse(at, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var claimed) && now() - claimed > ClaimLasts)
                {
                    memos[i] = m with { State = Waiting, Computer = null, ClaimedAt = null };
                    changed = true;
                }
            if (changed) Write(memos);
            return [.. memos.OrderByDescending(m => m.Added, StringComparer.Ordinal)];
        }
    }

    public VoiceMemo? Get(string id) => List().FirstOrDefault(m => m.Id == id);

    public string PathOf(VoiceMemo m) => Path.Combine(Dir, m.File);

    /// <summary>A recording that's arrived (<paramref name="staged"/>, moved in here), waiting for a computer.</summary>
    public VoiceMemo Add(string staged, string sentName, long size, string? className, string? title, string by)
    {
        lock (gate)
        {
            Directory.CreateDirectory(Dir);
            string id = Http.TokenUrlSafe(9);
            string ending = Path.GetExtension(sentName).ToLowerInvariant();
            string file = id + (IsAudio(sentName) ? ending : ".m4a");
            System.IO.File.Move(staged, Path.Combine(Dir, file));
            string name = Attachments.SafeName(sentName);
            string plainTitle = string.IsNullOrWhiteSpace(title) ? Path.GetFileNameWithoutExtension(name) : title.Trim();
            var memo = new VoiceMemo(id, name, file, string.IsNullOrWhiteSpace(className) ? null : className.Trim(), Py.Head(plainTitle, 120), size, Stamp(), by);
            var memos = Read();
            memos.Add(memo);
            Write(memos);
            return memo;
        }
    }

    /// <summary>A computer takes a waiting memo to write down; null when it isn't waiting (another took it).</summary>
    public VoiceMemo? Claim(string id, string computer)
    {
        List();
        lock (gate)
        {
            var memos = Read();
            int at = memos.FindIndex(m => m.Id == id);
            if (at < 0 || memos[at].State != Waiting) return null;
            memos[at] = memos[at] with { State = Transcribing, Computer = computer, ClaimedAt = Stamp(), Error = null };
            Write(memos);
            return memos[at];
        }
    }

    /// <summary>The computer that took it says how it went: the lecture it became, or why not. Once it's a lecture the
    /// recording itself goes (the computer has its own copy); a failed one keeps it, to try again.</summary>
    public VoiceMemo? Finish(string id, string? lecture, string? error)
    {
        lock (gate)
        {
            var memos = Read();
            int at = memos.FindIndex(m => m.Id == id);
            if (at < 0) return null;
            var m = memos[at];
            memos[at] = lecture is { Length: > 0 }
                ? m with { State = Done, Lecture = lecture, Error = null }
                : m with { State = Failed, Error = string.IsNullOrWhiteSpace(error) ? "It couldn't be written down." : Py.Head(error.Trim(), 300) };
            Write(memos);
            if (memos[at].State == Done) TryDelete(PathOf(m));
            return memos[at];
        }
    }

    /// <summary>A failed memo, waiting again.</summary>
    public VoiceMemo? Retry(string id)
    {
        lock (gate)
        {
            var memos = Read();
            int at = memos.FindIndex(m => m.Id == id);
            if (at < 0 || memos[at].State != Failed || !System.IO.File.Exists(PathOf(memos[at]))) return null;
            memos[at] = memos[at] with { State = Waiting, Computer = null, ClaimedAt = null, Error = null };
            Write(memos);
            return memos[at];
        }
    }

    public bool Remove(string id)
    {
        lock (gate)
        {
            var memos = Read();
            var m = memos.FirstOrDefault(x => x.Id == id);
            if (m is null) return false;
            memos.Remove(m);
            Write(memos);
            TryDelete(PathOf(m));
            return true;
        }
    }

    static void TryDelete(string path)
    {
        try
        {
            System.IO.File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }
}
