using Avalonia.Automation;
using StudyStash.App.Platform;
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
/// when it can't be seen, and starts again when it can. And a closed window, once nothing shows it, is let go.
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
    public void A_spinner_in_a_window_that_closes_stops_for_good()
    {
        // A window that closes is hidden first and taken apart after, and a spinner taken apart counted as seen again:
        // its timer started with nothing left listening to stop it, whether it was showing or hidden when the window
        // closed. The running timer kept the spinner, and the spinner the whole closed window, for as long as the app
        // ran: setup's window after a first run (about 20 MB), and Connect Canvas's every time it was opened (about 6).
        string? still = Environment.GetEnvironmentVariable("STUDYSTASH_STILL");
        Environment.SetEnvironmentVariable("STUDYSTASH_STILL", null); // other tests' pictures set it for the whole run
        try
        {
            foreach (bool showing in new[] { true, false })
            {
                var spinner = new StudyStash.App.Controls.Spinner { Width = 12, Height = 12 };
                var dot = new PulseDot { Width = 8, Height = 8 };
                var row = new StackPanel { Children = { spinner, dot }, IsVisible = showing };
                var w = Show(row);
                Assert.Equal(showing, spinner.Turning);
                Assert.Equal(showing, dot.Breathing);
                w.Close();
                Dispatcher.UIThread.RunJobs();
                Assert.False(spinner.Turning);
                Assert.False(dot.Breathing);
                // What the window showed goes on changing after it has gone (a check that finishes, a step that moves on).
                row.IsVisible = !showing;
                row.IsVisible = showing;
                Assert.False(spinner.Turning);
                Assert.False(dot.Breathing);
            }
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
    public void A_hidden_view_lets_go_of_the_notes_it_built_before_when_they_change()
    {
        var note = new NoteView { Markdown = "## One\n\nWords." };
        var holder = new StackPanel { Children = { note } };
        var w = Show(holder);
        Assert.NotEmpty(note.Children);
        holder.IsVisible = false;
        note.Markdown = "## Two\n\nOther words.";
        Assert.Empty(note.Children); // the old notes, out of date and unseen, aren't kept
        Assert.True(note.Unbuilt);
        holder.IsVisible = true;
        Dispatcher.UIThread.RunJobs();
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

    /// <summary>Whether <paramref name="from"/> holds <paramref name="target"/>: through what a menu item does when it's
    /// clicked, the objects its closures capture, and theirs in turn.</summary>
    static bool Holds(object? from, object target, HashSet<object> seen)
    {
        if (from is null || from is string || from.GetType().IsPrimitive || !seen.Add(from)) return false;
        if (ReferenceEquals(from, target)) return true;
        if (from is Delegate d) return d.GetInvocationList().Any(i => Holds(i.Target, target, seen));
        var type = from.GetType();
        if (type.Namespace?.StartsWith("Avalonia", StringComparison.Ordinal) == true || type.Namespace?.StartsWith("System", StringComparison.Ordinal) == true) return false;
        return type.GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
            .Any(f => !f.FieldType.IsPrimitive && Holds(f.GetValue(from), target, seen));
    }

    [AvaloniaFact]
    public void The_menu_a_window_gives_the_menu_bar_does_not_hold_the_window()
    {
        // The Mac's menu bar keeps the menu a window gave it (and so what its items do) for as long as the app runs. Items
        // that held their window kept every Settings window ever opened in memory, with all of its pages: about 25 MB of
        // the heap and 65 MB of the window's own memory each time it was opened.
        var window = new Window();
        var menu = AppMenu.ForWindow(window, () => { }, () => { });
        var items = menu.Items.OfType<NativeMenuItem>().SelectMany(i => i.Menu?.Items.OfType<NativeMenuItem>() ?? []).ToList();
        Assert.NotEmpty(items);
        var click = typeof(NativeMenuItem).GetField("Click", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(click);
        foreach (var item in items)
            Assert.False(Holds(click!.GetValue(item), window, new HashSet<object>(ReferenceEqualityComparer.Instance)), $"\"{item.Header}\" holds its window");
    }
}
