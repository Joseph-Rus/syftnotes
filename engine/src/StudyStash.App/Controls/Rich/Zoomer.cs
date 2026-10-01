using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using StudyStash.App.Platform;

namespace StudyStash.App.Controls.Rich;

/// <summary>
/// Zooms and pans a picture inside the room it's seen in, by moving and scaling it as drawn (its layout never
/// changes, so the page around it doesn't move, and nothing is drawn again while it moves). Zoom 1 is the picture
/// fitted as the page lays it out; it zooms about a point (the pointer's, or the middle), never so far out that it's
/// smaller than fitted nor in past <see cref="Max"/>, and pans only as far as keeps the picture covering its room (a
/// picture smaller than its room stays centred). Each change glides, unless the student asked for less motion.
/// </summary>
sealed class Zoomer
{
    readonly Control target;
    readonly Control room;
    double zoom = 1;
    Vector pan;
    IDisposable? gliding;

    /// <param name="target">The picture: zoomed and panned as drawn.</param>
    /// <param name="room">Where it's seen: the picture itself (in a note, where it keeps its place on the page) or the
    /// panel it's centred in (a window of its own).</param>
    public Zoomer(Control target, Control room)
    {
        this.target = target;
        this.room = room;
        target.RenderTransformOrigin = RelativePoint.TopLeft;
        target.SizeChanged += (_, _) => Settle();
        if (!ReferenceEquals(room, target)) room.SizeChanged += (_, _) => Settle();
    }

    /// <summary>The least it zooms (1: fitted) and the most.</summary>
    public double Min { get; set; } = 1;

    public double Max { get; set; } = 4;

    public double Zoom => zoom;

    /// <summary>Zoomed in past fitted (so a drag pans it).</summary>
    public bool Zoomed => zoom > Min + 1e-3 || zoom > 1 + 1e-3;

    /// <summary>Called whenever the zoom or the pan changes (each frame of a glide too).</summary>
    public event Action? Changed;

    /// <summary>Called once a glide (or a jump) has come to rest.</summary>
    public event Action? Rested;

    /// <summary>Where the picture's own layout puts its top-left in the room.</summary>
    Point Origin => ReferenceEquals(room, target) ? default : target.Bounds.Position;

    Size Room => ReferenceEquals(room, target) ? target.Bounds.Size : room.Bounds.Size;

    /// <summary>A point of the room (as the pointer gives it) in the picture's own units, before zooming.</summary>
    public Point ToPicture(Point inRoom) => new((inRoom.X - Origin.X - pan.X) / zoom, (inRoom.Y - Origin.Y - pan.Y) / zoom);

    /// <summary>A point of the picture (in its own units) where it's seen in the room now.</summary>
    public Point ToRoom(Point inPicture) => new(Origin.X + pan.X + inPicture.X * zoom, Origin.Y + pan.Y + inPicture.Y * zoom);

    /// <summary>Zooms by <paramref name="factor"/> about <paramref name="about"/> (a point of the room; the middle if
    /// none).</summary>
    public void ZoomBy(double factor, Point? about = null, bool glide = true) => ZoomTo(zoom * factor, about, glide);

    public void ZoomTo(double to, Point? about = null, bool glide = true)
    {
        var at = about ?? new Point(Room.Width / 2, Room.Height / 2);
        var picture = ToPicture(at);
        double z = Math.Clamp(to, Min, Max);
        Go(z, new Vector(at.X - Origin.X - picture.X * z, at.Y - Origin.Y - picture.Y * z), glide);
    }

    /// <summary>Moves the picture by <paramref name="delta"/> (the room's pixels), as far as it can go.</summary>
    public void PanBy(Vector delta)
    {
        gliding?.Dispose();
        gliding = null;
        (zoom, pan) = Clamp(zoom, pan + delta);
        Apply();
        Rested?.Invoke();
    }

    /// <summary>Back to fitted.</summary>
    public void Fit(bool glide = true) => Go(1, default, glide);

    /// <summary>Zooms to show <paramref name="area"/> (in the picture's units) as large as the room allows, up to
    /// <paramref name="most"/>, centred, with a margin round it.</summary>
    public void Show(Rect area, double most, bool glide = true)
    {
        var r = Room;
        if (r.Width <= 0 || r.Height <= 0 || area.Width <= 0 || area.Height <= 0) return;
        double z = Math.Clamp(Math.Min(most, Math.Min((r.Width - 48) / area.Width, (r.Height - 48) / area.Height)), Min, Max);
        var c = area.Center;
        Go(z, new Vector(r.Width / 2 - Origin.X - c.X * z, r.Height / 2 - Origin.Y - c.Y * z), glide);
    }

    void Go(double z, Vector to, bool glide)
    {
        (z, to) = Clamp(z, to);
        gliding?.Dispose();
        double z0 = zoom;
        var p0 = pan;
        if (!glide || (Math.Abs(z - z0) < 1e-6 && (to - p0).Length < 0.5))
        {
            zoom = z;
            pan = to;
            Apply();
            Rested?.Invoke();
            return;
        }
        gliding = Motion.Animate(target, TimeSpan.FromMilliseconds(200), t =>
        {
            // Zoom in equal ratios, so the glide feels even; the point being zoomed about stays put on the way.
            zoom = z0 * Math.Pow(z / z0, t);
            pan = p0 + (to - p0) * t;
            Apply();
        }, () =>
        {
            gliding = null;
            Rested?.Invoke();
        });
    }

    /// <summary>The zoom and pan nearest these that keep the picture covering its room (or centred in it, where it's
    /// smaller).</summary>
    (double, Vector) Clamp(double z, Vector p)
    {
        z = Math.Clamp(z, Min, Max);
        var size = target.Bounds.Size;
        var r = Room;
        var o = Origin;
        double Axis(double offset, double origin, double length, double roomLength)
        {
            double drawn = length * z;
            if (drawn <= roomLength + 0.01) return (roomLength - drawn) / 2 - origin;
            return Math.Clamp(offset, roomLength - drawn - origin, -origin);
        }
        return (z, new Vector(Axis(p.X, o.X, size.Width, r.Width), Axis(p.Y, o.Y, size.Height, r.Height)));
    }

    /// <summary>Keeps the zoom and pan sensible after the picture or its room changes size.</summary>
    void Settle()
    {
        if (gliding is not null) return;
        var (z, p) = Clamp(zoom, pan);
        if (Math.Abs(z - zoom) < 1e-9 && (p - pan).Length < 0.01) return;
        (zoom, pan) = (z, p);
        Apply();
    }

    void Apply()
    {
        target.RenderTransform = Math.Abs(zoom - 1) < 1e-9 && pan.Length < 1e-9 ? null : new MatrixTransform(Matrix.CreateScale(zoom, zoom) * Matrix.CreateTranslation(pan));
        Changed?.Invoke();
    }
}
