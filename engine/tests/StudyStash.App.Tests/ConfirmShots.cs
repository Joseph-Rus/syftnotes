using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using StudyStash.App.Controls;
using StudyStash.App.ViewModels;
using StudyStash.App.ViewModels.Kit;
using StudyStash.App.Views;

namespace StudyStash.App.Tests;

/// <summary>
/// The one "Are you sure?" drawn over the full app as a student sees it, light and dark, both looks
/// (shots/confirm-&lt;question&gt;-&lt;mac|win&gt;-&lt;light|dark&gt;.png): deleting a lecture, and the preview that lists
/// every class "Use Canvas course names" would rename. Drawn off-screen, never on the student's screen.
/// </summary>
public class ConfirmShots
{
    static readonly ThemeVariant[] Themes = [ThemeVariant.Light, ThemeVariant.Dark];

    static async Task TakeAsync(string name, SkinKind skin, ThemeVariant variant, ConfirmAsk ask)
    {
        ((App)Application.Current!).UseSkin(skin);
        Control page = skin == SkinKind.Mac
            ? new MacLibrary { DataContext = Demo.Library() }
            : new WinLibrary { DataContext = Demo.Library() };
        var window = new Window { Width = 1280, Height = 800, RequestedThemeVariant = variant };
        window.Content = ConfirmLayer.Wrap(window, page);
        Look.Apply(window);
        window.Show();
        var asking = Confirm.Default.AskAsync(ask);
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
        DiagramsReady.Wait(window);
        var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("nothing rendered");
        using (var file = File.Create(Path.Combine(Shot.Dir, $"confirm-{name}-{(skin == SkinKind.Mac ? "mac" : "win")}-{(variant == ThemeVariant.Dark ? "dark" : "light")}.png")))
            frame.Save(file, PngBitmapEncoderOptions.Default);
        ConfirmLayer.Pick()!.Host.CurrentSession?.Close(false);
        Assert.False(await asking);
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData(SkinKind.Mac)]
    [InlineData(SkinKind.Win)]
    public async Task Delete_this_lecture(SkinKind skin)
    {
        var d = new LectureDeletion("lec-recursion", "Recursion and the call stack", "CS 101");
        foreach (var t in Themes) await TakeAsync("delete", skin, t, new ConfirmAsk(d.Ask, d.Warning, "Delete"));
    }

    [AvaloniaTheory]
    [InlineData(SkinKind.Mac)]
    [InlineData(SkinKind.Win)]
    public async Task Rename_classes_to_their_canvas_course_names(SkinKind skin)
    {
        var ask = new ConfirmAsk("Use Canvas course names?", "2 classes are named from course codes. Their lectures move with them.", "Rename 2 classes")
        {
            Lines = ["202710.TS.CSCI321.A becomes Software Engineering · 18 lectures move with it", "202710.TS.ENGR401.A becomes Senior Design · 14 lectures move with it"],
        };
        foreach (var t in Themes) await TakeAsync("rename", skin, t, ask);
    }
}
