using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using StudyStash.Core;

namespace StudyStash.Library;

/// <summary>
/// The browsers signed in to the library's pages (sessions.json beside the config). Each sign-in gets a secret of its
/// own for its <c>pool</c> cookie, and only a keyed hash of it is kept here: Log out ends that one sign-in on the
/// library, so a copy of its cookie opens nothing afterwards. A sign-in lasts <see cref="Life"/>, through the
/// library's restarts and updates.
/// </summary>
public sealed class Sessions
{
    public const string Cookie = "pool";
    public static readonly TimeSpan Life = TimeSpan.FromDays(180);
    /// <summary>A library can't keep sign-ins without end: past this many, the oldest goes.</summary>
    public const int MaxSessions = 100;

    static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    sealed class Session
    {
        public string TokenHash { get; set; } = "";
        public DateTimeOffset Started { get; set; }
    }

    sealed class State
    {
        public List<Session> Sessions { get; set; } = [];
    }

    readonly string path;
    readonly byte[] key;
    readonly Func<DateTimeOffset> clock;
    readonly Lock gate = new();
    State state;
    byte[]? loadedBytes;

    public Sessions(string home, Func<DateTimeOffset>? clock = null)
    {
        path = Path.Combine(home, "sessions.json");
        key = WebSecret(home);
        this.clock = clock ?? (() => DateTimeOffset.UtcNow);
        state = Load();
    }

    DateTimeOffset Now => clock();

    /// <summary>The key sign-ins are hashed with, kept in web_secret so they survive restarts and updates. One that
    /// can't be kept lasts until the library stops, and its sign-ins with it.</summary>
    static byte[] WebSecret(string home)
    {
        string path = Path.Combine(home, "web_secret");
        try
        {
            if (File.Exists(path)) return Convert.FromHexString(Py.Strip(Py.ReadText(path)));
            Directory.CreateDirectory(home);
            byte[] key = RandomNumberGenerator.GetBytes(32);
            Py.WriteText(path, Convert.ToHexStringLower(key));
            Py.OwnerOnly(path);
            return key;
        }
        catch (Exception e) when (e is IOException or FormatException or UnauthorizedAccessException)
        {
            return RandomNumberGenerator.GetBytes(32);
        }
    }

    State Load()
    {
        try
        {
            byte[] bytes = File.Exists(path) ? File.ReadAllBytes(path) : [];
            var loaded = bytes.Length == 0 ? new State() : JsonSerializer.Deserialize<State>(bytes, Json) ?? new State();
            loadedBytes = bytes;
            return loaded;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            loadedBytes = null;
            return new State();
        }
    }

    /// <summary>Another copy changed sessions.json (a second library process): read it again, so a Log out there
    /// takes effect here at once. Compares the file's bytes, as <see cref="Devices"/> does. Called under the lock.</summary>
    void Fresh()
    {
        byte[] bytes;
        try { bytes = File.Exists(path) ? File.ReadAllBytes(path) : []; }
        catch (IOException) { return; } // another copy is mid-write; try again next time
        if (loadedBytes is null || !bytes.AsSpan().SequenceEqual(loadedBytes)) state = Load();
    }

    void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string tmp = path + ".tmp";
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(state, Json);
        File.WriteAllBytes(tmp, bytes);
        Py.OwnerOnly(tmp);
        File.Move(tmp, path, overwrite: true);
        loadedBytes = bytes;
    }

    string Hash(string what) => Convert.ToHexStringLower(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(what)));

    static bool Same(string a, string b) => CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));

    /// <summary>Sign-ins that have run their time go. True when any did. Called under the lock.</summary>
    bool Tidy() => state.Sessions.RemoveAll(s => Now - s.Started >= Life) > 0;

    /// <summary>A new sign-in: the secret for its cookie, given out only here.</summary>
    public string Start()
    {
        string token = Http.TokenUrlSafe(32);
        lock (gate)
        {
            Fresh();
            Tidy();
            state.Sessions.Add(new Session { TokenHash = Hash(token), Started = Now });
            while (state.Sessions.Count > MaxSessions) state.Sessions.Remove(state.Sessions.MinBy(s => s.Started)!);
            Save();
        }
        return token;
    }

    /// <summary>Whether a cookie's secret is a sign-in that still stands: made here, not logged out, not run out.</summary>
    public bool Check(string? token)
    {
        if (string.IsNullOrEmpty(token) || token.Length > 200) return false;
        string h = Hash(token);
        lock (gate)
        {
            Fresh();
            if (Tidy()) Save();
            return state.Sessions.Any(s => Same(s.TokenHash, h));
        }
    }

    /// <summary>Log out: that sign-in is over, for every copy of its cookie.</summary>
    public void End(string? token)
    {
        if (string.IsNullOrEmpty(token) || token.Length > 200) return;
        string h = Hash(token);
        lock (gate)
        {
            Fresh();
            if (state.Sessions.RemoveAll(s => Same(s.TokenHash, h)) > 0) Save();
        }
    }
}
