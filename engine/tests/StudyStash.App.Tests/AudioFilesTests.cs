using System.Diagnostics;
using StudyStash.Audio;
using StudyStash.Core;

namespace StudyStash.App.Tests;

/// <summary>A voice memo's recording turned into the recorder's own sound, for real on a Mac: an AAC .m4a (what Voice
/// Memos saves) from macOS's own speech, through afconvert, read back the way Whisper reads a recording.</summary>
public class AudioFilesTests
{
    [Fact]
    public void A_voice_memo_m4a_becomes_a_16_khz_mono_recording_whisper_can_read()
    {
        if (!OperatingSystem.IsMacOS() || !File.Exists("/usr/bin/say")) return;
        using var dir = new TempHome();
        string m4a = Path.Combine(dir.Path, "memo.m4a"), wav = Path.Combine(dir.Path, "memo.wav");
        using (var say = Process.Start(new ProcessStartInfo("/usr/bin/say", ["-o", m4a, "--data-format=aac", "Gradient descent lowers the loss one step at a time."]) { UseShellExecute = false })!)
            say.WaitForExit();
        Assert.True(File.Exists(m4a));

        AudioFiles.ToRecording(m4a, wav);

        var info = Sound.Wav(wav);
        Assert.Equal(Sound.Rate, info.Rate);
        Assert.InRange(info.Seconds, 1, 10);
        var samples = Sound.ReadWav(wav);
        Assert.Contains(samples, s => Math.Abs(s) > 0.05f); // there's speech in it, not silence
    }

    [Fact]
    public void What_isnt_a_recording_is_refused_in_words()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var dir = new TempHome();
        string bad = Path.Combine(dir.Path, "notes.m4a");
        File.WriteAllText(bad, "not sound at all");
        var e = Assert.Throws<InvalidOperationException>(() =>
        {
            AudioFiles.ToRecording(bad, Path.Combine(dir.Path, "out.wav"));
        });
        Assert.StartsWith(AudioFiles.CantRead, e.Message);
    }
}
