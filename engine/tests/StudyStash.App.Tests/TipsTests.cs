using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StudyStash.App.Controls;
using StudyStash.App.Views;

namespace StudyStash.App.Tests;

/// <summary>Tooltips are calm: they wait about a second, never show over an open menu or right after a scroll, and
/// only icons and names cut short have one.</summary>
public class TipsTests
{
    static Window Show(Control content)
    {
        Tips.Reset();
        var window = new Window { Width = 600, Height = 400, Content = content };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    static Button Tipped()
    {
        var button = new Button { Content = new Icon { Glyph = "delete", Size = 16 }, Width = 32, Height = 32 };
        ToolTip.SetTip(button, "Delete lecture");
        return button;
    }

    [AvaloniaFact]
    public void A_tooltip_waits_about_a_second_like_the_systems_own()
    {
        Assert.Equal(1000, ToolTip.GetShowDelay(Tipped()));
        var asked = new Button { [ToolTip.ShowDelayProperty] = 200, [ToolTip.TipProperty] = "Back" };
        Assert.Equal(200, ToolTip.GetShowDelay(asked));
    }

    [AvaloniaFact]
    public void No_tooltip_shows_while_a_menu_is_open()
    {
        var tipped = Tipped();
        var opener = new Button { Content = "Sorting", Flyout = new MenuFlyout { Items = { new MenuItem { Header = "By date" } } } };
        var window = Show(new StackPanel { Children = { tipped, opener } });
        Assert.False(Tips.Hold(tipped));

        opener.Flyout!.ShowAt(opener);
        Dispatcher.UIThread.RunJobs();
        Assert.True(Tips.Hold(tipped));
        ToolTip.SetIsOpen(tipped, true);
        Dispatcher.UIThread.RunJobs();
        Assert.False(ToolTip.GetIsOpen(tipped), "a tooltip opened over the menu");

        opener.Flyout.Hide();
        Dispatcher.UIThread.RunJobs();
        Assert.False(Tips.Hold(tipped));
        ToolTip.SetIsOpen(tipped, true);
        Dispatcher.UIThread.RunJobs();
        Assert.True(ToolTip.GetIsOpen(tipped));
        ToolTip.SetIsOpen(tipped, false);
        window.Close();
    }

    [AvaloniaFact]
    public void After_a_scroll_no_tooltip_shows_until_the_pointer_really_moves()
    {
        var tipped = Tipped();
        var window = Show(new StackPanel { Children = { tipped } });
        var at = new Point(16, 16);
        window.MouseMove(at);
        window.MouseWheel(at, new Vector(0, -1));
        Dispatcher.UIThread.RunJobs();
        Assert.True(Tips.Hold(tipped));

        window.MouseMove(at); // the content moved under a still pointer
        Assert.True(Tips.Hold(tipped));
        window.MouseMove(new Point(20, 24));
        Assert.False(Tips.Hold(tipped));
        window.Close();
    }

    [AvaloniaFact]
    public void A_name_shows_its_tooltip_only_when_it_is_cut_short()
    {
        TextBlock Name(string text)
        {
            var name = new TextBlock { Text = text, Width = 120, TextTrimming = TextTrimming.CharacterEllipsis };
            ToolTip.SetTip(name, text);
            Tips.SetWhenCut(name, true);
            return name;
        }

        var whole = Name("CS 101");
        var cut = Name("https://library.example.test/lectures/cs-101/week-3");
        var window = Show(new StackPanel { Children = { whole, cut } });
        Assert.False(Tips.IsCut(whole));
        Assert.True(Tips.Hold(whole));
        Assert.True(Tips.IsCut(cut));
        Assert.False(Tips.Hold(cut));
        window.Close();
    }

    /// <summary>
    /// In the everyday windows, in both looks, a tooltip is only on something that has no words of its own showing
    /// (an icon button) or is a name that shows its tooltip only when cut short; nothing repeats what it already says,
    /// and the recorder's pill doesn't pop one up every time the pointer crosses it.
    /// </summary>
    [AvaloniaFact]
    public void Only_icons_and_cut_names_have_tooltips()
    {
        try
        {
            foreach (var skin in new[] { SkinKind.Mac, SkinKind.Win })
            {
                ((App)Application.Current!).UseSkin(skin);
                var views = skin == SkinKind.Mac
                    ? new (string, Func<Control>)[]
                    {
                        ("dropdown", () => new MacPanel { DataContext = Demo.Panel(recording: false) }),
                        ("pill", () => new MacRecorder { DataContext = Demo.Recorder() }),
                        ("recorder", () => new MacRecorder { DataContext = Demo.Recorder(expanded: true) }),
                        ("library", () => new MacLibrary { DataContext = Demo.Library(), Width = 1280, Height = 800 }),
                    }
                    : [
                        ("flyout", () => new WinPanel { DataContext = Demo.Panel(recording: false) }),
                        ("pill", () => new WinRecorder { DataContext = Demo.Recorder() }),
                        ("recorder", () => new WinRecorder { DataContext = Demo.Recorder(expanded: true) }),
                        ("library", () => new WinLibrary { DataContext = Demo.Library(), Width = 1280, Height = 800 }),
                    ];
                foreach (var (name, build) in views)
                {
                    var view = build();
                    view.HorizontalAlignment = HorizontalAlignment.Left;
                    view.VerticalAlignment = VerticalAlignment.Top;
                    var window = Show(view);
                    window.Width = 1300;
                    window.Height = 900;
                    Dispatcher.UIThread.RunJobs();
                    foreach (var control in view.GetVisualDescendants().Prepend(view).OfType<Control>().Where(c => ToolTip.GetTip(c) is not null))
                    {
                        string where = $"{skin} {name}: {control.GetType().Name} {control.Name} \"{ToolTip.GetTip(control)}\"";
                        if (control is TextBlock text)
                        {
                            Assert.True(Tips.GetWhenCut(text) || ToolTip.GetTip(text) as string != text.Text, $"{where} repeats its own words");
                            continue;
                        }
                        var words = control.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible && !string.IsNullOrWhiteSpace(t.Text)).Select(t => t.Text);
                        Assert.True(!words.Any(), $"{where} already says \"{string.Join(" ", words)}\"");
                    }
                    window.Close();
                }
            }
        }
        finally
        {
            ((App)Application.Current!).UseSkin(SkinKind.Mac);
        }
    }
}
