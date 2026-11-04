using System.Globalization;

namespace StudyStash.Core;

/// <summary>
/// A recording made somewhere else, turned into a lecture waiting for Whisper: its sound becomes the recorder's own
/// (16 kHz mono WAV, by the <c>convert</c> the app gives, which for a video takes the sound alone), and the lecture is
/// added as recorded and waiting for Whisper, which writes it down and sends it on to the library the way any
/// recording goes. A voice memo from the phone (<see cref="VoiceMemoImport"/>) and a file the student picks on this
/// computer take the same steps. The file itself is only read: never moved, changed or deleted.
/// </summary>
public static class LectureFromFile
{
    /// <summary>What the phone sends and Voice Memos saves (.m4a), and the usual other sound files. (.mp4 is here
    /// because a phone can name a recording so; a video of that ending is read for its sound as any other.)</summary>
    public static readonly string[] SoundEndings = [".m4a", ".mp3", ".wav", ".aac", ".caf", ".mp4", ".aif", ".aiff", ".qta"];

    /// <summary>What a file on this computer can be, by its name's ending: sound, or a video whose sound is taken.
    /// A Mac (afconvert) and Windows (Media Foundation's MPEG-4 source) both read .mov and .m4v; only Windows
    /// reads .wmv.</summary>
    public static readonly string[] Endings = OperatingSystem.IsWindows()
        ? [.. SoundEndings, ".mov", ".m4v", ".wmv"]
        : [.. SoundEndings, ".mov", ".m4v"];

    public static bool CanRead(string path) => Endings.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Makes a lecture from a recording or video on this computer, waiting for Whisper. <paramref name="className"/>
    /// empty lets the library sort it. <paramref name="title"/> null is the file's name without its ending, which the
    /// library names the lecture over from what was said, as it does any lecture; one given is the student's own, and
    /// the library keeps it. <paramref name="day"/> null is the day the file was last changed; either way the time of
    /// day is the file's, so lectures of one day sort in the order they were made. Converting an hours-long video takes a
    /// while: call this off the screen's thread.
    /// Throws InvalidOperationException or InvalidDataException, with words for the student, when it can't. Cancelling
    /// doesn't stop a conversion that's running; it only means the lecture isn't added (and the sound made for it is
    /// removed), with an OperationCanceledException.
    /// </summary>
    public static Lecture FromFile(LectureStore store, Action<string, string> convert, string path, string? className, string? title, DateOnly? day, string owner, CancellationToken cancel = default)
    {
        cancel.ThrowIfCancellationRequested();
        if (!File.Exists(path)) throw new InvalidOperationException("Syft Notes can't find that file any more.");
        if (!CanRead(path)) throw new InvalidOperationException("That isn't a recording or video Syft Notes can read. Sound files (.m4a, .mp3, .wav) and videos (.mp4, .mov) work.");
        var changed = File.GetLastWriteTime(path);
        var started = new DateTimeOffset(day is { } d ? d.ToDateTime(TimeOnly.FromDateTime(changed)) : changed);
        string typed = (title ?? "").Trim();
        return Add(store, convert, path, (className ?? "").Trim(), typed.Length > 0 ? typed : Path.GetFileNameWithoutExtension(path), started, owner, titleTyped: typed.Length > 0, cancel);
    }

    /// <summary>The step every way in shares: the file's sound as the recorder's own, then the lecture in the store.
    /// Nothing is left behind when it fails.</summary>
    internal static Lecture Add(LectureStore store, Action<string, string> convert, string path, string className, string title, DateTimeOffset started, string owner, bool titleTyped = false, CancellationToken cancel = default)
    {
        string id = Lecture.NewId(started);
        string wav = store.AudioPath(id);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(wav)!);
            convert(path, wav);
            double seconds = Sound.WavSeconds(wav);
            if (seconds < 1) throw new InvalidDataException("The recording has no sound in it.");
            cancel.ThrowIfCancellationRequested();
            return store.Add(new Lecture
            {
                Id = id, Started = started.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture),
                ClassName = className, Title = title, TitleTyped = titleTyped, Owner = owner,
                Seconds = seconds, State = LectureState.Transcribing,
            });
        }
        catch
        {
            TryDelete(wav);
            throw;
        }
    }

    internal static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }
}
