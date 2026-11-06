using System.Text.Json;
using StudyStash.App.ViewModels.Kit;

namespace StudyStash.App.Services;

/// <summary>A hand-in ready to tidy: what the library saw, and what it found for it in the folders to look in.</summary>
public sealed record TidyItem(CanvasApi.HandInWaiting Waiting, CanvasApi.HandInCheck Check)
{
    public string Id => Waiting.Class + "\n" + Waiting.Key;

    /// <summary>The files a press of Tidy up moves as it stands: the file handed in when Syft Notes is sure it's the one
    /// (an unsure one counts once the student says Yes), and the files worked from that it's sure of.</summary>
    public int Files => (Check.File.Found == "exact" ? 1 : 0) + Check.Materials.Count(m => m.Sure == "high");
}

/// <summary>
/// Submit &amp; Organize's "Ready to tidy" (test builds only): the hand-ins waiting for the student's say, each with what
/// was found for it, kept current from the shared <see cref="CanvasWatch"/>. Home's list, the assignment page's button,
/// the notification and the tidy card all read this one list. A hand-in stays until it's tidied or dismissed ("Not this
/// one"); <see cref="Arrived"/> says once, ever, that one is new (the notification), remembered in
/// &lt;home&gt;/tidy-told.json so a restart doesn't say it again. Nothing here touches the UI thread.
/// </summary>
public sealed class TidyList : IDisposable
{
    readonly CanvasContext context;
    readonly CanvasWatch? watch;
    readonly Lock gate = new();
    readonly List<TidyItem> items = [];
    readonly HashSet<string> checking = [];
    /// <summary>The hand-ins told of, oldest first (the file keeps the last 200).</summary>
    List<string>? told;

    public TidyList(CanvasContext context, CanvasWatch? watch = null)
    {
        this.context = context;
        this.watch = watch;
        if (watch is not null) watch.Changed += OnWatchChanged;
    }

    public IReadOnlyList<TidyItem> Items
    {
        get
        {
            lock (gate) return [.. items];
        }
    }

    /// <summary>The list changed (off the UI thread).</summary>
    public event Action? Changed;

    /// <summary>A hand-in is ready to tidy for the first time ever (off the UI thread): the notification.</summary>
    public event Action<TidyItem>? Arrived;

    /// <summary>The hand-in waiting for this assignment, if one is.</summary>
    public TidyItem? For(string cls, string id) => Items.FirstOrDefault(i => i.Waiting.Class == cls && i.Waiting.Id == id);

    void OnWatchChanged()
    {
        if (watch?.State is { } state) UiTasks.RunQuietly(() => FollowAsync(state.HandedIn), "TidyList");
    }

    /// <summary>Follows what the library says waits: one gone from there leaves the list; a new one is checked (what was
    /// found for it), then joins it. A check that fails is tried again on the watch's next look.</summary>
    public async Task FollowAsync(IReadOnlyList<CanvasApi.HandInWaiting> waiting)
    {
        bool gone;
        lock (gate) gone = items.RemoveAll(i => !waiting.Any(w => w.Class == i.Waiting.Class && w.Key == i.Waiting.Key)) > 0;
        if (gone) Changed?.Invoke();
        foreach (var w in waiting)
        {
            string id = w.Class + "\n" + w.Key;
            lock (gate)
                if (items.Any(i => i.Id == id) || !checking.Add(id)) continue;
            try
            {
                if (context.Client is not { } client) continue;
                if ((await LibraryCall.Run(() => client.CheckHandInAsync(w.Class, w.Key))).Value is not { } check) continue;
                var item = new TidyItem(w, check);
                lock (gate) items.Add(item);
                Changed?.Invoke();
                if (Tell(id)) Arrived?.Invoke(item);
            }
            finally
            {
                lock (gate) checking.Remove(id);
            }
        }
    }

    /// <summary>"Not this one": it leaves the list here and in the library; nothing of it moves.</summary>
    public async Task<Outcome> DismissAsync(TidyItem item)
    {
        if (context.Client is not { } client) return Outcome.Failed(Failure.Offline);
        var done = await LibraryCall.Run(() => client.DismissHandInAsync(item.Waiting.Class, item.Waiting.Key));
        if (done.Ok) Tidied(item);
        return done;
    }

    /// <summary>It was tidied (the library took it off its list too).</summary>
    public void Tidied(TidyItem item)
    {
        bool removed;
        lock (gate) removed = items.RemoveAll(i => i.Id == item.Id) > 0;
        if (removed) Changed?.Invoke();
    }

    string ToldPath => Path.Combine(context.Home, "tidy-told.json");

    /// <summary>True the first time a hand-in is told of, ever; false after.</summary>
    bool Tell(string id)
    {
        lock (gate)
        {
            if (told is null)
            {
                try
                {
                    told = File.Exists(ToldPath) ? JsonSerializer.Deserialize<List<string>>(File.ReadAllText(ToldPath)) ?? [] : [];
                }
                catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
                {
                    told = [];
                }
            }
            if (told.Contains(id)) return false;
            told.Add(id);
            if (told.Count > 200) told.RemoveRange(0, told.Count - 200);
            try
            {
                if (context.Home.Length > 0) File.WriteAllText(ToldPath, JsonSerializer.Serialize(told));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Not kept: at worst it's said again after a restart.
            }
            return true;
        }
    }

    public void Dispose()
    {
        if (watch is not null) watch.Changed -= OnWatchChanged;
    }
}
