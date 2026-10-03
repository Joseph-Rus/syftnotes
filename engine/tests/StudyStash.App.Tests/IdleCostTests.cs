using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StudyStash.App.Controls;
using StudyStash.App.Controls.Rich;
using StudyStash.Core.Rich;

namespace StudyStash.App.Tests;

/// <summary>
/// What the app does while nobody is looking at it, or while it is looked at: a spinner that went on turning in a panel
/// that's hidden, or in a library window closed to the menu bar, woke the app thirty times a second for as long as it
/// ran; the recording dot, redrawn sixty times a second, cost the recorder most of its CPU; a notes view built the same
/// lecture again for each hidden copy of it; a slider left playing went on being redrawn in a closed window. Each stops
/// when it can't be seen, and starts again when it can.
/// </summary>
public class IdleCostTests
{
    static Window Show(Control content)
    {
        var w = new Window { Width = 700, Height = 700, Content = content };
        w.Show();
        Dispatcher.UIThread.RunJobs();
        return w;
    }

    [AvaloniaFact]
    public void A_spinner_turns_only_while_it_can_be_seen()
    {
        string? still = Environment.GetEnvironmentVariable("STUDYSTASH_STILL");
        Environment.SetEnvironmentVariable("STUDYSTASH_STILL", null); // other tests' pictures set it for the whole run
        try
        {
            var spinner = new StudyStash.App.Controls.Spinner { Width = 12, Height = 12 };
            var row = new StackPanel { Children = { spinner }, IsVisible = false };
            var w = Show(row);
            Assert.False(spinner.Turning); // in a panel that's hidden (the "Attaching…" row, with nothing attaching)
            row.IsVisible = true;
            Assert.True(spinner.Turning);
            w.Hide(); // the library window closed to the menu bar
            Assert.False(spinner.Turning);
            w.Show();
            Assert.True(spinner.Turning);
            row.IsVisible = false;
            Assert.False(spinner.Turning);
            w.Close();
        }
        finally
        {
            Environment.SetEnvironmentVariable("STUDYSTASH_STILL", still);
        }
    }

    [AvaloniaFact]
    public void The_recording_dot_breathes_only_while_it_can_be_seen_and_in_the_same_slow_breath()
    {
        var dot = new PulseDot { Width = 8, Height = 8 };
        var row = new StackPanel { Children = { dot }, IsVisible = false };
        var w = Show(row);
        Assert.False(dot.Breathing);
        row.IsVisible = true;
        Assert.True(dot.Breathing);
        w.Hide();
        Assert.False(dot.Breathing);
        Assert.Equal(1, dot.Opacity);
        w.Close();
        // Full at the start, a third at the faintest half a period on, and back: what the dot always did.
        Assert.Equal(1, PulseDot.OpacityAt(0), 3);
        Assert.Equal(PulseDot.Faintest, PulseDot.OpacityAt(PulseDot.Period / 2), 3);
        Assert.Equal(1, PulseDot.OpacityAt(PulseDot.Period), 3);
        for (double t = 0; t < 7; t += 0.1) Assert.InRange(PulseDot.OpacityAt(t), PulseDot.Faintest - 1e-9, 1 + 1e-9);
        // Ten steps a second, not sixty frames: that's what made it cheap.
        Assert.True(PulseDot.Step >= TimeSpan.FromMilliseconds(80));
    }

    [AvaloniaFact]
    public void Notes_in_a_hidden_view_are_built_when_it_shows_not_before()
    {
        var note = new NoteView();
        var holder = new StackPanel { Children = { note }, IsVisible = false };
        var w = Show(holder);
        note.Markdown = "## Summary\n\nSome words.\n\n- one\n- two";
        Dispatcher.UIThread.RunJobs();
        Assert.True(note.Unbuilt);
        Assert.Empty(note.Children);
        holder.IsVisible = true;
        Dispatcher.UIThread.RunJobs();
        Assert.False(note.Unbuilt);
        Assert.NotEmpty(note.Children);
        // Shown, it builds as it always did.
        note.Markdown = "## Another\n\nWords.";
        Assert.False(note.Unbuilt);
        Assert.NotEmpty(note.Children);
        w.Close();
    }

    [AvaloniaFact]
    public void A_slider_left_playing_stops_when_the_window_is_closed_to_the_menu_bar()
    {
        var note = new NoteView { Markdown = "```plot\n" + PlotDesign.Normal + "\n```", Width = 620 };
        var w = Show(note);
        var plot = Assert.Single(note.GetLogicalDescendants().OfType<PlotView>());
        var play = plot.GetVisualDescendants().OfType<Button>().First(b => (AutomationProperties.GetName(b) ?? "").StartsWith("Play", StringComparison.Ordinal));
        play.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.True(plot.Playing);
        w.Hide();
        Assert.False(plot.Playing);
        w.Close();
    }
}
