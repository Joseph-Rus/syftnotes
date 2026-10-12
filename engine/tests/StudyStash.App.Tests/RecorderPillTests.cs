using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StudyStash.App.ViewModels;
using StudyStash.App.Views;
using StudyStash.Core;

namespace StudyStash.App.Tests;

/// <summary>While a lecture records, the floating recorder is a small white pill: its red dot is Stop, then the class
/// in a word or two, a level meter and the time. Red is only in the dot and the meter; no colour theme reaches it.</summary>
public class RecorderPillTests
{
    static (Control View, Control Pill) Show(SkinKind skin, RecorderModel model)
    {
        Skin.UseTheme(ColourThemes.Default);
        ((App)Application.Current!).UseSkin(skin);
        Control view = skin == SkinKind.Mac ? new MacRecorder { DataContext = model } : new WinRecorder { DataContext = model };
        var window = new Window { Width = 400, Height = 200, RequestedThemeVariant = ThemeVariant.Light, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (view, view.FindControl<Control>("Pill")!);
    }

    static bool Shown(Visual v) => v.IsVisible && v.GetVisualAncestors().All(a => a.IsVisible);

    static List<T> Showing<T>(Visual root) where T : Visual => [.. root.GetVisualDescendants().OfType<T>().Where(Shown)];

    [AvaloniaTheory]
    [InlineData(SkinKind.Mac)]
    [InlineData(SkinKind.Win)]
    public void The_pill_is_a_small_white_capsule_with_stop_the_class_and_the_time(SkinKind skin)
    {
        var model = Demo.Recorder();
        model.ClassName = "Theory of Computation and Compilers(CSCI4150.A)";
        bool stopped = false;
        model.OnStop = () => stopped = true;
        var (view, pill) = Show(skin, model);

        Assert.Equal(44, pill.Bounds.Height);
        Assert.True(pill.Bounds.Width <= 260, $"{skin}: the pill is {pill.Bounds.Width} wide");
        // The class in a word, and the time; its one button is Stop (the red dot).
        var words = Showing<TextBlock>(pill).Where(t => t.Opacity > 0).Select(t => t.Text).ToList();
        Assert.Equal(["Theory", "24:18"], words);
        var stop = Assert.Single(Showing<Button>(pill));
        Assert.Equal("Stop", Avalonia.Automation.AutomationProperties.GetName(stop));
        stop.Command!.Execute(null);
        Assert.True(stopped);
        // White, whatever the colour theme: only the dot is red.
        Color Token(string key) => view.TryFindResource(key, view.ActualThemeVariant, out var b) ? ((ISolidColorBrush)b!).Color : default;
        Assert.Equal(Token("Surface"), ((ISolidColorBrush)((Border)pill).Background!).Color);
        var dot = pill.GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("record-dot"));
        Assert.Equal(Token("Record"), ((ISolidColorBrush)dot.Background!).Color);
    }

    /// <summary>Pausing swaps the class for "Paused" and rests the meter, each in the other's room: a see-through
    /// window resized on screen under the pointer is what made the recorder lag once.</summary>
    [AvaloniaTheory]
    [InlineData(SkinKind.Mac)]
    [InlineData(SkinKind.Win)]
    public void Pausing_never_changes_the_pills_size(SkinKind skin)
    {
        var model = Demo.Recorder();
        var (_, pill) = Show(skin, model);
        var size = pill.Bounds.Size;

        model.IsPaused = true;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(size, pill.Bounds.Size);
        Assert.Equal(["Paused", "24:18"], Showing<TextBlock>(pill).Where(t => t.Opacity > 0).Select(t => t.Text));
        Assert.Equal("Paused CS 101, 24:18", model.PillSaid);

        model.IsPaused = false;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(size, pill.Bounds.Size);
    }

    [Fact]
    public void A_click_on_the_pill_opens_the_recorder()
    {
        var model = new RecorderModel();
        bool? opened = null;
        model.OnExpand = e => opened = e;

        model.ToggleCommand.Execute(null);

        Assert.True(model.Expanded);
        Assert.True(opened);
    }

    /// <summary>The open recorder shows its newest line, at the bottom, however many came before it: as the live words
    /// come every second, when it opens on a long transcript, and as it grows.</summary>
    [AvaloniaTheory]
    [InlineData(SkinKind.Mac)]
    [InlineData(SkinKind.Win)]
    public void The_open_recorder_keeps_its_newest_line_in_view(SkinKind skin)
    {
        Skin.UseTheme(ColourThemes.Default);
        ((App)Application.Current!).UseSkin(skin);
        var model = Demo.Recorder();
        model.Lines.Clear();
        for (int i = 0; i < 30; i++) model.Lines.Add(new HeardLine { Time = TimedText.Clock(i * 10), Text = $"Line {i}: what was said then, at some length.", Start = i * 10 });
        Control view = skin == SkinKind.Mac ? new MacRecorder { DataContext = model } : new WinRecorder { DataContext = model };
        var window = new Window { Width = 432, Height = 400, RequestedThemeVariant = ThemeVariant.Light, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var heard = view.FindControl<ScrollViewer>("Heard")!;

        void Settle()
        {
            for (int i = 0; i < 4; i++)
            {
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
            }
        }

        void NewestShows(string when)
        {
            Settle();
            Assert.True(heard.Extent.Height > heard.Viewport.Height, $"{skin} {when}: the transcript fits, so this proves nothing");
            Assert.True(heard.Offset.Y >= heard.Extent.Height - heard.Viewport.Height - 1,
                $"{skin} {when}: scrolled to {heard.Offset.Y} of {heard.Extent.Height - heard.Viewport.Height}");
            var newest = heard.GetVisualDescendants().OfType<TextBlock>().Last(t => t.Text?.StartsWith("Line ", StringComparison.Ordinal) == true);
            Assert.Equal(model.Lines[^1].Text, newest.Text);
            var bottom = newest.TranslatePoint(new Point(0, newest.Bounds.Height), heard)!.Value;
            Assert.InRange(bottom.Y, 0, heard.Viewport.Height + 1);
        }

        model.Expanded = true;
        NewestShows("opened");
        model.Lines.Add(new HeardLine { Time = "05:00", Text = "Line 30: the newest words.", Start = 300, Latest = true });
        NewestShows("a new line");
        window.Height = 600;
        NewestShows("grown");
    }
}
