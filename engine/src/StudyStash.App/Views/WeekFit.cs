using Avalonia.Controls;

namespace StudyStash.App.Views;

/// <summary>Home's week is as wide as the page gives it, its days sharing that width; where the page is too narrow for
/// eight columns to read (below <see cref="Least"/>, the design's own width: a day then holds "9:00 ● Recorded" on
/// one line and a class's name without breaking a word) the week keeps that width and scrolls sideways instead.</summary>
public static class WeekFit
{
    public const double Least = 900;

    public static void Keep(ScrollViewer room, Control week)
    {
        void Fit() => week.Width = Math.Max(Least, room.Bounds.Width - week.Margin.Left - week.Margin.Right);
        room.SizeChanged += (_, _) => Fit();
        Fit();
    }
}
