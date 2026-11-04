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
    public static IReadOnlyList<LibraryFolder>? ToAsk(AppHost host, Func<string?, List<LibraryFolder>>? find = null)
    {
        if (host.Settings.Role != AppRole.Both || !host.Settings.SetupDone) return null;
        if (LibraryHere.Existing(host.Home) is not { } cfg) return null;
        var found = (find ?? LibraryFolders.Find)(cfg.PoolDir);
        return LibraryFolders.ShouldAsk(found, host.Settings.NotesFoldersSeen, host.Settings.NotesFolderChosen) ? found : null;
    }

    /// <summary>
    /// The student picked <paramref name="picked"/> from <paramref name="shown"/>. When it isn't the folder in use,
    /// config.toml's pool_dir becomes it, this computer's library starts again to read it (as a new password does), and
    /// then lists the lectures in it state.db doesn't (a folder used before state.db was made afresh) and copies in the
    /// files of the lectures it lists from the other folders, so nothing it lists is read or written there any more. With
    /// <paramref name="bringIn"/>, the other folders' own notes are copied in too (<see cref="LibraryBringIn"/>). Not a
    /// file is moved or deleted. What that did, or why it couldn't; <paramref name="progress"/> hears how far it's got.
    /// The answer is remembered once the folder is in use. A config.toml that can't be written is said in plain words,
    /// and nothing is remembered then, so it's asked again.
    /// </summary>
    public static async Task<Outcome<List<BringInCount>>> UseAsync(AppHost host, LibraryFolder picked, IReadOnlyList<LibraryFolder> shown,
        bool bringIn = false, Action<int, int>? progress = null, CancellationToken ct = default)
    {
        if (!picked.InUse)
        {
            if (LibraryHere.Existing(host.Home) is not { } cfg) return Outcome<List<BringInCount>>.Failed(Failure.Offline);
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
        host.Save(s =>
        {
            s.NotesFoldersSeen = [.. shown.Select(f => f.Path)];
            s.NotesFolderChosen = picked.Path;
        });
        if (picked.InUse && !bringIn) return Outcome<List<BringInCount>>.Answered([]);
        var others = Others(picked, shown);
        var ask = new JsonObject
        {
            ["kind"] = BringInJob.Bring,
            ["from"] = Paths(bringIn ? others : []),
            ["refile"] = Paths(bringIn ? [] : others),
        };
        var done = await RunJobAsync(host, ask, progress, ct);
        if (done.Ok) host.Log($"[library] brought in: {string.Join("; ", done.Value!.Select(d => $"{d.New} new, {d.Here} here, {d.Both} both, {d.Deleted} deleted, {d.Files} files"))}");
        else host.Log($"[library] couldn't finish with the notes folder: {done.Failure!.Kind}");
        // The folder is in use either way; only bringing notes in is the student's to retry.
        return done.Ok || bringIn ? done : Outcome<List<BringInCount>>.Answered([]);
    }

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
        var started = await LibraryCall.Run(() => host.Remote()?.StartFolderJobAsync(ask, ct) ?? Task.FromResult<BringInStatus?>(null), ct);
        if (!started.Ok) return Outcome<List<BringInCount>>.Failed(started.Failure!);
        var last = started.Value!;
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
        if (lost is not null) return Outcome<List<BringInCount>>.Failed(lost);
        if (last.Error is { } why)
        {
            host.Log($"[library] bring-in stopped: {why}");
            return Outcome<List<BringInCount>>.Failed(Stopped);
        }
        return Outcome<List<BringInCount>>.Answered(last.Results);
    }

    static JsonArray Paths(IEnumerable<string> paths) => new([.. paths.Select(p => (JsonNode)p)]);

    static List<string> Others(LibraryFolder picked, IReadOnlyList<LibraryFolder> shown) =>
        [.. shown.Where(f => !LibraryFolders.Comparer.Equals(f.Path, picked.Path)).Select(f => f.Path)];
}
