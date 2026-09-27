using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using StudyStash.Core;

namespace StudyStash.Audio;

/// <summary>
/// The Mac's microphone, through AudioToolbox's audio queue: it hands over 16 kHz mono floats, converting from
/// whatever the hardware runs at. Ask for permission first (<see cref="MacPermissions.RequestMicrophoneAsync"/>): a
/// queue opened while macOS's prompt is still up is refused.
/// </summary>
[SupportedOSPlatform("macos")]
public sealed unsafe class MacMicrophone : IAudioSource
{
    const string Toolbox = "/System/Library/Frameworks/AudioToolbox.framework/AudioToolbox";
    const uint LinearPcm = 0x6C70636D; // 'lpcm'
    const uint FloatPacked = 1 | 8; // kAudioFormatFlagIsFloat | kAudioFormatFlagIsPacked
    const int Buffers = 4, FramesPerBuffer = Sound.Rate / 10; // 100 ms each

    [StructLayout(LayoutKind.Sequential)]
    struct StreamFormat
    {
        public double SampleRate;
        public uint FormatId, FormatFlags, BytesPerPacket, FramesPerPacket, BytesPerFrame, ChannelsPerFrame, BitsPerChannel, Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct QueueBuffer
    {
        public uint Capacity;
        public IntPtr Data;
        public uint ByteSize;
        public IntPtr UserData;
        public uint PacketDescriptionCapacity;
        public IntPtr PacketDescriptions;
        public uint PacketDescriptionCount;
    }

    [DllImport(Toolbox)]
    static extern int AudioQueueNewInput(StreamFormat* format, delegate* unmanaged<IntPtr, IntPtr, QueueBuffer*, IntPtr, uint, IntPtr, void> callback,
        IntPtr userData, IntPtr runLoop, IntPtr runLoopMode, uint flags, IntPtr* queue);

    [DllImport(Toolbox)]
    static extern int AudioQueueAllocateBuffer(IntPtr queue, uint bytes, QueueBuffer** buffer);

    [DllImport(Toolbox)]
    static extern int AudioQueueEnqueueBuffer(IntPtr queue, QueueBuffer* buffer, uint packets, IntPtr descriptions);

    [DllImport(Toolbox)]
    static extern int AudioQueueStart(IntPtr queue, IntPtr startTime);

    [DllImport(Toolbox)]
    static extern int AudioQueueStop(IntPtr queue, byte immediate);

    [DllImport(Toolbox)]
    static extern int AudioQueueDispose(IntPtr queue, byte immediate);

    GCHandle self;
    IntPtr queue;
    volatile bool running;

    public string Name => "Microphone";
    public int SampleRate => Sound.Rate;
    public int Channels => 1;
    public event Action<float[]>? Samples;
    public event Action<string>? Failed;

    [StructLayout(LayoutKind.Sequential)]
    struct PropertyAddress
    {
        public uint Selector, Scope, Element;
    }

    [DllImport("/System/Library/Frameworks/CoreAudio.framework/CoreAudio")]
    static extern int AudioObjectGetPropertyData(uint objectId, PropertyAddress* address, uint qualifierSize, IntPtr qualifier, uint* size, void* data);

    /// <summary>Whether macOS has an input device to record from (a Mac mini with nothing plugged in doesn't). Null
    /// when it can't tell, which counts as yes.</summary>
    public static bool? HasInputDevice()
    {
        // kAudioObjectSystemObject, kAudioHardwarePropertyDefaultInputDevice ('dIn '), kAudioObjectPropertyScopeGlobal ('glob').
        var address = new PropertyAddress { Selector = 0x64496E20, Scope = 0x676C6F62, Element = 0 };
        uint device = 0, size = sizeof(uint);
        try
        {
            return AudioObjectGetPropertyData(1, &address, 0, IntPtr.Zero, &size, &device) == 0 ? device != 0 : null;
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }

    /// <summary>Starts the microphone. Throws <see cref="MicrophoneException"/> with the student's words when it
    /// can't: no microphone, not allowed, or macOS not handing it over. A start that fails is tried once more after a
    /// moment, since a device switching over (AirPods connecting) can refuse the first one.</summary>
    public void Start()
    {
        if (queue != IntPtr.Zero) return;
        if (HasInputDevice() == false) throw new MicrophoneException(MicTrouble.NoDevice(windows: false));
        int status = TryStart();
        if (status == 0) return;
        Thread.Sleep(300);
        status = TryStart();
        if (status == 0) return;
        throw new MicrophoneException(MicTrouble.From(status, MacPermissions.Microphone()), status);
    }

    /// <summary>One try at opening and starting the audio queue: 0, or the status it failed with (everything it
    /// opened is closed again).</summary>
    int TryStart()
    {
        var format = new StreamFormat
        {
            SampleRate = Sound.Rate, FormatId = LinearPcm, FormatFlags = FloatPacked, BytesPerPacket = 4, FramesPerPacket = 1,
            BytesPerFrame = 4, ChannelsPerFrame = 1, BitsPerChannel = 32,
        };
        self = GCHandle.Alloc(this);
        IntPtr q;
        int err = AudioQueueNewInput(&format, &OnBuffer, GCHandle.ToIntPtr(self), IntPtr.Zero, IntPtr.Zero, 0, &q);
        if (err != 0)
        {
            self.Free();
            return err;
        }
        queue = q;
        for (int i = 0; i < Buffers; i++)
        {
            QueueBuffer* b;
            if (AudioQueueAllocateBuffer(queue, FramesPerBuffer * 4, &b) == 0) AudioQueueEnqueueBuffer(queue, b, 0, IntPtr.Zero);
        }
        running = true;
        err = AudioQueueStart(queue, IntPtr.Zero);
        if (err != 0) Stop();
        return err;
    }

    [UnmanagedCallersOnly]
    static void OnBuffer(IntPtr user, IntPtr queue, QueueBuffer* buffer, IntPtr startTime, uint packets, IntPtr descriptions)
    {
        if (GCHandle.FromIntPtr(user).Target is not MacMicrophone mic || !mic.running) return;
        int n = (int)(buffer->ByteSize / 4);
        if (n > 0)
        {
            var samples = new float[n];
            new ReadOnlySpan<float>((void*)buffer->Data, n).CopyTo(samples);
            try
            {
                mic.Samples?.Invoke(samples);
            }
            catch (Exception e)
            {
                mic.Failed?.Invoke(e.Message);
            }
        }
        AudioQueueEnqueueBuffer(queue, buffer, 0, IntPtr.Zero);
    }

    public void Stop()
    {
        running = false;
        if (queue == IntPtr.Zero) return;
        AudioQueueStop(queue, 1);
        AudioQueueDispose(queue, 1);
        queue = IntPtr.Zero;
        if (self.IsAllocated) self.Free();
    }

    public void Dispose() => Stop();
}

/// <summary>Whether macOS lets Study Stash use the microphone (AVCaptureDevice's answer).</summary>
public enum MicAccess
{
    Unknown,
    /// <summary>Not asked yet: recording asks.</summary>
    NotAsked,
    Allowed,
    Denied,
    /// <summary>A school or parental policy says no.</summary>
    Restricted,
}

[SupportedOSPlatform("macos")]
public static class MacPermissions
{
    const string ObjC = "/usr/lib/libobjc.A.dylib";

    [DllImport(ObjC)]
    static extern IntPtr objc_getClass(string name);

    [DllImport(ObjC)]
    static extern IntPtr sel_registerName(string name);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    static extern long SendLong(IntPtr receiver, IntPtr selector, IntPtr arg);

    public const string MicrophoneSettingsUrl = MicTrouble.MacPrivacyUrl;

    public static MicAccess Microphone()
    {
        try
        {
            IntPtr av = NativeLibrary.Load("/System/Library/Frameworks/AVFoundation.framework/AVFoundation");
            IntPtr audio = Marshal.ReadIntPtr(NativeLibrary.GetExport(av, "AVMediaTypeAudio"));
            long status = SendLong(objc_getClass("AVCaptureDevice"), sel_registerName("authorizationStatusForMediaType:"), audio);
            return status switch { 0 => MicAccess.NotAsked, 1 => MicAccess.Restricted, 2 => MicAccess.Denied, 3 => MicAccess.Allowed, _ => MicAccess.Unknown };
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            return MicAccess.Unknown;
        }
    }

    // [AVCaptureDevice requestAccessForMediaType:completionHandler:] takes an Objective-C block. A global block (one
    // that captures nothing) is a fixed struct: built once in native memory that never moves or is freed, macOS's
    // Block_copy hands back the same pointer, and its invoke function completes whichever request is waiting.
    [StructLayout(LayoutKind.Sequential)]
    struct BlockDescriptor
    {
        public nuint Reserved, Size;
    }

    [StructLayout(LayoutKind.Sequential)]
    unsafe struct Block
    {
        public IntPtr Isa;
        public int Flags, Reserved;
        public IntPtr Invoke;
        public BlockDescriptor* Descriptor;
    }

    const int BlockIsGlobal = 1 << 28;
    static readonly Lock asking = new();
    static unsafe Block* answerBlock;
    static TaskCompletionSource<bool>? answer;

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    static extern unsafe void SendAsk(IntPtr receiver, IntPtr selector, IntPtr mediaType, Block* handler);

    [UnmanagedCallersOnly]
    static unsafe void OnAnswer(Block* block, byte granted) => Volatile.Read(ref answer)?.TrySetResult(granted != 0);

    /// <summary>
    /// Has macOS ask the student for the microphone (its own prompt, once) and waits up to a minute for the answer;
    /// asked already, it answers straight away with what the student said then. Nothing is opened: recording starts
    /// only once the answer is yes.
    /// </summary>
    public static async Task<MicAccess> RequestMicrophoneAsync(TimeSpan? wait = null)
    {
        var now = Microphone();
        if (now != MicAccess.NotAsked) return now;
        Task<bool> answered;
        try
        {
            answered = Ask();
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            return MicAccess.Unknown;
        }
        await Task.WhenAny(answered, Task.Delay(wait ?? TimeSpan.FromMinutes(1)));
        var after = Microphone();
        if (after == MicAccess.NotAsked && answered.IsCompletedSuccessfully) return answered.Result ? MicAccess.Allowed : MicAccess.Denied;
        return after;
    }

    /// <summary>Sends the request, unless one is already waiting for its answer: the task its answer completes.</summary>
    static unsafe Task<bool> Ask()
    {
        lock (asking)
        {
            if (answer is { Task.IsCompleted: false } waiting) return waiting.Task;
            IntPtr av = NativeLibrary.Load("/System/Library/Frameworks/AVFoundation.framework/AVFoundation");
            IntPtr audio = Marshal.ReadIntPtr(NativeLibrary.GetExport(av, "AVMediaTypeAudio"));
            if (answerBlock is null)
            {
                var descriptor = (BlockDescriptor*)NativeMemory.AllocZeroed((nuint)sizeof(BlockDescriptor));
                descriptor->Size = (nuint)sizeof(Block);
                var block = (Block*)NativeMemory.AllocZeroed((nuint)sizeof(Block));
                block->Isa = NativeLibrary.GetExport(NativeLibrary.Load("/usr/lib/libSystem.B.dylib"), "_NSConcreteGlobalBlock");
                block->Flags = BlockIsGlobal;
                block->Invoke = (IntPtr)(delegate* unmanaged<Block*, byte, void>)&OnAnswer;
                block->Descriptor = descriptor;
                answerBlock = block;
            }
            var fresh = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Volatile.Write(ref answer, fresh);
            SendAsk(objc_getClass("AVCaptureDevice"), sel_registerName("requestAccessForMediaType:completionHandler:"), audio, answerBlock);
            return fresh.Task;
        }
    }
}

/// <summary>Keeps the computer from sleeping while it records (the screen may still turn off).</summary>
public sealed class KeepAwake : IDisposable
{
    [DllImport("/System/Library/Frameworks/IOKit.framework/IOKit")]
    static extern int IOPMAssertionCreateWithName(IntPtr type, uint level, IntPtr name, out uint id);

    [DllImport("/System/Library/Frameworks/IOKit.framework/IOKit")]
    static extern int IOPMAssertionRelease(uint id);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    static extern IntPtr CFStringCreateWithCString(IntPtr allocator, string text, uint encoding);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    static extern void CFRelease(IntPtr cf);

    [DllImport("kernel32.dll")]
    static extern uint SetThreadExecutionState(uint flags);

    const uint Continuous = 0x80000000, SystemRequired = 0x00000001;
    uint macId;
    Thread? windowsHolder;
    readonly ManualResetEventSlim release = new();

    public KeepAwake(string why)
    {
        if (OperatingSystem.IsMacOS())
        {
            IntPtr type = CFStringCreateWithCString(IntPtr.Zero, "PreventUserIdleSystemSleep", 0x08000100);
            IntPtr name = CFStringCreateWithCString(IntPtr.Zero, why, 0x08000100);
            IOPMAssertionCreateWithName(type, 255, name, out macId);
            CFRelease(type);
            CFRelease(name);
        }
        else if (OperatingSystem.IsWindows())
        {
            // The flag belongs to the thread that set it, so one thread holds it until release.
            windowsHolder = new Thread(() =>
            {
                SetThreadExecutionState(Continuous | SystemRequired);
                release.Wait();
                SetThreadExecutionState(Continuous);
            }) { IsBackground = true, Name = "Keep awake" };
            windowsHolder.Start();
        }
    }

    public void Dispose()
    {
        if (macId != 0)
        {
            IOPMAssertionRelease(macId);
            macId = 0;
        }
        release.Set();
        windowsHolder?.Join(TimeSpan.FromSeconds(2));
        windowsHolder = null;
    }
}
