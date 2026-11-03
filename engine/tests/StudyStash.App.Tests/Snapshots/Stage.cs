using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Styling;
using Avalonia.Threading;
using StudyStash.App.Controls;
using StudyStash.App.Platform;
using StudyStash.App.Views;

namespace StudyStash.App.Tests.Snapshots;

/// <summary>Puts a screen on the Mac's desktop in a window exactly as big as the picture: shown, laid out and still
/// (nothing moves on its own, and every date and time in the demo data is a fixed one).</summary>
static class Stage
{
    const double Desk = 28;

    /// <summary>The library window with <see cref="Desk"/> points of desktop round it.</summary>
    public static Window Library(Func<Control> build, ThemeVariant variant, double width = 1280, double height = 860) =>
        Surface(build, variant, new Size(width + 2 * Desk, height + 2 * Desk), new Thickness(Desk), width, height);

    /// <summary>Something smaller (a panel, a toast, a pill) <paramref name="at"/> in on the desktop.</summary>
    public static Window Surface(Func<Control> build, ThemeVariant variant, Size size, Thickness? at = null, double? width = null, double? height = null)
    {
        Environment.SetEnvironmentVariable("STUDYSTASH_STILL", "1");
        Skin.UseTheme(ColourThemes.Default);
        ((App)Application.Current!).UseSkin(SkinKind.Mac);
        // The look is set before the screen is made: its views read the look's colours as they are built.
        var surface = build();
        if (width is { } w) surface.Width = w;
        if (height is { } h) surface.Height = h;
        surface.HorizontalAlignment = HorizontalAlignment.Left;
        surface.VerticalAlignment = VerticalAlignment.Top;
        var desk = new WallpaperPicture(variant == ThemeVariant.Dark) { Width = size.Width, Height = size.Height, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        var on = new Border { Padding = at ?? new Thickness(32), Child = surface, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        var window = new Window { Width = size.Width, Height = size.Height, RequestedThemeVariant = variant, Content = new Panel { Children = { desk, on } } };
        Glass.SetBlurBackdrop(window, true);
        Look.Apply(window);
        window.Show();
        DiagramsReady.Wait(window);
        Dispatcher.UIThread.RunJobs();
        return window;
    }
}
