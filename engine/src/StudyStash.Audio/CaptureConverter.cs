using System.Runtime.InteropServices;
using NAudio.Wave;
using StudyStash.Core;

namespace StudyStash.Audio;

/// <summary>
/// Turns what Windows hands over from a microphone (any rate, any number of channels; 32-bit float, or 16, 24 or
/// 32-bit whole numbers) into the 16 kHz mono floats Whisper hears. Only NAudio's format description is used, so it
/// runs, and is tested, on every system.
/// </summary>
internal sealed class CaptureConverter(WaveFormat format)
{
    readonly Resampler rate = new(format.SampleRate, format.Channels);

    public WaveFormat Format { get; } = format;

    /// <summary>One buffer as 16 kHz mono. A buffer Windows marks silent is silence, whatever it holds.</summary>
    public float[] Convert(ReadOnlySpan<byte> buffer, bool silent = false) => rate.Process(Floats(buffer, Format, silent));

    /// <summary>A buffer as interleaved floats from -1 to 1.</summary>
    internal static float[] Floats(ReadOnlySpan<byte> buffer, WaveFormat format, bool silent = false)
    {
        int bits = format.BitsPerSample, size = Math.Max(1, bits / 8), n = buffer.Length / size;
        if (silent) return new float[n];
        if (IsFloat(format) && bits == 32) return MemoryMarshal.Cast<byte, float>(buffer[..(n * 4)]).ToArray();
        var result = new float[n];
        for (int i = 0; i < n; i++)
        {
            var at = buffer.Slice(i * size, size);
            result[i] = bits switch
            {
                8 => (at[0] - 128) / 128f, // 8-bit sound is unsigned, silence at 128
                16 => BitConverter.ToInt16(at) / 32768f,
                24 => (at[0] | at[1] << 8 | (sbyte)at[2] << 16) / 8388608f,
                32 => BitConverter.ToInt32(at) / 2147483648f,
                _ => 0,
            };
        }
        return result;
    }

    static bool IsFloat(WaveFormat format) => format.Encoding == WaveFormatEncoding.IeeeFloat
        || (format.Encoding == WaveFormatEncoding.Extensible && format is WaveFormatExtensible x && x.SubFormat == IeeeFloat);

    static readonly Guid IeeeFloat = new("00000003-0000-0010-8000-00aa00389b71"); // KSDATAFORMAT_SUBTYPE_IEEE_FLOAT
}
