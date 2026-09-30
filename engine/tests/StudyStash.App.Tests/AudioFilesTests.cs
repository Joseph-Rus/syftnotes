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
        if (!OperatingSystem.IsMacOS() || !File.Exists("/usr/bin/afconvert")) return;
        using var dir = new TempHome();
        string source = Path.Combine(dir.Path, "tone.wav"), m4a = Path.Combine(dir.Path, "memo.m4a"), wav = Path.Combine(dir.Path, "memo.wav");
        // An AAC .m4a like Voice Memos saves, made from a known sound (2 s of a 440 Hz tone, stereo at 44.1 kHz) by macOS's
        // own encoder. It used to be the say command's speech, but say on a CI runner often writes a memo only
        // milliseconds long, its speech service not yet up, and that failed the test rather than the conversion.
        WriteTone(source, seconds: 2, rate: 44_100, channels: 2);
        using (var encode = Process.Start(new ProcessStartInfo("/usr/bin/afconvert", ["-f", "m4af", "-d", "aac", source, m4a]) { UseShellExecute = false })!)
            encode.WaitForExit();
        Assert.True(File.Exists(m4a));

        AudioFiles.ToRecording(m4a, wav);
        var info = Sound.Wav(wav);

        Assert.Equal(Sound.Rate, info.Rate);
        Assert.InRange(info.Seconds, 1.8, 2.3);
        var samples = Sound.ReadWav(wav);
        Assert.Contains(samples, s => Math.Abs(s) > 0.05f); // the tone came through, not silence
    }

    /// <summary>A 16-bit PCM WAV of a 440 Hz tone at half volume.</summary>
    static void WriteTone(string path, double seconds, int rate, int channels)
    {
        int frames = (int)(seconds * rate), bytes = frames * channels * 2;
        using var w = new BinaryWriter(File.Create(path));
        w.Write("RIFF"u8); w.Write(36 + bytes); w.Write("WAVE"u8);
        w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)channels); w.Write(rate);
        w.Write(rate * channels * 2); w.Write((short)(channels * 2)); w.Write((short)16);
        w.Write("data"u8); w.Write(bytes);
        for (int i = 0; i < frames; i++)
        {
            short v = (short)(Math.Sin(2 * Math.PI * 440 * i / rate) * short.MaxValue * 0.5);
            for (int c = 0; c < channels; c++) w.Write(v);
        }
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
