namespace StudyStash.Core.Canvas;

/// <summary>
/// "Something changed" for whoever is waiting on it: the library holds the extension's request for work until there
/// is some, and this is what tells it to look again. Each <see cref="Next"/> finishes at the next <see cref="Raise"/>
/// after it was taken; a raise with nobody waiting is simply gone.
/// </summary>
public sealed class Wake
{
    readonly Lock gate = new();
    TaskCompletionSource next = New();

    static TaskCompletionSource New() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Finishes at the next raise. Take it before looking, so a raise in between isn't missed.</summary>
    public Task Next
    {
        get
        {
            lock (gate) return next.Task;
        }
    }

    /// <summary>Wake everyone waiting.</summary>
    public void Raise()
    {
        TaskCompletionSource was;
        lock (gate)
        {
            was = next;
            next = New();
        }
        was.TrySetResult();
    }
}
