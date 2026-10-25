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
}
