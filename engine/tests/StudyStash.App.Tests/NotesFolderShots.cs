using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using StudyStash.App.ViewModels;
using StudyStash.App.Views;
using StudyStash.Core;

namespace StudyStash.App.Tests;

/// <summary>"Which notes folder should Syft Notes use?" with a school OneDrive's folder in use and a personal one's
/// from before: a picture for each look.</summary>
public class NotesFolderShots
{
    static NotesFolderPick View()
    {
        string home = OperatingSystem.IsWindows() ? @"C:\Users\ada" : "/Users/ada";
        LibraryFolder[] found =
        [
            new(Path.Combine(home, "OneDrive - Example University", "Documents", "Study Stash"), 4, false, new DateOnly(2026, 11, 2), true),
            new(Path.Combine(home, "OneDrive", "Documents", "Study Stash"), 38, false, new DateOnly(2026, 10, 3), false),
        ];
        var model = new NotesFolderPickModel(found, home, _ => Task.FromResult(Services.Outcome.Done), _ => { }, new DateOnly(2026, 11, 3));
        return new NotesFolderPick { DataContext = model, Height = model.WantedHeight };
    }

    [AvaloniaFact]
    public void Mac_and_Win_notes_folder_question()
    {
        foreach (var t in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            Shot.Take("mac-notes-folder", SkinKind.Mac, t, View, size: new Avalonia.Size(708, 580));
            Shot.Take("win-notes-folder", SkinKind.Win, t, View, size: new Avalonia.Size(708, 580));
        }
    }
}
