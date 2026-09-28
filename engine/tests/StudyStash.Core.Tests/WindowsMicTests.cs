using System.Diagnostics;
using System.Runtime.InteropServices;
using NAudio.Wave;
using StudyStash.Audio;

namespace StudyStash.Core.Tests;

/// <summary>A test that needs Windows (its sound system): skipped on a Mac or Linux.</summary>
public sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute()
    {
        if (!OperatingSystem.IsWindows()) Skip = "needs Windows";
    }
}

/// <summary>
/// The Windows microphone: what WASAPI hands over becomes 16 kHz mono (tested everywhere), what Windows answers
/// becomes the student's words, and opening it never hangs. On Windows (GitHub's runner has no microphone) the real
/// path is opened and must say "No microphone found".
/// </summary>
public class WindowsMicTests
{
    // --- the format: any rate, channels and sample type to 16 kHz mono -----------------------------------------

    static byte[] Bytes<T>(T[] samples) where T : struct => MemoryMarshal.AsBytes(samples.AsSpan()).ToArray();

    [Fact]
    public void Sixteen_bit_whole_numbers_become_floats()
    {
        var format = new WaveFormat(48000, 16, 2);
        var f = CaptureConverter.Floats(Bytes(new short[] { 0, short.MaxValue, short.MinValue, 16384 }), format);
        Assert.Equal([0f, 32767 / 32768f, -1f, 0.5f], f);
    }

    [Fact]
    public void Twenty_four_bit_samples_keep_their_sign()
    {
        var format = new WaveFormat(44100, 24, 1);
        // 0x400000 is half of full scale; 0xC00000 is minus half.
        byte[] raw = [0x00, 0x00, 0x40, 0x00, 0x00, 0xC0, 0xFF, 0xFF, 0x7F];
        var f = CaptureConverter.Floats(raw, format);
        Assert.Equal(0.5f, f[0]);
        Assert.Equal(-0.5f, f[1]);
        Assert.Equal(8388607 / 8388608f, f[2]);
    }

    [Fact]
    public void Thirty_two_bit_whole_numbers_and_eight_bit_sound_become_floats()
    {
        Assert.Equal([0.5f, -1f], CaptureConverter.Floats(Bytes(new[] { 1 << 30, int.MinValue }), new WaveFormat(16000, 32, 1)));
        Assert.Equal([0f, -1f, 0.5f], CaptureConverter.Floats([128, 0, 192], new WaveFormat(8000, 8, 1)));
    }

    [Fact]
    public void Float_sound_is_read_as_it_is_plain_or_extensible()
    {
        float[] samples = [0.25f, -0.75f, 1f];
        Assert.Equal(samples, CaptureConverter.Floats(Bytes(samples), WaveFormat.CreateIeeeFloatWaveFormat(48000, 1)));
        // What a Windows mix format usually is: WAVE_FORMAT_EXTENSIBLE, float, 48 kHz stereo.
        var mix = new WaveFormatExtensible(48000, 32, 2);
        Assert.Equal(WaveFormatEncoding.Extensible, mix.Encoding);
        Assert.Equal(samples, CaptureConverter.Floats(Bytes(samples), mix));
    }

    [Fact]
    public void A_buffer_marked_silent_is_silence_whatever_it_holds()
    {
        var f = CaptureConverter.Floats(Bytes(new float[] { 0.9f, 0.9f, 0.9f, 0.9f }), WaveFormat.CreateIeeeFloatWaveFormat(48000, 2), silent: true);
        Assert.Equal(new float[4], f);
    }

    [Fact]
    public void A_48k_stereo_microphone_becomes_16k_mono_speech()
    {
        // A second of a 1 kHz tone, as a USB microphone's 16-bit stereo, in 10 ms buffers as WASAPI hands them over.
        var c = new CaptureConverter(new WaveFormat(48000, 16, 2));
        var output = new List<float>();
        for (int block = 0; block < 100; block++)
        {
            var buf = new short[480 * 2];
            for (int i = 0; i < 480; i++)
            {
                short v = (short)(0.5 * 32767 * Math.Sin(2 * Math.PI * 1000 * (block * 480 + i) / 48000));
                buf[2 * i] = buf[2 * i + 1] = v;
            }
            output.AddRange(c.Convert(Bytes(buf)));
        }
        Assert.InRange(output.Count, 15900, 16000); // the filter holds back a few samples for the next buffer
        double rms = Math.Sqrt(output.Skip(500).Take(15000).Average(x => x * (double)x));
        Assert.InRange(rms * Math.Sqrt(2), 0.48, 0.52);
    }

    [Fact]
    public void Sixteen_k_mono_float_passes_straight_through()
    {
        var c = new CaptureConverter(WaveFormat.CreateIeeeFloatWaveFormat(16000, 1));
        float[] samples = [0.1f, 0.2f, -0.3f];
        Assert.Equal(samples, c.Convert(Bytes(samples)));
    }

    // --- what Windows says, in the student's words ---------------------------------------------------------------

    [Fact]
    public void Windows_refusing_the_microphone_says_how_to_allow_it()
    {
        foreach (var t in new[] { MicTrouble.FromWindows(MicTrouble.AccessDenied), MicTrouble.FromWindows(0, refused: true) })
        {
            Assert.Equal(MicTroubleKind.Denied, t.Kind);
            Assert.Equal(RecordingWords.CantHearWindows, t.Detail);
            Assert.Equal("ms-settings:privacy-microphone", t.ActionUrl);
            Assert.Equal("Open Settings", t.ActionLabel);
        }
    }

    [Fact]
    public void No_microphone_taken_or_anything_else_each_have_their_words()
    {
        foreach (int hr in new[] { MicTrouble.NotFound, MicTrouble.DeviceInvalidated })
        {
            var t = MicTrouble.FromWindows(hr);
            Assert.Equal(MicTroubleKind.NoDevice, t.Kind);
            Assert.Equal("No microphone found", t.Title);
            Assert.Equal("ms-settings:sound", t.ActionUrl);
        }
        var used = MicTrouble.FromWindows(MicTrouble.DeviceInUse);
        Assert.Equal(MicTroubleKind.InUse, used.Kind);
        Assert.Equal("Another app is using the microphone", used.Title);
        Assert.Equal("ms-settings:sound", used.ActionUrl);
        var other = MicTrouble.FromWindows(unchecked((int)0x88890010)); // AUDCLNT_E_SERVICE_NOT_RUNNING
        Assert.Equal(MicTroubleKind.Other, other.Kind);
        Assert.Contains("restart your PC", other.Detail, StringComparison.Ordinal);
        foreach (var t in new[] { used, other }) Assert.DoesNotContain("0x", t.Title + t.Detail, StringComparison.Ordinal);
    }

    // --- never hangs -------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Opening_that_never_answers_gives_up_in_time_and_closes_it_later()
    {
        using var release = new ManualResetEventSlim();
        var closed = new TaskCompletionSource<string>();
        var watch = Stopwatch.StartNew();
        var e = Assert.Throws<TimeoutException>(() => Opening.Within(TimeSpan.FromMilliseconds(200), () =>
        {
            release.Wait(TimeSpan.FromSeconds(10));
            return "the microphone";
        }, closed.SetResult, () => new TimeoutException("late")));
        Assert.Equal("late", e.Message);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(3), $"took {watch.Elapsed}");
        release.Set();
        Assert.Equal("the microphone", await closed.Task.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void Opening_that_fails_throws_its_own_exception_and_one_that_works_is_returned()
    {
        var e = Assert.Throws<MicrophoneException>(() => Opening.Within<string>(TimeSpan.FromSeconds(5),
            () => throw new MicrophoneException(MicTrouble.NoDevice(windows: true)), _ => { }, () => new TimeoutException()));
        Assert.Equal(MicTroubleKind.NoDevice, e.Trouble.Kind);
        Assert.Equal("ok", Opening.Within(TimeSpan.FromSeconds(5), () => "ok", _ => { }, () => new TimeoutException()));
    }

    // --- the real Windows path -----------------------------------------------------------------------------------

    [WindowsFact]
    public void A_pc_with_no_microphone_says_so_in_plain_words()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var mic = new WindowsSound(withComputerAudio: false, () => null);
        var e = Assert.Throws<MicrophoneException>(mic.Start);
        Assert.Equal(MicTroubleKind.NoDevice, e.Trouble.Kind);
        Assert.Equal("No microphone found", e.Trouble.Title);
        Assert.Equal("Plug one in, or pick one in Settings → System → Sound → Input.", e.Trouble.Detail);
        mic.Stop(); // nothing open: nothing to do
    }

    [WindowsFact]
    public void A_microphone_that_never_opens_is_given_up_on_in_time()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var release = new ManualResetEventSlim();
        using var mic = new WindowsSound(withComputerAudio: false, () =>
        {
            release.Wait(TimeSpan.FromSeconds(10));
            return null;
        }) { OpenTimeout = TimeSpan.FromMilliseconds(300) };
        var watch = Stopwatch.StartNew();
        var e = Assert.Throws<MicrophoneException>(mic.Start);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(3), $"took {watch.Elapsed}");
        Assert.Equal(MicTroubleKind.Other, e.Trouble.Kind);
        release.Set();
    }

    /// <summary>The real default-microphone path, from the enumerator to WASAPI. On a PC with no microphone (GitHub's
    /// Windows runner) it must say "No microphone found", quickly. On a PC with one it isn't opened: a test never
    /// records anyone.</summary>
    [WindowsFact]
    public void The_real_microphone_path_on_a_pc_without_one_says_no_microphone()
    {
        if (!OperatingSystem.IsWindows()) return;
        var access = WindowsPermissions.Microphone();
        string seen = $"access={access}";
        try
        {
            using var probe = new WindowsSound(withComputerAudio: false) { OpenTimeout = TimeSpan.FromSeconds(8) };
            probe.Start();
            seen += " started";
        }
        catch (Exception x)
        {
            seen += $" {x.GetType().Name} {x.Message} {(x as MicrophoneException)?.Trouble.Kind} status={(x as MicrophoneException)?.Status:X}";
        }
        Assert.Fail("DIAGNOSTIC " + seen);
        if (access == MicAccess.Allowed) return; // there's a real microphone here: leave it alone
        using var mic = new WindowsSound(withComputerAudio: true);
        var watch = Stopwatch.StartNew();
        var e = Assert.Throws<MicrophoneException>(mic.Start);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), $"took {watch.Elapsed}");
        MicTroubleKind[] expected = access == MicAccess.Unknown ? [MicTroubleKind.NoDevice] : [MicTroubleKind.Denied, MicTroubleKind.NoDevice];
        Assert.Contains(e.Trouble.Kind, expected);
        Assert.False(string.IsNullOrWhiteSpace(e.Trouble.Detail));
    }
}
