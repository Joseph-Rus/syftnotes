using StudyStash.Core.Study;

namespace StudyStash.Core.Tests;

/// <summary>Voices to download that come as fast as they're let: a first chunk, then (once <see cref="Let"/> is
/// set, or straight away) the rest, or a failure.</summary>
public sealed class FakeVoicePack(ISpeech speech) : IVoicePack
{
    public bool Here { get; set; }
    public Exception? Fail { get; set; }
    public TaskCompletionSource? Let { get; set; }
    public int Downloads { get; private set; }
    public string Name => "Kokoro";
    public string License => "Apache-2.0";
    public string About => "Natural voices for Listen.";
    public long Bytes => 1_000_000;
    public bool IsHere => Here;
    public long OnDisk => Here ? Bytes : 0;
    public ISpeech Speech => speech;

    public async Task DownloadAsync(IProgress<long>? progress, CancellationToken ct)
    {
        Downloads++;
        progress?.Report(400_000);
        if (Let is { } let) await let.Task;
        if (Fail is { } e) throw e;
        progress?.Report(Bytes);
        Here = true;
    }

    public void Delete() => Here = false;
}
