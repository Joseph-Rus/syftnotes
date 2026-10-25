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
        void Asked() => FollowEnd.ToEnd(thread);
        Hear(view, m => m.Asked += Asked, m => m.Asked -= Asked);
    }

    /// <summary>The Chats list under <paramref name="button"/> closes once a chat is picked or deleted there.</summary>
    public static void ClosesChats(Control view, Button button)
    {
        void Done() => button.Flyout?.Hide();
        Hear(view, m => m.ChatsDone += Done, m => m.ChatsDone -= Done);
    }

    /// <summary>The view's Ask model is listened to only while the view is on screen, so a page's model kept for later
    /// doesn't keep a closed view alive.</summary>
    static void Hear(Control view, Action<AiAskModel> on, Action<AiAskModel> off)
    {
        AiAskModel? heard = null;
        void Listen(AiAskModel? now)
        {
            if (ReferenceEquals(now, heard)) return;
            if (heard is not null) off(heard);
            heard = now;
            if (heard is not null) on(heard);
        }
        view.DataContextChanged += (_, _) => Listen(view.IsAttachedToVisualTree() ? view.DataContext as AiAskModel : null);
        view.AttachedToVisualTree += (_, _) => Listen(view.DataContext as AiAskModel);
        view.DetachedFromVisualTree += (_, _) => Listen(null);
    }
}
