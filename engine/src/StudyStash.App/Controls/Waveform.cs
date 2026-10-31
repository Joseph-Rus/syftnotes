using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;

namespace StudyStash.App.Controls;

/// <summary>
/// The recording's loudness as columns of the pixel s's cells, so it's all in order: each column four cells on the
/// mark's grid (pitch 8, cell 6.6, corners 1.8), scaled to its height and laid on whole pixels so the gaps stay
/// crisp, lit from the bottom by the level a whole cell at a time in <see cref="Foreground"/> (the recording colour),
/// the rest the mesh's quiet fill. The pills' three, the note page's bar's five, the dropdown's and setup's microphone
/// check's longer rows. <see cref="Levels"/> is 0 to 1, oldest first; the newest columns are drawn. While one is bound
/// to a recording and on screen, it reads the newest levels itself (<see cref="Live"/>) twenty times a second, one
/// column's worth of sound (50 ms) at a time, so it moves with the voice: the app's own tick, four times a second, made
/// it jump. A cell lights or goes out by its opacity alone, quickly (<see cref="Fade"/>), moved by the render thread
/// (<see cref="Clocked"/>) and only while a cell is changing; with less motion asked for it just switches. Off screen
/// it doesn't wake the app at all.
/// </summary>
public sealed class Waveform : Clocked
{
    /// <summary>The recording's newest levels, as the recorder has them now; null while nothing records.</summary>
    public static Func<IReadOnlyList<double>?>? Live { get; set; }

    public static readonly TimeSpan Step = TimeSpan.FromMilliseconds(50);

    /// <summary>Cells in a column.</summary>
    public const int Rows = 4;

    /// <summary>How long a cell takes to light or go out, in seconds.</summary>
    public const double Fade = 0.08;

    readonly DispatcherTimer timer = new(DispatcherPriority.Normal) { Interval = Step };
    IReadOnlyList<double>? live;
    // Each cell (column by column, the bottom first) lit (1) or out (0) now, what it showed when that changed, and when
    // that was: seconds since the clock last started, by the UI thread's watch (0 for a change that starts it).
    float[] from = [], to = [];
    double changed;
    bool fading;
    long started;

    public Waveform()
    {
        timer.Tick += (_, _) =>
        {
            // Only a waveform showing a recording follows it (its Levels are bound to one, null otherwise).
            var now = Levels is null ? null : Live?.Invoke();
            if (now is null && live is null)
            {
                if (fading) Light(); // to stop the clock once its fades are done
                return;
            }
            live = now;
            Light();
        };
        _ = new Seen(this, inView =>
        {
            if (inView) timer.Start();
            else
            {
                timer.Stop();
                live = null;
                Light();
            }
        });
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
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

    /// <summary>Its columns stretched to the last few seconds' loudness (the meters by the recording's time): a
    /// lecturer across the room is -35 to -25 dBFS, which on the plain scale only ever lit the same two cells. The
    /// room's hum or under is the bottom cell alone, so a quiet room still shows it's live.</summary>
    public static readonly StyledProperty<bool> LivelyProperty = AvaloniaProperty.Register<Waveform, bool>(nameof(Lively));

    /// <summary>Below this (-45 dBFS) a lively waveform is flat: a quiet room's hum.</summary>
    const double Hush = 0.25;

    static Waveform()
    {
        AffectsMeasure<Waveform>(BarsProperty);
        AffectsRender<Waveform>(ForegroundProperty);
    }

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

    /// <summary>How many columns.</summary>
    public int Bars
    {
        get => GetValue(BarsProperty);
        set => SetValue(BarsProperty, value);
    }

    /// <summary>The lit cells' colour.</summary>
    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == LevelsProperty || change.Property == BarsProperty || change.Property == LivelyProperty) Light();
    }

    protected override bool Moves => fading;

    /// <summary>Drawn still, every cell has finished changing.</summary>
    protected override double StillAt => 1e6;

    double Elapsed() => Stopwatch.GetElapsedTime(started).TotalSeconds;

    /// <summary>Which cells the levels light, column by column, the bottom first: the loudness on four steps, a whole
    /// cell each (a lively one at least the bottom cell); none at all while nothing records.</summary>
    internal static float[] Lit(IReadOnlyList<double>? levels, int columns, bool lively)
    {
        var lit = new float[Math.Max(0, columns) * Rows];
        if (levels is null) return lit;
        // Lively: the loudest of the last 3 s fills the column, and anything at the room's hum or under is flat.
        double span = lively && levels.Count > 0 ? Math.Max(levels.Max() - Hush, 0.15) : 0;
        for (int col = 0; col < columns; col++)
        {
            double level = levels.Count > 0 ? levels[Math.Max(0, levels.Count - columns + col)] : 0;
            if (span > 0) level = Math.Clamp((level - Hush) / span, 0, 1);
            int cells = Math.Clamp((int)Math.Round(level * Rows, MidpointRounding.AwayFromZero), lively ? 1 : 0, Rows);
            for (int row = 0; row < cells; row++) lit[col * Rows + row] = 1;
        }
        return lit;
    }

    /// <summary>Lights the cells for the levels now: the ones that change fade there on the render thread's clock (or
    /// switch, with less motion), and a clock whose fades are all done stops.</summary>
    void Light()
    {
        var lit = Lit(Levels is null ? null : live ?? Levels, Bars, Lively);
        if (lit.AsSpan().SequenceEqual(to))
        {
            if (fading && Elapsed() >= changed + Fade)
            {
                fading = false;
                Sync();
            }
            return;
        }
        bool fade = to.Length == lit.Length && !Platform.Motion.Reduced;
        var was = lit;
        if (fade)
        {
            // Each cell from what it shows this moment, mid-fade or not.
            double now = Turning ? Elapsed() : changed + Fade;
            was = new float[lit.Length];
            for (int i = 0; i < lit.Length; i++) was[i] = Shown(from[i], to[i], now - changed);
        }
        if (!Turning) started = Stopwatch.GetTimestamp();
        (from, to, changed, fading) = (was, lit, Turning ? Elapsed() : 0, fade);
        Sync();
        InvalidateVisual();
    }

    /// <summary>A cell <paramref name="seconds"/> into a change from <paramref name="was"/> to <paramref name="now"/>.</summary>
    static float Shown(float was, float now, double seconds) => was + (now - was) * (float)Math.Clamp(seconds / Fade, 0, 1);

    /// <summary>The pitch and the cell in a column <paramref name="height"/> tall, on whole pixels at
    /// <paramref name="scale"/>: the mark's 8 to 6.6, as near as whole pixels go, with a pixel at least between.</summary>
    internal static (double Pitch, double Cell) Grid(double height, double scale)
    {
        double pixels = Math.Floor(height * scale + 1e-6), unit = pixels / ((Rows - 1) * Syft.Pitch + Syft.Cell);
        int pitch = Math.Max(2, (int)Math.Round(Syft.Pitch * unit)), cell = Math.Max(1, (int)Math.Round(Syft.Cell * unit));
        while (true)
        {
            cell = Math.Min(cell, pitch - 1);
            if ((Rows - 1) * pitch + cell <= pixels || pitch <= 2) break;
            pitch--;
        }
        return (pitch / scale, cell / scale);
    }

    double Scale => TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;

    protected override Size MeasureOverride(Size availableSize)
    {
        double h = double.IsInfinity(availableSize.Height) ? 16 : availableSize.Height;
        var (pitch, cell) = Grid(h, Scale);
        double w = (Math.Max(1, Bars) - 1) * pitch + cell;
        return new Size(double.IsInfinity(availableSize.Width) ? w : Math.Min(availableSize.Width, w), h);
    }

    public override void Render(DrawingContext context)
    {
        var size = Bounds.Size;
        int columns = Bars;
        if (size.Width <= 0 || size.Height <= 0 || columns <= 0) return;
        if (to.Length != columns * Rows) from = to = Lit(Levels is null ? null : live ?? Levels, columns, Lively);
        double scale = Scale;
        var (pitch, cell) = Grid(size.Height, scale);
        // Centred, on whole pixels.
        float left = (float)(Math.Round((size.Width - ((columns - 1) * pitch + cell)) / 2 * scale) / scale);
        float top = (float)(Math.Round((size.Height - ((Rows - 1) * pitch + cell)) / 2 * scale) / scale);
        var lit = Foreground is ISolidColorBrush b ? b.Color : Colors.Gray;
        var mesh = ActualThemeVariant == ThemeVariant.Dark ? Syft.MeshDark : Syft.MeshLight;
        var (was, now, at) = (from, to, changed);
        float p = (float)pitch, c = (float)cell, r = (float)(cell * SyftCell.Round);
        DrawClocked(context, (canvas, seconds, _) =>
        {
            using var paint = Syft.Fill();
            for (int col = 0; col < columns; col++)
                for (int row = 0; row < Rows; row++)
                {
                    float x = left + col * p, y = top + (Rows - 1 - row) * p;
                    paint.Color = Syft.Sk(mesh);
                    Syft.DrawCell(canvas, paint, x, y, c, r);
                    int i = col * Rows + row;
                    float shown = i < now.Length ? Shown(was[i], now[i], seconds - at) : 0;
                    if (shown <= 0.004f) continue;
                    paint.Color = Syft.Sk(lit, shown);
                    Syft.DrawCell(canvas, paint, x, y, c, r);
                }
        });
    }
}
