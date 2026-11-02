namespace StudyStash.Core.Study;

/// <summary>The voices Listen can download, as the student is told of them: what they are, how big, under what licence,
/// and whether they're here, coming (how much so far) or couldn't be fetched (why).</summary>
public sealed record VoicesInfo(string Name, string State)
{
    public const string None = "none", Downloading = "downloading", Ready = "ready", Failed = "failed", Unavailable = "unavailable";
    public string License { get; init; } = "";
    public string About { get; init; } = "";
    /// <summary>All of it, in bytes.</summary>
    public long Bytes { get; init; }
    /// <summary>How much is here (all of it when ready, what's come so far while downloading).</summary>
    public long Done { get; init; }
    public string Error { get; init; } = "";
    /// <summary>A listen made now is read with them (they're all here).</summary>
    public bool InUse { get; init; }
}

/// <summary>Voices made by an open-source model, downloaded once into the library's home, that read a listen better
/// than the computer's own: their files, fetching them, and speaking with them once they're here.</summary>
public interface IVoicePack
{
    string Name { get; }
    string License { get; }
    /// <summary>A line on what they are, for Settings.</summary>
    string About { get; }
    long Bytes { get; }
    /// <summary>Every file is here, whole.</summary>
    bool IsHere { get; }
    /// <summary>How much is here, a stopped download's part files counted.</summary>
    long OnDisk { get; }
    /// <summary>Speaking with them (only once <see cref="IsHere"/>).</summary>
    ISpeech Speech { get; }
    /// <summary>Fetches what isn't here yet, saying how many bytes are here as it goes; picks up where a stopped one
    /// left off. Throws IOException, HttpRequestException or InvalidDataException when it can't.</summary>
    Task DownloadAsync(IProgress<long>? progress, CancellationToken ct);
    /// <summary>Removes them and whatever came of a download.</summary>
    void Delete();
}

/// <summary>
/// The voices a listen is read with: the downloaded ones (<see cref="IVoicePack"/>, Kokoro) once they're all here, else
/// the computer's own (say, Windows' speech, espeak). Also fetches the downloaded ones in the background, one download
/// at a time, and says how far it's got.
/// </summary>
public sealed class ListenVoices(IVoicePack? pack, ISpeech computer) : ISpeech
{
    readonly object gate = new();
    Task? downloading;
    long done;
    string error = "";

    public IVoicePack? Pack => pack;

    /// <summary>What reads a listen started now. A listen keeps to the one it started with, so a download finishing
    /// halfway through doesn't change voices in the middle of it.</summary>
    public ISpeech Now => pack is { IsHere: true } ? pack.Speech : computer;

    public (bool Ok, string Why) Ready() => Now.Ready();

    public Task SayAsync(string text, string who, string wav, CancellationToken ct) => Now.SayAsync(text, who, wav, ct);

    public VoicesInfo Info()
    {
        if (pack is null) return new VoicesInfo("", VoicesInfo.Unavailable);
        var info = new VoicesInfo(pack.Name, VoicesInfo.None) { License = pack.License, About = pack.About, Bytes = pack.Bytes };
        lock (gate)
        {
            if (downloading is { IsCompleted: false }) return info with { State = VoicesInfo.Downloading, Done = done };
            if (pack.IsHere) return info with { State = VoicesInfo.Ready, Done = pack.Bytes, InUse = true };
            return info with { State = error.Length > 0 ? VoicesInfo.Failed : VoicesInfo.None, Done = pack.OnDisk, Error = error };
        }
    }

    /// <summary>Starts fetching them in the background (nothing new while they're coming or here already).</summary>
    public VoicesInfo Download(Action<string>? log = null)
    {
        if (pack is null) return Info();
        lock (gate)
        {
            if (downloading is { IsCompleted: false } || pack.IsHere) return Info();
            error = "";
            done = pack.OnDisk;
            downloading = Task.Run(async () =>
            {
                try
                {
                    await pack.DownloadAsync(new Progress(this), CancellationToken.None);
                    log?.Invoke($"[listen] {pack.Name} voices downloaded");
                }
                catch (Exception e) when (e is IOException or HttpRequestException or InvalidDataException or UnauthorizedAccessException or TaskCanceledException)
                {
                    log?.Invoke($"[listen] {pack.Name} voices didn't download: {e.Message}");
                    lock (gate) error = e.Message;
                }
            });
        }
        return Info();
    }

    /// <summary>Waits for a download, if one is going (tests).</summary>
    public Task Finished()
    {
        lock (gate) return downloading ?? Task.CompletedTask;
    }

    /// <summary>Removes them, so listens are read by the computer's own voices again. False while they're coming.</summary>
    public bool Delete()
    {
        if (pack is null) return false;
        lock (gate)
        {
            if (downloading is { IsCompleted: false }) return false;
            pack.Delete();
            error = "";
            return true;
        }
    }

    /// <summary>Reported on the downloading thread, straight into the count (Progress&lt;T&gt; would post it elsewhere).</summary>
    sealed class Progress(ListenVoices voices) : IProgress<long>
    {
        public void Report(long value)
        {
            lock (voices.gate) voices.done = value;
        }
    }
}
