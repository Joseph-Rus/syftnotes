using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace StudyStash.App.Controls;

/// <summary>The recording's loudness as bars, 3 px wide with 2 px between, centered and rounded: the recorder's and the
/// panel's waveform. <see cref="Levels"/> is 0 to 1, oldest first; the newest bars are drawn. While one is bound to a
/// recording and on screen, it reads the newest levels itself (<see cref="Live"/>) twenty times a second, one bar's
/// worth of sound (50 ms) at a time, so it moves with the voice: the app's own tick, four times a second, made it jump.
/// Off screen it doesn't wake the app at all.</summary>
public sealed class Waveform : Control
{
    /// <summary>The recording's newest levels, as the recorder has them now; null while nothing records.</summary>
    public static Func<IReadOnlyList<double>?>? Live { get; set; }

    public static readonly TimeSpan Step = TimeSpan.FromMilliseconds(50);

    readonly DispatcherTimer timer = new(DispatcherPriority.Normal) { Interval = Step };
    IReadOnlyList<double>? live;

    public Waveform()
    {
        timer.Tick += (_, _) =>
        {
            // Only a waveform showing a recording follows it (its Levels are bound to one, null otherwise).
            var now = Levels is null ? null : Live?.Invoke();
            if (now is null && live is null) return;
            live = now;
            InvalidateVisual();
        };
        _ = new Seen(this, inView =>
        {
            if (inView) timer.Start();
            else
            {
                timer.Stop();
                live = null;
            }
        });
    }

    /// <summary>Whether it's following the recording now: only while it's on screen.</summary>
    internal bool Following => timer.IsEnabled;

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        timer.Stop();
    }

    public static readonly StyledProperty<IReadOnlyList<double>?> LevelsProperty = AvaloniaProperty.Register<Waveform, IReadOnlyList<double>?>(nameof(Levels));
    public static readonly StyledProperty<int> BarsProperty = AvaloniaProperty.Register<Waveform, int>(nameof(Bars), 20);
    public static readonly StyledProperty<IBrush?> ForegroundProperty = TextBlock.ForegroundProperty.AddOwner<Waveform>();
    public static readonly StyledProperty<double> MinBarProperty = AvaloniaProperty.Register<Waveform, double>(nameof(MinBar), 4);

    /// <summary>Its bars stretched to the last few seconds' loudness (the pill's three): a lecturer across the room is
    /// -35 to -25 dBFS, which on the plain scale moved a 12 px bar between 5 and 7 px.</summary>
    public static readonly StyledProperty<bool> LivelyProperty = AvaloniaProperty.Register<Waveform, bool>(nameof(Lively));

    /// <summary>Below this (-45 dBFS) a lively waveform is flat: a quiet room's hum.</summary>
    const double Hush = 0.25;

    static Waveform() => AffectsRender<Waveform>(LevelsProperty, BarsProperty, ForegroundProperty, LivelyProperty);

    public bool Lively
    {
        get => GetValue(LivelyProperty);
        set => SetValue(LivelyProperty, value);
    }

    public IReadOnlyList<double>? Levels
    {
        get => GetValue(LevelsProperty);
        set => SetValue(LevelsProperty, value);
    }

    public int Bars
    {
        get => GetValue(BarsProperty);
        set => SetValue(BarsProperty, value);
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public double MinBar
    {
        get => GetValue(MinBarProperty);
        set => SetValue(MinBarProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? Bars * 5 - 2 : Math.Min(availableSize.Width, Bars * 5 - 2), double.IsInfinity(availableSize.Height) ? 28 : availableSize.Height);

    public override void Render(DrawingContext context)
    {
        var levels = Levels is null ? null : live ?? Levels;
        var brush = Foreground ?? Brushes.Gray;
        double h = Bounds.Height;
        // Lively: the loudest of the last 3 s fills the bar, and anything at the room's hum or under is flat.
        double span = Lively && levels is { Count: > 0 } ? Math.Max(levels.Max() - Hush, 0.15) : 0;
        for (int i = 0; i < Bars; i++)
        {
            double level = levels is { Count: > 0 } ? levels[Math.Max(0, levels.Count - Bars + i)] : 0;
            if (span > 0) level = Math.Clamp((level - Hush) / span, 0, 1);
            double bar = Math.Max(MinBar, Math.Min(h, level * h));
            context.DrawRectangle(brush, null, new RoundedRect(new Rect(i * 5, (h - bar) / 2, 3, bar), 2));
        }
    }
}
