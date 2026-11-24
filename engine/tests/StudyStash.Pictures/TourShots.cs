using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using StudyStash.App.ViewModels;
using StudyStash.App.ViewModels.Kit;
using StudyStash.App.Views;

namespace StudyStash.App.Pictures;

/// <summary>
/// The look round Home's buttons over the full app, as a student sees it after a first run
/// (shots/tour-&lt;step&gt;-&lt;mac|win&gt;-&lt;light|dark&gt;-&lt;width&gt;.png): a few steps, both looks, light and dark, a
/// big window and a small one. Drawn off-screen, never on the student's screen.
/// </summary>
public class TourShots
{
    static async Task TakeAsync(SkinKind skin, ThemeVariant variant, Size size, string target)
    {
        ((App)Application.Current!).UseSkin(skin);
        var library = Demo.Overview(narrow: size.Width < 1000);
        library.DrawChrome = true;
        Control page = skin == SkinKind.Mac ? new MacLibrary { DataContext = library } : new WinLibrary { DataContext = library };
        var window = new Window { Width = size.Width, Height = size.Height, RequestedThemeVariant = variant, Content = page };
        Look.Apply(window);
        window.Show();
        var tour = new TourModel(HomeTour.Steps);
        library.Tour = tour;
        await Wait.UntilAsync(() => tour.Current is not null, "the tour to start");
        while (tour.Current is { } step && step.Target != target) tour.NextCommand.Execute(null);
        Assert.NotNull(tour.Current);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        DiagramsReady.Wait(window);
        var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("nothing rendered");
        string name = $"tour-{tour.Current!.Target.ToLowerInvariant()}-{(skin == SkinKind.Mac ? "mac" : "win")}-{(variant == ThemeVariant.Dark ? "dark" : "light")}-{size.Width:0}.png";
        using (var file = File.Create(Path.Combine(Shot.Dir, name)))
            frame.Save(file, PngBitmapEncoderOptions.Default);
        window.Close();
    }

    [AvaloniaFact]
    public async Task Record_mac_light() => await TakeAsync(SkinKind.Mac, ThemeVariant.Light, new Size(1280, 800), "Record");

    [AvaloniaFact]
    public async Task Lectures_win_dark() => await TakeAsync(SkinKind.Win, ThemeVariant.Dark, new Size(1280, 800), "NoteRows");

    [AvaloniaFact]
    public async Task Settings_mac_dark_small() => await TakeAsync(SkinKind.Mac, ThemeVariant.Dark, new Size(820, 600), "Settings");

    [AvaloniaFact]
    public async Task Study_win_light() => await TakeAsync(SkinKind.Win, ThemeVariant.Light, new Size(1280, 800), "StudyRow");
}
