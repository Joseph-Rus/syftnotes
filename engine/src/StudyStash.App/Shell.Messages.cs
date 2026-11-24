using CommunityToolkit.Mvvm.Messaging;

namespace StudyStash.App;

/// <summary>The shell's side of the app's messages (Messages.cs): what services, view models and the toolkit ask of the
/// app, done here the way the calls into Shell they replace did it.</summary>
public static partial class Shell
{
    /// <summary>The shell's place on the messenger, kept for the app's life (the messenger holds it weakly).</summary>
    static readonly object listener = new();

    static void Listen(IMessenger messages)
    {
        messages.Register<ToastRequested>(listener, static (_, m) => Toast(m.Title, m.Text, m.Action, m.Run, m.Stay));
        messages.Register<QuitRequested>(listener, static (_, m) => Quit(m.Code));
        messages.Register<QuitAskingRequested>(listener, static (_, _) => QuitAsking());
        messages.Register<SettingsRequested>(listener, static (_, m) =>
        {
            if (m.Section is null) SettingsFromMenu();
            else ShowSettings(m.Section);
        });
        messages.Register<LibraryRequested>(listener, static (_, _) => ShowLibrary());
        messages.Register<SetupAgainRequested>(listener, static (_, m) => RunSetupAgain(m.ByHand));
        messages.Register<TourRequested>(listener, static (_, _) => ShowTourAgain());
        messages.Register<CanvasConnectRequested>(listener, static (_, _) => ShowCanvasConnect());
        messages.Register<BrowserChoiceRequested>(listener, static (_, m) => ChooseBrowser(m.Ask));
        messages.Register<ShortcutsPaused>(listener, static (_, m) => PauseShortcuts(m.Paused));
        messages.Register<ShortcutsWordsRequest>(listener, static (_, m) => m.Reply(ShortcutsSay()));
        messages.Register<LibraryClassesChanged>(listener, static (_, _) => LibraryClassesChanged());
        messages.Register<WindowsChanged>(listener, static (_, _) => UpdateDock());
        messages.Register<SomethingWentWrong>(listener, static (_, _) => SayOops());
    }
}
