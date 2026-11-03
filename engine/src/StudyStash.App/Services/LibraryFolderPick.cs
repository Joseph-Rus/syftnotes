using StudyStash.Core;

namespace StudyStash.App.Services;

/// <summary>
/// "Which notes folder should Syft Notes use?": when this computer has two or more library folders (Documents moved
/// to OneDrive, or from a personal OneDrive to a school one, and a library was set up afresh in the new Documents), the
/// student picks the one the library keeps its notes in. Asked at start, in the one-laptop setup, only when
/// <see cref="LibraryFolders.ShouldAsk"/> says so; the answer is remembered with the folders it was given for. Nothing
/// is moved, merged or deleted: the pick only changes config.toml's pool_dir, and the library starts again to use it.
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
    /// it isn't the folder in use, config.toml's pool_dir becomes it and this computer's library starts again to read
    /// it (as a new password does). Not a file is moved. A config.toml that can't be written is said in plain words, and
    /// nothing is remembered then, so it's asked again.
    /// </summary>
    public static async Task<Outcome> UseAsync(AppHost host, LibraryFolder picked, IReadOnlyList<LibraryFolder> shown)
    {
        if (!picked.InUse)
        {
            if (LibraryHere.Existing(host.Home) is not { } cfg) return Outcome.Failed(Failure.Offline);
            string was = cfg.PoolDir;
            cfg.PoolDir = Py.NormPath(picked.Path);
            try
            {
                Configs.Save(cfg);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                host.Log($"[library] couldn't switch the notes folder: {e.Message}");
                return Outcome.Failed(new Failure(FailureKind.Refused, "Syft Notes couldn't save that. Check this computer has free space, then try again."));
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
        return Outcome.Done;
    }
}
