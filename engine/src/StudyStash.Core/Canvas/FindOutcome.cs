using System.Text.Json.Nodes;

namespace StudyStash.Core.Canvas;

/// <summary>
/// Why "Find my courses" came back the way it did, in one word the Settings page turns into a sentence: found,
/// noaddress (no Canvas address yet), signedout (Chrome isn't signed in to Canvas), noextension (no extension has
/// ever checked in), away (the extension has checked in before but didn't answer this time), or other (Canvas's own
/// words).
/// </summary>
public static class FindOutcome
{
    public const string Found = "found", NoAddress = "noaddress", SignedOut = "signedout", NoExtension = "noextension",
        Away = "away", Other = "other";

    /// <summary>What went wrong with a find (<paramref name="result"/> is what it answered), given how Canvas stands
    /// now.</summary>
    public static string Of(JsonObject result, CanvasSettings settings, DateTimeOffset now)
    {
        if (result["error"] is not JsonValue e || !e.TryGetValue(out string? error) || error.Length == 0) return Found;
        if (!settings.On || error == CanvasSync.NotCanvas || error == LibraryNoAddress) return NoAddress;
        if (error == CanvasSync.SignedOutAnswer) return SignedOut;
        if (settings.ExtensionSeen.Length == 0) return NoExtension;
        if (error == AgentQueue.NoAnswer || !settings.ExtensionConnected(now)) return Away;
        return Other;
    }

    /// <summary>What the library answers a find before anything was asked of Chrome.</summary>
    public const string LibraryNoAddress = "Add your school's Canvas address first.";

    /// <summary>The sentence for an outcome; <paramref name="words"/> are Canvas's own, for <see cref="Other"/>.</summary>
    public static string Say(string outcome, string words = "") => outcome switch
    {
        Found => "Found your Canvas courses. Pick one for each class.",
        NoAddress => "Add your school's Canvas address first, like school.instructure.com.",
        SignedOut => "Chrome isn't signed in to Canvas. Open Canvas in Chrome, sign in, then try again.",
        NoExtension => "Add the Study Stash extension to Chrome first (below).",
        Away => "Chrome didn't answer. Set up the extension below, keep Chrome open and signed in to Canvas, then try again.",
        _ => words.Length > 0 ? $"Couldn't find your courses: {words}" : "Couldn't find your courses.",
    };
}
