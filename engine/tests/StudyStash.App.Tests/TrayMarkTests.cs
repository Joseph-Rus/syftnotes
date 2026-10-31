using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using StudyStash.App.Platform;

namespace StudyStash.App.Tests;

/// <summary>The pixel s in the menu bar and the tray (and the app icon files it comes with): one ink, its top-right cell
/// red only while recording, and every icon file the installers and the library's pages need.</summary>
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
    public void Recording_turns_the_top_right_cell_red_a_square_in_its_own_slot()
    {
        foreach (int size in new[] { 36, 32 })
        {
            var px = Pixels(TrayMark.Png(size, Brushes.White, recording: true));
            var cells = TrayMark.CellRects(size);
            var red = cells[TrayMark.RecordingCell];
            Assert.True(red.Center.X > size / 2 && red.Center.Y < size / 2, "the red cell is the s's top right");
            // The whole square red, its corners too (a cell, not a dot), and nothing red outside it.
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    Assert.True(Reddish(px[x, y]) == red.Contains(new Point(x + 0.5, y + 0.5)), $"{size} px at {x},{y}: {px[x, y]}");
            // Every other cell of the s is still there, in the ink, and the slot round the red one is clear as before.
            for (int i = 0; i < cells.Length; i++)
            {
                if (i == TrayMark.RecordingCell) continue;
                var c = px[(int)cells[i].Center.X, (int)cells[i].Center.Y];
                Assert.True(c.A == 255 && c.R > 240 && c.G > 240 && c.B > 240, $"cell p{i} isn't white ink: {c}");
            }
            Assert.True(px[(int)red.X - 1, (int)red.Bottom].A == 0, "the gap beside the red cell is clear");
        }
        Assert.DoesNotContain(All(Pixels(TrayMark.Png(32, Brushes.Black, recording: false))), Reddish);
    }

    [AvaloniaFact]
    public void The_menu_bar_has_a_one_x_picture_on_whole_pixels_too()
    {
        var idle = Pixels(TrayMark.OneX(TrayMark.Png(36, Brushes.Black, recording: false))!);
        Assert.Equal(18, idle.GetLength(0));
        // Cells two pixels square on whole pixels (a 12 × 14 pixel letter): no half-covered pixels anywhere.
        Assert.All(All(idle), c => Assert.True(c.A is 0 or 255, $"a blurred pixel: {c}"));
        Assert.Equal(24 * 4, All(idle).Count(c => c.A == 255));
        var recording = Pixels(TrayMark.OneX(TrayMark.Png(36, Brushes.White, recording: true))!);
        Assert.Contains(All(recording), Reddish);
        Assert.Contains(All(recording), c => c.A == 255 && c.R > 240 && c.G > 240 && c.B > 240);
        Assert.Null(TrayMark.OneX(TrayMark.Png(32, Brushes.Black, recording: false)));
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
        // The marks make_art.swift drew are the cells TrayMark draws.
        foreach (int size in new[] { 32, 36 })
        {
            using var file = AssetLoader.Open(new Uri($"avares://StudyStash/Assets/mark-{size}.png"));
            using var copy = new MemoryStream();
            file.CopyTo(copy);
            var shipped = Pixels(copy.ToArray());
            var drawn = Pixels(TrayMark.Png(size, Brushes.Black, recording: false));
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    Assert.True(shipped[x, y].A > 127 == drawn[x, y].A > 127, $"mark-{size}.png differs from TrayMark at {x},{y}");
        }
    }

    /// <summary>With STUDYSTASH_SHOTS set, the icons as they're drawn (Mac idle and recording at 2× and 1×, Windows
    /// light and dark, idle and recording), to look at beside the design.</summary>
    [AvaloniaFact]
    public void Shots_of_the_marks()
    {
        if (Environment.GetEnvironmentVariable("STUDYSTASH_SHOTS") is not { Length: > 0 } dir) return;
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "tray-mac-idle.png"), TrayMark.Png(36, Brushes.Black, false));
        File.WriteAllBytes(Path.Combine(dir, "tray-mac-recording-dark-bar.png"), TrayMark.Png(36, Brushes.White, true));
        File.WriteAllBytes(Path.Combine(dir, "tray-mac-recording-light-bar.png"), TrayMark.Png(36, Brushes.Black, true));
        File.WriteAllBytes(Path.Combine(dir, "tray-mac-idle-1x.png"), TrayMark.OneX(TrayMark.Png(36, Brushes.Black, false))!);
        File.WriteAllBytes(Path.Combine(dir, "tray-mac-recording-dark-bar-1x.png"), TrayMark.OneX(TrayMark.Png(36, Brushes.White, true))!);
        File.WriteAllBytes(Path.Combine(dir, "tray-win-dark.png"), TrayMark.Png(32, Brushes.White, false));
        File.WriteAllBytes(Path.Combine(dir, "tray-win-light.png"), TrayMark.Png(32, Brushes.Black, false));
        File.WriteAllBytes(Path.Combine(dir, "tray-win-dark-recording.png"), TrayMark.Png(32, Brushes.White, true));
        File.WriteAllBytes(Path.Combine(dir, "tray-win-light-recording.png"), TrayMark.Png(32, Brushes.Black, true));
    }

    [AvaloniaFact]
    public void The_tray_menu_offers_record_wherever_this_computer_records()
    {
        static List<string> Items(Services.AppRole role) =>
            [.. Shell.TrayMenu(role).Items.OfType<Avalonia.Controls.NativeMenuItem>().Select(i => i.Header ?? "")];
        Assert.Equal("Record", Items(Services.AppRole.Both)[0]);
        Assert.Equal("Record", Items(Services.AppRole.Laptop)[0]);
        Assert.DoesNotContain("Record", Items(Services.AppRole.Library));
        Assert.Equal("Quit Syft Notes", Items(Services.AppRole.Both)[^1]);
    }
}
