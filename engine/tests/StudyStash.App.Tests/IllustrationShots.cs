using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StudyStash.App.Controls;
using StudyStash.App.Controls.Rich;
using StudyStash.App.Views;

namespace StudyStash.App.Tests;

/// <summary>
/// An illustration in a note, as the app draws it (light and dark, both looks): still, with a part lit and pinned (its
/// card beside it), and while testing yourself. The drawing is <c>STUDYSTASH_ILLUSTRATION</c> (a drawn illustration's
/// SVG, to look at a real one) or else the small drone the tests use.
/// </summary>
public class IllustrationShots
{
    static IllustrationShots() => Environment.SetEnvironmentVariable("STUDYSTASH_STILL", "1");

    [AvaloniaFact]
    public void Illustration_in_a_note()
    {
        string svg = Environment.GetEnvironmentVariable("STUDYSTASH_ILLUSTRATION") is { Length: > 0 } path && File.Exists(path) ? File.ReadAllText(path) : IllustrationViewTests.Drone;
        string name = Environment.GetEnvironmentVariable("STUDYSTASH_ILLUSTRATION_NAME") is { Length: > 0 } n ? n : "drone";
        string markdown = "## The figure\n\nWhat the lecture described, drawn.\n\n```svg\n" + svg + "\n```\n\n*The caption goes here.*";
        Platform.Motion.Override = true;
        try
        {
            foreach (var skin in new[] { SkinKind.Mac, SkinKind.Win })
                foreach (var t in new[] { ThemeVariant.Light, ThemeVariant.Dark })
                {
                    Skin.UseTheme(ColourThemes.Default);
                    ((App)Application.Current!).UseSkin(skin);
                    var size = new Size(876, 1100);
                    var window = new Window { Width = size.Width, Height = size.Height, RequestedThemeVariant = t, Content = RichShots.NotePage(skin, markdown) };
                    Look.Apply(window);
                    window.Show();
                    Dispatcher.UIThread.RunJobs();
                    var view = window.GetVisualDescendants().OfType<SvgView>().Single();
                    string look = skin == SkinKind.Mac ? "mac" : "win";
                    Save($"illustration-{name}-{look}-still", t, window, size);
                    if (view.Explorer is not { } x) continue;
                    var until = DateTime.UtcNow.AddSeconds(30);
                    while (x.Map is null && DateTime.UtcNow < until)
                    {
                        Thread.Sleep(5);
                        Dispatcher.UIThread.RunJobs();
                    }
                    // The part with the most to say, pinned with its card.
                    var part = x.Parts.OrderByDescending(p => x.Map?.Of(p.Id) is { } b ? Math.Min(b.W, b.H) : 0).First();
                    if (x.Map?.Of(part.Id) is { } box)
                    {
                        var canvas = view.GetVisualDescendants().OfType<SvgCanvas>().Single();
                        double k = canvas.Bounds.Width / x.Drawing!.Width;
                        var p = canvas.TranslatePoint(new Point(box.CenterX * k, box.CenterY * k), window)!.Value;
                        string? hit = x.PartAt(new Point(box.CenterX * k, box.CenterY * k));
                        window.MouseMove(p);
                        window.MouseDown(p, MouseButton.Left);
                        window.MouseUp(p, MouseButton.Left);
                        Dispatcher.UIThread.RunJobs();
                        Save($"illustration-{name}-{look}-pinned", t, window, size);
                        x.Unpin();
                        window.MouseMove(new Point(2, 2));
                    }
                    x.ToggleLabels();
                    Dispatcher.UIThread.RunJobs();
                    Save($"illustration-{name}-{look}-nolabels", t, window, size);
                    x.ToggleLabels();
                    x.StartRecall();
                    if (x.Parts.FirstOrDefault() is { } first) x.Reveal(first.Id);
                    Dispatcher.UIThread.RunJobs();
                    Save($"illustration-{name}-{look}-test", t, window, size);
                    window.Close();
                }
        }
        finally
        {
            Platform.Motion.Override = null;
        }
    }

    static void Save(string name, ThemeVariant variant, Window window, Size size)
    {
        var whole = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("nothing rendered");
        var frame = new RenderTargetBitmap(new PixelSize((int)size.Width, (int)size.Height));
        using (var ctx = frame.CreateDrawingContext()) ctx.DrawImage(whole, new Rect(size), new Rect(size));
        using var file = File.Create(Path.Combine(Shot.Dir, $"{name}-{(variant == ThemeVariant.Dark ? "dark" : "light")}.png"));
        frame.Save(file, PngBitmapEncoderOptions.Default);
    }
}
