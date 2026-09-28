using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using StudyStash.App.Platform;

namespace StudyStash.App.Tests;

/// <summary>The "S." in the menu bar and the tray (and the app icon files it comes with): one ink, a red dot only while
/// recording, and every icon file the installers and the library's pages need.</summary>
public class TrayMarkTests
{
    static Color[,] Pixels(byte[] png)
    {
        using var decoded = new Bitmap(new MemoryStream(png));
        int w = decoded.PixelSize.Width, h = decoded.PixelSize.Height;
        var bytes = new byte[w * h * 4];
        nint buffer = System.Runtime.InteropServices.Marshal.AllocHGlobal(bytes.Length);
        try
        {
            decoded.CopyPixels(new PixelRect(0, 0, w, h), buffer, bytes.Length, w * 4);
            System.Runtime.InteropServices.Marshal.Copy(buffer, bytes, 0, bytes.Length);
        }
        finally
        {
            System.Runtime.InteropServices.Marshal.FreeHGlobal(buffer);
        }
        bool rgba = decoded.Format == Avalonia.Platform.PixelFormats.Rgba8888;
        var result = new Color[w, h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = (y * w + x) * 4;
                result[x, y] = rgba ? Color.FromArgb(bytes[i + 3], bytes[i], bytes[i + 1], bytes[i + 2]) : Color.FromArgb(bytes[i + 3], bytes[i + 2], bytes[i + 1], bytes[i]);
            }
        return result;
    }

    static IEnumerable<Color> All(Color[,] px)
    {
        foreach (var c in px) yield return c;
    }

    static bool Reddish(Color c) => c.A > 200 && c.R > 180 && c.G < 120 && c.B < 120;

    [AvaloniaFact]
    public void Idle_is_the_mark_alone_in_one_ink()
    {
        var px = Pixels(TrayMark.Png(36, Brushes.Black, recording: false));

        Assert.Equal(36, px.GetLength(0));
        Assert.Equal(0, px[0, 0].A); // clear round the mark
        Assert.Contains(All(px), c => c.A == 255);
        Assert.All(All(px).Where(c => c.A > 0), c => Assert.True(c.R < 30 && c.G < 30 && c.B < 30, $"not black ink: {c}"));
    }

    [AvaloniaFact]
    public void Recording_adds_a_red_dot_at_the_top_right_set_off_by_a_clear_ring()
    {
        var px = Pixels(TrayMark.Png(36, Brushes.White, recording: true));
        var (centre, r) = TrayMark.Badge(36);

        Assert.True(Reddish(px[(int)centre.X, (int)centre.Y]), $"no red dot at {centre}: {px[(int)centre.X, (int)centre.Y]}");
        Assert.True((int)centre.X > 18 && (int)centre.Y < 18, "the dot sits at the top right");
        // Just outside the dot, in the ring: nothing drawn (the mark is cut back there).
        int ringX = (int)Math.Round(centre.X - r * 1.3), ringY = (int)Math.Round(centre.Y + r * 1.3);
        Assert.True(px[ringX, ringY].A < 40, $"the ring isn't clear: {px[ringX, ringY]}");
        Assert.Contains(All(px), c => c.A == 255 && c.R > 240 && c.G > 240 && c.B > 240); // the mark, white
        Assert.DoesNotContain(All(Pixels(TrayMark.Png(32, Brushes.Black, recording: false))), Reddish);
    }

    static string Repo([System.Runtime.CompilerServices.CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", ".."));

    [Fact]
    public void The_windows_icon_has_every_size_windows_asks_for()
    {
        byte[] ico = File.ReadAllBytes(Path.Combine(Repo(), "assets", "study-stash.ico"));
        Assert.Equal([0, 0, 1, 0], ico[..4]);
        int count = BitConverter.ToUInt16(ico, 4);
        var sizes = Enumerable.Range(0, count).Select(i => ico[6 + 16 * i] is 0 ? 256 : ico[6 + 16 * i]).Order().ToArray();
        Assert.Equal([16, 20, 24, 32, 40, 48, 64, 128, 256], sizes);
    }

    [AvaloniaFact]
    public void The_app_ships_its_icon_and_both_marks()
    {
        foreach (var (name, size) in new[] { ("icon.png", 256), ("mark-36.png", 36), ("mark-32.png", 32) })
        {
            using var bmp = new Bitmap(AssetLoader.Open(new Uri($"avares://StudyStash/Assets/{name}")));
            Assert.Equal(new PixelSize(size, size), bmp.PixelSize);
        }
    }

    /// <summary>With STUDYSTASH_SHOTS set, the four icons as they're drawn (Mac idle and recording, Windows light and
    /// dark), to look at beside the design.</summary>
    [AvaloniaFact]
    public void Shots_of_the_marks()
    {
        if (Environment.GetEnvironmentVariable("STUDYSTASH_SHOTS") is not { Length: > 0 } dir) return;
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "tray-mac-idle.png"), TrayMark.Png(36, Brushes.Black, false));
        File.WriteAllBytes(Path.Combine(dir, "tray-mac-recording-dark-bar.png"), TrayMark.Png(36, Brushes.White, true));
        File.WriteAllBytes(Path.Combine(dir, "tray-mac-recording-light-bar.png"), TrayMark.Png(36, Brushes.Black, true));
        File.WriteAllBytes(Path.Combine(dir, "tray-win-dark.png"), TrayMark.Png(32, Brushes.White, false));
    }

    [AvaloniaFact]
    public void The_tray_menu_offers_record_wherever_this_computer_records()
    {
        static List<string> Items(Services.AppRole role) =>
            [.. Shell.TrayMenu(role).Items.OfType<Avalonia.Controls.NativeMenuItem>().Select(i => i.Header ?? "")];
        Assert.Equal("Record", Items(Services.AppRole.Both)[0]);
        Assert.Equal("Record", Items(Services.AppRole.Laptop)[0]);
        Assert.DoesNotContain("Record", Items(Services.AppRole.Library));
        Assert.Equal("Quit Study Stash", Items(Services.AppRole.Both)[^1]);
    }
}
