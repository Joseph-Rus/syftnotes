using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using StudyStash.App.ViewModels;
using StudyStash.Core;
using StudyStash.App.Views;

namespace StudyStash.App.Pictures;

/// <summary>The notification a lecture pauses with when the microphone is allowed but gives only silence (muted, or its
/// input at zero): it points at the Sound settings, not Privacy. Drawn off screen into STUDYSTASH_SHOTS; only with
/// STUDYSTASH_FILM=1.</summary>
public class MicSilentShots
{
    [AvaloniaFact]
    public void The_paused_notice_for_a_muted_microphone()
    {
        if (Environment.GetEnvironmentVariable("STUDYSTASH_FILM") != "1") return;
        foreach (var skin in new[] { SkinKind.Mac, SkinKind.Win })
            foreach (var t in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            {
                bool mac = skin == SkinKind.Mac;
                var (text, action, _) = NoticeWords.Paused(mac ? RecordingWords.MutedMac : RecordingWords.MutedWindows, windows: !mac);
                Shot.Take($"{(mac ? "mac" : "win")}-paused-muted-mic", skin, t,
                    () => new ToastView { Title = NoticeWords.PausedTitle, Text = text, ActionLabel = action, Width = 400 }, size: new Size(560, 300));
            }
    }
}
