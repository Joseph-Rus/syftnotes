
namespace StudyStash.App.ViewModels;

/// <summary>
/// An answer arriving a few words at a time, put on screen at a steady pace: its first words at once, then the latest
/// text at most every <see cref="Every"/>, so a fast engine reads smoothly instead of redrawing the answer on every
/// word. Each <see cref="Show"/> is all of the text so far; only the latest is ever drawn.
/// </summary>
public sealed class Paced(Action<string> draw, TimeProvider? time = null)
{
    readonly TimeProvider clock = time ?? TimeProvider.System;
    /// <summary>About two frames: often enough to read as flowing, rarely enough that laying out the answer again
    /// never gets in the way.</summary>
    public static readonly TimeSpan Every = TimeSpan.FromMilliseconds(40);

    readonly Lock gate = new();
    string? waiting;
    long? drawnAt;
    bool scheduled, over;

    /// <summary>The text so far: drawn now, or with the next beat.</summary>
    public void Show(string text)
    {
        TimeSpan wait;
        lock (gate)
        {
            if (over) return;
            waiting = text;
            if (scheduled) return;
            wait = drawnAt is not { } at ? TimeSpan.Zero : Every - clock.GetElapsedTime(at);
            if (wait > TimeSpan.Zero) scheduled = true;
        }
        if (wait > TimeSpan.Zero) _ = DrawLater(wait);
        else Draw();
    }

    /// <summary>Stops drawing (the whole answer, or why it stopped, takes over from here) and hands back the last text
    /// it was given, drawn or not; null when it was given none.</summary>
    public string? End()
    {
        lock (gate)
        {
            over = true;
            return waiting;
        }
    }

    async Task DrawLater(TimeSpan wait)
    {
        await Task.Delay(wait, clock); // back on the thread it was shown on (the UI's), where drawing belongs
        lock (gate) scheduled = false;
        Draw();
    }

    void Draw()
    {
        // Drawn under the lock: once End has returned, nothing drawn late can cover what the caller shows next.
        lock (gate)
        {
            if (over || waiting is null) return;
            drawnAt = clock.GetTimestamp();
            draw(waiting);
        }
    }
}
