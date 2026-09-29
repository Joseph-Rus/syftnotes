using System.Diagnostics;
using System.Runtime.Versioning;
using NAudio.Wave;

namespace StudyStash.Audio;

/// <summary>
/// A recording made somewhere else (a voice memo from the phone: .m4a, .mp3, .wav…) turned into what Whisper and the
/// recorder use: 16 kHz, mono, 16-bit WAV. A Mac has it built in (afconvert, the Core Audio converter); Windows reads
/// it with Media Foundation; anywhere else it needs ffmpeg.
/// </summary>
public static class AudioFiles
{
    public const string CantRead = "Study Stash couldn't read that recording. Voice Memos' .m4a, .mp3 and .wav all work.";

    /// <summary>Converts <paramref name="input"/> to a 16 kHz mono WAV at <paramref name="output"/>. Throws
    /// InvalidOperationException, with words for the student, when it can't.</summary>
    public static void ToRecording(string input, string output)
    {
        if (OperatingSystem.IsMacOS()) Run("/usr/bin/afconvert", ["-f", "WAVE", "-d", "LEI16@16000", "-c", "1", input, output]);
        else if (OperatingSystem.IsWindows()) WithMediaFoundation(input, output);
        else Run("ffmpeg", ["-nostdin", "-loglevel", "error", "-y", "-i", input, "-ac", "1", "-ar", "16000", "-sample_fmt", "s16", output]);
        if (!File.Exists(output) || new FileInfo(output).Length <= 44) throw new InvalidOperationException(CantRead);
    }

    static void Run(string exe, string[] args)
    {
        var psi = new ProcessStartInfo(exe) { RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (string a in args) psi.ArgumentList.Add(a);
        Process? p;
        try
        {
            p = Process.Start(psi);
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            throw new InvalidOperationException(OperatingSystem.IsLinux() ? "Reading a voice memo here needs ffmpeg. Install it, then try again." : CantRead, e);
        }
        using (p)
        {
            if (p is null) throw new InvalidOperationException(CantRead);
            var err = p.StandardError.ReadToEndAsync();
            p.StandardOutput.ReadToEnd();
            // A recording hours long converts in seconds; ten minutes means it's stuck.
            if (!p.WaitForExit(TimeSpan.FromMinutes(10)))
            {
                p.Kill(entireProcessTree: true);
                throw new InvalidOperationException(CantRead);
            }
            if (p.ExitCode != 0) throw new InvalidOperationException($"{CantRead} ({err.Result.Trim().Split('\n')[0]})");
        }
    }

    [SupportedOSPlatform("windows")]
    static void WithMediaFoundation(string input, string output)
    {
        try
        {
            using var reader = new MediaFoundationReader(input);
            using var resampler = new MediaFoundationResampler(reader, new WaveFormat(16000, 16, 1)) { ResamplerQuality = 60 };
            WaveFileWriter.CreateWaveFile(output, resampler);
        }
        catch (Exception e) when (e is System.Runtime.InteropServices.COMException or InvalidOperationException or ArgumentException or IOException)
        {
            throw new InvalidOperationException(CantRead, e);
        }
    }
}
