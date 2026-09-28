using System.Xml.Linq;

namespace StudyStash.Core.Tests;

/// <summary>The QR code "Add a phone" shows: a whole code (its three corner squares in place), crisp at any size, and
/// always dark on white.</summary>
public class QrTests
{
    const string Url = "https://mini.tail1234.ts.net:8443/app/";

    static bool Finder(bool[,] m, int top, int left)
    {
        for (int y = 0; y < 7; y++)
            for (int x = 0; x < 7; x++)
            {
                bool ring = y is 0 or 6 || x is 0 or 6, middle = y is >= 2 and <= 4 && x is >= 2 and <= 4;
                if (m[top + y, left + x] != (ring || middle)) return false;
            }
        return true;
    }

    [Fact]
    public void The_code_has_its_three_corner_squares_and_no_margin_of_its_own()
    {
        var m = Qr.Modules(Url);
        int n = m.GetLength(0);
        Assert.Equal(n, m.GetLength(1));
        Assert.Equal(0, (n - 17) % 4);
        Assert.True(Finder(m, 0, 0));
        Assert.True(Finder(m, 0, n - 7));
        Assert.True(Finder(m, n - 7, 0));
        Assert.False(Finder(m, n - 7, n - 7));
    }

    [Fact]
    public void Another_address_is_another_code()
    {
        var a = Qr.Modules(Url);
        var b = Qr.Modules("https://other.tail1234.ts.net:8443/app/");
        Assert.NotEqual(Qr.PathData(a), Qr.PathData(b));
    }

    [Fact]
    public void The_picture_is_dark_squares_on_white_with_a_margin()
    {
        string svg = Qr.Svg(Url, 200, "Scan with your phone's camera");
        var doc = XDocument.Parse(svg);
        XNamespace ns = "http://www.w3.org/2000/svg";
        int n = Qr.Modules(Url).GetLength(0) + 2 * Qr.Margin;
        Assert.Equal($"0 0 {n} {n}", doc.Root!.Attribute("viewBox")!.Value);
        Assert.Equal("crispEdges", doc.Root.Attribute("shape-rendering")!.Value);
        Assert.Equal("#fff", doc.Root.Element(ns + "rect")!.Attribute("fill")!.Value);
        var path = doc.Root.Element(ns + "path")!;
        Assert.Equal("#000", path.Attribute("fill")!.Value);
        Assert.StartsWith($"M{Qr.Margin} {Qr.Margin}h7v1h-7z", path.Attribute("d")!.Value);
        Assert.Equal("Scan with your phone's camera", doc.Root.Attribute("aria-label")!.Value);
    }
}
