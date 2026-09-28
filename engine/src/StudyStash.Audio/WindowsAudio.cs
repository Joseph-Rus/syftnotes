using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using StudyStash.Core;

namespace StudyStash.Audio;

/// <summary>
/// Windows sound through WASAPI: the default microphone and, for a lecture on Zoom or Teams, what the computer plays.
/// Both become 16 kHz mono and are added together; the microphone keeps time, since Windows sends nothing for
/// the computer's sound while it's silent.
/// </summary>
/// <remarks>
/// The microphone is the one Settings → System → Sound → Input calls the default, opened in shared mode (another app
/// can record at the same time). Windows converts to 16 kHz mono itself; a driver that won't gets its own format and
/// <see cref="CaptureConverter"/> does it. Opening happens on a pool thread (COM's free-threaded apartment, never the
/// window's) and is given <see cref="OpenTimeout"/> to answer, so Record never hangs on a stuck driver.
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class WindowsSound : IAudioSource
{
    const int MaxLag = Sound.Rate; // a second of the computer's sound waiting for the microphone
    static readonly WaveFormat Wanted = WaveFormat.CreateIeeeFloatWaveFormat(Sound.Rate, 1);

    readonly bool withComputerAudio;
    readonly Func<MMDevice?> findMicrophone;
    readonly Lock gate = new();
    readonly Queue<float> played = new();
    Capture? open;

    public WindowsSound(bool withComputerAudio) : this(withComputerAudio, DefaultMicrophone)
    {
    }

    /// <summary>A test's stand-in for "which microphone": null is a PC with none.</summary>
    internal WindowsSound(bool withComputerAudio, Func<MMDevice?> findMicrophone)
    {
        this.withComputerAudio = withComputerAudio;
        this.findMicrophone = findMicrophone;
    }

    /// <summary>How long Windows gets to open the microphone before Record says it didn't start.</summary>
    internal TimeSpan OpenTimeout { get; init; } = TimeSpan.FromSeconds(8);

    public string Name => withComputerAudio ? "Microphone and computer audio" : "Microphone";
    public int SampleRate => Sound.Rate;
    public int Channels => 1;
    public event Action<float[]>? Samples;
    public event Action<string>? Failed;

    /// <summary>The default microphone, or null when this PC has none (or Windows' sound isn't running).</summary>
    static MMDevice? DefaultMicrophone()
    {
        try
        {
            using var devices = new MMDeviceEnumerator();
            return devices.TryGetDefaultAudioEndpoint(DataFlow.Capture, Role.Console, out var input) ? input : null;
        }
        catch (COMException)
        {
            return null; // the audio service is off (a server, or a broken install): to the student, no microphone
        }
    }

    /// <summary>Starts the microphone. Throws <see cref="MicrophoneException"/> with the student's words when it
    /// isn't allowed, isn't there, is taken, or doesn't answer in time.</summary>
    public void Start()
    {
        if (open is not null) return;
        open = Opening.Within(OpenTimeout, Open, c => c.Dispose(),
            () => new MicrophoneException(MicTrouble.Other(windows: true)));
    }

    Capture Open()
    {
        var c = new Capture();
        try
        {
            c.Device = findMicrophone() ?? throw new MicrophoneException(MicTrouble.NoDevice(windows: true));
            c.Mic = Record(() => new WasapiRecorderBuilder().WithDevice(c.Device), OnMic, e => Failed?.Invoke(Explain(e)));
            if (withComputerAudio)
            {
                try
                {
                    c.Loop = Record(() => new WasapiRecorderBuilder().WithLoopbackCapture(), OnPlayed, _ => { });
                }
                catch (Exception e) when (e is COMException or InvalidOperationException or UnauthorizedAccessException)
                {
                    c.Loop = null; // no speakers, or they won't share: the microphone alone
                }
            }
            return c;
        }
        catch (Exception e) when (e is COMException or UnauthorizedAccessException or InvalidOperationException && e is not MicrophoneException)
        {
            c.Dispose();
            throw new MicrophoneException(MicTrouble.FromWindows(e.HResult, refused: e is UnauthorizedAccessException), e.HResult);
        }
        catch
        {
            c.Dispose();
            throw;
        }
    }

    /// <summary>A recorder started in 16 kHz mono float, or in the device's own format when its driver refuses that
    /// one. Starting is what initializes the stream (and so checks the format), so each is tried by starting it.</summary>
    static Line Record(Func<WasapiRecorderBuilder> builder, Action<float[]> onSound, Action<Exception> onFailed)
    {
        try
        {
            return Started(builder().WithFormat(Wanted).Build(), onSound, onFailed);
        }
        catch (COMException e) when (e is AudioFormatNotSupportedException || (uint)e.HResult == 0x80070057) // E_INVALIDARG
        {
            return Started(builder().Build(), onSound, onFailed);
        }
    }

    static Line Started(WasapiRecorder recorder, Action<float[]> onSound, Action<Exception> onFailed)
    {
        var s = new Line(recorder, new CaptureConverter(recorder.WaveFormat));
        recorder.DataAvailable += (buffer, flags, _, _) =>
        {
            var sound = s.Converter.Convert(buffer, flags.HasFlag(AudioClientBufferFlags.Silent));
            if (sound.Length > 0) onSound(sound);
        };
        // Raised on the capture thread (it was made on a pool thread, which has no synchronization context).
        recorder.RecordingStopped += (_, e) =>
        {
            if (e.Exception is not null) onFailed(e.Exception);
        };
        try
        {
            recorder.StartRecording();
        }
        catch
        {
            recorder.Dispose();
            throw;
        }
        return s;
    }

    /// <summary>The words for a device error. A device that went away mid-lecture (unplugged, AUDCLNT_E_DEVICE_INVALIDATED)
    /// raises <see cref="Failed"/> with these, and the recorder opens the default microphone again before pausing.</summary>
    static string Explain(Exception e) =>
        e is UnauthorizedAccessException || e.HResult == MicTrouble.AccessDenied
            ? RecordingWords.CantHearWindows
            : $"The microphone stopped ({e.Message}).";

    void OnPlayed(float[] s)
    {
        lock (gate)
        {
            foreach (float x in s) played.Enqueue(x);
            while (played.Count > MaxLag) played.Dequeue();
        }
    }

    void OnMic(float[] s)
    {
        if (withComputerAudio)
        {
            lock (gate)
            {
                for (int i = 0; i < s.Length && played.Count > 0; i++) s[i] = Math.Clamp(s[i] + played.Dequeue(), -1f, 1f);
            }
        }
        Samples?.Invoke(s);
    }

    /// <summary>Stops recording. Closing waits for WASAPI's capture thread, so it's given a few seconds and then
    /// left to finish on its own: Stop never hangs either.</summary>
    public void Stop()
    {
        var c = open;
        open = null;
        if (c is null) return;
        c.Mic?.Recorder.StopRecording();
        c.Loop?.Recorder.StopRecording();
        Task.Run(c.Dispose).Wait(TimeSpan.FromSeconds(3));
        lock (gate) played.Clear();
    }

    public void Dispose() => Stop();

    sealed record Line(WasapiRecorder Recorder, CaptureConverter Converter);

    /// <summary>What an open microphone holds on to, closed in one go.</summary>
    sealed class Capture : IDisposable
    {
        public MMDevice? Device;
        public Line? Mic, Loop;

        public void Dispose()
        {
            foreach (var s in new[] { Mic, Loop })
            {
                try
                {
                    s?.Recorder.Dispose();
                }
                catch (COMException)
                {
                }
            }
            Device?.Dispose();
            Mic = Loop = null;
            Device = null;
        }
    }
}

/// <summary>Opening something that might never answer (a sound driver), from a thread that mustn't wait for it
/// forever (the window's).</summary>
internal static class Opening
{
    /// <summary>Runs <paramref name="open"/> on a pool thread and waits up to <paramref name="limit"/>. Its own
    /// exception comes back as it was thrown; past the limit <paramref name="late"/>'s is thrown instead, and whatever
    /// <paramref name="open"/> opens after all is closed with <paramref name="close"/>.</summary>
    public static T Within<T>(TimeSpan limit, Func<T> open, Action<T> close, Func<Exception> late)
    {
        var opening = Task.Run(open);
        if (!((IAsyncResult)opening).AsyncWaitHandle.WaitOne(limit))
        {
            _ = opening.ContinueWith(t =>
            {
                if (t.IsCompletedSuccessfully) close(t.Result);
            }, TaskScheduler.Default);
            throw late();
        }
        return opening.GetAwaiter().GetResult();
    }
}

[SupportedOSPlatform("windows")]
public static class WindowsPermissions
{
    public const string MicrophoneSettings = "ms-settings:privacy-microphone";
    const string Store = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\microphone";

    /// <summary>Whether Windows lets desktop apps use the microphone: the switches in Settings → Privacy & security →
    /// Microphone. Unknown when there's no microphone to ask about (or Windows' sound isn't running).</summary>
    public static MicAccess Microphone()
    {
        string? Read(RegistryKey root, string sub) => root.OpenSubKey(sub)?.GetValue("Value") as string;
        if (Read(Registry.LocalMachine, Store) == "Deny") return MicAccess.Restricted;
        if (Read(Registry.CurrentUser, Store) == "Deny" || Read(Registry.CurrentUser, Store + @"\NonPackaged") == "Deny") return MicAccess.Denied;
        try
        {
            using var devices = new MMDeviceEnumerator();
            if (!devices.TryGetDefaultAudioEndpoint(DataFlow.Capture, Role.Console, out var d)) return MicAccess.Unknown;
            d.Dispose();
            return MicAccess.Allowed;
        }
        catch (COMException)
        {
            return MicAccess.Unknown;
        }
    }
}
