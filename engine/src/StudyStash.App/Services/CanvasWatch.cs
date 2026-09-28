namespace StudyStash.App.Services;

/// <summary>
/// Canvas's only timer: polls the library's state and tells whoever's watching when it changes — the status card,
/// Settings → Canvas, and (T5) the connect flow, all share one of these rather than each keeping their own. Polls
/// faster while there's something worth catching up to (syncing, or waiting for the Chrome extension to check in
/// for the first time); a minute otherwise. Nothing here touches the UI thread — a host marshals <see cref="Changed"/>
/// back to it if it needs to.
/// </summary>
public sealed class CanvasWatch(CanvasContext context)
{
    static readonly TimeSpan Fast = TimeSpan.FromSeconds(5);
    static readonly TimeSpan Slow = TimeSpan.FromSeconds(60);
    static readonly TimeSpan Waiting = TimeSpan.FromSeconds(3);

    CancellationTokenSource? cts;
    /// <summary>The wait between two refreshes, cancelled to ask again straight away.</summary>
    CancellationTokenSource? nap;
    int hurrying;
    bool startedToHurry;

    /// <summary>The library's answer as of the last <see cref="RefreshAsync"/> (null before the first one, or when
    /// there's no library to ask).</summary>
    public CanvasApi.State? State { get; private set; }

    /// <summary>Raised after every <see cref="RefreshAsync"/> that reached the library, whether or not the state
    /// actually changed — a host that only cares about a real change can compare <see cref="State"/> itself.</summary>
    public event Action? Changed;

    /// <summary>Told about anything that went wrong while asking (for the log); the watch carries on regardless.</summary>
    public Action<Exception>? OnError { get; init; }

    /// <summary>Keeps this computer's Chrome extension folder current on every refresh (on start, and whenever the
    /// library's Canvas address or key changes), and says whether Chrome is connected. Null: leave the folder alone.</summary>
    public ExtensionKeeper? Extension { get; init; }

    /// <summary>3 seconds while a screen waits for Chrome (<see cref="Hurry"/>); 5 while syncing or the extension hasn't
    /// checked in yet; a minute otherwise.</summary>
    public TimeSpan NextDelay => hurrying > 0 ? Waiting : State?.Status is "syncing" or "no_extension" ? Fast : Slow;

    /// <summary>The watch is polling (<see cref="Start"/> until <see cref="Stop"/>).</summary>
    public bool Running => cts is not null;

    /// <summary>
    /// "Add to Chrome" is showing: ask the library now and every 3 seconds until the returned handle is disposed, so
    /// "Connected" turns up moments after Chrome loads the extension. Starts the watch if nothing else had (and stops
    /// it again when the last hurry ends).
    /// </summary>
    public IDisposable Hurry()
    {
        if (Interlocked.Increment(ref hurrying) == 1 && !Running)
        {
            Start();
            startedToHurry = true;
        }
        else
        {
            nap?.Cancel();
        }
        return new Hurried(this);
    }

    sealed class Hurried(CanvasWatch watch) : IDisposable
    {
        int done;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref done, 1) == 1) return;
            if (Interlocked.Decrement(ref watch.hurrying) > 0 || !watch.startedToHurry) return;
            watch.startedToHurry = false;
            watch.Stop();
        }
    }

    /// <summary>Asks the library once. Does nothing (and leaves <see cref="State"/> as it was) when there's no
    /// library to ask yet.</summary>
    public async Task RefreshAsync(CancellationToken stop = default)
    {
        if (context.Client is not { } client) return;
        State = await client.StateAsync(stop);
        if (Extension is { } keeper) await keeper.KeepAsync(stop);
        Changed?.Invoke();
    }

    /// <summary>Refreshes now, then again after <see cref="NextDelay"/>, until <see cref="Stop"/>. Restarting an
    /// already-running watch stops the old loop first.</summary>
    public void Start()
    {
        Stop();
        var token = (cts = new CancellationTokenSource()).Token;
        _ = Loop(token);
    }

    async Task Loop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await RefreshAsync(token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e)
            {
                // The library didn't answer this time, or a screen following the watch stumbled: keep what's known,
                // and ask again after the usual wait. The watch never stops on its own, or nothing would update again.
                OnError?.Invoke(e);
            }
            using var wake = CancellationTokenSource.CreateLinkedTokenSource(token);
            nap = wake;
            try
            {
                await Task.Delay(NextDelay, wake.Token);
            }
            catch (TaskCanceledException)
            {
                if (token.IsCancellationRequested) return;
            }
            finally
            {
                nap = null;
            }
        }
    }

    public void Stop()
    {
        cts?.Cancel();
        cts = null;
        startedToHurry = false;
    }
}
