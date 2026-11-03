using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StudyStash.App.Services;

namespace StudyStash.App.ViewModels.Kit;

/// <summary>
/// Something a student starts with a button, in one object: it's <see cref="Busy"/> while it runs, says what went wrong
/// (<see cref="Say"/>, from the <see cref="Failure"/>'s words) when it didn't work, and can be cancelled. A second press
/// while it's busy is ignored, so two of the same never run at once. A view binds <see cref="Command"/>, <see cref="Busy"/>
/// and <see cref="Say"/>.
/// </summary>
public sealed partial class AsyncAction : ObservableObject
{
    readonly Func<CancellationToken, Task<Outcome>>? work;
    CancellationTokenSource? running;

    /// <param name="work">What <see cref="Command"/> runs; null when the action is only ever run with
    /// <see cref="RunAsync"/>.</param>
    public AsyncAction(Func<CancellationToken, Task<Outcome>>? work = null)
    {
        this.work = work;
        Command = new AsyncRelayCommand(() => this.work is { } w ? RunAsync(w) : Task.CompletedTask);
    }

    [ObservableProperty] public partial bool Busy { get; private set; }
    /// <summary>What the student reads about how it went: the failure's words, or null.</summary>
    [ObservableProperty] public partial string? Say { get; private set; }
    /// <summary>Why the last run didn't work; null after one that did (or before any).</summary>
    [ObservableProperty] public partial Failure? Failure { get; private set; }

    public IAsyncRelayCommand Command { get; }

    /// <summary>Runs <paramref name="what"/> unless the action is busy already (then it returns null at once). A run
    /// that was cancelled ends quietly: not busy, nothing said.</summary>
    public async Task<Outcome?> RunAsync(Func<CancellationToken, Task<Outcome>> what)
    {
        if (Busy) return null;
        using var cts = new CancellationTokenSource();
        running = cts;
        Busy = true;
        Failure = null;
        Say = null;
        try
        {
            var outcome = await what(cts.Token);
            Failure = outcome.Failure;
            Say = outcome.Failure?.Words;
            return outcome;
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            return null;
        }
        finally
        {
            running = null;
            Busy = false;
        }
    }

    /// <summary>Stops the run that's going, if there is one.</summary>
    public void Cancel() => running?.Cancel();
}
