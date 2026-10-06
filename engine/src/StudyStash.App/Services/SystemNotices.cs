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
/// longer is. Where the system won't have Study Stash's notifications at all, or its question about allowing them
/// goes unanswered, the notice goes to <c>own</c> (the app's own card) instead, so nothing is lost; where the
/// student has said no to them, nothing is shown, as they asked.
/// </summary>
public sealed class SystemNotices
{
    /// <summary>How long the system's "allow notifications?" may sit unanswered before a notice waiting on it is
    /// shown as the app's own card instead.</summary>
    public static readonly TimeSpan AnswerWait = TimeSpan.FromSeconds(8);

    /// <summary>How many notices are remembered for a click that comes later (from the system's list of earlier ones).</summary>
    public const int Kept = 60;

    const string Act = "act", Later = "later";

    readonly ISystemNotifications system;
    readonly Action<Notice> own;
    readonly Func<DateTime> clock;
    readonly Action<string> log;
    /// <summary>What the system holds or showed, by id, oldest first: what a click on one does, and when it was said.</summary>
    readonly List<(string Id, Notice Notice, DateTime Said)> live = [];
    NotificationState last;

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

    /// <summary>A Canvas notice has Open and Later (which only marks it seen); any other, its one button if it has one.</summary>
    static IReadOnlyList<NotificationButton> Buttons(Notice notice) => notice.Model is CanvasToastModel canvas
        ? [new(Act, notice.ActionLabel ?? "Open"), new(Later, canvas.DismissLabel, OpensApp: false)]
        : notice.ActionLabel is { Length: > 0 } label ? [new(Act, label)] : [];

    public void Show(Notice notice)
    {
        switch (system.State)
        {
            case NotificationState.Unavailable:
                own(notice);
                return;
            case NotificationState.Denied:
                return;
        }
        string id = IdOf(notice);
        live.RemoveAll(l => l.Id == id);
        live.Add((id, notice, clock()));
        if (live.Count > Kept) live.RemoveRange(0, live.Count - Kept);
        system.Show(id, notice.Title, notice.Text, Buttons(notice));
    }

    void Responded(string id, string what)
    {
        int at = live.FindIndex(l => l.Id == id);
        if (at < 0) return; // from before this start of the app: the system brings Study Stash forward, and that's all
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
    /// waiting on its answer are shown as the app's own cards. The student said no: they're let go.</summary>
    void StateChanged()
    {
        var now = system.State;
        if (now == last) return;
        last = now;
        log(now switch
        {
            NotificationState.Allowed => "[notifications] the system shows them",
            NotificationState.Denied => "[notifications] turned off for Study Stash in the system's settings: none are shown",
            NotificationState.Unavailable => "[notifications] the system won't have them from this copy of the app: Study Stash shows its own",
            _ => "[notifications] waiting for the system",
        });
        if (now is not (NotificationState.Unavailable or NotificationState.Denied)) return;
        var waiting = live.Select(l => l.Notice).ToList();
        live.Clear();
        if (now == NotificationState.Unavailable)
            foreach (var notice in waiting.Where(n => n.StillTrue?.Invoke() != false)) own(notice);
    }

    /// <summary>A few times a second: what's no longer so is taken down, and a notice still waiting on the system's
    /// question after <see cref="AnswerWait"/> is shown as the app's own card (the question stays up; once it's
    /// answered the system takes over).</summary>
    public void Tick()
    {
        var now = clock();
        bool asking = system.State == NotificationState.Unknown;
        for (int i = live.Count - 1; i >= 0; i--)
        {
            var (id, notice, said) = live[i];
            if (notice.StillTrue is { } still && !still())
            {
                live.RemoveAt(i);
                system.Remove(id);
            }
            else if (asking && now - said >= AnswerWait)
            {
                live.RemoveAt(i);
                system.Remove(id);
                own(notice);
            }
        }
    }

    /// <summary>Quitting: what said something was still so (a paused lecture, an update on its way) is taken down; the
    /// rest stay in the system's list, as any app's do.</summary>
    public void Quit()
    {
        foreach (var (id, notice, _) in live.Where(l => l.Notice.UntilClosed || l.Notice.StillTrue is not null).ToList()) system.Remove(id);
        live.Clear();
    }
}
