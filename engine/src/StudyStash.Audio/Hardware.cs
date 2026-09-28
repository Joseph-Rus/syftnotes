using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.X86;
using StudyStash.Core;

namespace StudyStash.Audio;

/// <summary>The kind of computer Study Stash is on.</summary>
public enum HostOs
{
    Mac,
    Windows,
    Linux,
}

/// <summary>A graphics card as Windows describes it: its name, the memory of its own (none to speak of for graphics
/// built into the processor), and whether it's that built-in kind.</summary>
public sealed record GraphicsCard(string Name, double MemoryGb, bool Integrated);

/// <summary>
/// What this computer has that decides how big a Whisper model keeps up with a lecture: the system, the processor
/// (its cores, and whether it has the fast maths whisper.cpp leans on: AVX2 on Intel and AMD, NEON on ARM), the
/// memory, and on Windows the best graphics card and whether Vulkan (how Whisper reaches that card) is installed.
/// </summary>
public sealed record HardwareProfile(HostOs Os, Architecture Arch, int Cores, bool FastMath, double? RamGb, GraphicsCard? Card = null,
    bool Vulkan = false)
{
    public bool AppleSilicon => Os == HostOs.Mac && Arch == Architecture.Arm64;

    /// <summary>The card Whisper runs on, or null when it runs on the processor. Study Stash's Windows build has
    /// Whisper's Vulkan runtime for x64 only (none exists for ARM), so it takes an x64 PC, Vulkan, and a real card
    /// with memory of its own: graphics built into the processor share the processor's memory and help too little.</summary>
    public GraphicsCard? WhisperCard =>
        Os == HostOs.Windows && Arch == Architecture.X64 && Vulkan && Card is { Integrated: false } card ? card : null;

    /// <summary>For the log: "Windows X64, 8 threads, AVX2, 16 GB, NVIDIA GeForce GTX 1650 (4 GB of its own), Vulkan".</summary>
    public string Describe() => string.Join(", ", new[]
    {
        $"{Os} {Arch}", $"{Cores} threads", FastMath ? (Arch == Architecture.Arm64 ? "NEON" : "AVX2") : "no AVX2",
        RamGb is { } ram ? $"{ram:0} GB" : "memory unknown",
        Card is { } card ? $"{card.Name} ({card.MemoryGb:0.#} GB of its own{(card.Integrated ? ", built in" : "")})" : Os == HostOs.Windows ? "no graphics card" : null,
        Os == HostOs.Windows ? (Vulkan ? "Vulkan" : "no Vulkan") : null,
    }.OfType<string>());

    /// <summary>"Mac", "PC" or "computer", as Study Stash says this one.</summary>
    public string DeviceWord => Os switch { HostOs.Mac => "Mac", HostOs.Windows => "PC", _ => "computer" };
}

/// <summary>Finds out what this computer has (tests give a fake).</summary>
public interface IHardwareProbe
{
    /// <summary>This computer's profile. Never throws: anything it can't tell is left out.</summary>
    HardwareProfile Probe();
}

/// <summary>One adapter as DXGI lists it: the raw facts <see cref="HardwareProbe.BestCard"/> reads.</summary>
public sealed record DisplayAdapter(string Description, uint VendorId, ulong DedicatedBytes, uint Flags);

/// <summary>
/// This computer's real profile. Everything it asks is quick (Windows lists its graphics adapters through DXGI, which
/// loads no driver UI), but it's still asked once per run, off the UI thread (see <see cref="System"/>).
/// <paramref name="adapters"/>, <paramref name="vulkan"/> and <paramref name="ramGb"/> stand in for Windows' own
/// answers in tests.
/// </summary>
public sealed class HardwareProbe(Func<IReadOnlyList<DisplayAdapter>>? adapters = null, Func<bool>? vulkan = null, Func<double?>? ramGb = null,
    HostOs? os = null) : IHardwareProbe
{
    /// <summary>This computer, asked once however often it's wanted.</summary>
    public static IHardwareProbe System { get; } = new Cached(new HardwareProbe());

    const uint SoftwareAdapter = 2; // DXGI_ADAPTER_FLAG_SOFTWARE
    const uint MicrosoftVendor = 0x1414; // Microsoft Basic Render Driver, Remote Display Adapter
    /// <summary>Graphics built into the processor report a sliver of memory of their own (128 MB on Intel, up to 512
    /// MB on AMD); a card of its own has 2 GB or more.</summary>
    const double IntegratedBelowGb = 1;

    public HardwareProfile Probe()
    {
        var kind = os ?? (OperatingSystem.IsWindows() ? HostOs.Windows : OperatingSystem.IsMacOS() ? HostOs.Mac : HostOs.Linux);
        var arch = RuntimeInformation.ProcessArchitecture;
        bool fastMath = arch is Architecture.Arm64 || Avx2.IsSupported;
        GraphicsCard? card = null;
        bool hasVulkan = false;
        if (kind == HostOs.Windows)
        {
            card = BestCard(Safe(adapters ?? Dxgi.Adapters, []));
            hasVulkan = Safe(vulkan ?? VulkanLoader, false);
        }
        return new HardwareProfile(kind, arch, Environment.ProcessorCount, fastMath, Safe(ramGb ?? Machine.TotalRamGb, null), card, hasVulkan);
    }

    /// <summary>The card Whisper would use: the one with the most memory of its own, never a stand-in that draws with
    /// the processor ("Microsoft Basic Display Adapter", a remote desktop's). Null when there's no real one.</summary>
    public static GraphicsCard? BestCard(IEnumerable<DisplayAdapter> adapters) => adapters
        .Where(a => (a.Flags & SoftwareAdapter) == 0 && a.VendorId != MicrosoftVendor
                    && !a.Description.Contains("Microsoft Basic", StringComparison.OrdinalIgnoreCase))
        .OrderByDescending(a => a.DedicatedBytes)
        .Select(a => new GraphicsCard(a.Description.Trim(), a.DedicatedBytes / (double)(1L << 30), a.DedicatedBytes / (double)(1L << 30) < IntegratedBelowGb))
        .FirstOrDefault();

    /// <summary>The Vulkan loader every graphics driver with Vulkan installs beside Windows' own files.</summary>
    static bool VulkanLoader() => File.Exists(Path.Combine(Environment.SystemDirectory, "vulkan-1.dll"));

    static T Safe<T>(Func<T> ask, T otherwise)
    {
        try
        {
            return ask();
        }
        catch (Exception)
        {
            // A driver that answers badly never stops the app: Study Stash just can't tell, and plays it safe.
            return otherwise;
        }
    }

    sealed class Cached(IHardwareProbe inner) : IHardwareProbe
    {
        readonly Lazy<HardwareProfile> profile = new(inner.Probe, LazyThreadSafetyMode.ExecutionAndPublication);
        public HardwareProfile Probe() => profile.Value;
    }

    /// <summary>DXGI's adapter list, through its COM interfaces by hand (no Windows SDK wrapper to ship).</summary>
    static unsafe class Dxgi
    {
        static readonly Guid Factory1 = new("770aae78-f26f-4dba-a829-253c83d1b387");
        const int NotFound = unchecked((int)0x887A0002); // DXGI_ERROR_NOT_FOUND: past the last adapter

        [StructLayout(LayoutKind.Sequential)]
        struct AdapterDesc1
        {
            public fixed char Description[128];
            public uint VendorId, DeviceId, SubSysId, Revision;
            public nuint DedicatedVideoMemory, DedicatedSystemMemory, SharedSystemMemory;
            public uint LuidLow;
            public int LuidHigh;
            public uint Flags;
        }

        [DllImport("dxgi.dll")]
        static extern int CreateDXGIFactory1(in Guid riid, out IntPtr factory);

        static void Release(IntPtr unknown) => ((delegate* unmanaged[Stdcall]<IntPtr, uint>)(*(void***)unknown)[2])(unknown);

        public static IReadOnlyList<DisplayAdapter> Adapters()
        {
            if (!OperatingSystem.IsWindows() || CreateDXGIFactory1(Factory1, out var factory) < 0) return [];
            var found = new List<DisplayAdapter>();
            try
            {
                // IDXGIFactory1::EnumAdapters1 is slot 12; IDXGIAdapter1::GetDesc1 is slot 10.
                var enumAdapters = (delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr*, int>)(*(void***)factory)[12];
                for (uint i = 0; i < 16; i++)
                {
                    IntPtr adapter;
                    int hr = enumAdapters(factory, i, &adapter);
                    if (hr == NotFound || hr < 0) break;
                    try
                    {
                        AdapterDesc1 d;
                        var getDesc = (delegate* unmanaged[Stdcall]<IntPtr, AdapterDesc1*, int>)(*(void***)adapter)[10];
                        if (getDesc(adapter, &d) >= 0)
                            found.Add(new DisplayAdapter(new string(d.Description).TrimEnd('\0'), d.VendorId, d.DedicatedVideoMemory, d.Flags));
                    }
                    finally
                    {
                        Release(adapter);
                    }
                }
            }
            finally
            {
                Release(factory);
            }
            return found;
        }
    }
}
