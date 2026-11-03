using CommunityToolkit.Mvvm.Messaging.Messages;
using StudyStash.App.Services;

namespace StudyStash.App;

// What a service or a view model asks of the app as a whole, as messages instead of calls into Shell (connection rule
// 5: services never call Shell). They go out on the app's one messenger: the container's IMessenger, which is
// WeakReferenceMessenger.Default, so a static service sends on that. Shell listens (Shell.Messages.cs) and does what it
// did before. A message nobody listens for goes nowhere: in a test, where Shell never starts, nothing happens.

/// <summary>A notification for the student (<see cref="Shell.Toast"/>): a title, a line, and an optional button.</summary>
public sealed record ToastRequested(string Title, string Text, string? Action = null, Action? Run = null, TimeSpan? Stay = null);

/// <summary>Quit Syft Notes, with an exit code.</summary>
public sealed record QuitRequested(int Code = 0);

/// <summary>Settings, open at <see cref="Section"/> ("AI", "Canvas"…). With no section it's Settings as the app menu
/// and ⌘, open it: the dropdown and the quick panel make way, and before setup's done, setup comes forward instead.</summary>
public sealed record SettingsRequested(string? Section = null);

/// <summary>The library window.</summary>
public sealed record LibraryRequested;

/// <summary>Settings → General's Run setup again (<see cref="ByHand"/>: setup by hand).</summary>
public sealed record SetupAgainRequested(bool ByHand = false);

/// <summary>Connect Canvas, in its own window.</summary>
public sealed record CanvasConnectRequested;

/// <summary>Which browser Canvas uses: the question asked from Settings → Canvas's "Change browser".</summary>
public sealed record BrowserChoiceRequested(CanvasApi.BrowserQuestion Ask);

/// <summary>A screen is listening for a new shortcut (true) or done (false): the app lets go of its shortcuts meanwhile.</summary>
public sealed record ShortcutsPaused(bool Paused);

/// <summary>What Settings says about the shortcuts: which one (if any) another app already has. Null when nobody
/// answers, or there's nothing to say.</summary>
public sealed class ShortcutsWordsRequest : RequestMessage<string?>;

/// <summary>The library's classes changed (made from Canvas courses, renamed): the sidebar and pages read them again.</summary>
public sealed record LibraryClassesChanged;

/// <summary>A window opened or closed: a Mac's Dock follows which are open.</summary>
public sealed record WindowsChanged;

/// <summary>Something unexpected went wrong (already in the log): the student is told once.</summary>
public sealed record SomethingWentWrong;
