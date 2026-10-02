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

    /// <summary>A real answer from the Ask bar (Claude, asked to plot how a network's linear regions grow, on the
    /// 28 Sep machine learning lecture): its sentences, then its plot, in the answer's compact column.</summary>
    [AvaloniaFact]
    public void Plot_in_an_answer()
    {
        if (Environment.GetEnvironmentVariable("STUDYSTASH_PLOT_ANSWER") is not { Length: > 0 } file) return;
        string answer = File.ReadAllText(file);
        foreach (var t in Themes)
            Shot.Take("plot-answer-mac", SkinKind.Mac, t, () =>
            {
                var note = new NoteView { Markdown = answer, Compact = true, BodySize = 14, BodyLineHeight = 21, Width = 520 };
                var page = new Border { Padding = new Thickness(24), Child = note };
                page.Bind(Border.BackgroundProperty, page.GetResourceObservable("Win"));
                return page;
            }, size: new Size(700, 760));
    }

    /// <summary>A note the diagram designer drew a plot into (a real run), as the library's page shows it.</summary>
    [AvaloniaFact]
    public void Plot_in_designed_notes()
    {
        if (Environment.GetEnvironmentVariable("STUDYSTASH_PLOT_NOTE") is not { Length: > 0 } file) return;
        string notes = File.ReadAllText(file);
        foreach (var t in Themes)
            Shot.Take("plot-designed-mac", SkinKind.Mac, t, () => RichShots.NotePage(SkinKind.Mac, notes), size: new Size(876, 1500));
    }

    /// <summary>A lecture page that can be asked about and has a transcript, so a plot's own buttons all show.</summary>
    sealed class Page : IDiagramHost
    {
        public bool CanAsk => true;
        public void Ask(string question) { }
        public IReadOnlyList<StudyStash.Core.Spoken> Transcript { get; } = [new(760, 770, "The sigmoid squashes the score.")];
        public void ShowTranscript(double seconds) { }
        public bool CanPlay => true;
        public void Play(double seconds) { }
    }

    /// <summary>A machine learning lecture's notes with the plots the diagram pass would add.</summary>
    internal static string MlLecture => "## Summary\n\nLogistic regression squashes a linear score into a probability with the sigmoid, and is fitted by gradient descent.\n\n"
        + "## Key points\n\n- The sigmoid turns a score into a probability:\n\n$$\\sigma(z) = \\frac{1}{1 + e^{-kz}}$$\n\n"
        + "**The sigmoid squashes a score into a probability**\n\n```plot\n%% Study Stash diagram, from 12:40\n" + string.Join("\n", PlotDesign.Sigmoid.Split('\n').Skip(1)) + "\ntangent σ at 0 \"slope {slope}\"\n```\n\n"
        + "*σ goes to 0 for very negative scores and 1 for very positive ones, crossing one half at 0. Raise k and the S becomes a step.* (from 12:40)\n\n"
        + "## Gradient descent\n\n- Each step moves the weights against the gradient, scaled by the learning rate η.\n\n"
        + "**Gradient descent on a long, narrow bowl**\n\n```plot\n%% Study Stash diagram, from 31:05\n" + string.Join("\n", PlotDesign.Surface.Split('\n').Skip(1)) + "\n```\n\n"
        + "*Too large a learning rate zigzags across the narrow direction; past 0.2 it diverges.* (from 31:05)";

    /// <summary>The whole app on that lecture, scrolled to its first plot, the pointer reading it: its toolbar in the
    /// corner, its crosshair and tip, a pinned reading's actions.</summary>
    [AvaloniaFact]
    public void Full_app_plot_lecture()
    {
        foreach (var (skin, look) in new[] { (SkinKind.Mac, "mac"), (SkinKind.Win, "win") })
            foreach (var t in Themes)
            {
                Skin.UseTheme(ColourThemes.Default);
                ((App)Application.Current!).UseSkin(skin);
                var size = new Size(1280, 800);
                var model = RichShots.CardiacLibrary();
                model.Note = new ViewModels.NoteModel { ClassName = "CSCI 4220", Dot = Skin.ClassDot(0), Meta = "Mon 28 Sep · 52 min", Title = "Logistic regression", Markdown = MlLecture };
                model.Diagrams = new Page();
                Control content = skin == SkinKind.Mac
                    ? new Views.MacLibrary { DataContext = model, Width = size.Width, Height = size.Height }
                    : new Views.WinLibrary { DataContext = model, Width = size.Width, Height = size.Height };
                var window = new Window { Width = size.Width, Height = size.Height, RequestedThemeVariant = t, Content = content };
                Views.Look.Apply(window);
                window.Show();
                Dispatcher.UIThread.RunJobs();
                var scroller = window.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault(s => s.GetVisualDescendants().OfType<NoteView>().Any());
                var plot = window.GetVisualDescendants().OfType<PlotView>().First();
                if (scroller is not null)
                {
                    scroller.Offset = new Vector(0, Math.Max(0, scroller.Offset.Y + (plot.TranslatePoint(default, scroller)?.Y ?? 0) - 60));
                    Dispatcher.UIThread.RunJobs();
                }
                var map = plot.Canvas.Scene!.Map;
                plot.Canvas.Pin(1.4, 0.5);
                var hover = plot.Canvas.TranslatePoint(new Point(map.ScreenX(-2.2), map.ScreenY(0.3)), window)!.Value;
                window.MouseMove(hover);
                Dispatcher.UIThread.RunJobs();
                RichShots.Save($"plot-full-app-{look}", t, window, size);
                window.Close();
            }
    }
}
