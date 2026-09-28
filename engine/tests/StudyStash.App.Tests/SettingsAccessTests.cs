using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StudyStash.App.Platform;
using StudyStash.App.ViewModels;
using StudyStash.App.Views;

namespace StudyStash.App.Tests;

/// <summary>
/// Settings can be reached from everywhere: a gear in the dropdown (beside Open Study Stash) and in the library
/// window's toolbar, the Mac's app menu (Settings… ⌘,), ⌘, / Ctrl+, in any window; and when a full menu bar hides
/// the S., the app says so and can move it into view.
/// </summary>
public class SettingsAccessTests
{
    static Window Host(Control content, SkinKind skin)
    {
        ((App)Application.Current!).UseSkin(skin);
        var w = new Window { Width = 1300, Height = 900, RequestedThemeVariant = ThemeVariant.Light, Content = content };
        w.Show();
        Dispatcher.UIThread.RunJobs();
        return w;
    }

    [AvaloniaTheory]
    [InlineData(SkinKind.Mac)]
    [InlineData(SkinKind.Win)]
    public void The_dropdown_has_a_Settings_gear_beside_Open_Study_Stash(SkinKind skin)
    {
        ((App)Application.Current!).UseSkin(skin);
        int opened = 0;
        var model = Demo.Panel(recording: false);
        model.OnSettings = () => opened++;
        Control view = skin == SkinKind.Mac ? new MacPanel { DataContext = model } : new WinPanel { DataContext = model };
        var w = Host(view, skin);
        try
        {
            var gear = view.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "Settings");
            var open = view.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "OpenApp");
            Assert.Same(model.SettingsCommand, gear.Command);
            Assert.Equal("Settings", Avalonia.Automation.AutomationProperties.GetName(gear));
            gear.Command!.Execute(null);
            Assert.Equal(1, opened);

            // On one row with Open Study Stash, the same height and centred on it; its right edge is the search
            // field's (Mac) or the footer's inset (Windows).
            var g = gear.TranslatePoint(default, view)!.Value;
            var o = open.TranslatePoint(default, view)!.Value;
            Assert.Equal(o.Y + open.Bounds.Height / 2, g.Y + gear.Bounds.Height / 2, 1);
            Assert.True(g.X > o.X + open.Bounds.Width - 1, "the gear sits right of Open Study Stash");
            if (skin == SkinKind.Mac)
            {
                var search = view.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "Search");
                Assert.Equal(search.TranslatePoint(default, view)!.Value.X + search.Bounds.Width, g.X + gear.Bounds.Width, 1);
                Assert.Equal(open.Bounds.Height, gear.Bounds.Height, 1);
            }
        }
        finally
        {
            w.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(SkinKind.Mac)]
    [InlineData(SkinKind.Win)]
    public void The_library_window_opens_Settings_from_its_own_buttons(SkinKind skin)
    {
        ((App)Application.Current!).UseSkin(skin);
        var model = new LibraryModel();
        int opened = 0;
        model.OnSettings = () => opened++;
        Control view = skin == SkinKind.Mac ? new MacLibrary { DataContext = model } : new WinLibrary { DataContext = model };
        var w = Host(view, skin);
        try
        {
            var buttons = view.GetVisualDescendants().OfType<Button>().Where(b => b.Command == model.SettingsCommand && b.IsEffectivelyVisible).ToList();
            Assert.NotEmpty(buttons);
            if (skin == SkinKind.Mac)
            {
                // In the title bar's toolbar, right of search.
                var header = view.FindControl<Control>("Header")!;
                var gear = Assert.Single(buttons);
                Assert.Contains(header, gear.GetVisualAncestors());
                var search = header.GetVisualDescendants().OfType<Button>().Single(b => b.Command == model.SearchCommand);
                Assert.True(gear.TranslatePoint(default, view)!.Value.X > search.TranslatePoint(default, view)!.Value.X);
            }
            buttons[0].Command!.Execute(null);
            Assert.Equal(1, opened);
        }
        finally
        {
            w.Close();
        }
    }

    [Fact]
    public void The_app_menu_has_About_and_Settings_with_its_shortcut()
    {
        int about = 0, settings = 0;
        var menu = AppMenu.Build(() => about++, () => settings++);
        var items = menu.Items.OfType<NativeMenuItem>().Where(i => i is not NativeMenuItemSeparator).ToList();
        Assert.Equal(["About Study Stash", "Settings…"], items.Select(i => i.Header));
        var s = items.Single(i => i.Header == "Settings…");
        Assert.Equal(Key.OemComma, s.Gesture!.Key);
        Assert.Equal(OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control, s.Gesture.KeyModifiers);
        // Clicked the way the menu bar clicks it.
        var raise = typeof(NativeMenuItem).GetInterfaces().SelectMany(i => i.GetMethods()).Single(m => m.Name == "RaiseClicked");
        raise.Invoke(s, null);
        raise.Invoke(items[0], null);
        Assert.Equal(1, settings);
        Assert.Equal(1, about);
    }

    [Fact]
    public void The_app_menu_already_showing_is_filled_in_place()
    {
        // Avalonia's default ("About Avalonia") is already in the menu bar: the same menu gets Study Stash's items.
        var shown = new NativeMenu();
        shown.Add(new NativeMenuItem("About Avalonia"));
        AppMenu.Fill(shown, () => { }, () => { });
        Assert.Equal(["About Study Stash", "Settings…"], shown.Items.OfType<NativeMenuItem>().Where(i => i is not NativeMenuItemSeparator).Select(i => i.Header));
    }

    [AvaloniaFact]
    public void Every_window_opens_Settings_with_its_shortcut()
    {
        var w = new Window();
        int opened = 0;
        AppMenu.Attach(w, () => { }, () => opened++);
        var binding = Assert.Single(w.KeyBindings, b => b.Gesture.Equals(AppMenu.SettingsGesture));
        binding.Command.Execute(null);
        Assert.Equal(1, opened);
        if (OperatingSystem.IsMacOS())
        {
            var bar = NativeMenu.GetMenu(w)!;
            var window = Assert.Single(bar.Items.OfType<NativeMenuItem>());
            Assert.Equal("Window", window.Header);
            Assert.Equal(["Minimize", "Zoom", "Close", "Study Stash library", "Settings…"], window.Menu!.Items.OfType<NativeMenuItem>().Where(i => i is not NativeMenuItemSeparator).Select(i => i.Header));
            Assert.Equal(new KeyGesture(Key.M, KeyModifiers.Meta), window.Menu.Items.OfType<NativeMenuItem>().First().Gesture);
        }

        var floating = new Window();
        AppMenu.AddSettingsKey(floating, () => opened++);
        Assert.Single(floating.KeyBindings, b => b.Gesture.Equals(AppMenu.SettingsGesture)).Command.Execute(null);
        Assert.Equal(2, opened);
    }

    [Fact]
    public void Where_the_S_lives_is_said_plainly_and_a_hidden_one_can_be_shown()
    {
        var (title, text) = IconWords.WhereItIs(mac: true, hidden: false);
        Assert.Equal("Study Stash is in your menu bar", title);
        Assert.Contains("the S. at the top right", text, StringComparison.Ordinal);
        (title, text) = IconWords.WhereItIs(mac: true, hidden: true);
        Assert.Equal("Your menu bar is full", title);
        Assert.Contains(IconWords.ShowIt, text, StringComparison.Ordinal);
        (title, text) = IconWords.WhereItIs(mac: false, hidden: false);
        Assert.Contains("taskbar", title, StringComparison.Ordinal);
        // A notification shows two lines of about 50 characters: every one fits.
        foreach (var (m, h) in new[] { (true, false), (true, true), (false, false) })
        {
            var (t, x) = IconWords.WhereItIs(m, h);
            Assert.True(t.Length <= 40, t);
            Assert.True(x.Length <= 110, x);
        }
    }

    [Fact]
    public void The_pointer_to_the_S_keeps_out_of_the_way_once_the_dropdown_is_open()
    {
        Assert.True(IconWords.WorthSaying(hidden: false, dropdownOpen: false));
        Assert.False(IconWords.WorthSaying(hidden: false, dropdownOpen: true));
        // A hidden S. still needs its Show it, dropdown or not.
        Assert.True(IconWords.WorthSaying(hidden: true, dropdownOpen: true));
    }

    [Theory]
    [InlineData(1512, 664, 600)] // a 14" MacBook Pro: just right of the notch
    [InlineData(1728, 772, 708)]
    [InlineData(2560, 0, 1216)] // no notch: halfway along
    [InlineData(200, 0, 40)]
    public void A_hidden_S_is_moved_just_right_of_the_notch(double screen, double rightOfNotch, double position) =>
        Assert.Equal(position, Windows.Placement.MenuBarPosition(screen, rightOfNotch));
}
