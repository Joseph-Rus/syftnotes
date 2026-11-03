namespace StudyStash.App.Services;

/// <summary>
/// The app's one Canvas context and its one <see cref="CanvasWatch"/>, for the library the app is connected to now.
/// A new library address or password makes both again (the old watch stopped first) and says so with
/// <see cref="Made"/>, so the shell can follow the new watch. Every Canvas screen reads <see cref="Context"/> and
/// <see cref="Watch"/> from here (through <c>CanvasModels</c>), never a copy of its own. Used on the UI thread.
/// </summary>
public sealed class CanvasLink(AppHost host)
{
    CanvasContext? context;
    CanvasWatch? watch;
    string madeFor = "";

    /// <summary>A new context and watch were made: the old watch (null the first time), already stopped, and the new
    /// context and watch, not started yet.</summary>
    public event Action<CanvasWatch?, CanvasContext, CanvasWatch>? Made;

    /// <summary>The library (address and key) the context was made for: "" before the first one.</summary>
    public string MadeFor => madeFor;

    /// <summary>The Canvas context for the library the app is connected to now.</summary>
    public CanvasContext Context
    {
        get
        {
            Follow();
            return context!;
        }
    }

    /// <summary>The one watch over <see cref="Context"/>.</summary>
    public CanvasWatch Watch
    {
        get
        {
            Follow();
            return watch!;
        }
    }

    /// <summary>The watch as it is, without making one: null before anything asked for Canvas.</summary>
    public CanvasWatch? Current => watch;

    void Follow()
    {
        var cc = host.Client();
        string key = cc.ServerUrl + "\n" + cc.PoolKey;
        if (context is not null && key == madeFor) return;
        var old = watch;
        old?.Stop();
        madeFor = key;
        var made = CanvasContext.For(host);
        var follows = new CanvasWatch(made)
        {
            Extension = new ExtensionKeeper(host.Home, () => made.Client, host.Log),
            OnError = e => host.Log($"[canvas] asking the library about Canvas: {e.GetType().Name}: {e.Message}"),
        };
        context = made;
        watch = follows;
        Made?.Invoke(old, made, follows);
    }
}
