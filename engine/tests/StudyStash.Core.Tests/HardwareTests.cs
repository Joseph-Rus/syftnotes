using System.Runtime.InteropServices;
using StudyStash.Audio;

namespace StudyStash.Core.Tests;

public class HardwareTests
{
    const ulong GB = 1UL << 30;

    static HardwareProfile Mac(double? ram, Architecture arch = Architecture.Arm64, int cores = 8) =>
        new(HostOs.Mac, arch, cores, true, ram);

    static HardwareProfile Pc(int cores = 12, double? ram = 16, GraphicsCard? card = null, bool vulkan = true, bool fastMath = true,
        Architecture arch = Architecture.X64) =>
        new(HostOs.Windows, arch, cores, fastMath, ram, card, vulkan);

    static GraphicsCard Card(double gb, string name = "NVIDIA GeForce RTX 3060") => new(name, gb, gb < 1);

    public static TheoryData<string, HardwareProfile, string> Computers => new()
    {
        // Apple silicon keeps large-v3, as it always has; only a Mac with very little memory gets the compact model.
        { "M1 with 8 GB", Mac(8), "large-v3" },
        { "M3 Pro with 36 GB", Mac(36), "large-v3" },
        { "Apple silicon that won't say its memory", Mac(null), "large-v3" },
        { "Apple silicon with 4 GB", Mac(4), "large-v3-turbo-q5" },
        // An Intel Mac has no Whisper graphics: it's the processor's call.
        { "Intel Mac, 8 cores", Mac(16, Architecture.X64, 8), "large-v3-turbo-q5" },
        { "Intel Mac, 4 cores", Mac(8, Architecture.X64, 4), "small" },
        // A PC with a real graphics card and Vulkan: what fits the card's own memory.
        { "PC, 12 GB card", Pc(card: Card(12)), "large-v3" },
        { "PC, 8 GB card (says 7.8)", Pc(card: Card(7.8)), "large-v3" },
        { "PC, 8 GB card, 6 GB of memory", Pc(ram: 6, card: Card(8)), "large-v3-turbo" },
        { "PC, 6 GB card", Pc(card: Card(6)), "large-v3-turbo" },
        { "PC, 4 GB card", Pc(card: Card(3.9)), "large-v3-turbo" },
        { "PC, 2 GB card", Pc(card: Card(2)), "large-v3-turbo-q5" },
        // No card Whisper can use: the processor decides.
        { "PC, 8 GB card but no Vulkan", Pc(card: Card(8), vulkan: false), "large-v3-turbo-q5" },
        { "PC, graphics built into the processor", Pc(card: Card(0.125, "Intel(R) UHD Graphics 620")), "large-v3-turbo-q5" },
        { "PC, 1.5 GB card", Pc(card: new GraphicsCard("AMD Radeon(TM) Graphics", 1.5, false)), "large-v3-turbo-q5" },
        { "ARM PC with a card (no Vulkan Whisper for ARM)", Pc(card: Card(8), arch: Architecture.Arm64), "large-v3-turbo-q5" },
        { "PC, no card, 8 threads, 8 GB", Pc(cores: 8, ram: 8), "large-v3-turbo-q5" },
        { "PC, no card, 8 threads, no AVX2", Pc(cores: 8, fastMath: false), "small" },
        { "PC, no card, 4 threads", Pc(cores: 4, ram: 8), "small" },
        { "PC, no card, 16 threads, 6 GB", Pc(cores: 16, ram: 6), "small" },
        { "PC, no card, 2 threads", Pc(cores: 2, ram: 8), "base" },
        { "PC, no card, 3 GB", Pc(cores: 8, ram: 3), "base" },
        { "PC that won't say its memory", Pc(cores: 8, ram: null), "large-v3-turbo-q5" },
        { "Linux, 8 threads", new(HostOs.Linux, Architecture.X64, 8, true, 16), "large-v3-turbo-q5" },
    };

    [Theory]
    [MemberData(nameof(Computers))]
    public void Each_computer_gets_the_model_that_keeps_up(string computer, HardwareProfile hw, string model)
    {
        var advice = WhisperModels.Advise(hw);
        Assert.True(advice.Model.Id == model, $"{computer}: {advice.Model.Id}, expected {model}");
        Assert.False(string.IsNullOrWhiteSpace(advice.Why));
        Assert.EndsWith(".", advice.Why, StringComparison.Ordinal);
    }

    [Fact]
    public void The_reason_is_one_plain_line()
    {
        Assert.Equal("This PC has no graphics card Whisper can use, so the compact model keeps up with a lecture.",
            WhisperModels.Advise(Pc(cores: 8)).Why);
        Assert.Equal("This Mac's Apple silicon runs the most accurate model and keeps up with a lecture.", WhisperModels.Advise(Mac(16)).Why);
        Assert.Equal("This PC's graphics card (NVIDIA GeForce RTX 3060) has room for large-v3 turbo, which keeps up with a lecture.",
            WhisperModels.Advise(Pc(card: Card(6))).Why);
        Assert.Equal("This Mac has 4 GB of memory, so the compact model leaves room for everything else.", WhisperModels.Advise(Mac(4)).Why);
        Assert.Equal("This PC has little memory, so Whisper base keeps up with a lecture and leaves room for everything else.",
            WhisperModels.Advise(Pc(ram: 3)).Why);
        Assert.Equal("This PC's processor would fall behind a lecture with anything bigger, so Whisper base keeps up.",
            WhisperModels.Advise(Pc(cores: 2)).Why);
    }

    [Fact]
    public void Heavier_goes_by_what_a_model_asks_of_the_computer()
    {
        Assert.True(WhisperModels.Heavier(WhisperModels.LargeV3, WhisperModels.LargeV3TurboSmall));
        Assert.True(WhisperModels.Heavier(WhisperModels.LargeV3TurboSmall, WhisperModels.Small));
        Assert.False(WhisperModels.Heavier(WhisperModels.Small, WhisperModels.LargeV3Turbo));
        Assert.False(WhisperModels.Heavier(WhisperModels.LargeV3, WhisperModels.LargeV3));
        var stranger = new WhisperModel("x", "X", "x.bin", 1, "", "");
        Assert.False(WhisperModels.Heavier(stranger, WhisperModels.Tiny));
    }

    [Fact]
    public void The_lighter_models_are_whisper_cpps_own_files()
    {
        // From the files' Git LFS pointers on Hugging Face (huggingface.co/ggerganov/whisper.cpp/raw/main/<file>).
        Assert.Equal(("ggml-small.bin", 487601967L, "1be3a9b2063867b937e64e2ec7483364a79917e157fa98c5d94b5c1fffea987b"),
            (WhisperModels.Small.File, WhisperModels.Small.Bytes, WhisperModels.Small.Sha256));
        Assert.Equal(("ggml-base.bin", 147951465L, "60ed5bc3dd14eea856493d334349b405782ddcaf0028d4b5df4088345fba2efe"),
            (WhisperModels.Base.File, WhisperModels.Base.Bytes, WhisperModels.Base.Sha256));
        Assert.Equal("488 MB", WhisperModels.Small.Size);
        Assert.Equal("148 MB", WhisperModels.Base.Size);
        Assert.Equal(WhisperModels.All.Count, WhisperModels.All.Select(m => m.Id).Distinct().Count());
    }

    [Fact]
    public void The_best_card_is_a_real_one_with_the_most_memory_of_its_own()
    {
        var card = HardwareProbe.BestCard(
        [
            new DisplayAdapter("Intel(R) UHD Graphics 630", 0x8086, 128UL << 20, 0),
            new DisplayAdapter("NVIDIA GeForce GTX 1650", 0x10DE, 4 * GB, 0),
            new DisplayAdapter("Microsoft Basic Render Driver", 0x1414, 0, 2),
        ]);
        Assert.Equal(new GraphicsCard("NVIDIA GeForce GTX 1650", 4, false), card);

        // Built into the processor: it has a sliver of memory of its own.
        var built = HardwareProbe.BestCard([new DisplayAdapter("Intel(R) Iris(R) Xe Graphics", 0x8086, 128UL << 20, 0)]);
        Assert.NotNull(built);
        Assert.True(built.Integrated);

        // Only stand-ins that draw with the processor: no card at all.
        Assert.Null(HardwareProbe.BestCard(
        [
            new DisplayAdapter("Microsoft Basic Display Adapter", 0x1234, 0, 0),
            new DisplayAdapter("Microsoft Remote Display Adapter", 0x1414, 0, 0),
            new DisplayAdapter("A software renderer", 0x5143, 0, 2),
        ]));
        Assert.Null(HardwareProbe.BestCard([]));
    }

    [Fact]
    public void The_probe_reads_windows_answers_and_survives_bad_ones()
    {
        var pc = new HardwareProbe(() => [new DisplayAdapter("AMD Radeon RX 6600  ", 0x1002, 8 * GB, 0)], () => true, () => 16, HostOs.Windows).Probe();
        Assert.Equal(HostOs.Windows, pc.Os);
        Assert.Equal(new GraphicsCard("AMD Radeon RX 6600", 8, false), pc.Card);
        Assert.True(pc.Vulkan);
        Assert.Equal(16, pc.RamGb);
        Assert.Equal(Environment.ProcessorCount, pc.Cores);

        // A driver that throws, or memory it can't tell: no card, no Vulkan, and still a profile.
        var bad = new HardwareProbe(() => throw new InvalidOperationException("driver"), () => throw new IOException("disk"),
            () => throw new InvalidOperationException("memory"), HostOs.Windows).Probe();
        Assert.Null(bad.Card);
        Assert.False(bad.Vulkan);
        Assert.Null(bad.RamGb);
        Assert.Null(bad.WhisperCard);

        // A Mac never asks about Windows' graphics.
        bool asked = false;
        var mac = new HardwareProbe(() =>
        {
            asked = true;
            return [];
        }, () => true, () => 8, HostOs.Mac).Probe();
        Assert.False(asked);
        Assert.Null(mac.Card);
        Assert.Equal("Mac", mac.DeviceWord);
    }

    [Fact]
    public void This_computer_is_asked_once()
    {
        var first = HardwareProbe.System.Probe();
        Assert.Same(first, HardwareProbe.System.Probe());
        Assert.True(first.Cores >= 1);
    }
}
