using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using StudyStash.App.Windows;

namespace StudyStash.App.Tests;

/// <summary>A floating window whose content changes size (the recorder's pill opening) ends up sized to it and where
/// it was put, even when the system had resized it by hand before.</summary>
public class FloatingRefitTests
{
    [AvaloniaFact]
    public void Growing_content_is_placed_again_once_the_window_has_its_new_size()
    {
        var content = new Border { Width = 120, Height = 28 };
        var w = new Floating { Content = content };
        var at = new PixelPoint(400, 300);
        w.Show();
        w.Put(at);
        w.SizeToContent = SizeToContent.Manual; // what the system resizing it once leaves behind

        content.Width = 360;
        content.Height = 520;
        int placed = 0;
        w.Refit(() =>
        {
            placed++;
            w.Put(at);
        });
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(2, placed);
        Assert.Equal(SizeToContent.WidthAndHeight, w.SizeToContent);
        var panel = w.Panel()!.Value;
        double scale = w.DesktopScaling;
        Assert.Equal(new PixelSize((int)(360 * scale), (int)(520 * scale)), panel.Size);
        int room = (int)(Floating.ShadowRoom * scale);
        Assert.Equal(new PixelPoint(at.X + room, at.Y + room), panel.Position);
        w.Close();
    }
}
