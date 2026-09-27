using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using StudyStash.App.Views;

namespace StudyStash.App.Tests;

/// <summary>The menu bar dropdown and the tray flyout are as compact as the system's own: Record and the class switcher
/// a normal control's height, the search field a small one, the whole panel narrower than the design's first cut.</summary>
public class CompactPanelTests
{
    static Control Laid(SkinKind skin)
    {
        ((App)Application.Current!).UseSkin(skin);
        Control view = skin == SkinKind.Mac
            ? new MacPanel { DataContext = Demo.Panel(recording: false), VerticalAlignment = VerticalAlignment.Top }
            : new WinPanel { DataContext = Demo.Panel(recording: false), VerticalAlignment = VerticalAlignment.Top };
        var window = new Window { Width = 600, Height = 900, Content = view };
        window.Show();
        window.UpdateLayout();
        return view;
    }

    [AvaloniaFact]
    public void The_mac_dropdown_is_a_compact_menu_bar_extra()
    {
        try
        {
            var view = Laid(SkinKind.Mac);
            Assert.InRange(view.Bounds.Width, 280, 320);
            Assert.InRange(view.FindControl<Button>("Record")!.Bounds.Height, 30, 36);
            var chooser = view.FindControl<Button>("SwitchClass")!;
            Assert.InRange(chooser.Bounds.Height, 30, 36);
            Assert.True(chooser.Bounds.Width <= 36, $"the class switcher is {chooser.Bounds.Width} wide");
            Assert.InRange(view.FindControl<Button>("Search")!.Bounds.Height, 26, 30);
            ((Window)view.Parent!).Close();
        }
        finally
        {
            ((App)Application.Current!).UseSkin(SkinKind.Mac);
        }
    }

    [AvaloniaFact]
    public void The_windows_flyout_uses_windows_11_control_sizes()
    {
        try
        {
            var view = Laid(SkinKind.Win);
            Assert.InRange(view.Bounds.Width, 320, 360);
            Assert.InRange(view.FindControl<Button>("Record")!.Bounds.Height, 32, 36);
            Assert.InRange(view.FindControl<Button>("SwitchClass")!.Bounds.Width, 32, 40);
            Assert.Equal(32, view.FindControl<Button>("Search")!.Bounds.Height);
            ((Window)view.Parent!).Close();
        }
        finally
        {
            ((App)Application.Current!).UseSkin(SkinKind.Mac);
        }
    }
}
