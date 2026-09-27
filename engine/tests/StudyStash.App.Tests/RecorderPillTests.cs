using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StudyStash.App.ViewModels;
using StudyStash.App.Views;

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
    public void The_pill_is_small_and_shows_only_the_time(SkinKind skin)
    {
        var model = Demo.Recorder();
        var (_, pill) = Show(skin, model);

        Assert.True(pill.Bounds.Height <= 32, $"{skin}: the pill is {pill.Bounds.Height} tall");
        Assert.True(pill.Bounds.Width <= 110, $"{skin}: the pill is {pill.Bounds.Width} wide");
        var words = Showing<TextBlock>(pill).Select(t => t.Text).ToList();
        Assert.Equal(["24:18"], words);
        Assert.DoesNotContain(model.ClassName, string.Concat(words));
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
}
