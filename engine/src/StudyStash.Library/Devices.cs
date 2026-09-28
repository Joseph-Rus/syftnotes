using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using StudyStash.Core;

namespace StudyStash.Library;

/// <summary>A phone that paired with the library: its name, when it paired, when it last asked for something, and
/// the hash of the secret its <c>device</c> cookie holds (the secret itself is never kept).</summary>
public sealed class PairedDevice
{
    public required string Id { get; init; }
    public string Name { get; set; } = "";
    public string TokenHash { get; set; } = "";
    public DateTimeOffset Added { get; init; }
    public DateTimeOffset LastSeen { get; set; }
}

/// <summary>
/// The phones that may read the library (devices.json beside the config), and the 6-digit code that adds one. A
/// code is made on a computer that's already in (the app's Settings, or the library's page), lasts
/// <see cref="CodeLife"/>, and works once. Removing a phone locks it out on its very next request.
/// </summary>
public sealed class Devices
{
    public const string Cookie = "device";
    public static readonly TimeSpan CodeLife = TimeSpan.FromMinutes(10);
    /// <summary>Wrong codes one code takes before it stops working, so it can't be guessed.</summary>
    public const int TriesPerCode = 5;
    /// <summary>Wrong codes the library takes from everyone together in <see cref="TryWindow"/> before it stops
    /// listening for a while.</summary>
    public const int TriesPerWindow = 10;
    public static readonly TimeSpan TryWindow = TimeSpan.FromMinutes(10);
    /// <summary>How often a phone's "last seen" is written down: every request would write the file each time.</summary>
    static readonly TimeSpan SeenEvery = TimeSpan.FromMinutes(1);
    const int MaxDevices = 50, MaxName = 60;

    static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    sealed class State
    {
        public List<PairedDevice> Devices { get; set; } = [];
    }

    /// <summary>The code on show right now: its digits, when it stops working, and how many wrong tries it has had.</summary>
    sealed record Code(string Digits, DateTimeOffset Expires, int Wrong);

    readonly string path;
    readonly Func<DateTimeOffset> clock;
    readonly Lock gate = new();
    readonly List<DateTimeOffset> failures = [];
    State state;
    byte[]? loadedBytes;
    Code? code;

    public Devices(string home, Func<DateTimeOffset>? clock = null)
    {
        path = Path.Combine(home, "devices.json");
        this.clock = clock ?? (() => DateTimeOffset.UtcNow);
        state = Load();
    }

    DateTimeOffset Now => clock();

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

    /// <summary>Another copy changed devices.json (a second library process): read it again, so a removal takes
    /// effect at once. Compares the file's actual bytes rather than its last-write time: Windows' clock only ticks
    /// every ~15 ms, so two quick writes (one process pairs, another removes) can land on the same timestamp there
    /// and a change would go unseen. Called under the lock.</summary>
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
        File.Move(tmp, path, overwrite: true);
        Py.OwnerOnly(path);
        loadedBytes = bytes;
    }

    static string Hash(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    static bool Same(string a, string b) => CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));

    /// <summary>The paired phones, the one heard from most recently first.</summary>
    public List<PairedDevice> List()
    {
        lock (gate)
        {
            Fresh();
            return [.. state.Devices.OrderByDescending(d => d.LastSeen)];
        }
    }

    /// <summary>The phone a <c>device</c> cookie belongs to, or null (unknown, or removed). Marks it seen.</summary>
    public PairedDevice? Check(string? token)
    {
        if (string.IsNullOrEmpty(token) || token.Length > 200) return null;
        string h = Hash(token);
        lock (gate)
        {
            Fresh();
            var d = state.Devices.FirstOrDefault(x => Same(x.TokenHash, h));
            if (d is null) return null;
            if (Now - d.LastSeen >= SeenEvery)
            {
                d.LastSeen = Now;
                Save();
            }
            return d;
        }
    }

    /// <summary>Removes a phone: its cookie stops working straight away. False when there's no such phone.</summary>
    public bool Remove(string id)
    {
        lock (gate)
        {
            Fresh();
            int n = state.Devices.RemoveAll(d => d.Id == id);
            if (n > 0) Save();
            return n > 0;
        }
    }

    /// <summary>A new 6-digit code, which replaces any code still on show.</summary>
    public (string Code, DateTimeOffset Expires) NewCode()
    {
        lock (gate)
        {
            code = new Code(RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture), Now + CodeLife, 0);
            return (code.Digits, code.Expires);
        }
    }

    /// <summary>Why pairing was refused: the code was wrong (or old, or used), or too many wrong codes came in.</summary>
    public enum Refusal { WrongCode, TooManyTries }

    /// <summary>Pairs a phone with the code on show: its record and the secret for its cookie (given out only here).
    /// The code works once; a wrong one counts against it and against everyone's tries.</summary>
    public (PairedDevice? Device, string? Token, Refusal? Why) Pair(string? typed, string? name)
    {
        lock (gate)
        {
            failures.RemoveAll(t => Now - t > TryWindow);
            if (failures.Count >= TriesPerWindow) return (null, null, Refusal.TooManyTries);
            string digits = new((typed ?? "").Where(char.IsAsciiDigit).ToArray());
            var c = code;
            // The same work whether or not there's a code on show, so the timing doesn't tell.
            bool right = Same(digits, c?.Digits ?? "------") && c is not null && Now < c.Expires;
            if (!right)
            {
                failures.Add(Now);
                if (c is not null) code = c.Wrong + 1 >= TriesPerCode || Now >= c.Expires ? null : c with { Wrong = c.Wrong + 1 };
                return (null, null, Refusal.WrongCode);
            }
            code = null;
            Fresh();
            string token = Http.TokenUrlSafe(32);
            var device = new PairedDevice
            {
                Id = Http.TokenUrlSafe(9), Name = CleanName(name), TokenHash = Hash(token), Added = Now, LastSeen = Now,
            };
            state.Devices.Add(device);
            // A library can't keep phones without end: the one heard from longest ago goes.
            while (state.Devices.Count > MaxDevices) state.Devices.Remove(state.Devices.MinBy(d => d.LastSeen)!);
            Save();
            return (device, token, null);
        }
    }

    /// <summary>A phone's name as it's kept: trimmed, on one line, up to 60 characters; "Phone" when none was given.</summary>
    public static string CleanName(string? name)
    {
        string n = string.Join(' ', (name ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        n = new string(n.Where(ch => !char.IsControl(ch)).ToArray());
        if (n.Length > MaxName) n = n[..MaxName].TrimEnd();
        return n.Length > 0 ? n : "Phone";
    }
}
