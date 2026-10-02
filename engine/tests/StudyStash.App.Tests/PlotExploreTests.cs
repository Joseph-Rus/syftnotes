using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StudyStash.App.Controls;
using StudyStash.App.Controls.Rich;
using StudyStash.Core.Rich;

namespace StudyStash.App.Tests;

/// <summary>
/// Playing with a plot the way a student does, with the pointer and the keys: a slider dragged redraws it, the pointer
/// reads its values and a click pins them, a tangent's point drags along the curve, ⌘/Ctrl + scroll zooms and 0 goes
/// back, the legend shows and hides a curve, and a curve can be predicted (sketched, then revealed and scored).
/// </summary>
public class PlotExploreTests
{
    static (Window Window, PlotView View) Open(string source)
    {
        var note = new NoteView { Markdown = "```plot\n" + source + "\n```", Width = 620 };
        var w = new Window { Width = 700, Height = 900, Content = note };
        w.Show();
        Dispatcher.UIThread.RunJobs();
        return (w, note.GetLogicalDescendants().OfType<PlotView>().Single());
    }

    /// <summary>A point of the plane where it's drawn in the window.</summary>
    static Point At(Window w, PlotView view, double x, double y)
    {
        var p = view.Canvas.Scene!.Map.Screen(x, y);
        return view.Canvas.TranslatePoint(new Point(p.X, p.Y), w)!.Value;
    }

    static void Drag(Window w, Point from, Point to)
    {
        w.MouseMove(from);
        w.MouseDown(from, MouseButton.Left);
        for (int i = 1; i <= 8; i++) w.MouseMove(from + (to - from) * (i / 8.0), RawInputModifiers.LeftMouseButton);
        w.MouseUp(to, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void A_dragged_slider_redraws_the_plot_and_says_its_value()
    {
        var (w, view) = Open(PlotDesign.Sigmoid);
        var slider = view.GetVisualDescendants().OfType<PlotSlider>().Single();
        var value = ((Grid)slider.Parent!).Children.OfType<TextBlock>().Last();
        var before = view.Canvas.Scene!.Data.OfType<PlotLines>().First(l => l.Item is PlotCurve { Flat: false }).Lines[0].ToList();
        var left = slider.TranslatePoint(new Point(10, 12), w)!.Value;
        var right = slider.TranslatePoint(new Point(slider.Bounds.Width + 30, 12), w)!.Value; // past its end: it stops there
        Drag(w, left, right);
        Assert.Equal(5, view.State.Values[0], 6);
        Assert.Equal("5", value.Text);
        var after = view.Canvas.Scene!.Data.OfType<PlotLines>().First(l => l.Item is PlotCurve { Flat: false }).Lines[0].ToList();
        Assert.False(before.SequenceEqual(after));
        // R puts it back.
        view.Focus();
        w.KeyPress(Key.R, RawInputModifiers.None, PhysicalKey.R, "r");
        Assert.True(view.State.AtDefaults);
        w.Close();
    }

    [AvaloniaFact]
    public void The_pointer_reads_the_curve_and_a_click_pins_it()
    {
        var (w, view) = Open(PlotDesign.Sigmoid);
        var p = At(w, view, 2, 0.3);
        w.MouseMove(p);
        Dispatcher.UIThread.RunJobs();
        var reading = view.Canvas.ReadAt(view.Canvas.Pointer!.Value)!;
        Assert.Equal("z = 2", reading.Heading.Replace("2.00", "2"));
        Assert.Equal(PlotNumber.Format(1 / (1 + Math.Exp(-2))), reading.Rows.Single().Value);
        w.MouseDown(p, MouseButton.Left);
        w.MouseUp(p, MouseButton.Left);
        Assert.True(view.Canvas.IsPinned);
        w.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Assert.False(view.Canvas.IsPinned);
        w.Close();
    }

    [AvaloniaFact]
    public void A_tangents_point_drags_along_the_curve()
    {
        var (w, view) = Open(PlotDesign.Tangent);
        var handle = view.Canvas.Scene!.Handles.Single();
        var from = view.Canvas.TranslatePoint(new Point(handle.At.X, handle.At.Y), w)!.Value;
        Drag(w, from, At(w, view, 0.5, 1));
        Assert.Equal(0.5, view.State.Values[0], 1);
        var tangent = view.Plot.Items.OfType<PlotTangent>().Single();
        Assert.True(view.State.Env.Slots[tangent.SlopeSlot] < 0); // left of the minimum, it slopes down
        w.Close();
    }

    [AvaloniaFact]
    public void Command_scroll_zooms_in_and_0_goes_back()
    {
        var (w, view) = Open(PlotDesign.Normal);
        var p = At(w, view, 0, 0.2);
        w.MouseWheel(p, new Vector(0, 3), RawInputModifiers.Control);
        Assert.True(view.State.Zoomed);
        Assert.True(view.State.View.Width < view.State.Home.Width);
        // Plain scrolling is the note's: it doesn't zoom.
        double width = view.State.View.Width;
        w.MouseWheel(p, new Vector(0, -1));
        Assert.Equal(width, view.State.View.Width, 9);
        view.Focus();
        w.KeyPress(Key.D0, RawInputModifiers.None, PhysicalKey.Digit0, "0");
        Assert.False(view.State.Zoomed);
        w.Close();
    }

    [AvaloniaFact]
    public void The_legend_shows_and_hides_a_curve()
    {
        var (w, view) = Open(PlotDesign.Activations);
        var key = view.Canvas.Scene!.Keys.First(k => k.Text == "softplus");
        var at = view.Canvas.TranslatePoint(new Point(key.Box.X + 10, key.Box.Y + key.Box.H / 2), w)!.Value;
        w.MouseMove(at);
        w.MouseDown(at, MouseButton.Left);
        w.MouseUp(at, MouseButton.Left);
        Assert.DoesNotContain(view.Canvas.Scene!.Data, m => m.Item == key.Item);
        Assert.True(view.Canvas.Scene!.Keys.Single(k => k.Text == "softplus").Off);
        w.MouseDown(at, MouseButton.Left);
        w.MouseUp(at, MouseButton.Left);
        Assert.Contains(view.Canvas.Scene!.Data, m => m.Item == key.Item);
        w.Close();
    }

    [AvaloniaFact]
    public void A_curve_can_be_predicted_then_revealed_and_scored()
    {
        var (w, view) = Open(PlotDesign.Tangent);
        view.Focus();
        w.KeyPress(Key.P, RawInputModifiers.None, PhysicalKey.P, "p");
        Assert.True(view.Canvas.Look.Predicting);
        Assert.DoesNotContain(view.Canvas.Scene!.Data, m => m.Item is PlotCurve);
        // A good sketch: along the curve itself.
        w.MouseMove(At(w, view, -0.8, 0.5 * Math.Pow(-0.8 - 1.4, 2) + 0.6));
        w.MouseDown(At(w, view, -0.8, 0.5 * Math.Pow(-0.8 - 1.4, 2) + 0.6), MouseButton.Left);
        for (double x = -0.8; x <= 3.8; x += 0.1) w.MouseMove(At(w, view, x, 0.5 * Math.Pow(x - 1.4, 2) + 0.6), RawInputModifiers.LeftMouseButton);
        w.MouseUp(At(w, view, 3.8, 3.48), MouseButton.Left);
        Assert.True(view.Canvas.Sketch.Count > 10);
        var reveal = view.GetVisualDescendants().OfType<Button>().Single(b => AutomationName(b) == "Reveal");
        reveal.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.False(view.Canvas.Look.Predicting);
        Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(), t => t.Text?.StartsWith("Spot on", StringComparison.Ordinal) == true);
        w.Close();
    }

    static string? AutomationName(Control c) => Avalonia.Automation.AutomationProperties.GetName(c);
}
