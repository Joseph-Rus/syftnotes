namespace StudyStash.Core;

/// <summary>Hands a link to the system: a web page, or a settings pane on a Mac or Windows.</summary>
public static class Dialogs
{
    /// <summary>Whether a link someone else wrote (in a note, on Canvas, in an AI's answer) is one to hand to the
    /// system: a web page, or an address to write an email to. Nothing else is opened from a click on such a link:
    /// the system would open a file with whatever runs it, and start a program outright.</summary>
    public static bool IsWebLink(string? link) =>
        Uri.TryCreate(link, UriKind.Absolute, out var u) && u.Scheme is "http" or "https" or "mailto";

    /// <summary><see cref="OpenUrl"/> for a link someone else wrote: opened only when it <see cref="IsWebLink"/>.</summary>
    public static void OpenWebLink(string? link, Runner? run = null, string? system = null)
    {
        if (IsWebLink(link)) OpenUrl(link!, run, system);
    }

    /// <summary>Open a web page or a System Settings pane with the system's default handler. For addresses Study
    /// Stash wrote itself; a link from a note or Canvas goes through <see cref="OpenWebLink"/>.</summary>
    public static void OpenUrl(string url, Runner? run = null, string? system = null)
    {
        run ??= Machine.Run;
        system ??= Machine.Platform;
        try
        {
            if (system == "Darwin") run("open", [url], TimeSpan.FromSeconds(30));
            else if (system == "Windows") Machine.Open(url);
            else run("xdg-open", [url], TimeSpan.FromSeconds(30));
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
        }
    }
}
