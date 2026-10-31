using System.Diagnostics;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using SkiaSharp;
using static StudyStash.App.Controls.Keyframes;

namespace StudyStash.App.Controls;

/// <summary>Which of the motion board's splash screens.</summary>
public enum SyftSplashKind
{
    /// <summary>Sort In in a dark window, Sieve Drop in a light one.</summary>
    Auto,
    /// <summary>S1: on ink, the s's cells rain in spinning and stack from the bottom up, then the wordmark rises.</summary>
    SortIn,
    /// <summary>S2: on white, the empty grid shows, loose bits fall straight through, the s lights up along its path.</summary>
    SieveDrop,
    /// <summary>S3: on paper, an ink s pops in row by row and its one coral bit hops home with three ticks.</summary>
    PopBack,
}

/// <summary>
/// One of Syft Notes' splash screens, its keyframes, easings, durations and delays exactly the motion reference's
/// (syft-motion.html), played once: the s comes together and the wordmark rises, and it holds there. Filling a window
/// it's drawn on its own ground, the reference's 100 × 200 stage as tall as the window, and when
/// <see cref="IsReady"/> it fades out over a quarter of a second and says <see cref="Finished"/>: never before
/// <see cref="MinSeconds"/>, and by <see cref="MaxSeconds"/> whether ready or not. <see cref="Inline"/>, it's just the
/// mark and wordmark, cut close, on whatever is behind, and stays. Moved by the render thread (<see cref="Clocked"/>);
/// with less motion asked for, it's the finished mark and wordmark fading in and out, nothing moving.
/// </summary>
public sealed class SyftSplash : Clocked
{
    public static readonly StyledProperty<SyftSplashKind> KindProperty = AvaloniaProperty.Register<SyftSplash, SyftSplashKind>(nameof(Kind));
    public static readonly StyledProperty<bool> InlineProperty = AvaloniaProperty.Register<SyftSplash, bool>(nameof(Inline));
    public static readonly StyledProperty<double> SizeProperty = AvaloniaProperty.Register<SyftSplash, double>(nameof(Size), 150);
    public static readonly StyledProperty<bool> IsReadyProperty = AvaloniaProperty.Register<SyftSplash, bool>(nameof(IsReady));

    public const double MinSeconds = 1.2, MaxSeconds = 4, FadeSeconds = 0.25;

    /// <summary>Inline, the stage is cut to this: the s with room over it for Pop Back's hop, and the wordmark under it.</summary>
    internal static readonly SKRect InlineFrame = new(12, 42, 88, 158);

    readonly Stopwatch shown = new();
    IDisposable? due;
    bool fading, finished, settled;

    static SyftSplash()
    {
        AffectsMeasure<SyftSplash>(InlineProperty, SizeProperty);
        AffectsRender<SyftSplash>(KindProperty, InlineProperty);
        AutomationProperties.NameProperty.OverrideDefaultValue<SyftSplash>("Syft Notes");
    }

    public SyftSplash() => ActualThemeVariantChanged += (_, _) => InvalidateVisual();

    public SyftSplashKind Kind
    {
        get => GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    /// <summary>Just the mark and wordmark, no ground, <see cref="Size"/> tall; it plays once and stays.</summary>
    public bool Inline
    {
        get => GetValue(InlineProperty);
        set => SetValue(InlineProperty, value);
    }

    /// <summary>Inline, its height.</summary>
    public double Size
    {
        get => GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    /// <summary>Whatever it covers is ready: it goes, once it has been up <see cref="MinSeconds"/>.</summary>
    public bool IsReady
    {
        get => GetValue(IsReadyProperty);
        set => SetValue(IsReadyProperty, value);
    }

    /// <summary>Faded out: whoever put it up takes it away.</summary>
    public event Action? Finished;

    /// <summary>The kind it plays, Auto settled by the window's light or dark.</summary>
    public SyftSplashKind Playing => Kind != SyftSplashKind.Auto ? Kind : ActualThemeVariant == ThemeVariant.Dark ? SyftSplashKind.SortIn : SyftSplashKind.SieveDrop;

    protected override bool Moves => !finished && !settled;

    /// <summary>Long after every keyframe has held: the finished mark and wordmark.</summary>
    protected override double StillAt => 60;

    /// <summary>When its "in" part has played out (the last element at its hold), in seconds.</summary>
    internal static double IntroSeconds(SyftSplashKind kind) => kind switch
    {
        SyftSplashKind.SortIn => 0.80 + 5 * 0.86,
        SyftSplashKind.SieveDrop => 2.4 + 2.7,
        _ => 0.56 + 4.5 * 0.88,
    };

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (shown.IsRunning || finished) return;
        shown.Start();
        if (Inline)
            // Played out, it rests: nothing left to move.
            Later(IntroSeconds(Playing) + 0.1, () =>
            {
                settled = true;
                Sync();
            });
        else
        {
            Later(MaxSeconds, FadeOut);
            if (IsReady) Ready();
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        due?.Dispose();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsReadyProperty && IsReady && shown.IsRunning && !Inline) Ready();
    }

    void Ready()
    {
        double left = MinSeconds - shown.Elapsed.TotalSeconds;
        if (left <= 0) FadeOut();
        else Later(left, FadeOut);
    }

    void Later(double seconds, Action then)
    {
        due?.Dispose();
        due = DispatcherTimer.RunOnce(then, TimeSpan.FromSeconds(Math.Max(0, seconds)));
    }

    /// <summary>Fades out over <see cref="FadeSeconds"/> (timed on the render thread's clock), then says it's finished.</summary>
    void FadeOut()
    {
        if (fading || finished) return;
        fading = true;
        Mark();
        InvalidateVisual();
        DispatcherTimer.RunOnce(() =>
        {
            finished = true;
            Sync();
            Finished?.Invoke();
        }, TimeSpan.FromSeconds(FadeSeconds + 0.02));
    }

    protected override Size MeasureOverride(Size availableSize) =>
        Inline ? new Size(Math.Ceiling(Size * InlineFrame.Width / InlineFrame.Height), Size) : default;

    public override void Render(DrawingContext context)
    {
        var box = new Rect(Bounds.Size);
        if (box.Width <= 0 || box.Height <= 0) return;
        var (kind, inline, reduced, fadingNow) = (Playing, Inline, Platform.Motion.Reduced, fading);
        // Inline on a dark page, Pop Back's ink would vanish: there it's paper.
        bool light = inline && ActualThemeVariant == ThemeVariant.Dark;
        double still = StillAt;
        DrawClocked(context, (canvas, seconds, mark) =>
        {
            // Less motion: the finished mark and wordmark, fading in (and out), nothing moving.
            double alpha = reduced ? Math.Clamp(seconds / FadeSeconds, 0, 1) : 1;
            // Going: faded from the moment the render thread noted (until it has, it's still all there).
            if (fadingNow && !double.IsNaN(mark)) alpha *= 1 - Ease.Out.Apply(Math.Clamp((seconds - mark) / FadeSeconds, 0, 1));
            if (alpha <= 0) return;
            Draw(canvas, box, kind, inline, reduced ? still : seconds, alpha, light);
        });
    }

    /// <summary>The splash <paramref name="seconds"/> in, at <paramref name="alpha"/>, in <paramref name="box"/>.</summary>
    internal static void Draw(SKCanvas canvas, Rect box, SyftSplashKind kind, bool inline, double seconds, double alpha = 1, bool onDark = false)
    {
        float w = (float)box.Width, h = (float)box.Height;
        canvas.ClipRect(new SKRect(0, 0, w, h));
        if (alpha < 1)
        {
            using var layer = new SKPaint { Color = SKColors.White.WithAlpha((byte)Math.Round(255 * alpha)) };
            canvas.SaveLayer(new SKRect(0, 0, w, h), layer);
        }
        using var paint = Syft.Fill();
        if (inline)
        {
            var f = InlineFrame;
            float scale = Math.Min(w / f.Width, h / f.Height);
            canvas.Translate((w - f.Width * scale) / 2 - f.Left * scale, (h - f.Height * scale) / 2 - f.Top * scale);
            canvas.Scale(scale);
        }
        else
        {
            paint.Color = Syft.Sk(kind switch { SyftSplashKind.SortIn => Syft.Ink, SyftSplashKind.SieveDrop => Syft.White, _ => Syft.Paper });
            canvas.DrawRect(0, 0, w, h, paint);
            // The reference's phone, as tall as the window (never smaller than twice its size), in the middle.
            float scale = Math.Max(2, h / 200);
            canvas.Translate((w - 100 * scale) / 2, (h - 200 * scale) / 2);
            canvas.Scale(scale);
        }
        switch (kind)
        {
            case SyftSplashKind.SortIn: SortIn(canvas, paint, seconds); break;
            case SyftSplashKind.SieveDrop: SieveDrop(canvas, paint, seconds); break;
            default: PopBack(canvas, paint, seconds, onDark ? Syft.Paper : Syft.Ink); break;
        }
    }

    /// <summary>A cell drawn at its pose: moved, turned and grown about its own centre (transform-box: fill-box).</summary>
    static void Cell(SKCanvas canvas, SKPaint paint, float x, float y, Color colour, Pose pose, float side = Syft.Cell, float radius = Syft.Corner)
    {
        if (pose.Opacity <= 0 || pose.Scale <= 0) return;
        paint.Color = Syft.Sk(colour, pose.Opacity);
        int saved = canvas.Save();
        canvas.Translate(x + side / 2 + (float)pose.X, y + side / 2 + (float)pose.Y);
        if (pose.Turn != 0) canvas.RotateDegrees((float)pose.Turn);
        if (pose.Scale != 1) canvas.Scale((float)pose.Scale);
        Syft.DrawCell(canvas, paint, -side / 2, -side / 2, side, radius);
        canvas.RestoreToCount(saved);
    }

    static void Word(SKCanvas canvas, Color colour, Pose pose)
    {
        if (pose.Opacity <= 0) return;
        Syft.DrawWordmark(canvas, 50, 152 + (float)pose.Y, 13, Syft.Sk(colour, pose.Opacity));
    }

    // ---- S1 · Sort In (5s): sf-in, cubic-bezier(.22,.9,.3,1), backwards; per cell start offset, spin and delay ----

    static readonly (double Dx, double Dy, double R, double Delay)[] SortStarts =
    [
        (-10, -92, 70, .64), (8, -86, -60, .76), (-12, -94, 90, .74), (10, -90, -110, .78), (-8, -84, 70, .72), (12, -92, -80, .80),
        (14, -100, -100, .62), (-6, -88, 50, .66), (10, -96, 110, .52), (-14, -104, -80, .55), (12, -95, 60, .40), (-8, -112, -120, .44),
        (15, -100, 80, .38), (-12, -108, -50, .42), (-14, -100, -70, .30), (9, -118, 100, .27), (-10, -110, 70, .16), (6, -120, -110, .20),
        (8, -110, 80, 0), (-12, -120, -60, .05), (14, -105, 120, .02), (-6, -125, -90, .08), (10, -115, 45, .04), (12, -105, 90, .14),
    ];

    static readonly Ease SortEase = new(.22, .9, .3, 1);

    internal static readonly Anim[] SortCells =
    [
        .. SortStarts.Select(s => new Anim(5, SortEase,
            T(0, s.Dx, s.Dy, s.R, opacity: 0), O(0.06, 1), T(0.26), T(0.29, y: -1.5), T(0.32), T(0.86, opacity: 1), T(0.96, opacity: 0), T(1, opacity: 0))
        { Backwards = true, Delay = s.Delay, Hold = 0.86 }),
    ];

    static readonly Anim SortWord = new Anim(5, Ease.Out, [.. At(T(0, y: 4, opacity: 0), 0, 0.36), .. At(T(0, opacity: 1), 0.46, 0.86), O(0.96, 0), O(1, 0)]).Once(0.86);

    static void SortIn(SKCanvas canvas, SKPaint paint, double t)
    {
        canvas.Save();
        canvas.Translate(26.7f, 70);
        Syft.DrawMesh(canvas, paint, Syft.MeshDark);
        for (int i = 0; i < 24; i++) Cell(canvas, paint, Syft.Cells[i].X, Syft.Cells[i].Y, Syft.Night[i], SortCells[i].At(t));
        canvas.Restore();
        Word(canvas, Syft.White, SortWord.At(t));
    }

    // ---- S2 · Sieve Drop (5s): the mesh fades in, bits fall through, cells pop in along the path ----

    static readonly Anim SieveMesh = new Anim(5, Ease.Default, O(0, 0), O(0.08, 1), O(0.88, 1), O(0.98, 0), O(1, 0)).Once(0.88);

    internal static readonly Anim[] SieveCells =
    [
        .. Enumerable.Range(0, 24).Select(i => new Anim(5, new Ease(.3, .7, .4, 1),
            T(0, scale: 0.3, opacity: 0), T(0.07, scale: 1.18, opacity: 1), T(0.11), T(0.86, opacity: 1), T(0.96, opacity: 0), T(1, opacity: 0))
        { Backwards = true, Delay = 0.5 + i * 0.05, Hold = 0.86 }),
    ];

    static readonly (float X, Color Fill, double Turn, double Duration, double Delay)[] SieveBits =
    [
        (30, Syft.Coral, 220, 2.6, 0), (46, Syft.Sky, -180, 3.1, .7), (58, Syft.Lilac, 260, 2.8, 1.4),
        (70, Syft.Mustard, -240, 3.3, .3), (38, Syft.Ink, 200, 2.9, 2), (64, Syft.Coral, -200, 2.7, 2.4),
    ];

    /// <summary>Each bit falls through once (the reference loops them; the app's splash plays once).</summary>
    static readonly Anim[] SieveFalls =
    [
        .. SieveBits.Select(b => new Anim(b.Duration, Ease.Linear, T(0), T(1, y: 240, turn: b.Turn)) { Backwards = true, Delay = b.Delay, Hold = 1 }),
    ];

    static readonly Anim SieveWord = new Anim(5, Ease.Out, [.. At(T(0, y: 4, opacity: 0), 0, 0.40), .. At(T(0, opacity: 1), 0.50, 0.86), O(0.96, 0), O(1, 0)]).Once(0.86);

    static void SieveDrop(SKCanvas canvas, SKPaint paint, double t)
    {
        canvas.Save();
        canvas.Translate(26.7f, 70);
        Syft.DrawMesh(canvas, paint, Syft.MeshLight, SieveMesh.At(t).Opacity);
        canvas.Restore();
        for (int i = 0; i < SieveBits.Length; i++)
        {
            var pose = SieveFalls[i].At(t);
            if (pose.Y >= 240) continue; // fallen through
            Cell(canvas, paint, SieveBits[i].X, -10, SieveBits[i].Fill, pose, 5.6f, 1.6f);
        }
        canvas.Save();
        canvas.Translate(26.7f, 70);
        for (int i = 0; i < 24; i++) Cell(canvas, paint, Syft.Cells[i].X, Syft.Cells[i].Y, Syft.Segments[i], SieveCells[i].At(t));
        canvas.Restore();
        Word(canvas, Syft.Ink, SieveWord.At(t));
    }

    // ---- S3 · Pop Back (4.5s): ink cells pop in row by row, the coral bit hops home with three ticks ----

    static readonly (float X, float Y)[][] PopRows =
    [
        [(8, 0), (16, 0), (24, 0), (32, 0)],
        [(0, 8), (8, 8), (40, 8)],
        [(0, 16), (8, 16)],
        [(8, 24), (16, 24), (24, 24), (32, 24)],
        [(32, 32), (40, 32)],
        [(0, 40), (32, 40), (40, 40)],
        [(0, 48), (8, 48), (16, 48), (24, 48), (32, 48)],
    ];

    static readonly Anim[] PopIns =
    [
        .. Enumerable.Range(0, 7).Select(r => new Anim(4.5, new Ease(.3, .7, .4, 1),
            T(0, scale: 0, opacity: 0), T(0.10, scale: 1.12, opacity: 1), T(0.14), T(0.88, opacity: 1), T(0.98, opacity: 0), T(1, opacity: 0))
        { Backwards = true, Delay = 0.20 + r * 0.06, Hold = 0.88 }),
    ];

    internal static readonly Anim PopHop = new Anim(4.5, new Ease(.4, 0, .2, 1),
    [
        .. At(T(0, 10, -14, 25, opacity: 0), 0, 0.20), T(0.26, 10, -14, 25, opacity: 1), T(0.40, 4, -22, -12), T(0.50), T(0.54, y: -2.5), T(0.58),
        T(0.88, opacity: 1), T(0.98, opacity: 0), T(1, opacity: 0),
    ]).Once(0.88);

    static readonly Anim PopTick = new Anim(4.5, Ease.Out, O(0, 0), O(0.49, 0), O(0.53, 1), O(0.66, 0), O(1, 0)).Once(0.88);

    static readonly Anim PopWord = new Anim(4.5, Ease.Out, [.. At(T(0, y: 4, opacity: 0), 0, 0.56), .. At(T(0, opacity: 1), 0.66, 0.88), O(0.98, 0), O(1, 0)]).Once(0.88);

    static void PopBack(SKCanvas canvas, SKPaint paint, double t, Color ink)
    {
        canvas.Save();
        canvas.Translate(26.7f, 70);
        for (int r = 0; r < PopRows.Length; r++)
        {
            var pose = PopIns[r].At(t);
            foreach (var (x, y) in PopRows[r]) Cell(canvas, paint, x, y, ink, pose);
        }
        Cell(canvas, paint, 40, 0, Syft.Coral, PopHop.At(t));
        canvas.Restore();
        double tick = PopTick.At(t).Opacity;
        if (tick > 0)
        {
            using var line = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.2f, StrokeCap = SKStrokeCap.Round, Color = Syft.Sk(Syft.Coral, tick) };
            canvas.DrawLine(70, 66, 70, 62, line);
            canvas.DrawLine(75, 67.5f, 78, 64.5f, line);
            canvas.DrawLine(65, 67.5f, 62, 64.5f, line);
        }
        Word(canvas, ink, PopWord.At(t));
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new SplashPeer(this);

    sealed class SplashPeer(SyftSplash owner) : ControlAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Image;
        protected override bool IsContentElementCore() => true;
        protected override bool IsControlElementCore() => true;
    }
}
