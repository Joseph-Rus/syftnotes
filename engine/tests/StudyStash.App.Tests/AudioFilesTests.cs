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

    /// <summary>A made-up video (a navy 16 by 16 picture and 1.5 s of a 440 Hz tone, H.264 and AAC in an .mp4), made with
    /// ffmpeg and then stripped of everything else: <c>-f lavfi -i color=c=navy:s=16x16:r=2:d=1.5 -f lavfi -i sine=frequency=440:duration=1.5:sample_rate=16000
    /// -ac 1 -c:v libx264 -preset veryslow -crf 45 -pix_fmt yuv420p -g 30 -bsf:v filter_units=remove_types=6 -c:a aac -b:a 12k -map_metadata -1</c>.</summary>
    const string TinyVideo = """
        AAAAIGZ0eXBpc29tAAACAGlzb21pc28yYXZjMW1wNDEAAAAIZnJlZQAACgxtZGF0AAAAEmWIgQACn/2zm/ApU4rVEH3DgQAAAAlB
        mghNiCX/q4ABIk8pjDR6pWePmvvqCbiSIiRB1rqvVuq7Vxnj3GexdZ7F8D1b0XrXlPDcp6F2GerOS0FNdeg7Z/vz+DX+v8ehpp2e
        jZKlkn1spVKVRldhNhjJjI0jSNJM8zzAwMiBgY2bNyokUsps2blllllllllllllllllllllllllllllllllllllllllllllllll4
        AAAACQGeECcgl//lgQEElNoJdQS6sl1ZTp//o/+/483qa1+f/9X/3/XrjVuOf/8n/r/PWuJd7//x/+f+POtScBLxnueeeeeCXkyf
        0pdWjyknSpip3p53p53p53uad6dSnkznOp57mqz96jn357P30J0nZC29zc70/O9DzvfL6PfL6fRnyO88nz2VfTn0j7dGokDAwMSH
        Mg2fAP7vISTxMznx4/j9H79PN3erXLtdXvqUEsrbYxUng/17nudzNU1ld1SqminBCysKPw7ooQqpoQje/uQ+PjMHfeEZPgwz++gZ
        PhsZ/eEZPjMM/voGT4zB330If+JrCn8Swfbwu/8P4Hk8IsfxT4AA9C8YziYgycGev/7+f/99Nb88qk31rnxxx4612GdnprxGnDHB
        q+vux7uvuZ2Ev3PyIOvCl6a6XZ2JnZwJJruRcEopGBnNiQIrEVi6h7FILEUjCxEUjglFj4AA8C8YzjQpwXFd//3P/p//1vip14ip
        XWt+JF3QZ2dnmrmkmkxk0auPb6vbRVQJs9hsHPzbdn+Y91U002S4FzZrRUTwMxiWPdTCcVE8TOQTKkHrJlyDKVSNpOL2vADsLxjW
        JhHRVzO//7n9f/++mufO8S+fieKl98WAGBkZLFl1k3Zv7N+fl7LrIcAkiTI0NKAO/O6y5Z16ZXW1saRciuQWMLMckiumCDIRWNlO
        CxAIsxQNJOL2/AD0LxjOJiDJwV3//pn//nUtrNxVdfHj1LzjjsOztXTX14U141393X3Y7uuulyK+1+/J3Y1tW0jOzm7EbtesN/gU
        iqpoVMKRhUKpQVU0JEFCaRwILGZlNCRdKLTwAPAvINI0Gck6p7//3vz//3ki6qYu+u+7krjAERhBEF+1nTa7JjWttm21rziSnjml
        +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
        AAAAAAAAAAAAAAhpbHN0
        """;

    [Fact]
    public void A_video_gives_up_its_sound_the_way_a_recording_does()
    {
        if (!OperatingSystem.IsMacOS() || !File.Exists("/usr/bin/afconvert")) return;
        using var dir = new TempHome();
        string mp4 = Path.Combine(dir.Path, "lecture.mp4"), wav = Path.Combine(dir.Path, "lecture.wav");
        File.WriteAllBytes(mp4, Convert.FromBase64String(TinyVideo));

        AudioFiles.ToRecording(mp4, wav);
        var info = Sound.Wav(wav);

        Assert.Equal(Sound.Rate, info.Rate);
        Assert.InRange(info.Seconds, 1.3, 1.7);
        Assert.Contains(Sound.ReadWav(wav), s => Math.Abs(s) > 0.02f); // the tone came through, not silence
        Assert.True(File.Exists(mp4)); // the video itself is only read
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
