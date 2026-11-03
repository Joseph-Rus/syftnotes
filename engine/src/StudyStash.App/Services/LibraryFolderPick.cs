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
    /// The student picked <paramref name="picked"/> from <paramref name="shown"/>: the answer is remembered, and when
    /// it isn't the folder in use, config.toml's pool_dir becomes it, this computer's library starts again to read it
    /// (as a new password does), and lists the lectures in it state.db doesn't (a folder used before state.db was made
    /// afresh). With <paramref name="bringIn"/>, the other folders' notes are then copied in (<see cref="LibraryBringIn"/>):
    /// what that did. Not a file is moved or deleted. A config.toml that can't be written is said in plain words, and
    /// nothing is remembered then, so it's asked again.
    /// </summary>
    public static async Task<Outcome<List<BringInCount>>> UseAsync(AppHost host, LibraryFolder picked, IReadOnlyList<LibraryFolder> shown,
        bool bringIn = false)
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
        var others = bringIn ? Others(picked, shown) : [];
        var done = await LibraryCall.Run(() => host.Remote()?.BringInAsync(others) ?? Task.FromResult<List<BringInCount>?>(null));
        if (done.Ok) host.Log($"[library] brought in: {string.Join("; ", done.Value!.Select(d => $"{d.New} new, {d.Here} here, {d.Both} both, {d.Deleted} deleted"))}");
        else host.Log($"[library] couldn't list the notes folder's lectures: {done.Failure!.Kind}");
        // The folder is in use either way; only bringing notes in is the student's to retry.
        return done.Ok || bringIn ? done : Outcome<List<BringInCount>>.Answered([]);
    }

    /// <summary>What bringing in the other folders' notes would do, with <paramref name="picked"/> as the folder the
    /// library uses: one count per other folder. Changes nothing.</summary>
    public static Task<Outcome<List<BringInCount>>> PlanAsync(AppHost host, LibraryFolder picked, IReadOnlyList<LibraryFolder> shown) =>
        LibraryCall.Run(() => host.Remote()?.BringInPlanAsync(picked.Path, Others(picked, shown)) ?? Task.FromResult<List<BringInCount>?>(null));

    static List<string> Others(LibraryFolder picked, IReadOnlyList<LibraryFolder> shown) =>
        [.. shown.Where(f => !LibraryFolders.Comparer.Equals(f.Path, picked.Path)).Select(f => f.Path)];
}
