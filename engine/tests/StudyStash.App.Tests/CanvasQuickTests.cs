using StudyStash.App.Services;
using StudyStash.App.ViewModels;

namespace StudyStash.App.Tests;

public class CanvasQuickTests
{
    static readonly TimeZoneInfo Zone = CanvasFixtures.Zone;
    static readonly DateTimeOffset Now = CanvasFixtures.Now;

    [Fact]
    public void NextDue_reads_the_soonest_item_as_the_dropdown_s_line()
    {
        var due = CanvasFixtures.Load<CanvasApi.DueResponse>("due");
        CanvasApi.Item? opened = null;

        var next = CanvasQuick.NextDue(due, Zone, Now, i => opened = i);

        Assert.NotNull(next);
        Assert.Equal("Next due: Quiz 3 practice · Tomorrow, 9:00 AM", next.Text);
        next.Open();
        Assert.Equal("8102", opened!.Id);
    }

    [Fact]
    public void NextDue_is_null_when_nothing_is_due()
    {
        Assert.Null(CanvasQuick.NextDue(new CanvasApi.DueResponse(), Zone, Now, _ => { }));
        Assert.Null(CanvasQuick.NextDue(null, Zone, Now, _ => { }));
    }

    [Theory]
    [InlineData("due")]
    [InlineData("canvas")]
    [InlineData("assignment")]
    [InlineData("homework")]
    [InlineData("sync")]
    [InlineData("quiz")] // a word of an item's own name
    [InlineData("calc")] // a word of an item's own class
    public void Matches_the_trigger_words_and_the_due_list_s_own_words(string query)
    {
        var due = CanvasFixtures.Load<CanvasApi.DueResponse>("due");
        Assert.True(CanvasQuick.Matches(due, query));
    }

    [Theory]
    [InlineData("")]
    [InlineData("granola")]
    [InlineData("osmosis")] // that item's already handed in, not something still to do
    public void Matches_nothing_else(string query)
    {
        var due = CanvasFixtures.Load<CanvasApi.DueResponse>("due");
        Assert.False(CanvasQuick.Matches(due, query));
    }

    [Fact]
    public void Matches_is_false_with_no_due_data_at_all_unless_the_query_is_a_trigger_word()
    {
        Assert.True(CanvasQuick.Matches(null, "due"));
        Assert.False(CanvasQuick.Matches(null, "quiz"));
    }

    [Fact]
    public void Rows_reads_exactly_as_mac_12_the_due_header_the_three_items_then_actions()
    {
        var due = CanvasFixtures.Load<CanvasApi.DueResponse>("due");
        var state = CanvasFixtures.Load<CanvasApi.State>("state-connected");
        var opened = new List<string>();

        var rows = CanvasQuick.Rows(due, state, "due", Zone, Now, _ => Avalonia.Media.Brushes.Gray, i => opened.Add(i.Id), () => { }, () => { });

        Assert.Equal(
            [
                (QuickKind.Header, "Due", ""),
                (QuickKind.Lecture, "Reading response: Treaty of Versailles", "Missing · was due Mon"),
                (QuickKind.Lecture, "Quiz 3 practice", "Tomorrow, 9:00 AM"),
                (QuickKind.Lecture, "Lab 3: recursion traces", "Tue 30 Sep, 11:59 PM"),
                (QuickKind.Header, "Actions", ""),
                (QuickKind.Action, "Sync Canvas", "Last sync 10:24"),
                (QuickKind.Action, "Open Due", ""),
            ],
            rows.Select(r => (r.Kind, r.Title, r.Meta)));
        Assert.True(rows[0].First);
        Assert.False(rows[4].First);

        rows[1].Run!();
        Assert.Equal(["7051"], opened);
    }

    [Fact]
    public void Rows_narrows_to_a_query_word_that_isn_t_a_bare_trigger()
    {
        var due = CanvasFixtures.Load<CanvasApi.DueResponse>("due");
        var rows = CanvasQuick.Rows(due, null, "quiz", Zone, Now, _ => Avalonia.Media.Brushes.Gray, _ => { }, () => { }, () => { });

        Assert.Equal(["Due", "Quiz 3 practice", "Actions", "Sync Canvas", "Open Due"], rows.Select(r => r.Title));
    }

    [Fact]
    public void Rows_still_offers_the_actions_when_there_s_no_due_data_yet()
    {
        var rows = CanvasQuick.Rows(null, null, "canvas", Zone, Now, _ => Avalonia.Media.Brushes.Gray, _ => { }, () => { }, () => { });

        Assert.Equal(["Actions", "Sync Canvas", "Open Due"], rows.Select(r => r.Title));
        Assert.True(rows[0].First);
    }

    [Fact]
    public void Rows_actions_run_sync_and_open_due()
    {
        var due = CanvasFixtures.Load<CanvasApi.DueResponse>("due");
        bool synced = false, openedDue = false;
        var rows = CanvasQuick.Rows(due, null, "due", Zone, Now, _ => Avalonia.Media.Brushes.Gray, _ => { }, () => synced = true, () => openedDue = true);

        rows.Single(r => r.Title == "Sync Canvas").Run!();
        rows.Single(r => r.Title == "Open Due").Run!();
        Assert.True(synced);
        Assert.True(openedDue);
    }
}
