namespace StudyStash.App.Services;

/// <summary>How long a notification stays on screen by itself.</summary>
public static class NoticeTimes
{
    /// <summary>Something done, with nothing to do about it ("Recording saved"): about as long as the system's own.</summary>
    public static readonly TimeSpan Brief = TimeSpan.FromSeconds(6);

    /// <summary>With a button to press: long enough to read it and decide.</summary>
    public static readonly TimeSpan WithAction = TimeSpan.FromSeconds(10);

    /// <summary>Advice worth reading (a lighter model, where the S. is): half a minute.</summary>
    public static readonly TimeSpan Advice = TimeSpan.FromSeconds(30);

    /// <summary>The pointer just left it: it stays at least this much longer, so it doesn't vanish the moment you look
    /// away.</summary>
    public static readonly TimeSpan AfterHover = TimeSpan.FromSeconds(3);
}

/// <summary>
/// One notification: what it says, its button and what that does, and how long it stays. Its clock runs only while
/// it's on screen with the pointer off it: hovering holds it, and so does waiting for room (the dropdown open over its
/// corner).
/// </summary>
public sealed class Notice
{
    public required string Title { get; init; }
    public string Text { get; init; } = "";

    /// <summary>The button's word ("Open note", "Settings"); null for none. Clicking the notification itself does the
    /// same, as the system's own.</summary>
    public string? ActionLabel { get; init; }

    public Action? Act { get; init; }

    /// <summary>How long it stays by itself; null for the usual time (<see cref="NoticeTimes.Brief"/>, or
    /// <see cref="NoticeTimes.WithAction"/> with a button).</summary>
    public TimeSpan? Stay { get; init; }

    /// <summary>Stays until it's closed or acted on: a problem that needs you (the lecture paused).</summary>
    public bool UntilClosed { get; init; }

    /// <summary>What it says is still so; once this turns false (the lecture resumed, the password was typed) it goes,
    /// however long it had left.</summary>
    public Func<bool>? StillTrue { get; init; }

    /// <summary>Two notifications with the same key are one: the second starts the first's time again instead of
    /// showing twice. Its title and words, unless it has an identity of its own (a Canvas notification's id).</summary>
    public string Key
    {
        get => key ?? Title + "\n" + Text;
        init => key = value;
    }

    string? key;

    /// <summary>What it's about, when it's more than words (a Canvas notification's model).</summary>
    public object? Model { get; init; }

    /// <summary>How long it stays, all told: null while it's <see cref="UntilClosed"/>.</summary>
    public TimeSpan? Lasts => UntilClosed ? null : Stay ?? (ActionLabel is { Length: > 0 } ? NoticeTimes.WithAction : NoticeTimes.Brief);

    TimeSpan left;
    DateTime? since;

    public bool Hovered { get; private set; }
    public bool Waiting { get; private set; }

    /// <summary>On screen now: its full time, running.</summary>
    internal void Start(DateTime now)
    {
        left = Lasts ?? TimeSpan.MaxValue;
        since = Hovered || Waiting ? null : now;
    }

    /// <summary>Time it has left by itself (<see cref="TimeSpan.MaxValue"/> while it stays until closed).</summary>
    public TimeSpan Left(DateTime now) => Lasts is null ? TimeSpan.MaxValue : since is { } s ? left - (now - s) : left;

    /// <summary>The pointer came onto it (<paramref name="over"/>) or left it: its time holds while it's there, and
    /// it has at least <see cref="NoticeTimes.AfterHover"/> once it's gone.</summary>
    public void Hover(bool over, DateTime now)
    {
        if (over == Hovered) return;
        Pause(now);
        Hovered = over;
        if (!over && left < NoticeTimes.AfterHover) left = NoticeTimes.AfterHover;
        Resume(now);
    }

    /// <summary>It's waiting for room (<paramref name="waiting"/>), or has it again: no time passes while it waits.</summary>
    public void Wait(bool waiting, DateTime now)
    {
        if (waiting == Waiting) return;
        Pause(now);
        Waiting = waiting;
        Resume(now);
    }

    void Pause(DateTime now)
    {
        if (since is not { } s || Lasts is null) return;
        left -= now - s;
        since = null;
    }

    void Resume(DateTime now)
    {
        if (!Hovered && !Waiting) since = now;
    }

    /// <summary>Time to go: its time ran out on screen, or what it says is no longer so.</summary>
    public bool Gone(DateTime now)
    {
        if (StillTrue is { } still && !still()) return true;
        return Lasts is not null && !Hovered && !Waiting && Left(now) <= TimeSpan.Zero;
    }
}

/// <summary>
/// The notifications on screen, newest first, as the system's own keep them: never two alike (the second starts the
/// first's time again), and at most <see cref="Most"/> at once, the oldest folding away as a new one comes (a problem
/// that needs you last of all).
/// </summary>
public sealed class NoticeStack
{
    public const int Most = 3;

    readonly List<Notice> showing = [];

    /// <summary>Newest first: the one nearest the screen's edge.</summary>
    public IReadOnlyList<Notice> Showing => showing;

    /// <summary>Puts <paramref name="notice"/> on top, and says what folded away to make room. One alike already
    /// showing takes its place instead (its time starts again) and nothing is added.</summary>
    public (bool Added, IReadOnlyList<Notice> Folded) Add(Notice notice, DateTime now)
    {
        if (showing.FirstOrDefault(n => n.Key == notice.Key) is { } same)
        {
            same.Start(now);
            return (false, []);
        }
        notice.Start(now);
        showing.Insert(0, notice);
        var folded = new List<Notice>();
        while (showing.Count > Most)
        {
            var oldest = showing.Skip(1).LastOrDefault(n => !n.UntilClosed) ?? showing[^1];
            showing.Remove(oldest);
            folded.Add(oldest);
        }
        return (true, folded);
    }

    public bool Remove(Notice notice) => showing.Remove(notice);

    /// <summary>The ones to take down now (see <see cref="Notice.Gone"/>).</summary>
    public IReadOnlyList<Notice> Due(DateTime now) => [.. showing.Where(n => n.Gone(now))];
}
