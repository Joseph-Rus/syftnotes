using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using StudyStash.App.Services;
using StudyStash.App.Services.Stores;
using StudyStash.App.ViewModels;
using StudyStash.App.ViewModels.Kit;
using StudyStash.App.Windows;
using StudyStash.Core;

namespace StudyStash.App;

/// <summary>Where the app starts from: its settings folder (~/.study-stash, or --home) and where its log lines go.</summary>
public sealed record AppStart(string Home, Action<string> Log);

/// <summary>
/// The app's composition root: every long-lived piece is registered here once, and the container made from it is
/// built (and checked) once as the app starts. <see cref="Shell"/> resolves what it needs from it instead of making
/// it. A view model the shell builds at runtime comes from a factory registered here, which hands it its connections
/// in its constructor. AppServicesTests builds this same container and resolves every registration, so a missing
/// connection fails a test, not a button.
/// </summary>
public static class AppServices
{
    /// <summary>Checked when built: anything registered that can't be made fails then, not on first use.</summary>
    public static readonly ServiceProviderOptions Checked = new() { ValidateOnBuild = true, ValidateScopes = true };

    /// <summary>The app's container, built once by <see cref="Shell.Start"/>. Disposing it stops the engine (it owns
    /// the <see cref="AppHost"/>).</summary>
    public static ServiceProvider Build(AppStart start) => Register(new ServiceCollection(), start).BuildServiceProvider(Checked);

    /// <summary>Everything the app registers, onto <paramref name="services"/>.</summary>
    public static IServiceCollection Register(IServiceCollection services, AppStart start)
    {
        services.AddSingleton(start);
        services.AddSingleton(TimeProvider.System);
        // The one messenger services and view models send on, and Shell listens to (Messages.cs).
        services.AddSingleton<IMessenger>(WeakReferenceMessenger.Default);
        services.AddSingleton<ToolkitHooks>();
        // The one "Are you sure?": view models take IConfirm, so a test answers it without a window.
        services.AddSingleton<IConfirm>(Confirm.Default);

        // The engine room, and the long-lived pieces it already owns: registered as they are, so whoever needs one
        // takes it in its constructor instead of reaching through the host.
        services.AddSingleton(sp => new AppHost(start.Home, laptop: new LaptopHost(), log: start.Log, clock: sp.GetRequiredService<TimeProvider>()));
        services.AddSingleton(sp => sp.GetRequiredService<AppHost>().Api);
        services.AddSingleton<AiStatusStore>(sp => sp.GetRequiredService<AppHost>().AiStatus);
        services.AddSingleton<CanvasLink>();

        // View models made at runtime, with their connections.
        services.AddSingleton<AskFactory>();
        services.AddSingleton<CanvasModels>();
        return services;
    }
}

/// <summary>
/// The toolkit's app-wide pieces that are static (<see cref="UiTasks"/>, <see cref="AppWindows"/>), connected once
/// from the container as the app starts: their log lines go to the app's log, and what they ask of the app (say
/// something went wrong, open the library or Settings, follow the Dock) goes out as messages Shell listens for.
/// </summary>
public sealed class ToolkitHooks(AppStart start, IMessenger messages)
{
    public void Connect()
    {
        UiTasks.Use(start.Log, () => messages.Send(new SomethingWentWrong()));
        AppWindows.Use(() => messages.Send(new LibraryRequested()), () => messages.Send(new SettingsRequested()), () => messages.Send(new WindowsChanged()));
    }
}
