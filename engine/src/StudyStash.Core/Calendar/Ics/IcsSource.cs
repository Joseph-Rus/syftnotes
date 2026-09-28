using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace StudyStash.Core.Calendar.Ics;

/// <summary>A feed couldn't be read: what to tell the student, in their words.</summary>
public sealed class CalendarFeedException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// An iCalendar feed the student pasted into Settings (webcal:// or https://): Google's "secret address", an iCloud
/// public calendar, a school timetable, Canvas's calendar feed. One feed is one calendar. It's downloaded at most once
/// a minute however often it's asked, since listing calendars and reading events come together.
/// </summary>
public sealed class IcsSource(CalendarSourceSettings settings, HttpClient http, TimeZoneInfo? local = null, Func<DateTimeOffset>? clock = null) : ICalendarSource
{
    public const string KindName = "ics";

    /// <summary>The biggest feed read: a school's whole timetable is well under this.</summary>
    public const int MaxBytes = 20 * 1024 * 1024;

    static readonly TimeSpan Fresh = TimeSpan.FromMinutes(1);
    readonly Func<DateTimeOffset> clock = clock ?? (() => DateTimeOffset.UtcNow);
    readonly SemaphoreSlim gate = new(1, 1);
    (IcsFeed Feed, DateTimeOffset At)? last;

    public string Id => settings.Id;
    public string Kind => KindName;
    public string Name => settings.Name;

    /// <summary>The address as downloaded: webcal:// and webcals:// are https://.</summary>
    public static string FetchUrl(string url)
    {
        string u = url.Trim();
        foreach (string scheme in new[] { "webcals://", "webcal://" })
            if (u.StartsWith(scheme, StringComparison.OrdinalIgnoreCase)) return "https://" + u[scheme.Length..];
        return u;
    }

    /// <summary>A pasted address, tidied ("calendar.google.com/…" gets its https://); null when it isn't a web address.</summary>
    public static string? CleanUrl(string typed)
    {
        string t = typed.Trim();
        if (t.Length == 0 || t.Any(char.IsWhiteSpace)) return null;
        if (!t.Contains("://", StringComparison.Ordinal)) t = "https://" + t;
        return Uri.TryCreate(FetchUrl(t), UriKind.Absolute, out var u) && u.Scheme is "https" or "http" && u.Host.Contains('.') ? t : null;
    }

    /// <summary>"ics:" and a short fingerprint of the address, so one feed added twice is one source.</summary>
    public static string IdFor(string url) =>
        "ics:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(FetchUrl(url).ToLowerInvariant())))[..12];

    /// <summary>A new feed source's settings, named by the feed (or its web address when it has no name).</summary>
    public static CalendarSourceSettings New(string url, string? name, DateTimeOffset now) => new()
    {
        Id = IdFor(url), Kind = KindName, Url = url,
        Name = name ?? (Uri.TryCreate(FetchUrl(url), UriKind.Absolute, out var u) ? u.Host : "Calendar feed"),
        Added = now.ToString("o", System.Globalization.CultureInfo.InvariantCulture),
    };

    /// <summary>Download and read the feed (or the copy from the last minute).</summary>
    public async Task<IcsFeed> FeedAsync(CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            if (last is { } l && clock() - l.At < Fresh) return l.Feed;
            var feed = await DownloadAsync(settings.Url, http, local, ct);
            last = (feed, clock());
            return feed;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Download a feed and read it. Throws <see cref="CalendarFeedException"/> with words for the student.</summary>
    public static async Task<IcsFeed> DownloadAsync(string url, HttpClient http, TimeZoneInfo? local, CancellationToken ct)
    {
        string text;
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(30));
            using var request = new HttpRequestMessage(HttpMethod.Get, FetchUrl(url));
            request.Headers.TryAddWithoutValidation("Accept", "text/calendar, */*");
            using var r = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            if (r.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                throw new CalendarFeedException("That calendar isn't public. Copy its secret or public address instead.");
            if (r.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
                throw new CalendarFeedException("That calendar isn't there any more. Check its address.");
            if (!r.IsSuccessStatusCode)
                throw new CalendarFeedException($"The calendar's server didn't answer ({(int)r.StatusCode}). Study Stash tries again soon.");
            if (r.Content.Headers.ContentLength is > MaxBytes) throw new CalendarFeedException("That calendar is too big to read.");
            await using var stream = await r.Content.ReadAsStreamAsync(cts.Token);
            using var buffer = new MemoryStream();
            var chunk = new byte[81920];
            int n;
            while ((n = await stream.ReadAsync(chunk, cts.Token)) > 0)
            {
                buffer.Write(chunk, 0, n);
                if (buffer.Length > MaxBytes) throw new CalendarFeedException("That calendar is too big to read.");
            }
            text = Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            throw new CalendarFeedException("Couldn't reach that calendar. Check the address and your internet.", e);
        }
        var feed = IcsFeed.Parse(text, local);
        if (!feed.IsCalendar) throw new CalendarFeedException("That address isn't a calendar feed. Look for one ending in .ics, or starting webcal://.");
        return feed;
    }

    public async Task<IReadOnlyList<CalendarInfo>> ListCalendarsAsync(CancellationToken ct)
    {
        var feed = await FeedAsync(ct);
        return [new CalendarInfo(Id, Id, feed.Name ?? Name, feed.Color, true)];
    }

    public async Task<IReadOnlyList<CalendarEvent>> EventsAsync(IReadOnlyCollection<string> calendarIds, DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        if (!calendarIds.Contains(Id)) return [];
        return (await FeedAsync(ct)).Events(Id, Id, from, to);
    }
}
