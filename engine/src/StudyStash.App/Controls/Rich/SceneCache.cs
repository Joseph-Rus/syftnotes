using System.Globalization;
using Avalonia;
using Avalonia.Media;
using StudyStash.Core.Rich;

namespace StudyStash.App.Controls.Rich;

/// <summary>
/// Flowcharts laid out away from the window, one at a time, and kept for the 64 diagrams drawn last (keyed by the
/// chart as written, the font and the direction), so a big chart never holds up the notes it's in, and a note shown
/// again or a theme switched redraws without laying anything out. Scenes hold no colours. A chart that can't be laid
/// out is remembered too, so it's tried once, not on every measure.
/// </summary>
static class SceneCache
{
    const int Capacity = 64;

    /// <summary>A chart's scene for one direction: laid out (with its scene), still being laid out (with the task
    /// that finishes when it is), or given up on.</summary>
    public readonly record struct Lookup(DiagramScene? Scene, Task? Laying)
    {
        public bool Failed => Scene is null && Laying is null;
    }

    sealed record Entry(string Key, DiagramScene? Scene);

    static readonly Dictionary<string, LinkedListNode<Entry>> map = [];
    static readonly LinkedList<Entry> order = new();
    static readonly Dictionary<string, Task<DiagramScene?>> running = [];
    static readonly Lock gate = new();
    // One layout at a time, in the order asked for: MSAGL was never meant to run beside itself, and a note's charts
    // arrive one after another anyway.
    static Task queue = Task.CompletedTask;
    // The app the scenes were measured in. There's only ever one, except in tests, where each has its own: a layout
    // that outlives its test's app is dropped, never kept for the next (its fonts are gone with it).
    static Application? app;

    /// <summary>For a test: runs just before each layout, on the thread laying it out — to hold one back (and show
    /// that nothing waits for it) or to make one fail.</summary>
    internal static Action<Flowchart>? Laying { get; set; }

    /// <summary>Where a chart that couldn't be laid out is noted: the app's log (a test keeps its own).</summary>
    internal static Action<string> Log { get; set; } = Program.Log;

    /// <summary>What the cache knows of <paramref name="chart"/> (written as <paramref name="source"/>, its
    /// <see cref="Flowchart.ToSource"/>) in <paramref name="family"/> and <paramref name="direction"/> (null: its own),
    /// starting its layout in the background when nothing is known yet. Never lays anything out itself.</summary>
    public static Lookup Find(Flowchart chart, string source, FontFamily family, ChartDirection? direction)
    {
        string key = Key(source, family, direction);
        var owner = Application.Current;
        lock (gate)
        {
            if (!ReferenceEquals(app, owner))
            {
                map.Clear();
                order.Clear();
                running.Clear();
                app = owner;
            }
            if (map.TryGetValue(key, out var hit))
            {
                order.Remove(hit);
                order.AddFirst(hit);
                return new Lookup(hit.Value.Scene, null);
            }
            if (running.TryGetValue(key, out var busy)) return new Lookup(null, busy);
            var measure = Measurer(family);
            var job = queue.ContinueWith(_ => Lay(key, chart, measure, direction, owner), CancellationToken.None,
                TaskContinuationOptions.None, TaskScheduler.Default);
            queue = job;
            running[key] = job;
            return new Lookup(null, job);
        }
    }

    /// <summary>The chart's scene, waiting for it if it's being laid out: for a download, which runs away from the
    /// window already. Null when it couldn't be laid out. Never call this on the UI thread.</summary>
    public static DiagramScene? Laid(Flowchart chart, FontFamily family, ChartDirection? direction = null)
    {
        var found = Find(chart, chart.ToSource(), family, direction);
        return found.Laying is Task<DiagramScene?> job ? job.GetAwaiter().GetResult() : found.Scene;
    }

    /// <summary>Whether any chart is still being laid out.</summary>
    public static bool Busy
    {
        get
        {
            lock (gate) return running.Count > 0;
        }
    }

    /// <summary>Waits (up to <paramref name="timeout"/>) until no chart is being laid out; false if one still is.</summary>
    public static bool Settle(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            Task pending;
            lock (gate)
            {
                if (running.Count == 0) return true;
                pending = queue;
            }
            var left = deadline - DateTime.UtcNow;
            if (left <= TimeSpan.Zero || !pending.Wait(left)) return false;
        }
    }

    static DiagramScene? Lay(string key, Flowchart chart, Func<string, double, bool, double> measure, ChartDirection? direction, Application? owner)
    {
        bool Stale()
        {
            lock (gate) return !ReferenceEquals(app, owner);
        }
        if (Stale()) return null;
        DiagramScene? scene = null;
        try
        {
            Laying?.Invoke(chart);
            scene = DiagramLayout.Lay(chart, measure, direction);
        }
        catch (Exception e) // a chart that can't be laid out shows the calm card; it must never take the app down
        {
            if (!Stale()) Log($"[diagram] couldn't lay out a chart: {e.GetType().Name}: {e.Message}");
        }
        lock (gate)
        {
            if (!ReferenceEquals(app, owner)) return scene;
            running.Remove(key);
            map[key] = order.AddFirst(new Entry(key, scene));
            while (order.Count > Capacity)
            {
                map.Remove(order.Last!.Value.Key);
                order.RemoveLast();
            }
        }
        return scene;
    }

    static string Key(string source, FontFamily family, ChartDirection? direction) => $"{family.Name}\u0001{direction}\u0001{source}";

    /// <summary>
    /// Text widths in the look's own font: box words in its medium weight, group titles (12 px) in semibold, the
    /// words on arrows in regular. Safe away from the UI thread: Avalonia shapes text on any thread.
    /// </summary>
    public static Func<string, double, bool, double> Measurer(FontFamily family) => (text, size, bold) =>
    {
        var weight = !bold ? FontWeight.Normal : size <= DiagramLayout.TitleSize ? FontWeight.SemiBold : FontWeight.Medium;
        return new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(family, FontStyle.Normal, weight), size, null)
            .WidthIncludingTrailingWhitespace;
    };
}
