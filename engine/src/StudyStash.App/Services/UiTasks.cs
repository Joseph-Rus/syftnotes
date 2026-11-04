using Avalonia.Threading;

namespace StudyStash.App.Services;

/// <summary>
/// Work the app starts and doesn't wait for (a page being read, a list being loaded, a click's errand): it runs on the
/// UI thread, and if it fails the way nothing expected, that's written down with what it was, and the student is told
/// the same way as for anything else unexpected (<see cref="Use"/>), not left looking at a blank or stale page. Being
/// cancelled (a window closed, the app quitting) is no failure: nothing is said.
/// </summary>
internal static class UiTasks
{
    static Action<string> log = Program.Log;
    static Action oops = () => { };

    /// <summary>Where failures are written (<paramref name="write"/>) and how the student is told (<paramref name="say"/>,
    /// on the UI thread).</summary>
    public static void Use(Action<string> write, Action say)
    {
        log = write;
        oops = say;
    }

    /// <summary>Starts <paramref name="work"/> on the UI thread (at once when already on it) and watches it to the end.
    /// <paramref name="what"/> names it in the log ("show Home").</summary>
    public static void Run(Func<Task> work, string what) => Start(work, what, oops);

    /// <summary><see cref="Run"/> for work on a timer that the student didn't ask for (a poll every minute): a failure is
    /// written down but not said, since a message the student can do nothing about would come back on every turn.</summary>
    public static void RunQuietly(Func<Task> work, string what) => Start(work, what, () => { });

    static void Start(Func<Task> work, string what, Action say)
    {
        if (Dispatcher.UIThread.CheckAccess()) _ = Guard(work, what, log, say);
        else Dispatcher.UIThread.Post(() => _ = Guard(work, what, log, say));
    }

    /// <summary>Runs <paramref name="work"/> to the end and never throws: a failure goes to <paramref name="write"/> and
    /// <paramref name="say"/>; a real cancellation goes nowhere (a timeout is no cancellation).</summary>
    internal static async Task Guard(Func<Task> work, string what, Action<string> write, Action say)
    {
        try
        {
            await work();
        }
        // A request that timed out is a TaskCanceledException too, but nobody cancelled it: that's a failure like any other.
        catch (OperationCanceledException e) when (e.InnerException is not TimeoutException)
        {
        }
        catch (Exception e)
        {
            write($"[error] {what} failed: {e}");
            try
            {
                say();
            }
            catch (Exception again)
            {
                write($"[error] couldn't say so: {again.Message}");
            }
        }
    }
}
