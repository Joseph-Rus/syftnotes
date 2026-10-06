namespace StudyStash.Core.Canvas;

/// <summary>
/// Canvas reads an AI asked for (the course scout, or a chat), carried out by the Chrome extension. The MCP tool asks
/// the library; the library queues the read here and waits while the extension, told to check often whenever
/// something is queued, fetches it with the person's own Canvas session.
/// </summary>
public sealed class AgentQueue
{
    /// <summary>After an AI's last read, the extension keeps checking often for this long: it's likely to ask again.</summary>
    public static readonly TimeSpan HotFor = TimeSpan.FromMinutes(2);

    /// <summary>What a read gets back when the browser with the extension didn't answer in time.</summary>
    public const string NoAnswer = "Your browser didn't answer. Is it open, with the Study Stash extension on?";

    readonly Lock gate = new();
    readonly List<CanvasJob> jobs = [];
    /// <summary>Handed to the extension and not answered yet, in the order they went out.</summary>
    readonly List<CanvasJob> taken = [];
    readonly Dictionary<string, TaskCompletionSource<CanvasResult>> waiting = [];
    DateTime last = DateTime.MinValue;
    int seq;

    /// <summary>Raised whenever a read is queued: the library's held request for work answers at once.</summary>
    public Wake Queued { get; } = new();

    /// <summary>Something is queued or was lately: the extension should check every second or two.</summary>
    public bool Hot
    {
        get
        {
            lock (gate) return jobs.Count > 0 || waiting.Count > 0 || DateTime.UtcNow - last < HotFor;
        }
    }

    /// <summary>Queue one read and wait for the extension's answer.</summary>
    public async Task<CanvasResult> FetchAsync(string url, string kind, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        string id;
        var done = new TaskCompletionSource<CanvasResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (gate)
        {
            id = $"a{++seq}";
            waiting[id] = done;
            jobs.Add(new CanvasJob(id, url, kind));
            last = DateTime.UtcNow;
        }
        Queued.Raise();
        try
        {
            return await done.Task.WaitAsync(timeout ?? TimeSpan.FromMinutes(2), ct);
        }
        catch (TimeoutException)
        {
            return new CanvasResult(id, 0, "", "", "", NoAnswer, "");
        }
        finally
        {
            lock (gate)
            {
                waiting.Remove(id);
                jobs.RemoveAll(j => j.Id == id);
                taken.RemoveAll(j => j.Id == id);
            }
        }
    }

    /// <summary>Up to <paramref name="n"/> queued reads for the extension, oldest first.</summary>
    public List<CanvasJob> Take(int n = 6)
    {
        lock (gate)
        {
            var out_ = jobs.Take(n).ToList();
            jobs.RemoveRange(0, out_.Count);
            taken.AddRange(out_);
            return out_;
        }
    }

    /// <summary>The extension started afresh (reloaded into a new version, or asked to sync): reads its old copy took
    /// and never answered go out again, first, while the AI is still waiting for them.</summary>
    public void Requeue()
    {
        lock (gate)
        {
            if (taken.Count == 0) return;
            jobs.InsertRange(0, taken);
            taken.Clear();
        }
        Queued.Raise();
    }

    /// <summary>Hand over an answer. False when it isn't one of these reads (then it's the sync's).</summary>
    public bool Answer(CanvasResult r)
    {
        if (!r.Id.StartsWith('a')) return false;
        lock (gate)
        {
            taken.RemoveAll(j => j.Id == r.Id);
            if (waiting.TryGetValue(r.Id, out var w)) w.TrySetResult(r);
        }
        return true;
    }
}
