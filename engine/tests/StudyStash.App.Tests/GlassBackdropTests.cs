using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using SkiaSharp;
using StudyStash.App.Controls;
using StudyStash.App.Windows;

namespace StudyStash.App.Tests;

/// <summary>The Mac's blur sits only under a floating window's glass: the window itself stays clear, and each glass
/// gets its own system blur view, placed and shaped in AppKit's terms. The placing and the mask are pure, so they're
/// tested without a real window.</summary>
public class GlassBackdropTests
{
    [Fact]
    public void A_blur_frame_counts_up_from_the_bottom_when_the_view_isnt_flipped()
    {
        // A 300x120 glass 60pt in from the left and top of a 420x240 window.
        var frame = GlassBackdrop.FrameFor(new Rect(60, 60, 300, 120), 240, flipped: false);

        Assert.Equal(new Rect(60.5, 60.5, 299, 119), frame);
    }

    [Fact]
    public void A_blur_frame_is_the_glass_itself_in_a_flipped_view()
    {
        var frame = GlassBackdrop.FrameFor(new Rect(60, 20, 300, 120), 400, flipped: true);

        Assert.Equal(new Rect(60.5, 20.5, 299, 119), frame);
    }

    [Fact]
    public void A_blur_frame_uses_points_not_pixels()
    {
        // Low on a tall window: y counts from the bottom, and nothing is multiplied by a display's scale.
        var frame = GlassBackdrop.FrameFor(new Rect(0, 500, 100, 50), 600, flipped: false);

        Assert.Equal(new Rect(0.5, 50.5, 99, 49), frame);
    }

    [Fact]
    public void A_blur_view_stays_pinned_to_the_top_left_as_the_window_resizes()
    {
        const ulong flexibleRight = 4, flexibleBottomUp = 8, flexibleBottomFlipped = 32;

        Assert.Equal(flexibleRight | flexibleBottomUp, GlassBackdrop.AutoresizingFor(flipped: false));
        Assert.Equal(flexibleRight | flexibleBottomFlipped, GlassBackdrop.AutoresizingFor(flipped: true));
    }

    /// <summary>No window-wide blur, on either look: a floating window is clear, so nothing shows round its panel but
    /// the panel's own shadow.</summary>
    [AvaloniaTheory]
    [InlineData(SkinKind.Mac)]
    [InlineData(SkinKind.Win)]
    public void A_floating_window_only_ever_asks_to_be_clear(SkinKind skin)
    {
        Skin.UseTheme(ColourThemes.Default);
        var app = (App)Application.Current!;
        app.UseSkin(skin);
        var popup = new Floating { Content = new Glass { Width = 200, Height = 100, CornerRadius = new CornerRadius(24) } };
        try
        {
            popup.Show();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.Equal([WindowTransparencyLevel.Transparent], popup.TransparencyLevelHint);
        }
        finally
        {
            popup.Close();
            app.UseSkin(SkinKind.Mac);
        }
    }

    static byte AlphaAt(byte[] png, int x, int y)
    {
        using var bitmap = SKBitmap.Decode(png);
        return bitmap.GetPixel(x, y).Alpha;
    }

    [Fact]
    public void A_shapes_corner_is_transparent_and_its_centre_opaque()
    {
        var shapes = new[] { (new Rect(10, 10, 100, 100), new CornerRadius(24)) };
        var png = GlassBackdrop.BuildMask(shapes, new PixelSize(150, 150));

        Assert.Equal(0, AlphaAt(png, 11, 11)); // the round corner, just inside the rect
        Assert.Equal(255, AlphaAt(png, 60, 60)); // well inside the shape
    }

    [Fact]
    public void Outside_every_shape_is_transparent()
    {
        var shapes = new[] { (new Rect(20, 20, 40, 40), new CornerRadius(8)) };
        var png = GlassBackdrop.BuildMask(shapes, new PixelSize(100, 100));

        Assert.Equal(0, AlphaAt(png, 5, 5));
        Assert.Equal(0, AlphaAt(png, 90, 90));
    }

    [Fact]
    public void No_shapes_makes_a_wholly_transparent_mask()
    {
        var png = GlassBackdrop.BuildMask([], new PixelSize(40, 40));

        Assert.Equal(0, AlphaAt(png, 20, 20));
    }
}
