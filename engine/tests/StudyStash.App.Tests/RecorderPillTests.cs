using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StudyStash.App.ViewModels;
using StudyStash.App.Views;
using StudyStash.Core;

namespace StudyStash.App.Tests;

/// <summary>While a lecture records, the floating pill is tiny and only says it's recording: the dot, the time and a
/// small level meter. No class, no buttons, until the pointer is over it; then Stop, in the meter's place.</summary>
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
    public void The_pill_is_a_small_upright_capsule_with_no_words(SkinKind skin)
    {
        var model = Demo.Recorder();
        var (_, pill) = Show(skin, model);

        Assert.True(pill.Bounds.Height <= 90, $"{skin}: the pill is {pill.Bounds.Height} tall");
        Assert.True(pill.Bounds.Width <= 48, $"{skin}: the pill is {pill.Bounds.Width} wide");
        // Its mark and the level meter only: the time is its tooltip, the class is the recorder's.
        Assert.Empty(Showing<TextBlock>(pill));
        Assert.Equal("24:18", ToolTip.GetTip(pill));
        Assert.Empty(Showing<Button>(pill));
    }

    [AvaloniaTheory]
    [InlineData(SkinKind.Mac)]
    [InlineData(SkinKind.Win)]
    public void The_pointer_over_the_pill_puts_stop_in_the_meters_place(SkinKind skin)
    {
        var model = Demo.Recorder();
        bool stopped = false;
        model.OnStop = () => stopped = true;
        var (_, pill) = Show(skin, model);
        double width = pill.Bounds.Width;

        model.Hovered = true;
        Dispatcher.UIThread.RunJobs();
        var stop = Assert.Single(Showing<Button>(pill));
        Assert.Equal(width, pill.Bounds.Width); // the pill doesn't grow under the pointer
        stop.Command!.Execute(null);
        Assert.True(stopped);
    }

    [Fact]
    public void Opening_the_recorder_from_the_pill_forgets_the_pointer()
    {
        var model = new RecorderModel { Hovered = true };
        bool? opened = null;
        model.OnExpand = e => opened = e;

        model.ToggleCommand.Execute(null);

        Assert.True(model.Expanded);
        Assert.True(opened);
        Assert.False(model.Hovered);
        Assert.True(model.ShowMeter);
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
