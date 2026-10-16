using Avalonia.Media;
using StudyStash.App.Services;
using StudyStash.App.ViewModels;
using StudyStash.Core;
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

    /// <summary>Every event called this is the suggested class's from now on (the library keeps the title as one of
    /// the class's names), then Coming up shows it so.</summary>
    static async Task FileEventAsync(ComingUpRow row)
    {
        if (row.Suggested is not { } cls || host.Remote() is not { } lib) return;
        try
        {
            await lib.SetAliasAsync(cls, row.Title);
            await host.CheckLibraryAsync();
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or LibraryRefusedException)
        {
            return;
        }
        UpdateComingUp();
    }

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
        // An event with notes already written for it says so: they're where its recording goes when it starts.
        var ready = host.Lectures.All().Where(l => l.State == LectureState.Planned && l.Notes.Trim().Length > 0 && l.Event is not null)
            .Select(l => l.Event!.Id).ToHashSet();
        rows = [.. rows.Select(r => ready.Contains(r.Id) ? r with { When = $"{r.When} · notes ready", Short = $"{r.Short} · notes ready" } : r)];
        panel.ComingUp.Show(any, rows);
        library.ComingUp.Show(any, rows);
    }
}
