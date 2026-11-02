using StudyStash.Core;
using StudyStash.Core.Ai;

namespace StudyStash.App.ViewModels;

/// <summary>
/// Watches the library while an AI's sign-in is going, so the row or card that started it turns ready by itself: the
/// library signs in hidden, looks at the AI's own status command, and says <see cref="EngineInfo.SignIn"/> on the AI's
/// row until it's done. This asks for the overview every <see cref="Every"/> while one waits for the browser and hands
/// each answer on; it ends by itself when none is
/// going, and starts again from <see cref="Begin"/> when the student leaves the page and comes back to a sign-in still
/// waiting. The student presses nothing.
/// </summary>
public sealed class SignInWatch(IAiLibrary library, Func<AiOverview, Task> changed)
{
    /// <summary>How often the library is asked while a sign-in is going.</summary>
    public TimeSpan Every { get; set; } = TimeSpan.FromSeconds(2);

    bool running;

    /// <summary>Whether a sign-in the student can still finish is going on some AI: waiting for the browser. (One that
    /// stopped waits for Try again.)</summary>
    public static bool Going(AiOverview overview) =>
        overview.Engines.Any(e => e.State == "not_signed_in" && e.SignIn == SignInPhase.Waiting);

    /// <summary>Starts watching when the overview shows a sign-in going (and none is being watched yet).</summary>
    public void Begin(AiOverview? overview)
    {
        if (overview is null || running || !Going(overview)) return;
        running = true;
        _ = LoopAsync();
    }

    async Task LoopAsync()
    {
        try
        {
            while (true)
            {
                await Task.Delay(Every);
                AiOverview? now;
                try
                {
                    now = await library.EnginesAsync();
                }
                catch (Exception e) when (e is HttpRequestException or TaskCanceledException or LibraryRefusedException)
                {
                    return; // the library isn't answering: the page says so itself
                }
                if (now is null) return;
                await changed(now);
                if (!Going(now)) return;
            }
        }
        finally
        {
            running = false;
        }
    }

    /// <summary>What a view shows of the AIs, to tell whether an answer changed anything worth drawing again.</summary>
    public static string Signature(AiOverview overview) =>
        string.Join('|', overview.Engines.Select(e => $"{e.Id}:{e.State}:{e.SignIn}")) + "#" + string.Join('|', overview.Problems.Select(p => $"{p.Id}:{p.SignIn}"));
}
