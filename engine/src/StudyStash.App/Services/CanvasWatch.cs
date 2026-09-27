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

    CancellationTokenSource? cts;

    /// <summary>The library's answer as of the last <see cref="RefreshAsync"/> (null before the first one, or when
    /// there's no library to ask).</summary>
    public CanvasApi.State? State { get; private set; }

    /// <summary>Raised after every <see cref="RefreshAsync"/> that reached the library, whether or not the state
    /// actually changed — a host that only cares about a real change can compare <see cref="State"/> itself.</summary>
    public event Action? Changed;

    /// <summary>Keeps this computer's Chrome extension folder current on every refresh (on start, and whenever the
    /// library's Canvas address or key changes), and says whether Chrome is connected. Null: leave the folder alone.</summary>
    public ExtensionKeeper? Extension { get; init; }

    /// <summary>5 seconds while syncing or the extension hasn't checked in yet; a minute otherwise.</summary>
    public TimeSpan NextDelay => State?.Status is "syncing" or "no_extension" ? Fast : Slow;

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
            catch (Exception e) when (e is HttpRequestException or CanvasLibraryException or System.Text.Json.JsonException or InvalidOperationException
                                          || (e is TaskCanceledException && !token.IsCancellationRequested))
            {
                // The library didn't answer this time: keep what's known, and ask again after the usual wait.
            }
            catch (OperationCanceledException)
            {
                return;
            }
            try
            {
                await Task.Delay(NextDelay, token);
            }
            catch (TaskCanceledException)
            {
                return;
            }
        }
    }

    public void Stop()
    {
        cts?.Cancel();
        cts = null;
    }
}
