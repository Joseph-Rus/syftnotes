using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace StudyStash.App.Controls;

/// <summary>A window's own title bar, across its whole top: drag it to move the window, double-click it to zoom, and
/// the system's window buttons sit in it (the Mac's traffic lights on the left, Windows' caption buttons on the
/// right). Whatever goes inside (the library's toolbar, a search box) sits on top and stays clickable; only the bar's
/// own background is the drag area. Screenshots have no system buttons, so with <see cref="DrawChrome"/> it draws
/// them itself.</summary>
public sealed class WindowHeader : Panel
{
    /// <summary>A Mac window's unified title bar: the height of a toolbar window, lights centred in it.</summary>
    public const double MacHeight = 52;
    /// <summary>Where the Mac's first light starts, and the room the three take (with a gap after them).</summary>
    public const double LightsInset = 20, LightsRoom = 88;
    /// <summary>Windows' caption buttons are 46 px each.</summary>
    public const double CaptionWidth = 46;

    public static readonly StyledProperty<string?> TitleProperty = AvaloniaProperty.Register<WindowHeader, string?>(nameof(Title));
    public static readonly StyledProperty<bool> DrawChromeProperty = AvaloniaProperty.Register<WindowHeader, bool>(nameof(DrawChrome));
    public static readonly StyledProperty<bool> CanResizeProperty = AvaloniaProperty.Register<WindowHeader, bool>(nameof(CanResize), true);

    readonly bool mac = Skin.Current == SkinKind.Mac;
    readonly Border drag = new() { Name = "Drag" };
    readonly TextBlock title = new() { IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    readonly StackPanel titleRow = new() { Orientation = Orientation.Horizontal, Spacing = 10, IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Center };
    readonly StackPanel lights = new() { Orientation = Orientation.Horizontal, Spacing = 8, IsVisible = false, VerticalAlignment = VerticalAlignment.Center };
    readonly StackPanel captions = new() { Orientation = Orientation.Horizontal, IsVisible = false, HorizontalAlignment = HorizontalAlignment.Right };
    readonly Ellipse minimise = Light("#FEBC2E"), zoom = Light("#28C840");
    readonly Button maximise;
    readonly int parts;

    public WindowHeader()
    {
        // The drag area is the background only (never a parent of the buttons): the platform moves the window from
        // it, and zooms on a double-click.
        drag.Background = Brushes.Transparent;
        if (mac) drag.Bind(Border.BackgroundProperty, drag.GetResourceObservable("Win"));
        WindowDecorationProperties.SetElementRole(drag, WindowDecorationsElementRole.TitleBar);
        drag.PointerPressed += OnPressed;
        drag.DoubleTapped += OnDoubleTapped;
        Children.Add(drag);

        if (mac)
        {
            lights.Margin = new Thickness(LightsInset, 0, 0, 0);
            lights.Children.Add(Light("#FF5F57"));
            lights.Children.Add(minimise);
            lights.Children.Add(zoom);
            lights.HorizontalAlignment = HorizontalAlignment.Left;
            Children.Add(lights);
            title.HorizontalAlignment = HorizontalAlignment.Center;
            title.FontSize = 13;
            title.FontWeight = FontWeight.SemiBold;
            title.Bind(TextBlock.ForegroundProperty, title.GetResourceObservable("Fg2"));
            Children.Add(title);
        }
        else
        {
            var mark = new Border { Width = 16, Height = 16, CornerRadius = new CornerRadius(4), Child = new Icon { Glyph = "graphic_eq", Size = 12 } };
            mark.Bind(Border.BackgroundProperty, mark.GetResourceObservable("Accent"));
            ((Icon)mark.Child).Bind(Icon.ForegroundProperty, mark.GetResourceObservable("OnAccent"));
            title.FontSize = 12;
            titleRow.Margin = new Thickness(16, 0, 0, 0);
            titleRow.HorizontalAlignment = HorizontalAlignment.Left;
            titleRow.Children.Add(mark);
            titleRow.Children.Add(title);
            Children.Add(titleRow);
            maximise = Caption("crop_square", 13);
            captions.Children.Add(Caption("remove", 15));
            captions.Children.Add(maximise);
            captions.Children.Add(Caption("close", 15));
            Children.Add(captions);
        }
        maximise ??= new Button();
        parts = Children.Count;
    }

    /// <summary>The window's name: centred in grey on a Mac, beside the app's mark on Windows.</summary>
    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>Screenshots: draw the window's own buttons, which the system draws in the real app.</summary>
    public bool DrawChrome
    {
        get => GetValue(DrawChromeProperty);
        set => SetValue(DrawChromeProperty, value);
    }

    /// <summary>A fixed-size window (setup, Settings): the Mac's zoom light is off, Windows has no maximise button,
    /// and a double-click does nothing.</summary>
    public bool CanResize
    {
        get => GetValue(CanResizeProperty);
        set => SetValue(CanResizeProperty, value);
    }

    /// <summary>The drag area: the part of the bar behind everything else.</summary>
    public Border DragArea => drag;

    /// <summary>What the header keeps clear for the window's buttons: the lights on a Mac's left, the caption buttons
    /// on Windows' right (three, as a real window may show them all).</summary>
    public Thickness ButtonRoom => mac ? new Thickness(LightsRoom, 0, 0, 0) : new Thickness(0, 0, CaptionWidth * (DrawChrome && !CanResize ? 2 : 3), 0);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TitleProperty) title.Text = Title;
        else if (change.Property == DrawChromeProperty || change.Property == CanResizeProperty)
        {
            lights.IsVisible = mac && DrawChrome;
            captions.IsVisible = !mac && DrawChrome;
            // A fixed-size Mac window shows its lights the way the design draws them: red, then two greys.
            foreach (var e in new[] { minimise, zoom })
            {
                if (CanResize) e.Fill = Brush.Parse(e == minimise ? "#FEBC2E" : "#28C840");
                else e.Bind(Shape.FillProperty, e.GetResourceObservable("Fill2"));
            }
            maximise.IsVisible = CanResize;
            InvalidateArrange();
        }
    }

    /// <summary>52 on a Mac and 32 on Windows unless the view says otherwise (the library's Windows bar is 48).</summary>
    protected override Size MeasureOverride(Size availableSize)
    {
        var size = base.MeasureOverride(availableSize);
        return new Size(size.Width, mac ? MacHeight : 32);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var all = new Rect(finalSize);
        var room = ButtonRoom;
        var inner = new Rect(room.Left, 0, Math.Max(0, finalSize.Width - room.Left - room.Right), finalSize.Height);
        for (int i = 0; i < Children.Count; i++) Children[i].Arrange(i < parts ? all : inner);
        return finalSize;
    }

    /// <summary>Where the platform doesn't move the window from the drag area itself, the press reaches here: move
    /// it by hand.</summary>
    void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not Window w || !e.GetCurrentPoint(drag).Properties.IsLeftButtonPressed || e.ClickCount > 1) return;
        w.BeginMoveDrag(e);
    }

    void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not Window { CanResize: true } w) return;
        w.WindowState = w.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    static Ellipse Light(string colour) => new() { Width = 12, Height = 12, Fill = Brush.Parse(colour) };

    Button Caption(string glyph, double size)
    {
        var icon = new Icon { Glyph = glyph, Size = size };
        icon.Bind(Icon.ForegroundProperty, this.GetResourceObservable("Fg"));
        var b = new Button { Width = CaptionWidth, HorizontalContentAlignment = HorizontalAlignment.Center, Content = icon, Focusable = false };
        b.Bind(StyledElement.ThemeProperty, this.GetResourceObservable("Surface"));
        return b;
    }
}
