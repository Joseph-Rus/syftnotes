using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
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
/// page do nothing; Escape skips, Right goes on, Left goes back, Return presses the card's focused button (Next).
/// Targets are found by their Name among the view's visible controls, so the view needs no other wiring.
/// </summary>
public sealed class TourLayer : Panel
{
    public static readonly StyledProperty<TourModel?> TourProperty = AvaloniaProperty.Register<TourLayer, TourModel?>(nameof(Tour));

    const double Pad = 6, Gap = 12, Edge = 16, Round = 10;

    readonly TourCard card = new();
    readonly Veil veil = new();
    Rect hole;

    public TourLayer()
    {
        IsVisible = false;
        Background = Brushes.Transparent; // the dimmed page takes no clicks
        Children.Add(veil);
        Children.Add(card);
        AddHandler(KeyDownEvent, OnKey, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        LayoutUpdated += (_, _) => Follow();
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
            if (Tour == tour && tour.Current is null) tour.Begin(name => Target(name) is not null);
        }, DispatcherPriority.Loaded);
    }

    void OnStep(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(TourModel.Current)) return;
        IsVisible = Tour?.Current is not null;
        if (Tour?.Current is null) return;
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
        var at = Target(step.Target) is { } c && c.TranslatePoint(default, this) is { } p
            ? new Rect(p, c.Bounds.Size).Inflate(Pad).Intersect(new Rect(Bounds.Size))
            : default;
        if (at == hole) return;
        hole = veil.Hole = at;
        InvalidateArrange();
        veil.InvalidateVisual();
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

    /// <summary>The dimmed page with the hole in it, and the ink ring round the hole.</summary>
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
