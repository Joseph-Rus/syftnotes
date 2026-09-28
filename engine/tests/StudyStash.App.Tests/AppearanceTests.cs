using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using StudyStash.App.Services;

namespace StudyStash.App.Tests;

/// <summary>Settings → Appearance's mode (Match system, Light, Dark): saved next to the colour theme, a settings
/// file from before it existed loads as Match system, and picking one sets the whole app's theme variant at once.</summary>
public class AppearanceTests
{
    [Fact]
    public void A_new_settings_file_defaults_to_match_system() =>
        Assert.Equal(AppAppearance.System, new AppSettings().Appearance);

    [Fact]
    public void An_old_settings_file_without_appearance_loads_as_match_system()
    {
        using var home = new TempHome();
        File.WriteAllText(AppSettings.PathIn(home.Path), """{"setup_done": true, "theme": "Plum"}""");

        var loaded = AppSettings.Load(home.Path);

        Assert.Equal(AppAppearance.System, loaded.Appearance);
        Assert.Equal("Plum", loaded.Theme);
    }

    [Fact]
    public void The_setting_round_trips_through_app_json()
    {
        using var home = new TempHome();
        new AppSettings { SetupDone = true, Appearance = AppAppearance.Dark }.Save(home.Path);

        Assert.Equal(AppAppearance.Dark, AppSettings.Load(home.Path).Appearance);
    }

    [Fact]
    public void VariantFor_maps_each_mode_to_Avalonias_own()
    {
        Assert.Equal(ThemeVariant.Default, Skin.VariantFor(AppAppearance.System));
        Assert.Equal(ThemeVariant.Light, Skin.VariantFor(AppAppearance.Light));
        Assert.Equal(ThemeVariant.Dark, Skin.VariantFor(AppAppearance.Dark));
    }

    /// <summary>Opening Settings on a computer that already picked Dark shows Dark as chosen, and doesn't resave the
    /// file just for having been opened (the same guard as the colour theme).</summary>
    [AvaloniaFact]
    public void Opening_settings_with_a_saved_mode_shows_it_and_saves_nothing()
    {
        using var home = new TempHome();
        new AppSettings { SetupDone = true, Appearance = AppAppearance.Dark }.Save(home.Path);
        byte[] before = File.ReadAllBytes(AppSettings.PathIn(home.Path));
        using var host = new AppHost(home.Path);
        try
        {
            using var model = SettingsModel.Make(host);

            Assert.Equal(AppAppearance.Dark, model.Appearance);
            Assert.True(model.AppearanceIsDark);
            Assert.True(model.AppearanceOptions.Single(o => o.Mode == AppAppearance.Dark).Chosen);
            Assert.False(model.AppearanceOptions.Single(o => o.Mode == AppAppearance.System).Chosen);
            Assert.Equal(before, File.ReadAllBytes(AppSettings.PathIn(home.Path)));
        }
        finally
        {
            Skin.UseAppearance(AppAppearance.System);
        }
    }

    /// <summary>Picking a mode in Settings saves it and, at once, sets the variant every open window (and anything
    /// that reads a window's own ActualThemeVariant) follows — no restart, the same as picking a colour theme.</summary>
    [AvaloniaFact]
    public void Picking_a_mode_in_settings_saves_it_and_sets_the_variant_everywhere()
    {
        using var home = new TempHome();
        using var host = new AppHost(home.Path);
        try
        {
            using var model = SettingsModel.Make(host);
            var window = new Window { Width = 40, Height = 40 };
            window.Show();
            try
            {
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(ThemeVariant.Default, Application.Current!.RequestedThemeVariant);
                Assert.True(model.AppearanceIsSystem);

                model.PickAppearanceCommand.Execute(AppAppearance.Dark);
                Dispatcher.UIThread.RunJobs();

                Assert.Equal(AppAppearance.Dark, model.Appearance);
                Assert.True(model.AppearanceIsDark);
                Assert.True(model.AppearanceOptions.Single(o => o.Mode == AppAppearance.Dark).Chosen);
                Assert.Equal(ThemeVariant.Dark, Application.Current!.RequestedThemeVariant);
                Assert.Equal(ThemeVariant.Dark, window.ActualThemeVariant);
                Assert.Equal(AppAppearance.Dark, AppSettings.Load(home.Path).Appearance);

                model.PickAppearanceCommand.Execute(AppAppearance.Light);
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(ThemeVariant.Light, Application.Current!.RequestedThemeVariant);
                Assert.Equal(ThemeVariant.Light, window.ActualThemeVariant);
                Assert.Equal(AppAppearance.Light, AppSettings.Load(home.Path).Appearance);

                model.PickAppearanceCommand.Execute(AppAppearance.System);
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(ThemeVariant.Default, Application.Current!.RequestedThemeVariant);
                Assert.Equal(AppAppearance.System, AppSettings.Load(home.Path).Appearance);
            }
            finally
            {
                window.Close();
            }
        }
        finally
        {
            Skin.UseAppearance(AppAppearance.System);
        }
    }
}
