using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using StudyStash.App.Views;

namespace StudyStash.App.Pictures;

/// <summary>
/// A lecture's Share menu with "Send to a Syft Notes user…", Mac and Windows, light and dark. Only when STUDYSTASH_SHOTS
/// names the folder to put them in:
/// <code>STUDYSTASH_SHOTS=/tmp/shots dotnet test engine/tests/StudyStash.Pictures --filter ShareMenuShots</code>
/// </summary>
public sealed class ShareMenuShots
{
    [AvaloniaFact]
    public void Share_menu()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("STUDYSTASH_SHOTS") is { Length: > 0 }, "Only with STUDYSTASH_SHOTS naming a folder.");
        foreach (var skin in new[] { SkinKind.Mac, SkinKind.Win })
            foreach (var t in new[] { ThemeVariant.Light, ThemeVariant.Dark })
                // The menu drawn as it is, in place, rather than in a popup the headless screen doesn't show.
                Shot.Take($"{(skin == SkinKind.Mac ? "mac" : "win")}-share-menu", skin, t,
                    () => DownloadMenu.Build("Cell Biology", includeTranscripts: false, () => { }, () => { }, () => { }, () => { }, () => { }),
                    size: new Size(420, 300));
    }
}
