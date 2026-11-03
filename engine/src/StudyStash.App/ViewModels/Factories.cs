using CommunityToolkit.Mvvm.Messaging;
using StudyStash.App.Services;

namespace StudyStash.App.ViewModels;

/// <summary>
/// Makes the app's ask bars (the recorder's chat, a lecture's, Home's and a class's) over the library's one AI client,
/// each with its links already connected: "Change defaults" opens Settings → Your AI, and a deleted chat says so with
/// Undo. Where the bar sits (its lecture, class, scopes, sources) is the caller's to set.
/// </summary>
public sealed class AskFactory(LibraryClients api, IMessenger messages)
{
    readonly AskServices services = new(
        OpenSettings: () => messages.Send(new SettingsRequested("AI")),
        ToldDeleted: (title, undo) => messages.Send(new ToastRequested("Chat deleted", title, "Undo", () => UiTasks.Run(undo, "undo"))));

    public AiAskModel Create() => new(api.Ai, services);
}

/// <summary>
/// Makes Connect Canvas, the first run's sync and its add-on step over the app's one Canvas context and watch
/// (<see cref="CanvasLink"/>), so none is ever made over a copy of its own. Each takes what its buttons need when it's
/// made: a missing connection fails there, not as a button that does nothing.
/// </summary>
public sealed class CanvasModels(CanvasLink canvas, AppHost host, IMessenger messages)
{
    /// <summary>Connect Canvas in a window of its own: its footer shows, with <paramref name="exits"/>' buttons, and
    /// courses brought in show in the library straight away.</summary>
    public CanvasConnectModel ForWindow(CanvasConnectExits exits)
    {
        var model = new CanvasConnectModel(canvas.Context, canvas.Watch, exits, forSetup: false) { ShowFooter = true, FinishLabel = "Finish", StepLabel = "" };
        model.Picker.OnSaved = () =>
        {
            messages.Send(new LibraryClassesChanged());
            return Task.CompletedTask;
        };
        return model;
    }

    /// <summary>Connect Canvas inside setup or the first run, under the flow's own buttons (its footer hidden); it stops
    /// once the courses are found. <paramref name="finishLabel"/>: the flow's own word for going on.</summary>
    public CanvasConnectModel ForSetup(CanvasConnectExits exits, string? finishLabel = null)
    {
        var model = new CanvasConnectModel(canvas.Context, canvas.Watch, exits, forSetup: true) { ShowFooter = false };
        if (finishLabel is not null) model.FinishLabel = finishLabel;
        return model;
    }

    /// <summary>The first run's sync after <paramref name="connect"/>: when the courses become classes, the library is
    /// read again and the sidebar shows them.</summary>
    public FirstSyncModel Sync(CanvasConnectModel connect) => new(connect, canvas.Context, canvas.Watch, async () =>
    {
        await host.CheckLibraryAsync();
        messages.Send(new LibraryClassesChanged());
    });

    /// <summary>The first run's step that adds the Canvas add-on from the browser's store.</summary>
    public AddOnModel AddOn() => new(canvas.Context, canvas.Watch);
}
