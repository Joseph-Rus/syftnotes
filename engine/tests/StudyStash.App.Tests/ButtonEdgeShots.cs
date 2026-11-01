using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using StudyStash.App.Services;
using StudyStash.App.Views;

namespace StudyStash.App.Tests;

/// <summary>
/// The pages with the most small buttons on them, drawn as a Retina screen draws them (two pixels to a point), light
/// and dark, both looks: Settings → Library (Add a laptop…, Show in Finder), Settings → Phone, Settings → Appearance's
/// segmented control, and Home (Record, All notes, the ask bar). At 1x a cut-off edge is a pixel or less and slips
/// by; at 2x it shows (shots/&lt;look&gt;-edges-&lt;page&gt;-2x-&lt;light|dark&gt;.png).
/// </summary>
public class ButtonEdgeShots
{
    static readonly ThemeVariant[] Themes = [ThemeVariant.Light, ThemeVariant.Dark];

    static string Look(SkinKind skin) => skin == SkinKind.Mac ? "mac" : "win";

    static Size SettingsSize(SkinKind skin) => new(1700, skin == SkinKind.Mac ? 908 : 988);

    [AvaloniaTheory]
    [InlineData(SkinKind.Mac)]
    [InlineData(SkinKind.Win)]
    public void Settings_library_and_appearance(SkinKind skin)
    {
        foreach (var (section, page) in new[] { ("One-computer", "library"), ("Appearance", "appearance") })
        {
            var (model, host, home) = SurfaceShots.MakeSettings(section, skin);
            try
            {
                foreach (var t in Themes)
                    Shot.Take($"{Look(skin)}-edges-{page}-2x", skin, t, () => new SettingsView { DataContext = model, DrawChrome = true }, size: SettingsSize(skin), scale: 2);
            }
            finally
            {
                model.Dispose();
                host.Dispose();
                if (Directory.Exists(home)) Directory.Delete(home, recursive: true);
            }
        }
    }

    /// <summary>Settings → Phone on just this computer, not yet connected to Google, Other ways open with a code on
    /// show and a phone added over Tailscale before.</summary>
    [AvaloniaTheory]
    [InlineData(SkinKind.Mac)]
    [InlineData(SkinKind.Win)]
    public async Task Settings_phone(SkinKind skin)
    {
        ((App)Application.Current!).UseSkin(skin);
        string home = Path.Combine(Path.GetTempPath(), "studystash-edges-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(home);
        new AppSettings { SetupDone = true, Role = AppRole.Both }.Save(home);
        var google = await PhonesViewTests.GoogleDrive(account: false);
        var drive = google.Drive;
        var host = new AppHost(home, phoneDrive: _ => drive);
        ViewModels.LibrarySettingsModel.Call call = (method, path, _) => Task.FromResult<JsonObject?>((method.Method, path) switch
        {
            ("GET", "") => new JsonObject
            {
                ["devices"] = new JsonArray(new JsonObject
                {
                    ["id"] = "a", ["name"] = "Sam's iPhone", ["added"] = DateTimeOffset.UtcNow.AddDays(-30).ToString("o"), ["lastSeen"] = DateTimeOffset.UtcNow.AddDays(-9).ToString("o"),
                }),
            },
            ("POST", "/code") => new JsonObject
            {
                ["code"] = "042917", ["url"] = "https://mini.tail1234.ts.net:8443/app/", ["qrUrl"] = "https://mini.tail1234.ts.net:8443/app/#code=042917",
                ["expires"] = DateTimeOffset.UtcNow.AddMinutes(10).ToString("o"),
            },
            _ => null,
        });
        var model = SettingsModel.Make(host).WithPhones(() => call);
        try
        {
            model.Section = "Phone";
            await model.Phones.Load();
            model.Phones.OtherWays = true;
            await model.Phones.AddPhoneCommand.ExecuteAsync(null);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            foreach (var t in Themes)
                Shot.Take($"{Look(skin)}-edges-phone-2x", skin, t, () => new SettingsView { DataContext = model, DrawChrome = true }, size: SettingsSize(skin), scale: 2);
        }
        finally
        {
            model.Dispose();
            host.Dispose();
            await google.DisposeAsync();
            if (Directory.Exists(home)) Directory.Delete(home, recursive: true);
        }
    }

    [AvaloniaFact]
    public void Home()
    {
        foreach (var t in Themes)
        {
            Shot.Take("mac-edges-home-2x", SkinKind.Mac, t, () => new MacLibrary { DataContext = Demo.Semester(), Width = 1280, Height = 860 }, size: new Size(1408, 988), scale: 2);
            Shot.Take("win-edges-home-2x", SkinKind.Win, t, () => new WinLibrary { DataContext = Demo.Semester(), Width = 1280, Height = 860 }, size: new Size(1408, 988), scale: 2);
        }
    }
}
