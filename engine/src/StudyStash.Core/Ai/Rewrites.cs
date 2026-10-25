using System.Text.Json;
using System.Text.Json.Nodes;

namespace StudyStash.Core.Ai;

/// <summary>A rewrite couldn't do what was asked, with the HTTP status the library's endpoint should answer.</summary>
public sealed class RewriteRefusedException(int status, string message) : Exception(message)
{
    public int Status { get; } = status;
}

/// <summary>
/// One rewrite job per lecture: "Rewrite notes with" runs in the background with the engine the student picked,
/// while the lecture's current notes stay exactly as they are. When it's ready the student keeps the old notes,
/// compares, or uses the new ones — <see cref="Use"/> is the only thing that actually changes the lecture. A ready
/// (or failed, or cancelled) job is written to <c>home/rewrites</c> as it happens, so it survives a restart; one
/// still working when the library stopped comes back failed, since nothing here can pick a run back up mid-way. As
/// with the pipeline's notes, the new notes come without the designer's diagrams, ready as soon as their words are:
/// once they're used, <paramref name="diagrams"/> adds those (and says so, while it does, in <see cref="Get"/>).
/// </summary>
public sealed class Rewrites(Config cfg, Store store, AiJobs ai, Action<string>? log = null, DiagramJobs? diagrams = null)
{
    readonly Lock gate = new();
    readonly Dictionary<string, Job> jobs = LoadAll(cfg.Home);
    readonly Action<string> log = log ?? Console.WriteLine;

    sealed class Job
    {
        public required string Engine;
        public string State = "working"; // working | ready | failed | cancelled
        public string Started = "";
        public string Error = "";
        public int Done;
        public int Parts;
        public string? DraftMarkdown;
        /// <summary>What wrote the draft, as <see cref="AiJobs.DescribeChoice"/> put it ("Claude sonnet"): kept raw
        /// so <see cref="Use"/> can save it exactly as any other note's <c>summary_model</c> would be saved.</summary>
        public string DraftModel = "";
        public string DraftAt = "";
        /// <summary>The attachments whose words went into the draft (not kept across a restart: then none count as used).</summary>
        public List<string> AttachmentIds = [];
        /// <summary>The engine that wrote the draft when a designer adds its diagrams once it's used; null when none do.</summary>
        public string? DiagramsBy;
        /// <summary>Null once the job's finished — nothing left for <see cref="Cancel"/> to stop.</summary>
        public CancellationTokenSource? Cts;
    }

    /// <summary>The same shape as <see cref="Job"/>, minus what only matters while a job is running, plus the
    /// lecture id it's for (a file's name is a hash of that, not the id itself, so nothing about it needs to survive
    /// a round trip through a file name).</summary>
    sealed record Saved(string Id, string Engine, string State, string Started, string Error, int Done, int Parts,
        string? DraftMarkdown, string DraftModel, string DraftAt, string? DiagramsBy = null);

    static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    static string Dir(string home) => Path.Combine(home, "rewrites");

    static string FileFor(string home, string id) =>
        Path.Combine(Dir(home), Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(id))) + ".json");

    static void Persist(string home, string id, Job job)
    {
        Directory.CreateDirectory(Dir(home));
        var saved = new Saved(id, job.Engine, job.State, job.Started, job.Error, job.Done, job.Parts,
            job.DraftMarkdown, job.DraftModel, job.DraftAt, job.DiagramsBy);
        Py.WriteText(FileFor(home, id), JsonSerializer.Serialize(saved, JsonOptions));
    }

    static void Forget(string home, string id)
    {
        string path = FileFor(home, id);
        if (File.Exists(path)) File.Delete(path);
    }

    /// <summary>Every job the last run left behind. One still "working" never really finished, so it comes back
    /// failed instead of stuck forever looking busy.</summary>
    static Dictionary<string, Job> LoadAll(string home)
    {
        var result = new Dictionary<string, Job>();
        if (!Directory.Exists(Dir(home))) return result;
        foreach (string path in Directory.EnumerateFiles(Dir(home), "*.json"))
        {
            Saved? saved;
            try
            {
                saved = JsonSerializer.Deserialize<Saved>(File.ReadAllText(path), JsonOptions);
            }
            catch (JsonException)
            {
                continue; // a broken file: treat that lecture as having no rewrite job
            }
            if (saved is null) continue;
            var job = new Job
            {
                Engine = saved.Engine, State = saved.State, Started = saved.Started, Error = saved.Error,
                Done = saved.Done, Parts = saved.Parts, DraftMarkdown = saved.DraftMarkdown,
                DraftModel = saved.DraftModel, DraftAt = saved.DraftAt, DiagramsBy = saved.DiagramsBy,
            };
            if (job.State == "working")
            {
                job.State = "failed";
                job.Error = "The library restarted before the new notes were ready.";
                Persist(home, saved.Id, job);
            }
            result[saved.Id] = job;
        }
        return result;
    }

    bool Installed(string engine) =>
        engine == "ollama" ? ai.Checks.OllamaInstalled() : ai.Checks.Which(AiProviders.Get(engine).Binary) is not null;

    static List<string> JsonTopics(string? json) =>
        (JsonNode.Parse(string.IsNullOrEmpty(json) ? "[]" : json) as JsonArray ?? []).Select(Py.Str).ToList();

    RewriteInfo Build(string id, NoteRow row, Job? job)
    {
        var current = new NotesVersion(row.SummaryMd ?? "", Engines.WhoWrote(row.SummaryModel ?? ""), row.UpdatedAt ?? "");
        string adding = diagrams?.Adding(id) == true ? "adding" : "";
        var change = Change(id);
        if (job is null) return new RewriteInfo(id, "none") { Current = current, Diagrams = adding, Change = change };
        return new RewriteInfo(id, job.State)
        {
            Engine = job.Engine, EngineName = Engines.Name(job.Engine), Started = job.Started, Error = job.Error,
            Done = job.Done, Parts = job.Parts, Current = current, Diagrams = adding, Change = change,
            Draft = job.State == "ready" && job.DraftMarkdown is { } md
                ? new NotesVersion(md, Engines.WhoWrote(job.DraftModel), job.DraftAt) : null,
        };
    }

    NoteRow Require(string id) => store.Get(id) ?? throw new RewriteRefusedException(404, "there's no lecture with that id.");

    /// <summary>Where things stand: <c>none</c> when nobody's ever asked for a rewrite (or it was kept/used away).</summary>
    public RewriteInfo Get(string id)
    {
        var row = Require(id);
        lock (gate) return Build(id, row, jobs.GetValueOrDefault(id));
    }

    /// <summary>Start rewriting, with the old notes staying put until the student chooses. Refuses (409) when one's
    /// already running, the transcript's too short to write from, the pipeline is still writing this lecture's
    /// first notes, or the engine isn't installed; 404 for an unknown lecture.</summary>
    public RewriteInfo Start(string id, string engine)
    {
        var row = Require(id);
        lock (gate)
        {
            if (jobs.TryGetValue(id, out var running) && running.State == "working")
                throw new RewriteRefusedException(409, "a rewrite is already running for this lecture.");
            var m = store.Meeting(row);
            store.Attach(m); // the new notes use what the student attached, too
            if (Py.Strip(m.Transcript).Length < Summarize.MinTranscriptChars && !Summarize.FromNotes(m))
                throw new RewriteRefusedException(409, "this lecture doesn't have enough of a transcript to rewrite from.");
            if (row.Status is Store.Queued or Store.Working)
                throw new RewriteRefusedException(409, "the library is still writing this lecture's notes.");
            if (!Installed(engine))
                throw new RewriteRefusedException(409, $"{Engines.Name(engine)} isn't set up on your library's computer.");

            var cts = new CancellationTokenSource();
            var job = new Job { Engine = engine, Started = ai.Checks.Now().ToString("o"), Cts = cts, AttachmentIds = [.. m.Attached.Select(a => a.Id), .. m.Photos.Select(p => p.Id)] };
            jobs[id] = job;
            Persist(cfg.Home, id, job);
            _ = RunAsync(id, m, job, cts.Token);
            return Build(id, row, job);
        }
    }

    async Task RunAsync(string id, Meeting m, Job job, CancellationToken ct)
    {
        string draft = "";
        string? diagramsBy = null;
        Exception? failure = null;
        try
        {
            (draft, diagramsBy) = await ai.WriteNotesPlannedAsync(m, cfg, job.Engine, (done, parts) =>
            {
                lock (gate) { job.Done = done; job.Parts = parts; }
            }, ct);
        }
        catch (Exception e)
        {
            // A background job never gets another chance to answer: whatever went wrong (the engine, a bad
            // response, cancellation), it ends this job as failed or cancelled rather than faulting unseen.
            failure = e;
        }
        // New notes with nothing in them aren't notes: nothing is offered in place of the ones the lecture has.
        if (failure is null && Py.Strip(draft).Length == 0) failure = new InvalidOperationException($"{Engines.Name(job.Engine)} wrote nothing.");
        lock (gate)
        {
            // Keep or a fresh Start already moved this lecture on; this run's result no longer belongs anywhere.
            if (!jobs.TryGetValue(id, out var tracked) || !ReferenceEquals(tracked, job) || job.State == "cancelled")
                return;
            if (failure is OperationCanceledException)
            {
                job.State = "cancelled";
            }
            else if (failure is not null)
            {
                job.State = "failed";
                // Signed out: said as what to do, not as the AI's own "Not logged in · Please run /login".
                job.Error = job.Engine != "ollama" && Engines.LooksSignedOut(failure.Message)
                    ? Engines.RewriteNeedsSignIn(Engines.Name(job.Engine)) : failure.Message;
                log($"[rewrite] {Engines.Name(job.Engine)} couldn't rewrite '{m.Title}': {failure.Message}");
                ai.Record(job.Engine, false, failure.Message);
            }
            else
            {
                job.State = "ready";
                job.DraftMarkdown = draft;
                job.DiagramsBy = diagramsBy;
                job.DraftModel = ai.DescribeChoice(job.Engine, cfg);
                job.DraftAt = ai.Checks.Now().ToString("o");
                ai.Record(job.Engine, true, "");
            }
            job.Cts = null;
            Persist(cfg.Home, id, job);
        }
    }

    /// <summary>Stop a running rewrite. Best-effort: the engine's token is cancelled, but the job is marked
    /// cancelled right away either way, so a real Ollama call with no way to stop mid-generation still ends the job
    /// here rather than leaving it looking busy forever.</summary>
    public RewriteInfo Cancel(string id)
    {
        var row = Require(id);
        lock (gate)
        {
            if (jobs.TryGetValue(id, out var job) && job.State == "working")
            {
                job.Cts?.Cancel();
                job.Cts = null;
                job.State = "cancelled";
                Persist(cfg.Home, id, job);
            }
            return Build(id, row, jobs.GetValueOrDefault(id));
        }
    }

    /// <summary>Drop the draft: the lecture's notes were never touched, so there's nothing else to undo.</summary>
    public RewriteInfo Keep(string id)
    {
        var row = Require(id);
        lock (gate)
        {
            if (jobs.Remove(id, out var job))
            {
                job.Cts?.Cancel();
                Forget(cfg.Home, id);
            }
            return Build(id, row, null);
        }
    }

    /// <summary>Save the ready draft as the lecture's notes, shown at once; their diagrams follow (any still coming for
    /// the old notes are dropped). Refuses (409) with no ready draft, or while the pipeline has this lecture queued or
    /// working right now — checked here, not just at <see cref="Start"/>, since the two can race on the same row.</summary>
    public RewriteInfo Use(string id)
    {
        var row = Require(id);
        lock (gate)
        {
            // A draft with nothing in it (one kept from before a rewrite that wrote nothing counted as failed) isn't new notes.
            if (!jobs.TryGetValue(id, out var job) || job.State != "ready" || job.DraftMarkdown is not { } draft || Py.Strip(draft).Length == 0)
                throw new RewriteRefusedException(409, "there are no new notes ready to use yet.");
            if (row.Status is Store.Queued or Store.Working)
                throw new RewriteRefusedException(409, "the library is writing this lecture's notes right now.");
            var m = store.Meeting(row);
            var c = new Classification(row.ClassName ?? "", row.Confidence ?? 1, row.ClassifiedBy ?? "human",
                row.LectureTitle ?? "", JsonTopics(row.Topics));
            store.Save(m, c, draft, job.DraftModel, versionKind: NotesChangeKind.Rewrite);
            store.MarkAttachmentsUsed(job.AttachmentIds);
            jobs.Remove(id);
            Forget(cfg.Home, id);
            try
            {
                diagrams?.AfterFiled(id, job.DiagramsBy);
            }
            catch (IOException e)
            {
                log($"[rewrite] '{m.Title}': its diagrams couldn't be queued ({e.Message}); the new notes are in");
            }
            return Build(id, store.Get(id) ?? row, null);
        }
    }

    /// <summary>Save the lecture's notes as the student edited them by hand (<see cref="Store.EditNotes"/>): shown at
    /// once, whoever wrote them still named as their writer, and a rewrite running or waiting to be chosen left as it
    /// is. Refuses empty notes (400); while the pipeline is writing this lecture's notes (409); and, with
    /// <paramref name="basedOn"/> given, when the notes are no longer the ones the edit started from (412: their
    /// diagrams arrived, another device changed them, or their summary was edited in the note file itself, which
    /// makes those the notes), so the student chooses rather than undoing that unseen.</summary>
    public RewriteInfo Edit(string id, string markdown, string? basedOn = null)
    {
        var row = Require(id);
        markdown = Py.Strip(markdown.ReplaceLineEndings("\n"));
        if (markdown.Length == 0) throw new RewriteRefusedException(400, "the notes are empty.");
        lock (gate)
        {
            switch (store.EditNotes(id, markdown, basedOn))
            {
                case NotesEdited.Gone:
                    throw new RewriteRefusedException(404, "there's no lecture with that id.");
                case NotesEdited.Busy:
                    throw new RewriteRefusedException(409, "the library is writing this lecture's notes right now.");
                case NotesEdited.Changed:
                    throw new RewriteRefusedException(412, "these notes changed while you were editing them.");
                case NotesEdited.Unwritten:
                    throw new RewriteRefusedException(503, "the note's file couldn't be saved (another app may have it open).");
            }
            return Build(id, store.Get(id) ?? row, jobs.GetValueOrDefault(id));
        }
    }

    // --- notes from an AI the library doesn't run: an AI app through Study Stash's connection, or a free chat ----------

    /// <summary>
    /// Notes an AI app wrote (save_notes) or the student pasted from a chat, saved as the lecture's notes the way an edit
    /// is (<see cref="Store.EditNotes"/>), named as <paramref name="writer"/>'s: tidied first of what chats put round
    /// notes (<see cref="ChatNotes.Clean"/>), every line the student typed during the lecture kept in them and marked
    /// as theirs, as the library's own notes keep them (<see cref="Summarize.KeepTyped"/>), and the notes they replace
    /// kept to put back. Refuses empty notes (400); a lecture with notes of its own, unless <paramref name="replace"/>
    /// (409); while the pipeline is writing this lecture's notes (409); and, with <paramref name="basedOn"/>, notes
    /// that changed since (412).
    /// </summary>
    public RewriteInfo Save(string id, string markdown, bool replace, string writer, string? basedOn = null, string kind = NotesChangeKind.App)
    {
        var row = Require(id);
        string notes = ChatNotes.Clean(markdown);
        if (notes.Length == 0) throw new RewriteRefusedException(400, "the notes are empty.");
        lock (gate)
        {
            row = store.Get(id) ?? row;
            if (row.Status is Store.Queued or Store.Working)
                throw new RewriteRefusedException(409, "the library is writing this lecture's notes right now. Try again once they're done.");
            var m = store.Meeting(row);
            bool hasNotes = Py.Strip(row.SummaryMd).Length > 0 || Py.Strip(m.NotesMarkdown).Length > 0;
            if (hasNotes && !replace)
                throw new RewriteRefusedException(409, "this lecture already has notes. Ask the student whether to replace them; "
                    + "if they say yes, save again with replace set to true (the notes it has now are kept, and the student can put them back).");
            notes = Summarize.KeepTyped(notes, Summarize.Typed(m));
            // An AI app putting its own draft right (a diagram fixed) keeps one version of it; a paste is always its own.
            switch (store.EditNotes(id, notes, basedOn, writer, redraft: kind == NotesChangeKind.App, kind: kind))
            {
                case NotesEdited.Gone:
                    throw new RewriteRefusedException(404, "there's no lecture with that id.");
                case NotesEdited.Busy:
                    throw new RewriteRefusedException(409, "the library is writing this lecture's notes right now. Try again once they're done.");
                case NotesEdited.Changed:
                    throw new RewriteRefusedException(412, "this lecture's notes changed meanwhile.");
                case NotesEdited.Unwritten:
                    throw new RewriteRefusedException(503, "the note's file couldn't be saved (another app may have it open).");
            }
            log($"[notes] '{m.Title}': notes saved, written by {writer}");
            return Build(id, store.Get(id) ?? row, jobs.GetValueOrDefault(id));
        }
    }

    static string Id(long seq) => seq.ToString(System.Globalization.CultureInfo.InvariantCulture);

    static NotesKept Kept(NoteVersion v, bool current = false, bool words = false) =>
        new(Id(v.Seq), v.Kind, v.Model.Length > 0 ? Engines.WhoWrote(v.Model) : "", v.At) { Current = current, Markdown = words ? v.Markdown : "" };

    /// <summary>The last change to the lecture's notes, and whether Undo or Redo can follow it.</summary>
    NotesChange? Change(string id)
    {
        // One row, without its words: asked on every page open and every poll.
        if (store.NewestVersion(id) is not { } newest) return null;
        return new NotesChange(Id(newest.Seq), newest.Kind, newest.Model.Length > 0 ? Engines.WhoWrote(newest.Model) : "", newest.At)
        {
            CanUndo = newest.UndoTo is not null, CanRedo = newest.RedoTo is not null,
        };
    }

    /// <summary>The versions of the lecture's notes, newest first (the first is the notes now). A lecture from before
    /// versions were kept gets its notes as they are as its first.</summary>
    public List<NotesKept> Versions(string id)
    {
        Require(id);
        store.EnsureFirstVersion(id);
        return [.. store.NotesVersions(id).Select((v, i) => Kept(v, current: i == 0))];
    }

    /// <summary>One version, with its words; 404 when there's no such version.</summary>
    public NotesKept Version(string id, string version)
    {
        Require(id);
        var kept = long.TryParse(version, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out long seq)
            ? store.NotesVersion(id, seq) : null;
        if (kept is null) throw new RewriteRefusedException(404, "there's no such version of these notes.");
        return Kept(kept, current: store.NewestVersion(id)?.Seq == kept.Seq, words: true);
    }

    /// <summary>Puts a version of the lecture's notes back as a new version (<paramref name="version"/>, or the one before
    /// the notes now: <see cref="Store.RestoreNotesVersion"/>), undone like any change. Refuses (409) with no such
    /// version, or while the pipeline is writing this lecture's notes.</summary>
    public RewriteInfo Restore(string id, string? version = null)
    {
        long? seq = long.TryParse(version, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out long n) ? n : null;
        if (version is { Length: > 0 } && seq is null) throw new RewriteRefusedException(409, "there are no such earlier notes to put back.");
        return PutBack(id, () => store.RestoreNotesVersion(id, seq), "there are no earlier notes to put back.");
    }

    /// <summary>Undo: the notes before the last change, put back (409 with nothing to undo). <paramref name="expect"/>:
    /// the version the student's app shows; when the notes changed elsewhere since, 412, and nothing is undone.</summary>
    public RewriteInfo Undo(string id, string? expect = null) => PutBack(id, () => store.UndoNotes(id, Seq(expect)), "there's nothing to undo.");

    /// <summary>Redo: the notes the last undo went back from, put back (409 with nothing to redo; 412 as for Undo).</summary>
    public RewriteInfo Redo(string id, string? expect = null) => PutBack(id, () => store.RedoNotes(id, Seq(expect)), "there's nothing to redo.");

    /// <summary>A version's id as the library numbers them; null for none (or one that isn't a number, which no version has).</summary>
    static long? Seq(string? version) =>
        long.TryParse(version, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out long n) ? n : version is { Length: > 0 } ? -1 : null;

    RewriteInfo PutBack(string id, Func<NotesEdited> run, string none)
    {
        var row = Require(id);
        lock (gate)
        {
            switch (run())
            {
                case NotesEdited.Gone:
                    throw new RewriteRefusedException(409, none);
                case NotesEdited.Busy:
                    throw new RewriteRefusedException(409, "the library is writing this lecture's notes right now.");
                case NotesEdited.Changed:
                    throw new RewriteRefusedException(412, "these notes changed meanwhile.");
                case NotesEdited.Unwritten:
                    throw new RewriteRefusedException(503, "the note's file couldn't be saved (another app may have it open).");
            }
            return Build(id, store.Get(id) ?? row, jobs.GetValueOrDefault(id));
        }
    }

    /// <summary>
    /// How Study Stash writes this lecture's notes, for an AI it doesn't run: its own notes prompt
    /// (<see cref="Summarize.Brief"/>, with the lines the student typed), drawing what the rich notes switches allow. For
    /// a free chat (<paramref name="chat"/>), flowcharts at most (SVG is beyond most of them), and the whole request to
    /// copy, transcript and all, in as many parts as it takes (<see cref="ChatNotes.Request"/>). Refuses (409) a lecture
    /// with nothing to write notes from.
    /// </summary>
    public NotesBrief Brief(string id, bool chat)
    {
        var row = Require(id);
        var m = store.Meeting(row);
        var kinds = ai.KindsNow();
        var drawings = chat ? (kinds.HasFlag(RichKinds.Diagrams) ? Drawings.Flowcharts : Drawings.None) : AiJobs.NotesDrawings("claude", kinds);
        string brief = Summarize.Brief(m, drawings);
        if (Summarize.FromNotes(m))
        {
            // No transcript: what it came with is what the notes are written from.
            string prompt = Summarize.NotesPrompt(m, Py.Head(Py.Strip(m.NotesMarkdown), ChatNotes.OneMessage));
            return new NotesBrief(cfg.ForSchool ? brief : Summarize.ForMeetings(brief), [cfg.ForSchool ? prompt : Summarize.ForMeetings(prompt)]);
        }
        if (Py.Strip(m.Transcript).Length == 0) throw new RewriteRefusedException(409, "this lecture has no transcript to write notes from.");
        return new NotesBrief(cfg.ForSchool ? brief : Summarize.ForMeetings(brief), chat ? ChatNotes.Request(brief, m.Transcript, cfg.ForSchool) : []);
    }
}
