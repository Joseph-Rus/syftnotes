using Microsoft.Extensions.DependencyInjection;
using StudyStash.App.ViewModels;

namespace StudyStash.App.Tests;

/// <summary>The app's container, built the way the app builds it (only its settings folder is a test's own): everything
/// registered can be made, and the view models the shell makes through it have every connection their buttons need.
/// A registration nobody can make, or a factory that leaves a hook out, fails here instead of as a dead button.</summary>
public sealed class AppServicesTests
{
    [Fact]
    public void EverythingRegisteredCanBeMadeAndConnected()
    {
        using var home = new TempHome();
        var registered = AppServices.Register(new ServiceCollection(), new AppStart(home.Path, _ => { }));
        // ValidateOnBuild and ValidateScopes: a missing dependency throws here.
        using var services = registered.BuildServiceProvider(AppServices.Checked);
        foreach (var type in registered.Select(d => d.ServiceType).Distinct())
            Assert.NotNull(services.GetRequiredService(type));

        var ask = services.GetRequiredService<AskFactory>().Create();
        Assert.NotNull(ask.OpenSettings);
        Assert.NotNull(ask.ToldDeleted);

        var canvas = services.GetRequiredService<CanvasModels>();
        using var window = canvas.ForWindow(new CanvasConnectExits(() => { }, () => { }, () => { }));
        Assert.True(window.ShowFooter);
        Assert.NotNull(window.OnSkip);
        Assert.NotNull(window.OnBack);
        Assert.NotNull(window.OnFinish);
        Assert.NotNull(window.Picker.OnSaved);
        using var inSetup = canvas.ForSetup(CanvasConnectExits.InFlow, "Continue");
        Assert.False(inSetup.ShowFooter);
        Assert.Equal("Continue", inSetup.FinishLabel);
        using var sync = canvas.Sync(inSetup);
        Assert.NotNull(sync.ClassesChanged);
        using var addOn = canvas.AddOn();
    }
}
