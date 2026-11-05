using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StudyStash.App.Controls;
using StudyStash.App.Services;
using StudyStash.App.Tests;
using StudyStash.App.Views;
using StudyStash.Core;

namespace StudyStash.App.Pictures;

/// <summary>
/// Settings → Classes, the × on a class: the question it asks first, over the page, light and dark, both looks
/// (shots/confirm-remove-class-&lt;mac|win&gt;-&lt;light|dark&gt;.png), and the page once it's said yes
/// (shots/settings-classes-removed-&lt;mac|win&gt;-&lt;light|dark&gt;.png). A stand-in library and a throwaway home;
/// drawn off-screen, never on the student's screen.
/// </summary>
public class ClassRemoveShots
{
    static void Save(Window window, string name)
    {
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("nothing rendered");
        using var file = File.Create(Path.Combine(Shot.Dir, name + ".png"));
        frame.Save(file, PngBitmapEncoderOptions.Default);
    }

    static async Task TakeAsync(SkinKind skin, ThemeVariant variant)
    {
        ((App)Application.Current!).UseSkin(skin);
        string tail = $"{(skin == SkinKind.Mac ? "mac" : "win")}-{(variant == ThemeVariant.Dark ? "dark" : "light")}";
        using var home = new TempHome();
        new AppSettings { SetupDone = true, Role = AppRole.Laptop }.Save(home.Path);
        var cc = Configs.LoadClient(home.Path);
        cc.ServerUrl = "http://library.example:9";
        cc.PoolKey = "pw";
        cc.PoolName = "Sam's library";
        Configs.SaveClient(cc);
        using var host = new AppHost(home.Path, log: _ => { });
        var fake = new FakeLibrarySettings();
        using var model = SettingsModel.Make(host, library: () => fake.Call);
        var window = new Window { Width = 1000, Height = 780, RequestedThemeVariant = variant };
        window.Content = ConfirmLayer.Wrap(window, new SettingsView { DataContext = model, DrawChrome = true });
        Look.Apply(window);
        window.Show();
        model.Section = "Classes";
        await Wait.UntilAsync(() => model.Lib.Classes.Any(c => c.Name == "BIO 110"), "the classes to load");

        var removing = model.Lib.RemoveClassCommand.ExecuteAsync(model.Lib.Classes.Single(c => c.Name == "BIO 110"));
        await Wait.UntilAsync(() => window.GetVisualDescendants().OfType<ConfirmDialog>().FirstOrDefault() is { IsEffectivelyVisible: true } d && d.FindControl<Button>("No")!.IsFocused,
            "the question to show");
        // The question opens with a short fade and the veil comes down with it: draw once both have finished.
        var dialog = window.GetVisualDescendants().OfType<ConfirmDialog>().Single();
        var cover = ConfirmLayer.Pick()!.Host.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Rectangle>().Single(r => r.Name == "PART_ContentCover");
        await Wait.UntilAsync(() =>
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            return cover.Opacity >= 1 && dialog.GetVisualAncestors().All(a => a.Opacity >= 1);
        }, "the question to finish opening");
        Save(window, $"confirm-remove-class-{tail}");

        ConfirmLayer.Pick()!.Host.CurrentSession?.Close(true);
        await removing;
        Assert.DoesNotContain(model.Lib.Classes, c => c.Name == "BIO 110");
        await Wait.UntilAsync(() =>
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            return !window.GetVisualDescendants().OfType<ConfirmDialog>().Any() && cover.Opacity <= 0;
        }, "the question to close");
        Dispatcher.UIThread.RunJobs();
        Save(window, $"settings-classes-removed-{tail}");
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData(SkinKind.Mac)]
    [InlineData(SkinKind.Win)]
    public async Task Remove_a_class(SkinKind skin)
    {
        try
        {
            foreach (var t in new[] { ThemeVariant.Light, ThemeVariant.Dark }) await TakeAsync(skin, t);
        }
        finally
        {
            ((App)Application.Current!).UseSkin(SkinKind.Mac);
        }
    }
}
