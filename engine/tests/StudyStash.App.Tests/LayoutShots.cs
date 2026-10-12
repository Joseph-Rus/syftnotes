using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using StudyStash.App.ViewModels;
using StudyStash.App.Views;

namespace StudyStash.App.Tests;

/// <summary>A lecture with the sidebar folded away (the notes keep their place in the room they have), and the pages
/// beside the sidebar: Home, Due and a folder's.</summary>
public class LayoutShots
{
    static LibraryModel Lecture()
    {
        var m = Demo.Library();
        m.Notes = AiDemo.LectureNotes(Demo.Notes);
        m.Ask = AiDemo.AskIdle();
        m.SidebarHidden = true;
        return m;
    }

    [AvaloniaFact]
    public void Mac_and_Win_folded()
    {
        var size = new Avalonia.Size(1408, 928);
        Shot.Take("mac-04-sidebar-hidden", SkinKind.Mac, ThemeVariant.Dark, () => new MacLibrary { DataContext = Lecture(), Width = 1280, Height = 800 }, size: size);
        Shot.Take("win-04-sidebar-hidden", SkinKind.Win, ThemeVariant.Light, () => new WinLibrary { DataContext = Lecture(), Width = 1280, Height = 800 }, size: size);
        var home = Demo.Overview();
        home.Overview!.Ask = AiDemo.AskIdle();
        home.Overview.Ask.OnlyScopes("all");
        home.Overview.Ask.NameScopes(Nouns.Lecture, null);
        Shot.Take("mac-19-home", SkinKind.Mac, ThemeVariant.Dark, () => new MacLibrary { DataContext = home, Width = 1280, Height = 800 }, size: size);
        Shot.Take("win-19-home", SkinKind.Win, ThemeVariant.Light, () => new WinLibrary { DataContext = home, Width = 1280, Height = 800 }, size: size);
        Shot.Take("mac-19-due", SkinKind.Mac, ThemeVariant.Dark, () => new MacLibrary { DataContext = Demo.Overview("\u0001due"), Width = 1280, Height = 900 }, size: new Avalonia.Size(1408, 1028));
        Shot.Take("win-19-due", SkinKind.Win, ThemeVariant.Light, () => new WinLibrary { DataContext = Demo.Overview("\u0001due"), Width = 1280, Height = 900 }, size: new Avalonia.Size(1408, 1028));
        Shot.Take("mac-19-folder", SkinKind.Mac, ThemeVariant.Dark, () => new MacLibrary { DataContext = Demo.Overview("CS 101"), Width = 1280, Height = 900 }, size: new Avalonia.Size(1408, 1028));
    }
}
