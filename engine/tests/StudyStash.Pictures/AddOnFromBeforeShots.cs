using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using StudyStash.App.Services;
using StudyStash.App.ViewModels;
using StudyStash.App.Views;

namespace StudyStash.App.Pictures;

/// <summary>
/// Setup's Canvas step when an add-on from an earlier install still knocks with an old key, light and dark, both looks
/// (shots/addon-from-before-&lt;paste|school&gt;-&lt;mac|win&gt;-&lt;light|dark&gt;.png): the code to paste in its button
/// with "Not in Firefox? Add it:" and the store's steps under it (a library that knows the Canvas address), and the
/// school step after Add to Firefox (a fresh library), its code line saying to choose Paste a new code. A pretend
/// library; drawn off-screen.
/// </summary>
public class AddOnFromBeforeShots
{
    static readonly ThemeVariant[] Themes = [ThemeVariant.Light, ThemeVariant.Dark];
    static readonly DateTimeOffset Added = DateTimeOffset.Parse("2026-11-02T09:00:00Z");

    static FunnelModel Funnel(SkinKind skin, FunnelServices services)
    {
        var setup = SetupModel.For(skin);
        setup.OneButton = true;
        setup.OnConnect = () =>
        {
            setup.LibraryOk = true;
            return Task.CompletedTask;
        };
        setup.OnGetStarted = () => Task.CompletedTask;
        return new FunnelModel(setup, services);
    }

    static void Take(string name, SkinKind skin, FunnelModel f)
    {
        foreach (var t in Themes)
            Shot.Take($"addon-from-before-{name}-{(skin == SkinKind.Mac ? "mac" : "win")}", skin, t,
                () => skin == SkinKind.Mac ? new MacFunnel { DataContext = f, DrawChrome = true } : new WinFunnel { DataContext = f, DrawChrome = true },
                size: new Size(748, 788));
    }

    [AvaloniaTheory]
    [InlineData(SkinKind.Mac)]
    [InlineData(SkinKind.Win)]
    public async Task The_code_to_paste_with_the_store_s_steps_under_it(SkinKind skin)
    {
        using var home = new TempHome();
        var handler = new FakeLibrary()
            .Json(HttpMethod.Post, "/api/v2/canvas/pair", """{"until":"2026-11-02T09:05:00Z"}""")
            .Json(HttpMethod.Get, "/api/v2/canvas/extension", "extension")
            .Json(HttpMethod.Get, "/api/v2/canvas/state", """{"state": "no_extension", "url": "https://school.instructure.com", "extension": {"connected": false, "refused_at": "2026-11-02T08:59:00Z"}}""");
        var context = CanvasFixtures.Context(handler, browsers: [Services.Browsers.Firefox]);
        var watch = new CanvasWatch(context) { Extension = new ExtensionKeeper(home.Path, () => context.Client) };
        using var f = Funnel(skin, new FunnelServices { MakeAddOn = () => new AddOnModel(context, watch, () => Added) });
        await f.GetStartedCommand.ExecuteAsync(null);
        await f.IAmAStudentCommand.ExecuteAsync(null);
        await f.AddOn!.AddCommand.ExecuteAsync(null);
        handler.Json(HttpMethod.Get, "/api/v2/canvas/state", """{"state": "no_extension", "url": "https://school.instructure.com", "extension": {"connected": false, "refused_at": "2026-11-02T09:00:20Z"}}""");
        await watch.RefreshAsync();
        Assert.True(f.AddOn.ShowPaste);
        Take("paste", skin, f);
    }

    [AvaloniaTheory]
    [InlineData(SkinKind.Mac)]
    [InlineData(SkinKind.Win)]
    public async Task The_school_step_after_Add_says_to_choose_Paste_a_new_code(SkinKind skin)
    {
        var handler = new FakeLibrary()
            .Json(HttpMethod.Post, "/api/v2/canvas/pair", """{"until":"2026-11-02T09:05:00Z"}""")
            .Json(HttpMethod.Post, "/api/v2/canvas", "canvas")
            .Json(HttpMethod.Get, "/api/v2/canvas/classes", "[]")
            .Json(HttpMethod.Get, "/api/v2/canvas/extension", "extension")
            .Json(HttpMethod.Get, "/api/v2/canvas/state", """{"state": "not_set_up", "url": "", "extension": {"connected": false, "refused_at": "2026-11-02T09:00:20Z"}}""");
        var context = CanvasFixtures.Context(handler, browsers: [Services.Browsers.Firefox]);
        var watch = new CanvasWatch(context);
        using var f = Funnel(skin, new FunnelServices
        {
            MakeAddOn = () => new AddOnModel(context, watch, () => Added),
            MakeCanvas = async () =>
            {
                var c = new CanvasConnectModel(context, watch, forSetup: true);
                await c.StartAsync(CanvasFixtures.Load<CanvasApi.State>("state-not-set-up"), []);
                return c;
            },
        });
        await f.GetStartedCommand.ExecuteAsync(null);
        await f.IAmAStudentCommand.ExecuteAsync(null);
        await f.AddOn!.AddCommand.ExecuteAsync(null);
        await watch.RefreshAsync();
        await Wait.UntilAsync(() => f.AsksSchool, "the step that asks for the school's address");
        f.Canvas!.SchoolField = "school.instructure.com";
        handler.Json(HttpMethod.Get, "/api/v2/canvas/state", """{"state": "no_extension", "url": "https://school.instructure.com", "extension": {"connected": false, "refused_at": "2026-11-02T09:00:50Z"}}""");
        await f.Canvas.ContinueCommand.ExecuteAsync(null);
        await f.Canvas.AddToBrowserCommand.ExecuteAsync(null);
        Assert.True(f.Canvas.ShowCode && f.Canvas.ShowAddOnLink);
        Take("school", skin, f);
    }
}
