using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using StudyStash.App.ViewModels;
using StudyStash.App.Views;
using StudyStash.Core;

namespace StudyStash.App.Pictures;

/// <summary>A lecture page for a two-minute recording the microphone heard nothing in: it says nothing was heard and where to
/// check the microphone, not that it's too short. Drawn off screen into STUDYSTASH_SHOTS; only with STUDYSTASH_FILM=1 (demo
/// data only). The words name the system settings of the computer drawing them.</summary>
public class SilentRecordingShots
{
    [AvaloniaFact]
    public void A_two_minute_recording_that_heard_nothing_says_so()
    {
        if (Environment.GetEnvironmentVariable("STUDYSTASH_FILM") != "1") return;
        foreach (var skin in new[] { SkinKind.Mac, SkinKind.Win })
            foreach (var t in new[] { ThemeVariant.Light, ThemeVariant.Dark })
                Shot.Take($"{(skin == SkinKind.Mac ? "mac" : "win")}-silent-recording-{(t == ThemeVariant.Light ? "light" : "dark")}", skin, t, () =>
                {
                    var m = Demo.Semester();
                    m.Overview = null;
                    m.Note = new NoteModel
                    {
                        Id = "rec-1", ClassName = "BIO 110", Dot = Skin.ClassDot(0), Noun = Nouns.Recording, When = "Tuesday 4 November at 10:00 AM, 2 min",
                        Title = "Recording", Pending = NoteModel.TooShort(Nouns.Recording, transcript: false, canContinue: true, seconds: 120),
                        OnContinue = () => { },
                    }.Titled();
                    return skin == SkinKind.Mac ? new MacLibrary { DataContext = m, Width = 1280, Height = 860 } : new WinLibrary { DataContext = m, Width = 1280, Height = 860 };
                }, size: new Size(1408, 988));
    }
}
