using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StudyStash.App.Controls;
using StudyStash.App.Controls.Rich;
using StudyStash.Core.Rich;

namespace StudyStash.App.Tests;

/// <summary>
/// A plot in a note: on screen a slider for each parameter that redraws it, fast enough to drag; on paper the still
/// picture at its sliders' starting values, saying what they are, with nothing to move; and one that can't be read is
/// the calm card with the line that's wrong.
/// </summary>
public class PlotViewTests
{
    static Window Show(Control content, double width = 700)
    {
        var w = new Window { Width = width, Height = 1400, Content = content };
        w.Show();
        Dispatcher.UIThread.RunJobs();
        return w;
    }

    static string Fenced(string source) => "## Plot\n\n```plot\n" + source + "\n```\n\nAfter it.";

    [AvaloniaFact]
    public void On_paper_a_plot_is_the_still_picture_at_its_starting_values()
    {
        var note = new NoteView { Markdown = Fenced(PlotDesign.Normal), PageHeight = 900, Width = 620 };
        var w = Show(note);
        var view = Assert.Single(note.GetLogicalDescendants().OfType<PlotView>());
        Assert.True(view.Still);
        Assert.True(view.Canvas.Still);
        Assert.Empty(view.GetVisualDescendants().OfType<PlotSlider>());
        Assert.Empty(view.GetVisualDescendants().OfType<Button>());
        Assert.True(view.State.AtDefaults);
        Assert.Contains("Drawn at μ = 0 (mean μ), σ = 1 (spread σ).", view.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));
        Assert.NotNull(view.Canvas.Scene);
        Assert.True(view.Bounds.Height > 250);
        w.Close();
    }

    [AvaloniaFact]
    public void On_screen_each_parameter_has_a_slider_that_redraws_the_plot()
    {
        var note = new NoteView { Markdown = Fenced(PlotDesign.Binomial), Width = 620 };
        var w = Show(note);
        var view = Assert.Single(note.GetLogicalDescendants().OfType<PlotView>());
        var sliders = view.GetVisualDescendants().OfType<PlotSlider>().ToList();
        Assert.Equal(2, sliders.Count);
        int bars = view.Canvas.Scene!.Data.OfType<PlotBar>().Count();
        // n from 10 to 20: more bars.
        view.State.Set(0, 20);
        view.Canvas.Refresh();
        Dispatcher.UIThread.RunJobs();
        Assert.True(view.Canvas.Scene!.Data.OfType<PlotBar>().Count() > bars);
        Assert.False(view.State.AtDefaults);
        w.Close();
    }

    [AvaloniaFact]
    public void A_plot_that_cant_be_read_is_the_calm_card_saying_which_line()
    {
        var note = new NoteView { Markdown = Fenced("x -5 to 5\ny = sigm(x)"), Width = 620 };
        var w = Show(note);
        var card = Assert.Single(note.GetLogicalDescendants().OfType<DiagramCard>());
        Assert.Contains("Line 2", card.Reason);
        Assert.Contains("sigm", card.Reason);
        w.Close();
    }

    /// <summary>Every sample laid out again as a slider moves, at the coarser detail a drag uses on a heavy plot: well
    /// within a frame on a normal laptop (the bound here is loose, so a slow test machine doesn't fail it).</summary>
    [AvaloniaFact]
    public void Dragging_a_slider_lays_the_plot_out_again_quickly()
    {
        foreach (var (title, _, source) in PlotDesign.Examples)
        {
            var state = new PlotState(Plot.Parse(source));
            double Measure(string text, double size, bool bold) => text.Length * size * 0.55;
            var look = new PlotLook { Detail = state.Plot.Heavy ? 0.5 : 1 };
            PlotLayout.Build(state, 620, Measure, look);
            var clock = Stopwatch.StartNew();
            const int frames = 20;
            for (int i = 0; i < frames; i++)
            {
                if (state.Plot.Params.Count > 0)
                {
                    var p = state.Plot.Params[0];
                    state.Set(0, p.Min + (p.Max - p.Min) * i / frames);
                }
                PlotLayout.Build(state, 620, Measure, look);
            }
            double ms = clock.Elapsed.TotalMilliseconds / frames;
            Assert.True(ms < 120, $"{title}: {ms:0.0} ms a frame");
        }
    }

    /// <summary>On paper (a PDF), a plot's words are text and its lines are vectors, at the sliders' starting values.</summary>
    [AvaloniaFact]
    public async Task A_pdf_draws_the_plot_as_vectors_with_its_words_as_text()
    {
        Skin.UseTheme(ColourThemes.Default);
        ((App)Application.Current!).UseSkin(SkinKind.Mac);
        Application.Current!.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Light;
        var lecture = new System.Text.Json.Nodes.JsonObject
        {
            ["title"] = "Logistic regression", ["class"] = "CSCI 4220", ["date"] = "2026-09-28T09:00:00",
            ["notes"] = "## The sigmoid\n\n```plot\n" + PlotDesign.Sigmoid + "\n```\n\n## Growth\n\n```plot\n" + PlotDesign.Growth + "\n```",
        };
        using var output = new MemoryStream();
        int pages = await Services.NotesPdf.WriteAsync(output, lecture, false, Services.Paper.Letter, Avalonia.Media.Colors.Teal);
        var pdf = new PdfProbe(output.ToArray());
        if (Environment.GetEnvironmentVariable("STUDYSTASH_SHOTS") is { Length: > 0 } dir)
        {
            Directory.CreateDirectory(dir); // this test may be the first to keep a picture
            await File.WriteAllBytesAsync(Path.Combine(dir, "plot-paper.pdf"), output.ToArray());
        }
        Assert.True(pdf.IsPdf);
        Assert.True(pdf.Count("threshold 0.5") == 1);
        Assert.True(pdf.Count("Drawn at k = 1 (steepness k).") == 1);
        Assert.True(pdf.Count("n log₂ n") >= 1);
        Assert.False(pdf.HasImages);
        Assert.Empty(pdf.FontsNotEmbedded);
        Assert.InRange(pages, 1, 3);
    }
}
