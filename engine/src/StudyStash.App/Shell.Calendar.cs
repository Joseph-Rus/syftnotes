using Avalonia.Media;
using StudyStash.App.Services;
using StudyStash.App.ViewModels;
using StudyStash.Core.Calendar;

namespace StudyStash.App;

/// <summary>Calendars in the running app: Coming up in the dropdown and the library window, and Record's class and
/// line while an event is on.</summary>
public static partial class Shell
{
    /// <summary>The class the event on now is for, when the student hasn't picked one and the library has it; "".</summary>
    static string CalendarClass() =>
        chosenClass.Length == 0 && host.Settings.Role != AppRole.Library && host.EventNow() is { Class: { } c } && host.Classes().Any(x => x.Name == c) ? c : "";

    /// <summary>The line under Record while an event is on and nothing was picked by hand: "From your calendar: …".</summary>
    static string? CalendarHint() =>
        chosenClass.Length == 0 && host.Settings.Role != AppRole.Library && host.EventNow() is { } now ? CalendarWords.FromCalendar(now.Event.Title) : null;

    /// <summary>Coming up, in the dropdown and the library window, from what the calendars last read.</summary>
    static void UpdateComingUp()
    {
        if (host.Settings.Role == AppRole.Library)
        {
            panel.ComingUp.Show(false, []);
            library.ComingUp.Show(false, []);
            return;
        }
        var upcoming = host.Calendars;
        bool any = host.HasCalendars;
        var rows = ComingUpModel.For(upcoming.Events, DateTimeOffset.Now, TimeZoneInfo.Local, host.CalendarClasses(),
            cls => host.ColorOf(cls) is >= 0 and var color ? Skin.ClassDot(color) : (IBrush?)null);
        panel.ComingUp.Show(any, rows);
        library.ComingUp.Show(any, rows);
    }
}
