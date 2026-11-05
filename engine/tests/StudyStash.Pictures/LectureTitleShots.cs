using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using StudyStash.App.ViewModels;
using StudyStash.App.Views;

namespace StudyStash.App.Pictures;

/// <summary>
/// A lecture's title, which is typed over to rename it, drawn off-screen as a student sees it: at rest (a plain
/// heading), with the pointer over it, and with the caret in it
/// (shots/lecture-title-&lt;rest|hover|focus&gt;-&lt;mac|win&gt;-&lt;light|dark&gt;.png).
/// </summary>
public class LectureTitleShots
{
    static LibraryModel Lecture() => new()
    {
        Note = new NoteModel { Id = "lec-recursion", ClassName = "CS 101", Title = "Recursion and the call stack", When = "Monday 12 October at 9:30 AM, 52 min" }.Titled(),
    };

    static void Save(Window window, string name)
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        var whole = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("nothing rendered");
        var rtb = new RenderTargetBitmap(new PixelSize(900, 240));
        using (var ctx = rtb.CreateDrawingContext())
            ctx.DrawImage(whole, new Rect(0, 0, 900, 240), new Rect(0, 0, 900, 240));
        using var file = File.Create(Path.Combine(Shot.Dir, name));
        rtb.Save(file, PngBitmapEncoderOptions.Default);
    }

    [AvaloniaTheory]
    [InlineData(SkinKind.Mac)]
    [InlineData(SkinKind.Win)]
    public void Rest_hover_and_focus(SkinKind skin)
    {
        foreach (var variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            Skin.UseTheme(ColourThemes.Default);
            ((App)Application.Current!).UseSkin(skin);
            var model = Lecture();
            model.Note!.OnRename = _ => Task.FromResult(true);
            var page = new NotePage { DataContext = model };
            var window = new Window { Width = 900, Height = 700, RequestedThemeVariant = variant, Content = page };
            Look.Apply(window);
            window.Show();
            DiagramsReady.Wait(window);
            window.UpdateLayout();
            string tag = $"{(skin == SkinKind.Mac ? "mac" : "win")}-{(variant == ThemeVariant.Dark ? "dark" : "light")}";
            var title = page.FindControl<TextBox>("Title")!;
            var over = title.TranslatePoint(new Point(title.Bounds.Width * 0.6, title.Bounds.Height / 2), window)!.Value;

            window.MouseMove(new Point(880, 680));
            Save(window, $"lecture-title-rest-{tag}.png");
            window.MouseMove(over);
            Save(window, $"lecture-title-hover-{tag}.png");
            window.MouseDown(over, MouseButton.Left);
            window.MouseUp(over, MouseButton.Left);
            window.MouseMove(new Point(880, 680));
            Assert.True(title.IsFocused);
            Save(window, $"lecture-title-focus-{tag}.png");
            window.Close();
        }
    }
}
