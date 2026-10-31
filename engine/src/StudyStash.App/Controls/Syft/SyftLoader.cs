using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using SkiaSharp;
using static StudyStash.App.Controls.Keyframes;

namespace StudyStash.App.Controls;

/// <summary>Which of the motion board's loaders.</summary>
public enum SyftLoaderKind
{
    /// <summary>L1: a lit trail runs along the s on its dark tile (2 s a lap). Something that fills a page: a recording,
    /// notes being written.</summary>
    PathTrace,
    /// <summary>L2: four bits hop and flip in a wave (1.2 s). Small busy spots: beside "Thinking…", in a row, on a step.</summary>
    HoppingBits,
    /// <summary>L3: ten cells fill left to right, then wipe (3 s); or light up to <see cref="SyftLoader.Progress"/>.
    /// Getting something in: a sync, a download, an install.</summary>
    PixelProgress,
}

/// <summary>
/// One of Syft Notes' loaders, timed exactly as the motion reference (syft-motion.html) has them, and moved by the render
/// thread (<see cref="Clocked"/>) so they stay smooth while the app is busy: <c>&lt;c:SyftLoader Kind="HoppingBits"
/// Size="14"/&gt;</c>. <see cref="Size"/> is its height. It moves only while it can be seen and is
/// <see cref="IsActive"/>; with less motion asked for (the system's setting) it only breathes, a slow fade in and out.
/// Screen readers hear "Loading", politely.
/// </summary>
public sealed class SyftLoader : Clocked
{
    public static readonly StyledProperty<SyftLoaderKind> KindProperty = AvaloniaProperty.Register<SyftLoader, SyftLoaderKind>(nameof(Kind));
    public static readonly StyledProperty<double> SizeProperty = AvaloniaProperty.Register<SyftLoader, double>(nameof(Size), 14);
    public static readonly StyledProperty<bool> IsActiveProperty = AvaloniaProperty.Register<SyftLoader, bool>(nameof(IsActive), true);
    public static readonly StyledProperty<double> ProgressProperty = AvaloniaProperty.Register<SyftLoader, double>(nameof(Progress));
    public static readonly StyledProperty<bool> IsIndeterminateProperty = AvaloniaProperty.Register<SyftLoader, bool>(nameof(IsIndeterminate), true);

    static SyftLoader()
    {
        AffectsMeasure<SyftLoader>(KindProperty, SizeProperty);
        AffectsRender<SyftLoader>(KindProperty, IsActiveProperty, ProgressProperty, IsIndeterminateProperty);
        AutomationProperties.NameProperty.OverrideDefaultValue<SyftLoader>("Loading");
        AutomationProperties.LiveSettingProperty.OverrideDefaultValue<SyftLoader>(AutomationLiveSetting.Polite);
    }

    public SyftLoader() => ActualThemeVariantChanged += (_, _) => InvalidateVisual();

    public SyftLoaderKind Kind
    {
        get => GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    public double Size
    {
        get => GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    /// <summary>Moving (true) or at rest: Path Trace at rest is the whole s lit (a recording that's paused).</summary>
    public bool IsActive
    {
        get => GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    /// <summary>Pixel Progress's real progress, 0–1, shown when <see cref="IsIndeterminate"/> is false.</summary>
    public double Progress
    {
        get => GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    public bool IsIndeterminate
    {
        get => GetValue(IsIndeterminateProperty);
        set => SetValue(IsIndeterminateProperty, value);
    }

    protected override bool Moves => IsActive && !(Kind == SyftLoaderKind.PixelProgress && !IsIndeterminate);

    /// <summary>A moment that shows each loader at work, for pictures drawn still.</summary>
    protected override double StillAt => Kind switch
    {
        SyftLoaderKind.PathTrace => 0.9,
        SyftLoaderKind.HoppingBits => 0.5,
        _ => 1.3,
    };

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsActiveProperty || change.Property == KindProperty || change.Property == IsIndeterminateProperty) Sync();
    }

    // ---- The reference's boxes, in its 100-unit tiles ----

    /// <summary>L1's tile is the whole 100 × 100; L2 is the bits' row and the room they hop and turn in; L3 the ten cells.</summary>
    internal static SKRect Frame(SyftLoaderKind kind) => kind switch
    {
        SyftLoaderKind.PathTrace => new SKRect(0, 0, 100, 100),
        SyftLoaderKind.HoppingBits => new SKRect(21.8f, 30, 78.2f, 54),
        _ => new SKRect(10.7f, 44, 89.3f, 50.6f),
    };

    protected override Size MeasureOverride(Size availableSize)
    {
        var f = Frame(Kind);
        return new Size(Math.Ceiling(Size * f.Width / f.Height), Size);
    }

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        // Its own size, never stretched by the room it's given; centred in it.
        var frame = Frame(Kind);
        double bh = Math.Min(Size, bounds.Height), bw = Math.Min(bounds.Width, bh * frame.Width / frame.Height);
        var box = new Rect((bounds.Width - bw) / 2, (bounds.Height - bh) / 2, bw, bh);
        var (kind, active, progress, determinate, dark, reduced, live) =
            (Kind, IsActive, Math.Clamp(Progress, 0, 1), !IsIndeterminate, ActualThemeVariant == ThemeVariant.Dark, Platform.Motion.Reduced, Turning || At is not null);
        DrawClocked(context, (canvas, seconds, _) =>
        {
            var f = Frame(kind);
            float scale = (float)Math.Min(box.Width / f.Width, box.Height / f.Height);
            canvas.Translate((float)(box.X + (box.Width - f.Width * scale) / 2) - f.Left * scale, (float)(box.Y + (box.Height - f.Height * scale) / 2) - f.Top * scale);
            canvas.Scale(scale);
            // Less motion: no hops, no trail, a slow breath (opacity only).
            double breath = reduced && active && live ? 0.6 + 0.4 * Math.Cos(seconds * Math.PI) : 1;
            switch (kind)
            {
                case SyftLoaderKind.PathTrace: DrawTrace(canvas, seconds, active && !reduced, breath); break;
                case SyftLoaderKind.HoppingBits: DrawHops(canvas, seconds, active && !reduced, breath); break;
                default: DrawProgress(canvas, seconds, determinate ? progress : null, active && !reduced, breath, dark); break;
            }
        });
    }

    // ---- L1 · Path Trace: tr 2s linear infinite backwards, delay = index × 0.075s ----

    static readonly Anim Trace = new(2, Ease.Linear, O(0, 0), O(0.06, 1), O(0.30, 1), O(0.52, 0), O(1, 0)) { Backwards = true };
    internal static readonly Anim[] Traces = [.. Enumerable.Range(0, 24).Select(i => Trace.After(i * 0.075))];

    internal static void DrawTrace(SKCanvas canvas, double seconds, bool moving, double breath)
    {
        using var paint = Syft.Fill();
        paint.Color = Syft.Sk(Syft.Ink);
        canvas.DrawRoundRect(0, 0, 100, 100, 12.3f, 12.3f, paint);
        canvas.Translate(26.7f, 22.7f);
        Syft.DrawMesh(canvas, paint, Syft.MeshTrace);
        paint.Color = Syft.Sk(Syft.GhostDark);
        foreach (var (x, y) in Syft.Cells) Syft.DrawCell(canvas, paint, x, y);
        for (int i = 0; i < 24; i++)
        {
            double opacity = moving ? Traces[i].At(seconds).Opacity : breath;
            if (opacity <= 0) continue;
            paint.Color = Syft.Sk(Syft.Night[i], opacity);
            Syft.DrawCell(canvas, paint, Syft.Cells[i].X, Syft.Cells[i].Y);
        }
    }

    // ---- L2 · Hopping Bits: bb 1.2s ease-in-out infinite, delays 0, .12, .24, .36 ----

    static readonly Anim Hop = new(1.2, Ease.InOut, [.. At(T(0), 0, 0.6, 1), T(0.3, y: -14, turn: 90)]);
    internal static readonly Anim[] Hops = [.. Enumerable.Range(0, 4).Select(i => Hop.After(i * 0.12))];
    static readonly (float X, Color Fill)[] HopBits = [(24, Syft.Lilac), (38, Syft.Sky), (52, Syft.Coral), (66, Syft.Mustard)];

    internal static void DrawHops(SKCanvas canvas, double seconds, bool moving, double breath)
    {
        using var paint = Syft.Fill();
        for (int i = 0; i < 4; i++)
        {
            var pose = moving ? Hops[i].At(seconds) : Pose.Rest;
            paint.Color = Syft.Sk(HopBits[i].Fill, breath);
            int saved = canvas.Save();
            canvas.Translate(HopBits[i].X + 5 + (float)pose.X, 49 + (float)pose.Y);
            canvas.RotateDegrees((float)pose.Turn);
            canvas.DrawRoundRect(-5, -5, 10, 10, 2.8f, 2.8f, paint);
            canvas.RestoreToCount(saved);
        }
    }

    // ---- L3 · Pixel Progress: pg 3s ease-out infinite backwards, delay = index × 0.15s ----

    static readonly Anim Fill = new(3, Ease.Out,
        T(0, scale: 0.4, opacity: 0), T(0.06, opacity: 1), T(0.78, opacity: 1), T(0.88, opacity: 0), T(1, opacity: 0)) { Backwards = true };
    internal static readonly Anim[] Fills = [.. Enumerable.Range(0, 10).Select(i => Fill.After(i * 0.15))];
    static readonly Color[] FillColours = [Syft.Lilac, Syft.Lilac, Syft.Lilac, Syft.Sky, Syft.Sky, Syft.Coral, Syft.Coral, Syft.Coral, Syft.Mustard, Syft.Mustard];

    internal static void DrawProgress(SKCanvas canvas, double seconds, double? progress, bool moving, double breath, bool dark)
    {
        using var paint = Syft.Fill();
        paint.Color = Syft.Sk(dark ? Syft.MeshDark : Syft.MeshLight);
        for (int i = 0; i < 10; i++) Syft.DrawCell(canvas, paint, 10.7f + i * 8, 44);
        for (int i = 0; i < 10; i++)
        {
            Pose pose;
            if (progress is { } p)
            {
                // Real progress: each tenth lights its cell, the one in hand growing in as it goes.
                double part = Math.Clamp(p * 10 - i, 0, 1);
                pose = Pose.Rest with { Scale = 0.4 + 0.6 * part, Opacity = part };
            }
            else pose = moving ? Fills[i].At(seconds) : Pose.Rest with { Opacity = breath };
            if (pose.Opacity <= 0) continue;
            paint.Color = Syft.Sk(FillColours[i], pose.Opacity);
            int saved = canvas.Save();
            canvas.Translate(10.7f + i * 8 + Syft.Cell / 2, 44 + Syft.Cell / 2);
            canvas.Scale((float)pose.Scale);
            Syft.DrawCell(canvas, paint, -Syft.Cell / 2, -Syft.Cell / 2);
            canvas.RestoreToCount(saved);
        }
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new LoaderPeer(this);

    sealed class LoaderPeer(SyftLoader owner) : ControlAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.ProgressBar;
        protected override bool IsContentElementCore() => true;
        protected override bool IsControlElementCore() => true;
    }
}
