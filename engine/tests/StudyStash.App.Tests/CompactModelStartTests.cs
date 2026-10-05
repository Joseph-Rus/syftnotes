using System.Runtime.InteropServices;
using StudyStash.App.Services;
using StudyStash.Audio;

namespace StudyStash.App.Tests;

/// <summary>Every computer starts on the compact large-v3 turbo, easy on the computer, unless it's too weak for it;
/// the bigger models stay a choice. An install already on a bigger model its computer keeps up with keeps it, with no
/// word about it; one on more than its computer keeps up with hears about the heaviest that does, once.</summary>
public class CompactModelStartTests
{
    static FakeHardware StrongPc() => new(new HardwareProfile(HostOs.Windows, Architecture.X64, 16, true, 32,
        new GraphicsCard("NVIDIA GeForce RTX 4070", 12, false), Vulkan: true));

    static FakeHardware MidPc() => new(new HardwareProfile(HostOs.Windows, Architecture.X64, 12, true, 16,
        new GraphicsCard("NVIDIA GeForce RTX 3060", 6, false), Vulkan: true));

    static AppHost Host(TempHome home, FakeHardware hardware) =>
        new(home.Path, () => throw new InvalidOperationException("no microphone in this test"), log: _ => { },
            loginItems: new CountingLoginItems(), models: ModelSetting.None, hardware: hardware);

    [Theory]
    [InlineData("apple silicon")]
    [InlineData("strong pc")]
    [InlineData("plain pc")]
    public void A_new_install_starts_on_the_compact_model(string computer)
    {
        using var home = new TempHome();
        var hw = computer switch { "apple silicon" => FakeHardware.AppleSilicon(), "strong pc" => StrongPc(), _ => FakeHardware.PlainPc() };
        using var host = Host(home, hw);
        Assert.Same(WhisperModels.LargeV3TurboSmall, host.Model);
        Assert.Same(WhisperModels.LargeV3TurboSmall, host.Advice.Model);
    }

    [Fact]
    public void A_PC_with_no_graphics_card_starts_on_Parakeet_unless_the_lecture_is_in_a_language_it_does_not_read()
    {
        using var home = new TempHome();
        using (var host = Host(home, FakeHardware.ProcessorPc()))
        {
            Assert.Same(WhisperModels.Parakeet, host.Model);
            Assert.True(host.ParakeetFits);
        }
        using var korean = new TempHome();
        new AppSettings { Language = "ko" }.Save(korean.Path);
        using var other = Host(korean, FakeHardware.ProcessorPc());
        Assert.False(other.ParakeetFits);
        Assert.Same(WhisperModels.LargeV3TurboSmall, other.Model);
    }

    [Fact]
    public async Task An_install_on_large_v3_that_its_Mac_keeps_up_with_keeps_it_and_hears_nothing()
    {
        // Saved by setup or Settings.
        using var saved = new TempHome();
        new AppSettings { SetupDone = true, Model = "large-v3" }.Save(saved.Path);
        using (var host = Host(saved, FakeHardware.AppleSilicon()))
        {
            Assert.Same(WhisperModels.LargeV3, host.Model);
            Assert.Null(await host.ModelSuggestionAsync());
        }

        // From before models were saved: large-v3 on disk, kept, and still no word.
        using var older = new TempHome();
        new AppSettings { SetupDone = true }.Save(older.Path);
        Directory.CreateDirectory(WhisperModels.Dir(older.Path));
        File.WriteAllBytes(WhisperModels.PathFor(older.Path, WhisperModels.LargeV3) + ".part", new byte[10]);
        using (var host = Host(older, FakeHardware.AppleSilicon()))
        {
            Assert.Same(WhisperModels.LargeV3, host.Model);
            Assert.Null(await host.ModelSuggestionAsync());
        }

        // A strong graphics card: the same.
        using var pc = new TempHome();
        new AppSettings { SetupDone = true, Model = "large-v3" }.Save(pc.Path);
        using (var host = Host(pc, StrongPc())) Assert.Null(await host.ModelSuggestionAsync());
    }

    [Fact]
    public async Task More_than_a_computer_keeps_up_with_hears_about_the_heaviest_that_does_once()
    {
        using var home = new TempHome();
        new AppSettings { SetupDone = true, Model = "large-v3" }.Save(home.Path);
        using var host = Host(home, MidPc());
        var suggestion = await host.ModelSuggestionAsync();
        Assert.NotNull(suggestion);
        Assert.Same(WhisperModels.LargeV3Turbo, suggestion.Model); // not the compact one: turbo keeps up here
        Assert.Equal("This PC's graphics card (NVIDIA GeForce RTX 3060) has room for large-v3 turbo, which keeps up with a lecture.", suggestion.Why);
        host.ModelSuggestionMade(suggestion.Model);
        Assert.Null(await host.ModelSuggestionAsync());
    }

    [Fact]
    public void Settings_offers_every_model_and_marks_the_compact_one_for_this_computer()
    {
        var advice = WhisperModels.Advise(FakeHardware.AppleSilicon().Probe());
        using var home = new TempHome();
        var choices = ModelChoice.For(WhisperModels.LargeV3, advice, home.Path).ToList();
        Assert.Equal(["large-v3", "large-v3-turbo", "large-v3-turbo-q5", "parakeet-v3", "small", "base", "whistle"], choices.Select(c => c.Model.Id));
        var marked = Assert.Single(choices, c => c.Recommended);
        Assert.Same(WhisperModels.LargeV3TurboSmall, marked.Model);
        Assert.StartsWith("The compact model keeps up with a lecture and leaves this Mac room", marked.Line, StringComparison.Ordinal);
        Assert.True(choices.Single(c => c.Model == WhisperModels.LargeV3).Chosen); // the one in use stays chosen
        Assert.Contains("heaviest", choices.Single(c => c.Model == WhisperModels.LargeV3).Line, StringComparison.Ordinal);
    }
}
