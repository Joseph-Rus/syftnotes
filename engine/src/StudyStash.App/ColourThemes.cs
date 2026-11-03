using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace StudyStash.App;

/// <summary>OKLCH, the colour space the design picks its colours in, to the sRGB a screen shows.</summary>
public static class Oklch
{
    /// <summary>
    /// <c>oklch(l c h / alpha)</c> as a browser draws it: each channel clipped to sRGB (no gamut mapping, the same as
    /// Chrome), then rounded to a byte. The matrices are the ones <see cref="StudyStash.Core.ClassColors.Hex"/> uses.
    /// </summary>
    public static Color ToColor(double l, double c, double h, double alpha = 1)
    {
        double hr = h * Math.PI / 180, a = c * Math.Cos(hr), b = c * Math.Sin(hr);
        double l_ = l + 0.3963377774 * a + 0.2158037573 * b;
        double m_ = l - 0.1055613458 * a - 0.0638541728 * b;
        double s_ = l - 0.0894841775 * a - 1.2914855480 * b;
        double lc = l_ * l_ * l_, mc = m_ * m_ * m_, sc = s_ * s_ * s_;
        double r = 4.0767416621 * lc - 3.3077115913 * mc + 0.2309699292 * sc;
        double g = -1.2684380046 * lc + 2.6097574011 * mc - 0.3413193965 * sc;
        double bl = -0.0041960863 * lc - 0.7034186147 * mc + 1.7076147010 * sc;
        return Color.FromArgb(Byte(alpha), Byte(Gamma(r)), Byte(Gamma(g)), Byte(Gamma(bl)));
    }

    static double Gamma(double x)
    {
        x = Math.Clamp(x, 0, 1);
        return x <= 0.0031308 ? 12.92 * x : 1.055 * Math.Pow(x, 1 / 2.4) - 0.055;
    }

    /// <summary>0..1 to 0..255, halves rounded up like CSS.</summary>
    internal static byte Byte(double v) => (byte)Math.Round(Math.Clamp(v, 0, 1) * 255, MidpointRounding.AwayFromZero);
}

/// <summary>
/// What a colour theme was. There's one look now (the Syft look, <see cref="Skin"/>), light, dark or following the
/// computer; the twenty themes and their palettes are gone. The name stays because app.json may still keep one
/// ("Lagoon", "Plum"): it loads as before and the app simply shows the Syft look.
/// </summary>
public sealed record ColourTheme(string Name);

/// <summary>The one look, under the name a saved setting would have.</summary>
public static class ColourThemes
{
    public static readonly ColourTheme Syft = new("Syft");

    public static readonly IReadOnlyList<ColourTheme> All = [Syft];

    public static ColourTheme Default => Syft;

    /// <summary>Whatever app.json names (an old theme, nothing at all), the Syft look.</summary>
    public static ColourTheme Find(string? name) => Default;
}

/// <summary>A colour theme as Settings once listed it: its name, and whether it's the one in use.</summary>
public sealed partial class ThemeSwatch : ObservableObject
{
    public ThemeSwatch(ColourTheme theme) => Name = theme.Name;

    public string Name { get; }
    [ObservableProperty] public partial bool Chosen { get; set; }
}
