using System.Text.Json;
using StudyStash.Core;
using StudyStash.Core.Ai;
using StudyStash.App.ViewModels;

namespace StudyStash.App.Services;

/// <summary>
/// The library's API clients (<c>host.Api</c>; Core's static <see cref="StudyStash.Core.LibraryApi"/> already has the
/// name), made once and made again only when the library's address changes. The password is read
/// at each request, so a password changed while a window is open (Add a laptop, Password → Change) is used at once.
/// </summary>
public sealed class LibraryClients(Func<ClientConfig> client)
{
    readonly Lock gate = new();
    AiRemote? ai;
    string aiAddress = "";
    RemoteLibrary? library;
    string libraryAddress = "";
    CanvasClient? canvas;
    string canvasAddress = "";

    /// <summary>The library's AI (/api/v2/ai) at the address client.toml has now.</summary>
    public IAiLibrary Ai
    {
        get
        {
            string address = client().ServerUrl;
            lock (gate)
            {
                if (ai is null || aiAddress != address)
                {
                    ai = new AiRemote(address, () => client().PoolKey);
                    aiAddress = address;
                }
                return ai;
            }
        }
    }
    /// <summary>The library over its API (/api/v2) at the address client.toml has now; null while no library is set up.
    /// Shared: nobody disposes it.</summary>
    public RemoteLibrary? Library
    {
        get
        {
            string address = client().ServerUrl;
            if (address.Length == 0) return null;
            lock (gate)
            {
                if (library is null || libraryAddress != address)
                {
                    library = new RemoteLibrary(address, () => client().PoolKey);
                    libraryAddress = address;
                }
                return library;
            }
        }
    }

    /// <summary>The library's Canvas API at the address client.toml has now; null while no library is set up.</summary>
    public CanvasClient? Canvas
    {
        get
        {
            string address = client().ServerUrl;
            if (address.Length == 0) return null;
            lock (gate)
            {
                if (canvas is null || canvasAddress != address)
                {
                    canvas = new CanvasClient(address, () => client().PoolKey);
                    canvasAddress = address;
                }
                return canvas;
            }
        }
    }
}

/// <summary>Why a call to the library didn't give an answer.</summary>
public enum FailureKind
{
    /// <summary>The library isn't answering: not on, not reachable, too slow, or no library set up yet.</summary>
    Offline,
    /// <summary>The library answered, but it runs a Syft Notes from before this route.</summary>
    OlderLibrary,
    /// <summary>The library said no, with its own words (a wrong password, something it can't do now).</summary>
    Refused,
}

/// <summary>A call that didn't work, with the words a student reads about it.</summary>
public sealed record Failure(FailureKind Kind, string Words)
{
    /// <summary>The library's HTTP status when it refused (401: the password changed); 0 otherwise.</summary>
    public int Status { get; init; }
    /// <summary>The page that fixes it, when the library named one.</summary>
    public string? Fix { get; init; }

    public static Failure Offline { get; } = new(FailureKind.Offline, AiWords.NotAnswering);
    public static Failure OlderLibrary { get; } = new(FailureKind.OlderLibrary, AiWords.OlderLibraryWords);

    public static Failure Refused(LibraryRefusedException e) =>
        new(FailureKind.Refused, e.Message) { Status = e.Status, Fix = e.Fix };
}

/// <summary>How something the student started ended: done, or a <see cref="Failure"/>.</summary>
public class Outcome
{
    protected Outcome(Failure? failure) => Failure = failure;

    public Failure? Failure { get; }
    public bool Ok => Failure is null;

    public static Outcome Done { get; } = new(null);
    public static Outcome Failed(Failure failure) => new(failure);
}

/// <summary>An <see cref="Outcome"/> with the library's answer when it worked.</summary>
public sealed class Outcome<T> : Outcome where T : class
{
    Outcome(T? value, Failure? failure) : base(failure) => Value = value;

    /// <summary>The answer (never null when <see cref="Outcome.Ok"/>).</summary>
    public T? Value { get; }

    public static Outcome<T> Answered(T value) => new(value, null);
    public static new Outcome<T> Failed(Failure failure) => new(null, failure);
}

/// <summary>
/// One call to the library, with every way it can go wrong turned into one <see cref="Failure"/>: not answering
/// (unreachable, timed out, no library set up, a reply that isn't the library's), from before the route (it answered
/// null), or refused with its own words. Only the caller's own cancelling is let through, as an
/// <see cref="OperationCanceledException"/>.
/// </summary>
public static class LibraryCall
{
    public static async Task<Outcome<T>> Run<T>(Func<Task<T?>> call, CancellationToken ct = default) where T : class
    {
        try
        {
            return await call() is { } answer ? Outcome<T>.Answered(answer) : Outcome<T>.Failed(Failure.OlderLibrary);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (LibraryRefusedException e)
        {
            return Outcome<T>.Failed(Failure.Refused(e));
        }
        catch (CanvasLibraryException e)
        {
            // Canvas's routes say no in plain words (a 4xx with its detail); anything else is the library not coping.
            if (e.Status is >= 400 and < 500 && e.Message.Length > 0) return Outcome<T>.Failed(new Failure(FailureKind.Refused, e.Message) { Status = e.Status });
            Program.Log($"[library] a Canvas call failed: {e.Status} {e.Message}");
            return Outcome<T>.Failed(Failure.Offline);
        }
        catch (Exception e) when (IsOffline(e))
        {
            return Outcome<T>.Failed(Failure.Offline);
        }
    }

    /// <summary>What "the library isn't answering" looks like from here: no connection, a timeout (HttpClient's is a
    /// <see cref="TaskCanceledException"/>), no address to ask (an <see cref="InvalidOperationException"/> from a
    /// request with no host), or a reply that isn't the library's JSON.</summary>
    public static bool IsOffline(Exception e) =>
        e is HttpRequestException or TaskCanceledException or TimeoutException or IOException or JsonException
            or InvalidOperationException or UriFormatException;

    /// <summary>For <c>catch (Exception e) when (LibraryCall.Handled(e, "[area] what"))</c> where a screen shows its own
    /// plain words for any failure of a call to the library: the expected ones (<see cref="IsOffline"/>) pass quietly,
    /// anything else is written to the log first, so it isn't lost. Always true.</summary>
    public static bool Handled(Exception e, string where)
    {
        if (!IsOffline(e)) Program.Log($"{where} failed: {e.GetType().Name}: {e.Message}");
        return true;
    }
}
