using System.Globalization;

namespace StudyStash.Core;

/// <summary>Each class's dot: the same color on every screen and computer, from the class's place in the library.</summary>
public static class ClassColors
{
    /// <summary>OKLCH hues at one lightness and chroma, so no class shouts: blue, green, violet, amber, then the rest.</summary>
    public static readonly (double L, double C, double H)[] Palette =
    [
        (0.62, 0.14, 250), (0.64, 0.14, 155), (0.60, 0.14, 295), (0.70, 0.13, 70),
        (0.63, 0.14, 20), (0.66, 0.12, 200), (0.62, 0.14, 330), (0.66, 0.13, 120),
    ];

    public static (double L, double C, double H) For(int index) => Palette[((index % Palette.Length) + Palette.Length) % Palette.Length];

    /// <summary>OKLCH to sRGB, clipped: "#4F86D9".</summary>
    public static string Hex((double L, double C, double H) c)
    {
        double hr = c.H * Math.PI / 180, a = c.C * Math.Cos(hr), b = c.C * Math.Sin(hr);
        double l_ = c.L + 0.3963377774 * a + 0.2158037573 * b;
        double m_ = c.L - 0.1055613458 * a - 0.0638541728 * b;
        double s_ = c.L - 0.0894841775 * a - 1.2914855480 * b;
        double l = l_ * l_ * l_, m = m_ * m_ * m_, s = s_ * s_ * s_;
        double r = 4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s;
        double g = -1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s;
        double bl = -0.0041960863 * l - 0.7034186147 * m + 1.7076147010 * s;
        static int Gamma(double x)
        {
            x = Math.Clamp(x, 0, 1);
            double v = x <= 0.0031308 ? 12.92 * x : 1.055 * Math.Pow(x, 1 / 2.4) - 0.055;
            return (int)Math.Round(v * 255);
        }
        return string.Create(CultureInfo.InvariantCulture, $"#{Gamma(r):X2}{Gamma(g):X2}{Gamma(bl):X2}");
    }
}
