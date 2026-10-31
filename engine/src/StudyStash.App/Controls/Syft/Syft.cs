using Avalonia.Media;
using Avalonia.Platform;
using SkiaSharp;

namespace StudyStash.App.Controls;

/// <summary>
/// Syft Notes' brand, Joey's "Pixel Sift": a pixel s of 24 rounded cells on a 6 × 7 grid, its colours, the ways it's
/// coloured (its looks), the loose bits around it and the "syft notes" wordmark. Every brand piece in the app (the mark,
/// the loaders, the splash, the app's icon in a view) draws from here; the same colours are the app's brand tokens
/// (<c>{StaticResource SyftLilac}</c>, from <see cref="Skin"/>). Units are the boards': a cell is 6.6 across with
/// corners of 1.8, one every 8 (so 1.4 between), the grid 48 × 56.
/// </summary>
public static class Syft
{
    public static readonly Color Lilac = Color.Parse("#CDBDFF");
    public static readonly Color Sky = Color.Parse("#A3DAF2");
    public static readonly Color Coral = Color.Parse("#FF6E4E");
    public static readonly Color Mustard = Color.Parse("#FFC943");
    public static readonly Color Ink = Color.Parse("#16131D");
    public static readonly Color Paper = Color.Parse("#FBFAF7");
    public static readonly Color MeshLight = Color.Parse("#EFEDE8");
    public static readonly Color MeshDark = Color.Parse("#24212D");
    public static readonly Color GhostDark = Color.Parse("#2E2A39");
    public static readonly Color Muted = Color.Parse("#5C5B63");
    public static readonly Color White = Color.Parse("#FFFFFF");
    /// <summary>The empty grid behind the Path Trace loader's s (the reference's mesh-l1).</summary>
    public static readonly Color MeshTrace = Color.Parse("#1F1C27");

    /// <summary>Each colour by its token name, for the app's resources.</summary>
    public static readonly IReadOnlyDictionary<string, Color> Tokens = new Dictionary<string, Color>
    {
        ["SyftLilac"] = Lilac, ["SyftSky"] = Sky, ["SyftCoral"] = Coral, ["SyftMustard"] = Mustard, ["SyftInk"] = Ink,
        ["SyftPaper"] = Paper, ["SyftMeshLight"] = MeshLight, ["SyftMeshDark"] = MeshDark, ["SyftGhostDark"] = GhostDark,
        ["SyftMuted"] = Muted, ["SyftWhite"] = White,
    };

    public const float Cell = 6.6f, Corner = 1.8f, Pitch = 8f;

    /// <summary>The 24 cells' top left corners in path order, p0 to p23: from the top right tip, across the top, down the
    /// left, across the middle, down the right, across the bottom, to the bottom left tip. Anything that travels along
    /// the s takes its delays in this order.</summary>
    public static readonly (float X, float Y)[] Cells =
    [
        (40, 8), (40, 0), (32, 0), (24, 0), (16, 0), (8, 0), (0, 8), (8, 8), (0, 16), (8, 16), (8, 24), (16, 24),
        (24, 24), (32, 24), (32, 32), (40, 32), (32, 40), (40, 40), (32, 48), (24, 48), (16, 48), (8, 48), (0, 48), (0, 40),
    ];

    /// <summary>The s's own box: its cells' extent (the grid's 48 × 56 less the gap after the last column and row).</summary>
    public static readonly SKRect Box = new(0, 0, 40 + Cell, 48 + Cell);

    /// <summary>Sorted (the light look): rows 0–1 lilac, 2–3 sky, 4–5 coral, the bottom row ink.</summary>
    public static readonly Color[] Sorted = Paint(Lilac, Sky, Coral, Ink);

    /// <summary>Night (the dark look, and the Sort In splash and Path Trace loader): the same, the bottom row mustard.</summary>
    public static readonly Color[] Night = Paint(Lilac, Sky, Coral, Mustard);

    /// <summary>Segments (the Sieve Drop splash): each stroke its own colour, top lilac, left sky, middle ink, right
    /// coral, bottom mustard.</summary>
    public static readonly Color[] Segments =
    [
        Lilac, Lilac, Lilac, Lilac, Lilac, Lilac, Sky, Sky, Sky, Sky, Ink, Ink,
        Ink, Ink, Coral, Coral, Coral, Coral, Mustard, Mustard, Mustard, Mustard, Mustard, Mustard,
    ];

    /// <summary>p0–p7 the first colour, p8–p13 the second, p14–p17 and p23 the third, p18–p22 the fourth.</summary>
    static Color[] Paint(Color top, Color middle, Color lower, Color bottom)
    {
        var c = new Color[24];
        for (int i = 0; i < 24; i++)
            c[i] = i <= 7 ? top : i <= 13 ? middle : i <= 17 || i == 23 ? lower : bottom;
        return c;
    }

    /// <summary>A loose bit: its centre, side, corner and turn.</summary>
    public readonly record struct Bit(float X, float Y, float Side, float Radius, float Turn, Color Fill);

    /// <summary>Where the centre of a grid square is, given in pitches from the centre of the top left one.</summary>
    static (float X, float Y) At(float col, float row) => (Cell / 2 + col * Pitch, Cell / 2 + row * Pitch);

    static Bit B(float col, float row, float side, float radius, float turn, Color fill)
    {
        var (x, y) = At(col, row);
        return new Bit(x, y, side, radius, turn, fill);
    }

    /// <summary>The light look's six bits tumbling in above the s (his logo board's hero), a cell's size each.</summary>
    public static readonly Bit[] BitsAbove =
    [
        B(0.3f, -1.5f, Cell, Corner, 20, Coral), B(1.0f, -1.7f, Cell, Corner, 15, Lilac), B(1.5f, -2.6f, Cell, Corner, 25, Sky),
        B(2.6f, -2.1f, Cell, Corner, 15, Mustard), B(3.8f, -2.3f, Cell, Corner, 30, Lilac), B(4.8f, -1.9f, Cell, Corner, 35, Ink),
    ];

    /// <summary>The dark look's three bits falling out below it (the app icon ladder), the size of the splash's loose bits.</summary>
    public static readonly Bit[] BitsBelow =
    [
        B(1.0f, 6 + 1.6f, 5.6f, 1.6f, 20, Coral), B(2.8f, 6 + 2.5f, 5.6f, 1.6f, 20, Sky), B(4.3f, 6 + 1.4f, 5.6f, 1.6f, 20, Lilac),
    ];

    /// <summary>The Odd One Out's p1, popped up and right out of its slot (the Pop Back splash's first moment: 10 right,
    /// 14 up, a quarter turn and a bit).</summary>
    public const float PoppedX = 10, PoppedY = -14, PoppedTurn = 25;

    /// <summary>The three little tick marks over a landing (Pop Back's, in the s's own units): one straight up, one
    /// either side.</summary>
    public static readonly (float X1, float Y1, float X2, float Y2)[] Ticks =
    [
        (43.3f, -4, 43.3f, -8), (48.3f, -2.5f, 51.3f, -5.5f), (38.3f, -2.5f, 35.3f, -5.5f),
    ];

    public static SKColor Sk(Color c, double opacity = 1) =>
        new(c.R, c.G, c.B, (byte)Math.Round(c.A * Math.Clamp(opacity, 0, 1)));

    /// <summary>One rounded cell at (x, y), <paramref name="side"/> across.</summary>
    public static void DrawCell(SKCanvas canvas, SKPaint paint, float x, float y, float side = Cell, float radius = Corner) =>
        canvas.DrawRoundRect(x, y, side, side, radius, radius, paint);

    /// <summary>A bit, turned about its own centre.</summary>
    public static void DrawBit(SKCanvas canvas, SKPaint paint, Bit bit, double opacity = 1)
    {
        paint.Color = Sk(bit.Fill, opacity);
        int saved = canvas.Save();
        canvas.Translate(bit.X, bit.Y);
        canvas.RotateDegrees(bit.Turn);
        DrawCell(canvas, paint, -bit.Side / 2, -bit.Side / 2, bit.Side, bit.Radius);
        canvas.RestoreToCount(saved);
    }

    /// <summary>The empty 6 × 7 grid (the mesh) at the s's own origin.</summary>
    public static void DrawMesh(SKCanvas canvas, SKPaint paint, Color colour, double opacity = 1)
    {
        paint.Color = Sk(colour, opacity);
        for (int row = 0; row < 7; row++)
            for (int col = 0; col < 6; col++)
                DrawCell(canvas, paint, col * Pitch, row * Pitch);
    }

    public static SKPaint Fill() => new() { IsAntialias = true, Style = SKPaintStyle.Fill };

    // ---- The wordmark: "syft" in Archivo Black Italic, " notes" in Archivo Regular, a little tight ----

    static SKTypeface? black, regular;

    static SKTypeface Face(ref SKTypeface? held, string file)
    {
        if (held is not null) return held;
        try
        {
            using var stream = AssetLoader.Open(new Uri($"avares://StudyStash/Assets/Fonts/{file}"));
            using var data = SKData.Create(stream);
            held = SKTypeface.FromData(data);
        }
        catch (Exception e) when (e is FileNotFoundException or InvalidOperationException or IOException)
        {
        }
        return held ??= SKTypeface.Default;
    }

    public static SKTypeface WordmarkBlack => Face(ref black, "Archivo-BlackItalic.ttf");
    public static SKTypeface WordmarkRegular => Face(ref regular, "Archivo-Regular.ttf");

    /// <summary>The wordmark's two runs and where each glyph goes, at <paramref name="size"/> with letter-spacing of
    /// <paramref name="spacing"/> after every letter (as SVG's letter-spacing does); and how wide it all is.</summary>
    static (List<(SKFont Font, ushort[] Glyphs, SKPoint[] At)> Runs, float Width) Lay(float size, float spacing)
    {
        var runs = new List<(SKFont, ushort[], SKPoint[])>();
        float x = 0;
        foreach (var (face, text) in new[] { (WordmarkBlack, "syft"), (WordmarkRegular, " notes") })
        {
            var font = new SKFont(face, size) { Subpixel = true, Edging = SKFontEdging.Antialias };
            var glyphs = font.GetGlyphs(text);
            var widths = font.GetGlyphWidths(glyphs);
            var at = new SKPoint[glyphs.Length];
            for (int i = 0; i < glyphs.Length; i++)
            {
                at[i] = new SKPoint(x, 0);
                x += widths[i] + spacing;
            }
            runs.Add((font, glyphs, at));
        }
        return (runs, x - spacing);
    }

    /// <summary>The width of the wordmark at <paramref name="size"/> (letter-spacing −0.02 em).</summary>
    public static float WordmarkWidth(float size) => Lay(size, -0.023f * size).Width;

    /// <summary>"syft notes" with its baseline at <paramref name="baseline"/>, centred on <paramref name="centreX"/>
    /// (or starting there when <paramref name="centred"/> is false). The motion reference's is 13 units with −0.3 of
    /// letter-spacing (about −0.02 em).</summary>
    public static void DrawWordmark(SKCanvas canvas, float centreX, float baseline, float size, SKColor colour, bool centred = true)
    {
        var (runs, width) = Lay(size, -0.3f * size / 13);
        float left = centred ? centreX - width / 2 : centreX;
        using var paint = new SKPaint { IsAntialias = true, Color = colour };
        foreach (var (font, glyphs, at) in runs)
        {
            using (font)
            {
                using var builder = new SKTextBlobBuilder();
                var run = builder.AllocatePositionedRun(font, glyphs.Length);
                var g = run.Glyphs;
                var p = run.Positions;
                for (int i = 0; i < glyphs.Length; i++)
                {
                    g[i] = glyphs[i];
                    p[i] = at[i];
                }
                using var blob = builder.Build();
                if (blob is not null) canvas.DrawText(blob, left, baseline, paint);
            }
        }
    }
}
