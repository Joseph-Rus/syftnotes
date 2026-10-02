using System.Globalization;

namespace StudyStash.Core.Rich;

/// <summary>
/// How an illustration's own colours (a hand's skin, a drone's carbon frame, a board's green) are shown on a dark
/// page: still themselves, so the subject reads as what it is, but moved into the lightness a dark page can carry.
/// Darks are lifted clear of the dark paper (a black motor stays a visible dark grey), mid and light tones keep most of
/// their lightness and contrast, and the lightest are held back from glaring; hue is kept, and the order of light to
/// dark too, so shading still reads the right way up. A near-white with no colour in it is paper: a background left
/// in becomes the dark paper, not a bright card. Worked in OKLab, where equal steps look equal.
/// </summary>
public static class SvgColour
{
    /// <summary>Where lightness lands in dark: (written, shown) points, joined by straight lines.</summary>
    static readonly (double From, double To)[] Curve = [(0, 0.44), (0.5, 0.6), (0.75, 0.77), (1, 0.9)];

    /// <summary>The dark page's version of <paramref name="hex"/> (lower-case #rrggbb); <paramref name="paper"/> for a
    /// near-white with no colour in it.</summary>
    public static string ForDark(string hex, string paper)
    {
        if (Lab(hex) is not { } lab) return hex;
        var (l, a, b) = lab;
        if (l > 0.96 && Math.Sqrt(a * a + b * b) < 0.02) return paper;
        double to = Lift(l);
        return Hex(to, a, b);
    }

    /// <summary>Lightness on a dark page, for lightness <paramref name="l"/> on a light one.</summary>
    public static double Lift(double l)
    {
        l = Math.Clamp(l, 0, 1);
        for (int i = 1; i < Curve.Length; i++)
            if (l <= Curve[i].From)
            {
                var (x0, y0) = Curve[i - 1];
                var (x1, y1) = Curve[i];
                return y0 + (y1 - y0) * (l - x0) / (x1 - x0);
            }
        return Curve[^1].To;
    }

    /// <summary>A #rrggbb colour's OKLab lightness and its a and b; null when it isn't one.</summary>
    public static (double L, double A, double B)? Lab(string hex)
    {
        if (hex.Length != 7 || hex[0] != '#' || !hex.Skip(1).All(Uri.IsHexDigit)) return null;
        static double Linear(int c)
        {
            double x = c / 255.0;
            return x <= 0.04045 ? x / 12.92 : Math.Pow((x + 0.055) / 1.055, 2.4);
        }
        double r = Linear(Convert.ToInt32(hex[1..3], 16)), g = Linear(Convert.ToInt32(hex[3..5], 16)), bl = Linear(Convert.ToInt32(hex[5..7], 16));
        double l = Math.Cbrt(0.4122214708 * r + 0.5363325363 * g + 0.0514459929 * bl);
        double m = Math.Cbrt(0.2119034982 * r + 0.6806995451 * g + 0.1073969566 * bl);
        double s = Math.Cbrt(0.0883024619 * r + 0.2817188376 * g + 0.6299787005 * bl);
        return (0.2104542553 * l + 0.7936177850 * m - 0.0040720468 * s,
            1.9779984951 * l - 2.4285922050 * m + 0.4505937099 * s,
            0.0259040371 * l + 0.7827717662 * m - 0.8086757660 * s);
    }

    /// <summary>An OKLab colour as #rrggbb, its colour reduced until it fits sRGB (so its hue holds).</summary>
    public static string Hex(double l, double a, double b)
    {
        for (int i = 0; i < 24; i++)
        {
            if (Rgb(l, a, b) is { } rgb) return Format(rgb);
            a *= 0.85;
            b *= 0.85;
        }
        return Format(Rgb(l, 0, 0) ?? (l, l, l));
    }

    static (double R, double G, double B)? Rgb(double l, double a, double b)
    {
        double l_ = l + 0.3963377774 * a + 0.2158037573 * b;
        double m_ = l - 0.1055613458 * a - 0.0638541728 * b;
        double s_ = l - 0.0894841775 * a - 1.2914855480 * b;
        double L3 = l_ * l_ * l_, M3 = m_ * m_ * m_, S3 = s_ * s_ * s_;
        double r = 4.0767416621 * L3 - 3.3077115913 * M3 + 0.2309699292 * S3;
        double g = -1.2684380046 * L3 + 2.6097574011 * M3 - 0.3413193965 * S3;
        double bl = -0.0041960863 * L3 - 0.7034186147 * M3 + 1.7076147010 * S3;
        const double slack = 0.002;
        if (r < -slack || r > 1 + slack || g < -slack || g > 1 + slack || bl < -slack || bl > 1 + slack) return null;
        return (r, g, bl);
    }

    static string Format((double R, double G, double B) linear)
    {
        static int Channel(double x)
        {
            x = Math.Clamp(x, 0, 1);
            double v = x <= 0.0031308 ? 12.92 * x : 1.055 * Math.Pow(x, 1 / 2.4) - 0.055;
            return (int)Math.Round(Math.Clamp(v, 0, 1) * 255);
        }
        return string.Create(CultureInfo.InvariantCulture, $"#{Channel(linear.R):x2}{Channel(linear.G):x2}{Channel(linear.B):x2}");
    }
}
