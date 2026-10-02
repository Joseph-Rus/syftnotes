using SkiaSharp;
using StudyStash.Core.Rich;

namespace StudyStash.App.Controls.Rich;

/// <summary>
/// Which part of an illustration is where: the drawing drawn again a part at a time (and each part's label with it),
/// in the order it's drawn, onto a map at about a pixel a unit, so the part on top at any point is the one there, its
/// real outline (not a box round it) — a thin tendon is a thin tendon. Each part's box comes with it. Made away from
/// the window, once a drawing arrives; never throws (a part that can't be drawn just isn't on the map).
/// </summary>
sealed class PartMap
{
    /// <summary>How near (in the drawing's units, at its own size) the pointer must be to a part to be on it.</summary>
    public const double Slop = 5;

    readonly short[] owner;
    readonly int width, height;
    readonly double scale;
    readonly string[] ids;
    readonly Bounds?[] boxes;

    PartMap(short[] owner, int width, int height, double scale, string[] ids, Bounds?[] boxes)
    {
        this.owner = owner;
        this.width = width;
        this.height = height;
        this.scale = scale;
        this.ids = ids;
        this.boxes = boxes;
    }

    /// <summary>The part at a point of the drawing (its own units), or the nearest within <paramref name="slop"/>.</summary>
    public string? At(double x, double y, double slop = Slop)
    {
        int px = (int)Math.Floor(x * scale), py = (int)Math.Floor(y * scale);
        if (Owner(px, py) is int hit) return ids[hit];
        int reach = (int)Math.Ceiling(slop * scale);
        int? best = null;
        double nearest = double.MaxValue;
        for (int dy = -reach; dy <= reach; dy++)
            for (int dx = -reach; dx <= reach; dx++)
            {
                double d = dx * dx + dy * dy;
                if (d > reach * reach || d >= nearest || Owner(px + dx, py + dy) is not int o) continue;
                nearest = d;
                best = o;
            }
        return best is int b ? ids[b] : null;
    }

    int? Owner(int x, int y) => x < 0 || y < 0 || x >= width || y >= height || owner[y * width + x] == 0 ? null : owner[y * width + x] - 1;

    /// <summary>Where a part is drawn (its label not counted), in the drawing's own units.</summary>
    public Bounds? Of(string id) => Array.IndexOf(ids, id) is int i and >= 0 ? boxes[i] : null;

    /// <summary>The map of <paramref name="cleaned"/> (a drawing <paramref name="w"/> by <paramref name="h"/> units)
    /// for the parts named <paramref name="parts"/>.</summary>
    public static PartMap Make(string cleaned, IReadOnlyList<string> parts, double w, double h)
    {
        double scale = Math.Min(1.5, 900 / Math.Max(w, h));
        int width = Math.Max(1, (int)Math.Ceiling(w * scale)), height = Math.Max(1, (int)Math.Ceiling(h * scale));
        var owner = new short[width * height];
        var boxes = new Bounds?[parts.Count];
        using var mask = new SKBitmap(new SKImageInfo(width, height, SKColorType.Alpha8, SKAlphaType.Premul));
        void Stamp(string svg, int index, bool box)
        {
            using var picture = SvgPictures.Draw(svg);
            if (picture is null) return;
            mask.Erase(SKColors.Transparent);
            using (var canvas = new SKCanvas(mask))
            {
                canvas.Scale((float)scale);
                canvas.DrawPicture(picture.Picture);
            }
            var pixels = mask.GetPixelSpan();
            int x0 = int.MaxValue, y0 = int.MaxValue, x1 = -1, y1 = -1;
            for (int y = 0; y < height; y++)
            {
                int row = y * width;
                for (int x = 0; x < width; x++)
                {
                    if (pixels[row + x] < 48) continue;
                    owner[row + x] = (short)(index + 1);
                    if (!box) continue;
                    if (x < x0) x0 = x;
                    if (x > x1) x1 = x;
                    if (y < y0) y0 = y;
                    if (y > y1) y1 = y;
                }
            }
            if (box && x1 >= 0) boxes[index] = new Bounds(x0 / scale, y0 / scale, (x1 - x0 + 1) / scale, (y1 - y0 + 1) / scale);
        }
        for (int i = 0; i < parts.Count && i < short.MaxValue - 1; i++)
        {
            try
            {
                Stamp(Illustration.Part(cleaned, parts[i]), i, box: true);
            }
            catch (Exception)
            {
                // A part that can't be drawn alone isn't on the map.
            }
        }
        // Labels last, on top: a click on a part's name is a click on the part.
        for (int i = 0; i < parts.Count && i < short.MaxValue - 1; i++)
        {
            try
            {
                Stamp(Illustration.CalloutOf(cleaned, parts[i]), i, box: false);
            }
            catch (Exception)
            {
            }
        }
        return new PartMap(owner, width, height, scale, [.. parts], boxes);
    }
}
