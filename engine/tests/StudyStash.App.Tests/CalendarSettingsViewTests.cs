using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StudyStash.App.ViewModels;
using StudyStash.App.Views;
using StudyStash.Core.Calendar;

namespace StudyStash.App.Tests;

/// <summary>Settings → Calendars drawn in both looks and both themes: a source's name and switch per calendar, and
/// the "coming soon" note while no kind offers a Connect button.</summary>
public class CalendarSettingsViewTests
{
    static CalendarSettingsModel Model()
    {
        using var home = new TempHome();
        var model = new CalendarSettingsModel(home.Path);
        CalendarSettings.Update(home.Path, s =>
        {
            s.Sources.Add(new CalendarSourceSettings { Id = "ics:abc", Kind = "ics", Name = "Fall 2026 classes" });
            s.Listed("ics:abc", [new CalendarInfo("ics:abc", "ics:abc", "Fall 2026 classes", null, true)]);
        });
        model.Load();
        return model;
    }

    public static IEnumerable<object[]> Looks() =>
        from skin in new[] { SkinKind.Mac, SkinKind.Win }
        from dark in new[] { false, true }
        select new object[] { skin, dark };

    [AvaloniaTheory]
    [MemberData(nameof(Looks))]
    public void A_source_shows_with_a_switch_per_calendar_and_no_connect_kind_yet(SkinKind skin, bool dark)
    {
        ((App)Application.Current!).UseSkin(skin);
        var model = Model();
        Control view = skin == SkinKind.Mac ? new MacCalendarSettings { DataContext = model } : new WinCalendarSettings { DataContext = model };
        var w = new Window { Width = 760, Height = 700, RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light, Content = view };
        w.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Fall 2026 classes");
        var toggle = view.GetVisualDescendants().OfType<ToggleButton>().Single(t => t.Classes.Contains("switch"));
        Assert.True(toggle.IsChecked);
        Assert.Contains(view.GetVisualDescendants().OfType<Button>(), b => b.Content is TextBlock { Text: "Remove" } && b.IsEffectivelyVisible);
        Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == CalendarWords.MoreComingSoon);
        w.Close();
    }

    [AvaloniaTheory]
    [MemberData(nameof(Looks))]
    public void With_no_source_yet_a_quiet_line_says_so(SkinKind skin, bool dark)
    {
        ((App)Application.Current!).UseSkin(skin);
        using var home = new TempHome();
        var model = new CalendarSettingsModel(home.Path);
        Control view = skin == SkinKind.Mac ? new MacCalendarSettings { DataContext = model } : new WinCalendarSettings { DataContext = model };
        var w = new Window { Width = 760, Height = 700, RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light, Content = view };
        w.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == CalendarWords.NoSources);
        w.Close();
    }
}
