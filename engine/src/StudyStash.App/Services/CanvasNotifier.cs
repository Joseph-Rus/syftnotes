using System.Text.Json;
using StudyStash.App.ViewModels;

namespace StudyStash.App.Services;

/// <summary>
/// Watches for new Canvas notifications and turns them into toasts (design 12). The very first poll a laptop ever
/// makes only records where the library's list currently ends — a fresh install shouldn't dump every notification
/// Canvas has ever produced as toasts — and every poll after that turns what's new since the last one (and not seen
/// already, somewhere else) into up to three toasts, the most recent one <see cref="CanvasToastModel.Expanded"/>. Remembers the bookmark in
/// <c>&lt;home&gt;/canvas-notified.json</c> so a restart carries on rather than starting over.
/// </summary>
public sealed class CanvasNotifier(CanvasContext context)
{
    long? after;
    bool loaded;

    /// <summary>The host's own reaction to Open (e.g. navigate to the assignment or announcement); every toast also
    /// marks itself seen once opened, whatever this does.</summary>
    public Action<CanvasApi.NotificationRow>? OnOpen { get; set; }

    string StorePath => Path.Combine(context.Home, "canvas-notified.json");

    void Load()
    {
        if (loaded) return;
        loaded = true;
        try
        {
            if (File.Exists(StorePath)) after = JsonSerializer.Deserialize<Stored>(File.ReadAllText(StorePath))?.After;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
        }
    }

    void Save()
    {
        try
        {
            if (context.Home.Length > 0) File.WriteAllText(StorePath, JsonSerializer.Serialize(new Stored(after)));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    sealed record Stored(long? After);

    /// <summary>Asks the library what's new since the last poll. No library yet, or the library doesn't answer, or
    /// this is the very first poll ever: no toasts (the first case just quietly records the bookmark).</summary>
    public async Task<IReadOnlyList<CanvasToastModel>> PollAsync(CancellationToken stop = default)
    {
        Load();
        if (context.Client is not { } client) return [];
        bool firstRun = after is null;
        var resp = await client.NotificationsAsync(after, stop);
        if (resp is null) return [];
        // The library's list ends before the bookmark: it isn't the list the bookmark was for (another library, or one
        // started afresh). Like a first poll, that only marks where it ends now: none of it is new to this laptop.
        bool startedOver = after is { } mark && resp.Last is { } last && last < mark;
        after = resp.Last ?? after;
        Save();
        if (firstRun || startedOver) return [];

        var zone = context.Clock.Zone;
        var now = context.Clock.Now();
        // The library lists oldest first; the most recently posted is the one we make prominent, at most three. One
        // already seen (opened on the library's page, or on another laptop) isn't news here.
        var newest = resp.Items.Where(n => !n.Seen).TakeLast(3).Reverse().ToList();
        var toasts = new List<CanvasToastModel>(newest.Count);
        for (int i = 0; i < newest.Count; i++)
        {
            var toast = new CanvasToastModel(newest[i]) { When = CanvasWords.NotificationAge(newest[i].At, zone, now), Expanded = i == 0 };
            toast.OnOpen = async n =>
            {
                OnOpen?.Invoke(n);
                await SeenAsync(n);
            };
            toast.OnDismiss = SeenAsync;
            toasts.Add(toast);
        }
        return toasts;
    }

    Task SeenAsync(CanvasApi.NotificationRow item) => context.Client?.MarkNotificationsSeenAsync(item.Id) ?? Task.CompletedTask;
}
