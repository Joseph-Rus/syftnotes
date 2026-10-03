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
/// What the app does while nobody is looking at it. A spinner that went on turning in a panel that's hidden, or in a
/// library window closed to the menu bar, woke the app thirty times a second for as long as it ran (about 1.7% of a core
/// with a lecture opened once, against 0.5% without). It stops when it can't be seen, and starts again when it can.
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
}
