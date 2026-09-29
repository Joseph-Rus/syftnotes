using System.Diagnostics;
using StudyStash.Audio;

namespace StudyStash.Core.Tests;

/// <summary>
/// Recording what the computer plays alongside the microphone: the two are added with the microphone keeping time
/// (tested everywhere), and on a Mac the real tap hears what the speakers play (a live test, since it plays sound out
/// loud: <c>STUDYSTASH_LIVE_COMPUTER_AUDIO=1</c>).
/// </summary>
public class ComputerAudioTests
{
    [Fact]
    public void The_computer_sound_is_added_onto_the_microphone()
    {
        var played = new PlayedSound();
        played.Add([0.25f, 0.5f]);
        float[] mic = [0.1f, 0.1f, 0.1f];
        played.MixInto(mic);
        Assert.Equal([0.35f, 0.6f, 0.1f], mic);
    }

    [Fact]
    public void Loud_sound_together_never_goes_past_full_scale()
    {
        var played = new PlayedSound();
        played.Add([0.9f, -0.9f]);
        float[] mic = [0.5f, -0.5f];
        played.MixInto(mic);
        Assert.Equal([1f, -1f], mic);
    }

    [Fact]
    public void More_than_a_second_waiting_drops_the_oldest()
    {
        var played = new PlayedSound();
        played.Add(Enumerable.Repeat(0.5f, Sound.Rate).ToArray());
        played.Add([0.25f]);
        var mic = new float[Sound.Rate + 1];
        played.MixInto(mic);
        Assert.Equal(0.25f, mic[Sound.Rate - 1]);
        Assert.Equal(0f, mic[Sound.Rate]);
    }

    [Fact]
    public void Nothing_waits_after_clearing()
    {
        var played = new PlayedSound();
        played.Add([0.5f]);
        played.Clear();
        float[] mic = [0.1f];
        played.MixInto(mic);
        Assert.Equal([0.1f], mic);
    }

    /// <summary>The real tap on a Mac: <c>say</c> speaks out loud, and the tap must hear it as 16 kHz sound.</summary>
    [Fact]
    public void A_mac_hears_what_it_plays()
    {
        if (Environment.GetEnvironmentVariable("STUDYSTASH_LIVE_COMPUTER_AUDIO") != "1" || !OperatingSystem.IsMacOSVersionAtLeast(14, 2)) return;
        var heard = new List<float>();
        using var computer = new MacComputerAudio();
        computer.Samples += s =>
        {
            lock (heard) heard.AddRange(s);
        };
        computer.Start();
        using (var say = Process.Start("say", "The mitochondria is the powerhouse of the cell.")!) say.WaitForExit();
        Thread.Sleep(300);
        computer.Dispose();
        lock (heard)
        {
            Assert.True(heard.Count > Sound.Rate, $"heard {heard.Count} samples");
            Assert.True(heard.Max(Math.Abs) > 0.05f, "heard only silence (is 'System Audio Recording' allowed for this terminal?)");
        }
    }
}
