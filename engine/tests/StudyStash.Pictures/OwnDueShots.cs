using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using StudyStash.App.Controls;
using StudyStash.App.Services;
using StudyStash.App.ViewModels;
using StudyStash.App.Views;

namespace StudyStash.App.Pictures;

/// <summary>The student's own items on Due (shots/due-own-items-*.png): the calendar's month and week with two of them (an exam and a
/// study session, each with its small kind word), the Add form open, and one of them open with Mark done and Delete. Both looks,
/// light and dark. Drawn off screen with the design's sample, never on the student's screen; only with STUDYSTASH_FILM=1.</summary>
public class OwnDueShots
{
    static readonly SkinKind[] Skins = [SkinKind.Mac, SkinKind.Win];
    static readonly ThemeVariant[] Themes = [ThemeVariant.Light, ThemeVariant.Dark];
    static readonly Size Page = new(1408, 1128);
    static readonly TimeZoneInfo DemoZone = TimeZoneInfo.CreateCustomTimeZone("Demo", TimeSpan.FromHours(-4), "Demo", "Demo");

    static bool On => Environment.GetEnvironmentVariable("STUDYSTASH_FILM") == "1";
    static string Name(string what, SkinKind skin, ThemeVariant t) =>
        $"due-own-items-{what}-{(skin == SkinKind.Mac ? "mac" : "win")}-{(t == ThemeVariant.Dark ? "dark" : "light")}";

    static Control Library(SkinKind skin, LibraryModel m) =>
        skin == SkinKind.Mac ? new MacLibrary { DataContext = m, Width = 1280, Height = 1000 } : new WinLibrary { DataContext = m, Width = 1280, Height = 1000 };

    [AvaloniaFact]
    public void The_calendar_with_an_exam_and_a_study_session_of_the_students_own_in_the_month_and_the_week()
    {
        if (!On) return;
        foreach (var skin in Skins)
            foreach (var t in Themes)
            {
                Shot.Take($"due-own-items-month-{(skin == SkinKind.Mac ? "mac" : "win")}", skin, t, () => Library(skin, Demo.Semester(Demo.Page.Due, own: true)), size: Page);
                Shot.Take($"due-own-items-week-{(skin == SkinKind.Mac ? "mac" : "win")}", skin, t, () => Library(skin, Demo.Semester(Demo.Page.DueWeek, own: true)), size: Page);
            }
    }

    [AvaloniaFact]
    public async Task The_Add_form_and_an_item_of_the_students_own_open_with_Mark_done_and_Delete()
    {
        if (!On) return;
        foreach (var skin in Skins)
            foreach (var t in Themes)
            {
                await TakeAsync("add", skin, t, form => form.Add(new DateTime(2026, 10, 15)), typed: form =>
                {
                    form.Title = "Statistics final";
                    form.Day = new DateTime(2026, 10, 15);
                });
                await TakeAsync("edit", skin, t, form => form.Edit(new CanvasApi.Item
                {
                    Class = "Data Mining", Id = "o1", Name = "Data Mining midterm", Kind = "mine", What = "exam", Status = "open",
                    DueAt = new DateTimeOffset(2026, 10, 14, 9, 0, 0, TimeSpan.FromHours(-4)),
                }, DemoZone));
            }
    }

    /// <summary>The Due page with its form opened the way the app opens it (the model says so, and the calendar puts it in the window's
    /// dialog layer), drawn once the veil and the form have finished coming in.</summary>
    static async Task TakeAsync(string what, SkinKind skin, ThemeVariant variant, Action<ViewModels.Kit.OwnDueFormModel> open, Action<ViewModels.Kit.OwnDueFormModel>? typed = null)
    {
        Skin.UseTheme(ColourThemes.Default);
        ((App)Application.Current!).UseSkin(skin);
        var library = Demo.Semester(Demo.Page.Due, own: true);
        var calendar = ((OverviewModel)library.Overview!).Calendar!;
        var window = new Window { Width = 1280, Height = 1000, RequestedThemeVariant = variant };
        window.Content = ConfirmLayer.Wrap(window, Library(skin, library));
        Look.Apply(window);
        window.Show();
        open(calendar.Own);
        await Wait.UntilAsync(() => window.GetVisualDescendants().OfType<OwnDueForm>().FirstOrDefault() is { IsEffectivelyVisible: true }, "the form to show");
        typed?.Invoke(calendar.Own);
        var form = window.GetVisualDescendants().OfType<OwnDueForm>().Single();
        var cover = ConfirmLayer.Pick()!.Host.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Rectangle>().Single(r => r.Name == "PART_ContentCover");
        await Wait.UntilAsync(() =>
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            return cover.Opacity >= 1 && form.GetVisualAncestors().All(a => a.Opacity >= 1);
        }, "the form to finish opening");
        DiagramsReady.Wait(window);
        var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("nothing rendered");
        using (var file = File.Create(Path.Combine(Shot.Dir, Name(what, skin, variant) + ".png"))) frame.Save(file, PngBitmapEncoderOptions.Default);
        calendar.Own.Close();
        await Wait.UntilAsync(() => !ConfirmLayer.Pick()!.Host.IsOpen, "the form to close");
        window.Close();
    }
}
