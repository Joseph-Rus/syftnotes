using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using StudyStash.Core;

namespace StudyStash.Audio;

/// <summary>
/// Linux sound through PulseAudio's simple API (libpulse-simple), which PipeWire answers too (pipewire-pulse, the
/// default on Ubuntu, Fedora and the rest): the default microphone and, for a lecture on Zoom or Teams, what the
/// computer plays, read from the default speakers' monitor. The sound server hands both over as 16 kHz mono floats, so
/// nothing is converted here; they're added together and the microphone keeps time, as on Windows.
/// </summary>
/// <remarks>
/// Each stream is read on a thread of its own, a tenth of a second at a time (a read blocks until that much has come).
/// Opening is given <see cref="OpenTimeout"/>, so Record never hangs on a sound server that doesn't answer.
/// </remarks>
public sealed class LinuxSound : IAudioSource
{
    /// <summary>A read's worth: a tenth of a second.</summary>
    internal const int Block = Sound.Rate / 10;

    /// <summary>The default speakers' monitor: what the computer plays.</summary>
    internal const string Monitor = "@DEFAULT_MONITOR@";

    readonly bool withComputerAudio;
    readonly Func<string?, string, IPulseStream> open;
    readonly PlayedSound played = new();
    Running? running;

    [SupportedOSPlatform("linux")]
    public LinuxSound(bool withComputerAudio) : this(withComputerAudio, PulseStream.Record)
    {
    }

    /// <summary>A test's stand-in for the sound server: opens a stream from a device (null is the default
    /// microphone) under a name.</summary>
    internal LinuxSound(bool withComputerAudio, Func<string?, string, IPulseStream> open)
    {
        this.withComputerAudio = withComputerAudio;
        this.open = open;
    }

    /// <summary>How long the sound server gets to open the microphone before Record says it didn't start.</summary>
    internal TimeSpan OpenTimeout { get; init; } = TimeSpan.FromSeconds(8);

    public string Name => withComputerAudio ? "Microphone and computer audio" : "Microphone";
    public int SampleRate => Sound.Rate;
    public int Channels => 1;
    public event Action<float[]>? Samples;
    public event Action<string>? Failed;

    /// <summary>Starts the microphone. Throws <see cref="MicrophoneException"/> with the student's words when there's
    /// no sound server, no microphone, or it doesn't answer in time.</summary>
    public void Start()
    {
        if (running is not null) return;
        var (mic, loop) = Opening.Within(OpenTimeout, Open, s =>
        {
            s.Mic.Dispose();
            s.Loop?.Dispose();
        }, () => new MicrophoneException(MicTrouble.Linux.Other()));
        var r = new Running(mic, loop);
        r.Threads.Add(Read(r, mic, OnMic, why => Failed?.Invoke(why)));
        if (loop is not null) r.Threads.Add(Read(r, loop, played.Add, _ => { }));
        running = r;
    }

    (IPulseStream Mic, IPulseStream? Loop) Open()
    {
        IPulseStream mic;
        try
        {
            mic = open(null, "Microphone");
        }
        catch (PulseException e)
        {
            throw new MicrophoneException(MicTrouble.FromPulse(e.Code), e.Code);
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            throw new MicrophoneException(MicTrouble.Linux.NoSoundServer());
        }
        IPulseStream? loop = null;
        if (withComputerAudio)
        {
            try
            {
                loop = open(Monitor, "Computer audio");
            }
            catch (PulseException)
            {
                loop = null; // no speakers to listen to: the microphone alone
            }
        }
        return (mic, loop);
    }

    static Thread Read(Running r, IPulseStream stream, Action<float[]> onSound, Action<string> onFailed)
    {
        var t = new Thread(() =>
        {
            while (!r.Stopping)
            {
                var s = new float[Block];
                try
                {
                    stream.Read(s);
                }
                catch (PulseException e)
                {
                    if (!r.Stopping) onFailed($"The microphone stopped ({e.Message}).");
                    return;
                }
                if (r.Stopping) return;
                onSound(s);
            }
        })
        { IsBackground = true, Name = "Syft Notes sound" };
        t.Start();
        return t;
    }

    void OnMic(float[] s)
    {
        if (withComputerAudio) played.MixInto(s);
        Samples?.Invoke(s);
    }

    /// <summary>Stops recording. Each reader finishes the read it's in (a tenth of a second at most) before its stream
    /// is closed; one that doesn't come back in time is left open rather than closed under it.</summary>
    public void Stop()
    {
        var r = running;
        running = null;
        if (r is null) return;
        r.Stopping = true;
        bool done = r.Threads.All(t => t.Join(TimeSpan.FromSeconds(2)));
        if (done)
        {
            r.Mic.Dispose();
            r.Loop?.Dispose();
        }
        played.Clear();
    }

    public void Dispose() => Stop();

    sealed class Running(IPulseStream mic, IPulseStream? loop)
    {
        public readonly IPulseStream Mic = mic;
        public readonly IPulseStream? Loop = loop;
        public readonly List<Thread> Threads = [];
        public volatile bool Stopping;
    }
}

/// <summary>One stream from the sound server: 16 kHz mono floats in, a buffer at a time.</summary>
internal interface IPulseStream : IDisposable
{
    /// <summary>Fills <paramref name="into"/>, waiting until there's that much. Throws <see cref="PulseException"/>
    /// when the stream has gone (the server stopped, the device went away).</summary>
    void Read(float[] into);
}

/// <summary>The sound server said no; <see cref="Code"/> is PulseAudio's error number (pa_error_code_t).</summary>
internal sealed class PulseException(int code, string message) : IOException(message)
{
    public int Code { get; } = code;
}

/// <summary>PulseAudio's error numbers the app tells apart.</summary>
internal static class PulseError
{
    public const int Access = 1; // PA_ERR_ACCESS: refused (a sandbox without the microphone)
    public const int NoEntity = 5; // PA_ERR_NOENTITY: no such device
    public const int ConnectionRefused = 6; // PA_ERR_CONNECTIONREFUSED: no sound server running
    public const int Timeout = 8; // PA_ERR_TIMEOUT
    public const int Busy = 26; // PA_ERR_BUSY: the device is taken
}

/// <summary>libpulse-simple, by hand: a recording or playing stream in a few calls.</summary>
[SupportedOSPlatform("linux")]
internal sealed class PulseStream : IPulseStream
{
    const string Simple = "libpulse-simple.so.0";
    const string Pulse = "libpulse.so.0";
    const int Float32Le = 5; // PA_SAMPLE_FLOAT32LE
    const int Playback = 1, Recording = 2; // pa_stream_direction_t

    [StructLayout(LayoutKind.Sequential)]
    struct SampleSpec
    {
        public int Format;
        public uint Rate;
        public byte Channels;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct BufferAttr
    {
        public uint MaxLength, TLength, PreBuf, MinReq, FragSize;
    }

    [DllImport(Simple)]
    static extern IntPtr pa_simple_new(string? server, string name, int dir, string? dev, string streamName, ref SampleSpec ss,
        IntPtr map, ref BufferAttr attr, out int error);

    [DllImport(Simple)]
    static extern unsafe int pa_simple_read(IntPtr s, void* data, nuint bytes, out int error);

    [DllImport(Simple)]
    static extern unsafe int pa_simple_write(IntPtr s, void* data, nuint bytes, out int error);

    [DllImport(Simple)]
    static extern int pa_simple_drain(IntPtr s, out int error);

    [DllImport(Simple)]
    static extern void pa_simple_free(IntPtr s);

    [DllImport(Pulse)]
    static extern IntPtr pa_strerror(int error);

    IntPtr handle;

    PulseStream(IntPtr handle) => this.handle = handle;

    /// <summary>Records 16 kHz mono from <paramref name="device"/> (null: the default microphone).</summary>
    public static IPulseStream Record(string? device, string streamName) => Open(Recording, device, streamName, Sound.Rate, 1);

    /// <summary>Plays <paramref name="channels"/> of floats at <paramref name="rate"/> on the default speakers.</summary>
    public static PulseStream Play(int rate, int channels) => Open(Playback, null, "Lecture", rate, channels);

    static PulseStream Open(int direction, string? device, string streamName, int rate, int channels)
    {
        var spec = new SampleSpec { Format = Float32Le, Rate = (uint)rate, Channels = (byte)channels };
        // A recording hands over a tenth of a second at a time, so live words and the level meter keep up; the rest is
        // the server's own choice (uint.MaxValue).
        uint fragment = direction == Recording ? (uint)(rate / 10 * channels * sizeof(float)) : uint.MaxValue;
        var attr = new BufferAttr { MaxLength = uint.MaxValue, TLength = uint.MaxValue, PreBuf = uint.MaxValue, MinReq = uint.MaxValue, FragSize = fragment };
        IntPtr s = pa_simple_new(null, "Syft Notes", direction, device, streamName, ref spec, IntPtr.Zero, ref attr, out int error);
        if (s == IntPtr.Zero) throw Error(error);
        return new PulseStream(s);
    }

    static PulseException Error(int code) => new(code, Marshal.PtrToStringUTF8(pa_strerror(code)) ?? $"PulseAudio error {code}");

    public unsafe void Read(float[] into)
    {
        fixed (float* p = into)
            if (pa_simple_read(handle, p, (nuint)(into.Length * sizeof(float)), out int error) < 0) throw Error(error);
    }

    public unsafe void Write(ReadOnlySpan<float> sound)
    {
        fixed (float* p = sound)
            if (pa_simple_write(handle, p, (nuint)(sound.Length * sizeof(float)), out int error) < 0) throw Error(error);
    }

    public void Drain() => pa_simple_drain(handle, out _);

    public void Dispose()
    {
        var h = Interlocked.Exchange(ref handle, IntPtr.Zero);
        if (h != IntPtr.Zero) pa_simple_free(h);
    }
}
