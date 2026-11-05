using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using StudyStash.App.Controls;
using StudyStash.App.ViewModels;
using StudyStash.App.Views;

namespace StudyStash.App.Pictures;

/// <summary>
/// The ask for a tip ("Enjoying Syft Notes?") popped up over the library window on Home, light and dark, both looks
/// (shots/support-ask-&lt;mac|win&gt;-&lt;light|dark&gt;.png). Drawn off-screen, never on the student's screen.
/// </summary>
public class SupportAskShots
{
    [AvaloniaTheory]
    [InlineData(SkinKind.Mac)]
    [InlineData(SkinKind.Win)]
    public async Task Enjoying_syft_notes(SkinKind skin)
    {
        foreach (var variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            ((App)Application.Current!).UseSkin(skin);
            var library = Demo.Semester();
            Control page = skin == SkinKind.Mac ? new MacLibrary { DataContext = library } : new WinLibrary { DataContext = library };
            var window = new Window { Width = 1280, Height = 800, RequestedThemeVariant = variant };
            window.Content = ConfirmLayer.Wrap(window, page);
            Look.Apply(window);
            window.Show();
            var ask = library.AskForSupport(last: false)!;
            var showing = SupportAskDialog.ShowAsync(ConfirmLayer.For(window)!, ask);
            await Wait.UntilAsync(() => window.GetVisualDescendants().OfType<SupportAskDialog>().FirstOrDefault() is { IsEffectivelyVisible: true } d
                && d.FindControl<Button>("Later")!.IsFocused, "the ask to pop up");
            // It opens with a short fade and the veil comes down with it: draw once both have finished.
            var dialog = window.GetVisualDescendants().OfType<SupportAskDialog>().Single();
            var cover = ConfirmLayer.For(window)!.Host.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Rectangle>().Single(r => r.Name == "PART_ContentCover");
            await Wait.UntilAsync(() =>
            {
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                return cover.Opacity >= 1 && dialog.GetVisualAncestors().All(a => a.Opacity >= 1);
            }, "the ask to finish opening");
            DiagramsReady.Wait(window);
            var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("nothing rendered");
            using (var file = File.Create(Path.Combine(Shot.Dir, $"support-ask-{(skin == SkinKind.Mac ? "mac" : "win")}-{(variant == ThemeVariant.Dark ? "dark" : "light")}.png")))
                frame.Save(file, PngBitmapEncoderOptions.Default);
            ask.PutAway();
            await showing;
            window.Close();
        }
    }
}
