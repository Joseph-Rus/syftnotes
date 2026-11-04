using Avalonia.Controls;
using Avalonia.Threading;
using StudyStash.App.Platform;
using StudyStash.App.Services;
using StudyStash.App.ViewModels;
using StudyStash.App.Views;
using StudyStash.App.Windows;
using StudyStash.Core;

namespace StudyStash.App;

/// <summary>
/// "You have Syft Notes folders in two places", asked at start only when this computer has two or more library folders
/// the student hasn't answered for (<see cref="LibraryFolderPick"/>): use one, and bring the others' notes into it if
/// they want. Closing the window keeps the folder in use, and it's asked again next time.
/// </summary>
public static partial class Shell
{
    /// <summary>Looks for library folders off the UI thread, and asks if there's something to ask.</summary>
    static void AskWhichNotesFolderSoon()
    {
        // Not for the self-test: its library is a folder of its own, and it drives the windows it expects.
        if (SelfTest.Dir is not null) return;
        Run(async () =>
        {
            var found = await Task.Run(() => LibraryFolderPick.ToAsk(host));
            if (found is not null) Dispatcher.UIThread.Post(() => AskWhichNotesFolder(found));
        }, "LibraryFolderPick");
    }

    static void AskWhichNotesFolder(IReadOnlyList<LibraryFolder> found)
    {
        var model = new NotesFolderPickModel(found, Py.UserHome(), (picked, bringIn, progress, ct) => UseNotesFolderAsync(picked, found, bringIn, progress, ct),
            (picked, progress, ct) => LibraryFolderPick.PlanAsync(host, picked, found, progress, ct), dir => Machine.Open(dir));
        var view = new NotesFolderPick { DataContext = model, Height = model.WantedHeight };
        // Come up by itself at a login start, it leaves the keyboard where it was.
        bool asked = !Program.Background;
        AppWindows.Open(new WindowSpec("notes-folder", model.Title, view) { Activate = asked, AppForward = asked }, w =>
        {
            if (Skin.Current == SkinKind.Mac) w.Bind(Window.BackgroundProperty, w.GetResourceObservable("Win"));
            model.OnDone = w.Close;
            w.Closed += (_, _) =>
            {
                model.OnDone = null;
                view.DataContext = null;
            };
        });
    }

    static async Task<Outcome<List<BringInCount>>> UseNotesFolderAsync(LibraryFolder picked, IReadOnlyList<LibraryFolder> shown, bool bringIn,
        Action<int, int> progress, CancellationToken ct)
    {
        var outcome = await LibraryFolderPick.UseAsync(host, picked, shown, bringIn, progress, ct);
        if (outcome.Ok && (bringIn || !picked.InUse)) RequestLibraryReload(whole: true);
        return outcome;
    }
}
