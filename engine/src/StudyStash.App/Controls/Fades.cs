using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace StudyStash.App.Views;

/// <summary>A gradient from nothing to the window's color: the page fading out under a floating bar.</summary>
public static class Fades
{
    /// <summary>Paints the fade from the <paramref name="colorKey"/> token, and again whenever that token changes
    /// (light to dark, another colour theme).</summary>
    public static void Under(Border fade, string colorKey, double solidFrom) =>
        fade.Bind(Border.BackgroundProperty, fade.GetResourceObservable(colorKey, v => v is ISolidColorBrush { Color.A: > 0 } b ? Gradient(b.Color, solidFrom) : null));

    /// <summary>The same fade for a page on a translucent layer (Windows' content layer over Mica): it fades to the
    /// colour the eye sees, <paramref name="layerKey"/> over <paramref name="colorKey"/>, so the fade meets the page
    /// under it without a band. Over the real Mica (the window's <paramref name="colorKey"/> made clear) no one colour
    /// is what the eye sees, so there's no fade: a guessed one showed as a pale band over the page.</summary>
    public static void Under(Border fade, string colorKey, string layerKey, double solidFrom)
    {
        Color? under = null, over = null;
        void Paint()
        {
            if (under is { A: 0 }) fade.Background = null;
            else if (under is { } u) fade.Background = Gradient(over is { } o ? Over(o, u) : u, solidFrom);
        }
        fade.GetResourceObservable(colorKey).Subscribe(new Watch(v =>
        {
            under = (v as ISolidColorBrush)?.Color;
            Paint();
        }));
        fade.GetResourceObservable(layerKey).Subscribe(new Watch(v =>
        {
            over = (v as ISolidColorBrush)?.Color;
            Paint();
        }));
    }

    /// <summary><paramref name="top"/> laid over the opaque <paramref name="bottom"/>.</summary>
    static Color Over(Color top, Color bottom)
    {
        double a = top.A / 255.0;
        byte Mix(byte t, byte b) => (byte)Math.Round(t * a + b * (1 - a));
        return Color.FromRgb(Mix(top.R, bottom.R), Mix(top.G, bottom.G), Mix(top.B, bottom.B));
    }

    sealed class Watch(Action<object?> next) : IObserver<object?>
    {
        public void OnNext(object? value) => next(value);
        public void OnCompleted() { }
        public void OnError(Exception error) { }
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
