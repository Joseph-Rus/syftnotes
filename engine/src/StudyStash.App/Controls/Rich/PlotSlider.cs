using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;

namespace StudyStash.App.Controls.Rich;

/// <summary>
/// A plot's slider, drawn in the look it's in: a thin track filled in the accent up to a round knob (a Mac's white
/// knob with its soft shadow; Windows 11's accent dot in a white ring), a little larger while it's held. Drag it or
/// click along it; the arrow keys step it (Shift for ten steps), Page Up and Down by a tenth, Home and End to its ends.
/// It says its value to a screen reader as a range.
/// </summary>
public sealed class PlotSlider : Control
{
    public static readonly StyledProperty<IBrush?> AccentProperty = AvaloniaProperty.Register<PlotSlider, IBrush?>(nameof(Accent));
    public static readonly StyledProperty<IBrush?> TrackProperty = AvaloniaProperty.Register<PlotSlider, IBrush?>(nameof(Track));
    public static readonly StyledProperty<IBrush?> RingProperty = AvaloniaProperty.Register<PlotSlider, IBrush?>(nameof(Ring));

    static PlotSlider() => AffectsRender<PlotSlider>(AccentProperty, TrackProperty, RingProperty);

    public PlotSlider()
    {
        Focusable = true;
        Height = 24;
        MinWidth = 80;
        Cursor = new Cursor(StandardCursorType.Hand);
        Bind(AccentProperty, this.GetResourceObservable("Accent"));
        Bind(TrackProperty, this.GetResourceObservable("Fill2"));
        Bind(RingProperty, this.GetResourceObservable("Accent"));
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
        GotFocus += (_, _) => InvalidateVisual();
        LostFocus += (_, _) => InvalidateVisual();
    }

    public IBrush? Accent { get => GetValue(AccentProperty); set => SetValue(AccentProperty, value); }
    public IBrush? Track { get => GetValue(TrackProperty); set => SetValue(TrackProperty, value); }
    public IBrush? Ring { get => GetValue(RingProperty); set => SetValue(RingProperty, value); }

    public double Minimum { get; init; }
    public double Maximum { get; init; } = 1;
    /// <summary>The step the keys move it by (and what it snaps to, when the slider has one).</summary>
    public double Step { get; init; }

    double value;

    public double Value
    {
        get => value;
        set
        {
            double v = Math.Clamp(value, Minimum, Maximum);
            if (v == this.value) return;
            this.value = v;
            InvalidateVisual();
        }
    }

    /// <summary>Called as the student moves it (each step of a drag), with the value asked for.</summary>
    public event Action<double>? Moved;

    /// <summary>Whether it's being dragged now (a plot draws coarser meanwhile).</summary>
    public bool Dragging { get; private set; }

    /// <summary>Called when a drag starts and when it ends.</summary>
    public event Action<bool>? DragChanged;

    bool Mac => Skin.Current == SkinKind.Mac;
    const double Pad = 9;

    double Fraction => Maximum > Minimum ? (value - Minimum) / (Maximum - Minimum) : 0;

    double ValueAt(double x)
    {
        double w = Math.Max(1, Bounds.Width - 2 * Pad);
        return Minimum + Math.Clamp((x - Pad) / w, 0, 1) * (Maximum - Minimum);
    }

    void Ask(double v)
    {
        if (Step > 0) v = Minimum + Math.Round((v - Minimum) / Step) * Step;
        v = Math.Clamp(v, Minimum, Maximum);
        Moved?.Invoke(v);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        e.Pointer.Capture(this);
        Dragging = true;
        DragChanged?.Invoke(true);
        Focus(NavigationMethod.Pointer);
        Ask(ValueAt(e.GetPosition(this).X));
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!Dragging) return;
        Ask(ValueAt(e.GetPosition(this).X));
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!Dragging) return;
        End(e.Pointer);
        e.Handled = true;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (Dragging) End(null);
    }

    void End(IPointer? pointer)
    {
        Dragging = false;
        pointer?.Capture(null);
        DragChanged?.Invoke(false);
        InvalidateVisual();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        double step = Step > 0 ? Step : (Maximum - Minimum) / 100;
        double big = Math.Max(step, (Maximum - Minimum) / 10);
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) step *= 10;
        double? to = e.Key switch
        {
            Key.Left or Key.Down => value - step,
            Key.Right or Key.Up => value + step,
            Key.PageDown => value - big,
            Key.PageUp => value + big,
            Key.Home => Minimum,
            Key.End => Maximum,
            _ => null,
        };
        if (to is not { } v) return;
        Ask(v);
        e.Handled = true;
    }

    public override void Render(DrawingContext context)
    {
        double w = Bounds.Width, cy = Bounds.Height / 2;
        if (w <= 2 * Pad) return;
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
        bool dark = ActualThemeVariant == ThemeVariant.Dark;
        double x = Pad + Fraction * (w - 2 * Pad);
        double track = Mac ? 4 : 4;
        var trackBrush = Track ?? new SolidColorBrush(dark ? Color.FromArgb(40, 255, 255, 255) : Color.FromArgb(24, 0, 0, 0));
        context.DrawRectangle(trackBrush, null, new RoundedRect(new Rect(Pad, cy - track / 2, w - 2 * Pad, track), track / 2));
        context.DrawRectangle(Accent ?? Brushes.SteelBlue, null, new RoundedRect(new Rect(Pad, cy - track / 2, Math.Max(track, x - Pad), track), track / 2));
        if (Mac)
        {
            // A white knob with a soft shadow and a hairline edge.
            double r = Dragging ? 8.5 : 8;
            context.DrawEllipse(new SolidColorBrush(Color.FromArgb(40, 0, 0, 0)), null, new Point(x, cy + 0.8), r + 0.6, r + 0.6);
            context.DrawEllipse(dark ? new SolidColorBrush(Color.Parse("#D8D8DC")) : Brushes.White,
                new Pen(new SolidColorBrush(Color.FromArgb(dark ? (byte)0 : (byte)30, 0, 0, 0)), 0.5), new Point(x, cy), r, r);
        }
        else
        {
            // Windows 11: the accent dot in a white ring, which grows as it's held.
            context.DrawEllipse(dark ? new SolidColorBrush(Color.Parse("#454545")) : Brushes.White,
                new Pen(new SolidColorBrush(Color.FromArgb(30, 0, 0, 0)), 1), new Point(x, cy), 10, 10);
            double inner = Dragging ? 7 : IsPointerOver ? 7 : 5.5;
            context.DrawEllipse(Accent ?? Brushes.SteelBlue, null, new Point(x, cy), inner, inner);
        }
        if (IsKeyboardFocusWithin && !Dragging)
            context.DrawEllipse(null, new Pen(Ring ?? Brushes.SteelBlue, 2), new Point(x, cy), 12, 12);
    }

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        InvalidateVisual();
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new Peer(this);

    /// <summary>The slider as a screen reader hears it: a range with its value.</summary>
    sealed class Peer(PlotSlider owner) : ControlAutomationPeer(owner), IRangeValueProvider
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Slider;
        public bool IsReadOnly => false;
        public double Minimum => owner.Minimum;
        public double Maximum => owner.Maximum;
        public double Value => owner.Value;
        public double SmallChange => owner.Step > 0 ? owner.Step : (owner.Maximum - owner.Minimum) / 100;
        public double LargeChange => (owner.Maximum - owner.Minimum) / 10;
        public void SetValue(double value) => owner.Ask(value);
    }
}
