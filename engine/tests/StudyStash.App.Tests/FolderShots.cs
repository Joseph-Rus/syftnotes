using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using StudyStash.App.ViewModels;
using StudyStash.App.Views;

namespace StudyStash.App.Tests;

/// <summary>A sidebar folder of the student's own under the classes, and a class's home with its linked folders.</summary>
public class FolderShots
{
    static LibraryModel App()
    {
        var m = Demo.Overview("CS 101");
        m.Classes.Add(new ClassItem { Name = "Capstone", Dot = Skin.ClassDot(4), Count = 2, Group = "Projects", Section = "Projects" });
        m.Classes.Add(new ClassItem { Name = "Robotics club", Dot = Skin.ClassDot(5), Count = 0, Group = "Projects" });
        m.Overview!.SetFolders([
            new LinkedFolder("Argus", "/Users/me/Code/Argus", true, () => { }, () => { }),
            new LinkedFolder("Shared drive", "/Volumes/Team/Capstone", false, () => { }, () => { }),
        ], () => { });
        return m;
    }

    [AvaloniaFact]
    public void Mac_and_Win_folders()
    {
        foreach (var t in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            Shot.Take("mac-19-folders", SkinKind.Mac, t, () => new MacLibrary { DataContext = App(), Width = 1280, Height = 900 }, size: new Avalonia.Size(1408, 1028));
            Shot.Take("win-19-folders", SkinKind.Win, t, () => new WinLibrary { DataContext = App(), Width = 1280, Height = 900 }, size: new Avalonia.Size(1408, 1028));
        }
    }
}
