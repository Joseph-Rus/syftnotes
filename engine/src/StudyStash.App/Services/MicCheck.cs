using System.Runtime.InteropServices;
using StudyStash.Audio;
using StudyStash.Core;

namespace StudyStash.App.Services;

/// <summary>
/// Setup's microphone step: opens a microphone (through whatever <see cref="Open"/> is given — never the real one in
/// a test) and counts its loudness in 50 ms blocks, so the step's waveform can show the student Study Stash hearing
/// them. Opening happens off the caller's thread (a sound driver can take its time), and a microphone that won't
/// open leaves its <see cref="Trouble"/> for the step to show and is tried again a few seconds later (one plugged in
/// meanwhile just works). Pure and thread-safe: nothing here touches a view model, so a caller (Setup, Shell) copies
/// <see cref="Heard"/>, <see cref="Levels"/> and <see cref="Trouble"/> across on its own schedule.
/// </summary>
public sealed class MicCheck(Func<DateTime>? clock = null) : IDisposable
{
    public const int Bars = 24;
    const int BlockSamples = Sound.Rate / 20; // 50 ms
    public static readonly TimeSpan TryAgainAfter = TimeSpan.FromSeconds(3);

    readonly Func<DateTime> clock = clock ?? (() => DateTime.UtcNow);
    readonly Lock gate = new();
    readonly double[] levels = new double[Bars];
    readonly List<float> buf = [];
    int at;
    volatile bool heard;
    IAudioSource? mic;
    Task? opening;
    int turn; // Close moves it on, so a microphone that finishes opening after that is closed straight away
    MicTrouble? trouble;
    DateTime troubleAt;

    /// <summary>A level has passed the "Study Stash hears you" mark since the microphone opened.</summary>
    public bool Heard => heard;

    /// <summary>Why the microphone didn't open last time, in the student's words; null once it's open (or not tried).</summary>
    public MicTrouble? Trouble
    {
        get
        {
            lock (gate) return trouble;
        }
    }

    /// <summary>The microphone is open and counting.</summary>
    public bool IsOpen
    {
        get
        {
            lock (gate) return mic is not null;
        }
    }

    /// <summary>The open still under way, if one is (a test waits for it).</summary>
    internal Task Opened
    {
        get
        {
            lock (gate) return opening ?? Task.CompletedTask;
        }
    }

    /// <summary>Starts opening the microphone and counting; does nothing if one is open or opening, or if the last
    /// try failed less than <see cref="TryAgainAfter"/> ago.</summary>
    public void Open(Func<IAudioSource> open)
    {
        lock (gate)
        {
            if (mic is not null || opening is not null) return;
            if (trouble is not null && clock() - troubleAt < TryAgainAfter) return;
            Array.Clear(levels);
            buf.Clear();
            at = 0;
            heard = false;
            int mine = turn;
            opening = Task.Run(() => OpenNow(open, mine));
        }
    }

    void OpenNow(Func<IAudioSource> open, int mine)
    {
        IAudioSource? m = null;
        MicTrouble? t = null;
        try
        {
            m = open();
            m.Samples += OnSamples;
            m.Start();
        }
        catch (MicrophoneException e)
        {
            t = e.Trouble;
        }
        catch (Exception e) when (e is InvalidOperationException or IOException or PlatformNotSupportedException or ExternalException or UnauthorizedAccessException)
        {
            t = MicTrouble.Other(OperatingSystem.IsWindows());
        }
        bool keep;
        lock (gate)
        {
            opening = null;
            keep = t is null && mine == turn;
            if (keep) mic = m;
            trouble = t;
            if (t is not null) troubleAt = clock();
        }
        if (!keep && m is not null) Shut(m);
    }

    void OnSamples(float[] samples)
    {
        lock (gate)
        {
            foreach (float s in samples)
            {
                buf.Add(s);
                if (buf.Count < BlockSamples) continue;
                double level = Sound.Level(Sound.Db(CollectionsMarshal.AsSpan(buf)));
                levels[at] = level;
                at = (at + 1) % Bars;
                buf.Clear();
                if (level > 0.25) heard = true;
            }
        }
    }

    /// <summary>The last bars, oldest first, 0 to 1: for the waveform.</summary>
    public double[] Levels()
    {
        lock (gate)
        {
            var result = new double[Bars];
            for (int i = 0; i < Bars; i++) result[i] = levels[(at + i) % Bars];
            return result;
        }
    }

    /// <summary>Closes the microphone, if one is open, and forgets any trouble (coming back to the step tries
    /// afresh). Safe to call more than once.</summary>
    public void Close()
    {
        IAudioSource? m;
        lock (gate)
        {
            turn++;
            m = mic;
            mic = null;
            trouble = null;
        }
        if (m is not null) Shut(m);
    }

    void Shut(IAudioSource m)
    {
        m.Samples -= OnSamples;
        try
        {
            m.Stop();
        }
        catch (Exception e) when (e is InvalidOperationException or IOException or ExternalException)
        {
        }
        m.Dispose();
    }

    public void Dispose() => Close();
}
