using System.Text.Json.Nodes;

namespace StudyStash.Core;

/// <summary>
/// The lecture the laptop is recording right now, as the laptop last said (at start, every 30 seconds, and at stop).
/// Kept in memory: it lapses 90 seconds after the last report, so a crash never leaves it stuck. Photos taken for a
/// recording that hasn't reached the library yet are accepted for its id for a day after.
/// </summary>
public sealed class RecordingNow(Func<DateTimeOffset>? clock = null)
{
    public static readonly TimeSpan Lapses = TimeSpan.FromSeconds(90), Remembered = TimeSpan.FromHours(24);

    sealed record Seen(string Id, string Class, string Title, string Started, DateTimeOffset At, bool Stopped);

    readonly Func<DateTimeOffset> now = clock ?? (() => DateTimeOffset.UtcNow);
    readonly Dictionary<string, Seen> seen = [];
    readonly object gate = new();

    /// <summary>The laptop says it records this lecture, or (<paramref name="stopped"/>) that it stopped.</summary>
    public void Report(string id, string className, string title, string started, bool stopped = false)
    {
        lock (gate)
        {
            var t = now();
            foreach (string old in seen.Where(kv => t - kv.Value.At > Remembered).Select(kv => kv.Key).ToList()) seen.Remove(old);
            // A stop names only the id: what was said before about the recording stays.
            if (seen.TryGetValue(id, out var before) && stopped && className.Length == 0) (className, title, started) = (before.Class, before.Title, before.Started);
            seen[id] = new Seen(id, className, title, started, t, stopped);
        }
    }

    /// <summary>{id, class, title, started} of the recording under way, or null.</summary>
    public JsonObject? Json()
    {
        lock (gate)
        {
            var t = now();
            var s = seen.Values.Where(x => !x.Stopped && t - x.At <= Lapses).OrderByDescending(x => x.At).FirstOrDefault();
            return s is null ? null : new JsonObject { ["id"] = s.Id, ["class"] = s.Class, ["title"] = s.Title, ["started"] = s.Started };
        }
    }

    /// <summary>The class a recording was made for ("" for none) when its id is the current or a recent recording's (last 24 hours); null otherwise.</summary>
    public string? ClassOf(string id)
    {
        lock (gate) return seen.TryGetValue(id, out var s) && now() - s.At <= Remembered ? s.Class : null;
    }
}
