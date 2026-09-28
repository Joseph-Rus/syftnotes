namespace StudyStash.Core.Calendar;

/// <summary>
/// Sends the laptop's week of calendar events to its library (POST /api/v2/calendar/upcoming) after each read, so the
/// library's pages and the phone can show what's coming up. The same week isn't sent again for half an hour; a
/// library that can't be reached (or is too old to take it) is tried at the next read.
/// </summary>
public sealed class UpcomingSender(Func<IReadOnlyList<CalendarEvent>> events, Func<ClientConfig> config, LaptopHost host,
    Func<IReadOnlyList<ClassHint>> classes, Action<string>? log = null)
{
    /// <summary>A week sent unchanged is sent again after this, so the library's "updated" stays recent.</summary>
    public static readonly TimeSpan Resend = TimeSpan.FromMinutes(30);

    readonly Action<string> log = log ?? (_ => { });
    readonly SemaphoreSlim sending = new(1, 1);
    string? lastBody;
    DateTimeOffset lastAt;
    string? lastProblem;

    /// <summary>Send the week now if it changed (or it's been <see cref="Resend"/>); true when it was sent.</summary>
    public async Task<bool> SendAsync()
    {
        var cc = config();
        if (cc.ServerUrl.Length == 0) return false;
        await sending.WaitAsync();
        try
        {
            var now = host.Clock();
            string body = LibraryCalendar.Body(events().Where(e => e.End > now && e.Start < now + Upcoming.Ahead), classes()).ToJsonString();
            if (body == lastBody && now - lastAt < Resend) return false;
            try
            {
                await host.Post($"{cc.ServerUrl.TrimEnd('/')}/api/v2/calendar/upcoming", body, cc.PoolKey);
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or LibraryRefusedException)
            {
                // Said once, not every five minutes.
                if (lastProblem != e.Message) log($"[calendar] couldn't send what's coming up to the library: {e.Message}");
                lastProblem = e.Message;
                return false;
            }
            lastBody = body;
            lastAt = now;
            lastProblem = null;
            return true;
        }
        finally
        {
            sending.Release();
        }
    }
}
