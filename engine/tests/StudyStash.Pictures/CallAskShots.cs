using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using StudyStash.App.Platform;
using StudyStash.App.ViewModels;
using StudyStash.App.Views;

namespace StudyStash.App.Pictures;

/// <summary>The ask when a call starts, the offer when it ends, and a Mac's "can only hear you", as the app's own card (Windows; a Mac with Syft Notes' notifications turned off),
/// Mac and Windows look, light and dark. Drawn off screen into STUDYSTASH_SHOTS; only with STUDYSTASH_FILM=1.</summary>
public class CallAskShots
{
    [AvaloniaFact]
    public void The_ask_when_a_call_starts()
    {
        if (Environment.GetEnvironmentVariable("STUDYSTASH_FILM") != "1") return;
        var ended = NoticeWords.CallEnded(() => { }, () => { }, () => true);
        var hears = NoticeWords.HearsOnlyYou(() => { }, () => true);
        foreach (var (name, notice) in new[] { ("ask", NoticeWords.CallAsk(new MicUser("Zoom", false), () => { }, () => true)), ("ended", ended), ("hear-you-only", hears) })
            foreach (var skin in new[] { SkinKind.Mac, SkinKind.Win })
                foreach (var t in new[] { ThemeVariant.Light, ThemeVariant.Dark })
                {
                    // The Mac's own question about recording what it plays: a Windows PC never needs it.
                    if (notice == hears && skin == SkinKind.Win) continue;
                    Shot.Take($"{(skin == SkinKind.Mac ? "mac" : "win")}-call-{name}", skin, t,
                        () => new ToastView { Title = notice.Title, Text = notice.Text, ActionLabel = notice.ActionLabel, SecondLabel = notice.SecondLabel, Width = 380 },
                        size: new Size(520, skin == SkinKind.Mac ? 200 : 240));
                }
    }
}
