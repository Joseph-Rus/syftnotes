using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using StudyStash.App.Platform;
using StudyStash.App.ViewModels;
using StudyStash.App.Views;

namespace StudyStash.App.Pictures;

/// <summary>The ask when a call starts, as the app's own card (Windows; a Mac with Syft Notes' notifications turned off),
/// Mac and Windows look, light and dark. Drawn off screen into STUDYSTASH_SHOTS; only with STUDYSTASH_FILM=1.</summary>
public class CallAskShots
{
    [AvaloniaFact]
    public void The_ask_when_a_call_starts()
    {
        if (Environment.GetEnvironmentVariable("STUDYSTASH_FILM") != "1") return;
        var ask = NoticeWords.CallAsk(new MicUser("Zoom", false), () => { }, () => true);
        foreach (var skin in new[] { SkinKind.Mac, SkinKind.Win })
            foreach (var t in new[] { ThemeVariant.Light, ThemeVariant.Dark })
                Shot.Take($"{(skin == SkinKind.Mac ? "mac" : "win")}-call-ask", skin, t,
                    () => new ToastView { Title = ask.Title, Text = ask.Text, ActionLabel = ask.ActionLabel, SecondLabel = ask.SecondLabel, Width = 380 },
                    size: new Size(520, skin == SkinKind.Mac ? 200 : 240));
    }
}
