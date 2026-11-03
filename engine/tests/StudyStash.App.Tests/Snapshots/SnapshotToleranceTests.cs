using SkiaSharp;
using VerifyTests;

namespace StudyStash.App.Tests.Snapshots;

/// <summary>The picture check is only worth having if a real change fails it and a rendering whisper doesn't. Runs
/// everywhere (it only reads a verified picture): a single row of pixels changed fails, a few pixels one shade off
/// (what another graphics driver does to a letter's edge) passes.</summary>
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

    [Fact]
    public void The_same_picture_is_the_same()
    {
        Assert.True(Ssim.Compare(Verified(), Verified()) >= SnapshotSetup.Similarity);
    }

    [Fact]
    public void One_row_of_pixels_changed_fails()
    {
        var changed = Changed(b =>
        {
            for (int x = 0; x < b.Width; x++) b.SetPixel(x, 500, SKColors.Black);
        });
        double score = Ssim.Compare(changed, Verified());
        Assert.True(score < SnapshotSetup.Similarity, $"a row of black pixels scored {score:0.0000}, and {SnapshotSetup.Similarity} passes");
    }

    [Fact]
    public void A_few_edge_pixels_a_shade_off_pass()
    {
        var changed = Changed(b =>
        {
            for (int i = 0; i < 400; i++)
            {
                int x = 340 + i % 200, y = 100 + i / 200 * 3;
                var p = b.GetPixel(x, y);
                b.SetPixel(x, y, new SKColor((byte)Math.Max(0, p.Red - 3), (byte)Math.Max(0, p.Green - 3), (byte)Math.Max(0, p.Blue - 3), p.Alpha));
            }
        });
        double score = Ssim.Compare(changed, Verified());
        Assert.True(score >= SnapshotSetup.Similarity, $"a few pixels three shades off scored {score:0.0000}, and {SnapshotSetup.Similarity} is needed");
    }
}
