using StudyStash.Core;

namespace StudyStash.Audio;

/// <summary>This computer's microphone, whichever system it runs.</summary>
public static class Microphones
{
    /// <summary>The microphone, and on Windows (if asked) what the computer plays too.</summary>
    public static IAudioSource Open(bool withComputerAudio = false)
    {
        if (OperatingSystem.IsMacOS()) return new MacMicrophone();
        if (OperatingSystem.IsWindows()) return new WindowsSound(withComputerAudio);
        throw new PlatformNotSupportedException("Recording works on a Mac or a Windows PC.");
    }

    /// <summary>Recording the computer's own sound (a lecture on Zoom) works on Windows; a Mac needs more for it.</summary>
    public static bool CanRecordComputerAudio => OperatingSystem.IsWindows();

    public static MicAccess Access()
    {
        if (OperatingSystem.IsMacOS()) return MacPermissions.Microphone();
        if (OperatingSystem.IsWindows()) return WindowsPermissions.Microphone();
        return MicAccess.Unknown;
    }

    /// <summary>Have the system ask the person, where it asks (a Mac, once), and wait for the answer. Elsewhere, or
    /// asked already, it's the answer there is now.</summary>
    public static Task<MicAccess> AskAsync()
    {
        if (OperatingSystem.IsMacOS()) return MacPermissions.RequestMicrophoneAsync();
        return Task.FromResult(Access());
    }

    /// <summary>Where the person turns the microphone on for Study Stash.</summary>
    public static string SettingsUrl => OperatingSystem.IsWindows() ? WindowsPermissions.MicrophoneSettings
        : OperatingSystem.IsMacOS() ? MacPermissions.MicrophoneSettingsUrl : "";
}

/// <summary>Asking the system about the microphone: whether Study Stash may use it, and having it ask the student.
/// The app goes through this so a test can stand in for the system's answers.</summary>
public interface IMicPermissions
{
    MicAccess Access();

    /// <summary>Has the system ask (only when it hasn't yet) and waits for the answer.</summary>
    Task<MicAccess> AskAsync();
}

public static class MicPermissions
{
    /// <summary>This computer's own answers.</summary>
    public static readonly IMicPermissions System = new SystemMic();

    /// <summary>A pretend microphone's: always allowed, and nothing is ever asked of the system.</summary>
    public static readonly IMicPermissions Pretend = new PretendMic();

    sealed class SystemMic : IMicPermissions
    {
        public MicAccess Access() => Microphones.Access();
        public Task<MicAccess> AskAsync() => Microphones.AskAsync();
    }

    sealed class PretendMic : IMicPermissions
    {
        public MicAccess Access() => MicAccess.Allowed;
        public Task<MicAccess> AskAsync() => Task.FromResult(MicAccess.Allowed);
    }
}
