using Avalonia.Controls;
using Avalonia.VisualTree;

namespace StudyStash.App.Views;

/// <summary>
/// Setup's Canvas step: the course list takes the room the page has left under the steps above it, so a whole term
/// of courses scrolls inside the list and the page itself doesn't scroll (or only as far as it must on a small
/// screen, where the list keeps a few rows). Continue, in the footer under the page, is always in view.
/// </summary>
public static class PickerRoom
{
    /// <summary>The fewest pixels the list gets however small the window: about three courses.</summary>
    public const double Least = 144;

    /// <summary>Keeps the course list (the ScrollViewer named PickerScroll inside <paramref name="page"/>) as tall
    /// as the page's room allows, after every layout.</summary>
    public static void Follow(ScrollViewer page)
    {
        ScrollViewer? list = null;
        page.LayoutUpdated += (_, _) =>
        {
            if (list is null || !list.IsAttachedToVisualTree())
                list = page.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault(s => s.Name == "PickerScroll");
            if (list is null || !list.IsEffectivelyVisible || page.Viewport.Height <= 0) return;
            // Everything on the page but the list, then what's left of the page's height for it.
            double rest = page.Extent.Height - list.Bounds.Height;
            double room = Math.Max(Least, Math.Floor(page.Viewport.Height - rest));
            if (Math.Abs(list.MaxHeight - room) > 0.5) list.MaxHeight = room;
        };
    }
}
