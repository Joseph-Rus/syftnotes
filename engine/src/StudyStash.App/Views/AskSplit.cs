using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using StudyStash.App.Platform;

namespace StudyStash.App.Views;

/// <summary>
/// A page with Ask on it (a lecture's notes, Home, a class's home, a folder): the page, the conversation and the ask
/// bar, laid out together. At rest the bar sits at the foot of the page, in the middle. Once something's asked (or a
/// kept chat is opened) the page splits: the page keeps the left, narrowed to a column that still reads well, and the
/// conversation is a panel down the right the page area's full height, the same bar docked at its foot, so a long
/// answer has the panel's whole width and never covers the notes. Where the two can't both fit (<see cref="SheetBelow"/>)
/// the conversation covers the page area instead, as a sheet with a way back.
/// <para>Opening and closing glide: the bar travels from the foot of the page to the panel's foot while the panel comes
/// in from the right and the page's column slides over to its new place. The page is laid out once, at where it ends
/// up, and only moved while it glides; the panel only moves; the bar alone is laid out each frame (it's small). Asked
/// again mid-glide, or closed, it carries on from wherever it was. With less motion asked for it goes straight there.</para>
/// </summary>
public sealed class AskSplit : Panel
{
    /// <summary>The conversation is open: the page splits (or the sheet covers it).</summary>
    public static readonly StyledProperty<bool> IsOpenProperty = AvaloniaProperty.Register<AskSplit, bool>(nameof(IsOpen));
    /// <summary>The window is narrow: the bar at rest keeps the page's narrow margins.</summary>
    public static readonly StyledProperty<bool> NarrowProperty = AvaloniaProperty.Register<AskSplit, bool>(nameof(Narrow));
    /// <summary>What Close does (and Esc, inside the panel): the page's Ask closes its conversation.</summary>
    public static readonly StyledProperty<ICommand?> CloseCommandProperty = AvaloniaProperty.Register<AskSplit, ICommand?>(nameof(CloseCommand));
    /// <summary>The bar's widest at rest: the notes' measure, so it lines up with them.</summary>
    public static readonly StyledProperty<double> RestWidthProperty = AvaloniaProperty.Register<AskSplit, double>(nameof(RestWidth), 704);
    /// <summary>The sheet's way back, in the page's own words ("Notes").</summary>
    public static readonly StyledProperty<string> BackWordsProperty = AvaloniaProperty.Register<AskSplit, string>(nameof(BackWords), "Back");

    public bool IsOpen
    {
        get => GetValue(IsOpenProperty);
        set => SetValue(IsOpenProperty, value);
    }

    public bool Narrow
    {
        get => GetValue(NarrowProperty);
        set => SetValue(NarrowProperty, value);
    }

    public ICommand? CloseCommand
    {
        get => GetValue(CloseCommandProperty);
        set => SetValue(CloseCommandProperty, value);
    }

    public double RestWidth
    {
        get => GetValue(RestWidthProperty);
        set => SetValue(RestWidthProperty, value);
    }

    public string BackWords
    {
        get => GetValue(BackWordsProperty);
        set => SetValue(BackWordsProperty, value);
    }

    /// <summary>The panel's share of the page area, and its narrowest and widest.</summary>
    public const double Share = 0.4, Least = 360, Most = 520;
    /// <summary>Below this the page area (the window less the sidebar) can't hold the panel and a column of notes that
    /// still reads well (about 440): the conversation covers the page as a sheet.</summary>
    public const double SheetBelow = 800;
    /// <summary>A bar narrower than this (docked in the panel) stacks: the question on a line of its own, what it's
    /// asked about and which AI under it.</summary>
    public const double StackBelow = 500;
    /// <summary>How long the glide takes, eased out with no bounce; Close plays it backwards.</summary>
    public static readonly TimeSpan Glide = TimeSpan.FromMilliseconds(320);
    /// <summary>The bar's room at rest: under it, and beside it at the page's margins.</summary>
    const double RestBelow = 20, RestSide = 56, RestSideNarrow = 24;
    /// <summary>The bar's room docked in the panel: from the panel's edges and its foot, and between the thread's end and the bar.</summary>
    const double DockSide = 16, DockBelow = 16, ThreadGap = 12;
    /// <summary>Room between the page and the panel's edge beside it, so the page's scroll bar doesn't sit on the panel's
    /// line and read as a second one.</summary>
    const double Gutter = 12;

    /// <summary>What drives a glide: the app's eased frame clock; a test's film steps it itself.</summary>
    internal static Func<Visual, TimeSpan, Action<double>, Action?, IDisposable> Animate { get; set; } = Motion.Animate;

    readonly Border sheet = new() { ClipToBounds = true };
    Control? page, chat, bar;
    /// <summary>The page's column (its notes, its list): followed so the glide moves it from where it was to where it lands.</summary>
    public Control? Anchor { get; set; }

    /// <summary>Where the layout is heading: open, and whether as a sheet.</summary>
    bool open, asSheet;
    /// <summary>The glide under way: how far (eased, 0 to 1), and where each piece started from.</summary>
    IDisposable? gliding;
    double eased = 1;
    Rect barFrom;
    double pageFrom, sheetFrom;
    /// <summary>A glide asked for, waiting for the layout at its end to say where the page's column lands.</summary>
    double? anchorWas;

    public AskSplit()
    {
        ClipToBounds = true;
        sheet.Bind(Border.BackgroundProperty, sheet.GetResourceObservable("Surface"));
        sheet.Bind(Border.BorderBrushProperty, sheet.GetResourceObservable("Line"));
        sheet.IsVisible = false;
        // Esc inside the conversation (its thread or the bar while it's docked) closes it, as Close does.
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Bubble);
    }

    /// <summary>The page: the left side once split.</summary>
    public Control? Page
    {
        get => page;
        set => Swap(ref page, value, 0);
    }

    /// <summary>The conversation (its header and thread): the panel, with the bar docked under it.</summary>
    public Control? Chat
    {
        get => chat;
        set
        {
            chat = value;
            sheet.Child = value;
            if (!Children.Contains(sheet)) Children.Insert(Math.Min(1, Children.Count), sheet);
        }
    }

    /// <summary>The ask bar: one control, which travels between the page's foot and the panel's.</summary>
    public Control? Bar
    {
        get => bar;
        set => Swap(ref bar, value, Children.Count);
    }

    void Swap(ref Control? field, Control? value, int at)
    {
        if (field is not null) Children.Remove(field);
        field = value;
        if (value is not null) Children.Insert(Math.Min(at, Children.Count), value);
    }

    /// <summary>Whether the page and the panel are side by side now (open, with room for both).</summary>
    public bool Beside => open && !asSheet;
    /// <summary>Whether the conversation covers the page as a sheet now.</summary>
    public bool Sheet => open && asSheet;
    /// <summary>The panel's width in a page area <paramref name="width"/> wide (the whole width, as a sheet).</summary>
    public static double PanelWidth(double width) => width < SheetBelow ? width : Math.Clamp(width * Share, Least, Most);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsOpenProperty) Go(change.GetNewValue<bool>());
        else if (change.Property == NarrowProperty || change.Property == RestWidthProperty) InvalidateMeasure();
    }

    /// <summary>Opens or closes, gliding from wherever things are now (mid-glide too).</summary>
    void Go(bool to)
    {
        if (to == open) return;
        bool shown = this.IsAttachedToVisualTree() && Bounds.Width > 0 && IsEffectivelyVisible;
        gliding?.Dispose();
        gliding = null;
        if (!shown)
        {
            open = to;
            eased = 1;
            Settle();
            InvalidateMeasure();
            return;
        }
        // Where each piece is now, on screen, mid-glide or at rest.
        barFrom = bar is { IsVisible: true } ? bar.Bounds : default;
        sheetFrom = sheet.IsVisible ? Shift(sheet) : Offscreen();
        anchorWas = AnchorX();
        pageFrom = 0;
        open = to;
        if (to) sheet.IsVisible = true;
        eased = 0;
        Mark();
        Place(0); // nothing jumps before the first frame: each piece is drawn where it was
        InvalidateMeasure();
        gliding = Animate(this, Glide, Step, () =>
        {
            gliding = null;
            Settle();
        });
    }

    /// <summary>One frame of the glide: the page and the panel moved, the bar laid out where it's got to.</summary>
    void Step(double e)
    {
        eased = e;
        Place(e);
        InvalidateArrange();
    }

    /// <summary>The glide's end (or no glide): everything where the layout puts it, nothing moved.</summary>
    void Settle()
    {
        eased = 1;
        anchorWas = null;
        if (page is not null) page.RenderTransform = null;
        sheet.RenderTransform = null;
        sheet.IsVisible = open;
        Mark();
        InvalidateArrange();
    }

    /// <summary>The page and the panel at <paramref name="e"/> of the way.</summary>
    void Place(double e)
    {
        if (page is not null) page.RenderTransform = pageFrom == 0 || e >= 1 ? null : new TranslateTransform(pageFrom * (1 - e), 0);
        double to = open ? 0 : Offscreen();
        double x = sheetFrom + (to - sheetFrom) * e;
        sheet.RenderTransform = Math.Abs(x) < 0.01 ? null : new TranslateTransform(x, 0);
    }

    double Offscreen() => (asSheet ? Bounds.Width : PanelWidth(Bounds.Width)) + 1;

    static double Shift(Visual v) => v.RenderTransform is TranslateTransform t ? t.X : 0;

    /// <summary>The page's column across the split, as drawn (its glide included).</summary>
    double? AnchorX() => Anchor is { } a && page is not null && a.IsEffectivelyVisible && a.TranslatePoint(default, this) is { } at ? at.X : null;

    /// <summary>The states the page's and the panel's looks key off: <c>:open</c>, <c>:beside</c>, <c>:sheet</c>.</summary>
    void Mark()
    {
        PseudoClasses.Set(":open", open);
        PseudoClasses.Set(":beside", Beside);
        PseudoClasses.Set(":sheet", Sheet);
        if (Beside != wasBeside) RaisePropertyChanged(BesideProperty, wasBeside, wasBeside = Beside);
    }

    bool wasBeside;

    /// <summary>Whether the page is split now, for a page that lays itself out differently beside the panel (a folder's
    /// rail goes under its notes).</summary>
    public static readonly DirectProperty<AskSplit, bool> BesideProperty = AvaloniaProperty.RegisterDirect<AskSplit, bool>(nameof(Beside), s => s.Beside);

    void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || e.Handled || !open || e.Source is not Visual from) return;
        if (!(sheet.IsVisualAncestorOf(from) || bar?.IsVisualAncestorOf(from) == true || ReferenceEquals(from, bar))) return;
        if (CloseCommand is { } close && close.CanExecute(null))
        {
            close.Execute(null);
            e.Handled = true;
        }
    }

    protected override Size MeasureOverride(Size available)
    {
        double w = double.IsInfinity(available.Width) ? 1000 : available.Width;
        double h = double.IsInfinity(available.Height) ? 800 : available.Height;
        bool wasSheet = asSheet;
        asSheet = w < SheetBelow;
        if (wasSheet != asSheet) Mark();
        double panel = PanelWidth(w);
        page?.Measure(new Size(Beside ? w - panel - Gutter : w, h));
        if (bar is not null) MeasureBar(BarAt(w, h, open).Width);
        sheet.Padding = new Thickness(0, 0, 0, BarRoom());
        sheet.BorderThickness = new Thickness(asSheet ? 0 : 1, 0, 0, 0);
        sheet.Measure(new Size(panel, h));
        return new Size(w, h);
    }

    /// <summary>The room the docked bar takes at the panel's foot, which the thread ends above.</summary>
    double BarRoom() => (bar is { IsVisible: true } ? bar.DesiredSize.Height : 52) + DockBelow + ThreadGap;

    /// <summary>The bar's place, at rest (at the foot of the page, in the middle) or docked (at the panel's foot).</summary>
    Rect BarAt(double w, double h, bool docked)
    {
        double height = bar?.DesiredSize.Height is > 0 and var d ? d : 52;
        if (docked)
        {
            double panel = PanelWidth(w);
            double left = w - panel + (asSheet ? 0 : 1);
            double width = Math.Min(panel - (asSheet ? 0 : 1) - 2 * DockSide, asSheet ? RestWidth : double.MaxValue);
            double x = left + (panel - (asSheet ? 0 : 1) - width) / 2;
            return new Rect(x, h - DockBelow - height, Math.Max(0, width), height);
        }
        double side = Narrow ? RestSideNarrow : RestSide;
        double rest = Math.Max(0, Math.Min(RestWidth, w - 2 * side));
        return new Rect((w - rest) / 2, h - RestBelow - height, rest, height);
    }

    protected override Size ArrangeOverride(Size final)
    {
        double w = final.Width, h = final.Height;
        double panel = PanelWidth(w);
        if (page is not null)
        {
            page.Arrange(new Rect(0, 0, Beside ? w - panel - Gutter : w, h));
            // Where the page's column lands, against where it was: the glide moves it across from there.
            if (anchorWas is { } was)
            {
                anchorWas = null;
                pageFrom = Anchor is { } a && a.TranslatePoint(default, page) is { } at ? was - at.X : 0;
                if (Math.Abs(pageFrom) < 0.5) pageFrom = 0;
                Place(eased);
            }
        }
        sheet.Arrange(new Rect(w - panel, 0, panel, h));
        if (bar is not null)
        {
            // Mid-glide the bar is laid out at the width it's got to (it's small), its foot moving from where it was.
            var to = BarAt(w, h, open);
            double e = eased < 1 && barFrom.Width > 0 ? eased : 1;
            double width = e < 1 ? barFrom.Width + (to.Width - barFrom.Width) * e : to.Width;
            MeasureBar(width);
            double tall = bar.DesiredSize.Height, foot = e < 1 ? barFrom.Bottom + (to.Bottom - barFrom.Bottom) * e : to.Bottom;
            double x = e < 1 ? barFrom.X + (to.X - barFrom.X) * e : to.X;
            bar.Arrange(new Rect(x, foot - tall, width, tall));
        }
        return final;
    }

    double barMeasured = -1;

    /// <summary>The bar measured for <paramref name="width"/> (again only when that changed, or it asks).</summary>
    void MeasureBar(double width)
    {
        if (bar is null || (Math.Abs(width - barMeasured) < 0.25 && bar.IsMeasureValid)) return;
        barMeasured = width;
        bar.Classes.Set("stacked", width < StackBelow);
        bar.Measure(new Size(width, double.PositiveInfinity));
    }
}
