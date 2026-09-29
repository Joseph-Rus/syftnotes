using System.Buffers.Binary;
using StudyStash.Core;

namespace StudyStash.Core.Tests;

/// <summary>WAVs other tools write: macOS's afconvert (a voice memo on its way to Whisper) writes 16-bit mono PCM in
/// the extensible form (format 0xFFFE, sub-format PCM), with a padding chunk before the sound.</summary>
public class SoundFormatTests
{
    /// <summary>The header afconvert wrote for a 16 kHz mono 16-bit file, then <paramref name="samples"/>.</summary>
    static byte[] Extensible(short[] samples, ushort subFormat = 1)
    {
        var fmt = new byte[40];
        BinaryPrimitives.WriteUInt16LittleEndian(fmt.AsSpan(0), 0xFFFE);
        BinaryPrimitives.WriteUInt16LittleEndian(fmt.AsSpan(2), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(fmt.AsSpan(4), 16000);
        BinaryPrimitives.WriteUInt32LittleEndian(fmt.AsSpan(8), 32000);
        BinaryPrimitives.WriteUInt16LittleEndian(fmt.AsSpan(12), 2);
        BinaryPrimitives.WriteUInt16LittleEndian(fmt.AsSpan(14), 16);
        BinaryPrimitives.WriteUInt16LittleEndian(fmt.AsSpan(16), 22);
        BinaryPrimitives.WriteUInt16LittleEndian(fmt.AsSpan(18), 16);
        BinaryPrimitives.WriteUInt32LittleEndian(fmt.AsSpan(20), 4);
        BinaryPrimitives.WriteUInt16LittleEndian(fmt.AsSpan(24), subFormat);
        var file = new MemoryStream();
        void Chunk(string id, byte[] body)
        {
            file.Write(System.Text.Encoding.ASCII.GetBytes(id));
            var n = new byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(n, (uint)body.Length);
            file.Write(n);
            file.Write(body);
        }
        file.Write("RIFF\0\0\0\0WAVE"u8);
        Chunk("fmt ", fmt);
        Chunk("FLLR", new byte[4020]);
        var data = new byte[samples.Length * 2];
        for (int i = 0; i < samples.Length; i++) BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(i * 2), samples[i]);
        Chunk("data", data);
        return file.ToArray();
    }

    [Fact]
    public void An_extensible_pcm_wav_as_afconvert_writes_it_is_read()
    {
        using var dir = new TempDir();
        string path = Path.Combine(dir.Path, "memo.wav");
        File.WriteAllBytes(path, Extensible([0, 16384, -16384, short.MaxValue]));

        Assert.Equal(16000, Sound.Wav(path).Rate);
        Assert.Equal(4, Sound.Wav(path).Samples);
        var read = Sound.ReadWav(path);
        Assert.Equal(0.5f, read[1], 3);
        Assert.Equal(-0.5f, read[2], 3);
        Assert.Equal(1f, read[3], 3);
    }

    [Fact]
    public void An_extensible_wav_that_isnt_pcm_is_still_refused()
    {
        using var dir = new TempDir();
        string path = Path.Combine(dir.Path, "float.wav");
        File.WriteAllBytes(path, Extensible([0, 1], subFormat: 3));
        Assert.Throws<InvalidDataException>(() => Sound.Wav(path));
    }
}
