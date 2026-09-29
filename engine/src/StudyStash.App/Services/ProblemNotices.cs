using StudyStash.App.ViewModels;

namespace StudyStash.App.Services;

/// <summary>
/// Which of the problems showing (<see cref="Problems.All"/>) to say as a notification, and when: one that stops
/// lectures being written down or filed until the student acts (<see cref="NoticeWords.WorthNotifying"/>), once it has
/// lasted <see cref="Settles"/> (a library restarting, or a state that flickers while the library is asked again,
/// never pops up a notification that's gone a moment later), and not again within <see cref="Again"/> if it comes and
/// goes.
/// </summary>
public sealed class ProblemNotices
{
    public static readonly TimeSpan Settles = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan Again = TimeSpan.FromMinutes(10);

    HashSet<ProblemKind> showing = [];
    readonly Dictionary<ProblemKind, (AppProblem Problem, DateTime Since)> starting = [];
    readonly Dictionary<ProblemKind, DateTime> said = [];

    /// <summary>The problem is still there (a notification about it stays while it is).</summary>
    public bool Showing(ProblemKind kind) => showing.Contains(kind);

    /// <summary>The problems there are now (at <paramref name="at"/>): ones that just started are noted.</summary>
    public void Seen(IReadOnlyList<AppProblem> problems, DateTime at)
    {
        var before = showing;
        showing = [.. problems.Select(p => p.Kind)];
        foreach (var kind in starting.Keys.Where(k => !showing.Contains(k)).ToList()) starting.Remove(kind);
        foreach (var p in problems.Where(p => NoticeWords.WorthNotifying(p.Kind)))
        {
            if (starting.TryGetValue(p.Kind, out var s)) starting[p.Kind] = (p, s.Since);
            else if (!before.Contains(p.Kind)) starting[p.Kind] = (p, at);
        }
    }

    /// <summary>The problems to say now: each once it has lasted <see cref="Settles"/>. <paramref name="quiet"/>
    /// (setup is showing them itself) lets them go unsaid.</summary>
    public IReadOnlyList<AppProblem> Due(DateTime at, bool quiet)
    {
        var due = new List<AppProblem>();
        foreach (var (kind, (p, since)) in starting.ToList())
        {
            if (at - since < Settles) continue;
            starting.Remove(kind);
            if (quiet || (said.TryGetValue(kind, out var last) && at - last < Again)) continue;
            said[kind] = at;
            due.Add(p);
        }
        return due;
    }
}
