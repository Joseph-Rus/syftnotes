using System.Globalization;
using StudyStash.Core.Calendar;

namespace StudyStash.App.ViewModels;

/// <summary>What calendars say to the student: Settings → Calendars, the Coming up list, and the line under Record.</summary>
public static class CalendarWords
{
    public const string Title = "Calendars";
    public const string Lede = "Study Stash reads your calendars to show what's coming up, and names a lecture you record during a class after it.";
    public const string AddFeedTitle = "Add a calendar feed";
    public const string AddFeedLine = "Paste a calendar's address: Google's secret address in iCal format, an iCloud public calendar, your school's timetable, or Canvas's calendar feed. It starts with webcal:// or https://.";
    public const string FeedPlaceholder = "webcal://… or https://….ics";
    public const string NotAnAddress = "That isn't a web address. Copy the whole address, starting webcal:// or https://.";
    public const string AlreadyAdded = "That calendar is already here.";
    public const string Checking = "Reading the calendar…";
    public const string NoSources = "No calendars yet. Add a feed above to see what's coming up.";
    public const string ConnectTitle = "Connect a calendar";
    public const string MoreComingSoon = "Apple Calendar, Google and Outlook are coming soon; for now, paste a feed's address.";
    public const string ComingUp = "Coming up";
    public const string NothingComingUp = "Nothing else today or tomorrow.";

    /// <summary>"Added School timetable." after a feed is added.</summary>
    public static string Added(string name) => $"Added {name}.";

    /// <summary>A source's line under its name: how many calendars, or what went wrong.</summary>
    public static string SourceLine(int calendars, string? problem) =>
        problem ?? (calendars == 1 ? "1 calendar" : $"{calendars} calendars");

    /// <summary>The line under Record while an event is on: "From your calendar: CS 101 Lecture".</summary>
    public static string FromCalendar(string title) => $"From your calendar: {title}";

    /// <summary>When an event in Coming up is: "Now, until 11:15 AM", "10:00 AM", "Tomorrow 9:00 AM".</summary>
    public static string When(CalendarEvent e, DateTimeOffset now, TimeZoneInfo zone)
    {
        var start = TimeZoneInfo.ConvertTime(e.Start, zone);
        var end = TimeZoneInfo.ConvertTime(e.End, zone);
        string Clock(DateTimeOffset t) => t.ToString("h:mm tt", CultureInfo.InvariantCulture);
        if (e.Start <= now) return $"Now, until {Clock(end)}";
        var today = TimeZoneInfo.ConvertTime(now, zone).Date;
        if (start.Date == today) return Clock(start);
        if (start.Date == today.AddDays(1)) return $"Tomorrow {Clock(start)}";
        return start.ToString("ddd h:mm tt", CultureInfo.InvariantCulture);
    }
}
