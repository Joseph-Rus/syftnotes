using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace StudyStash.App.Platform;

/// <summary>
/// The menu bar's and the tray's icon: Syft Notes' pixel s in one ink, every cell on whole pixels (the same cells as
/// Assets/mark-&lt;px&gt;.png, which tools/syft/make_art.swift draws), and while recording the s's top-right cell (p1, its
/// odd one out) turned the recording red: a square like the rest, in its own slot, where a round dot used to sit.
/// </summary>
public static class TrayMark
{
    /// <summary>The recording cell's red: the system's recording red, the one colour the mark ever has.</summary>
    public static readonly Color Red = Color.Parse("#E5484D");

    /// <summary>The s's 24 cells as (column, row) on its 6 × 7 grid, in path order p0 → p23 (Joey's syft-motion.html).</summary>
    static readonly (int Col, int Row)[] Cells =
    [
        (5, 1), (5, 0), (4, 0), (3, 0), (2, 0), (1, 0), (0, 1), (1, 1), (0, 2), (1, 2), (1, 3), (2, 3),
        (3, 3), (4, 3), (4, 4), (5, 4), (4, 5), (5, 5), (4, 6), (3, 6), (2, 6), (1, 6), (0, 6), (0, 5),
    ];

    /// <summary>Pixels from one cell to the next and each cell's side in a <paramref name="size"/>-pixel icon: 36 (the
    /// Mac's 18 pt menu bar at 2×) 5 and 4, so the s is 34 px tall; 18 (at 1×) 2 and 2, the cells touching, a 12 × 14
    /// pixel letter (2½-px steps can't land on whole pixels evenly, and 3 would be too tall); 32 (the Windows tray) 4
    /// and 3. make_art.swift's markGrid is the same table.</summary>
    public static (double Pitch, int Cell) Grid(int size)
    {
        switch (size)
        {
            case 18: return (2, 2);
            case 32: return (4, 3);
            case 36: return (5, 4);
        }
        double pitch = Math.Max(1, Math.Floor(size / 7.2));
        int cell = (int)Math.Max(1, Math.Round(pitch * 0.8, MidpointRounding.AwayFromZero));
        if (cell >= pitch && pitch > 1) cell = (int)pitch - 1;
        return (pitch, cell);
    }

    static double Step(int i, double pitch) => Math.Floor(i * pitch + 0.5);

    /// <summary>Each cell's square in a <paramref name="size"/>-pixel icon, p0 first: the s centred, on whole pixels.</summary>
    public static Rect[] CellRects(int size)
    {
        var (pitch, cell) = Grid(size);
        double w = Step(5, pitch) + cell, h = Step(6, pitch) + cell;
        double x0 = Math.Floor((size - w) / 2), y0 = Math.Floor((size - h) / 2);
        return [.. Cells.Select(c => new Rect(x0 + Step(c.Col, pitch), y0 + Step(c.Row, pitch), cell, cell))];
    }

    /// <summary>The icon as PNG bytes, <paramref name="size"/> pixels square (36 for the Mac's 18 pt menu bar at 2×, 18
    /// at 1×, 32 for the Windows tray), the s in <paramref name="ink"/>, its top-right cell red while
    /// <paramref name="recording"/>.</summary>
    public static byte[] Png(int size, IBrush ink, bool recording)
    {
        var cells = CellRects(size);
        var bmp = new RenderTargetBitmap(new PixelSize(size, size), new Vector(96, 96));
        using (var ctx = bmp.CreateDrawingContext())
        {
            var red = new SolidColorBrush(Red);
            for (int i = 0; i < cells.Length; i++)
                ctx.FillRectangle(recording && i == RecordingCell ? red : ink, cells[i]);
        }
        using var stream = new MemoryStream();
        bmp.Save(stream, PngBitmapEncoderOptions.Default);
        return stream.ToArray();
    }

    /// <summary>The cell that turns red while recording: p1, the s's top-right corner.</summary>
    public const int RecordingCell = 1;

    /// <summary>The 1× picture of a 36-pixel (2×) icon: the same ink and the same red cell, drawn again at 18 pixels so the
    /// cells land on whole pixels there too instead of being scaled to a blur. Null for any other picture.</summary>
    public static byte[]? OneX(byte[] png)
    {
        using var decoded = new Bitmap(new MemoryStream(png));
        if (decoded.PixelSize != new PixelSize(36, 36)) return null;
        var bytes = new byte[36 * 36 * 4];
        unsafe
        {
            fixed (byte* p = bytes) decoded.CopyPixels(new PixelRect(0, 0, 36, 36), (nint)p, bytes.Length, 36 * 4);
        }
        bool rgba = decoded.Format == Avalonia.Platform.PixelFormats.Rgba8888;
        bool recording = false;
        Color? ink = null;
        for (int i = 0; i < bytes.Length; i += 4)
        {
            if (bytes[i + 3] < 250) continue;
            byte red = rgba ? bytes[i] : bytes[i + 2], blue = rgba ? bytes[i + 2] : bytes[i];
            var c = Color.FromRgb(red, bytes[i + 1], blue);
            if (c.R > 180 && c.G < 120 && c.B < 120) recording = true;
            else ink ??= c;
        }
        return Png(18, new SolidColorBrush(ink ?? Colors.Black), recording);
    }
}
