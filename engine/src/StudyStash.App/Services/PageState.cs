namespace StudyStash.App.Services;

/// <summary>
/// Which page the library window shows, as one value: a new page replaces it whole (<see cref="Next"/>,
/// <see cref="Left"/>), so nothing of the page before is left behind for a page that forgot to clear it.
/// </summary>
internal sealed record PageState
{
    /// <summary>Every page shown takes the next turn: one whose answer comes back after a later page was asked for
    /// stops instead of drawing over it.</summary>
    public int Turn { get; init; }

    /// <summary>The overview showing: "" for Home, Due's own name for Due, a folder's name for its page; null for none
    /// (a note, an assignment or a class's Canvas lists is showing instead).</summary>
    public string? Overview { get; init; }

    /// <summary>The note whose page is open.</summary>
    public string? Lecture { get; init; }

    /// <summary>The class whose Canvas lists are open (Assignments, Modules, Announcements).</summary>
    public string? CanvasClass { get; init; }

    /// <summary>The assignment open (its class and id): on a page of its own, or beside the Canvas list it was picked
    /// from.</summary>
    public (string Class, string Id)? Assignment { get; init; }

    /// <summary>The folder in mind: the one whose page showed last, or whose note is open. It outlives the page (Home,
    /// Due and Study don't name one) until the next page that does.</summary>
    public string? Class { get; init; }

    /// <summary>Nothing open any more, in the same turn: whatever is shown next says what is.</summary>
    public PageState Left() => new() { Turn = Turn, Class = Class };

    /// <summary>A page being shown: the next turn, with nothing of the page before open.</summary>
    public PageState Next() => new() { Turn = Turn + 1, Class = Class };
}
