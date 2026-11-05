using StudyStash.Core.Calendar.Ics;
using StudyStash.Core.Canvas;
using StudyStash.Library;

namespace StudyStash.Core.Tests;

/// <summary>"What's due this Sunday?" is answered from the Due list and today's date, which the library hands Ask with
/// every question (LibraryWeb.AskAgenda): no lecture's notes say it, so before, the AI was never even asked.</summary>
public class AskAgendaTests
{
    [Fact]
    public async Task A_question_about_whats_due_reaches_the_ai_with_the_date_and_the_due_list()
    {
        using var dir = new TempDir();
        var cfg = new Config(dir["home"], dir["pool"]) { OllamaEnabled = true, Classes = [new ClassDef("CS 101")] };
        using var store = new Store(cfg.DbPath, cfg.PoolDir);
        store.Save(new Meeting("m1") { Title = "Recursion", Date = "2026-10-20" }, new Classification("CS 101", 1, "folder"),
            "## Summary\nA function that calls itself needs a base case.", "Claude");
        var reader = new LibraryReader(cfg, store);
        const string agenda = "Today is Saturday 24 October 2026, 11:56 PM.\nDue in Canvas, soonest first:\n- Sun 25 Oct, 11:59 PM: CS 101: Homework 4";

        string? prompt = null;
        var reply = await reader.AskAsync("What's due this coming Sunday?", chat: (p, _) =>
        {
            prompt = p;
            return Task.FromResult("{\"answer\": \"Homework 4 for CS 101, due Sunday at 11:59 PM.\", \"sources\": []}");
        }, agenda: agenda);

        Assert.NotNull(prompt);
        Assert.Contains("Today is Saturday 24 October 2026", prompt);
        Assert.Contains("CS 101: Homework 4", prompt);
        Assert.Equal("Homework 4 for CS 101, due Sunday at 11:59 PM.", reply["answer"]!.GetValue<string>());

        // Without the agenda (a library too old to give one), nothing matched is still the old "nothing found".
        bool asked = false;
        var none = await reader.AskAsync("What's due this coming Sunday?", chat: (_, _) => { asked = true; return Task.FromResult("{}"); });
        Assert.False(asked);
        Assert.Equal(LibraryReader.NoAnswer, none["answer"]!.GetValue<string>());
    }

    /// <summary>Home said "due at 10:15 AM" while Ask, told the same Due list, said 2:15 AM and overdue: the agenda took
    /// the row's local time for UTC and converted it again. Windows' name for the zone, as on that PC, and a due date
    /// past the end of daylight saving, so the list can't pin today's offset on it.</summary>
    [Fact]
    public void The_agenda_gives_due_times_in_the_students_time_zone_and_names_it()
    {
        using var dir = new TempDir();
        var zone = IcsZones.SystemZone("Pacific Standard Time")!;
        var now = new DateTimeOffset(2026, 10, 30, 16, 0, 0, TimeSpan.Zero); // Friday, 9:00 AM daylight time (UTC-7)
        var cfg = new Config(dir.Path, dir["pool"]) { Classes = [new ClassDef("CS 101")] };
        using var store = new Store(cfg.DbPath, cfg.PoolDir);
        CanvasSettings.Update(dir.Path, s =>
        {
            s.Url = "https://canvas.example.edu";
            s.Courses["CS 101"] = 4201; // its rows count only while the class is linked to a Canvas course
        });
        const string dueAt = "2026-11-04T18:15:00Z"; // Wednesday 10:15 AM standard time (UTC-8)
        Assignments.Save(dir.Path, [new Assignment("CS 101", 1, "Future of CS essay", Assignments.Local(dueAt, zone), 10, "open", null, "", "", DueAt: dueAt)]);
        var sync = new CanvasSync(dir.Path, c => FakeCanvas.ClassDir(dir, c), _ => { }) { Clock = () => now, Zone = zone };
        var web = new LibraryWeb(cfg, store, new Pipeline(cfg, store, log: _ => { }), new LibraryWebOptions { Canvas = sync });

        string agenda = web.AskAgenda(null);

        Assert.Contains("Today is Friday 30 October 2026, 9:00 AM, ", agenda);
        Assert.Contains(" (UTC-07:00). Every time below is local time in the student's time zone (" + zone.Id + "), daylight saving included.", agenda);
        Assert.Contains("- Wed 4 Nov 2026, 10:15 AM: CS 101: Future of CS essay", agenda);
        Assert.DoesNotContain("Overdue", agenda);
    }
}
