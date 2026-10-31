namespace StudyStash.App.Controls;

/// <summary>
/// CSS keyframes, worked out in C#: the motion reference (syft-motion.html) is plain SVG + CSS animations, and the
/// splash and loaders copy its keyframes, easings, durations and delays exactly. A keyframe gives a transform
/// (translate, rotate, scale: any it leaves out are none) and/or an opacity; as in a browser, each of the two runs
/// between only the keyframes that give it, from the element's own value (none, opacity 1) at 0% and 100% when those
/// don't, eased across each stretch by the animation's timing function.
/// </summary>
public static class Keyframes
{
    /// <summary>Where an element is at one moment: moved, turned about its own centre, grown, and how opaque.</summary>
    public readonly record struct Pose(double X, double Y, double Turn, double Scale, double Opacity)
    {
        public static readonly Pose Rest = new(0, 0, 0, 1, 1);
    }

    /// <summary>One keyframe: at <paramref name="At"/> (0–1), a transform if <paramref name="Moves"/>, an opacity if
    /// <paramref name="Opacity"/> has one.</summary>
    public readonly record struct Key(double At, bool Moves, double X, double Y, double Turn, double Scale, double? Opacity);

    /// <summary>A keyframe with a transform (and maybe an opacity): <c>translate(x, y) rotate(turn) scale(scale)</c>.</summary>
    public static Key T(double at, double x = 0, double y = 0, double turn = 0, double scale = 1, double? opacity = null) =>
        new(at, true, x, y, turn, scale, opacity);

    /// <summary>A keyframe with only an opacity.</summary>
    public static Key O(double at, double opacity) => new(at, false, 0, 0, 0, 1, opacity);

    /// <summary>The same keyframe at several offsets (CSS's <c>0%, 60%, 100% { … }</c>).</summary>
    public static IEnumerable<Key> At(Key key, params double[] offsets) => offsets.Select(o => key with { At = o });

    /// <summary>A CSS timing function: cubic-bezier(x1, y1, x2, y2), solved for x as browsers do.</summary>
    public sealed class Ease(double x1, double y1, double x2, double y2)
    {
        public static readonly Ease Linear = new(0, 0, 1, 1);
        public static readonly Ease Default = new(0.25, 0.1, 0.25, 1); // "ease"
        public static readonly Ease Out = new(0, 0, 0.58, 1); // "ease-out"
        public static readonly Ease InOut = new(0.42, 0, 0.58, 1); // "ease-in-out"

        public double Apply(double t)
        {
            if (t <= 0) return 0;
            if (t >= 1) return 1;
            if (x1 == y1 && x2 == y2) return t;
            // Newton's method on x(s) = t, then bisection if it wanders.
            double s = t;
            for (int i = 0; i < 8; i++)
            {
                double x = Bezier(s, x1, x2) - t;
                if (Math.Abs(x) < 1e-6) return Bezier(s, y1, y2);
                double d = Slope(s, x1, x2);
                if (Math.Abs(d) < 1e-6) break;
                s -= x / d;
            }
            double lo = 0, hi = 1;
            s = t;
            for (int i = 0; i < 40; i++)
            {
                double x = Bezier(s, x1, x2);
                if (Math.Abs(x - t) < 1e-7) break;
                if (x < t) lo = s;
                else hi = s;
                s = (lo + hi) / 2;
            }
            return Bezier(s, y1, y2);
        }

        static double Bezier(double s, double p1, double p2) => ((1 - 3 * p2 + 3 * p1) * s + (3 * p2 - 6 * p1)) * s * s + 3 * p1 * s;
        static double Slope(double s, double p1, double p2) => 3 * (1 - 3 * p2 + 3 * p1) * s * s + 2 * (3 * p2 - 6 * p1) * s + 3 * p1;
    }

    /// <summary>
    /// One CSS animation on one element: its keyframes, duration, timing function and delay, and whether a moment in
    /// its delay shows the first keyframe (<c>animation-fill-mode: backwards</c>) or the element as it is. It loops, or
    /// plays once and holds at <see cref="Hold"/> (the splash plays its "in" part once: the keyframes up to the
    /// moment the s is together and the wordmark is up, and stays there).
    /// </summary>
    public sealed class Anim
    {
        readonly (double At, Pose Value)[] moves, fades;

        public double Duration { get; }
        public double Delay { get; init; }
        public Ease Ease { get; }
        public bool Backwards { get; init; }
        /// <summary>Null: loops for ever. Otherwise plays once, up to this offset (0–1), and stays.</summary>
        public double? Hold { get; init; }

        public Anim(double duration, Ease ease, IEnumerable<Key> keys)
        {
            Duration = duration;
            Ease = ease;
            var all = keys.OrderBy(k => k.At).ToList();
            moves = Track(all.Where(k => k.Moves).Select(k => (k.At, new Pose(k.X, k.Y, k.Turn, k.Scale, 1))));
            fades = Track(all.Where(k => k.Opacity is not null).Select(k => (k.At, Pose.Rest with { Opacity = k.Opacity!.Value })));
        }

        public Anim(double duration, Ease ease, params Key[] keys) : this(duration, ease, (IEnumerable<Key>)keys)
        {
        }

        /// <summary>A property's keyframes, with the element's own value at 0% and 100% where none is given.</summary>
        static (double, Pose)[] Track(IEnumerable<(double At, Pose Value)> given)
        {
            var list = given.ToList();
            if (list.Count == 0 || list[0].At > 0) list.Insert(0, (0, Pose.Rest));
            if (list[^1].At < 1) list.Add((1, Pose.Rest));
            return [.. list];
        }

        /// <summary>A copy that starts <paramref name="delay"/> seconds in.</summary>
        public Anim After(double delay) => new(this) { Delay = delay, Backwards = Backwards, Hold = Hold };

        /// <summary>A copy that plays once and holds at <paramref name="offset"/>.</summary>
        public Anim Once(double offset = 1) => new(this) { Delay = Delay, Backwards = Backwards, Hold = offset };

        Anim(Anim from)
        {
            moves = from.moves;
            fades = from.fades;
            Duration = from.Duration;
            Ease = from.Ease;
        }

        /// <summary>Where the element is <paramref name="seconds"/> after the animation began (its delay included).</summary>
        public Pose At(double seconds)
        {
            double local = seconds - Delay;
            if (local < 0) return Backwards ? Sample(0) : Pose.Rest;
            double p = local / Duration;
            if (Hold is { } hold) p = Math.Min(p, hold);
            else p -= Math.Floor(p);
            return Sample(p);
        }

        /// <summary>The pose at offset <paramref name="p"/> (0–1).</summary>
        public Pose Sample(double p)
        {
            var m = Between(moves, p);
            var f = Between(fades, p);
            return m with { Opacity = f.Opacity };
        }

        Pose Between((double At, Pose Value)[] track, double p)
        {
            int i = 1;
            while (i < track.Length - 1 && track[i].At <= p) i++;
            var (a, from) = track[i - 1];
            var (b, to) = track[i];
            if (p <= a) return from;
            if (p >= b) return to;
            double e = Ease.Apply((p - a) / (b - a));
            return new Pose(Mix(from.X, to.X, e), Mix(from.Y, to.Y, e), Mix(from.Turn, to.Turn, e), Mix(from.Scale, to.Scale, e), Mix(from.Opacity, to.Opacity, e));
        }

        static double Mix(double a, double b, double e) => a + (b - a) * e;
    }
}
