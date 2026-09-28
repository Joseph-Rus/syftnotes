using System.Globalization;

namespace StudyStash.App.Services;

/// <summary>
/// What the app shows from Canvas, kept current: what's due (the sidebar's Due and its count, the Due page, the
/// dropdown's next due, the quick panel's Canvas rows) and every class with its course (a linked class's own page).
/// Follows the shared <see cref="CanvasWatch"/>: whenever the library says Canvas changed — a sync finished, a class
/// finished syncing, a course was linked or chosen — it reads both again and raises <see cref="Updated"/>, so an
/// open page refreshes by itself. A read that fails is tried again on the watch's next look, never given up on.
/// Nothing here touches the UI thread.
/// </summary>
public sealed class CanvasFeed : IDisposable
{
    readonly CanvasContext context;
    readonly CanvasWatch? watch;
    /// <summary>The <see cref="Revision"/> the data here was read at; null until a read after the watch's look worked.</summary>
    string? loadedAt;
    int catchingUp;

    public CanvasFeed(CanvasContext context, CanvasWatch? watch = null)
    {
        this.context = context;
        this.watch = watch;
        if (watch is not null) watch.Changed += OnWatchChanged;
    }

    /// <summary>What's due, as last read; null before the first read that worked.</summary>
    public CanvasApi.DueResponse? Due { get; private set; }

    /// <summary>Every class with its course, as last read.</summary>
    public IReadOnlyList<CanvasApi.ClassRow> Classes { get; private set; } = [];

    /// <summary>At least one class is linked to a Canvas course.</summary>
    public bool Linked => Classes.Any(c => c.Linked);

    /// <summary>The class's row when it's linked to a course; null otherwise.</summary>
    public CanvasApi.ClassRow? LinkedClass(string? name) => name is null ? null : Classes.FirstOrDefault(c => c.Linked && c.Class == name);

    /// <summary>Raised (off the UI thread) after the watch's look brought in newer data.</summary>
    public event Action? Updated;

    /// <summary>What the state says about Canvas's data: the library's own revision (a sync finished, a course was
    /// linked or chosen) or, from an older library, when it last synced; and how many classes a running sync has left,
    /// so each class that finishes shows without waiting for the rest.</summary>
    public static string Revision(CanvasApi.State s) =>
        (s.Revision ?? s.LastSync?.ToString("o", CultureInfo.InvariantCulture) ?? "") + "|" + (s.Syncing?.Left.ToString(CultureInfo.InvariantCulture) ?? "");

    /// <summary>The library has something newer than what's here (or nothing has been read yet).</summary>
    public bool Stale(CanvasApi.State s) => Due is null || loadedAt != Revision(s);

    void OnWatchChanged()
    {
        if (watch?.State is { } s && Stale(s)) _ = CatchUpAsync(s);
    }

    /// <summary>Reads again if <paramref name="s"/> says there's something newer, and raises <see cref="Updated"/>
    /// when that worked. One at a time: a look that comes while a read is running is caught by the next one.</summary>
    public async Task<bool> CatchUpAsync(CanvasApi.State s, CancellationToken stop = default)
    {
        if (!Stale(s) || Interlocked.Exchange(ref catchingUp, 1) == 1) return false;
        try
        {
            string revision = Revision(s);
            if (!await LoadAsync(stop)) return false;
            loadedAt = revision;
            // Still inside the one-at-a-time: two updates never reach a screen at once.
            Updated?.Invoke();
            return true;
        }
        finally
        {
            Volatile.Write(ref catchingUp, 0);
        }
    }

    /// <summary>Reads what's due and every class now. False when it couldn't (no library, an older one, or no
    /// answer), keeping what was known.</summary>
    public async Task<bool> LoadAsync(CancellationToken stop = default)
    {
        if (context.Client is not { } client) return false;
        try
        {
            var dueTask = client.DueAsync(stop);
            var classesTask = client.ClassesAsync(stop);
            await Task.WhenAll(dueTask, classesTask);
            if (await dueTask is not { } due) return false;
            Classes = await classesTask ?? [];
            Due = due;
            return true;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or CanvasLibraryException or System.Text.Json.JsonException or InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>Stops following the watch (the app moved to another library): the watch may outlive this feed.</summary>
    public void Dispose()
    {
        if (watch is not null) watch.Changed -= OnWatchChanged;
    }
}
