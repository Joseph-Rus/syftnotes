using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using StudyStash.App.ViewModels;
using StudyStash.App.Views;

namespace StudyStash.App.Tests;

/// <summary>Settings → AI engines' API keys: one saved (its last four), two to paste.</summary>
public class KeyShots
{
    [AvaloniaFact]
    public void Mac_and_Win_api_keys()
    {
        foreach (var t in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            Shot.Take("mac-13-api-keys", SkinKind.Mac, t, () => new MacAiEngines { DataContext = Model(), Width = 640, Height = 1700 }, size: new Avalonia.Size(768, 1828));
            Shot.Take("win-13-api-keys", SkinKind.Win, t, () => new WinAiEngines { DataContext = Model(), Width = 680, Height = 1800 }, size: new Avalonia.Size(808, 1928));
        }
    }

    static AiEnginesModel Model()
    {
        var m = AiDemo.Engines();
        m.Keys[2].Hint = "…a1b2";
        return m;
    }
}
