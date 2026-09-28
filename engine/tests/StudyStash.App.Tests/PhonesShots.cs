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

}
