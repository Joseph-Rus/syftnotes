using System.Text.Json.Nodes;
using StudyStash.App.Services.Kit;
using StudyStash.Core;

namespace StudyStash.App.Services;

/// <summary>
/// "You have Syft Notes folders in two places": when this computer has two or more library folders (Documents moved
/// to OneDrive, or from a personal OneDrive to a school one, and a library was set up afresh in the new Documents), the
/// student picks the one the library keeps its notes in, and can bring the others' notes into it
/// (<see cref="LibraryBringIn"/>, which the library does). Asked at start, in the one-laptop setup, only when
/// <see cref="LibraryFolders.ShouldAsk"/> says so; the answer is remembered with the folders it was given for. Nothing
/// is moved or deleted: the pick changes config.toml's pool_dir and the library starts again to use it; bringing notes
/// in copies them, and the other folders are only read.
/// </summary>
public static class LibraryFolderPick
{
    /// <summary>The folders to ask about, or null when there's nothing to ask: not the one-laptop setup, setup not
    /// done, no library here, fewer than two library folders, or ones the student has answered for. Looks at the disk:
    /// call it off the UI thread.</summary>
    public static IReadOnlyList<LibraryFolder>? ToAsk(AppHost host, Func<string?, List<LibraryFolder>>? find = null) =>
        Look(host, find) is (AtStart.Ask, var found) ? found : null;

    /// <summary>What to do about library folders when the app starts or setup ends.</summary>
    public enum AtStart
    {
        /// <summary>Not the one-laptop setup, setup not done, or no library here.</summary>
        Nothing,
        /// <summary>Two or more folders hold notes, and the student hasn't answered for them: ask which.</summary>
        Ask,
        /// <summary>One folder holds notes and it isn't the one in use (an earlier install's, or Documents moved): it's
        /// brought in without a question, once.</summary>
        BringIn,
        /// <summary>Otherwise: the folder in use's own note files that state.db doesn't list are listed (quick when
        /// there are none).</summary>
        ListOwn,
    }

    /// <summary>What to do (<see cref="AtStart"/>), with the library folders found. Looks at the disk: call it off the UI
    /// thread.</summary>
    public static (AtStart Step, IReadOnlyList<LibraryFolder> Found) Look(AppHost host, Func<string?, List<LibraryFolder>>? find = null)
    {
        if (host.Settings.Role != AppRole.Both || !host.Settings.SetupDone) return (AtStart.Nothing, Array.Empty<LibraryFolder>());
        if (LibraryHere.Existing(host.Home) is not { } cfg) return (AtStart.Nothing, Array.Empty<LibraryFolder>());
        var found = (find ?? LibraryFolders.Find)(cfg.PoolDir);
        if (found.Count >= 2)
            return (LibraryFolders.ShouldAsk(found, host.Settings.NotesFoldersSeen, host.Settings.NotesFolderChosen) ? AtStart.Ask : AtStart.ListOwn, found);
        if (found is [{ InUse: false } old] && host.Settings.NotesFoldersSeen?.Contains(old.Path, LibraryFolders.Comparer) != true)
            return (AtStart.BringIn, found);
        return (AtStart.ListOwn, found);
    }

    /// <summary>
    /// <see cref="AtStart.BringIn"/> or <see cref="AtStart.ListOwn"/>, once the library answers (it starts with the
    /// app): the library brings the one other folder's notes in (copied; that folder is only read), or lists its own
    /// folder's unlisted note files. A bring-in that worked is remembered with both folders, so it's done once and the
    /// question doesn't come up for them later. What it did, or why it couldn't.
    /// </summary>
    public static async Task<Outcome<List<BringInCount>>> AtStartAsync(AppHost host, AtStart step, IReadOnlyList<LibraryFolder> found,
        CancellationToken ct = default)
    {
        if (!await Poll.UntilAsync(() => host.LocalLibrary?.State is LibraryServiceState.Running or LibraryServiceState.Elsewhere,
                TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(2), ct))
            return Outcome<List<BringInCount>>.Failed(Failure.Offline);
        IEnumerable<string> from = step == AtStart.BringIn ? found.Select(f => f.Path) : Array.Empty<string>();
        var done = await RunJobAsync(host, new JsonObject { ["kind"] = BringInJob.Bring, ["from"] = Paths(from), ["refile"] = Paths([]) }, null, ct);
        if (!done.Ok)
        {
            host.Log($"[library] couldn't look for notes at start: {done.Failure!.Kind}");
            return done;
        }
        host.Log($"[library] at start ({step}): {string.Join("; ", done.Value!.Select(d => $"{d.New} new, {d.Both} both, {d.Files} files"))}");
        if (step == AtStart.BringIn && LibraryHere.Existing(host.Home) is { } cfg)
            host.Save(s =>
            {
                s.NotesFoldersSeen = [.. found.Select(f => f.Path), cfg.PoolDir];
                s.NotesFolderChosen = cfg.PoolDir;
            });
        return done;
    }

    /// <summary>What a start's bring-in says on a notice, or null when nothing came in. The words wait for the owner's OK.</summary>
    public static string? StartWords(AtStart step, IReadOnlyList<BringInCount> done)
    {
        int n = done.Sum(d => d.New + d.Both);
        if (n == 0) return null;
        string lectures = n == 1 ? "1 lecture" : $"{n} lectures";
        return step == AtStart.BringIn ? $"Brought in {lectures} from your earlier Syft Notes folder." : $"Found {lectures} in your Syft Notes folder.";
    }

    /// <summary>
    /// The student picked <paramref name="picked"/> from <paramref name="shown"/>. When it isn't the folder in use,
    /// config.toml's pool_dir becomes it and this computer's library starts again to read it (as a new password does).
    /// Then the library lists the lectures in it state.db doesn't (a folder used before state.db was made afresh), and
    /// copies in what it reads from the other folders: the files of the lectures it lists there, the trash's, and the
    /// Inbox's captures and class notes (<see cref="LibraryBringIn.Refile"/>), so nothing it shows is read or written
    /// there any more. With <paramref name="bringIn"/>, the other folders' own notes come in too
    /// (<see cref="LibraryBringIn.Apply"/>). Not a file is moved or deleted. <paramref name="progress"/> hears how far
    /// it's got. The answer is remembered only once all of that is done; until then the question comes back at the next
    /// start, and using the folder in use finishes the copying. A config.toml that can't be written is said in plain
    /// words.
    /// </summary>
    public static async Task<Outcome<List<BringInCount>>> UseAsync(AppHost host, LibraryFolder picked, IReadOnlyList<LibraryFolder> shown,
        bool bringIn = false, Action<int, int>? progress = null, CancellationToken ct = default)
    {
        if (LibraryHere.Existing(host.Home) is not { } cfg) return Outcome<List<BringInCount>>.Failed(Failure.Offline);
        // Which folder is in use is config.toml's, now: the window's "In use now" can be from before a switch that
        // didn't finish.
        bool inUse = IsInUse(cfg.PoolDir, picked.Path);
        if (!inUse)
        {
            string was = cfg.PoolDir;
            cfg.PoolDir = Py.NormPath(picked.Path);
            try
            {
                Configs.Save(cfg);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                host.Log($"[library] couldn't switch the notes folder: {e.Message}");
                return Outcome<List<BringInCount>>.Failed(new Failure(FailureKind.Refused, "Syft Notes couldn't save that. Check this computer has free space, then try again."));
            }
            host.Log($"[library] notes folder: {was} → {cfg.PoolDir} (the student's pick; nothing moved)");
            if (host.LocalLibrary is { } svc)
            {
                svc.Cfg.PoolDir = cfg.PoolDir;
                await svc.RestartAsync();
                await host.CheckLibraryAsync();
            }
        }
        // Then the lectures the library lists from the other folders come in, and with bringIn, the rest of their notes.
        // Using the folder in use runs it too: a switch whose copying didn't finish is finished then.
        var others = Others(picked, shown);
        var ask = new JsonObject
        {
            ["kind"] = BringInJob.Bring,
            ["from"] = Paths(bringIn ? others : []),
            ["refile"] = Paths(bringIn ? [] : others),
        };
        var done = await RunJobAsync(host, ask, progress, ct);
        if (!done.Ok)
        {
            // The answer isn't remembered, so the question comes back at the next start until the copying is done.
            host.Log($"[library] couldn't finish with the notes folder: {done.Failure!.Kind}");
            return bringIn || inUse ? done : Outcome<List<BringInCount>>.Failed(NotFinished(others.Count));
        }
        host.Log($"[library] brought in: {string.Join("; ", done.Value!.Select(d => $"{d.New} new, {d.Here} here, {d.Both} both, {d.Deleted} deleted, {d.Files} files"))}");
        host.Save(s =>
        {
            s.NotesFoldersSeen = [.. shown.Select(f => f.Path)];
            s.NotesFolderChosen = picked.Path;
        });
        return done;
    }

    /// <summary>Whether <paramref name="folder"/> is <paramref name="poolDir"/> (config.toml's), however each is written.</summary>
    public static bool IsInUse(string? poolDir, string folder) =>
        poolDir is { Length: > 0 } && LibraryFolders.Comparer.Equals(LibraryFolders.Real(poolDir), LibraryFolders.Real(folder));

    /// <summary>The folder config.toml says the library uses now; null when there's no library here.</summary>
    public static string? FolderInUse(AppHost host) => LibraryHere.Existing(host.Home)?.PoolDir;

    /// <summary>The folder was switched, but the lectures from the other one couldn't be copied in yet.</summary>
    public static Failure NotFinished(int others) => new(FailureKind.Refused,
        $"Syft Notes uses this folder now, but couldn't copy your lectures over from {(others > 1 ? "the others" : "the other one")} yet. Try again.");

    /// <summary>What bringing in the other folders' notes would do, with <paramref name="picked"/> as the folder the
    /// library uses: one count per other folder. Changes nothing.</summary>
    public static Task<Outcome<List<BringInCount>>> PlanAsync(AppHost host, LibraryFolder picked, IReadOnlyList<LibraryFolder> shown,
        Action<int, int>? progress = null, CancellationToken ct = default) =>
        RunJobAsync(host, new JsonObject { ["kind"] = BringInJob.Look, ["into"] = picked.Path, ["from"] = Paths(Others(picked, shown)) }, progress, ct);

    /// <summary>What a job that stopped part-way says: what came in stays, and doing it again brings only the rest.</summary>
    public static readonly Failure Stopped = new(FailureKind.Refused,
        "Syft Notes stopped before it finished. Try again: nothing comes in twice.");

    /// <summary>Starts the library's bring-in job (<see cref="LibraryBringIn.Route"/>) and asks after it each second until
    /// it ends, telling <paramref name="progress"/> how far it's got: its results, or why it didn't finish. A library
    /// that stops answering for half a minute, or starts again (the job is gone), ends the wait.</summary>
    static async Task<Outcome<List<BringInCount>>> RunJobAsync(AppHost host, JsonObject ask, Action<int, int>? progress, CancellationToken ct)
    {
        // The library runs one job at a time: one that isn't ours (another look, still going) is waited out, then ours
        // is asked for again.
        string mine = Guid.NewGuid().ToString("N");
        ask["asked"] = mine;
        BringInStatus? last = null;
        for (int tries = 0; tries < 5 && last is null; tries++)
        {
            var started = await LibraryCall.Run(() => host.Remote()?.StartFolderJobAsync(ask, ct) ?? Task.FromResult<BringInStatus?>(null), ct);
            if (!started.Ok) return Outcome<List<BringInCount>>.Failed(started.Failure!);
            if (started.Value!.Asked == mine)
            {
                last = started.Value;
                break;
            }
            var theirs = await FollowAsync(host, started.Value, null, ct);
            if (theirs.Failure?.Kind == FailureKind.Offline) return Outcome<List<BringInCount>>.Failed(theirs.Failure);
        }
        if (last is null) return Outcome<List<BringInCount>>.Failed(Stopped);
        var ours = await FollowAsync(host, last, progress, ct);
        if (!ours.Ok) return Outcome<List<BringInCount>>.Failed(ours.Failure!);
        last = ours.Value!;
        if (last.Error is { } why)
        {
            host.Log($"[library] bring-in stopped: {why}");
            return Outcome<List<BringInCount>>.Failed(Stopped);
        }
        return Outcome<List<BringInCount>>.Answered(last.Results);
    }

    /// <summary>Asks after the job <paramref name="job"/> each second until it ends: how it ended, or why the wait did.</summary>
    static async Task<Outcome<BringInStatus>> FollowAsync(AppHost host, BringInStatus job, Action<int, int>? progress, CancellationToken ct)
    {
        var last = job;
        Failure? lost = null;
        int quiet = 0;
        await Poll.UntilAsync(async c =>
        {
            if (!last.Running) return true;
            var now = await LibraryCall.Run(() => host.Remote()?.FolderJobAsync(c) ?? Task.FromResult<BringInStatus?>(null), c);
            if (now.Failure?.Kind == FailureKind.Offline && ++quiet < 30) return false;
            if (!now.Ok || now.Value!.Id != last.Id)
            {
                lost = now.Failure?.Kind == FailureKind.Offline ? now.Failure : Stopped;
                return true;
            }
            quiet = 0;
            last = now.Value;
            progress?.Invoke(last.Done, last.Total);
            return !last.Running;
        }, TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan, ct);
        return lost is null ? Outcome<BringInStatus>.Answered(last) : Outcome<BringInStatus>.Failed(lost);
    }

    static JsonArray Paths(IEnumerable<string> paths) => new([.. paths.Select(p => (JsonNode)p)]);

    static List<string> Others(LibraryFolder picked, IReadOnlyList<LibraryFolder> shown) =>
        [.. shown.Where(f => !LibraryFolders.Comparer.Equals(f.Path, picked.Path)).Select(f => f.Path)];
}
