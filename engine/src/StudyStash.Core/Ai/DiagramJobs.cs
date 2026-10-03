using System.Text.Json;
using System.Text.Json.Serialization;

namespace StudyStash.Core.Ai;

/// <summary>
/// The diagrams that follow a lecture's notes. The notes are filed the moment they're written, readable everywhere; a
/// job here then has the designer (<see cref="AiJobs.DesignDiagramsAsync"/>) add their diagrams, and puts them into the
/// notes once they're ready (<see cref="Store.AddDiagrams"/>), safely: notes the student changed meanwhile keep every
/// change, and each diagram goes under its heading only while that heading is still there.
/// <para>One lecture at a time, oldest first, and none starts while notes are being written; a pass on this computer's
/// own model (Ollama) steps aside for notes that come in while it runs, and starts again after. Each job is a file in
/// <c>home/diagram-jobs</c>, so a restart picks it back up: a pass cut off part-way is tried again, at most
/// <see cref="MaxTries"/> times all told, and diagrams already designed are just put in. Notes filed again (rewritten,
/// written again) replace the job with one for the new notes. The log says what each did.</para>
/// </summary>
public sealed class DiagramJobs
{
    /// <summary>How many times a lecture's diagrams are tried (a pass cut off by a restart counts) before they're given
    /// up on, the notes staying as filed.</summary>
    public const int MaxTries = 3;

    readonly Config cfg;
    readonly Store store;
    readonly AiJobs ai;
    readonly Action<string> log;
    readonly Lock gate = new();
    readonly Dictionary<string, Job> jobs;
    readonly SemaphoreSlim wake = new(0, 1);
    (string Id, CancellationTokenSource Cts)? running;

    public DiagramJobs(Config cfg, Store store, AiJobs ai, Action<string>? log = null)
    {
        this.cfg = cfg;
        this.store = store;
        this.ai = ai;
        this.log = log ?? Console.WriteLine;
        jobs = LoadAll(cfg.Home);
    }

    /// <summary>Notes are being written (or wait to be, with their engine answering): no pass starts meanwhile, and one
    /// on this computer's own model steps aside.</summary>
    public Func<bool> NotesBusy { get; init; } = () => false;

    /// <summary>How long the queue rests before looking again when nothing woke it.</summary>
    public TimeSpan Pause { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>How often a pass on this computer's own model looks whether notes need it.</summary>
    public TimeSpan YieldCheck { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>The most a pass may take, whatever the designer does: past the diagrams' and an illustration's own time,
    /// it's stopped, and the notes stay as filed.</summary>
    public TimeSpan HardStop { get; init; } = IllustrationDesign.Timeout + TimeSpan.FromMinutes(4);

    /// <summary>How long diagrams wait to go in again when the note file couldn't be written (another app had it).</summary>
    public TimeSpan RetryWait { get; init; } = TimeSpan.FromMinutes(1);

    sealed class Job
    {
        public string Id { get; set; } = "";
        /// <summary>The engine that wrote the notes: who designs their diagrams is picked by it.</summary>
        public string NotesEngine { get; set; } = "";
        public string Queued { get; set; } = "";
        /// <summary>queued | designing | designed</summary>
        public string State { get; set; } = "queued";
        public int Tries { get; set; }
        /// <summary>Tries at putting designed diagrams in (a note file another app held locked).</summary>
        public int Writes { get; set; }
        /// <summary>The notes the designer read, as <see cref="Notes.Fingerprint"/> keeps them.</summary>
        public string Seen { get; set; } = "";
        public List<DesignedDiagram> Diagrams { get; set; } = [];
        /// <summary>Not before this (ISO 8601), after a note file that couldn't be written.</summary>
        public string NotBefore { get; set; } = "";
    }

    static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, Converters = { new JsonStringEnumConverter() },
    };

    static string Dir(string home) => Path.Combine(home, "diagram-jobs");

    static string FileFor(string home, string id) =>
        Path.Combine(Dir(home), Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(id))) + ".json");

    void Persist(Job job)
    {
        Directory.CreateDirectory(Dir(cfg.Home));
        string path = FileFor(cfg.Home, job.Id), temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(job, Json));
        File.Move(temp, path, overwrite: true);
    }

    void Forget(string id)
    {
        string path = FileFor(cfg.Home, id);
        if (File.Exists(path)) File.Delete(path);
    }

    /// <summary>Every job the last run left. One that was being designed when it stopped goes back in the queue: its
    /// try already counts.</summary>
    static Dictionary<string, Job> LoadAll(string home)
    {
        var result = new Dictionary<string, Job>();
        if (!Directory.Exists(Dir(home))) return result;
        foreach (string path in Directory.EnumerateFiles(Dir(home), "*.json"))
        {
            try
            {
                if (JsonSerializer.Deserialize<Job>(File.ReadAllText(path), Json) is not { Id.Length: > 0 } job) continue;
                if (job.State == "designing") job.State = "queued";
                job.NotBefore = ""; // a restart tries again at once
                result[job.Id] = job;
            }
            catch (Exception e) when (e is JsonException or IOException)
            {
                // A broken file: that lecture's notes simply stay as filed.
            }
        }
        return result;
    }

    string Title(string id) => store.Get(id) is { } r ? (r.LectureTitle is { Length: > 0 } t ? t : r.Title ?? id) : id;

    // --- what the library does ---------------------------------------------------------------------------------------

    /// <summary>A lecture's notes were just filed: their diagrams follow when <paramref name="notesEngine"/> (the engine
    /// that wrote them) says a designer adds them, and otherwise any still coming for older notes are dropped.</summary>
    public void AfterFiled(string id, string? notesEngine)
    {
        if (notesEngine is null) Cancel(id);
        else Queue(id, notesEngine);
    }

    /// <summary>Diagrams for a lecture's notes as they're filed now: any job for older notes is replaced (a pass running
    /// for them stops, and its result is dropped).</summary>
    public void Queue(string id, string notesEngine)
    {
        CancellationTokenSource? older = null;
        lock (gate)
        {
            var job = new Job { Id = id, NotesEngine = notesEngine, Queued = DateTimeOffset.UtcNow.ToString("o") };
            jobs[id] = job;
            Persist(job);
            if (running is { } r && r.Id == id) older = r.Cts;
        }
        // Stopped once the new job is in its place, so the older pass sees it's been replaced.
        Stop(older);
        log($"[diagrams] '{Title(id)}': its notes are filed; diagrams follow");
        Wake();
    }

    /// <summary>No diagrams for this lecture after all (its notes were filed again without a designer, or it's gone).</summary>
    public void Cancel(string id)
    {
        CancellationTokenSource? older = null;
        lock (gate)
        {
            if (!jobs.Remove(id)) return;
            Forget(id);
            if (running is { } r && r.Id == id) older = r.Cts;
        }
        Stop(older);
    }

    static void Stop(CancellationTokenSource? pass)
    {
        try
        {
            pass?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // It had just finished.
        }
    }

    /// <summary>Whether diagrams are on their way for this lecture: waiting, or being designed.</summary>
    public bool Adding(string id)
    {
        lock (gate) return jobs.ContainsKey(id);
    }

    /// <summary>Look at the queue now instead of within <see cref="Pause"/>.</summary>
    public void Wake()
    {
        try
        {
            wake.Release();
        }
        catch (SemaphoreFullException)
        {
            // already woken
        }
    }

    public Task Start(CancellationToken stop) => Task.Run(() => RunForeverAsync(stop), CancellationToken.None);

    public async Task RunForeverAsync(CancellationToken stop)
    {
        int n;
        lock (gate) n = jobs.Count;
        if (n > 0) log($"[diagrams] {n} lecture(s) still to get their diagrams after a restart");
        while (!stop.IsCancellationRequested)
        {
            try
            {
                await RunPendingAsync(stop);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                log($"[diagrams] error: {e.Message}");
            }
            try
            {
                await wake.WaitAsync(Pause, stop);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>Every job that can run now, one after another; stops while notes are being written. How many ended.</summary>
    public async Task<int> RunPendingAsync(CancellationToken stop = default)
    {
        int ended = 0;
        while (!stop.IsCancellationRequested && !NotesBusy())
        {
            Job? job;
            string now = DateTimeOffset.UtcNow.ToString("o");
            lock (gate)
                job = jobs.Values.Where(j => string.CompareOrdinal(j.NotBefore, now) <= 0).OrderBy(j => j.Queued, StringComparer.Ordinal).FirstOrDefault();
            if (job is null) break;
            // A job that's to run again later (it stepped aside for notes, or its file waits to be written) isn't
            // picked again until then: notes are busy, or its time hasn't come.
            if (await RunAsync(job, stop)) ended++;
        }
        return ended;
    }

    bool Current(Job job)
    {
        lock (gate) return jobs.TryGetValue(job.Id, out var j) && ReferenceEquals(j, job);
    }

    /// <summary>Drop a job, saying why in the log (null: the pass said already).</summary>
    void End(Job job, string? why)
    {
        lock (gate)
        {
            if (!jobs.TryGetValue(job.Id, out var j) || !ReferenceEquals(j, job)) return;
            jobs.Remove(job.Id);
            Forget(job.Id);
        }
        if (why is not null) log($"[diagrams] '{Title(job.Id)}': {why}");
    }

    /// <summary>One job, as far as it can go now: true once it's ended (diagrams in, or given up on, or no longer
    /// wanted), false when it's to run again later (it stepped aside for notes, the library is stopping, or the note
    /// file couldn't be written yet).</summary>
    async Task<bool> RunAsync(Job job, CancellationToken stop)
    {
        var row = store.Get(job.Id);
        if (row is null || (row.Status is { Length: > 0 } s && s != Store.Done) || string.IsNullOrEmpty(row.SummaryMd))
        {
            End(job, "the lecture was deleted, or is being written again, before its diagrams; nothing changed");
            return true;
        }
        if (job.State == "designed") return Land(job);
        if (job.Tries >= MaxTries)
        {
            End(job, $"its diagrams were given up on after {MaxTries} tries; the notes stay as filed");
            return true;
        }

        var m = store.Meeting(row);
        string notes = row.SummaryMd;
        lock (gate)
        {
            if (!Current(job)) return true;
            job.Tries++;
            job.State = "designing";
            job.Seen = Notes.Fingerprint(notes);
            Persist(job);
        }
        DiagramPick? pick;
        try
        {
            pick = await ai.DesignerAsync(cfg, job.NotesEngine);
        }
        catch (Exception e) when (e is HttpRequestException or IOException or InvalidOperationException or TaskCanceledException)
        {
            pick = null;
            log($"[diagrams] '{Title(job.Id)}': couldn't tell which engine designs diagrams ({e.Message})");
        }
        if (pick is null)
        {
            End(job, "nothing designs diagrams now (they were turned off, or no engine is to be had); the notes stay as filed");
            return true;
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(stop);
        cts.CancelAfter(HardStop);
        lock (gate)
        {
            if (!Current(job)) return true;
            running = (job.Id, cts);
        }
        bool yielded = false;
        // A pass on this computer's own model shares it with the notes: it steps aside when they need it.
        var watching = pick.Engine == "ollama" ? WatchAsync(cts, () => yielded = true) : Task.CompletedTask;
        DesignResult? result;
        try
        {
            result = await ai.DesignDiagramsAsync(m, cfg, notes, pick, cts.Token).WaitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            if (!Current(job)) return true; // replaced by newer notes' job, or not wanted any more
            if (yielded || stop.IsCancellationRequested)
            {
                // Not a try that failed: it runs again (after the notes, or when the library starts again).
                lock (gate)
                {
                    job.Tries--;
                    job.State = "queued";
                    Persist(job);
                }
                if (yielded) log($"[diagrams] '{Title(job.Id)}': stepped aside while notes are written; it starts again after");
                return false;
            }
            End(job, $"its diagrams took longer than {(HardStop.TotalMinutes >= 1 ? $"{HardStop.TotalMinutes:0} minutes" : $"{HardStop.TotalSeconds:0.#} seconds")} and were stopped; the notes stay as filed");
            return true;
        }
        finally
        {
            lock (gate) running = null;
            await cts.CancelAsync();
            await watching;
        }
        if (!Current(job)) return true;
        if (result is null || result.Drawn.Count == 0)
        {
            End(job, null); // the pass said why, in the log
            return true;
        }
        lock (gate)
        {
            if (!Current(job)) return true;
            job.Diagrams = [.. result.Drawn];
            job.State = "designed";
            Persist(job);
        }
        return Land(job);
    }

    async Task WatchAsync(CancellationTokenSource cts, Action yielded)
    {
        try
        {
            while (!cts.IsCancellationRequested)
            {
                await Task.Delay(YieldCheck, cts.Token);
                if (!NotesBusy()) continue;
                yielded();
                await cts.CancelAsync();
                return;
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>Designed diagrams into the notes (<see cref="Store.AddDiagrams"/>), and what became of them in the log.</summary>
    bool Land(Job job)
    {
        DiagramsOutcome outcome;
        lock (gate)
        {
            if (!Current(job)) return true;
            outcome = store.AddDiagrams(job.Id, job.Seen, job.Diagrams);
            if (outcome.How == DiagramsLanded.Unwritten && ++job.Writes < MaxTries)
            {
                job.NotBefore = DateTimeOffset.UtcNow.Add(RetryWait).ToString("o");
                Persist(job);
                log($"[diagrams] '{Title(job.Id)}': couldn't write the note file ({outcome.Why}); trying again shortly");
                return false;
            }
        }
        static string Names(IEnumerable<DesignedDiagram> ds) => string.Join(", ", ds.Select(d => $"“{d.Title}”"));
        static string Count(int n) => n == 1 ? "1 diagram" : $"{n} diagrams";
        string file = outcome.FileEdited ? " (the note file had edits of its own: they're kept, the diagrams put into its summary)" : "";
        End(job, outcome.How switch
        {
            DiagramsLanded.Added => $"added {Count(outcome.Placed.Count)} to the notes{file}",
            DiagramsLanded.Reanchored => $"the notes changed while their diagrams were designed: {Count(outcome.Placed.Count)} went under {(outcome.Placed.Count == 1 ? "its heading" : "their headings")}"
                + (outcome.Skipped.Count > 0 ? $", and {Names(outcome.Skipped)} {(outcome.Skipped.Count == 1 ? "was" : "were")} left out, {(outcome.Skipped.Count == 1 ? "its heading" : "their headings")} gone" : "") + file,
            DiagramsLanded.Skipped => $"the notes changed while their diagrams were designed, and none of their headings are there now; the notes stay as they are",
            DiagramsLanded.Gone => "the lecture was deleted, or is being written again, before its diagrams were ready; nothing changed",
            _ => $"couldn't write the note file ({outcome.Why}) after {MaxTries} tries; the notes stay as filed",
        });
        return true;
    }
}
