using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json.Nodes;

namespace StudyStash.Library;

/// <summary>
/// Which laptops have reached the library lately: each one's name (the <see cref="Header"/> the app sends, or its
/// address when it sends none) and when it last asked. The library computer's own dropdown reads it ("2 laptops
/// connected"); it lives in memory only, so a restarted library starts counting again.
/// </summary>
public sealed class LaptopsSeen
{
    /// <summary>The header a laptop's app names its computer with.</summary>
    public const string Header = "X-Study-Stash-Computer";

    readonly ConcurrentDictionary<string, DateTimeOffset> seen = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>A laptop asked just now: <paramref name="name"/> if it said one, else its <paramref name="address"/>.
    /// A name is cut to 64 characters, and more than 32 laptops forget the longest-quiet.</summary>
    public void Seen(string? name, string? address, DateTimeOffset at)
    {
        string who = (string.IsNullOrWhiteSpace(name) ? address ?? "" : name).Trim();
        if (who.Length == 0) return;
        if (who.Length > 64) who = who[..64];
        seen[who] = at;
        while (seen.Count > 32 && seen.MinBy(kv => kv.Value) is { Key: var oldest }) seen.TryRemove(oldest, out _);
    }

    /// <summary>[{"name": "…", "seen": "2026-09-27T12:00:00+00:00"}, …], the latest first.</summary>
    public JsonArray Json() =>
        new(seen.OrderByDescending(kv => kv.Value)
            .Select(kv => (JsonNode?)new JsonObject { ["name"] = kv.Key, ["seen"] = kv.Value.ToString("o", CultureInfo.InvariantCulture) }).ToArray());
}
