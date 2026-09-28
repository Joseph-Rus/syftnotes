using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.VisualTree;
using StudyStash.App.ViewModels;
using StudyStash.App.Views;

namespace StudyStash.App.Tests;

/// <summary>Coming up, drawn in both looks, and in the dropdown.</summary>
public class ComingUpViewTests
{
    static ComingUpModel Model(bool rows)
    {
        var m = new ComingUpModel();
        m.Show(true, rows ? [new ComingUpRow("a", "Now, until 11:15 AM", "CS 101 Lecture", "Hall 204", "CS 101", Brushes.Red, true),
            new ComingUpRow("b", "Tomorrow 9:00 AM", "Dentist", null, null, Brushes.Gray, false)] : []);
        return m;
    }

    static List<string> Texts(Control view) =>
        [.. view.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text ?? "")];

    static Control Laid(Control view)
    {
        view.VerticalAlignment = VerticalAlignment.Top;
        var window = new Window { Width = 360, Height = 400, Content = view };
        window.Show();
        window.UpdateLayout();
        return view;
    }

    [AvaloniaFact]
    public void Both_looks_show_the_rows_with_when_and_class()
    {
        foreach (var view in new Control[] { new MacComingUp { DataContext = Model(true) }, new WinComingUp { DataContext = Model(true) } })
        {
            var texts = Texts(Laid(view));
            Assert.Contains("Coming up", texts);
            Assert.Contains("CS 101 Lecture", texts);
            Assert.Contains("CS 101 · Hall 204", texts);
            Assert.Contains("Tomorrow 9:00 AM", texts);
            Assert.DoesNotContain(CalendarWords.NothingComingUp, texts);
            ((Window)view.Parent!).Close();
        }
    }

    [AvaloniaFact]
    public void Nothing_left_says_so_and_no_calendars_hides_it()
    {
        var empty = Laid(new MacComingUp { DataContext = Model(false) });
        Assert.Contains(CalendarWords.NothingComingUp, Texts(empty));
        ((Window)empty.Parent!).Close();
        var none = Laid(new WinComingUp { DataContext = new ComingUpModel() });
        Assert.Empty(Texts(none));
        ((Window)none.Parent!).Close();
    }

    [AvaloniaFact]
    public void The_dropdown_carries_coming_up_above_recent()
    {
        var panel = new PanelModel();
        panel.ComingUp.Show(true, Model(true).Rows.ToList());
        var view = Laid(new MacPanel { DataContext = panel });
        Assert.Contains("CS 101 Lecture", Texts(view));
        Assert.Single(view.GetLogicalDescendants().OfType<MacComingUp>());
        ((Window)view.Parent!).Close();
        panel.LibraryOnly = true;
        var library = Laid(new WinPanel { DataContext = panel });
        Assert.DoesNotContain("CS 101 Lecture", Texts(library));
        ((Window)library.Parent!).Close();
    }
}
