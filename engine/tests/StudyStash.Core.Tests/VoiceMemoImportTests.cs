using System.Buffers.Binary;
using System.Text.Json.Nodes;
using StudyStash.Core;

namespace StudyStash.Core.Tests;

/// <summary>Bringing in voice memos from the phone: each waiting one is taken, fetched, turned into the recordings'
/// own sound and added as a lecture waiting for Whisper, and the library is told which lecture it became.</summary>
public class VoiceMemoImportTests
{
    sealed class FakeLibrary : IVoiceMemoLibrary
    {
        public List<JsonObject> Memos { get; } = [];
        public HashSet<string> TakenElsewhere { get; } = [];
        public List<(string Id, string? Lecture, string? Error)> Finished { get; } = [];
        public List<string> Claimed { get; } = [];

        public Task<JsonArray?> VoiceMemosAsync(CancellationToken stop = default) =>
            Task.FromResult<JsonArray?>(new JsonArray([.. Memos.Select(m => (JsonNode)m.DeepClone())]));

        public Task<bool> ClaimVoiceMemoAsync(string id, string computer, CancellationToken stop = default)
        {
            if (TakenElsewhere.Contains(id)) return Task.FromResult(false);
            Claimed.Add($"{id} by {computer}");
            return Task.FromResult(true);
        }

        public Task DownloadVoiceMemoAsync(string id, string path, CancellationToken stop = default)
        {
            File.WriteAllText(path, "pretend m4a: " + id);
            return Task.CompletedTask;
        }

        public Task FinishVoiceMemoAsync(string id, string? lecture, string? error, CancellationToken stop = default)
        {
            Finished.Add((id, lecture, error));
            return Task.CompletedTask;
        }
    }

    static JsonObject Memo(string id, string state = "waiting", string? cls = "CS 101", string title = "Recursion, part 2") => new()
    {
        ["id"] = id, ["name"] = "Lecture.m4a", ["class"] = cls, ["title"] = title, ["state"] = state, ["added"] = "2026-09-29T16:05:00Z",
    };

    /// <summary>A 16 kHz mono WAV of silence this many seconds long, as afconvert would write it.</summary>
    static void Wav(string path, double seconds)
    {
        int samples = (int)(seconds * 16000), bytes = samples * 2;
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
    public async Task A_waiting_memo_becomes_a_lecture_waiting_for_whisper_and_the_library_hears_which()
    {
        using var dir = new TempDir();
        var store = new LectureStore(dir.Path);
        var lib = new FakeLibrary();
        lib.Memos.Add(Memo("m1"));
        lib.Memos.Add(Memo("m2", state: "done"));
        int woke = 0;
        var converted = new List<string>();
        var import = new VoiceMemoImport(store, () => lib, (from, to) =>
        {
            converted.Add(File.ReadAllText(from));
            Wav(to, 90);
        }, () => "Sam", () => "Sam's MacBook", () => woke++);

        Assert.Equal(1, await import.StepAsync(CancellationToken.None));

        var lecture = Assert.Single(store.All());
        Assert.Equal((LectureState.Transcribing, "CS 101", "Recursion, part 2", "Sam"), (lecture.State, lecture.ClassName, lecture.Title, lecture.Owner));
        Assert.Equal(90, lecture.Seconds, 1);
        Assert.Equal(new DateTimeOffset(2026, 9, 29, 16, 5, 0, TimeSpan.Zero), lecture.StartedAt);
        Assert.True(File.Exists(store.AudioPath(lecture.Id)));
        Assert.Equal(["pretend m4a: m1"], converted);
        Assert.Equal(["m1 by Sam's MacBook"], lib.Claimed);
        Assert.Equal([("m1", lecture.Id, (string?)null)], lib.Finished);
        Assert.Equal(1, woke);
    }

    [Fact]
    public async Task One_another_computer_took_is_left_to_it()
    {
        using var dir = new TempDir();
        var store = new LectureStore(dir.Path);
        var lib = new FakeLibrary();
        lib.Memos.Add(Memo("m1"));
        lib.TakenElsewhere.Add("m1");
        var import = new VoiceMemoImport(store, () => lib, (_, to) => Wav(to, 5), () => "Sam", () => "Mac");

        Assert.Equal(0, await import.StepAsync(CancellationToken.None));
        Assert.Empty(store.All());
        Assert.Empty(lib.Finished);
    }

    [Fact]
    public async Task A_recording_that_cant_be_read_is_said_to_have_failed_and_leaves_nothing_behind()
    {
        using var dir = new TempDir();
        var store = new LectureStore(dir.Path);
        var lib = new FakeLibrary();
        lib.Memos.Add(Memo("bad"));
        lib.Memos.Add(Memo("silent", cls: null));
        var import = new VoiceMemoImport(store, () => lib, (from, to) =>
        {
            if (File.ReadAllText(from).EndsWith("bad", StringComparison.Ordinal)) throw new InvalidOperationException("Study Stash couldn't read that recording.");
            Wav(to, 0.2);
        }, () => "Sam", () => "Mac");

        Assert.Equal(0, await import.StepAsync(CancellationToken.None));

        Assert.Empty(store.All());
        Assert.Empty(Directory.GetFiles(store.Dir, "*.wav"));
        Assert.Equal([("bad", (string?)null, "Study Stash couldn't read that recording."), ("silent", null, "The recording has no sound in it.")], lib.Finished);
    }

    [Fact]
    public async Task With_no_library_set_up_there_is_nothing_to_do()
    {
        using var dir = new TempDir();
        var import = new VoiceMemoImport(new LectureStore(dir.Path), () => null, (_, _) => throw new InvalidOperationException("never"), () => "", () => "");
        Assert.Equal(0, await import.StepAsync(CancellationToken.None));
    }
}
