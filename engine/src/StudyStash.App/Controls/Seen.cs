using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace StudyStash.App.Controls;

/// <summary>
/// Says when a control can be seen and when it can't: it's in a window that's showing, and neither it nor anything above
/// it is hidden. Avalonia has the answer (<see cref="Visual.IsEffectivelyVisible"/>) but nothing to say when it changes,
/// so this listens to the visibility of the control and of each thing above it. A spinner, a pulsing dot or a playing
/// slider uses it to stop while nobody can see it (a hidden panel, a library window closed to the menu bar) instead of
/// waking the app for ever to redraw it.
/// </summary>
public sealed class Seen : IDisposable
{
    readonly Visual target;
    readonly Action<bool> changed;
    readonly List<IDisposable> listening = [];
    bool seen;

    /// <summary>Starts listening: <paramref name="changed"/> is called with true when <paramref name="target"/> comes into
    /// view and false when it goes out of it (once now, if it's already in view). Dispose to stop.</summary>
    public Seen(Visual target, Action<bool> changed)
    {
        this.target = target;
        this.changed = changed;
        target.AttachedToVisualTree += Attached;
        target.DetachedFromVisualTree += Detached;
        if (TopLevel.GetTopLevel(target) is not null) Listen();
    }

    /// <summary>Whether it's in view now.</summary>
    public bool InView => seen;

    void Attached(object? sender, VisualTreeAttachmentEventArgs e) => Listen();

    void Detached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        Forget();
        Update();
    }

    void Listen()
    {
        Forget();
        for (Visual? v = target; v is not null; v = v.GetVisualParent())
            listening.Add(v.GetObservable(Visual.IsVisibleProperty).Subscribe(new Watcher(this)));
        Update();
    }

    void Forget()
    {
        foreach (var l in listening) l.Dispose();
        listening.Clear();
    }

    void Update()
    {
        bool now = TopLevel.GetTopLevel(target) is not null && target.IsEffectivelyVisible;
        if (now == seen) return;
        seen = now;
        changed(now);
    }

    sealed class Watcher(Seen owner) : IObserver<bool>
    {
        public void OnNext(bool value) => owner.Update();
        public void OnError(Exception error) { }
        public void OnCompleted() { }
    }

    public void Dispose()
    {
        target.AttachedToVisualTree -= Attached;
        target.DetachedFromVisualTree -= Detached;
        Forget();
    }
}
