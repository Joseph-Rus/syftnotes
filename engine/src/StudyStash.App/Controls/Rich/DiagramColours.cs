using Avalonia.Media;
using StudyStash.Core.Rich;

namespace StudyStash.App.Controls.Rich;

/// <summary>
/// A diagram's colours in the app: the neutral box, and each tone's fill and stroke, worked out in OKLCH so they sit
/// with the design's colours, light and dark. The accent tone is the colour theme's own accent.
/// </summary>
public static class DiagramColours
{
    /// <summary>Each tone's hue: red 27, blue 255, green 150, amber 75, purple 305.</summary>
    public static double Hue(Tone tone) => tone switch
    {
        Tone.Red => 27,
        Tone.Blue => 255,
        Tone.Green => 150,
        Tone.Amber => 75,
        _ => 305,
    };

    /// <summary>A tone's fill and stroke: a pale fill and a strong stroke in light, a deep fill and a soft stroke in
    /// dark. For <see cref="Tone.Accent"/>, the theme's accent and its tint (made from the accent where a look has
    /// no tint of its own).</summary>
    public static (Color Fill, Color Stroke) Of(Tone tone, bool dark, Color accent, Color? accentTint)
    {
        if (tone == Tone.Accent) return (accentTint ?? Color.FromArgb((byte)(dark ? 64 : 46), accent.R, accent.G, accent.B), accent);
        double h = Hue(tone);
        return dark ? (Oklch.ToColor(0.30, 0.06, h), Oklch.ToColor(0.76, 0.12, h)) : (Oklch.ToColor(0.95, 0.035, h), Oklch.ToColor(0.56, 0.15, h));
    }

    /// <summary>An uncoloured box: white with a hairline of the text colour in light; a faint white glass with a
    /// brighter edge in dark.</summary>
    public static (Color Fill, Color Stroke) Neutral(bool dark, Color ink) =>
        dark ? (Color.FromArgb(20, 255, 255, 255), Color.FromArgb(56, 255, 255, 255)) : (Colors.White, Color.FromArgb((byte)Math.Round(ink.A * 0.16), ink.R, ink.G, ink.B));
}
