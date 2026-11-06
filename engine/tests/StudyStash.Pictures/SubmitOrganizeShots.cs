using System.Text.Json.Nodes;
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
using StudyStash.App.ViewModels;
using StudyStash.App.Views;
using StudyStash.Core;

namespace StudyStash.App.Pictures;

/// <summary>
/// Submit &amp; Organize, part 2 (test builds only), as a student would see it, light and dark, both looks: Settings →
/// Canvas's "After you hand in" off, then on with folders and Filed work (and after an Undo), the checklist over the
/// library after a hand-in (the file found for sure, not sure, and in Clean), and the notification after filing
/// (shots/submit-organize-*.png). A stand-in library with made-up folders; drawn off-screen, never on the student's screen,
/// and no folder on this computer is read.
/// </summary>
public class SubmitOrganizeShots
{
    static readonly ThemeVariant[] Themes = [ThemeVariant.Light, ThemeVariant.Dark];

    static string Tail(SkinKind skin, ThemeVariant variant) => $"{(skin == SkinKind.Mac ? "mac" : "win")}-{(variant == ThemeVariant.Dark ? "dark" : "light")}";

    /// <summary>A home folder that looks like the student's on each system: "/Users/sam" or "C:\Users\sam".</summary>
    static string P(SkinKind skin, string rest) => skin == SkinKind.Mac ? "/Users/sam/" + rest : @"C:\Users\sam\" + rest.Replace('/', '\\');

    static void Save(Window window, string name)
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        DiagramsReady.Wait(window);
        var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("nothing rendered");
        using var file = File.Create(Path.Combine(Shot.Dir, name + ".png"));
        frame.Save(file, PngBitmapEncoderOptions.Default);
    }

    static string FilingJson(SkinKind skin, bool on) => new JsonObject
    {
        ["on"] = on,
        ["folders"] = on ? new JsonArray(P(skin, "Downloads"), P(skin, "Documents/School")) : new JsonArray(),
        ["suggested"] = new JsonArray([.. (on ? new[] { "Desktop", "Documents" } : ["Downloads", "Desktop", "Documents"])
            .Select(n => (JsonNode)new JsonObject { ["name"] = n, ["path"] = P(skin, n) })]),
        ["when"] = "ask",
        ["mode"] = "organize",
        ["waiting"] = new JsonArray(),
        ["runs"] = on ? new JsonArray(
            new JsonObject { ["run"] = "r2", ["at"] = "2025-09-25T16:40:00Z", ["class"] = "CS 101", ["assignment"] = "Lab 3: Recursion traces", ["files"] = 3, ["undone"] = 0 },
            new JsonObject { ["run"] = "r1", ["at"] = "2025-09-22T20:05:00Z", ["class"] = "BIO 110", ["assignment"] = "Essay 2: Cell signalling", ["files"] = 1, ["undone"] = 0 }) : new JsonArray(),
    }.ToJsonString();

    static async Task SettingsAsync(SkinKind skin, ThemeVariant variant, bool on, bool undo)
    {
        ((App)Application.Current!).UseSkin(skin);
        using var home = new TempHome();
        new AppSettings { SetupDone = true, Role = AppRole.Laptop }.Save(home.Path);
        var cc = Configs.LoadClient(home.Path);
        cc.ServerUrl = "http://library.example:9";
        cc.PoolKey = "pw";
        cc.PoolName = "Sam's library";
        Configs.SaveClient(cc);
        using var host = new AppHost(home.Path, log: _ => { });
        var fake = new FakeLibrarySettings();
        var canvas = Scenes.ConnectedLibrary()
            .Json(HttpMethod.Get, "/api/v2/canvas/filing", FilingJson(skin, on))
            .Json(HttpMethod.Post, "/api/v2/canvas/filing/undo", """{"items": [{"from": "a", "to": "a", "how": "back"}, {"from": "b", "to": "b", "how": "back"}, {"from": "c", "to": null, "how": "in_trash"}]}""");
        using var model = SettingsModel.Make(host, canvas: CanvasFixtures.Context(canvas), library: () => fake.Call);
        var window = new Window { Width = 1000, Height = 900, RequestedThemeVariant = variant };
        window.Content = ConfirmLayer.Wrap(window, new SettingsView { DataContext = model, DrawChrome = true });
        Look.Apply(window);
        window.Show();
        model.Section = "Canvas";
        var filing = model.Canvas.Filing!;
        await Wait.UntilAsync(() => !model.Canvas.Busy && model.Canvas.School.Length > 0 && !filing.Saving.Busy && filing.HasRuns == on, "Canvas and its hand-in part to load");
        if (undo)
        {
            await filing.Runs[0].Undo.Command.ExecuteAsync(null);
            Assert.NotNull(filing.Runs[0].Said);
        }
        // The part is at the foot of Canvas: scroll it into view, as the student would.
        Dispatcher.UIThread.RunJobs();
        var part = window.GetVisualDescendants().OfType<SubmitOrganizeSettings>().First(v => v.IsEffectivelyVisible);
        part.BringIntoView();
        Save(window, $"submit-organize-settings-{(undo ? "after-undo" : on ? "on" : "off")}-{Tail(skin, variant)}");
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData(SkinKind.Mac)]
    [InlineData(SkinKind.Win)]
    public async Task Settings_after_you_hand_in(SkinKind skin)
    {
        try
        {
            foreach (var t in Themes)
            {
                await SettingsAsync(skin, t, on: false, undo: false);
                await SettingsAsync(skin, t, on: true, undo: false);
                await SettingsAsync(skin, t, on: true, undo: true);
            }
        }
        finally
        {
            ((App)Application.Current!).UseSkin(SkinKind.Mac);
        }
    }

    static string CheckJson(SkinKind skin, string mode, string found) => new JsonObject
    {
        ["class"] = "CS 101", ["key"] = "3:1:2025-09-25T16:38:00Z", ["assignment"] = "Lab 3: Recursion traces", ["mode"] = mode,
        ["folders"] = new JsonArray(P(skin, "Downloads"), P(skin, "Documents/School")),
        ["file"] = new JsonObject { ["name"] = "lab3-traces.pdf", ["path"] = P(skin, "Documents/School/CS 101/lab3-traces.pdf"), ["found"] = found },
        ["materials"] = new JsonArray(
            new JsonObject { ["path"] = P(skin, "Downloads/lab3-handout.pdf"), ["why"] = "Your professor attached a file of this name to the assignment", ["sure"] = "high" },
            new JsonObject { ["path"] = P(skin, "Documents/School/CS 101/recursion traces draft.docx"), ["why"] = "Its name shares words with the assignment, and you changed it while working on it", ["sure"] = "high" },
            new JsonObject { ["path"] = P(skin, "Downloads/call stack diagram.png"), ["why"] = "It's in the assignment's module on Canvas", ["sure"] = "medium" },
            new JsonObject { ["path"] = P(skin, "Downloads/grocery list.txt"), ["why"] = "You changed it while working on the assignment", ["sure"] = "low" }),
    }.ToJsonString();

    static async Task ChecklistAsync(SkinKind skin, ThemeVariant variant, string mode, string found, string name)
    {
        ((App)Application.Current!).UseSkin(skin);
        var library = Demo.Semester();
        Control page = skin == SkinKind.Mac ? new MacLibrary { DataContext = library } : new WinLibrary { DataContext = library };
        var window = new Window { Width = 1280, Height = 800, RequestedThemeVariant = variant };
        window.Content = ConfirmLayer.Wrap(window, page);
        Look.Apply(window);
        window.Show();
        var fake = new FakeLibrary().Json(HttpMethod.Get, "/api/v2/canvas/filing/check", CheckJson(skin, mode, found));
        var model = new HandInModel(CanvasFixtures.Context(fake),
            new CanvasApi.HandInWaiting { Class = "CS 101", Key = "3:1:2025-09-25T16:38:00Z", Assignment = "Lab 3: Recursion traces" },
            new HandInServices((_, _) => Task.FromResult<string?>(null), _ => { }));
        var showing = HandInSheet.ShowAsync(ConfirmLayer.For(window)!, model);
        await Wait.UntilAsync(() => model.Loaded && window.GetVisualDescendants().OfType<HandInSheet>().FirstOrDefault() is { IsEffectivelyVisible: true } s
            && s.FindControl<Button>("NotNow")!.IsFocused, "the checklist to show");
        var sheet = window.GetVisualDescendants().OfType<HandInSheet>().Single();
        var cover = ConfirmLayer.For(window)!.Host.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Rectangle>().Single(r => r.Name == "PART_ContentCover");
        await Wait.UntilAsync(() =>
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            return cover.Opacity >= 1 && sheet.GetVisualAncestors().All(a => a.Opacity >= 1);
        }, "the checklist to finish opening");
        Save(window, $"submit-organize-checklist-{name}-{Tail(skin, variant)}");
        Assert.Single(fake.Requests); // only the look: nothing filed
        model.NotNowCommand.Execute(null);
        await showing;
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData(SkinKind.Mac)]
    [InlineData(SkinKind.Win)]
    public async Task The_checklist_after_a_hand_in(SkinKind skin)
    {
        try
        {
            foreach (var t in Themes)
            {
                await ChecklistAsync(skin, t, "organize", "exact", "exact");
                await ChecklistAsync(skin, t, "organize", "unsure", "unsure");
                await ChecklistAsync(skin, t, "clean", "exact", "clean");
                await ChecklistAsync(skin, t, "organize", "none", "not-found");
            }
        }
        finally
        {
            ((App)Application.Current!).UseSkin(SkinKind.Mac);
        }
    }

    [AvaloniaFact]
    public void The_notification_after_filing()
    {
        foreach (var skin in new[] { SkinKind.Mac, SkinKind.Win })
            foreach (var t in Themes)
            {
                Shot.Take($"submit-organize-toast-{(skin == SkinKind.Mac ? "mac" : "win")}", skin, t,
                    () => new ToastView { Title = FilingWords.FiledTitle("CS 101", "Lab 3: Recursion traces"), Text = FilingWords.FiledText(3, 0), ActionLabel = FilingWords.Undo, Width = 380 },
                    size: new Size(540, 260));
                Shot.Take($"submit-organize-toast-undone-{(skin == SkinKind.Mac ? "mac" : "win")}", skin, t,
                    () => new ToastView { Title = FilingWords.PutBackTitle, Text = FilingWords.Undone(["back", "back", "beside"]), Width = 380 },
                    size: new Size(540, 260));
            }
        ((App)Application.Current!).UseSkin(SkinKind.Mac);
    }
}
