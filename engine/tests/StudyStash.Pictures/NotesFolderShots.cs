using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using StudyStash.App.ViewModels;
using StudyStash.App.Views;
using StudyStash.Core;

namespace StudyStash.App.Pictures;

/// <summary>"You have Syft Notes folders in two places" with a school OneDrive's folder in use and a personal one's
/// from before, and the bring-in's summary and result: a picture of each, in each look, light and dark.</summary>
public class NotesFolderShots
{
    static NotesFolderPick View(NotesFolderStep step = NotesFolderStep.Pick, bool copying = false, bool looking = false)
    {
        string home = OperatingSystem.IsWindows() ? @"C:\Users\ada" : "/Users/ada";
        LibraryFolder[] found =
        [
            new(Path.Combine(home, "OneDrive - Example University", "Documents", "Study Stash"), 4, false, new DateOnly(2026, 11, 2), true),
            new(Path.Combine(home, "OneDrive", "Documents", "Study Stash"), 38, false, new DateOnly(2026, 10, 3), false),
        ];
        var model = new NotesFolderPickModel(found, home, (_, _, progress, _) =>
            {
                if (!copying) return Task.FromResult(Services.Outcome<List<BringInCount>>.Answered([Counted(found[1].Path)]));
                // Part-way through a big folder: it says how far, and never finishes in the picture.
                progress(120, 400);
                return new TaskCompletionSource<Services.Outcome<List<BringInCount>>>().Task;
            },
            (_, progress, _) =>
            {
                if (!looking) return Task.FromResult(Services.Outcome<List<BringInCount>>.Answered([Counted(found[1].Path)]));
                // Looking through a big folder from the pick page: the longest progress line there is.
                progress(812, 1484);
                return new TaskCompletionSource<Services.Outcome<List<BringInCount>>>().Task;
            }, _ => { }, new DateOnly(2026, 11, 3));
        if (step != NotesFolderStep.Pick) (step == NotesFolderStep.Summary ? model.Look : model.Bring).Command.Execute(null);
        if (copying) model.Bring.Command.Execute(null);
        if (looking) model.Look.Command.Execute(null);
        Assert.Equal(step, model.Step);
        Assert.Equal(copying || looking, model.Busy);
        return new NotesFolderPick { DataContext = model, Height = model.WantedHeight };
    }

    static BringInCount Counted(string from) => new(from, New: 34, Here: 3, Both: 1, Deleted: 0, Classes: 2, Files: 5);

    [AvaloniaFact]
    public void Mac_and_Win_notes_folder_question()
    {
        foreach (var t in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            foreach (var step in new[] { NotesFolderStep.Pick, NotesFolderStep.Summary, NotesFolderStep.Done })
            {
                string name = step switch { NotesFolderStep.Pick => "notes-folder", NotesFolderStep.Summary => "notes-folder-summary", _ => "notes-folder-done" };
                Shot.Take("mac-" + name, SkinKind.Mac, t, () => View(step), size: new Avalonia.Size(708, 620));
                Shot.Take("win-" + name, SkinKind.Win, t, () => View(step), size: new Avalonia.Size(708, 620));
            }
            Shot.Take("mac-notes-folder-looking", SkinKind.Mac, t, () => View(looking: true), size: new Avalonia.Size(708, 620));
            Shot.Take("win-notes-folder-looking", SkinKind.Win, t, () => View(looking: true), size: new Avalonia.Size(708, 620));
            Shot.Take("mac-notes-folder-copying", SkinKind.Mac, t, () => View(NotesFolderStep.Summary, copying: true), size: new Avalonia.Size(708, 620));
            Shot.Take("win-notes-folder-copying", SkinKind.Win, t, () => View(NotesFolderStep.Summary, copying: true), size: new Avalonia.Size(708, 620));
        }
    }
}
