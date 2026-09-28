using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using StudyStash.App.Platform;
using StudyStash.App.Services;

namespace StudyStash.App;

/// <summary>
/// The self-test's pictures of the screen itself on Windows: what the student sees, as Windows composes it — the
/// window's real title bar and caption buttons, Mica or the colour behind it, its corners and edge — where
/// <see cref="Shot"/> draws only Avalonia's own content. And the looks: the library, Settings, the quick panel and
/// Connect Canvas, light and dark and in another colour theme, each pictured and its ground colour reported.
/// </summary>
public static partial class SelfTest
{
    /// <summary>How much of the desktop round the window the picture keeps: its shadow, and what's behind it.</summary>
    const int ScreenMargin = 24;

    /// <summary>A picture of the screen where <paramref name="w"/> is, as <c>&lt;name&gt;-screen.png</c>, and one line
    /// saying what's behind the window (Mica or the solid colour) and the colours sampled on the sidebar's ground and
    /// on the desktop beside it. Windows only.</summary>
    static void ScreenShot(Window? w, string name)
    {
        if (!OperatingSystem.IsWindows() || w is not { IsVisible: true }) return;
        try
        {
            if (Capture(w) is not { } picture) return;
            using (picture.Bitmap)
            using (var f = File.Create(Path.Combine(Dir!, name + "-screen.png")))
                picture.Bitmap.Save(f, PngBitmapEncoderOptions.Default);
            var (x, y) = picture.Ground;
            Say($"{name} (screen): {WinChrome.Of(w)}, ground {Hex(picture.Pixel(x, y))} at ({x}, {y}), desktop {Hex(picture.Pixel(4, picture.Height / 2))}");
        }
        catch (Exception e)
        {
            Say($"{name} (screen): no picture ({e.GetType().Name}: {e.Message})");
        }
    }

    static string Hex(uint bgra) => $"#{(bgra >> 16) & 0xFF:X2}{(bgra >> 8) & 0xFF:X2}{bgra & 0xFF:X2}";

    sealed record Picture(WriteableBitmap Bitmap, uint[] Pixels, int Width, int Height, (int X, int Y) Ground)
    {
        public uint Pixel(int x, int y) => Pixels[Math.Clamp(y, 0, Height - 1) * Width + Math.Clamp(x, 0, Width - 1)];
    }

    [SupportedOSPlatform("windows")]
    static Picture? Capture(Window w)
    {
        if (w.TryGetPlatformHandle()?.Handle is not IntPtr hwnd || hwnd == IntPtr.Zero) return null;
        // What's drawn on screen: DWM's own bounds, without the window's invisible resize borders.
        if (DwmGetWindowAttribute(hwnd, 9, out RECT r, Marshal.SizeOf<RECT>()) != 0 && !GetWindowRect(hwnd, out r)) return null;
        int left = r.Left - ScreenMargin, top = r.Top - ScreenMargin;
        int width = r.Right - r.Left + 2 * ScreenMargin, height = r.Bottom - r.Top + 2 * ScreenMargin;
        if (width <= 0 || height <= 0) return null;
        IntPtr screen = GetDC(IntPtr.Zero), memory = CreateCompatibleDC(screen), bitmap = CreateCompatibleBitmap(screen, width, height);
        var pixels = new uint[width * height];
        try
        {
            IntPtr old = SelectObject(memory, bitmap);
            BitBlt(memory, 0, 0, width, height, screen, left, top, 0x00CC0020 | 0x40000000); // SRCCOPY | CAPTUREBLT
            SelectObject(memory, old);
            var info = new BITMAPINFOHEADER { Size = Marshal.SizeOf<BITMAPINFOHEADER>(), Width = width, Height = -height, Planes = 1, BitCount = 32 };
            GetDIBits(memory, bitmap, 0, (uint)height, pixels, ref info, 0);
        }
        finally
        {
            DeleteObject(bitmap);
            DeleteDC(memory);
            ReleaseDC(IntPtr.Zero, screen);
        }
        for (int i = 0; i < pixels.Length; i++) pixels[i] |= 0xFF000000;
        var bmp = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
        using (var fb = bmp.Lock())
            for (int row = 0; row < height; row++)
                Marshal.Copy((int[])(object)pixels, row * width, fb.Address + row * fb.RowBytes, width);
        // The sidebar's ground: below its last row, clear of the title bar, in from the window's left edge.
        double scale = w.RenderScaling;
        var ground = (ScreenMargin + (int)(24 * scale), ScreenMargin + (r.Bottom - r.Top) * 3 / 4);
        return new Picture(bmp, pixels, width, height, ground);
    }

    /// <summary>The Windows looks, pictured on the real screen: the library (a lecture open) light, dark, and dark in
    /// Plum; Settings and the quick panel dark; Connect Canvas light. Then back to light and the default theme.</summary>
    static async Task RunLooksAsync()
    {
        if (!OperatingSystem.IsWindows()) return;
        Shell.ShowLibrary();
        await Wait(1.5);
        foreach (var (appearance, theme, name) in new[]
                 {
                     (AppAppearance.Light, ColourThemes.Default, "looks-library-light"),
                     (AppAppearance.Dark, ColourThemes.Default, "looks-library-dark"),
                     (AppAppearance.Dark, ColourThemes.Find("Plum"), "looks-library-plum-dark"),
                     (AppAppearance.Light, ColourThemes.Find("Plum"), "looks-library-plum-light"),
                 })
        {
            Skin.UseTheme(theme);
            Skin.UseAppearance(appearance);
            Shell.Windows.Main?.Activate();
            await Wait(1.5);
            Shot(Shell.Windows.Main, name);
        }
        Skin.UseTheme(ColourThemes.Default);
        Skin.UseAppearance(AppAppearance.Dark);
        Shell.ShowSettings();
        await Wait(1.5);
        Shot(Shell.Windows.Settings, "looks-settings-dark");
        Shell.Windows.Settings?.Close();
        Shell.Windows.ToggleQuick();
        await Wait(1);
        Shot(Shell.Windows.Quick, "looks-quick-dark");
        Shell.Windows.Quick?.Hide();
        Skin.UseAppearance(AppAppearance.Light);
        Shell.ShowCanvasConnect();
        await Wait(1.5);
        Shot(Shell.Windows.CanvasConnect, "looks-canvas-connect-light");
        Shell.Windows.CanvasConnect?.Close();
        // For comparison: the same window on the design's own colour, as Windows 10 (or transparency effects off)
        // shows it, then back to what this Windows has.
        if (Shell.Windows.Main is { } main)
        {
            Shell.ShowLibrary();
            WinChrome.Use(main, WinChrome.Ground.Solid);
            await Wait(1);
            Shot(main, "looks-library-solid-light");
            WinChrome.Use(main, WinChrome.Wanted());
        }
        Skin.UseAppearance(AppAppearance.System);
        await Wait(0.5);
    }

    [StructLayout(LayoutKind.Sequential)]
    struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct BITMAPINFOHEADER
    {
        public int Size, Width, Height;
        public short Planes, BitCount;
        public int Compression, SizeImage, XPelsPerMeter, YPelsPerMeter, ClrUsed, ClrImportant;
    }

    [DllImport("dwmapi.dll")]
    static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out RECT value, int size);

    [DllImport("user32.dll")]
    static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

    [DllImport("user32.dll")]
    static extern IntPtr GetDC(IntPtr hwnd);

    [DllImport("user32.dll")]
    static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);

    [DllImport("gdi32.dll")]
    static extern IntPtr CreateCompatibleDC(IntPtr dc);

    [DllImport("gdi32.dll")]
    static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int width, int height);

    [DllImport("gdi32.dll")]
    static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);

    [DllImport("gdi32.dll")]
    static extern bool BitBlt(IntPtr dest, int x, int y, int width, int height, IntPtr src, int sx, int sy, uint rop);

    [DllImport("gdi32.dll")]
    static extern int GetDIBits(IntPtr dc, IntPtr bitmap, uint start, uint lines, [Out] uint[] bits, ref BITMAPINFOHEADER info, uint usage);

    [DllImport("gdi32.dll")]
    static extern bool DeleteObject(IntPtr obj);

    [DllImport("gdi32.dll")]
    static extern bool DeleteDC(IntPtr dc);
}
