using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace StudyStash.App.Platform;

/// <summary>
/// The menu bar's and the tray's icon: the monochrome "S." (Assets/mark-&lt;px&gt;.png, drawn by macos/make_icon.swift
/// with the app icon) in one ink, and while recording a small red dot at its top right, set off from the S by a thin
/// clear ring the way the system's own badges are.
/// </summary>
public static class TrayMark
{
    /// <summary>The recording dot: the system's recording red, the one colour the mark ever has.</summary>
    public static readonly Color Red = Color.Parse("#E5484D");

    /// <summary>The icon as PNG bytes, <paramref name="size"/> pixels square (36 for the Mac's 18 pt menu bar at 2×, 32
    /// for the Windows tray), the mark in <paramref name="ink"/>.</summary>
    public static byte[] Png(int size, IBrush ink, bool recording)
    {
        using var mark = new Bitmap(AssetLoader.Open(new Uri($"avares://StudyStash/Assets/mark-{size}.png")));
        var bmp = new RenderTargetBitmap(new PixelSize(size, size), new Vector(96, 96));
        using (var ctx = bmp.CreateDrawingContext())
        {
            var all = new Rect(0, 0, size, size);
            var (centre, r) = Badge(size);
            Geometry clip = recording
                ? new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(all), new EllipseGeometry(new Rect(centre.X - r * 1.55, centre.Y - r * 1.55, r * 3.1, r * 3.1)))
                : new RectangleGeometry(all);
            using (ctx.PushGeometryClip(clip))
            using (ctx.PushOpacityMask(new ImageBrush(mark) { Stretch = Stretch.Fill }, all))
                ctx.FillRectangle(ink, all);
            if (recording) ctx.DrawEllipse(new SolidColorBrush(Red), null, centre, r, r);
        }
        using var stream = new MemoryStream();
        bmp.Save(stream, PngBitmapEncoderOptions.Default);
        return stream.ToArray();
    }

    /// <summary>Where the recording dot sits in a <paramref name="size"/>-pixel icon: its centre and radius.</summary>
    public static (Point Centre, double Radius) Badge(int size)
    {
        double r = size * 0.13;
        return (new Point(size - r - size * 0.03, r + size * 0.03), r);
    }
}
