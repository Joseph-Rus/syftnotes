namespace StudyStash.App.ViewModels.Kit;

/// <summary>
/// Where a page is scrolled to, so Back and Forward can bring the student to the same place. The page's view keeps
/// <see cref="Y"/> up to date as they scroll (<see cref="Controls.ScrollKeeper"/>); <see cref="PutBack"/> asks the view to
/// scroll there again.
/// </summary>
public sealed class ScrollPlace
{
    /// <summary>How far down the page is scrolled, in points.</summary>
    public double Y { get; set; }

    /// <summary>The page is to be scrolled to a place.</summary>
    public event Action<double>? Asked;

    /// <summary>Scrolls the page to <paramref name="y"/>, as far as it reaches and then as it grows (its rows and diagrams
    /// arrive after it opens), unless the student scrolls first.</summary>
    public void PutBack(double y)
    {
        Y = y;
        Asked?.Invoke(y);
    }
}
