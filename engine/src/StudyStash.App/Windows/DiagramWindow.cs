using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Styling;
using StudyStash.App.Controls;
using StudyStash.App.Controls.Rich;
using StudyStash.App.Views;

namespace StudyStash.App.Windows;

/// <summary>
/// A diagram from the notes, larger, in a window of its own: sized to show it at one and a half times (never more
/// than 85% of the screen), fitted to whatever size the student makes the window (up to twice its own size). A
/// flowchart is the whole explorer here: a toolbar across the top (zoom, step through, test yourself, open or fold its
/// groups, the moment of the lecture it comes from) and a strip along the bottom saying what's lit or where the steps
/// are; a drawing zooms and pans. Esc (once nothing in it is picked) or ⌘W (Ctrl+W) closes it. There's only ever one:
/// opening another diagram shows it in the same window.
/// </summary>
public static class DiagramWindow
{
    const double Pad = 32, Grow = 1.5, MaxScale = 2, MinWidth = 420, MinHeight = 300;

    /// <summary>The room the toolbar and the strip take, over the diagram's own.</summary>
    const double Chrome = 92;

    /// <summary>The window, while one is open.</summary>
    public static Window? Current { get; private set; }

    /// <summary>Shows the diagram <paramref name="request"/> asks for, near <paramref name="from"/>.</summary>
    public static Window Open(OpenDiagramEventArgs request, TopLevel? from)
    {
        var content = Content(request);
        var w = Current;
        if (w is null)
        {
            w = new Window { MinWidth = MinWidth, MinHeight = MinHeight, WindowStartupLocation = WindowStartupLocation.CenterScreen, CanResize = true };
            Look.Apply(w);
            w.KeyDown += (_, e) =>
            {
                if (e.Handled) return;
                if (e.Key == Key.Escape || (e.Key == Key.W && e.KeyModifiers is KeyModifiers.Meta or KeyModifiers.Control))
                {
                    e.Handled = true;
                    w.Close();
                }
            };
            w.Closed += (_, _) =>
            {
                if (ReferenceEquals(Current, w)) Current = null;
            };
            var natural = Natural(request);
            var (width, height) = Size(natural.WithHeight(natural.Height + (request.Chart is not null || Illustrated(request) ? Chrome : 48) / Grow), Area(from ?? w));
            w.Width = width;
            w.Height = height;
            Current = w;
        }
        w.Title = request.Title;
        w.Content = content;
        w.Show();
        w.Activate();
        // The keyboard starts on the diagram (a flowchart, or a drawing's stage), so its keys work at once; nothing is
        // left focused in what the window showed before.
        Control? keys = (Control?)content.GetLogicalDescendants().OfType<DiagramView>().FirstOrDefault()
            ?? (Control?)content.GetLogicalDescendants().OfType<SvgView>().FirstOrDefault(v => v.Explorer is not null)
            ?? content.GetLogicalDescendants().OfType<Panel>().FirstOrDefault(p => p.Focusable);
        if (keys is not null) keys.Focus(NavigationMethod.Pointer);
        else w.Focus();
        return w;
    }

    /// <summary>What the window shows: a flowchart filling it with its toolbar and strip, or a drawing that zooms and
    /// pans, on the window's ground.</summary>
    internal static Control Content(OpenDiagramEventArgs request)
    {
        Control content = request.Chart is { } chart ? Chart(request, chart) : Illustrated(request) ? Illustration(request) : Drawing(request);
        var ground = new Border { Child = content };
        ground.Bind(Border.BackgroundProperty, ground.GetResourceObservable(Skin.Current == SkinKind.Mac ? "Win" : "Mica"));
        return ground;
    }

    static Control Chart(OpenDiagramEventArgs request, Core.Rich.Flowchart chart)
    {
        var origin = request.Source as Control;
        var view = new DiagramView(opensLarger: false)
        {
            Source = request.Written, Folded = request.Folded, Origin = origin, MaxScale = MaxScale, Caption = (origin as DiagramView)?.Caption,
        };
        view.Chart = chart;
        var chrome = view.Chrome!;
        // A question asked, or a moment found, here goes to the lecture the note is on: its window comes forward.
        view.Explorer!.AskedFromWindow = () => (TopLevel.GetTopLevel(origin) as Window)?.Activate();
        var dock = new DockPanel();
        DockPanel.SetDock(chrome.Tools, Dock.Top);
        DockPanel.SetDock(chrome.Strip, Dock.Bottom);
        dock.Children.Add(chrome.Tools);
        dock.Children.Add(chrome.Strip);
        dock.Children.Add(view);
        return dock;
    }

    /// <summary>A drawing whose parts are named (an illustration).</summary>
    static bool Illustrated(OpenDiagramEventArgs request) => request.Svg is { } svg && Core.Rich.SafeSvg.Clean(svg).Illustrated;

    /// <summary>An illustration, explored as in its note but larger: its toolbar across the top (zoom, labels, the
    /// tour, test yourself, the moment of the lecture) and a strip along the bottom naming the part the pointer is on.</summary>
    static Control Illustration(OpenDiagramEventArgs request)
    {
        var origin = request.Source as Control;
        var view = new SvgView(opensLarger: false) { Origin = origin, MaxScale = MaxScale };
        view.Source = request.Svg;
        var chrome = view.Chrome!;
        view.Explorer!.AskedFromWindow = () => (TopLevel.GetTopLevel(origin) as Window)?.Activate();
        var dock = new DockPanel();
        DockPanel.SetDock(chrome.Tools, Dock.Top);
        DockPanel.SetDock(chrome.Strip, Dock.Bottom);
        dock.Children.Add(chrome.Tools);
        dock.Children.Add(chrome.Strip);
        dock.Children.Add(view);
        return dock;
    }

    /// <summary>A drawing (an AI's SVG): fitted to the window, then zoomed (the buttons, ⌘/Ctrl + scroll, a pinch, + and
    /// −) and moved about (a drag, or scrolling).</summary>
    static Control Drawing(OpenDiagramEventArgs request)
    {
        var svg = new SvgView(opensLarger: false) { Source = request.Svg, MaxScale = MaxScale, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(Pad) };
        var stage = new Panel { ClipToBounds = true, Background = Brushes.Transparent, Focusable = true, Children = { svg } };
        AutomationProperties.SetName(stage, AutomationProperties.GetName(svg));
        var zoomer = new Zoomer(svg, stage) { Max = 6 };
        bool mac = Skin.Current == SkinKind.Mac;
        Button Tool(string? glyph, string? words, string tip, Action click)
        {
            var b = new Button { Height = 28, MinWidth = 28, Padding = new Thickness(words is null ? 6 : 9, 0), CornerRadius = new CornerRadius(mac ? 7 : 4), HorizontalContentAlignment = HorizontalAlignment.Center };
            if (Application.Current?.TryFindResource("Surface", out var theme) == true && theme is ControlTheme t) b.Theme = t;
            Control content;
            if (glyph is not null)
            {
                var icon = new Icon { Glyph = glyph, Size = 16 };
                icon.Bind(Icon.ForegroundProperty, icon.GetResourceObservable("Fg"));
                content = icon;
            }
            else
            {
                var text = new TextBlock { Text = words, FontSize = 12, FontWeight = FontWeight.Medium, VerticalAlignment = VerticalAlignment.Center };
                text.Bind(TextBlock.ForegroundProperty, text.GetResourceObservable("Fg2"));
                content = text;
            }
            b.Content = content;
            ToolTip.SetTip(b, tip);
            AutomationProperties.SetName(b, words ?? tip);
            b.Click += (_, e) =>
            {
                e.Handled = true;
                click();
            };
            return b;
        }
        var percent = Tool(null, "100%", "Fit the window (0)", () => zoomer.Fit());
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 1,
            Children = { Tool("remove", null, "Zoom out (−)", () => zoomer.ZoomBy(1 / 1.25)), percent, Tool("add", null, "Zoom in (+)", () => zoomer.ZoomBy(1.25)) },
        };
        zoomer.Changed += () =>
        {
            if (percent.Content is TextBlock t) t.Text = $"{Math.Round(svg.Scale * zoomer.Zoom * 100):0}%";
        };
        var bar = new Border { Child = row, Padding = new Thickness(mac ? 14 : 12, 8) };
        var rule = new Border { Height = 1, VerticalAlignment = VerticalAlignment.Bottom };
        rule.Bind(Border.BackgroundProperty, rule.GetResourceObservable("Sep"));
        var top = new Panel { Children = { bar, rule } };

        Point? pressed = null;
        Point last = default;
        stage.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(stage).Properties.IsLeftButtonPressed) return;
            pressed = last = e.GetPosition(stage);
            e.Pointer.Capture(stage);
            stage.Focus(NavigationMethod.Pointer);
        };
        stage.PointerMoved += (_, e) =>
        {
            if (pressed is null) return;
            var p = e.GetPosition(stage);
            zoomer.PanBy(p - last);
            last = p;
        };
        stage.PointerReleased += (_, e) =>
        {
            pressed = null;
            e.Pointer.Capture(null);
        };
        stage.PointerWheelChanged += (_, e) =>
        {
            if (e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta))
                zoomer.ZoomBy(Math.Pow(1.18, e.Delta.Y + e.Delta.X), e.GetPosition(stage), glide: false);
            else zoomer.PanBy(new Vector(e.Delta.X, e.Delta.Y) * 40);
            e.Handled = true;
        };
        stage.PointerTouchPadGestureMagnify += (_, e) =>
        {
            zoomer.ZoomBy(1 + e.Delta.X, e.GetPosition(stage), glide: false);
            e.Handled = true;
        };
        stage.KeyDown += (_, e) =>
        {
            switch (e.Key)
            {
                case Key.OemPlus or Key.Add:
                    zoomer.ZoomBy(1.25);
                    break;
                case Key.OemMinus or Key.Subtract:
                    zoomer.ZoomBy(1 / 1.25);
                    break;
                case Key.D0 or Key.NumPad0:
                    zoomer.Fit();
                    break;
                case Key.Escape when zoomer.Zoom > 1.001:
                    zoomer.Fit();
                    break;
                default:
                    return;
            }
            e.Handled = true;
        };
        var dock = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        dock.Children.Add(top);
        dock.Children.Add(stage);
        return dock;
    }

    /// <summary>The diagram's own size: a chart's laid-out scene, a drawing's natural width.</summary>
    internal static Size Natural(OpenDiagramEventArgs request)
    {
        if (request.Scene is { } scene) return new Size(scene.Width, scene.Height);
        if (request.Svg is { } svg && Core.Rich.SafeSvg.Clean(svg) is { Svg: not null } d)
        {
            double width = SvgView.Natural(d.Width);
            return new Size(width, d.Height * width / d.Width);
        }
        return new Size(640, 400);
    }

    /// <summary>The window for a diagram: its size at one and a half times, with the margins, within 85% of the
    /// screen's working area and no smaller than the window's minimum.</summary>
    internal static (double Width, double Height) Size(Size natural, Size area)
    {
        double width = Math.Clamp(natural.Width * Grow + 2 * Pad, MinWidth, Math.Max(MinWidth, area.Width * 0.85));
        double height = Math.Clamp(natural.Height * Grow + 2 * Pad, MinHeight, Math.Max(MinHeight, area.Height * 0.85));
        return (Math.Round(width), Math.Round(height));
    }

    /// <summary>The working area (in the window's own units) of the screen the student is looking at.</summary>
    static Size Area(TopLevel top)
    {
        var screens = top.Screens;
        var screen = screens?.ScreenFromTopLevel(top) ?? screens?.Primary ?? screens?.All.FirstOrDefault();
        if (screen is null || screen.WorkingArea.Width <= 0) return new Size(1440, 900);
        return new Size(screen.WorkingArea.Width / screen.Scaling, screen.WorkingArea.Height / screen.Scaling);
    }
}
