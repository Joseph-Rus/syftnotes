using System.Diagnostics;
using System.Net.Sockets;
using StudyStash.Core.Ai;

namespace StudyStash.Core;

/// <summary>The notes engine didn't answer at all (not running, or not reachable): the lecture waits for it instead of
/// being filed without notes.</summary>
/// <summary>No AI is chosen to write the notes yet (a fresh library): the lecture is filed with its transcript and says so,
/// and its notes are written once the student chooses one.</summary>
public sealed class NoNotesAiException() : Exception(Engines.NotesNeedAnAi);

public sealed class EngineOfflineException(string engine, Exception inner)
    : Exception($"{engine} isn't answering: {inner.Message}", inner)
{
    /// <summary>What writes the notes ("Ollama").</summary>
    public string Engine { get; } = engine;
}

/// <summary>The computer slept while the notes were being written, and they failed on waking: the lecture goes back
/// in the queue for another try, not filed without notes.</summary>
public sealed class SleptException(Exception inner) : Exception("the computer slept while the notes were being written", inner);

/// <summary>
/// Turns queued lectures into filed ones: writes our notes from the transcript, sorts, saves. Runs in the
/// background of the library, so an upload from the laptop returns at once even when a big model takes minutes.
/// While the notes engine isn't answering, lectures stay in the queue and get their notes when it's back; notes cut
/// off because the computer slept (<paramref name="sleep"/> tells) are written again after it wakes. A lecture that
/// has notes already (a rewrite, or one sent again) keeps them unless new ones are written. A lecture is filed
/// as soon as its notes are written: <paramref name="filed"/> hears its id then, and whatever follows the notes (their
/// diagrams, <see cref="Ai.DiagramJobs"/>) is queued there, never waited for.
/// </summary>
public sealed class Pipeline(Config cfg, Store store, SortChatFn? chat = null,
    Func<Meeting, Config, Task<string>>? summarize = null, Action<string>? log = null, Func<string>? notesModel = null,
    Func<string>? notesEngine = null, SleepClock? sleep = null, Action<string>? filed = null, Func<bool>? notesFast = null,
    Func<string, DateTime?>? limitUntil = null)
{
    readonly SleepClock sleep = sleep ?? SleepClock.System;
    readonly Func<Meeting, Config, Task<string>> summarize = summarize ?? ((m, c) => Summarize.SummarizeTranscriptAsync(m, c));
    readonly Action<string> log = log ?? Console.WriteLine;
    readonly SemaphoreSlim wake = new(0, 1);
    volatile string? current;
    volatile string? engineProblem;

    public Config Cfg { get; } = cfg;

    /// <summary>The id of the lecture being written right now.</summary>
    public string? Current => current;

    /// <summary>Notes are being written right now, or lectures wait for theirs with the notes engine answering: what
    /// follows the notes (their diagrams) doesn't start meanwhile, so it never holds them up.</summary>
    public bool Writing => current is not null || (engineProblem is null && store.StatusCounts().GetValueOrDefault(Store.Queued) > 0);

    /// <summary>"Ollama isn't answering on your library. New lectures wait and get their notes when it's back.", while
    /// it isn't; null once notes are written again.</summary>
    public string? EngineProblem => engineProblem;

    /// <summary>How long a pass waits before looking at the queue again: longer while the notes engine is away.</summary>
    public TimeSpan Pause => engineProblem is null ? TimeSpan.FromSeconds(30) : TimeSpan.FromSeconds(60);

    /// <summary>Not an answer, but no answer: nothing listening, the connection dropped, or no reply in time. A
    /// refusal with a status (a model it doesn't have) is an answer, and keeps today's handling.</summary>
    public static bool IsOffline(Exception e) => e switch
    {
        TimeoutException => true,
        HttpRequestException h => h.StatusCode is null || h.InnerException is SocketException,
        _ => false,
    };

    public async Task<string?> ProcessAsync(NoteRow row)
    {
        var m = store.Meeting(row);
        store.Attach(m); // what the student attached, with the words read from it so far
        string summary = "", model = "", error = "";
        // Why this run writes no notes, for a lecture that keeps the ones it has (below).
        string unwritten = !Cfg.SummaryEnabled ? "The notes weren't rewritten: writing study notes is switched off in your library's Settings."
            : !Cfg.OllamaEnabled ? "The notes weren't rewritten: AI is switched off in your library's Settings."
            : "The notes weren't rewritten: the transcript is too short to write notes from.";
        if (Summarize.WantsSummary(m, Cfg))
        {
            model = notesModel?.Invoke() ?? Cfg.EffectiveSummaryModel; // what writes the notes, as the note records it
            unwritten = $"The notes weren't rewritten: {model} wrote nothing.";
            string how = notesFast?.Invoke() == true ? " in fast mode" : ""; // Claude Code's fast mode costs more: the log says when it was used
            var watch = Stopwatch.StartNew();
            var slept = sleep.Start();
            try
            {
                summary = await summarize(m, Cfg);
                // What really wrote them (another model than the one asked, when a limit moved it): the byline's.
                model = notesModel?.Invoke() ?? model;
                // The engine just answered: say so now, before the row's status turns to "done" below, so anyone
                // watching never sees a finished lecture next to a stale "isn't answering" message.
                if (engineProblem is not null)
                {
                    engineProblem = null;
                    log("[pipeline] the notes engine is answering again");
                }
                log($"[pipeline] summarized '{m.Title}' with {model}{how} in {watch.Elapsed.TotalSeconds:0}s");
            }
            catch (NoNotesAiException)
            {
                error = Engines.NotesNeedAnAi;
                unwritten = "The notes weren't written: no AI writes them yet. Choose one in Settings → Your AI.";
                log($"[pipeline] '{m.Title}' has no notes yet: no AI is chosen to write them");
                summary = model = "";
            }
            catch (Exception e) when (IsOffline(e))
            {
                throw new EngineOfflineException(notesEngine?.Invoke() ?? "Ollama", e);
            }
            catch (Exception e) when (slept())
            {
                throw new SleptException(e);
            }
            catch (Exception e)
            {
                error = $"Summary with {model} failed: {e.Message}";
                // An engine's own words often start with its name ("Claude: Claude isn't installed"): it's just been said.
                int named = e.Message.IndexOf(": ", StringComparison.Ordinal);
                string why = named > 0 && model.StartsWith(e.Message[..named], StringComparison.Ordinal) ? e.Message[(named + 2)..] : e.Message;
                unwritten = Sentence($"{model} couldn't rewrite the notes: {why}");
                log($"[pipeline] {error}");
                // An AI that needs signing in again: its own words ("Not logged in · Please run /login") don't tell a
                // student why their notes aren't there or what to do, so the lecture says it plainly.
                if (Engines.WhoWrote(model) is { Length: > 0 } who && who != "Ollama" && Engines.LooksSignedOut(e.Message))
                {
                    error = Engines.NotesNeedSignIn(who);
                    unwritten = Engines.RewriteNeedsSignIn(who);
                }
                // An AI over its usage limit (Gemini's free allowance used up for the week): the lecture keeps its
                // transcript and says until when; the library writes its notes once the limit's over.
                else if (Engines.WhoWrote(model) is { Length: > 0 } limited && limited != "Ollama" && Engines.LooksLikeLimit(e.Message))
                    error = Engines.NotesWaitForLimit(limited, limitUntil?.Invoke(limited));
                summary = model = "";
            }
        }
        // A lecture that has notes keeps them until new ones are written: a run that writes none (its engine failed or
        // wrote nothing, notes are switched off, the transcript is too short) changes nothing about it but to say why.
        // Filing it the way a new lecture with no notes is filed wiped the notes it had, its title and its topics.
        if (Py.Strip(summary).Length == 0 && !string.IsNullOrEmpty(row.SummaryMd))
        {
            string? kept = store.KeepNotes(row, m, unwritten);
            log(kept is null
                ? $"[pipeline] '{m.Title}' changed while it was being written; not saving the old result"
                : $"[pipeline] '{m.Title}' keeps the notes it has: {unwritten}");
            return kept;
        }
        Classification c;
        if (row.ClassifiedBy == "human" && !string.IsNullOrEmpty(row.ClassName))
        {
            // A person filed it: writing the notes again never undoes that.
            c = new Classification(row.ClassName, 1.0, "human", row.LectureTitle ?? "", JsonTopics(row.Topics));
        }
        else
        {
            // Sort on our notes when we have them: they are cleaner than the raw transcript. A lecture recorded with no
            // class has no folder: the AI reads it against each class's name, other names, what it covers and its
            // course's name on Canvas.
            var basis = summary.Length > 0 ? m.WithNotes(summary) : m;
            var classes = Classify.WithCanvas(Cfg.Classes, Cfg.Home);
            c = await Classify.ClassifyAsync(basis, Cfg, chat, classes);
            if (c.By is "folder" or "rules" && Cfg.OllamaEnabled && classes.Count > 0)
            {
                // The folder or title picked the class; still let the model name the lecture and tag its topics.
                var described = await Classify.WithOllamaAsync(basis, classes, Cfg, chat);
                if (described is not null)
                {
                    if (described.LectureTitle.Length > 0) c.LectureTitle = described.LectureTitle;
                    c.Topics = described.Topics;
                }
            }
        }
        // A title the student typed (on the recording, or renaming it since) is the lecture's: no AI names it over them.
        // Nor is one that was kept recording named again: it has the name its first notes were filed under.
        if (m.TitleStays) c.LectureTitle = !string.IsNullOrEmpty(row.LectureTitle) ? row.LectureTitle : m.Title;
        string? path = store.Finish(row, m, c, summary, model, error);
        if (path is not null && summary.Length > 0) store.MarkAttachmentsUsed(m.Attached.Select(a => a.Id).Concat(m.Photos.Select(p => p.Id)));
        log(path is null
            ? $"[pipeline] '{m.Title}' changed while it was being written; not saving the old result"
            : $"[pipeline] filed '{m.Title}' \u2192 {c.ClassName} ({c.By} {Py.FormatFixed(c.Confidence, 2)})");
        if (path is not null && filed is not null)
        {
            try
            {
                filed(row.Id);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // The notes are filed whatever becomes of what follows them.
                log($"[pipeline] '{m.Title}': couldn't queue what follows its notes ({e.Message})");
            }
        }
        return path;
    }

    /// <summary>An engine's own words as a sentence: a full stop is added when they end without one.</summary>
    static string Sentence(string text)
    {
        string t = text.TrimEnd();
        return t.Length > 0 && t[^1] is '.' or '!' or '?' ? t : t + ".";
    }

    static List<string> JsonTopics(string? json) =>
        (System.Text.Json.Nodes.JsonNode.Parse(string.IsNullOrEmpty(json) ? "[]" : json) as System.Text.Json.Nodes.JsonArray
         ?? new System.Text.Json.Nodes.JsonArray()).Select(Py.Str).ToList();

    public async Task<int> RunPendingAsync(CancellationToken stop = default)
    {
        int done = 0;
        while (!stop.IsCancellationRequested)
        {
            var row = store.ClaimNext();
            if (row is null) break;
            current = row.Id;
            try
            {
                await ProcessAsync(row); // clears engineProblem itself, as soon as the engine answers
                done++;
            }
            catch (SleptException e)
            {
                // Back in the queue: the next pass writes them now the computer's awake.
                store.ResetWorking();
                log($"[pipeline] '{row.Id}' waits: {e.Message} ({e.InnerException?.Message})");
                break;
            }
            catch (EngineOfflineException e)
            {
                // Back in the queue, with the rest waiting behind it: the next pass tries again.
                store.ResetWorking();
                engineProblem = $"{e.Engine} isn't answering on your library. New lectures wait and get their notes when it's back.";
                log($"[pipeline] {e.Message}: '{row.Id}' waits");
                break;
            }
            catch (Exception e)
            {
                store.MarkFailed(row.Id, e.Message);
                log($"[pipeline] could not file {row.Id}: {e.Message}\n{e}");
            }
            finally
            {
                current = null;
            }
        }
        return done;
    }

    /// <summary>A new lecture arrived: look at the queue now instead of within 30 seconds.</summary>
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

    public async Task RunForeverAsync(CancellationToken stop)
    {
        int n = store.ResetWorking();
        if (n > 0) log($"[pipeline] resuming {n} note(s) interrupted by a restart");
        while (!stop.IsCancellationRequested)
        {
            try
            {
                await RunPendingAsync(stop);
            }
            catch (Exception e)
            {
                log($"[pipeline] error: {e.Message}");
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

    public Task Start(CancellationToken stop) => Task.Run(() => RunForeverAsync(stop), CancellationToken.None);
}
