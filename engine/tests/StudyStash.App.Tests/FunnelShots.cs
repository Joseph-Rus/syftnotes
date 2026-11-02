using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using StudyStash.App.Services;
using StudyStash.App.ViewModels;
using StudyStash.App.Views;

namespace StudyStash.App.Tests;

/// <summary>The first run's funnel, every screen, both looks, light and dark, drawn off screen: are you a student,
/// connect your classes (nothing to type, and the school asked while the stores have the old add-on), the sync part
/// way, done and stopped, connect your AI and the one more push to Gemini. Set STUDYSTASH_SHOTS to look at them.</summary>
public sealed class FunnelShots
{
    static readonly ThemeVariant[] Themes = [ThemeVariant.Light, ThemeVariant.Dark];
    static readonly SkinKind[] Looks = [SkinKind.Mac, SkinKind.Win];
    static readonly Size Room = new(748, 788);

    static Control View(SkinKind skin, FunnelModel f) =>
        skin == SkinKind.Mac ? new MacFunnel { DataContext = f, DrawChrome = true } : new WinFunnel { DataContext = f, DrawChrome = true };

    static FunnelModel At(SkinKind skin, FunnelStep step, FunnelServices? services = null, bool canvas = true)
    {
        var setup = SetupModel.For(skin);
        setup.OneButton = true;
        var f = new FunnelModel(setup, services ?? new FunnelServices()) { WithCanvas = canvas };
        f.Step = step;
        return f;
    }

    static void Each(string name, Func<SkinKind, FunnelModel> make)
    {
        foreach (var skin in Looks)
            foreach (var t in Themes)
                Shot.Take($"funnel-{(skin == SkinKind.Mac ? "mac" : "win")}-{name}", skin, t, () => View(skin, make(skin)), size: Room);
    }

    [AvaloniaFact]
    public void Every_screen()
    {
        Each("1-welcome", s => At(s, FunnelStep.Welcome));
        Each("2-student", s => At(s, FunnelStep.Student));
        Each("3-classes", s =>
        {
            var context = CanvasFixtures.Context(new FakeLibrary(), browsers: [Browsers.Chrome, Browsers.Firefox]);
            var addOn = new AddOnModel(context, new CanvasWatch(context)) { Added = true };
            var f = At(s, FunnelStep.Student, new FunnelServices { MakeAddOn = () => addOn });
            f.Step = FunnelStep.Classes;
            return f;
        });
        Each("3-classes-found", s =>
        {
            var context = CanvasFixtures.Context(new FakeLibrary());
            var addOn = new AddOnModel(context, new CanvasWatch(context)) { Added = true, Connected = true };
            var f = At(s, FunnelStep.Student, new FunnelServices { MakeAddOn = () => addOn });
            f.Step = FunnelStep.Classes;
            return f;
        });
        Each("4-sync", s => Synced(s, sync =>
        {
            sync.Find.State = StageState.Done;
            sync.Find.Detail = "6 found";
            sync.Due.State = StageState.Working;
            sync.Due.Detail = "3 of 6";
            sync.Due.Fraction = 0.5;
        }));
        Each("4-sync-done", s => Synced(s, sync =>
        {
            foreach (var st in sync.Stages) st.State = StageState.Done;
            sync.Find.Detail = "6 found";
            sync.Due.Detail = sync.Files.Detail = "6 of 6";
            sync.DoneLine = "Done. Your classes are in.";
            sync.Finished = true;
        }));
        Each("4-sync-problem", s => Synced(s, sync =>
        {
            sync.Find.State = StageState.Done;
            sync.Find.Detail = "6 found";
            sync.Due.State = StageState.Failed;
            sync.Problem = "Canvas didn't send your assignments and due dates. It may be busy: try again in a moment.";
        }));
        Each("5-ai", s => At(s, FunnelStep.Ai));
        Each("5-ai-again", s =>
        {
            var f = At(s, FunnelStep.Ai, canvas: false);
            f.SkipCommand.Execute(null);
            return f;
        });
    }

    /// <summary>The sync screen, its lines as <paramref name="state"/> leaves them.</summary>
    static FunnelModel Synced(SkinKind skin, Action<FirstSyncModel> state)
    {
        var context = CanvasFixtures.Context(new FakeLibrary());
        var watch = new CanvasWatch(context);
        var connect = new CanvasConnectModel(context, watch, forSetup: true);
        var f = At(skin, FunnelStep.Classes);
        f.Canvas = connect;
        var sync = new FirstSyncModel(connect, context, watch);
        state(sync);
        f.Sync = sync;
        f.Step = FunnelStep.Sync;
        return f;
    }
}
