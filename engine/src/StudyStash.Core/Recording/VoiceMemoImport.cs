using System.Globalization;
using System.Text.Json.Nodes;

namespace StudyStash.Core;

/// <summary>The library's side of a voice memo, as the importer needs it: what's waiting, taking one, fetching its
/// recording, and saying how it went. <see cref="RemoteLibrary"/> is the real one.</summary>
public interface IVoiceMemoLibrary
{
    Task<JsonArray?> VoiceMemosAsync(CancellationToken stop = default);
    Task<bool> ClaimVoiceMemoAsync(string id, string computer, CancellationToken stop = default);
    Task DownloadVoiceMemoAsync(string id, string path, CancellationToken stop = default);
    Task FinishVoiceMemoAsync(string id, string? lecture, string? error, CancellationToken stop = default);
}

/// <summary>
/// Voice memos sent from the phone, written down here: each waiting memo is taken (so no other computer does it too),
/// fetched, turned into the recordings' own sound (16 kHz mono WAV, <see cref="Convert"/>) and added to this
/// computer's lectures as recorded and waiting for Whisper, which writes it down and sends it on to the library the
/// way any recording goes. The library is told which lecture it became, or why it couldn't be one.
/// </summary>
public sealed class VoiceMemoImport(LectureStore store, Func<IVoiceMemoLibrary?> library, Action<string, string> convert,
    Func<string> owner, Func<string> computer, Action? transcribe = null, Action<string>? log = null, Func<DateTimeOffset>? clock = null)
{
    /// <summary>How often to look for memos while nothing's waiting.</summary>
    public static readonly TimeSpan Every = TimeSpan.FromMinutes(1);

    /// <summary>Turns a recording (any format the computer reads) into a 16 kHz mono 16-bit WAV at the second path.
    /// Throws with words to tell the student when it can't.</summary>
    public Action<string, string> Convert { get; } = convert;

    readonly Action<string> say = log ?? (_ => { });
    readonly Func<DateTimeOffset> now = clock ?? (() => DateTimeOffset.Now);

    /// <summary>A lecture this computer made from a memo: raised so the app can say so.</summary>
    public event Action<Lecture>? Imported;

    public async Task RunAsync(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                await StepAsync(stop);
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or LibraryRefusedException or InvalidOperationException or IOException)
            {
                if (stop.IsCancellationRequested) return;
                // The library is away (or older): look again later.
            }
            try
            {
                await Task.Delay(Every, stop);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>Take and bring in every memo waiting now. How many became lectures.</summary>
    public async Task<int> StepAsync(CancellationToken stop)
    {
        if (library() is not { } lib) return 0;
        var waiting = (await lib.VoiceMemosAsync(stop) ?? []).OfType<JsonObject>().Where(m => Py.AsString(m["state"]) == "waiting").ToList();
        int made = 0;
        foreach (var memo in waiting)
        {
            string id = Py.AsString(memo["id"]) ?? "";
            if (id.Length == 0 || !await lib.ClaimVoiceMemoAsync(id, computer(), stop)) continue;
            string? error;
            Lecture? lecture = null;
            try
            {
                lecture = await BringInAsync(lib, id, memo, stop);
                error = null;
            }
            catch (Exception e) when (e is InvalidOperationException or IOException or InvalidDataException or LibraryRefusedException or HttpRequestException or UnauthorizedAccessException)
            {
                error = e.Message;
                say($"[memo] {id}: couldn't bring it in: {e.Message}");
            }
            await lib.FinishVoiceMemoAsync(id, lecture?.Id, error, stop);
            if (lecture is null) continue;
            made++;
            say($"[memo] {id} is {lecture.Id} ({lecture.Seconds:0} s), waiting for Whisper");
            Imported?.Invoke(lecture);
            transcribe?.Invoke();
        }
        return made;
    }

    async Task<Lecture> BringInAsync(IVoiceMemoLibrary lib, string id, JsonObject memo, CancellationToken stop)
    {
        string name = Py.AsString(memo["name"]) ?? "memo.m4a";
        string download = Path.Combine(Path.GetTempPath(), $"studystash-memo-{id}{Path.GetExtension(name)}");
        var at = now();
        string lectureId = Lecture.NewId(at);
        string wav = store.AudioPath(lectureId);
        try
        {
            await lib.DownloadVoiceMemoAsync(id, download, stop);
            Directory.CreateDirectory(Path.GetDirectoryName(wav)!);
            Convert(download, wav);
            double seconds = Sound.WavSeconds(wav);
            if (seconds < 1) throw new InvalidDataException("The recording has no sound in it.");
            // When it was sent stands in for when it was recorded: Voice Memos doesn't say, and it's usually the same day.
            var sent = DateTimeOffset.TryParse(Py.AsString(memo["added"]), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var a) ? a.ToLocalTime() : at;
            var lecture = new Lecture
            {
                Id = lectureId, Started = sent.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture),
                ClassName = Py.AsString(memo["class"]) ?? "", Title = Py.AsString(memo["title"]) ?? "", Owner = owner(),
                Seconds = seconds, State = LectureState.Transcribing,
            };
            return store.Add(lecture);
        }
        catch
        {
            TryDelete(wav);
            throw;
        }
        finally
        {
            TryDelete(download);
        }
    }

    static void TryDelete(string path)
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
