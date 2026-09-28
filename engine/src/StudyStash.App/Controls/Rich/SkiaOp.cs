using Avalonia;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using SkiaSharp;

namespace StudyStash.App.Controls.Rich;

/// <summary>
/// Draws with Skia itself, in its place among Avalonia's drawing (a picture from an SVG, typeset maths). Whatever
/// <paramref name="hold"/> keeps alive (a cached picture) stays alive until the renderer is done with this drawing.
/// </summary>
sealed class SkiaOp(Rect bounds, Action<SKCanvas> draw, IDisposable? hold = null) : ICustomDrawOperation
{
    public Rect Bounds => bounds;

    public bool HitTest(Point p) => false;

    public bool Equals(ICustomDrawOperation? other) => false;

    public void Dispose() => hold?.Dispose();

    public void Render(ImmediateDrawingContext context)
    {
        if (context.TryGetFeature<ISkiaSharpApiLeaseFeature>() is not { } feature) return;
        using var lease = feature.Lease();
        var canvas = lease.SkCanvas;
        int saved = canvas.Save();
        try
        {
            draw(canvas);
        }
        finally
        {
            canvas.RestoreToCount(saved);
        }
    }
}
