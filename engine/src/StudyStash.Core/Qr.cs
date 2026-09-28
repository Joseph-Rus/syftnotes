using System.Globalization;
using System.Text;
using QRCoder;

namespace StudyStash.Core;

/// <summary>
/// A QR code of an address, for a phone's camera: "Add a phone" shows one of the phone app's address, in the app's
/// Settings and on the library's page. Always dark squares on white with a quiet margin around them, whatever the
/// theme, since that's what a camera reads.
/// </summary>
public static class Qr
{
    /// <summary>The white margin around the squares, in squares (the standard asks for 4).</summary>
    public const int Margin = 4;

    /// <summary>The code's squares, true for dark, without the margin. Medium error correction: it still reads with
    /// a glare across it.</summary>
    public static bool[,] Modules(string text)
    {
        using var gen = new QRCodeGenerator();
        using var data = gen.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);
        // QRCoder keeps its own 4-square margin in the matrix: take it off, so callers place the margin themselves.
        var rows = data.ModuleMatrix;
        int size = 17 + 4 * data.Version;
        int quiet = (rows.Count - size) / 2;
        var modules = new bool[size, size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                modules[y, x] = rows[y + quiet][x + quiet];
        return modules;
    }

    /// <summary>The dark squares as one SVG path in square units (row by row, each run of squares one rectangle),
    /// offset by the margin.</summary>
    public static string PathData(bool[,] modules)
    {
        var d = new StringBuilder();
        int size = modules.GetLength(0);
        for (int y = 0; y < size; y++)
        {
            int x = 0;
            while (x < size)
            {
                if (!modules[y, x])
                {
                    x++;
                    continue;
                }
                int start = x;
                while (x < size && modules[y, x]) x++;
                d.Append(CultureInfo.InvariantCulture, $"M{start + Margin} {y + Margin}h{x - start}v1h-{x - start}z");
            }
        }
        return d.ToString();
    }

    /// <summary>The code as an SVG that scales without blurring: white, with the dark squares on it.</summary>
    public static string Svg(string text, int pixels = 220, string label = "QR code")
    {
        var modules = Modules(text);
        int n = modules.GetLength(0) + 2 * Margin;
        return $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {n} {n}\" width=\"{pixels}\" height=\"{pixels}\" shape-rendering=\"crispEdges\" role=\"img\" aria-label=\"{Esc(label)}\">"
            + $"<rect width=\"{n}\" height=\"{n}\" fill=\"#fff\"/><path fill=\"#000\" d=\"{PathData(modules)}\"/></svg>";
    }

    static string Esc(string s) => s.Replace("&", "&amp;", StringComparison.Ordinal).Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace("\"", "&quot;", StringComparison.Ordinal);
}
