using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StudyStash.App.Controls;
using StudyStash.App.Controls.Rich;
using StudyStash.App.ViewModels;
using StudyStash.App.Views;
using StudyStash.App.Windows;
using StudyStash.Core.Rich;

namespace StudyStash.App.Tests;

public class DiagramLayingTests
{
    /// <summary>A big chart still being laid out: the quiet space its note keeps for it, about its size, with the
    /// words after it already in place.</summary>
    [AvaloniaFact]
    public void Diagram_laying()
    {
        var random = new Random(11);
        // Words no earlier picture laid out (the same length every time, and hidden until the chart is drawn).
        string stamp = Guid.NewGuid().ToString("N")[..6];
        var lines = new List<string> { "flowchart TD" };
        for (int i = 0; i < 40; i++) lines.Add($"N{i}[Admission step {i} {stamp}]");
        for (int i = 0; i < 70; i++) lines.Add($"N{random.Next(40)} --> N{random.Next(40)}");
        string markdown = "## Admitting a patient\n\nEvery step of an admission, from the door to the ward.\n\n```mermaid\n" + string.Join("\n", lines) +
            "\n```\n\nThe notes go on below while the chart is drawn.";
        using var release = new ManualResetEventSlim(false);
        SceneCache.Laying = _ => release.Wait(TimeSpan.FromSeconds(30));
        try
        {
            foreach (var skin in new[] { SkinKind.Mac, SkinKind.Win })
                foreach (var t in new[] { ThemeVariant.Light })
                {
                    Skin.UseTheme(ColourThemes.Default);
                    ((App)Application.Current!).UseSkin(skin);
                    var size = new Size(876, 960);
                    var window = new Window { Width = size.Width, Height = size.Height, RequestedThemeVariant = t, Content = Scenes.NotePage(skin, markdown) };
                    Look.Apply(window);
                    window.Show();
                    Dispatcher.UIThread.RunJobs();
                    Assert.True(window.GetVisualDescendants().OfType<DiagramView>().Single().IsLaying);
                    window.Close();
                }
        }
        finally
        {
            SceneCache.Laying = null;
            release.Set();
        }
    }
}
