using Avalonia;
using Avalonia.Controls.Shapes;
using Avalonia.Media;

namespace StudyStash.App.Controls;

/// <summary>
/// One of the pixel s's cells as a shape, where the app once had a round dot: the recording dot (in the bar, the
/// sidebar, the recorder, the dropdown), Start now's, a recorded meeting's. A rounded square with the mark's corners
/// (1.8 of a 6.6 cell, about 27 % of its side), filled or drawn as an outline like an <see cref="Ellipse"/>:
/// <c>&lt;c:SyftCell Width="10" Height="10" Fill="{DynamicResource Record}"/&gt;</c>.
/// </summary>
public class SyftCell : Shape
{
    /// <summary>A cell's corner, of its side.</summary>
    public const double Round = Syft.Corner / Syft.Cell;

    static SyftCell() => AffectsGeometry<SyftCell>(BoundsProperty, StrokeThicknessProperty);

    protected override Geometry CreateDefiningGeometry()
    {
        // The outside edge keeps the cell's corner; an outline's line runs half its width inside it.
        var outer = new Rect(Bounds.Size);
        double half = StrokeThickness / 2, corner = Math.Min(outer.Width, outer.Height) * Round;
        return new RectangleGeometry(outer.Deflate(half), Math.Max(0, corner - half), Math.Max(0, corner - half));
    }

    protected override Size MeasureOverride(Size availableSize) => new(StrokeThickness, StrokeThickness);
}
