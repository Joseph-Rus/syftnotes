using StudyStash.Core.Rich;

namespace StudyStash.Core;

/// <summary>A slide (its index in the deck's pages) and the moment of the lecture it was taught at, or null when that
/// isn't clear enough to say.</summary>
public sealed record SlideTime(int Slide, TimeSpan? At);

/// <summary>
/// Which moment of a lecture each of its slides was taught at, found from the words alone. The transcript is read in
/// minute-long windows; a slide scores, in each window, the share of its words heard there, its rarer words counting for
/// more. The slides are then laid on the windows in their order (a slide may be skipped), and a slide is given a time
/// only when its words were clearly said there and nowhere else.
/// </summary>
public static class SlideTimes
{
    /// <summary>Seconds of transcript a window holds, and how far each window starts after the one before.</summary>
    const double Window = 60, Step = 15;

    /// <summary>A word said in more than 1 / this of the lecture's windows counts for less, and for nothing in all of them.</summary>
    const double Spread = 4;

    /// <summary>Back-to-back pages sharing this much of their words are one slide built up (a Beamer build-up).</summary>
    const double SamePage = 0.8;

    /// <summary>A slide needs this many words that tell it apart: each weighing at least <see cref="RealWeight"/> of a
    /// word found on one slide only and said in under a quarter of the lecture (the most a word can weigh).</summary>
    const int FewestWords = 3;
    const double RealWeight = 0.25;

    /// <summary>A slide needs this share of its words heard in its window.</summary>
    const double LeastScore = 0.35;

    /// <summary>More than this far (seconds) from its spot (its window, and the windows either side it keeps scoring
    /// in), no window may score above <see cref="Elsewhere"/> of its score.</summary>
    const double Near = 120;
    const double Elsewhere = 0.7;

    /// <summary>The best window for a slide, order aside, may beat its own by no more than this.</summary>
    const double OrderCost = 1.2;

    /// <summary>One <see cref="SlideTime"/> per page, in the pages' order.</summary>
    public static IReadOnlyList<SlideTime> Match(IReadOnlyList<string> pages, IReadOnlyList<Spoken> spoken)
    {
        var words = pages.Select(p => DiagramMoment.Stems(p).ToHashSet()).ToList();
        var groups = Groups(words);
        var at = new TimeSpan?[pages.Count];
        var lines = spoken.Where(s => s.Text.Length > 0).OrderBy(s => s.Start).ToList();
        var worded = groups.Where(g => g.Words.Count > 0).ToList();
        if (lines.Count > 0 && worded.Count > 0)
        {
            foreach (var (group, time) in Place(worded, lines))
                foreach (int page in group.Pages) at[page] = time;
        }
        return [.. at.Select((t, i) => new SlideTime(i, t))];
    }

    sealed record Group(List<int> Pages, HashSet<string> Words);

    /// <summary>The pages, with back-to-back near copies (a slide built up a line at a time) joined into one. Each page is
    /// compared with its group's first page, so a short page between two slides (a caption) can't chain them together.
    /// The words on more than half the pages (a course name, a footer) are left out of the comparison.</summary>
    static List<Group> Groups(List<HashSet<string>> words)
    {
        var common = words.SelectMany(w => w).GroupBy(w => w).Where(g => g.Count() * 2 > words.Count).Select(g => g.Key).ToHashSet();
        var groups = new List<Group>();
        HashSet<string>? start = null;
        for (int i = 0; i < words.Count; i++)
        {
            var own = words[i].Except(common).ToHashSet();
            if (start is not null && own.Count > 0 && start.Count > 0
                && (double)own.Count(start.Contains) / Math.Min(own.Count, start.Count) >= SamePage)
            {
                groups[^1].Pages.Add(i);
                groups[^1].Words.UnionWith(words[i]);
            }
            else
            {
                groups.Add(new Group([i], [.. words[i]]));
                start = own;
            }
        }
        return groups;
    }

    static IEnumerable<(Group, TimeSpan)> Place(List<Group> groups, List<Spoken> lines)
    {
        int n = groups.Count;
        int windows = (int)Math.Floor(lines[^1].Start / Step) + 1;
        var text = lines.Select(l => DiagramMoment.Spaced(l.Text)).ToList();

        // Each word: on how many slides it is, and which windows heard it.
        var heard = new Dictionary<string, bool[]>();
        var weight = new Dictionary<string, double>();
        foreach (var stem in groups.SelectMany(g => g.Words).Distinct())
        {
            var inWindow = new bool[windows];
            int windowsWith = 0;
            for (int l = 0; l < lines.Count; l++)
            {
                if (!text[l].Contains(" " + stem, StringComparison.Ordinal)) continue;
                for (int w = First(lines[l].Start); w <= Last(lines[l].Start, windows); w++)
                    if (!inWindow[w]) { inWindow[w] = true; windowsWith++; }
            }
            heard[stem] = inWindow;
            // On every slide, or said all lecture long, a word says nothing about where a slide was taught. Said in up
            // to a quarter of the lecture it counts in full (a slide can be taught for a while), and less from there.
            // A deck of one slide (or one that joins into one) has nothing to tell its slide apart from, so is never placed.
            double onSlides = Math.Log((double)n / groups.Count(g => g.Words.Contains(stem)));
            double inLecture = Math.Clamp(Math.Log((double)windows / Math.Max(windowsWith, 1)) / Math.Log(Spread), 0, 1);
            weight[stem] = onSlides * inLecture;
        }

        // Each slide's score in each window: the share of its weighted words heard there.
        var score = new double[n, windows];
        var distinct = new List<string>[n];
        var telling = new int[n];
        for (int g = 0; g < n; g++)
        {
            distinct[g] = [.. groups[g].Words.Where(s => weight[s] > 0)];
            telling[g] = distinct[g].Count(s => weight[s] >= RealWeight * Math.Log(n));
            double whole = distinct[g].Sum(s => weight[s]);
            if (whole == 0) continue;
            foreach (var stem in distinct[g])
                for (int w = 0; w < windows; w++)
                    if (heard[stem][w]) score[g, w] += weight[stem] / whole;
        }

        // The slides laid on the windows in order: each one at the same window as the one before or later, or skipped.
        // A score too low to be placed counts as nothing, so it can't push the others about.
        var best = new double[n + 1, windows + 1];
        var take = new bool[n + 1, windows + 1];
        for (int g = 1; g <= n; g++)
            for (int w = 1; w <= windows; w++)
            {
                double s = score[g - 1, w - 1] >= LeastScore ? score[g - 1, w - 1] : 0;
                double taken = best[g - 1, w] + s;
                take[g, w] = s > 0 && taken > best[g, w - 1];
                best[g, w] = Math.Max(taken, best[g, w - 1]);
            }
        var chosen = new int?[n];
        for (int g = n, w = windows; g > 0 && w > 0;)
        {
            if (take[g, w]) chosen[--g] = w - 1;
            else if (best[g, w] == best[g, w - 1]) w--;
            else g--;
        }

        // Windows overlap, so a slide's first line can come before the previous slide's: a time is never earlier than that.
        var after = TimeSpan.Zero;
        for (int g = 0; g < n; g++)
        {
            if (chosen[g] is not int at || telling[g] < FewestWords) continue;
            double own = score[g, at];
            // Its spot: the windows around its own that it keeps scoring in (a slide can be taught for minutes on end).
            int first = at, last = at;
            while (first > 0 && score[g, first - 1] > Elsewhere * own) first--;
            while (last < windows - 1 && score[g, last + 1] > Elsewhere * own) last++;
            double elsewhere = 0, anywhere = 0;
            int top = at;
            for (int w = 0; w < windows; w++)
            {
                if ((first - w) * Step > Near || (w - last) * Step > Near) elsewhere = Math.Max(elsewhere, score[g, w]);
                if (score[g, w] > anywhere) (anywhere, top) = (score[g, w], w);
            }
            if (elsewhere > Elsewhere * own || (top != at && anywhere > OrderCost * own)) continue;
            // The moment: the window's first line that says one of the slide's words.
            double from = at * Step;
            int line = Enumerable.Range(0, lines.Count).First(l => lines[l].Start >= from && lines[l].Start < from + Window
                && distinct[g].Any(s => text[l].Contains(" " + s, StringComparison.Ordinal)));
            after = TimeSpan.FromSeconds(Math.Max(lines[line].Start, after.TotalSeconds));
            yield return (groups[g], after);
        }
    }

    /// <summary>The first and last window a line starting at <paramref name="start"/> falls in.</summary>
    static int First(double start) => Math.Max(0, (int)Math.Floor((start - Window) / Step) + 1);
    static int Last(double start, int windows) => Math.Min(windows - 1, (int)Math.Floor(start / Step));
}
