using Avalonia;
using Avalonia.Controls;

namespace StudyStash.App.Views;

/// <summary>
/// How tall the ask bar's conversation may grow: it scrolls inside a height of its own, at most 320, and at most about
/// a third of the window, so however long the chat gets, the notes (or Home's list) keep most of the room. The card
/// sits in room the page makes for it, never over the notes.
/// </summary>
public static class AnswerHeight
{
    public const double Most = 320, Least = 160, Share = 0.36;

    /// <summary>The thread's most height in a window <paramref name="windowHeight"/> tall.</summary>
    public static double For(double windowHeight) => Math.Clamp(windowHeight * Share, Least, Most);

    /// <summary>Keeps <paramref name="thread"/> within <see cref="For"/> of the window <paramref name="view"/> is in,
    /// as the window is resized.</summary>
    public static void Follow(Control view, ScrollViewer thread)
    {
        TopLevel? top = null;
        void Fit() => thread.MaxHeight = For(top?.ClientSize.Height ?? 0);
        void Resized(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property == TopLevel.ClientSizeProperty) Fit();
        }
        view.AttachedToVisualTree += (_, _) =>
        {
            top = TopLevel.GetTopLevel(view);
            if (top is null) return;
            top.PropertyChanged += Resized;
            Fit();
        };
        view.DetachedFromVisualTree += (_, _) =>
        {
            if (top is not null) top.PropertyChanged -= Resized;
            top = null;
        };
    }
}
