using System.Buffers.Binary;
using StudyStash.Core;

namespace StudyStash.Core.Tests;

/// <summary>A recording on this computer becomes a lecture waiting for Whisper, with the class, title and day the
/// student chose, and the file itself is left as it was.</summary>
public class LectureFromFileTests
{
    /// <summary>A 16 kHz mono WAV of silence this many seconds long.</summary>
    static void Wav(string path, double seconds)
    {
        int bytes = (int)(seconds * 16000) * 2;
        var data = new byte[44 + bytes];
        "RIFF"u8.CopyTo(data);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4), 36 + bytes);
        "WAVEfmt "u8.CopyTo(data.AsSpan(8));
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(16), 16);
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(20), 1);
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(22), 1);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(24), 16000);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(28), 32000);
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(32), 2);
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(34), 16);
        "data"u8.CopyTo(data.AsSpan(36));
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(40), bytes);
        File.WriteAllBytes(path, data);
    }

    [Fact]
    public void A_file_on_this_computer_becomes_a_lecture_waiting_for_whisper_and_the_file_is_left_alone()
    {
        using var dir = new TempDir();
        var store = new LectureStore(dir["home"]);
        string source = dir["Week 3 lecture.WAV"];
        Wav(source, 2);
        var changed = new DateTime(2026, 9, 15, 14, 30, 5, DateTimeKind.Local);
        File.SetLastWriteTime(source, changed);
        byte[] before = File.ReadAllBytes(source);
        void Convert(string from, string to) => File.Copy(from, to);

        // What the student chose: the class, the title (theirs, so the library keeps it) and the day, at the file's time of day.
        var chosen = LectureFromFile.FromFile(store, Convert, source, "CS 101", "Recursion, part 2", new DateOnly(2026, 9, 20), "Sam");
        Assert.Equal((LectureState.Transcribing, "CS 101", "Recursion, part 2", true, "Sam"), (chosen.State, chosen.ClassName, chosen.Title, chosen.TitleTyped, chosen.Owner));
        Assert.Equal(new DateTimeOffset(new DateTime(2026, 9, 20, 14, 30, 5)), chosen.StartedAt);
        Assert.Equal(2, chosen.Seconds, 1);
        Assert.True(File.Exists(store.AudioPath(chosen.Id)));

        // Nothing chosen: no class (the library sorts it), the file's name for a title, the day it was last changed.
        var plain = LectureFromFile.FromFile(store, Convert, source, null, null, null, "Sam");
        Assert.Equal(("", "Week 3 lecture", false), (plain.ClassName, plain.Title, plain.TitleTyped));
        Assert.Equal(new DateTimeOffset(changed), plain.StartedAt);
        Assert.Equal(2, store.All().Count);

        // The file is as it was, and one this can't read is refused in words, leaving nothing behind.
        Assert.Equal(before, File.ReadAllBytes(source));
        Assert.Equal(changed, File.GetLastWriteTime(source));
        string notes = dir["notes.txt"];
        File.WriteAllText(notes, "not a recording");
        Assert.StartsWith("That isn't a recording or video", Assert.Throws<InvalidOperationException>(() => LectureFromFile.FromFile(store, Convert, notes, null, null, null, "Sam")).Message);
        Assert.Equal(2, store.All().Count);
        Assert.Equal(2, Directory.GetFiles(store.Dir, "*.wav").Length);
    }
}
