using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using StudyStash.App.Controls;
using StudyStash.App.Controls.Rich;
using StudyStash.Core.Rich;

namespace StudyStash.App.Tests;

/// <summary>
/// The plots as a note shows them, light and dark, in both looks: each sample (a sigmoid with its slider, activation
/// functions, the normal distribution with a shaded probability, growth rates, a tangent, gradient descent on a loss
/// surface, a matrix's map, the binomial distribution) under its heading and sentence, in the notes column; then one
/// being read (a pinned reading's crosshair and its actions) and one being predicted.
/// </summary>
public class PlotShots
{
    static readonly ThemeVariant[] Themes = [ThemeVariant.Light, ThemeVariant.Dark];

    static PlotShots() => Environment.SetEnvironmentVariable("STUDYSTASH_STILL", "1");

    static string Notes(IEnumerable<(string Title, string Words, string Source)> plots) =>
        string.Join("\n\n", plots.Select(p => $"## {p.Title}\n\n{p.Words}\n\n```plot\n{p.Source}\n```"));

    [AvaloniaFact]
    public void Plots()
    {
        var all = PlotDesign.Examples;
        foreach (var skin in new[] { SkinKind.Mac, SkinKind.Win })
            foreach (var t in Themes)
            {
                string look = skin == SkinKind.Mac ? "mac" : "win";
                for (int page = 0; page < 4; page++)
                    Shot.Take($"plots-{page + 1}-{look}", skin, t, () => RichShots.NotePage(skin, Notes(all.Skip(page * 2).Take(2))), size: new Size(876, 1500));
            }
    }

    /// <summary>A plot being read: the pointer pinned a reading on the sigmoid (its crosshair, the curve's dot, the tip
    /// with its value, the actions beside it).</summary>
    [AvaloniaFact]
    public void Plot_reading()
    {
        foreach (var skin in new[] { SkinKind.Mac, SkinKind.Win })
            foreach (var t in Themes)
                Shot.Take($"plot-reading-{(skin == SkinKind.Mac ? "mac" : "win")}", skin, t, () => RichShots.NotePage(skin, Notes(PlotDesign.Examples.Take(1)), note =>
                {
                    var view = note.GetLogicalDescendants().OfType<PlotView>().Single();
                    view.AttachedToVisualTree += (_, _) => Dispatcher.UIThread.Post(() =>
                    {
                        view.Canvas.Pin(1.6, 0.5);
                        view.State.Set(0, 1.8);
                        view.Canvas.Refresh();
                    });
                }), size: new Size(876, 760));
    }
}
