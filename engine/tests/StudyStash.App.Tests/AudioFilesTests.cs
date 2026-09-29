using System.Diagnostics;
using StudyStash.Audio;
using StudyStash.Core;

namespace StudyStash.App.Tests;

/// <summary>A voice memo's recording turned into the recorder's own sound, for real on a Mac: an AAC .m4a (what Voice
/// Memos saves) made with afconvert, read back the way Whisper reads a recording.</summary>
public class AudioFilesTests
{
    [Fact]
    public void A_voice_memo_m4a_becomes_a_16_khz_mono_recording_whisper_can_read()
    {
        if (!OperatingSystem.IsMacOS() || !File.Exists("/usr/bin/afconvert")) return;
        using var dir = new TempHome();
        string tone = Path.Combine(dir.Path, "tone.wav"), m4a = Path.Combine(dir.Path, "memo.m4a"), wav = Path.Combine(dir.Path, "memo.wav");
        // Two seconds of a loud tone, made into an AAC .m4a the way Voice Memos saves one. (Speech from `say` isn't used:
        // a CI Mac's voice came out nearly empty, which said nothing about the conversion.)
        using (var w = new WavWriter(tone)) w.Write(Enumerable.Range(0, 2 * Sound.Rate).Select(i => (float)Math.Sin(i / 9.0) * 0.4f).ToArray());
        using (var make = Process.Start(new ProcessStartInfo("/usr/bin/afconvert", ["-f", "m4af", "-d", "aac", tone, m4a]) { UseShellExecute = false })!)
            make.WaitForExit();
        Assert.True(File.Exists(m4a));

        AudioFiles.ToRecording(m4a, wav);

        var info = Sound.Wav(wav);
        Assert.Equal(Sound.Rate, info.Rate);
        Assert.InRange(info.Seconds, 1.5, 2.5);
        var samples = Sound.ReadWav(wav);
        Assert.Contains(samples, s => Math.Abs(s) > 0.2f); // the tone is in it, not silence
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
