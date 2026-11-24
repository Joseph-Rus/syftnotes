using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StudyStash.App.ViewModels.Kit;
using StudyStash.App.Views;

namespace StudyStash.App.Controls;

/// <summary>
/// A <see cref="TourModel"/> over a view: put it last in the view's top panel and bind <see cref="Tour"/>. While a tour
/// runs, the view dims (the <c>Scrim</c> token), the step's control shows through a hole ringed in ink, and the
/// <see cref="TourCard"/> sits beside it, keeping up as the window is resized or the page reloads. Clicks on the dimmed
/// page do nothing; one on the control in the hole does what it always does, and ends the tour (<see cref="TourModel.Used"/>).
/// Escape skips, Right goes on, Left goes back, Return presses the card's focused button (Next).
/// Targets are found by their Name among the view's visible controls, so the view needs no other wiring.
/// </summary>
public sealed class TourLayer : Panel
{
    public static readonly StyledProperty<TourModel?> TourProperty = AvaloniaProperty.Register<TourLayer, TourModel?>(nameof(Tour));

    const double Pad = 6, Gap = 12, Edge = 16, Round = 10;

    readonly TourCard card = new();
    readonly Veil veil = new();
    Rect hole;
    /// <summary>The control in the hole, watched for a click.</summary>
    Control? watched;

    public TourLayer()
    {
        IsVisible = false;
        Children.Add(veil);
        Children.Add(card);
        AddHandler(KeyDownEvent, OnKey, RoutingStrategies.Tunnel);
        LayoutUpdated += (_, _) => Follow();
    }

    /// <summary>Clicks on the dimmed page stop before they reach it: caught on the way down, in the view this layer covers.</summary>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        (Parent as Interactive)?.AddHandler(PointerPressedEvent, OnPage, RoutingStrategies.Tunnel);
        (Parent as Interactive)?.AddHandler(PointerReleasedEvent, OnPage, RoutingStrategies.Tunnel);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        (Parent as Interactive)?.RemoveHandler(PointerPressedEvent, OnPage);
        (Parent as Interactive)?.RemoveHandler(PointerReleasedEvent, OnPage);
    }

    void OnPage(object? sender, PointerEventArgs e)
    {
        var at = e.GetPosition(this);
        if (IsVisible && !hole.Contains(at) && !card.Bounds.Contains(at)) e.Handled = true;
    }

    public TourModel? Tour
    {
        get => GetValue(TourProperty);
        set => SetValue(TourProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != TourProperty) return;
        if (change.OldValue is TourModel old) old.PropertyChanged -= OnStep;
        card.DataContext = Tour;
        IsVisible = Tour?.Current is not null;
        if (Tour is not { } tour) return;
        tour.PropertyChanged += OnStep;
        // Once the page under it is laid out, so what's on screen can be asked.
        Dispatcher.UIThread.Post(() =>
        {
            if (Tour == tour) tour.Start(name => Target(name) is not null);
        }, DispatcherPriority.Loaded);
    }

    void OnStep(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(TourModel.Current)) return;
        IsVisible = Tour?.Current is not null;
        if (Tour?.Current is null)
        {
            Watch(null);
            return;
        }
        Target(Tour.Current.Target)?.BringIntoView();
        Follow();
        Dispatcher.UIThread.Post(() => card.FindControl<Button>("Next")?.Focus(NavigationMethod.Tab), DispatcherPriority.Loaded);
    }

    /// <summary>The control named <paramref name="name"/> on screen under this layer (an items list's first item), if any.</summary>
    Control? Target(string name)
    {
        if (Parent is not Visual root) return null;
        var found = root.GetVisualDescendants().OfType<Control>()
            .FirstOrDefault(c => c.Name == name && c != this && !this.IsVisualAncestorOf(c) && c.IsEffectivelyVisible && c.Bounds.Width > 0);
        return found is ItemsControl items ? items.ContainerFromIndex(0) : found;
    }

    /// <summary>Where the step's control is now; a move (a resize, a reload, a scroll) redraws the hole and the card.</summary>
    void Follow()
    {
        if (!IsVisible || Tour?.Current is not { } step) return;
        var target = Target(step.Target);
        Watch(target);
        var at = target is { } c && c.TranslatePoint(default, this) is { } p
            ? new Rect(p, c.Bounds.Size).Inflate(Pad).Intersect(new Rect(Bounds.Size))
            : default;
        if (at == hole) return;
        hole = veil.Hole = at;
        InvalidateArrange();
        veil.InvalidateVisual();
    }

    /// <summary>Watches <paramref name="target"/> for a click: it does its own thing first, then the tour ends.</summary>
    void Watch(Control? target)
    {
        if (target == watched) return;
        watched?.RemoveHandler(PointerReleasedEvent, OnUsed);
        watched = target;
        watched?.AddHandler(PointerReleasedEvent, OnUsed, RoutingStrategies.Bubble, handledEventsToo: true);
    }

    void OnUsed(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton != MouseButton.Left || sender is not Control c || !new Rect(c.Bounds.Size).Contains(e.GetPosition(c))) return;
        var tour = Tour;
        Dispatcher.UIThread.Post(() => tour?.Used());
    }

    protected override Size ArrangeOverride(Size size)
    {
        var s = card.DesiredSize;
        // Under or over the hole, lined up with its left edge, or its right one for a control on the window's right.
        double x = hole.Center.X > size.Width / 2 ? hole.Right - s.Width : hole.X;
        Point at;
        if (hole == default) at = new Point((size.Width - s.Width) / 2, (size.Height - s.Height) / 2);
        else if (hole.Bottom + Gap + s.Height <= size.Height - Edge) at = new Point(x, hole.Bottom + Gap);
        else if (hole.Y - Gap - s.Height >= Edge) at = new Point(x, hole.Y - Gap - s.Height);
        else if (hole.Right + Gap + s.Width <= size.Width - Edge) at = new Point(hole.Right + Gap, hole.Y);
        else at = new Point(hole.X - Gap - s.Width, hole.Y);
        // Kept inside the window, however small.
        at = new Point(Math.Clamp(at.X, Edge, Math.Max(Edge, size.Width - s.Width - Edge)), Math.Clamp(at.Y, Edge, Math.Max(Edge, size.Height - s.Height - Edge)));
        card.Arrange(new Rect(at, s));
        veil.Arrange(new Rect(size));
        return size;
    }

    void OnKey(object? sender, KeyEventArgs e)
    {
        if (Tour is not { } tour) return;
        var command = e.Key switch
        {
            Key.Escape => tour.SkipCommand,
            Key.Right => tour.NextCommand,
            Key.Left => tour.BackCommand,
            _ => null,
        };
        if (command is null) return;
        e.Handled = true;
        command.Execute(null);
    }

    /// <summary>The dimmed page with the hole in it, and the ink ring round the hole. The pointer over the dim is over
    /// it (no hover on the page beneath); in the hole, it's over the control there.</summary>
    sealed class Veil : Control
    {
        public Rect Hole;

        public Veil() => ActualThemeVariantChanged += (_, _) => InvalidateVisual();

        public override void Render(DrawingContext context)
        {
            IBrush scrim = this.TryFindResource("Scrim", ActualThemeVariant, out var b) && b is IBrush sb ? sb : Brushes.Transparent;
            IBrush ink = this.TryFindResource("Ink", ActualThemeVariant, out var i) && i is IBrush ib ? ib : Brushes.Black;
            var all = new RectangleGeometry(new Rect(Bounds.Size));
            if (Hole == default)
            {
                context.DrawGeometry(scrim, null, all);
                return;
            }
            context.DrawGeometry(scrim, null, new CombinedGeometry(GeometryCombineMode.Exclude, all, new RectangleGeometry(Hole, Round, Round)));
            context.DrawRectangle(null, new Pen(ink, 2), Hole, Round, Round);
        }
    }
}
