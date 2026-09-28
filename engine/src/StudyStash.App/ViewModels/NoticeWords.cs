using StudyStash.App.Services;
using StudyStash.Audio;
using StudyStash.Core;

namespace StudyStash.App.ViewModels;

/// <summary>What the app's own notifications say, in the student's words: short enough to read at a glance, and what
/// to do when there's something to do. Each notification's button says what it does, so the words don't repeat it.</summary>
public static class NoticeWords
{
    /// <summary>The lecture paused by itself and waits for the student: <see cref="AppHost.RecordingPaused"/>.</summary>
    public const string PausedTitle = AppHost.RecordingPaused;

    /// <summary>A lecture the library has filed: its class, its title, and whether its notes were written (when they
    /// weren't, the transcript is there all the same).</summary>
    public static (string Title, string Text, string Action) Filed(string filedClass, string filedTitle, string error)
    {
        string title = $"Filed in {(filedClass.Length > 0 ? filedClass : "your library")}";
        if (error.Length > 0)
            return (title, (filedTitle.Length > 0 ? $"{filedTitle}: its" : "Its") + " notes couldn't be written. The transcript is there.", "Open");
        return (title, filedTitle.Length > 0 ? filedTitle : "Its notes are written.", "Open note");
    }

    /// <summary>What to do about the lecture that paused by itself (<paramref name="why"/> is the recorder's reason):
    /// Resume once the microphone or the disk is sorted, or the privacy settings when the system won't let Study Stash
    /// hear (<paramref name="opensSettings"/>).</summary>
    public static (string Text, string Action, bool OpensSettings) Paused(string why, bool windows)
    {
        if (why == RecordingWords.CantHearMac || why == RecordingWords.CantHearWindows)
            return (why, windows ? "Open Settings" : "Open System Settings", true);
        const string prefix = "Recording paused: ";
        string text = why == RecordingWords.DiskFull ? "Your disk is full. Free some space, then press Resume."
            : why.StartsWith(prefix, StringComparison.Ordinal) ? char.ToUpperInvariant(why[prefix.Length]) + why[(prefix.Length + 1)..]
            : why;
        return (text, "Resume", false);
    }

    /// <summary>The lecture just stopped and is kept: where it goes from here, which depends on whether the library can
    /// be reached.</summary>
    public static string Saved(LibraryState library, bool mac) => library switch
    {
        LibraryState.Unreachable => $"Study Stash is writing it down. It waits on this {(mac ? "Mac" : "PC")} and goes to your library when it's back.",
        LibraryState.WrongPassword => "Study Stash is writing it down. Type your library's new password in Settings to send it.",
        LibraryState.NotSetUp => "Study Stash is writing it down. Connect to your library in Settings to file it.",
        _ => "Study Stash is writing it down; the library files it and writes your notes.",
    };

    /// <summary>Record, before there's a transcription model: download it (the button), or how far along it is.</summary>
    public static (string Title, string Text, string? Action) NoModelYet(DownloadProgress? downloading) => downloading is { } d
        ? ("Downloading the transcription model", $"{d.Amount}. Record works once it's here.", null)
        : ("Download the transcription model", "Record works once it's here.", "Download");

    /// <summary>A lighter model this computer would keep up with (said once): why, and where to switch.</summary>
    public static (string Title, string Text) LighterModel(ModelAdvice advice) =>
        ("A lighter model would keep up better", $"{advice.Why} Switch in Settings → Recording.");

    /// <summary>The problems worth a notification when they start (not just a line in the dropdown): each stops
    /// lectures being written down or filed until the student does something. A library out of reach isn't one: the
    /// lectures wait on this computer by themselves, and that's normal away from home.</summary>
    public static bool WorthNotifying(ProblemKind kind) =>
        kind is ProblemKind.WhisperFailed or ProblemKind.DownloadFailed or ProblemKind.LibraryStopped or ProblemKind.WrongPassword;
}
