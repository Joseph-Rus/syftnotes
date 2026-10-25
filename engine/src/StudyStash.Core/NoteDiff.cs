namespace StudyStash.Core;

/// <summary>How a line of one version of some notes stands against another: the same in both, only in the version
/// (it would come back), or only in the notes now (it would go).</summary>
public enum DiffKind { Same, Added, Removed, Gap }

/// <summary>One line of a comparison of two versions of some notes.</summary>
public sealed record DiffLine(DiffKind Kind, string Text);

/// <summary>
/// What putting a version of a lecture's notes back would change, line by line (History's preview): the lines it
/// would bring back, the lines it would take away, and a little of what's the same round them, the rest folded into a
/// gap. A line-by-line longest common subsequence: notes are a few hundred lines at most.
/// </summary>
public static class NoteDiff
{
    /// <summary>Lines kept round each change; longer runs of unchanged lines fold into one gap.</summary>
    public const int Context = 2;

    /// <summary>The lines of <paramref name="version"/> against <paramref name="now"/>: Added for lines only in the
    /// version, Removed for lines only in the notes now. Blank lines don't count as changes.</summary>
    public static List<DiffLine> Lines(string now, string version)
    {
        var a = Split(now);
        var b = Split(version);
        // Longest common subsequence, from the end, then walked from the start.
        var lcs = new int[a.Length + 1, b.Length + 1];
        for (int i = a.Length - 1; i >= 0; i--)
            for (int j = b.Length - 1; j >= 0; j--)
                lcs[i, j] = a[i] == b[j] ? lcs[i + 1, j + 1] + 1 : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
        var all = new List<DiffLine>();
        int x = 0, y = 0;
        while (x < a.Length || y < b.Length)
        {
            if (x < a.Length && y < b.Length && a[x] == b[y]) { all.Add(new DiffLine(DiffKind.Same, a[x])); x++; y++; }
            else if (y < b.Length && (x == a.Length || lcs[x, y + 1] >= lcs[x + 1, y])) all.Add(new DiffLine(b[y].Length == 0 ? DiffKind.Same : DiffKind.Added, b[y++]));
            else all.Add(new DiffLine(a[x].Length == 0 ? DiffKind.Same : DiffKind.Removed, a[x++]));
        }
        // A little of what's the same round each change; the rest folds into a gap.
        var near = new bool[all.Count];
        for (int i = 0; i < all.Count; i++)
            if (all[i].Kind != DiffKind.Same)
                for (int k = Math.Max(0, i - Context); k <= Math.Min(all.Count - 1, i + Context); k++) near[k] = true;
        var shown = new List<DiffLine>();
        for (int i = 0; i < all.Count; i++)
        {
            if (near[i]) shown.Add(all[i]);
            else if (shown.Count == 0 || shown[^1].Kind != DiffKind.Gap) shown.Add(new DiffLine(DiffKind.Gap, "…"));
        }
        // Folding the start and end away says nothing: only gaps between changes are kept.
        while (shown.Count > 0 && shown[^1].Kind == DiffKind.Gap) shown.RemoveAt(shown.Count - 1);
        return shown.All(l => l.Kind is DiffKind.Same or DiffKind.Gap) ? [] : shown;
    }

    static string[] Split(string text) => Py.Strip(text.ReplaceLineEndings("\n")).Split('\n').Select(l => l.TrimEnd()).ToArray();
}
