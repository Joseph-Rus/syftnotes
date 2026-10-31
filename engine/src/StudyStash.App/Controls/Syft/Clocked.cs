using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Rendering.Composition;
using SkiaSharp;
using StudyStash.App.Controls.Rich;

namespace StudyStash.App.Controls;

/// <summary>
/// Something the render thread moves on, the way the app's spinner did: a clock on the compositor asks for every
/// frame the screen draws and marks the control's square, and the compositor draws the control's own drawing there
/// afresh at the clock's new moment; so a UI thread busy building a lecture's notes or loading a page can't make it
/// stutter. It runs only while it can be seen (in a window that's showing, with nothing above it hidden) and while it
/// has something to move (<see cref="Moves"/>); otherwise the render loop can sleep. Drawn without the compositor (a
/// picture made with RenderTargetBitmap, the tests' STUDYSTASH_STILL=1) it draws one still moment, or <see cref="At"/>.
/// </summary>
public abstract class Clocked : Control
{
    readonly Moment moment = new();
    readonly Seen seen;
    CompositionCustomVisual? ticker;
    bool turning;

    protected Clocked() => seen = new Seen(this, _ => Sync());

    /// <summary>Whether its clock is running now.</summary>
    internal bool Turning => turning;

    /// <summary>A moment to draw, in seconds, for pictures: drawn still at that moment, never moved on.</summary>
    public double? At
    {
        get;
        set
        {
            field = value;
            Sync();
            InvalidateVisual();
        }
    }

    /// <summary>Whether there's anything to move just now (a loader that's busy, a splash still playing).</summary>
    protected abstract bool Moves { get; }

    /// <summary>The moment drawn when nothing moves it (no compositor, or STUDYSTASH_STILL).</summary>
    protected virtual double StillAt => 0;

    /// <summary>Seconds since its clock last started (or the still moment), as the UI thread last saw them.</summary>
    protected double Seconds => At ?? (turning ? moment.Seconds : StillAt);

    /// <summary>Starts or stops the clock to match: call when <see cref="Moves"/> may have changed.</summary>
    protected void Sync()
    {
        bool turn = seen.InView && ticker is not null && Moves && At is null && Environment.GetEnvironmentVariable("STUDYSTASH_STILL") != "1";
        if (turn == turning) return;
        turning = turn;
        if (!turn) moment.Seconds = StillAt;
        ticker?.SendHandlerMessage(turn);
        InvalidateVisual();
    }

    /// <summary>Tells the render thread to note the moment now (read back as <see cref="MarkedAt"/> in the drawing):
    /// the splash's fade starting, timed on the compositor's own clock.</summary>
    protected void Mark()
    {
        if (turning) ticker?.SendHandlerMessage(Marker);
        else moment.Mark = moment.Seconds;
    }

    static readonly object Marker = new();

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (ElementComposition.GetElementVisual(this)?.Compositor is not { } compositor) return;
        ticker = compositor.CreateCustomVisual(new Ticker(moment));
        ticker.Size = new Vector(Bounds.Width, Bounds.Height);
        ElementComposition.SetElementChildVisual(this, ticker);
        Sync();
    }

    /// <summary>Out of its window (or the window closed): stopped, and its visual let go, so nothing on the render
    /// thread keeps the closed window alive.</summary>
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

    /// <summary>Draws <paramref name="draw"/> into the control's square at the clock's moment, whenever the drawing is
    /// drawn: by the render thread each frame while the clock runs, once otherwise. What it's handed must be locals,
    /// not the control (the render thread can't read it): its seconds and the mark (NaN when there's none).</summary>
    protected void DrawClocked(DrawingContext context, Action<SKCanvas, double, double> draw)
    {
        var (at, still, fixedAt, running) = (moment, StillAt, At, turning);
        context.Custom(new SkiaOp(new Rect(Bounds.Size), canvas =>
        {
            double seconds = fixedAt ?? (running ? at.Seconds : still);
            draw(canvas, seconds, at.Mark);
        }));
    }

    /// <summary>The clock's moment, written by the render thread, read wherever the control is drawn.</summary>
    sealed class Moment
    {
        double seconds, mark = double.NaN;

        public double Seconds
        {
            get => Volatile.Read(ref seconds);
            set => Volatile.Write(ref seconds, value);
        }

        public double Mark
        {
            get => Volatile.Read(ref mark);
            set => Volatile.Write(ref mark, value);
        }
    }

    /// <summary>The clock, on the render thread: told to run (true) or stop (false), or to note the moment, by the UI
    /// thread. While it runs it asks for every frame, sets the moment from the compositor's clock and marks its square.
    /// It draws nothing itself.</summary>
    sealed class Ticker(Moment moment) : CompositionCustomVisualHandler
    {
        bool running;
        TimeSpan began;

        public override void OnMessage(object message)
        {
            if (message == Marker)
            {
                moment.Mark = (CompositionNow - began).TotalSeconds;
                return;
            }
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
