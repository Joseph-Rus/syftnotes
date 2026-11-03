using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using SkiaSharp;
using StudyStash.App.Platform;
using StudyStash.App.Views;

namespace StudyStash.App.Tests.Snapshots;

/// <summary>The picture check is only worth having if a real change fails it and a rendering whisper doesn't. These run
/// everywhere (they draw or read pictures themselves, and never compare with a Mac's or Linux's verified ones): a changed
/// row, a changed word and a changed colour token each fail, and a handful of stray pixels pass.</summary>
public class SnapshotToleranceTests
{
    static byte[] Verified() => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Snapshots", "SnapshotTests.Home_light.verified.png"));

    static byte[] Changed(Action<SKBitmap> change)
    {
        using var bitmap = SKBitmap.Decode(Verified());
        change(bitmap);
        using var image = SKImage.FromBitmap(bitmap);
        return image.Encode(SKEncodedImageFormat.Png, 100).ToArray();
    }

    static int Count(byte[] a, byte[] b) => SnapshotSetup.Differences(a, b)?.Count ?? throw new InvalidOperationException("not the same size");

    static void Fails(int differing, string what) =>
        Assert.True(differing > SnapshotSetup.PixelsAllowed, $"{what}: only {differing} pixels differ, and {SnapshotSetup.PixelsAllowed} are allowed");

    [Fact]
    public void The_same_picture_is_the_same() => Assert.Equal(0, Count(Verified(), Verified()));

    [Fact]
    public void One_row_of_pixels_changed_fails() =>
        Fails(Count(Changed(b => { for (int x = 0; x < b.Width; x++) b.SetPixel(x, 500, SKColors.Black); }), Verified()), "a row of black pixels");

    [Fact]
    public void A_handful_of_stray_pixels_and_a_shade_off_pass()
    {
        var changed = Changed(b =>
        {
            for (int i = 0; i < 400; i++) // a shade off: below the channel delta
            {
                int x = 340 + i % 200, y = 100 + i / 200 * 3;
                var p = b.GetPixel(x, y);
                b.SetPixel(x, y, new SKColor((byte)Math.Max(0, p.Red - 3), (byte)Math.Max(0, p.Green - 3), (byte)Math.Max(0, p.Blue - 3), p.Alpha));
            }
            for (int i = 0; i < SnapshotSetup.PixelsAllowed; i++) b.SetPixel(500 + i, 300, SKColors.Black); // an edge's worth, far off
        });
        Assert.True(Count(changed, Verified()) <= SnapshotSetup.PixelsAllowed);
    }

    static Window Toast(string title) => Stage.Surface(() => new ToastView { Title = title, Text = "Recursion and the call stack", ActionLabel = "Open note" }, ThemeVariant.Light, new Size(420, 170));

    /// <summary>"Recent" became "Latest" in a menu and the old check passed it: a word is hundreds of pixels.</summary>
    [AvaloniaFact]
    public void A_changed_word_fails()
    {
        var was = SnapshotSetup.Png(Toast("Filed in CS 101"));
        var now = SnapshotSetup.Png(Toast("Saved in CS 101"));
        Fails(Count(now, was), "one word of a notification's title");
        Assert.Equal(0, Count(was, SnapshotSetup.Png(Toast("Filed in CS 101"))));
    }

    /// <summary>The Lilac token turning teal moved the menu's selected row and the recorder's logo cells, and passed. The
    /// token is read as the screens are built (a record of meanings, not a resource a test can swap), so this does to
    /// the verified dropdown's picture what the change does: every lilac pixel goes teal.</summary>
    [Fact]
    public void A_changed_colour_token_fails()
    {
        var verified = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Snapshots", "SnapshotTests.Menu_bar_dropdown_light.verified.png"));
        var token = StudyStash.App.Controls.Syft.Lilac;
        Assert.Equal("#CDBDFF", $"#{token.R:X2}{token.G:X2}{token.B:X2}"); // if the token changes, so must this test's idea of the pixels
        var lilac = new SKColor(token.R, token.G, token.B);
        var teal = new SKColor(0xA8, 0xE6, 0xD9);
        int recoloured = 0;
        using var bitmap = SKBitmap.Decode(verified);
        for (int y = 0; y < bitmap.Height; y++)
            for (int x = 0; x < bitmap.Width; x++)
            {
                var p = bitmap.GetPixel(x, y);
                if (Math.Abs(p.Red - lilac.Red) > 4 || Math.Abs(p.Green - lilac.Green) > 4 || Math.Abs(p.Blue - lilac.Blue) > 4) continue;
                bitmap.SetPixel(x, y, teal);
                recoloured++;
            }
        Assert.True(recoloured > 1000, $"the dropdown's picture has only {recoloured} lilac pixels: is its selected row still lilac?");
        using var image = SKImage.FromBitmap(bitmap);
        Fails(Count(image.Encode(SKEncodedImageFormat.Png, 100).ToArray(), verified), "the Lilac token made teal");
    }
}
