using StudyStash.Core;

namespace StudyStash.Audio;

/// <summary>
/// What the computer plays, waiting to be added onto the microphone. The microphone keeps time: the computer's sound
/// arrives on its own clock (and not at all while it's silent, on Windows), so it's queued here and each microphone
/// buffer takes as much as there is. More than a second waiting is dropped from the front, so a lag never grows.
/// </summary>
internal sealed class PlayedSound
{
    const int MaxLag = Sound.Rate;
    readonly Lock gate = new();
    readonly Queue<float> played = new();

    /// <summary>16 kHz mono from the computer's own sound.</summary>
    public void Add(float[] s)
    {
        lock (gate)
        {
            foreach (float x in s) played.Enqueue(x);
            while (played.Count > MaxLag) played.Dequeue();
        }
    }

    /// <summary>Adds what's waiting onto a microphone buffer, in place.</summary>
    public void MixInto(float[] mic)
    {
        lock (gate)
        {
            for (int i = 0; i < mic.Length && played.Count > 0; i++) mic[i] = Math.Clamp(mic[i] + played.Dequeue(), -1f, 1f);
        }
    }

    public void Clear()
    {
        lock (gate) played.Clear();
    }
}
