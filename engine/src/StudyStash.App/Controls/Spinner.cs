using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Rendering.Composition;
using SkiaSharp;
using StudyStash.App.Controls.Rich;

namespace StudyStash.App.Controls;

/// <summary>
/// Busy: an arc going round a faint ring of its own colour, growing long and short again as it goes (the rhythm of the
/// loading circles people know), in the colour it's given. The render thread moves it, at the screen's own pace (120
/// frames a second on a Mac that shows that many), not the UI thread: it used to be turned by a timer on the UI thread,
/// so whenever the app was busy there (a lecture's notes being built, a page loading) the ticks came late and the arc
/// jumped, which is just when a spinner is on screen. It only moves while it can be seen: a spinner in a panel that's
/// hidden (an "Attaching…" row nothing is attaching to), or in a window that's closed to the menu bar, would otherwise
/// wake the app every frame, for as long as it runs, to redraw something nobody is looking at.
/// </summary>
public sealed class Spinner : Control
{
    public static readonly StyledProperty<IBrush?> ForegroundProperty = TextBlock.ForegroundProperty.AddOwner<Spinner>();
    public static readonly StyledProperty<double> ThicknessProperty = AvaloniaProperty.Register<Spinner, double>(nameof(Thickness), 2);

    /// <summary>One grow and shrink of the arc, in seconds.</summary>
    internal const double Period = 1.4;

    /// <summary>How long the arc is at its shortest and at its longest, in degrees.</summary>
    internal const double Shortest = 20, Longest = 270;

    /// <summary>The whole thing also turns steadily, once every this many seconds, so even the end that's waiting for
    /// the other never stands still.</summary>
    internal const double TurnSeconds = 1.8;

    /// <summary>The ring under the arc: the arc's colour, this much of it.</summary>
    internal const double RingOpacity = 0.15;

    /// <summary>The moment drawn when it doesn't move (the pictures the tests take): the arc from just short of the top
    /// round to about four o'clock.</summary>
    internal const double StillAt = 1.06;

    static Spinner() => AffectsRender<Spinner>(ForegroundProperty, ThicknessProperty);

    /// <summary>How far into its turning it is, which the render thread moves on each frame.</summary>
    readonly Moment moment = new();

    /// <summary>What moves it on, on the render thread, while it's in a window.</summary>
    CompositionCustomVisual? ticker;
    bool turning;

    public Spinner() => _ = new Seen(this, Sync);

    /// <summary>Whether it's turning: only while it's on screen (in a window that's showing, with nothing above it hidden).</summary>
    internal bool Turning => turning;

    void Sync(bool inView)
    {
        bool turn = inView && ticker is not null && Environment.GetEnvironmentVariable("STUDYSTASH_STILL") != "1";
        if (turn == turning) return;
        turning = turn;
        ticker?.SendHandlerMessage(turn);
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public double Thickness
    {
        get => GetValue(ThicknessProperty);
        set => SetValue(ThicknessProperty, value);
    }

    /// <summary>In a window: a visual of its own under its drawing, for the render thread to keep moving on. (The
    /// control's own visual only exists while it's in a window, and so does this one.)</summary>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (ElementComposition.GetElementVisual(this)?.Compositor is not { } compositor) return;
        ticker = compositor.CreateCustomVisual(new Ticker(moment));
        ticker.Size = new Vector(Bounds.Width, Bounds.Height);
        ElementComposition.SetElementChildVisual(this, ticker);
    }

    /// <summary>Out of its window (or the window closed): stopped, and its visual let go, so nothing on the render
    /// thread goes on moving it or keeps the closed window's spinner, and with it the window, alive.</summary>
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (ticker is null) return;
        ticker.SendHandlerMessage(false);
        turning = false;
        ElementComposition.SetElementChildVisual(this, null);
        ticker = null;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (ticker is not null && change.Property == BoundsProperty
            && change.GetOldValue<Rect>().Size != change.GetNewValue<Rect>().Size)
            ticker.Size = new Vector(Bounds.Width, Bounds.Height);
    }

    /// <summary>
    /// Draws the moment it has got to whenever its drawing is drawn: by the render thread each frame while it turns
    /// (the ticker marks its square, and the compositor draws what's there afresh), and once, at that moment, when the
    /// control is drawn on its own (a picture of a window made with RenderTargetBitmap, which draws only the controls'
    /// own drawing, never a visual of the compositor's like the ticker, so the ticker can't be what draws it). Its
    /// colour is a plain brush's (grey for any other kind), as see-through as the brush is; a theme switched between
    /// light and dark gives it a new brush, and it's drawn again.
    /// </summary>
    public override void Render(DrawingContext context)
    {
        var brush = Foreground;
        var c = brush is ISolidColorBrush solid ? solid.Color : Colors.Gray;
        double opacity = Math.Clamp(brush?.Opacity ?? 1, 0, 1);
        var colour = new SKColor(c.R, c.G, c.B, (byte)Math.Round(c.A * opacity));
        float thickness = (float)Math.Max(0, Thickness);
        // Locals, so what the render thread keeps of the drawing holds the moment, not the control.
        var (w, h, at) = (Bounds.Width, Bounds.Height, moment);
        context.Custom(new SkiaOp(new Rect(Bounds.Size), canvas => Draw(canvas, w, h, colour, thickness, at.Seconds)));
    }

    /// <summary>
    /// Where the arc is <paramref name="seconds"/> after it started: where it begins (degrees clockwise from the top)
    /// and how far round it goes. In the first half of each period its front end runs ahead, eased, from the shortest
    /// arc to the longest; in the second its back end catches up; and the whole thing turns steadily all the while.
    /// Each period starts where the last one ended, so it never jumps.
    /// </summary>
    internal static (double Start, double Sweep) Pose(double seconds)
    {
        const double grow = Longest - Shortest;
        double periods = seconds / Period, done = Math.Floor(periods), into = periods - done;
        double back = done * grow + seconds / TurnSeconds * 360, front;
        if (into < 0.5) front = back + Shortest + grow * Ease(into * 2);
        else
        {
            front = back + Longest;
            back += grow * Ease(into * 2 - 1);
        }
        double start = back % 360;
        return (start < 0 ? start + 360 : start, front - back);
    }

    /// <summary>Slow to start, quick in the middle, slow to stop (a cubic each way).</summary>
    static double Ease(double t) => t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;

    /// <summary>
    /// Draws the ring and the arc <paramref name="seconds"/> in, centred in a <paramref name="width"/> by
    /// <paramref name="height"/> box: round ends, smoothed edges, one flat colour (a see-through one drawn as a whole, so
    /// the arc over the ring is no darker than the arc).
    /// </summary>
    internal static void Draw(SKCanvas canvas, double width, double height, SKColor colour, float thickness, double seconds)
    {
        float r = (float)(Math.Min(width, height) / 2 - thickness / 2.0);
        if (r <= 0 || thickness <= 0 || colour.Alpha == 0) return;
        float cx = (float)(width / 2), cy = (float)(height / 2);
        var solid = colour.WithAlpha(255);
        int saved = canvas.Save();
        try
        {
            if (colour.Alpha < 255)
            {
                using var whole = new SKPaint { Color = SKColors.White.WithAlpha(colour.Alpha) };
                canvas.SaveLayer(new SKRect(0, 0, (float)width, (float)height), whole);
            }
            using var paint = new SKPaint
            {
                IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = thickness, StrokeCap = SKStrokeCap.Round,
                Color = solid.WithAlpha((byte)Math.Round(255 * RingOpacity)),
            };
            canvas.DrawCircle(cx, cy, r, paint);
            var (start, sweep) = Pose(seconds);
            paint.Color = solid;
            // Skia counts from three o'clock; the pose from the top.
            canvas.DrawArc(new SKRect(cx - r, cy - r, cx + r, cy + r), (float)(start - 90), (float)sweep, false, paint);
        }
        finally
        {
            canvas.RestoreToCount(saved);
        }
    }

    /// <summary>How far into its turning a spinner is, in seconds: moved on by the render thread, read wherever it's
    /// drawn. Until it first turns it's at the still moment.</summary>
    sealed class Moment
    {
        double seconds = StillAt;

        public double Seconds
        {
            get => Volatile.Read(ref seconds);
            set => Volatile.Write(ref seconds, value);
        }
    }

    /// <summary>
    /// The spinner's clock, on the render thread. Told to run (true) or stop (false) by the UI thread; while it runs it
    /// asks for every frame the screen draws, sets the moment from the compositor's own clock and marks its square to be
    /// drawn again, and the compositor, drawing that square afresh, draws the spinner's own drawing there at that
    /// moment. So a busy UI thread can't hold it back. Stopped, it asks for nothing more, and the render loop can sleep.
    /// It draws nothing itself.
    /// </summary>
    sealed class Ticker(Moment moment) : CompositionCustomVisualHandler
    {
        bool running;
        TimeSpan began;

        public override void OnMessage(object message)
        {
            if (message is not bool run || run == running) return;
            running = run;
            if (!run) return;
            began = CompositionNow;
            moment.Seconds = 0;
            Invalidate();
            RegisterForNextAnimationFrameUpdate();
        }

        public override void OnAnimationFrameUpdate()
        {
            if (!running) return;
            moment.Seconds = (CompositionNow - began).TotalSeconds;
            Invalidate();
            RegisterForNextAnimationFrameUpdate();
        }

        public override void OnRender(ImmediateDrawingContext context)
        {
        }
    }
}
