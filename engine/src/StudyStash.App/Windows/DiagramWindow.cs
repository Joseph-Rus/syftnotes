using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using StudyStash.App.Controls.Rich;
using StudyStash.App.Views;

namespace StudyStash.App.Windows;

/// <summary>
/// A diagram from the notes, larger, in a window of its own: sized to show it at one and a half times (never more
/// than 85% of the screen), fitted to whatever size the student makes the window (up to twice its own size). Esc or
/// ⌘W (Ctrl+W) closes it. There's only ever one: opening another diagram shows it in the same window.
/// </summary>
public static class DiagramWindow
{
    const double Pad = 32, Grow = 1.5, MaxScale = 2, MinWidth = 420, MinHeight = 300;

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
            var (width, height) = Size(Natural(request), Area(from ?? w));
            w.Width = width;
            w.Height = height;
            Current = w;
        }
        w.Title = request.Title;
        w.Content = content;
        w.Show();
        w.Activate();
        return w;
    }

    /// <summary>What the window shows: the diagram, centred on the window's ground with a margin, fitted to it.</summary>
    internal static Control Content(OpenDiagramEventArgs request)
    {
        Control view = request.Chart is { } chart
            ? new DiagramView(opensLarger: false) { Chart = chart, MaxScale = MaxScale }
            : new SvgView(opensLarger: false) { Source = request.Svg, MaxScale = MaxScale };
        view.VerticalAlignment = VerticalAlignment.Center;
        var content = new Border { Padding = new Thickness(Pad), Child = view };
        content.Bind(Border.BackgroundProperty, content.GetResourceObservable(Skin.Current == SkinKind.Mac ? "Win" : "Mica"));
        return content;
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
