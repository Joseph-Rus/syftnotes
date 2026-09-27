using StudyStash.Core;

namespace StudyStash.Audio;

/// <summary>Why the microphone didn't start, as the student needs to hear it.</summary>
public enum MicTroubleKind
{
    /// <summary>The system says no: Study Stash isn't allowed the microphone.</summary>
    Denied,
    /// <summary>A Mac says Study Stash is allowed but won't hand the microphone over (an updated app it still
    /// remembers by its old signature).</summary>
    NotHandedOver,
    /// <summary>There is no microphone to record from.</summary>
    NoDevice,
    /// <summary>Anything else.</summary>
    Other,
}

/// <summary>
/// What to tell the student when the microphone won't start: a title, one line on how to fix it, and the button that
/// opens the right settings. Never an error code: that goes to the log.
/// </summary>
public sealed record MicTrouble(MicTroubleKind Kind, string Title, string Detail, string ActionLabel, string ActionUrl)
{
    public const string MacPrivacyUrl = "x-apple.systempreferences:com.apple.preference.security?Privacy_Microphone";
    public const string MacSoundUrl = "x-apple.systempreferences:com.apple.Sound-Settings.extension";
    public const string WindowsPrivacyUrl = "ms-settings:privacy-microphone";
    public const string WindowsSoundUrl = "ms-settings:sound";

    /// <summary>kAudioQueueErr_InvalidDevice: the audio queue found no device it may use.</summary>
    public const int InvalidDevice = -66680;

    public bool HasAction => ActionLabel.Length > 0;

    /// <summary>The system doesn't let Study Stash use the microphone.</summary>
    public static MicTrouble Denied(bool windows) => windows
        ? new(MicTroubleKind.Denied, "Study Stash can't use the microphone", RecordingWords.CantHearWindows, "Open Settings", WindowsPrivacyUrl)
        : new(MicTroubleKind.Denied, "Study Stash can't use the microphone",
            "Allow it in System Settings → Privacy & Security → Microphone.", "Open System Settings", MacPrivacyUrl);

    public static MicTrouble Denied() => Denied(OperatingSystem.IsWindows());

    /// <summary>No microphone plugged in or picked.</summary>
    public static MicTrouble NoDevice(bool windows) => windows
        ? new(MicTroubleKind.NoDevice, "No microphone found", "Plug one in, or pick one in Settings → System → Sound → Input.",
            "Open Sound settings", WindowsSoundUrl)
        : new(MicTroubleKind.NoDevice, "No microphone found", "Plug one in, or pick one in System Settings → Sound → Input.",
            "Open Sound settings", MacSoundUrl);

    public static MicTrouble NoDevice() => NoDevice(OperatingSystem.IsWindows());

    /// <summary>A Mac that says yes but won't hand the microphone over: switching Study Stash off and on again makes
    /// macOS remember this copy of the app.</summary>
    public static MicTrouble NotHandedOver() => new(MicTroubleKind.NotHandedOver, "macOS didn't hand over the microphone",
        "In System Settings → Privacy & Security → Microphone, turn Study Stash off and on again, then press Record.",
        "Open System Settings", MacPrivacyUrl);

    /// <summary>Something else went wrong: trying again usually works.</summary>
    public static MicTrouble Other(bool windows) => new(MicTroubleKind.Other, "The microphone didn't start",
        $"Press Record to try again. If it keeps happening, restart your {(windows ? "PC" : "Mac")}.", "", "");

    /// <summary>The words for a Mac's audio queue refusing to start with <paramref name="status"/>, given what macOS
    /// said about permission at the time.</summary>
    public static MicTrouble From(int status, MicAccess access) => access switch
    {
        MicAccess.Denied or MicAccess.Restricted => Denied(windows: false),
        MicAccess.Allowed when status == InvalidDevice => NotHandedOver(),
        _ => Other(windows: false),
    };
}

/// <summary>The microphone didn't start; <see cref="Trouble"/> says why in the student's words. The message (for the
/// log) carries the system's status code.</summary>
public sealed class MicrophoneException(MicTrouble trouble, int status = 0)
    : InvalidOperationException(status != 0 ? $"{trouble.Title} (status {status})" : trouble.Title)
{
    public MicTrouble Trouble { get; } = trouble;
    public int Status { get; } = status;
}
