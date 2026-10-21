using Avalonia.Controls;
using Avalonia.VisualTree;
using StudyStash.App.Controls;
using StudyStash.App.ViewModels;

namespace StudyStash.App.Views;

/// <summary>
/// A conversation's thread (the answer card's, the recorder's chat's) shows each question as it's asked: it goes back
/// to its end and follows the answer being written, even where the student had scrolled up to read something earlier
/// (<see cref="FollowEnd"/> would otherwise stay put, the thread only ever growing now). Scrolling up while an answer
/// is written still keeps it where they put it.
/// </summary>
public static class AskThread
{
    public static void Follow(Control view, ScrollViewer thread)
    {
        AiAskModel? heard = null;
        void Asked() => FollowEnd.ToEnd(thread);
        // Heard only while the view is on screen, so a page's model kept for later doesn't keep a closed view alive.
        void Listen(AiAskModel? now)
        {
            if (ReferenceEquals(now, heard)) return;
            if (heard is not null) heard.Asked -= Asked;
            heard = now;
            if (heard is not null) heard.Asked += Asked;
        }
        view.DataContextChanged += (_, _) => Listen(view.IsAttachedToVisualTree() ? view.DataContext as AiAskModel : null);
        view.AttachedToVisualTree += (_, _) => Listen(view.DataContext as AiAskModel);
        view.DetachedFromVisualTree += (_, _) => Listen(null);
    }
}
