using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StudyStash.App.Services;
using StudyStash.App.ViewModels;
using StudyStash.App.Views;
using StudyStash.Audio;
using StudyStash.Core;
using StudyStash.Core.Ai;
using StudyStash.Core.Setup;
using StudyStash.Core.Tests;
using static StudyStash.App.Tests.GuidedSetupTests;

namespace StudyStash.App.Tests;

/// <summary>
/// A first launch set up by the AI from start to finish, in the real window's views, with the mouse: Pick your AI,
/// Install (a fake installer), Sign in (a fake sign-in), then a scripted chat whose setup tools are called the way the
/// CLI calls them. The computer card makes the library, the microphone card hears a sound file (as
/// STUDYSTASH_MIC_FILE plays one), the model card starts a download that never leaves the test, classes go in, start
/// at login is turned on, and the finish card finishes. Setup run again then goes straight back to the chat.
/// </summary>
public sealed class GuidedSetupEndToEndTests
{
    static void Click(Window w, Control c)
    {
        Dispatcher.UIThread.RunJobs();
        var at = c.TranslatePoint(new Point(c.Bounds.Width / 2, c.Bounds.Height / 2), w) ?? throw new InvalidOperationException($"{c.Name} isn't on screen");
        w.MouseDown(at, MouseButton.Left);
        w.MouseUp(at, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    static Button Named(Window w, string name) =>
        w.GetVisualDescendants().OfType<Button>().First(b => b.Name == name && b.IsEffectivelyVisible);

    /// <summary>A card's own button, found by what it does ("setup", "download"…).</summary>
    static Button CardButton(Window w, CardEntry card, string action) =>
        w.GetVisualDescendants().OfType<Button>().First(b => b.DataContext == card && Equals(b.CommandParameter, action) && b.IsEffectivelyVisible);

    [AvaloniaFact]
    public async Task A_first_launch_is_set_up_by_the_ai_and_run_again_opens_the_chat()
    {
        string wav = Path.Combine(AppContext.BaseDirectory, "Fixtures", "speech.wav");
        await using var rig = await new Rig(mic: () => new FileMicrophone(wav, speed: 4)).OpenAsync();
        var g = rig.Guided;
        var skin = OperatingSystem.IsWindows() ? SkinKind.Win : SkinKind.Mac;
        ((App)Application.Current!).UseSkin(skin);
        Control view = skin == SkinKind.Mac ? new MacGuidedSetup { DataContext = g } : new WinGuidedSetup { DataContext = g };
        var w = new Window { Width = 1000, Height = 700, Content = view };
        w.Show();
        FakeAgents.Replay(rig.Bin, ["""{"type":"system","subtype":"init","session_id":"p-1","tools":[],"mcp_servers":[]}""", Says("ready"), Done("ready", "p-1")], 1);
        FakeAgents.Replay(rig.Bin, [Init(), Says("Hi! This takes about 5 minutes. Just this computer, or two?"), Done("Hi!")], 2);

        // Pick your AI → Install → Sign in, all with the mouse.
        Click(w, Named(w, "PickClaude"));
        Click(w, Named(w, "Continue"));
        await Until(() => g.Screen == GuidedScreen.Install, "Install");
        Click(w, Named(w, "Install"));
        await Until(() => g.Installed, "the install");
        Click(w, Named(w, "Continue"));
        await Until(() => g.Screen == GuidedScreen.SignIn, "Sign in");
        Click(w, Named(w, "OpenSignIn"));
        await Until(() => g.AiReady, "the sign-in and the plan check");
        Click(w, Named(w, "Continue"));
        await Until(() => g.Screen == GuidedScreen.Chat && !g.Busy && g.Thread.OfType<AiEntry>().Any(), "the chat's first turn");
        Assert.Equal(900, view.Width);

        // The computer card: Just this computer makes the library, quietly.
        Assert.False((await rig.CallAsync("offer_computer_setup")).Error);
        var computer = g.Thread.OfType<CardEntry>().Last();
        Click(w, CardButton(w, computer, "setup"));
        await Until(() => rig.Setup.LibraryOk && !computer.Busy, "the library");
        Assert.Equal(ChecklistState.Done, rig.Item("computer").State);

        // The microphone card: the microphone opens for it, and the app's own tick hears the sound file.
        await Until(() => !g.Busy, "the note's turn");
        Assert.False((await rig.CallAsync("offer_microphone_check")).Error);
        Assert.True(rig.Setup.MicCheckOpen);
        for (int i = 0; i < 200 && !rig.Setup.MicHeard; i++)
        {
            Services.Setup.Refresh(rig.Setup, rig.Host);
            Services.Setup.TickMic(rig.Setup, rig.Host, rig.Mic);
            await Task.Delay(25, TestContext.Current.CancellationToken);
        }
        Assert.True(rig.Setup.MicHeard);
        Assert.Equal(ChecklistState.Done, rig.Item("microphone").State);
        Services.Setup.TickMic(rig.Setup, rig.Host, rig.Mic);
        Assert.False(rig.Mic.IsOpen);

        // The model card: Download starts it (from the test's own mirror, never the internet).
        await Until(() => !g.Busy, "the note's turn");
        Assert.False((await rig.CallAsync("offer_model_download", new() { ["model_id"] = "large-v3-turbo-q5" })).Error);
        var model = g.Thread.OfType<CardEntry>().Last();
        Click(w, CardButton(w, model, "download"));
        await Until(() => rig.Models.Asked > 0, "the download");
        Assert.Equal(ChecklistState.Now, rig.Item("model").State);

        // Classes, then start at login, then finish.
        await Until(() => !g.Busy, "the note's turn");
        Assert.False((await rig.CallAsync("add_class", new() { ["name"] = "BIO 110", ["about"] = "Cells, genetics and evolution" })).Error);
        Assert.False((await rig.CallAsync("add_class", new() { ["name"] = "CS 101" })).Error);
        Assert.Equal("2 classes", rig.Item("classes").Detail);
        Assert.False((await rig.CallAsync("offer_start_at_login")).Error);
        Click(w, CardButton(w, g.Thread.OfType<CardEntry>().Last(), "on"));
        Assert.Equal([true], rig.Login.Calls);
        await Until(() => !g.Busy, "the note's turn");
        Assert.True(g.ReadyToFinish, string.Join(", ", g.Checklist.Select(r => $"{r.Id}:{r.State}")));
        bool finished = false;
        g.OnFinish = () =>
        {
            Services.Setup.Finish(rig.Setup, rig.Host);
            finished = true;
        };
        Assert.False((await rig.CallAsync("offer_finish")).Error);
        Click(w, CardButton(w, g.Thread.OfType<CardEntry>().Last(), "finish"));
        Assert.True(finished);
        var saved = AppSettings.Load(rig.Home.Path);
        Assert.True(saved.SetupDone);
        Assert.Equal(AppRole.Both, saved.Role);
        Assert.Equal("claude", saved.SetupAi);
        Assert.Equal("s-1", saved.SetupChat?.Session);
        w.Close();

        // Settings → General → Run setup: straight back to the chat, the conversation carried on.
        int turns = FakeAgents.Turns(rig.Bin);
        var again = Services.Setup.Make(rig.Host);
        again.Again = true;
        var services = new GuidedServices { Home = rig.Home.Path, Find = c => AgentInstall.Find(c, FakeAgents.Which(rig.Bin)) };
        using var reopened = new GuidedSetupModel(again, services, () => rig.Host.Settings, rig.Host.Save)
        {
            Door = new SetupDoor(rig.Door.Url, rig.Door.Token, rig.Tools.NewTurn),
        };
        await reopened.OpenAsync();
        Assert.Equal(GuidedScreen.Chat, reopened.Screen);
        await Until(() => FakeAgents.Turns(rig.Bin) > turns && !reopened.Busy, "the chat again");
        string[] argv = FakeAgents.Argv(rig.Bin, turns + 1);
        Assert.Equal("s-1", argv[Array.IndexOf(argv, "--resume") + 1]);
        Assert.Contains("[Study Stash] Setup was opened again from Settings.", FakeAgents.Input(rig.Bin, turns + 1));
        Assert.Equal("Set up Study Stash again", reopened.HeaderTitle);
        Assert.Equal(ChecklistState.Done, reopened.Checklist.Single(r => r.Id == "computer").State);
    }
}
