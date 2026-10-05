using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using StudyStash.App.ViewModels;
using StudyStash.App.Views;

namespace StudyStash.App.Tests;

/// <summary>A lecture with the sidebar folded away, then the list too: the notes stay centred in the room they have.</summary>
public class LayoutShots
{
    static LibraryModel Lecture(bool listHidden)
    {
        var m = Demo.Library();
        m.Notes = AiDemo.LectureNotes(Demo.Notes);
        m.Ask = AiDemo.AskIdle();
        m.SidebarHidden = !listHidden;
        m.ListHidden = listHidden;
        return m;
    }

    [AvaloniaFact]
    public void Mac_and_Win_folded()
    {
        var size = new Avalonia.Size(1408, 928);
        foreach (bool list in new[] { false, true })
        {
            string name = list ? "list-hidden" : "sidebar-hidden";
            Shot.Take($"mac-04-{name}", SkinKind.Mac, ThemeVariant.Dark, () => new MacLibrary { DataContext = Lecture(list), Width = 1280, Height = 800 }, size: size);
            Shot.Take($"win-04-{name}", SkinKind.Win, ThemeVariant.Light, () => new WinLibrary { DataContext = Lecture(list), Width = 1280, Height = 800 }, size: size);
        }
        var home = Demo.Overview();
        home.Overview!.OnRecord = () => { };
        home.Overview.Ask = AiDemo.AskIdle();
        home.Overview.Ask.OnlyScopes("all");
        Shot.Take("mac-19-home", SkinKind.Mac, ThemeVariant.Dark, () => new MacLibrary { DataContext = home, Width = 1280, Height = 800 }, size: size);
        Shot.Take("win-19-home", SkinKind.Win, ThemeVariant.Light, () => new WinLibrary { DataContext = home, Width = 1280, Height = 800 }, size: size);
        Shot.Take("mac-19-class-home", SkinKind.Mac, ThemeVariant.Dark, () => new MacLibrary { DataContext = Demo.Overview("CS 101"), Width = 1280, Height = 900 }, size: new Avalonia.Size(1408, 1028));
    }

    [Fact]
    public void Folding_the_list_leaves_a_narrow_window_alone()
    {
        var m = new LibraryModel { ListHidden = true };
        Assert.False(m.ShowListColumn);
        Assert.True(m.CanToggleList);
        m.Narrow = true;
        Assert.True(m.ShowListColumn); // narrow shows the list or the page, never neither
        Assert.False(m.CanToggleList);
    }
}
