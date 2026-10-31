using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Headless.XUnit;
using StudyStash.App.Services;
using StudyStash.App.Views;

namespace StudyStash.App.Tests;

/// <summary>Settings → Phone in the real settings window, both looks and both themes, with a code on show and a
/// phone already added (shots/mac-settings-phone*.png, win-settings-phone*.png).</summary>
public class PhonesShots
{
    [AvaloniaTheory]
    [InlineData(SkinKind.Mac)]
    [InlineData(SkinKind.Win)]
    public async Task Settings_phone(SkinKind skin)
    {
        ((App)Application.Current!).UseSkin(skin);
        string home = Path.Combine(Path.GetTempPath(), "studystash-phone-" + Guid.NewGuid().ToString("N"));
        var host = new AppHost(home);
        ViewModels.LibrarySettingsModel.Call call = (method, path, _) => Task.FromResult<JsonObject?>((method.Method, path) switch
        {
            ("GET", "") => new JsonObject
            {
                ["devices"] = new JsonArray(new JsonObject
                {
                    ["id"] = "a", ["name"] = "Sam's iPhone", ["added"] = DateTimeOffset.UtcNow.AddDays(-8).ToString("o"), ["lastSeen"] = DateTimeOffset.UtcNow.ToString("o"),
                }),
            },
            ("POST", "/code") => new JsonObject { ["code"] = "042917", ["url"] = "https://mini.tail1234.ts.net:8443/app/", ["expires"] = DateTimeOffset.UtcNow.AddMinutes(10).ToString("o") },
            _ => null,
        });
        var model = SettingsModel.Make(host).WithPhones(() => call);
        try
        {
            model.Section = "Phone";
            await model.Phones.Load();
            await model.Phones.AddPhoneCommand.ExecuteAsync(null);
            var size = new Size(1700, skin == SkinKind.Mac ? 908 : 988);
            foreach (var t in new[] { Avalonia.Styling.ThemeVariant.Light, Avalonia.Styling.ThemeVariant.Dark })
                Shot.Take($"{(skin == SkinKind.Mac ? "mac" : "win")}-settings-phone", skin, t, () => new SettingsView { DataContext = model, DrawChrome = true }, size: size);
        }
        finally
        {
            model.Dispose();
            host.Dispose();
            if (Directory.Exists(home)) Directory.Delete(home, recursive: true);
        }
    }

    /// <summary>Settings → Phone on just this computer, through Google: not connected, connected, connected with
    /// phones using it (one open now, one three hours ago) and one added over Tailscale before, and Other ways open with
    /// a code on show for an Android phone (shots/mac-settings-phone-google-*.png, win-settings-phone-google-*.png).</summary>
    [AvaloniaTheory]
    [InlineData(SkinKind.Mac, "not-connected")]
    [InlineData(SkinKind.Win, "not-connected")]
    [InlineData(SkinKind.Mac, "connected")]
    [InlineData(SkinKind.Win, "connected")]
    [InlineData(SkinKind.Mac, "phones")]
    [InlineData(SkinKind.Win, "phones")]
    [InlineData(SkinKind.Mac, "other-ways")]
    [InlineData(SkinKind.Win, "other-ways")]
    public async Task Settings_phone_google(SkinKind skin, string state)
    {
        ((App)Application.Current!).UseSkin(skin);
        string home = Path.Combine(Path.GetTempPath(), "studystash-phone-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(home);
        new AppSettings { SetupDone = true, Role = AppRole.Both }.Save(home);
        var google = await PhonesViewTests.GoogleDrive(account: state is not ("not-connected" or "other-ways"), phones: state == "phones" ? 2 : 0);
        var drive = google.Drive;
        var host = new AppHost(home, phoneDrive: _ => drive);
        ViewModels.LibrarySettingsModel.Call call = (method, path, _) => Task.FromResult<JsonObject?>((method.Method, path) switch
        {
            ("GET", "") => new JsonObject
            {
                ["devices"] = state is "phones" or "other-ways"
                    ? new JsonArray(new JsonObject
                    {
                        ["id"] = "a", ["name"] = "Sam's iPhone", ["added"] = DateTimeOffset.UtcNow.AddDays(-30).ToString("o"), ["lastSeen"] = DateTimeOffset.UtcNow.AddDays(-9).ToString("o"),
                    })
                    : new JsonArray(),
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
            if (state == "other-ways")
            {
                model.Phones.OtherWays = true;
                await model.Phones.AddPhoneCommand.ExecuteAsync(null);
            }
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var size = new Size(1700, skin == SkinKind.Mac ? 908 : 988);
            foreach (var t in new[] { Avalonia.Styling.ThemeVariant.Light, Avalonia.Styling.ThemeVariant.Dark })
                Shot.Take($"{(skin == SkinKind.Mac ? "mac" : "win")}-settings-phone-google-{state}", skin, t, () => new SettingsView { DataContext = model, DrawChrome = true }, size: size);
        }
        finally
        {
            model.Dispose();
            host.Dispose();
            await google.DisposeAsync();
            if (Directory.Exists(home)) Directory.Delete(home, recursive: true);
        }
    }
}
