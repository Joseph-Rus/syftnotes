using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StudyStash.App.Services;
using StudyStash.App.ViewModels;
using StudyStash.App.Views;
using StudyStash.App.Windows;

namespace StudyStash.App.Tests;

/// <summary>"Which browser do you use for Canvas?": the small window that asks when the extension checks in from two
/// browsers in one place. The question and the pick against a real library, what the window does with each answer, and
/// that a closed one is gone from memory.</summary>
public class BrowserChoiceTests
{
    [Fact]
    public async Task Two_browsers_checking_in_make_the_library_ask_and_the_pick_through_the_app_settles_it()
    {
        await using var rig = await LibraryRig.StartAsync();
        var client = rig.Client();
        const string here = "http://127.0.0.1:8787";

        var stop = TestContext.Current.CancellationToken;

        await rig.VisitAsync(address: here, browser: "Chrome", stop: stop);
        Assert.Null((await client.StateAsync(stop))!.Extension!.ChooseBrowser);

        await rig.VisitAsync(address: here, browser: "Edge", stop: stop);
        var ask = (await client.StateAsync(stop))!.Extension!.ChooseBrowser;
        Assert.NotNull(ask);
        Assert.Equal(("this_computer", "Chrome,Edge"), (ask.Where, string.Join(",", ask.Browsers)));

        // The window's own model, wired as the app wires it. A browser the library never heard from: its own words.
        string? done = "nothing yet";
        async Task<string?> Choose(string browser)
        {
            await client.ChooseBrowserAsync(ask.Where, browser, stop);
            return null;
        }
        var stranger = new BrowserChoiceModel(["Safari"], Choose) { OnDone = picked => done = picked };
        await stranger.Options[0].Pick.ExecuteAsync(null);
        Assert.Equal(("Study Stash hasn't heard from the extension in that browser.", "nothing yet"), (stranger.Problem, done));

        var model = new BrowserChoiceModel(ask.Browsers, Choose) { OnDone = picked => done = picked };
        await model.Options[1].Pick.ExecuteAsync(null);
        Assert.Equal((null, "Edge"), (model.Problem, done));
        var after = (await client.StateAsync(stop))!.Extension!;
        Assert.Null(after.ChooseBrowser);
        Assert.Equal("Edge", after.Browser);

        // Answered, it isn't asked again; Settings still offers the choice while both browsers are there, to change it.
        CanvasApi.BrowserQuestion? offered = null;
        var settings = new CanvasSettingsModel(rig.Context(client)) { OnChooseBrowser = q => offered = q };
        await settings.LoadAsync(stop);
        Assert.True(settings.CanChooseBrowser);
        Assert.Equal("Edge extension", settings.ExtensionLabel);
        settings.ChooseBrowserCommand.Execute(null);
        Assert.Equal(("this_computer", "Chrome,Edge"), (offered!.Where, string.Join(",", offered.Browsers)));
        await new BrowserChoiceModel(offered.Browsers, Choose).Options[0].Pick.ExecuteAsync(null);
        Assert.Equal("Chrome", (await client.StateAsync(stop))!.Extension!.Browser);
    }

    [Fact]
    public async Task With_one_browser_settings_offers_no_choice()
    {
        await using var rig = await LibraryRig.StartAsync();
        var stop = TestContext.Current.CancellationToken;
        await rig.VisitAsync(address: "http://127.0.0.1:8787", browser: "Chrome", stop: stop);
        var settings = new CanvasSettingsModel(rig.Context());
        await settings.LoadAsync(stop);
        Assert.False(settings.CanChooseBrowser);
    }

    [Fact]
    public async Task A_library_that_doesnt_answer_leaves_the_question_up_saying_so_and_not_now_only_closes()
    {
        string? done = "nothing yet";
        var model = new BrowserChoiceModel(["Chrome", "Edge"], _ => throw new HttpRequestException("no route")) { OnDone = picked => done = picked };
        Assert.StartsWith("The Study Stash extension is in both Chrome and Edge.", model.Lede);
        Assert.Equal("Chrome, Edge and Firefox", BrowserChoiceModel.Join(["Chrome", "Edge", "Firefox"]));

        await model.Options[0].Pick.ExecuteAsync(null);
        Assert.Equal(("Your library didn't answer. Try again in a moment.", "nothing yet"), (model.Problem, done));

        model.LaterCommand.Execute(null);
        Assert.Null(done);
    }

    static (Window Window, BrowserChoiceModel Model) Open(Func<string, Task<string?>> choose, Action<string?>? done = null)
    {
        var model = new BrowserChoiceModel(["Chrome", "Edge"], choose);
        var w = BrowserChoiceWindow.Make(model, done);
        // Everything the app puts on it before showing it (Shell.AskWhichBrowser), so what those hold is counted too.
        Look.Apply(w);
        StudyStash.App.Platform.WinChrome.Apply(w);
        StudyStash.App.Platform.AppMenu.Attach(w, () => { }, () => { });
        if (Skin.Current == SkinKind.Mac) StudyStash.App.Platform.MacTitleBar.Attach(w);
        w.Show();
        Dispatcher.UIThread.RunJobs();
        return (w, model);
    }

    /// <summary>The question's view got all the height it asked for.</summary>
    static bool Fits(Window w)
    {
        var view = w.GetVisualDescendants().OfType<Control>().First(c => c is MacBrowserChoice or WinBrowserChoice);
        return view.DesiredSize.Height > 100 && view.Bounds.Height >= view.DesiredSize.Height - 0.5;
    }

    [AvaloniaTheory]
    [InlineData(SkinKind.Mac)]
    [InlineData(SkinKind.Win)]
    public void It_opens_without_taking_the_keyboard_and_closes_on_a_pick_or_not_now(SkinKind skin)
    {
        ((App)Avalonia.Application.Current!).UseSkin(skin);
        try
        {
            var picks = new List<string?>();
            var (w, model) = Open(_ => Task.FromResult<string?>(null), picks.Add);
            Assert.False(w.ShowActivated);
            Assert.True(w.Topmost);
            // A button each and Not now, and the window as tall as what it says: nothing of it is cut off.
            Assert.Equal(3, w.GetVisualDescendants().OfType<Button>().Count(b => b.Command is not null && b.IsEffectivelyVisible));
            Assert.Equal(BrowserChoiceWindow.Wanted.Width, w.ClientSize.Width);
            Assert.True(Fits(w), "the question doesn't fit its window");

            model.Options[0].Pick.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.False(w.IsVisible);
            Assert.Equal(["Chrome"], picks);

            var (again, asked) = Open(_ => Task.FromResult<string?>(null), picks.Add);
            asked.LaterCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.False(again.IsVisible);
            Assert.Equal(["Chrome", null], picks);

            // A refusal keeps it open with the reason.
            var (refused, no) = Open(_ => Task.FromResult<string?>("Study Stash hasn't heard from the extension in that browser."), picks.Add);
            double before = refused.ClientSize.Height;
            no.Options[1].Pick.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.True(refused.IsVisible);
            Assert.True(no.HasProblem);
            Assert.True(refused.ClientSize.Height > before && Fits(refused), "the reason pushed Not now out of the window");
            refused.Close();
        }
        finally
        {
            ((App)Avalonia.Application.Current!).UseSkin(SkinKind.Mac);
        }
    }

    /// <summary>Opens one, uses it as <paramref name="how"/> says, and hands back only weak references to it.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    static WeakReference[] OpenAndClose(string how, List<TaskCompletionSource<string?>> answers)
    {
        var answer = new TaskCompletionSource<string?>();
        var (w, model) = Open(_ => answer.Task);
        var view = w.GetVisualDescendants().OfType<Control>().First(c => c is MacBrowserChoice or WinBrowserChoice);
        var button = w.GetVisualDescendants().OfType<Button>().First();
        switch (how)
        {
            case "picked":
                model.Options[0].Pick.Execute(null);
                answer.SetResult(null);
                break;
            case "not now":
                model.LaterCommand.Execute(null);
                break;
            case "closed":
                w.Close();
                break;
            case "refused, then closed":
                model.Options[0].Pick.Execute(null);
                answer.SetResult("Study Stash hasn't heard from the extension in that browser.");
                Dispatcher.UIThread.RunJobs();
                w.Close();
                break;
            case "closed while the library was still answering":
                model.Options[1].Pick.Execute(null);
                w.Close();
                answers.Add(answer); // answered after it has gone
                break;
        }
        Dispatcher.UIThread.RunJobs();
        Assert.False(w.IsVisible);
        return [new(w), new(view), new(model), new(button)];
    }

    [AvaloniaTheory]
    [InlineData(SkinKind.Mac)]
    [InlineData(SkinKind.Win)]
    public void A_closed_one_is_gone_from_memory_however_it_closed(SkinKind skin)
    {
        ((App)Avalonia.Application.Current!).UseSkin(skin);
        try
        {
            var late = new List<TaskCompletionSource<string?>>();
            var gone = new List<(string How, WeakReference[] Parts)>();
            // Each way twice over: whatever the windowing keeps of the last window it showed isn't the app holding on.
            foreach (string how in new[] { "picked", "not now", "closed", "refused, then closed", "closed while the library was still answering" })
                for (int i = 0; i < 2; i++)
                    gone.Add((how, OpenAndClose(how, late)));
            foreach (var answer in late) answer.SetResult(null);
            late.Clear();
            // One more opened and closed after them, so the last of those isn't the newest window any more.
            OpenAndClose("closed", late);
            for (int i = 0; i < 6; i++)
            {
                Dispatcher.UIThread.RunJobs();
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true);
                GC.WaitForPendingFinalizers();
            }
            string[] what = ["window", "view", "model", "button"];
            var alive = gone.SelectMany(g => g.Parts.Select((p, i) => (g.How, What: what[i], p.IsAlive))).Where(p => p.IsAlive).Select(p => $"{p.What} ({p.How})").ToList();
            Assert.True(alive.Count == 0, "still in memory after closing: " + string.Join(", ", alive));
        }
        finally
        {
            ((App)Avalonia.Application.Current!).UseSkin(SkinKind.Mac);
        }
    }

    [AvaloniaFact]
    public void Mac_and_Win_browser_choice()
    {
        foreach (var (skin, look) in new[] { (SkinKind.Mac, "mac"), (SkinKind.Win, "win") })
            foreach (var t in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            {
                Skin.UseTheme(ColourThemes.Default);
                ((App)Avalonia.Application.Current!).UseSkin(skin);
                var model = new BrowserChoiceModel(["Chrome", "Edge"], _ => Task.FromResult<string?>(null));
                var w = BrowserChoiceWindow.Make(model);
                w.RequestedThemeVariant = t;
                Look.Apply(w);
                w.Show();
                Dispatcher.UIThread.RunJobs();
                RichShots.Save($"{look}-browser-choice", t, w, w.ClientSize);
                w.Close();
            }
        ((App)Avalonia.Application.Current!).UseSkin(SkinKind.Mac);
    }
}
