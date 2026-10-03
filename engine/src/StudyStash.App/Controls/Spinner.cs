using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace StudyStash.App.Controls;

/// <summary>Busy: a quarter arc going round (Fluent's ProgressRing, small), in the accent. It only turns while it can be
/// seen: a spinner in a panel that's hidden (an "Attaching…" row nothing is attaching to), or in a window that's
/// closed to the menu bar, would otherwise wake the app thirty times a second, for as long as the app runs, to
/// redraw something nobody is looking at.</summary>
public sealed class Spinner : Control
{
    public static readonly StyledProperty<IBrush?> ForegroundProperty = TextBlock.ForegroundProperty.AddOwner<Spinner>();
    public static readonly StyledProperty<double> ThicknessProperty = AvaloniaProperty.Register<Spinner, double>(nameof(Thickness), 2);

    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    double angle;

    public Spinner()
    {
        timer.Tick += (_, _) =>
        {
            angle = (angle + 12) % 360;
            InvalidateVisual();
        };
        _ = new Seen(this, Sync);
    }

    /// <summary>Whether it's turning: only while it's on screen (in a window that's showing, with nothing above it hidden).</summary>
    internal bool Turning => timer.IsEnabled;

    void Sync(bool inView)
    {
        if (inView && Environment.GetEnvironmentVariable("STUDYSTASH_STILL") != "1") timer.Start();
        else timer.Stop();
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

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        timer.Stop();
    }

    public override void Render(DrawingContext context)
    {
        double r = Math.Min(Bounds.Width, Bounds.Height) / 2 - Thickness / 2;
        if (r <= 0) return;
        var c = new Point(Bounds.Width / 2, Bounds.Height / 2);
        double a0 = (angle - 90) * Math.PI / 180, a1 = a0 + Math.PI / 2;
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(new Point(c.X + r * Math.Cos(a0), c.Y + r * Math.Sin(a0)), false);
            ctx.ArcTo(new Point(c.X + r * Math.Cos(a1), c.Y + r * Math.Sin(a1)), new Size(r, r), 0, false, SweepDirection.Clockwise);
        }
        context.DrawGeometry(null, new Pen(Foreground ?? Brushes.Gray, Thickness, lineCap: PenLineCap.Round), g);
    }
}
