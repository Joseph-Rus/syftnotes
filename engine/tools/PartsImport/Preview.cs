using System.Text;
using SkiaSharp;

namespace PartsImport;

/// <summary>Pictures for looking at parts while choosing and checking them: one SVG as a PNG, or a contact sheet.</summary>
static class Preview
{
    public static SKPicture? Picture(string svg)
    {
        try
        {
            var s = new Svg.Skia.SKSvg();
            using var ms = new MemoryStream(Encoding.UTF8.GetBytes(svg));
            s.Load(ms);
            return s.Picture;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static void Png(string svg, string path, float scale = 2, SKColor? ground = null)
    {
        var pic = Picture(svg);
        if (pic is null) { Console.WriteLine($"  can't render {path}"); return; }
        var b = pic.CullRect;
        using var bmp = new SKBitmap((int)Math.Ceiling(b.Width * scale) + 2, (int)Math.Ceiling(b.Height * scale) + 2);
        using var c = new SKCanvas(bmp);
        c.Clear(ground ?? SKColors.White);
        c.Scale(scale);
        c.Translate(-b.Left, -b.Top);
        c.DrawPicture(pic);
        Save(bmp, path);
    }

    static void Save(SKBitmap bmp, string path)
    {
        using var img = SKImage.FromBitmap(bmp);
        using var data = img.Encode(SKEncodedImageFormat.Png, 90);
        File.WriteAllBytes(path, data.ToArray());
    }

    /// <summary>A grid of drawings, each fitted in its cell with its caption under it.</summary>
    public static void Sheet(IReadOnlyList<(string Caption, string Svg)> items, string path, int cell = 260, int columns = 5, SKColor? ground = null)
    {
        int rows = (items.Count + columns - 1) / columns;
        using var bmp = new SKBitmap(columns * cell, rows * (cell + 24));
        using var c = new SKCanvas(bmp);
        c.Clear(ground ?? SKColors.White);
        using var font = new SKFont(SKTypeface.FromFamilyName("Helvetica Neue"), 12);
        using var ink = new SKPaint { Color = ground is null ? SKColors.Black : SKColors.White, IsAntialias = true };
        using var frame = new SKPaint { Color = new SKColor(0xDD, 0xDD, 0xDD), Style = SKPaintStyle.Stroke };
        for (int i = 0; i < items.Count; i++)
        {
            int x = i % columns * cell, y = i / columns * (cell + 24);
            c.DrawRect(x + 2, y + 2, cell - 4, cell - 4, frame);
            var pic = Picture(items[i].Svg);
            if (pic is not null && pic.CullRect.Width > 0 && pic.CullRect.Height > 0)
            {
                var b = pic.CullRect;
                float s = Math.Min((cell - 12) / b.Width, (cell - 12) / b.Height);
                c.Save();
                c.Translate(x + 6 + (cell - 12 - b.Width * s) / 2, y + 6 + (cell - 12 - b.Height * s) / 2);
                c.Scale(s);
                c.Translate(-b.Left, -b.Top);
                c.DrawPicture(pic);
                c.Restore();
            }
            string caption = items[i].Caption.Length > 40 ? items[i].Caption[..40] : items[i].Caption;
            c.DrawText(caption, x + 4, y + cell + 14, font, ink);
        }
        Save(bmp, path);
    }
}
