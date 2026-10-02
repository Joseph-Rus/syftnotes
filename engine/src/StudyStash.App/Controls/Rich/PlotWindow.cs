using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using StudyStash.App.Views;
using StudyStash.Core.Rich;

namespace StudyStash.App.Controls.Rich;

/// <summary>
/// A plot from the notes, larger, in a window of its own, the way a diagram opens: the toolbar across the top (zoom,
/// back to where it opened, reset the sliders, predict it, explain it, the lecture's moment), the plot filling the
/// window, its sliders under it, set where the note's were. Esc (once nothing in it is pinned or zoomed) or ⌘W
/// (Ctrl+W) closes it. There's only ever one: opening another plot shows it in the same window.
/// </summary>
public static class PlotWindow
{
    public const double Pad = 28;

    public static Window? Current { get; private set; }

    public static Window Open(PlotView from, TopLevel? owner)
    {
        Plot plot;
        try
        {
            plot = Plot.Parse(from.Source);
        }
        catch (PlotException)
        {
            plot = from.Plot;
        }
        var view = new PlotView(plot, from.Source, windowed: true) { Origin = from, Caption = from.Caption };
        // Where the note's sliders were, and what it hid.
        for (int i = 0; i < plot.Params.Count && i < from.State.Values.Length; i++) view.State.Set(i, from.State.Values[i]);
        view.Canvas.Look = view.Canvas.Look with { Hidden = from.Canvas.Look.Hidden.Select(h => plot.Items.ElementAtOrDefault(from.Plot.Items.IndexOf(h))).OfType<PlotItem>().ToHashSet() };
        view.AskedFromWindow = () => (TopLevel.GetTopLevel(from) as Window)?.Activate();
        var ground = new Border { Child = view };
        ground.Bind(Border.BackgroundProperty, ground.GetResourceObservable(Skin.Current == SkinKind.Mac ? "Win" : "Mica"));

        var w = Current;
        if (w is null)
        {
            w = new Window { MinWidth = 460, MinHeight = 380, WindowStartupLocation = WindowStartupLocation.CenterScreen, CanResize = true };
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
            var area = Area(owner ?? w);
            w.Width = Math.Round(Math.Clamp(from.Bounds.Width * 1.45 + 2 * Pad, 560, Math.Max(560, area.Width * 0.85)));
            w.Height = Math.Round(Math.Clamp(from.Bounds.Height * 1.4 + 120, 440, Math.Max(440, area.Height * 0.85)));
            Current = w;
        }
        w.Title = view.Title;
        w.Content = ground;
        w.Show();
        w.Activate();
        view.Focus(NavigationMethod.Pointer);
        return w;
    }

    static Size Area(TopLevel top)
    {
        var screens = top.Screens;
        var screen = screens?.ScreenFromTopLevel(top) ?? screens?.Primary ?? screens?.All.FirstOrDefault();
        if (screen is null || screen.WorkingArea.Width <= 0) return new Size(1440, 900);
        return new Size(screen.WorkingArea.Width / screen.Scaling, screen.WorkingArea.Height / screen.Scaling);
    }
}
