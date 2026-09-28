using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace StudyStash.App.Views;

/// <summary>The page fading out under a floating bar: a gradient from nothing to the window's colour (the Mac's), or
/// a mask over the page itself (Windows', where Mica may be behind it).</summary>
public static class Fades
{
    /// <summary>Paints the fade from the <paramref name="colorKey"/> token, and again whenever that token changes
    /// (light to dark, another colour theme).</summary>
    public static void Under(Border fade, string colorKey, double solidFrom) =>
        fade.Bind(Border.BackgroundProperty, fade.GetResourceObservable(colorKey, v => v is ISolidColorBrush b ? Gradient(b.Color, solidFrom) : null));

    /// <summary>
    /// A scrolling page fading out over its bottom <paramref name="strip"/> (under a floating bar, or just at its end)
    /// by a mask rather than a colour laid over it: whatever is behind the page — Windows 11's Mica, or the design's
    /// colour — shows through the fade, with no band where a guessed colour would differ. Clear from
    /// <paramref name="clearFrom"/> of the way down the strip; follows both as they change size.
    /// </summary>
    public static void MaskBottom(Control page, Control strip, double clearFrom)
    {
        void Mask()
        {
            double h = page.Bounds.Height, fade = strip.Bounds.Height;
            page.OpacityMask = h <= 0 || fade <= 0 ? null : new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, Math.Max(0, h - fade), RelativeUnit.Absolute),
                EndPoint = new RelativePoint(0, h, RelativeUnit.Absolute),
                GradientStops = { new GradientStop(Colors.Black, 0), new GradientStop(Colors.Transparent, clearFrom) },
            };
        }
        page.SizeChanged += (_, _) => Mask();
        strip.SizeChanged += (_, _) => Mask();
    }

    /// <summary>The page fading out from the top, under a toolbar: solid until <paramref name="solidTo"/>, then to
    /// nothing.</summary>
    public static void Over(Border fade, string colorKey, double solidTo) =>
        fade.Bind(Border.BackgroundProperty, fade.GetResourceObservable(colorKey, v => v is ISolidColorBrush b ? FadeOut(b.Color, solidTo) : null));

    static LinearGradientBrush Gradient(Color c, double solidFrom) => new()
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
        GradientStops = { new GradientStop(Color.FromArgb(0, c.R, c.G, c.B), 0), new GradientStop(c, solidFrom) },
    };

    static LinearGradientBrush FadeOut(Color c, double solidTo) => new()
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
        GradientStops = { new GradientStop(c, 0), new GradientStop(c, solidTo), new GradientStop(Color.FromArgb(0, c.R, c.G, c.B), 1) },
    };
}
