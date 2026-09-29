using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using StudyStash.Core;

namespace StudyStash.Audio;

/// <summary>
/// The Mac's microphone and, for a lecture on Zoom or Teams, what the Mac plays: both become 16 kHz mono and are added
/// together, the microphone keeping time (as <see cref="WindowsSound"/> does).
/// </summary>
[SupportedOSPlatform("macos14.2")]
public sealed class MacSound(bool withComputerAudio) : IAudioSource
{
    readonly MacMicrophone mic = new();
    readonly PlayedSound played = new();
    MacComputerAudio? computer;

    public string Name => computer is not null ? "Microphone and computer audio" : "Microphone";
    public int SampleRate => Sound.Rate;
    public int Channels => 1;
    public event Action<float[]>? Samples;
    public event Action<string>? Failed;

    /// <summary>Starts the microphone (its troubles throw <see cref="MicrophoneException"/> as ever), then the
    /// computer's sound. A Mac that won't hand its sound over records the microphone alone.</summary>
    public void Start()
    {
        mic.Samples += OnMic;
        mic.Failed += OnFailed;
        mic.Start();
        if (!withComputerAudio || computer is not null) return;
        var c = new MacComputerAudio();
        c.Samples += played.Add;
        try
        {
            c.Start();
            computer = c;
        }
        catch (Exception e) when (e is MacComputerAudio.TapException or DllNotFoundException or EntryPointNotFoundException)
        {
            c.Dispose();
        }
    }

    void OnMic(float[] s)
    {
        if (computer is not null) played.MixInto(s);
        Samples?.Invoke(s);
    }

    void OnFailed(string why) => Failed?.Invoke(why);

    public void Stop()
    {
        mic.Samples -= OnMic;
        mic.Failed -= OnFailed;
        mic.Stop();
        computer?.Dispose();
        computer = null;
        played.Clear();
    }

    public void Dispose() => Stop();
}

/// <summary>
/// Everything the Mac plays, through a Core Audio process tap (macOS 14.2 and later): a tap on every app's sound, in a
/// private aggregate device of its own that nothing else sees, read with an IO proc and made 16 kHz mono. The first
/// time, macOS asks the student whether Study Stash may record what the Mac plays (Info.plist's
/// NSAudioCaptureUsageDescription); until they say yes, the tap hears silence. Nothing is muted: the student still
/// hears it all.
/// </summary>
[SupportedOSPlatform("macos14.2")]
public sealed unsafe class MacComputerAudio : IDisposable
{
    const string CoreAudio = "/System/Library/Frameworks/CoreAudio.framework/CoreAudio";
    const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    const string ObjC = "/usr/lib/libobjc.A.dylib";
    const uint Utf8 = 0x08000100; // kCFStringEncodingUTF8
    const uint Global = 0x676C6F62; // kAudioObjectPropertyScopeGlobal, 'glob'
    const uint NominalRate = 0x6E737274; // kAudioDevicePropertyNominalSampleRate, 'nsrt'
    const uint TapUid = 0x74756964; // kAudioTapPropertyUID, 'tuid'
    const uint TapFormat = 0x74666D74; // kAudioTapPropertyFormat, 'tfmt'

    /// <summary>The Mac said no to a step of opening its sound (the OSStatus says which).</summary>
    public sealed class TapException(string step, int status) : Exception($"{step} failed ({status})")
    {
        public int Status { get; } = status;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct PropertyAddress
    {
        public uint Selector, Scope, Element;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct StreamFormat
    {
        public double SampleRate;
        public uint FormatId, FormatFlags, BytesPerPacket, FramesPerPacket, BytesPerFrame, ChannelsPerFrame, BitsPerChannel, Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct AudioBuffer
    {
        public uint NumberChannels, DataByteSize;
        public IntPtr Data;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct AudioBufferList
    {
        public uint NumberBuffers;
        public AudioBuffer First; // the rest follow it
    }

    [DllImport(CoreAudio)]
    static extern int AudioHardwareCreateProcessTap(IntPtr description, uint* tap);

    [DllImport(CoreAudio)]
    static extern int AudioHardwareDestroyProcessTap(uint tap);

    [DllImport(CoreAudio)]
    static extern int AudioHardwareCreateAggregateDevice(IntPtr description, uint* device);

    [DllImport(CoreAudio)]
    static extern int AudioHardwareDestroyAggregateDevice(uint device);

    [DllImport(CoreAudio)]
    static extern int AudioDeviceCreateIOProcID(uint device,
        delegate* unmanaged<uint, IntPtr, AudioBufferList*, IntPtr, IntPtr, IntPtr, IntPtr, int> proc, IntPtr user, IntPtr* procId);

    [DllImport(CoreAudio)]
    static extern int AudioDeviceDestroyIOProcID(uint device, IntPtr procId);

    [DllImport(CoreAudio)]
    static extern int AudioDeviceStart(uint device, IntPtr procId);

    [DllImport(CoreAudio)]
    static extern int AudioDeviceStop(uint device, IntPtr procId);

    [DllImport(CoreAudio)]
    static extern int AudioObjectGetPropertyData(uint objectId, PropertyAddress* address, uint qualifierSize, IntPtr qualifier, uint* size, void* data);

    [DllImport(CoreFoundation)]
    static extern IntPtr CFStringCreateWithCString(IntPtr allocator, string text, uint encoding);

    [DllImport(CoreFoundation)]
    static extern IntPtr CFDictionaryCreateMutable(IntPtr allocator, nint capacity, IntPtr keyCallbacks, IntPtr valueCallbacks);

    [DllImport(CoreFoundation)]
    static extern void CFDictionarySetValue(IntPtr dictionary, IntPtr key, IntPtr value);

    [DllImport(CoreFoundation)]
    static extern IntPtr CFArrayCreateMutable(IntPtr allocator, nint capacity, IntPtr callbacks);

    [DllImport(CoreFoundation)]
    static extern void CFArrayAppendValue(IntPtr array, IntPtr value);

    [DllImport(CoreFoundation)]
    static extern void CFRelease(IntPtr cf);

    [DllImport(ObjC)]
    static extern IntPtr objc_getClass(string name);

    [DllImport(ObjC)]
    static extern IntPtr sel_registerName(string name);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    static extern IntPtr Send(IntPtr receiver, IntPtr selector);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    static extern IntPtr Send(IntPtr receiver, IntPtr selector, IntPtr arg);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    static extern void Send(IntPtr receiver, IntPtr selector, byte arg);

    GCHandle self;
    IntPtr description, procId;
    uint tap, device;
    Resampler? rate;
    volatile bool running;

    /// <summary>Whether this Mac has process taps (macOS 14.2 and later).</summary>
    public static bool Supported => OperatingSystem.IsMacOSVersionAtLeast(14, 2);

    /// <summary>16 kHz mono, raised on Core Audio's thread.</summary>
    public event Action<float[]>? Samples;

    /// <summary>Opens the tap and starts hearing. Throws <see cref="TapException"/> (with everything it opened closed
    /// again) when the Mac won't.</summary>
    public void Start()
    {
        if (running) return;
        NativeLibrary.Load("/System/Library/Frameworks/Foundation.framework/Foundation");
        NativeLibrary.Load(CoreAudio);
        try
        {
            Open();
        }
        catch
        {
            Close();
            throw;
        }
    }

    void Open()
    {
        // [[CATapDescription alloc] initMonoGlobalTapButExcludeProcesses:@[]]: every app's sound, mixed to mono.
        IntPtr none = Send(Send(objc_getClass("NSArray"), sel_registerName("alloc")), sel_registerName("init"));
        description = Send(Send(objc_getClass("CATapDescription"), sel_registerName("alloc")),
            sel_registerName("initMonoGlobalTapButExcludeProcesses:"), none);
        Send(none, sel_registerName("release"));
        if (description == IntPtr.Zero) throw new TapException("CATapDescription", -1);
        Send(description, sel_registerName("setPrivate:"), (byte)1);

        uint t;
        Check("AudioHardwareCreateProcessTap", AudioHardwareCreateProcessTap(description, &t));
        tap = t;
        IntPtr tapUid = GetString(tap, TapUid);
        try
        {
            uint d;
            IntPtr aggregate = Aggregate(tapUid);
            try
            {
                Check("AudioHardwareCreateAggregateDevice", AudioHardwareCreateAggregateDevice(aggregate, &d));
            }
            finally
            {
                CFRelease(aggregate);
            }
            device = d;
        }
        finally
        {
            CFRelease(tapUid);
        }

        var format = Get<StreamFormat>(tap, TapFormat);
        double hz = TryGet<double>(device, NominalRate) is double r and > 0 ? r : format.SampleRate;
        rate = new Resampler((int)Math.Round(hz > 0 ? hz : 48000), (int)Math.Max(1, format.ChannelsPerFrame));

        self = GCHandle.Alloc(this);
        IntPtr p;
        Check("AudioDeviceCreateIOProcID", AudioDeviceCreateIOProcID(device, &OnIo, GCHandle.ToIntPtr(self), &p));
        procId = p;
        running = true;
        Check("AudioDeviceStart", AudioDeviceStart(device, procId));
    }

    /// <summary>A private aggregate device with the tap as its only input, started with it and drift-corrected.</summary>
    static IntPtr Aggregate(IntPtr tapUid)
    {
        var strings = new List<IntPtr>();
        IntPtr S(string text)
        {
            var s = CFStringCreateWithCString(IntPtr.Zero, text, Utf8);
            strings.Add(s);
            return s;
        }
        IntPtr cf = NativeLibrary.Load(CoreFoundation);
        IntPtr keys = NativeLibrary.GetExport(cf, "kCFTypeDictionaryKeyCallBacks");
        IntPtr values = NativeLibrary.GetExport(cf, "kCFTypeDictionaryValueCallBacks");
        IntPtr yes = Marshal.ReadIntPtr(NativeLibrary.GetExport(cf, "kCFBooleanTrue"));

        IntPtr sub = CFDictionaryCreateMutable(IntPtr.Zero, 0, keys, values);
        CFDictionarySetValue(sub, S("uid"), tapUid); // kAudioSubTapUIDKey
        CFDictionarySetValue(sub, S("drift"), yes); // kAudioSubTapDriftCompensationKey
        IntPtr taps = CFArrayCreateMutable(IntPtr.Zero, 0, NativeLibrary.GetExport(cf, "kCFTypeArrayCallBacks"));
        CFArrayAppendValue(taps, sub);

        IntPtr all = CFDictionaryCreateMutable(IntPtr.Zero, 0, keys, values);
        CFDictionarySetValue(all, S("name"), S("Study Stash computer audio")); // kAudioAggregateDeviceNameKey
        CFDictionarySetValue(all, S("uid"), S($"com.study-stash.computer-audio.{Guid.NewGuid():N}")); // kAudioAggregateDeviceUIDKey
        CFDictionarySetValue(all, S("private"), yes); // kAudioAggregateDeviceIsPrivateKey
        CFDictionarySetValue(all, S("tapautostart"), yes); // kAudioAggregateDeviceTapAutoStartKey
        CFDictionarySetValue(all, S("taps"), taps); // kAudioAggregateDeviceTapListKey

        CFRelease(sub);
        CFRelease(taps);
        foreach (var s in strings) CFRelease(s);
        return all;
    }

    [UnmanagedCallersOnly]
    static int OnIo(uint device, IntPtr now, AudioBufferList* input, IntPtr inputTime, IntPtr output, IntPtr outputTime, IntPtr user)
    {
        if (GCHandle.FromIntPtr(user).Target is not MacComputerAudio me || !me.running || me.rate is not { } rate) return 0;
        if (input is null || input->NumberBuffers == 0 || input->First.Data == IntPtr.Zero) return 0;
        var sound = rate.Process(new ReadOnlySpan<float>((void*)input->First.Data, (int)(input->First.DataByteSize / 4)));
        if (sound.Length == 0) return 0;
        try
        {
            me.Samples?.Invoke(sound);
        }
        catch (Exception)
        {
            // A listener's trouble never reaches Core Audio's thread.
        }
        return 0;
    }

    static void Check(string step, int status)
    {
        if (status != 0) throw new TapException(step, status);
    }

    static T Get<T>(uint objectId, uint selector) where T : unmanaged =>
        TryGet<T>(objectId, selector, out int status) is { } value ? value : throw new TapException($"reading '{Fourcc(selector)}'", status);

    static T? TryGet<T>(uint objectId, uint selector) where T : unmanaged => TryGet<T>(objectId, selector, out _);

    static T? TryGet<T>(uint objectId, uint selector, out int status) where T : unmanaged
    {
        var address = new PropertyAddress { Selector = selector, Scope = Global, Element = 0 };
        T value = default;
        uint size = (uint)sizeof(T);
        status = AudioObjectGetPropertyData(objectId, &address, 0, IntPtr.Zero, &size, &value);
        return status == 0 ? value : null;
    }

    /// <summary>A CFString property (the caller releases it).</summary>
    static IntPtr GetString(uint objectId, uint selector) => Get<IntPtr>(objectId, selector) is var s && s != IntPtr.Zero
        ? s : throw new TapException($"reading '{Fourcc(selector)}'", -1);

    static string Fourcc(uint code) => new([(char)(code >> 24), (char)(code >> 16 & 0xFF), (char)(code >> 8 & 0xFF), (char)(code & 0xFF)]);

    void Close()
    {
        running = false;
        if (device != 0 && procId != IntPtr.Zero)
        {
            AudioDeviceStop(device, procId);
            AudioDeviceDestroyIOProcID(device, procId);
        }
        procId = IntPtr.Zero;
        if (device != 0) AudioHardwareDestroyAggregateDevice(device);
        device = 0;
        if (tap != 0) AudioHardwareDestroyProcessTap(tap);
        tap = 0;
        if (description != IntPtr.Zero) Send(description, sel_registerName("release"));
        description = IntPtr.Zero;
        if (self.IsAllocated) self.Free();
        rate = null;
    }

    public void Dispose() => Close();
}
