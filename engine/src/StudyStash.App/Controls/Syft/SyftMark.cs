using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using SkiaSharp;
using StudyStash.App.Controls.Rich;

namespace StudyStash.App.Controls;

/// <summary>How the pixel s is coloured (Joey's logo board).</summary>
public enum SyftLook
{
    /// <summary>Light in a light window, Dark in a dark one.</summary>
    Auto,
    /// <summary>Sorted: lilac, sky, coral bands and an ink bottom row; its bits tumble in above.</summary>
    Light,
    /// <summary>Night Mesh: the same bands with a mustard bottom row, over the empty grid; its bits fall out below.</summary>
    Dark,
    /// <summary>Each stroke its own colour (the Sieve Drop splash's).</summary>
    Segments,
    /// <summary>One colour, every cell (the menu bar's): <see cref="SyftMark.Foreground"/>, or ink (paper in a dark window).</summary>
    Ink,
    /// <summary>Odd One Out: the ink s with its top right cell popped up and out of its slot in coral, three ticks over it.</summary>
    OddOneOut,
}

/// <summary>What the mark stands on: nothing, the app icon's rounded square, or a circle (an avatar).</summary>
public enum SyftTile
{
    None,
    Square,
    Circle,
}

/// <summary>
/// Syft Notes' mark, the pixel s, drawn as vectors so it's sharp at any size: <c>&lt;c:SyftMark Size="26"/&gt;</c>.
/// <see cref="Size"/> is its height; it's as wide as its look needs (the s, its bits, a tile). It follows the window's
/// light or dark unless told a <see cref="Look"/>. The empty grid behind (<see cref="Mesh"/>) and the loose bits
/// (<see cref="Bits"/>) come with the looks that have them, and the bits go by themselves at sizes where they'd only
/// be specks. Screen readers hear "Syft Notes".
/// </summary>
public class SyftMark : Control
{
    public static readonly StyledProperty<double> SizeProperty = AvaloniaProperty.Register<SyftMark, double>(nameof(Size), 32);
    public static readonly StyledProperty<SyftLook> LookProperty = AvaloniaProperty.Register<SyftMark, SyftLook>(nameof(Look));
    public static readonly StyledProperty<bool?> MeshProperty = AvaloniaProperty.Register<SyftMark, bool?>(nameof(Mesh));
    public static readonly StyledProperty<bool?> BitsProperty = AvaloniaProperty.Register<SyftMark, bool?>(nameof(Bits));
    public static readonly StyledProperty<SyftTile> TileProperty = AvaloniaProperty.Register<SyftMark, SyftTile>(nameof(Tile));
    // Its own, not TextBlock's inherited one: the Ink look is ink (or paper) unless it's given a colour.
    public static readonly StyledProperty<IBrush?> ForegroundProperty = AvaloniaProperty.Register<SyftMark, IBrush?>(nameof(Foreground));

    /// <summary>The bits go below this many pixels a pitch (a cell about 2.5 px): smaller, they turn to mud.</summary>
    internal const double BitsFrom = 3;

    /// <summary>How far in from a tile's edge the mark sits, of the tile's side (his app icon ladder: about an eighth;
    /// more in a circle, which is narrower at its corners).</summary>
    internal static double TileInset(SyftTile tile) => tile == SyftTile.Circle ? 0.17 : 0.12;

    static SyftMark()
    {
        AffectsRender<SyftMark>(LookProperty, MeshProperty, BitsProperty, TileProperty, ForegroundProperty);
        AffectsMeasure<SyftMark>(SizeProperty, LookProperty, BitsProperty, TileProperty);
        AutomationProperties.NameProperty.OverrideDefaultValue<SyftMark>("Syft Notes");
    }

    public SyftMark() => ActualThemeVariantChanged += (_, _) =>
    {
        InvalidateMeasure();
        InvalidateVisual();
    };

    public double Size
    {
        get => GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    public SyftLook Look
    {
        get => GetValue(LookProperty);
        set => SetValue(LookProperty, value);
    }

    /// <summary>The empty grid behind the s: null for the look's own (only Dark has it).</summary>
    public bool? Mesh
    {
        get => GetValue(MeshProperty);
        set => SetValue(MeshProperty, value);
    }

    /// <summary>The loose bits: null for the look's own (Light above, Dark below; none for the rest), when there's room.</summary>
    public bool? Bits
    {
        get => GetValue(BitsProperty);
        set => SetValue(BitsProperty, value);
    }

    public SyftTile Tile
    {
        get => GetValue(TileProperty);
        set => SetValue(TileProperty, value);
    }

    /// <summary>The Ink look's colour (and Odd One Out's ink).</summary>
    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    bool DarkWindow => ActualThemeVariant == ThemeVariant.Dark;

    /// <summary>What the mark is drawn as, every choice made: the look, the colours, mesh, bits, tile, ink.</summary>
    internal readonly record struct Plan(SyftLook Look, Color[] Colours, Color? MeshColour, Syft.Bit[] Bits, SyftTile Tile, Color TileColour, Color InkColour);

    internal Plan Planned(double height)
    {
        var look = Look == SyftLook.Auto ? (DarkWindow ? SyftLook.Dark : SyftLook.Light) : Look;
        var ink = Foreground is ISolidColorBrush b ? b.Color : DarkWindow ? Syft.Paper : Syft.Ink;
        Color[] colours = look switch
        {
            SyftLook.Light => Syft.Sorted,
            SyftLook.Dark => Syft.Night,
            SyftLook.Segments => Syft.Segments,
            _ => [.. Enumerable.Repeat(ink, 24)],
        };
        bool dark = look == SyftLook.Dark;
        Color? mesh = (Mesh ?? dark) ? (dark || DarkWindow && Tile == SyftTile.None ? Syft.MeshDark : Syft.MeshLight) : null;
        var bits = Bits ?? true ? look switch { SyftLook.Light => Syft.BitsAbove, SyftLook.Dark => Syft.BitsBelow, _ => [] } : [];
        // Room for them? Worked out from the size with them; drop them if a pitch would come out too small.
        if (bits.Length > 0 && Bits is null && PitchPixels(height, look, bits, Tile) < BitsFrom) bits = [];
        var tile = dark || look == SyftLook.Ink && DarkWindow ? Syft.Ink : look == SyftLook.OddOneOut || look == SyftLook.Ink ? Syft.Paper : Syft.White;
        return new Plan(look, colours, mesh, bits, Tile, tile, ink);
    }

    /// <summary>The drawing's extent in the s's units: the s, its bits, Odd One Out's popped cell and ticks.</summary>
    internal static SKRect Extent(SyftLook look, Syft.Bit[] bits)
    {
        var r = Syft.Box;
        foreach (var bit in bits)
        {
            float half = (float)(bit.Side / 2 * (Math.Abs(Math.Cos(bit.Turn * Math.PI / 180)) + Math.Abs(Math.Sin(bit.Turn * Math.PI / 180))));
            r.Union(new SKRect(bit.X - half, bit.Y - half, bit.X + half, bit.Y + half));
        }
        if (look == SyftLook.OddOneOut)
            foreach (var (x1, y1, x2, y2) in Syft.Ticks)
                r.Union(new SKRect(Math.Min(x1, x2) + Syft.PoppedX - 0.6f, Math.Min(y1, y2) + Syft.PoppedY - 0.6f, Math.Max(x1, x2) + Syft.PoppedX + 0.6f, Math.Max(y1, y2) + Syft.PoppedY + 0.6f));
        return r;
    }

    static double PitchPixels(double height, SyftLook look, Syft.Bit[] bits, SyftTile tile)
    {
        var e = Extent(look, bits);
        double inner = tile == SyftTile.None ? height : height * (1 - 2 * TileInset(tile));
        double scale = Math.Min(inner / e.Height, (tile == SyftTile.None ? double.PositiveInfinity : inner) / e.Width);
        return scale * Syft.Pitch;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double h = Size;
        if (Tile != SyftTile.None) return new Size(h, h);
        var plan = Planned(h);
        var e = Extent(plan.Look, plan.Bits);
        return new Size(Math.Ceiling(h * e.Width / e.Height), h);
    }

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        Plan plan;
        Rect box;
        if (Tile != SyftTile.None)
        {
            // A tile fills its square, whatever the size says (an icon given a width and height).
            plan = Planned(Math.Min(bounds.Width, bounds.Height));
            box = bounds;
        }
        else
        {
            // Its own size, never stretched by the room it's given; centred in it.
            double h = Math.Min(Size, bounds.Height);
            plan = Planned(h);
            var e = Extent(plan.Look, plan.Bits);
            double w = Math.Min(bounds.Width, h * e.Width / e.Height);
            box = new Rect((bounds.Width - w) / 2, (bounds.Height - h) / 2, w, h);
        }
        context.Custom(new SkiaOp(bounds, canvas => Draw(canvas, box, plan)));
    }

    /// <summary>Draws a planned mark fitted (centred, never stretched) into <paramref name="box"/>.</summary>
    internal static void Draw(SKCanvas canvas, Rect box, Plan plan)
    {
        using var paint = Syft.Fill();
        var area = new SKRect((float)box.X, (float)box.Y, (float)box.Right, (float)box.Bottom);
        if (plan.Tile != SyftTile.None)
        {
            float side = Math.Min(area.Width, area.Height);
            var tile = SKRect.Create(area.MidX - side / 2, area.MidY - side / 2, side, side);
            paint.Color = Syft.Sk(plan.TileColour);
            if (plan.Tile == SyftTile.Circle) canvas.DrawOval(tile, paint);
            else canvas.DrawRoundRect(tile, side * 0.225f, side * 0.225f, paint);
            if (plan.TileColour == Syft.White || plan.TileColour == Syft.Paper)
            {
                // A white tile on a white page needs its edge: the boards' hairline.
                using var edge = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = Math.Max(1, side / 120f), Color = new SKColor(0x16, 0x13, 0x1D, 0x1A) };
                var rim = tile;
                rim.Inflate(-edge.StrokeWidth / 2, -edge.StrokeWidth / 2);
                if (plan.Tile == SyftTile.Circle) canvas.DrawOval(rim, edge);
                else canvas.DrawRoundRect(rim, side * 0.225f, side * 0.225f, edge);
            }
            area = tile;
            float inset = side * (float)TileInset(plan.Tile);
            area.Inflate(-inset, -inset);
        }
        var e = Extent(plan.Look, plan.Bits);
        float scale = Math.Min(area.Width / e.Width, area.Height / e.Height);
        int saved = canvas.Save();
        canvas.Translate(area.MidX - e.MidX * scale, area.MidY - e.MidY * scale);
        canvas.Scale(scale);
        if (plan.MeshColour is { } mesh) Syft.DrawMesh(canvas, paint, mesh);
        bool odd = plan.Look == SyftLook.OddOneOut;
        for (int i = 0; i < 24; i++)
        {
            if (odd && i == 1) continue;
            paint.Color = Syft.Sk(plan.Colours[i]);
            Syft.DrawCell(canvas, paint, Syft.Cells[i].X, Syft.Cells[i].Y);
        }
        foreach (var bit in plan.Bits) Syft.DrawBit(canvas, paint, bit);
        if (odd) DrawOdd(canvas, paint, plan.InkColour);
        canvas.RestoreToCount(saved);
    }

    /// <summary>Odd One Out's p1: its empty slot faintly outlined, the cell in coral up and to the right of it, turned, and
    /// the three ticks over it.</summary>
    static void DrawOdd(SKCanvas canvas, SKPaint paint, Color ink)
    {
        var (x, y) = Syft.Cells[1];
        using (var slot = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 0.6f, Color = Syft.Sk(ink, 0.3),
                   PathEffect = SKPathEffect.CreateDash([1.1f, 1.1f], 0) })
            canvas.DrawRoundRect(x + 0.3f, y + 0.3f, Syft.Cell - 0.6f, Syft.Cell - 0.6f, Syft.Corner, Syft.Corner, slot);
        Syft.DrawBit(canvas, paint, new Syft.Bit(x + Syft.Cell / 2 + Syft.PoppedX, y + Syft.Cell / 2 + Syft.PoppedY, Syft.Cell, Syft.Corner, Syft.PoppedTurn, Syft.Coral));
        using var tick = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.2f, StrokeCap = SKStrokeCap.Round, Color = Syft.Sk(Syft.Coral) };
        foreach (var (x1, y1, x2, y2) in Syft.Ticks)
            canvas.DrawLine(x1 + Syft.PoppedX, y1 + Syft.PoppedY, x2 + Syft.PoppedX, y2 + Syft.PoppedY, tick);
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new MarkPeer(this);

    sealed class MarkPeer(SyftMark owner) : ControlAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Image;
        protected override bool IsContentElementCore() => true;
        protected override bool IsControlElementCore() => true;
    }
}
