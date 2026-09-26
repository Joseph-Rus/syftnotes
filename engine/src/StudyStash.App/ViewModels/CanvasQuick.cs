using Avalonia.Media;
using StudyStash.App.Services;

namespace StudyStash.App.ViewModels;

/// <summary>The dropdown's "Next due" line (design 12): the soonest item that isn't overdue, or nothing at all when
/// there's none to show.</summary>
public sealed record NextDueModel(string Text, Action Open);

/// <summary>
/// Turns Canvas's due work into rows the quick panel already knows how to draw (<see cref="QuickRow"/>) and decides
/// whether a typed query is even about Canvas. The panel itself (<c>Views/{Mac,Win}Quick.axaml</c>) and its model
/// (<c>QuickModel</c>) belong to another lane; everything here is a pure function over data the host already has —
/// nothing fetches, nothing keeps state.
/// </summary>
public static class CanvasQuick
{
    static readonly string[] Triggers = ["due", "canvas", "assignment", "homework", "sync"];

    /// <summary>The dropdown's line for the soonest not-overdue item, or null when nothing's due.</summary>
    public static NextDueModel? NextDue(CanvasApi.DueResponse? due, TimeZoneInfo zone, DateTimeOffset now, Action<CanvasApi.Item> open)
    {
        if (due?.Next is not { } next) return null;
        return new NextDueModel(CanvasWords.DropdownNextDue(next, zone, now), () => open(next));
    }

    /// <summary>Every item still to hand in, in the Due list's own order (Overdue, then This week, then Later —
    /// never Handed in).</summary>
    static IEnumerable<CanvasApi.Item> ToHandIn(CanvasApi.DueResponse due) => due.Groups.Where(g => g.Key != "handed_in").SelectMany(g => g.Items);

    /// <summary>Whether Canvas has anything worth showing for this query: one of its own trigger words ("due",
    /// "canvas", "assignment", "homework", "sync"), typed as far as it goes, or a word from a due item's own name or
    /// class.</summary>
    public static bool Matches(CanvasApi.DueResponse? due, string query)
    {
        string q = query.Trim();
        if (q.Length == 0) return false;
        if (Triggers.Any(t => t.StartsWith(q, StringComparison.OrdinalIgnoreCase))) return true;
        return due is not null && ToHandIn(due).Any(i =>
            i.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || i.Class.Contains(q, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Rows for the quick panel: a "Due" header and one row per item still to hand in — filtered to whatever the
    /// query names when it isn't one of the bare trigger words, so typing "quiz" narrows to just that assignment —
    /// then an "Actions" header with Sync Canvas (its meta the last sync time) and Open Due. Nothing at all when
    /// there's no due data yet.
    /// </summary>
    public static IReadOnlyList<QuickRow> Rows(CanvasApi.DueResponse? due, CanvasApi.State? state, string query, TimeZoneInfo zone, DateTimeOffset now,
        Func<string, IBrush> dotOf, Action<CanvasApi.Item> open, Action sync, Action openDue)
    {
        var rows = new List<QuickRow>();
        if (due is not null)
        {
            string q = query.Trim();
            bool bare = Triggers.Any(t => t.StartsWith(q, StringComparison.OrdinalIgnoreCase));
            var items = ToHandIn(due).Where(i => bare || q.Length == 0
                || i.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || i.Class.Contains(q, StringComparison.OrdinalIgnoreCase));
            rows.Add(new QuickRow { Kind = QuickKind.Header, Title = "Due", First = true });
            foreach (var item in items)
                rows.Add(new QuickRow
                {
                    Kind = QuickKind.Lecture,
                    Title = item.Name,
                    Meta = CanvasWords.QuickDueLine(item, zone, now),
                    Dot = dotOf(item.Class),
                    ClassName = item.Class,
                    Run = () => open(item),
                });
        }
        rows.Add(new QuickRow { Kind = QuickKind.Header, Title = "Actions", First = rows.Count == 0 });
        rows.Add(new QuickRow { Kind = QuickKind.Action, Title = "Sync Canvas", Meta = CanvasWords.QuickLastSyncLine(state?.LastSync, zone), Glyph = "sync", Run = sync });
        rows.Add(new QuickRow { Kind = QuickKind.Action, Title = "Open Due", Glyph = "event_upcoming", Run = openDue });
        return rows;
    }
}
