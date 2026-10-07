using Avalonia.Headless.XUnit;
using StudyStash.App.Platform;
using StudyStash.App.Services;
using StudyStash.App.ViewModels;

namespace StudyStash.App.Tests;

/// <summary>Study Stash's notifications as the computer's own: what's handed to the system, what a click on one does,
/// and what happens where the system won't show them. The system here is a stand-in: the real one (a Mac's
/// Notification Center) only answers a signed app in an Applications folder.</summary>
public class SystemNoticesTests
{
    sealed class FakeSystem : ISystemNotifications
    {
        public NotificationState State { get; private set; } = NotificationState.Unknown;
        public event Action? StateChanged;
        public event Action<string, string>? Responded;
        public List<(string Id, string Title, string Body, IReadOnlyList<NotificationButton> Buttons)> Shown { get; } = [];
        public List<string> Removed { get; } = [];
        /// <summary>False: it won't take the next ones (Windows refusing a notification it can't read).</summary>
        public bool Takes { get; set; } = true;

        public bool Show(string id, string title, string body, IReadOnlyList<NotificationButton> buttons)
        {
            if (Takes) Shown.Add((id, title, body, buttons));
            return Takes;
        }
        public void Remove(string id) => Removed.Add(id);

        public void Say(NotificationState state)
        {
            State = state;
            StateChanged?.Invoke();
        }

        public void Respond(string id, string what) => Responded?.Invoke(id, what);
    }

    sealed class Rig
    {
        public FakeSystem System { get; } = new();
        public List<Notice> Own { get; } = [];
        public DateTime Now = new(2026, 10, 6, 14, 0, 0, DateTimeKind.Utc);
        public SystemNotices Notices { get; }

        public Rig(NotificationState state = NotificationState.Allowed)
        {
            if (state != NotificationState.Unknown) System.Say(state);
            Notices = new SystemNotices(System, run => run(), Own.Add, () => Now);
        }
    }

    [Fact]
    public void A_notice_is_one_of_the_systems_own_and_a_click_on_it_or_its_button_does_what_its_button_does()
    {
        var rig = new Rig();
        int opened = 0;
        Notice Filed() => new() { Title = "Filed in CS 101", Text = "Recursion and the call stack", ActionLabel = "Open note", Act = () => opened++ };

        rig.Notices.Show(Filed());

        var (id, title, body, buttons) = Assert.Single(rig.System.Shown);
        Assert.Equal(("Filed in CS 101", "Recursion and the call stack"), (title, body));
        Assert.Equal(new NotificationButton("act", "Open note"), Assert.Single(buttons));
        Assert.Empty(rig.Own); // not a card of the app's own

        // Said again, it's the same notification shown afresh, not a second one.
        rig.Notices.Show(Filed());
        Assert.Equal(id, rig.System.Shown[1].Id);

        rig.System.Respond(id, NotificationResponse.Clicked);
        Assert.Equal(1, opened);
        Assert.Contains(id, rig.System.Removed);
        rig.System.Respond(id, "act"); // it's gone: a second answer about it does nothing
        Assert.Equal(1, opened);

        rig.Notices.Show(Filed());
        rig.System.Respond(id, "act");
        Assert.Equal(2, opened);

        // One with no button only says something: a click on it does nothing more than bring the app forward.
        int ran = 0;
        var saved = new Notice { Title = "Recording saved", Text = "Study Stash is writing it down.", Act = () => ran++ };
        rig.Notices.Show(saved);
        Assert.Empty(rig.System.Shown[^1].Buttons);
        rig.System.Respond(SystemNotices.IdOf(saved), NotificationResponse.Clicked);
        Assert.Equal(0, ran);
    }

    [Fact]
    public void One_that_says_something_is_still_so_is_taken_down_when_it_isnt()
    {
        var rig = new Rig();
        bool paused = true;
        var notice = new Notice { Title = "Recording paused", Text = "The microphone stopped.", ActionLabel = "Resume", UntilClosed = true, StillTrue = () => paused };
        rig.Notices.Show(notice);

        rig.Notices.Tick();
        Assert.Empty(rig.System.Removed);

        paused = false;
        rig.Notices.Tick();
        Assert.Equal([SystemNotices.IdOf(notice)], rig.System.Removed);
    }

    [AvaloniaFact]
    public void A_canvas_notification_has_open_and_later_and_clearing_it_only_marks_it_seen()
    {
        var rig = new Rig();
        int opened = 0, dismissed = 0;
        var model = new CanvasToastModel(CanvasShots.ToastGallery()[0].Item)
        {
            OnOpen = _ => { opened++; return Task.CompletedTask; },
            OnDismiss = _ => { dismissed++; return Task.CompletedTask; },
        };
        Notice Canvas() => new() { Title = model.Title, Text = model.Text, ActionLabel = "Open", Model = model, Key = "canvas:1" };

        rig.Notices.Show(Canvas());
        var shown = rig.System.Shown[^1];
        Assert.Equal(["Open", model.DismissLabel], shown.Buttons.Select(b => b.Label));
        Assert.False(shown.Buttons[1].OpensApp); // Later does its work without bringing the app forward

        rig.System.Respond(shown.Id, "later");
        Assert.Equal((0, 1), (opened, dismissed));

        rig.Notices.Show(Canvas());
        rig.System.Respond(shown.Id, NotificationResponse.Dismissed);
        Assert.Equal((0, 2), (opened, dismissed));

        rig.Notices.Show(Canvas());
        rig.System.Respond(shown.Id, NotificationResponse.Clicked);
        Assert.Equal((1, 2), (opened, dismissed));
    }

    [Fact]
    public void Nothing_that_needs_the_student_is_lost_for_want_of_the_system()
    {
        var notice = new Notice { Title = "Recording saved" };
        var paused = new Notice { Title = "Recording paused", Text = "The microphone stopped.", ActionLabel = "Resume", UntilClosed = true, StillTrue = () => true };

        // A copy the system refuses (run from its disk image, or a build folder): the app's own card, as before.
        var refused = new Rig(NotificationState.Unavailable);
        refused.Notices.Show(notice);
        Assert.Empty(refused.System.Shown);
        Assert.Equal([notice], refused.Own);

        // Turned off for Study Stash in the system's settings: what only tells isn't shown, as the student asked. A
        // lecture that stopped recording still is, as the app's own card: that can't wait for them to look.
        var declined = new Rig(NotificationState.Denied);
        declined.Notices.Show(notice);
        declined.Notices.Show(paused);
        Assert.Empty(declined.System.Shown);
        Assert.Equal([paused], declined.Own);

        // Allowed, but the system won't take this one (Windows can't read it, or has nowhere to show it): it's said
        // all the same, and a click that can't come isn't waited for.
        var choosy = new Rig();
        choosy.System.Takes = false;
        choosy.Notices.Show(notice);
        Assert.Equal([notice], choosy.Own);
        Assert.False(choosy.Notices.Knows(SystemNotices.IdOf(notice)));
    }

    [Fact]
    public void While_the_systems_question_is_unanswered_nothing_is_lost()
    {
        // The first notice makes the system ask whether to allow them. Answered with a refusal of this copy: what
        // was waiting is shown as the app's own cards, except what's no longer so.
        var rig = new Rig(NotificationState.Unknown);
        bool paused = false;
        var saved = new Notice { Title = "Recording saved" };
        rig.Notices.Show(saved);
        rig.Notices.Show(new Notice { Title = "Recording paused", StillTrue = () => paused });
        Assert.Equal(2, rig.System.Shown.Count); // handed over: the system holds them until it's answered
        rig.System.Say(NotificationState.Unavailable);
        Assert.Equal([saved], rig.Own);

        // Left unanswered: after a few seconds the notice is shown as the app's own card and taken back from the
        // system, so it isn't said twice when the answer comes. Saying it again meanwhile doesn't start its wait over.
        var ignored = new Rig(NotificationState.Unknown);
        ignored.Notices.Show(saved);
        ignored.Notices.Tick();
        Assert.Empty(ignored.Own);
        ignored.Now += SystemNotices.AnswerWait - TimeSpan.FromSeconds(1);
        ignored.Notices.Show(new Notice { Title = "Recording saved" });
        ignored.Now += TimeSpan.FromSeconds(1);
        ignored.Notices.Tick();
        Assert.Equal("Recording saved", Assert.Single(ignored.Own).Title);
        Assert.Equal([SystemNotices.IdOf(saved)], ignored.System.Removed);
        // The question is still unanswered: the next one doesn't wait at all.
        var next = new Notice { Title = "Filed in CS 101" };
        ignored.Notices.Show(next);
        Assert.Equal(next, ignored.Own[^1]);
        // Answered Allow at last: from then on they're the system's.
        ignored.System.Say(NotificationState.Allowed);
        ignored.Notices.Show(new Notice { Title = "Filed in BIO 110" });
        Assert.Equal("Filed in BIO 110", ignored.System.Shown[^1].Title);
        Assert.Equal(2, ignored.Own.Count);

        // Answered "Don't Allow": what was waiting is let go.
        var declined = new Rig(NotificationState.Unknown);
        declined.Notices.Show(saved);
        declined.System.Say(NotificationState.Denied);
        Assert.Empty(declined.Own);
    }
}
