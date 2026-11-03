using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Avalonia.Styling;
using Avalonia.Threading;
using SkiaSharp;
using StudyStash.App.Controls;
using StudyStash.App.Services;
using StudyStash.App.ViewModels;
using StudyStash.App.Views;
using StudyStash.Core;

namespace StudyStash.App.Tests;

/// <summary>
/// The design's desktop (renderVals' WALL_L and WALL_D): a wash from top to bottom with three soft patches of colour
/// over it, blue at the bottom left, violet at the top right and a paler one in the middle. Drawn with Skia directly
/// so the gradients are dithered as Chrome's are: undithered steps come out as rings once the glass saturates them.
/// </summary>
sealed class WallpaperPicture(bool dark) : Control
{
    public override void Render(DrawingContext context) => context.Custom(new Paint(new Rect(Bounds.Size), dark));

    sealed class Paint(Rect rect, bool dark) : ICustomDrawOperation
    {
        public Rect Bounds => rect;

        public bool HitTest(Point p) => false;

        public bool Equals(ICustomDrawOperation? other) => false;

        public void Dispose() { }

        public void Render(ImmediateDrawingContext context)
        {
            if (context.TryGetFeature<ISkiaSharpApiLeaseFeature>() is not { } feature) return;
            using var lease = feature.Lease();
            var canvas = lease.SkCanvas;
            float w = (float)rect.Width, h = (float)rect.Height;
            using var paint = new SKPaint { IsDither = true };
            void Fill(SKShader shader)
            {
                using (shader)
                {
                    paint.Shader = shader;
                    canvas.DrawRect(0, 0, w, h, paint);
                    paint.Shader = null;
                }
            }
            // CSS lists the top layer first; here the bottom one comes first.
            Fill(dark ? Wash(w, h, (0.2, 0.04, 250), (0.14, 0.05, 275)) : Wash(w, h, (0.9, 0.04, 240), (0.82, 0.08, 265)));
            (double X, double Y, double Rx, double Ry, double Fade, Color Colour)[] patches = dark
                ?
                [
                    (0.60, 0.60, 0.6, 0.6, 0.7, Oklch.ToColor(0.3, 0.08, 230)),
                    (0.95, 0.00, 0.8, 0.7, 0.6, Oklch.ToColor(0.34, 0.12, 300)),
                    (0.15, 1.10, 0.9, 0.8, 0.6, Oklch.ToColor(0.38, 0.14, 260)),
                ]
                :
                [
                    (0.60, 0.60, 0.6, 0.6, 0.7, Oklch.ToColor(0.86, 0.07, 220)),
                    (0.95, 0.00, 0.8, 0.7, 0.6, Oklch.ToColor(0.8, 0.1, 300)),
                    (0.15, 1.10, 0.9, 0.8, 0.6, Oklch.ToColor(0.72, 0.13, 255)),
                ];
            foreach (var (x, y, rx, ry, fade, colour) in patches) Fill(Patch(w, h, x, y, rx, ry, colour, fade));
        }

        /// <summary>
        /// CSS <c>linear-gradient(160deg, from, to)</c> over a w × h box: the line runs at 160° through the centre, long
        /// enough that the corners get the end colours. oklch() colours mix in Oklab in CSS, so the stops between are
        /// worked out there.
        /// </summary>
        static SKShader Wash(float w, float h, (double L, double C, double H) from, (double L, double C, double H) to)
        {
            double angle = 160 * Math.PI / 180, dx = Math.Sin(angle), dy = -Math.Cos(angle);
            double half = (Math.Abs(w * dx) + Math.Abs(h * dy)) / 2;
            static (double L, double A, double B) Lab((double L, double C, double H) c) =>
                (c.L, c.C * Math.Cos(c.H * Math.PI / 180), c.C * Math.Sin(c.H * Math.PI / 180));
            var (a, b) = (Lab(from), Lab(to));
            const int steps = 8;
            var colours = new SKColor[steps + 1];
            var at = new float[steps + 1];
            for (int i = 0; i <= steps; i++)
            {
                double t = (double)i / steps, l = a.L + (b.L - a.L) * t, x = a.A + (b.A - a.A) * t, y = a.B + (b.B - a.B) * t;
                colours[i] = Sk(Oklch.ToColor(l, Math.Sqrt(x * x + y * y), Math.Atan2(y, x) * 180 / Math.PI));
                at[i] = (float)t;
            }
            return SKShader.CreateLinearGradient(new SKPoint((float)(w / 2 - dx * half), (float)(h / 2 - dy * half)),
                new SKPoint((float)(w / 2 + dx * half), (float)(h / 2 + dy * half)), colours, at, SKShaderTileMode.Clamp);
        }

        /// <summary>
        /// CSS <c>radial-gradient(rx ry at x y, colour 0%, transparent fade)</c>, all relative to the box: a unit circle
        /// stretched to the ellipse. It fades to the same colour with no alpha (as CSS mixes toward transparent), not
        /// to transparent black, which would grey it.
        /// </summary>
        static SKShader Patch(float w, float h, double x, double y, double rx, double ry, Color colour, double fade) =>
            SKShader.CreateRadialGradient(new SKPoint(0, 0), 1, [Sk(colour), Sk(colour).WithAlpha(0)], [0, (float)fade], SKShaderTileMode.Clamp,
                SKMatrix.CreateScale((float)(rx * w), (float)(ry * h)).PostConcat(SKMatrix.CreateTranslation((float)(x * w), (float)(y * h))));

        static SKColor Sk(Color c) => new(c.R, c.G, c.B, c.A);
    }
}
