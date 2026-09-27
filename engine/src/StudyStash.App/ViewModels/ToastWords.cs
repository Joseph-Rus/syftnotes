using System.Text.RegularExpressions;

namespace StudyStash.App.ViewModels;

/// <summary>What a notification may say: plain words. The codes the system hands back ("error -66680", "HRESULT
/// 0x80070005", "OSStatus 560557673") mean nothing to a student, so they come out of the toast and go to the log.</summary>
public static partial class ToastWords
{
    /// <summary><paramref name="text"/> without its error codes, tidied: "The microphone didn't start (error -66680)."
    /// becomes "The microphone didn't start."</summary>
    public static string Plain(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        string s = Bracketed().Replace(text, "");
        s = Bare().Replace(s, "");
        s = Spaces().Replace(s, " ").Trim();
        s = SpaceBeforeStop().Replace(s, "$1");
        // "…didn't start: ." or "…didn't start, ." once a trailing code has gone.
        s = DanglingColon().Replace(s, ".");
        return s.Trim();
    }

    /// <summary>Whether <see cref="Plain"/> took anything out (so the whole text is worth writing to the log).</summary>
    public static bool HadCodes(string? text) => !string.IsNullOrWhiteSpace(text) && Plain(text) != Spaces().Replace(text, " ").Trim();

    const string Word = @"(?:error|code|status|OSStatus|HRESULT|errno)\s*[:=#]?\s*";
    const string Hex = @"0x[0-9A-Fa-f]{4,}";

    /// <summary>"(error -50)", "(-66680)", "(0x80070005)": a code in brackets, named or a bare long number.</summary>
    [GeneratedRegex(@"\s*\(\s*(?:" + Word + @"(?:-?\d+|" + Hex + @")|-?\d{3,}|" + Hex + @")\s*\)", RegexOptions.IgnoreCase)]
    private static partial Regex Bracketed();

    /// <summary>", OSStatus -10863", ": error 28": a named code in the running text.</summary>
    [GeneratedRegex(@"[,:;]?\s*\b" + Word + @"(?:-?\d+|" + Hex + @")\b", RegexOptions.IgnoreCase)]
    private static partial Regex Bare();

    [GeneratedRegex(@"\s{2,}")] private static partial Regex Spaces();
    [GeneratedRegex(@"\s+([.,;:!?])")] private static partial Regex SpaceBeforeStop();
    [GeneratedRegex(@"[,:;]\s*\.$")] private static partial Regex DanglingColon();
}
