using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia.Headless.XUnit;
using StudyStash.App.Services;
using StudyStash.App.ViewModels;
using StudyStash.Audio;

namespace StudyStash.App.Tests;

/// <summary>A made-up computer, so a test never depends on the one it runs on.</summary>
sealed class FakeHardware(HardwareProfile profile) : IHardwareProbe
{
    public int Asked { get; private set; }

    public HardwareProfile Probe()
    {
        Asked++;
        return profile;
    }

    /// <summary>A PC with only the graphics built into its processor (no card Whisper can use): the compact turbo.</summary>
    public static FakeHardware PlainPc() => new(new HardwareProfile(HostOs.Windows, Architecture.X64, 8, true, 16,
        new GraphicsCard("Intel(R) UHD Graphics 620", 0.125, true), Vulkan: true));

    /// <summary>A Mac with Apple silicon: starts on the compact turbo, keeps up with large-v3.</summary>
    public static FakeHardware AppleSilicon() => new(new HardwareProfile(HostOs.Mac, Architecture.Arm64, 8, true, 16));

    /// <summary>An Intel Mac (no Whisper graphics): the compact turbo.</summary>
    public static FakeHardware IntelMac() => new(new HardwareProfile(HostOs.Mac, Architecture.X64, 8, true, 16));

    /// <summary>For a look's shots: a Mac (Apple silicon, or Intel) or a PC with no card Whisper can use.</summary>
    public static FakeHardware For(SkinKind skin, bool appleSilicon = true) =>
        skin == SkinKind.Win ? PlainPc() : appleSilicon ? AppleSilicon() : IntelMac();
}

public class ModelAdviceTests
{
    static readonly TimeSpan Soon = TimeSpan.FromSeconds(10);
    const string MirrorUrl = "https://mirror.example/models";
    const string PlainPcWhy = "This PC has no graphics card Whisper can use, so the compact model keeps up with a lecture.";

    /// <summary>A host on a made-up computer whose downloads go to a mirror that holds every request open (nothing
    /// real ever downloads): what's downloading can be seen, and nothing finishes.</summary>
    static (AppHost Host, Mirror Mirror) Host(TempHome home, FakeHardware? hardware = null)
    {
        var mirror = new Mirror();
        foreach (var m in WhisperModels.All) mirror.Hold.Add(m.File);
        var host = new AppHost(home.Path, () => throw new InvalidOperationException("no microphone in this test"), log: _ => { },
            loginItems: new CountingLoginItems(), models: new ModelSetting(Mirror: MirrorUrl), http: new HttpClient(mirror),
            hardware: hardware ?? FakeHardware.PlainPc());
        return (host, mirror);
    }

    static async Task Until(Func<bool> done)
    {
        var took = Stopwatch.StartNew();
        while (!done() && took.Elapsed < Soon) await Task.Delay(20, TestContext.Current.CancellationToken);
        Assert.True(done(), "it didn't happen in time");
    }

    [Fact]
    public void A_new_install_uses_the_model_for_its_computer()
    {
        using var home = new TempHome();
        var pc = FakeHardware.PlainPc();
        var (host, _) = Host(home, pc);
        using (host)
        {
            Assert.Same(WhisperModels.LargeV3TurboSmall, host.Model);
            Assert.Equal(PlainPcWhy, host.Advice.Why);
            Assert.Same(host.Hardware, host.Hardware);
            Assert.Equal(1, pc.Asked);
        }
        using var mac = new TempHome();
        var (onMac, _) = Host(mac, FakeHardware.AppleSilicon());
        using (onMac) Assert.Same(WhisperModels.LargeV3TurboSmall, onMac.Model); // Apple silicon starts compact too
    }

    [Fact]
    public void The_app_settings_keep_the_model_and_the_suggestion_made()
    {
        using var home = new TempHome();
        new AppSettings { Model = "small", ModelSuggested = "large-v3-turbo-q5" }.Save(home.Path);
        var back = AppSettings.Load(home.Path);
        Assert.Equal("small", back.Model);
        Assert.Equal("large-v3-turbo-q5", back.ModelSuggested);
        Assert.Contains("\"model_suggested\": \"large-v3-turbo-q5\"", File.ReadAllText(AppSettings.PathIn(home.Path)), StringComparison.Ordinal);

        // A settings file from before has none: nothing was suggested yet.
        File.WriteAllText(AppSettings.PathIn(home.Path), """{ "setup_done": true, "model": "" }""");
        Assert.Equal("", AppSettings.Load(home.Path).ModelSuggested);
    }

    [Fact]
    public async Task An_install_from_before_keeps_large_v3_and_hears_about_a_lighter_one_once()
    {
        using var home = new TempHome();
        // Set up before models were picked for the computer: no model saved, large-v3 part-way down.
        new AppSettings { SetupDone = true }.Save(home.Path);
        Directory.CreateDirectory(WhisperModels.Dir(home.Path));
        File.WriteAllBytes(WhisperModels.PathFor(home.Path, WhisperModels.LargeV3) + ".part", new byte[1000]);

        var (host, _) = Host(home);
        using (host)
        {
            // Kept: nothing switches (or downloads) by itself.
            Assert.Same(WhisperModels.LargeV3, host.Model);
            Assert.Equal("large-v3", AppSettings.Load(home.Path).Model);
            var suggestion = await host.ModelSuggestionAsync();
            Assert.NotNull(suggestion);
            Assert.Same(WhisperModels.LargeV3TurboSmall, suggestion.Model);
            Assert.Equal(PlainPcWhy, suggestion.Why);
            host.ModelSuggestionMade(suggestion.Model);
            Assert.Null(await host.ModelSuggestionAsync());
            Assert.Same(WhisperModels.LargeV3, host.Model);
        }
        // Once means once, across runs.
        var (again, _) = Host(home);
        using (again) Assert.Null(await again.ModelSuggestionAsync());
    }

    [Fact]
    public async Task No_suggestion_where_the_model_suits_the_computer()
    {
        // large-v3 on Apple silicon is the one for it.
        using var mac = new TempHome();
        new AppSettings { SetupDone = true, Model = "large-v3" }.Save(mac.Path);
        var (onMac, _) = Host(mac, FakeHardware.AppleSilicon());
        using (onMac) Assert.Null(await onMac.ModelSuggestionAsync());

        // A lighter one than the computer could run is the student's call.
        using var pc = new TempHome();
        new AppSettings { SetupDone = true, Model = "small" }.Save(pc.Path);
        var (onPc, _) = Host(pc);
        using (onPc) Assert.Null(await onPc.ModelSuggestionAsync());

        // Before setup's done, setup itself says it; a library never records.
        using var fresh = new TempHome();
        new AppSettings { Model = "large-v3" }.Save(fresh.Path);
        var (first, _) = Host(fresh);
        using (first) Assert.Null(await first.ModelSuggestionAsync());
        using var library = new TempHome();
        new AppSettings { SetupDone = true, Role = AppRole.Library, Model = "large-v3" }.Save(library.Path);
        var (lib, _) = Host(library);
        using (lib) Assert.Null(await lib.ModelSuggestionAsync());

        // The environment naming a model (tests, the self-test) is never second-guessed.
        using var named = new TempHome();
        new AppSettings { SetupDone = true, Model = "large-v3" }.Save(named.Path);
        using var env = new AppHost(named.Path, log: _ => { }, loginItems: new CountingLoginItems(), models: new ModelSetting(WhisperModels.LargeV3),
            hardware: FakeHardware.PlainPc());
        Assert.Null(await env.ModelSuggestionAsync());
    }

    [Fact]
    public void With_nothing_downloaded_yet_an_old_install_gets_the_model_for_its_computer()
    {
        using var home = new TempHome();
        new AppSettings { SetupDone = true }.Save(home.Path);
        var (host, _) = Host(home);
        using (host)
        {
            Assert.Same(WhisperModels.LargeV3TurboSmall, host.Model);
            Assert.Equal("", host.Settings.Model);
        }
    }

    [AvaloniaFact]
    public async Task Setup_offers_the_model_for_this_computer_and_the_student_can_pick_another()
    {
        using var home = new TempHome();
        var (host, mirror) = Host(home);
        using (host)
        {
            var setup = Setup.Make(host, AppRole.Laptop);
            Assert.Equal(["large-v3", "large-v3-turbo", "large-v3-turbo-q5", "small", "base"], setup.Models.Select(c => c.Model.Id));
            var recommended = Assert.Single(setup.Models, c => c.Recommended);
            Assert.Same(WhisperModels.LargeV3TurboSmall, recommended.Model);
            Assert.Same(recommended, setup.ChosenModel);
            Assert.True(recommended.Chosen);
            Assert.Equal(PlainPcWhy, recommended.Line);
            Assert.Equal("574 MB", recommended.Size);
            Assert.Equal("Whisper large-v3 turbo (compact)", setup.ModelName);
            Assert.Contains("The model is about 574 MB", setup.ModelBody, StringComparison.Ordinal);
            Assert.True(setup.ShowModelCard);
            Assert.Equal(0, mirror.Asked); // nothing downloads before the model step

            // The model step downloads the recommended one.
            setup.Go(SetupStep.Model);
            await Until(() => host.DownloadingModel == WhisperModels.LargeV3TurboSmall);

            // Change opens the list in place of the card and the download; picking another downloads that one instead.
            setup.ChangeModelCommand.Execute(null);
            Assert.True(setup.ChoosingModel);
            Assert.False(setup.ShowModelCard);
            Assert.False(setup.ShowModelDownload);
            var small = setup.Models.Single(c => c.Model == WhisperModels.Small);
            setup.PickModelCommand.Execute(small);
            Assert.False(setup.ChoosingModel);
            Assert.Same(small, setup.ChosenModel);
            Assert.Equal(small.Model.About, small.Line);
            Assert.Equal("488 MB", setup.ModelSize);
            Assert.Same(WhisperModels.Small, host.Model);
            Assert.Equal("small", AppSettings.Load(home.Path).Model);
            await Until(() => host.DownloadingModel == WhisperModels.Small);
            Assert.Single(setup.Models, c => c.Chosen);

            // Finishing keeps the pick, and the advice has been heard: no suggestion later.
            Setup.Finish(setup, host);
            var saved = AppSettings.Load(home.Path);
            Assert.Equal("small", saved.Model);
            Assert.Equal("large-v3-turbo-q5", saved.ModelSuggested);
            Assert.Null(await host.ModelSuggestionAsync());
        }
    }

    [AvaloniaFact]
    public void A_library_setup_never_asks_about_the_model()
    {
        using var home = new TempHome();
        var pc = FakeHardware.PlainPc();
        var (host, _) = Host(home, pc);
        using (host)
        {
            var setup = Setup.Make(host, AppRole.Library);
            Assert.Empty(setup.Models);
            Assert.Null(setup.ChosenModel);
            Setup.Finish(setup, host);
            Assert.Equal("", AppSettings.Load(home.Path).Model);
        }
    }

    [AvaloniaFact]
    public async Task Settings_marks_the_model_for_this_computer_and_switching_downloads_it()
    {
        using var home = new TempHome();
        new AppSettings { SetupDone = true, Model = "large-v3" }.Save(home.Path);
        var (host, _) = Host(home);
        using (host)
        using (var settings = SettingsModel.Make(host))
        {
            Assert.Same(WhisperModels.LargeV3TurboSmall, Assert.Single(settings.Models, c => c.Recommended).Model);
            Assert.Same(WhisperModels.LargeV3, Assert.Single(settings.Models, c => c.Chosen).Model);
            Assert.Equal(PlainPcWhy + " Whisper large-v3 may fall behind a lecture here: pick Whisper large-v3 turbo (compact) to switch. The one you have stays.",
                settings.ModelAdviceLine);

            settings.PickModelCommand.Execute(settings.Models.Single(c => c.Recommended));
            Assert.Equal("large-v3-turbo-q5", AppSettings.Load(home.Path).Model);
            await Until(() => host.DownloadingModel == WhisperModels.LargeV3TurboSmall);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.Equal(PlainPcWhy, settings.ModelAdviceLine);
            Assert.True(settings.ModelDownloading);
            Assert.StartsWith("Downloading Whisper large-v3 turbo (compact): 0 MB of 574 MB.", settings.ModelLine, StringComparison.Ordinal);
        }
    }

    /// <summary>A model file of the right size (nothing written: the disk keeps it sparse), so it counts as here.</summary>
    internal static void PretendDownloaded(string home, WhisperModel m)
    {
        Directory.CreateDirectory(WhisperModels.Dir(home));
        using var f = File.Create(WhisperModels.PathFor(home, m));
        f.SetLength(m.Bytes);
    }

    [AvaloniaFact]
    public void A_model_no_longer_in_use_is_removed_only_when_the_student_says_so()
    {
        using var home = new TempHome();
        new AppSettings { SetupDone = true, Model = "small" }.Save(home.Path);
        PretendDownloaded(home.Path, WhisperModels.Base);
        var (host, _) = Host(home);
        using (host)
        using (var settings = SettingsModel.Make(host))
        {
            Assert.Equal("Also on this computer: Whisper base (148 MB).", settings.SpareLine);
            Assert.True(settings.HasSpare);

            settings.AskRemoveSpareCommand.Execute(null);
            Assert.True(settings.ConfirmingRemove);
            Assert.False(settings.HasSpare);
            Assert.Equal("Remove Whisper base from this computer? It frees 148 MB, and you can download it again any time.", settings.RemoveQuestion);
            settings.KeepSpareCommand.Execute(null);
            Assert.True(WhisperModels.IsDownloaded(home.Path, WhisperModels.Base));

            settings.AskRemoveSpareCommand.Execute(null);
            settings.RemoveSpareCommand.Execute(null);
            Assert.False(File.Exists(WhisperModels.PathFor(home.Path, WhisperModels.Base)));
            Assert.Equal("", settings.SpareLine);
            Assert.False(settings.ConfirmingRemove);
        }
    }

    [Fact]
    public void The_model_in_use_is_never_removed()
    {
        using var home = new TempHome();
        new AppSettings { SetupDone = true, Model = "base" }.Save(home.Path);
        PretendDownloaded(home.Path, WhisperModels.Base);
        var (host, _) = Host(home);
        using (host)
        {
            Assert.Equal("Whisper base is the one in use.", host.RemoveModel(WhisperModels.Base));
            Assert.True(WhisperModels.IsDownloaded(home.Path, WhisperModels.Base));
            Assert.Null(host.RemoveModel(WhisperModels.Small)); // nothing there: nothing to do
        }
    }

    [Fact]
    public void The_advice_line_names_the_one_in_use_only_when_its_heavier()
    {
        var advice = WhisperModels.Advise(FakeHardware.PlainPc().Probe());
        Assert.Equal(PlainPcWhy, SettingsModel.AdviceWords(WhisperModels.LargeV3TurboSmall, advice));
        Assert.Equal(PlainPcWhy, SettingsModel.AdviceWords(WhisperModels.Base, advice));
        Assert.EndsWith("Whisper large-v3 turbo may fall behind a lecture here: pick Whisper large-v3 turbo (compact) to switch. The one you have stays.",
            SettingsModel.AdviceWords(WhisperModels.LargeV3Turbo, advice), StringComparison.Ordinal);
    }
}

public class FallingBehindTests
{
    static readonly ModelAdvice PlainPc = WhisperModels.Advise(FakeHardware.PlainPc().Probe());
    static readonly ModelAdvice AppleSilicon = WhisperModels.Advise(FakeHardware.AppleSilicon().Probe());

    [Fact]
    public void A_minute_or_two_behind_is_whispers_own_rhythm()
    {
        Assert.Null(AppHost.BehindWords(600, 560, WhisperModels.LargeV3, PlainPc));
        Assert.Null(AppHost.BehindWords(600, 421, WhisperModels.LargeV3, PlainPc));
        Assert.Null(AppHost.BehindWords(100, 0, WhisperModels.LargeV3, PlainPc));
    }

    [Fact]
    public void Clearly_behind_says_so_and_names_a_lighter_model()
    {
        var (title, text) = AppHost.BehindWords(900, 600, WhisperModels.LargeV3, PlainPc)!.Value;
        Assert.Equal("The transcript is falling behind", title);
        Assert.Equal("Whisper large-v3 is slower than the lecture on this computer. Nothing is lost: it catches up after class. "
                     + "Whisper large-v3 turbo (compact) would keep up: switch in Settings → Recording.", text);

        // Already on the one for this computer (or lighter): the next lighter one.
        Assert.EndsWith("Whisper small would keep up: switch in Settings → Recording.",
            AppHost.BehindWords(900, 600, WhisperModels.LargeV3TurboSmall, PlainPc)!.Value.Text, StringComparison.Ordinal);
        // A bigger one than a Mac starts on, falling behind: the compact one it starts on.
        Assert.EndsWith("Whisper large-v3 turbo (compact) would keep up: switch in Settings → Recording.",
            AppHost.BehindWords(900, 600, WhisperModels.LargeV3, AppleSilicon)!.Value.Text, StringComparison.Ordinal);

        // The lightest one has nothing lighter to offer (tiny is only for trying things out).
        Assert.Equal("Whisper base is slower than the lecture on this computer. Nothing is lost: it catches up after class.",
            AppHost.BehindWords(900, 600, WhisperModels.Base, PlainPc)!.Value.Text);
    }

    [Fact]
    public void Nothing_to_say_while_nothing_records()
    {
        using var home = new TempHome();
        using var host = new AppHost(home.Path, log: _ => { }, loginItems: new CountingLoginItems(), models: ModelSetting.None, hardware: FakeHardware.PlainPc());
        Assert.Null(host.FallingBehind());
    }
}
