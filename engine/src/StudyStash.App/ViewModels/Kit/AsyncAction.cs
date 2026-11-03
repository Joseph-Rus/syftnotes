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
    readonly AsyncRelayCommand command;
    /// <summary>What the next run of <see cref="command"/> does when it wasn't started by the button (<see cref="RunAsync"/>).</summary>
    Func<CancellationToken, Task<Outcome>>? pending;
    Outcome? outcome;

    /// <param name="work">What <see cref="Command"/> runs; null when the action is only ever run with
    /// <see cref="RunAsync"/>.</param>
    public AsyncAction(Func<CancellationToken, Task<Outcome>>? work = null)
    {
        this.work = work;
        // No AllowConcurrentExecutions: the toolkit's command refuses a second press while one runs.
        command = new AsyncRelayCommand(RunAsync, AsyncRelayCommandOptions.None);
        command.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IAsyncRelayCommand.IsRunning)) OnPropertyChanged(nameof(Busy));
        };
    }

    /// <summary>It's running (the command's <see cref="IAsyncRelayCommand.IsRunning"/>).</summary>
    public bool Busy => command.IsRunning;
    /// <summary>What the student reads about how it went: the failure's words, or null.</summary>
    [ObservableProperty] public partial string? Say { get; private set; }
    /// <summary>Why the last run didn't work; null after one that did (or before any).</summary>
    [ObservableProperty] public partial Failure? Failure { get; private set; }

    public IAsyncRelayCommand Command => command;

    async Task RunAsync(CancellationToken ct)
    {
        var what = pending ?? work;
        pending = null;
        outcome = null;
        if (what is null) return;
        Failure = null;
        Say = null;
        var done = await what(ct);
        Failure = done.Failure;
        Say = done.Failure?.Words;
        outcome = done;
    }

    /// <summary>Runs <paramref name="what"/> unless the action is busy already (then it returns null at once). A run
    /// that was cancelled ends quietly: not busy, nothing said.</summary>
    public async Task<Outcome?> RunAsync(Func<CancellationToken, Task<Outcome>> what)
    {
        if (command.IsRunning) return null;
        pending = what;
        try
        {
            await command.ExecuteAsync(null);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        return outcome;
    }

    /// <summary>Stops the run that's going, if there is one.</summary>
    public void Cancel() => command.Cancel();
}
