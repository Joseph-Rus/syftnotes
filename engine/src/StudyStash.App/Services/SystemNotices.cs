using System.Security.Cryptography;
using System.Text;
using StudyStash.App.Platform;
using StudyStash.App.ViewModels;

namespace StudyStash.App.Services;

/// <summary>
/// Study Stash's notifications as the computer's own (<see cref="ISystemNotifications"/>): each <see cref="Notice"/>
/// is handed to the system, which shows it its way; a click on it, or on its button, does what the notice's button
/// does. The system decides how long one stays and where, so <see cref="Notice.Stay"/> means nothing here; one that
/// says something is still so (<see cref="Notice.StillTrue"/>: the lecture is paused) is taken down when it no
/// longer is.
/// <para>Nothing is lost for want of the system. Where it won't have Study Stash's notifications at all, won't take
/// one, or leaves its question about allowing them unanswered, the notice goes to <c>own</c> (the app's own card)
/// instead. Where the student has turned them off, the ones that only tell (a lecture filed) aren't shown, as they
/// asked; one that needs them (<see cref="NeedsTheStudent"/>: the recording paused by itself) still shows, as the
/// app's own card.</para>
/// </summary>
public sealed class SystemNotices
{
    /// <summary>How long the system's "allow notifications?" may sit unanswered before a notice waiting on it is
    /// shown as the app's own card instead.</summary>
    public static readonly TimeSpan AnswerWait = TimeSpan.FromSeconds(8);

    /// <summary>How many notices are remembered for a click that comes later (from the system's list of earlier ones).</summary>
    public const int Kept = 60;

    const string Act = "act", Later = "later";

    /// <summary>One the system holds or showed: what a click on it does, when it was first said, and whether it's
    /// still waiting on the system's question.</summary>
    sealed record Live(string Id, Notice Notice, DateTime Said, bool Waiting);

    readonly ISystemNotifications system;
    readonly Action<Notice> own;
    readonly Func<DateTime> clock;
    readonly Action<string> log;
    /// <summary>Oldest first.</summary>
    readonly List<Live> live = [];
    NotificationState last;
    /// <summary>A notice already waited out the system's unanswered question: the ones after it don't wait again.</summary>
    bool gaveUpWaiting;

    /// <param name="system">The computer's notifications.</param>
    /// <param name="post">Runs something on the app's own thread (the system answers on any).</param>
    /// <param name="own">Shows a notice as the app's own card.</param>
    public SystemNotices(ISystemNotifications system, Action<Action> post, Action<Notice> own, Func<DateTime>? clock = null, Action<string>? log = null)
    {
        this.system = system;
        this.own = own;
        this.clock = clock ?? (() => DateTime.UtcNow);
        this.log = log ?? (_ => { });
        last = system.State;
        system.Responded += (id, what) => post(() => Responded(id, what));
        system.StateChanged += () => post(StateChanged);
    }

    /// <summary>A notice's id with the system: the same notice said twice is one notification, shown afresh.</summary>
    public static string IdOf(Notice notice) => "ss-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(notice.Key)))[..24];

    /// <summary>It says something that needs the student now (the lecture paused by itself, a problem with its fix):
    /// shown even where they've turned notifications off, as the app's own card.</summary>
    public static bool NeedsTheStudent(Notice notice) => notice.UntilClosed || notice.StillTrue is not null;

    /// <summary>Whether a click on notification <paramref name="id"/> is one this run of the app can act on.</summary>
    public bool Knows(string id) => live.Any(l => l.Id == id);

    /// <summary>A Canvas notice has Open and Later (which only marks it seen); any other, its one button if it has one.</summary>
    static IReadOnlyList<NotificationButton> Buttons(Notice notice) => notice.Model is CanvasToastModel canvas
        ? [new(Act, notice.ActionLabel ?? "Open"), new(Later, canvas.DismissLabel, OpensApp: false)]
        : notice.ActionLabel is { Length: > 0 } label ? [new(Act, label)] : [];

    public void Show(Notice notice)
    {
        var state = system.State;
        if (state == NotificationState.Unavailable || (state == NotificationState.Unknown && gaveUpWaiting))
        {
            own(notice);
            return;
        }
        if (state == NotificationState.Denied)
        {
            if (NeedsTheStudent(notice)) own(notice);
            return;
        }
        string id = IdOf(notice);
        bool waiting = state == NotificationState.Unknown;
        // Said again while it waits on the system's question, it has waited since it was first said.
        var said = waiting && live.FirstOrDefault(l => l.Id == id) is { Waiting: true } before ? before.Said : clock();
        live.RemoveAll(l => l.Id == id);
        live.Add(new Live(id, notice, said, waiting));
        if (live.Count > Kept) live.RemoveRange(0, live.Count - Kept);
        if (system.Show(id, notice.Title, notice.Text, Buttons(notice))) return;
        // The system wouldn't take this one: it's said all the same.
        live.RemoveAll(l => l.Id == id);
        log("[notifications] the system didn't take one: Study Stash shows it itself");
        own(notice);
    }

    void Responded(string id, string what)
    {
        int at = live.FindIndex(l => l.Id == id);
        if (at < 0) return; // from before this start of the app: nothing here knows what it was about
        var notice = live[at].Notice;
        live.RemoveAt(at);
        bool acted = what == Act || what == NotificationResponse.Clicked;
        if (notice.Model is CanvasToastModel canvas)
        {
            // Open also marks it seen; Later and clearing it away only mark it seen.
            var command = acted ? canvas.OpenCommand : canvas.DismissCommand;
            if (command.CanExecute(null)) command.Execute(null);
        }
        else if (acted && notice.ActionLabel is { Length: > 0 })
            notice.Act?.Invoke();
        if (acted || what == Later) system.Remove(id);
    }

    /// <summary>The system has said whether it shows Study Stash's notifications. It won't have them here: the ones
    /// waiting on its answer are shown as the app's own cards. The student said no: the ones that need them still
    /// are, and the rest are let go.</summary>
    void StateChanged()
    {
        var now = system.State;
        if (now == last) return;
        last = now;
        log(now switch
        {
            NotificationState.Allowed => "[notifications] the system shows them",
            NotificationState.Denied => "[notifications] turned off for Study Stash in the system's settings: only what needs the student is shown, by the app",
            NotificationState.Unavailable => "[notifications] the system won't have them from this copy of the app: Study Stash shows its own",
            _ => "[notifications] waiting for the system",
        });
        if (now == NotificationState.Allowed)
        {
            // Handed over: they're the system's now, nothing waits.
            for (int i = 0; i < live.Count; i++) live[i] = live[i] with { Waiting = false };
            return;
        }
        if (now is not (NotificationState.Unavailable or NotificationState.Denied)) return;
        var waiting = live.Where(l => l.Waiting).Select(l => l.Notice).ToList();
        live.RemoveAll(l => l.Waiting);
        foreach (var notice in waiting.Where(n => n.StillTrue?.Invoke() != false))
            if (now == NotificationState.Unavailable || NeedsTheStudent(notice)) own(notice);
    }

    /// <summary>A few times a second: what's no longer so is taken down, and a notice still waiting on the system's
    /// question after <see cref="AnswerWait"/> is shown as the app's own card (the question stays up; once it's
    /// answered the system takes over). The system is only asked where it stands when a notice has waited that long.</summary>
    public void Tick()
    {
        if (live.Count == 0) return;
        var now = clock();
        bool? asking = null;
        for (int i = live.Count - 1; i >= 0; i--)
        {
            var (id, notice, said, waiting) = live[i];
            if (notice.StillTrue is { } still && !still())
            {
                live.RemoveAt(i);
                system.Remove(id);
            }
            else if (waiting && now - said >= AnswerWait && (asking ??= system.State == NotificationState.Unknown))
            {
                live.RemoveAt(i);
                system.Remove(id);
                gaveUpWaiting = true;
                own(notice);
            }
        }
    }

    /// <summary>Quitting: what said something was still so (a paused lecture, an update on its way) is taken down; the
    /// rest stay in the system's list, as any app's do.</summary>
    public void Quit()
    {
        foreach (var l in live.Where(l => NeedsTheStudent(l.Notice)).ToList()) system.Remove(l.Id);
        live.Clear();
    }
}
