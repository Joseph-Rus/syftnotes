using System.Text.Json.Nodes;
using StudyStash.App.Services;

namespace StudyStash.App.ViewModels;

/// <summary>The library computer's dropdown, line by line: whether the library is running, what it holds, when
/// Canvas last synced, and which laptops are reaching it. Null for a line there's nothing to say on (an older library
/// that doesn't count its laptops).</summary>
public sealed record LibraryPanelLines(string Running, bool RunningGood, string Lectures, string? Canvas, bool CanvasGood, string? Laptops, bool LaptopsGood);

/// <summary>Words for a library-only computer's dropdown (no Record, no transcription model): pure, so a test needn't
/// run a library to check them.</summary>
public static class LibraryPanelWords
{
    /// <summary>A laptop that asked the library within this long is "connected" (the app asks every 20 seconds).</summary>
    public static readonly TimeSpan Connected = TimeSpan.FromMinutes(5);

    public static LibraryPanelLines From(bool running, bool starting, string device, JsonObject? overview, CanvasApi.State? canvas, DateTimeOffset now, TimeZoneInfo zone)
    {
        string runningWords = running ? $"Library running on this {device}" : starting ? "Starting your library…" : "Your library isn't running";
        var (canvasWords, canvasGood) = Canvas(canvas, now, zone);
        var (laptopWords, laptopsGood) = Laptops(overview?["laptops"] as JsonArray, now, zone);
        // Not running and nothing read from it: the one thing to say is where to start it.
        string lectures = overview is null && !running && !starting ? "Start it in Settings → Your library" : Lectures(overview);
        return new LibraryPanelLines(runningWords, running, lectures, canvasWords, canvasGood, laptopWords, laptopsGood);
    }

    /// <summary>"128 lectures in 6 classes", "1 lecture in 1 class", "No lectures yet", with "· writing 2" while notes
    /// are being written.</summary>
    public static string Lectures(JsonObject? overview)
    {
        if (overview is null) return "Reading your library…";
        var classes = (overview["classes"] as JsonArray ?? []).OfType<JsonObject>().ToList();
        int total = classes.Sum(c => Int(c["lectures"])) + Int(overview["unsorted"]);
        int writing = Int(overview["writing"]);
        string words = total == 0 ? "No lectures yet"
            : $"{total} {(total == 1 ? "lecture" : "lectures")} in {classes.Count} {(classes.Count == 1 ? "class" : "classes")}";
        return writing > 0 ? $"{words} · writing {writing}" : words;
    }

    /// <summary>"Canvas synced 10 min ago", "Canvas syncing…", or what it's waiting for.</summary>
    public static (string? Words, bool Good) Canvas(CanvasApi.State? state, DateTimeOffset now, TimeZoneInfo zone) => state?.Status switch
    {
        null or "" => (null, true),
        "not_set_up" => ("Canvas isn't connected", true),
        "no_extension" => ("Canvas is waiting for Chrome", false),
        "chrome_away" => ("Canvas is waiting for Chrome to open", false),
        "signed_out" => ("Canvas needs you to sign in", false),
        "syncing" => ("Canvas syncing…", true),
        "error" => ("Canvas couldn't sync", false),
        _ => (state.LastSync is { } at ? $"Canvas synced {CanvasWords.Ago(at, zone, now)}" : "Canvas connected", true),
    };

    /// <summary>"2 laptops connected", "No laptop connected · last seen 3 h ago", "No laptop has connected yet";
    /// null when the library doesn't say (an older one).</summary>
    public static (string? Words, bool Good) Laptops(JsonArray? laptops, DateTimeOffset now, TimeZoneInfo zone)
    {
        if (laptops is null) return (null, true);
        var seen = laptops.OfType<JsonObject>()
            .Select(l => DateTimeOffset.TryParse(l["seen"]?.GetValue<string>(), out var at) ? at : (DateTimeOffset?)null)
            .OfType<DateTimeOffset>().ToList();
        int connected = seen.Count(at => now - at <= Connected);
        if (connected > 0) return ($"{connected} {(connected == 1 ? "laptop" : "laptops")} connected", true);
        if (seen.Count > 0) return ($"No laptop connected · last seen {CanvasWords.Ago(seen.Max(), zone, now)}", false);
        return ("No laptop has connected yet", false);
    }

    static int Int(JsonNode? n) => n is JsonValue v && v.TryGetValue(out int i) ? i : 0;
}
