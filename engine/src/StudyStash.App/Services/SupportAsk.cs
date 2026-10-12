namespace StudyStash.App.Services;

/// <summary>How the student answered the library window's ask for a tip.</summary>
public enum SupportAnswer
{
    /// <summary>"Buy us a coffee": the Ko-fi page opens, and it never asks again.</summary>
    Tip,
    /// <summary>"Maybe later": it asks once more, a month on at the soonest.</summary>
    Later,
    /// <summary>"Don't ask again".</summary>
    Never,
}

/// <summary>
/// Where a tip for Study Stash goes, the words that ask for one, and when the library window asks: once the library
/// holds a few lectures, never while recording or during setup, and at most twice ("Maybe later" asks once more a
/// month on, then never again). "Buy us a coffee", in the ask or on Home, ends it for good.
/// </summary>
public static class SupportAsk
{
    /// <summary>The team's Ko-fi page, which "Buy us a coffee" opens in the browser.</summary>
    public const string Page = "https://ko-fi.com/studystashteam";

    /// <summary>The button's words, on Home and in the library window's ask alike.</summary>
    public const string Button = "Buy us a coffee";

    /// <summary>What Home's button is for, under the pointer.</summary>
    public const string HomeTip = "Study Stash is free. A tip on Ko-fi goes to the two students who build it.";

    /// <summary>The ask's title, and its two sentences, a line each.</summary>
    public const string Title = "Enjoying Study Stash?";
    public const string Text = "It's free, and two students build it with Claude.\nA coffee keeps us going.";
    public const string LaterButton = "Maybe later";
    public const string NeverButton = "Don't ask again";

    /// <summary>The library holds at least this many lectures before it asks.</summary>
    public const int AfterLectures = 5;

    /// <summary>How long "Maybe later" waits before the one last ask.</summary>
    public static readonly TimeSpan LaterWait = TimeSpan.FromDays(30);

    /// <summary>Whether the library window asks now, given how many lectures the library holds, whether a lecture is
    /// recording or setup is open, and what the student said before.</summary>
    public static bool Due(AppSettings settings, int lectures, bool recording, bool settingUp, DateTimeOffset now) =>
        !settings.SupportAskDone && !recording && !settingUp && lectures >= AfterLectures
        && (settings.SupportAskLater is not { } later || now - later >= LaterWait);

    /// <summary>This ask is the last: "Maybe later" was said once already, so it offers no later.</summary>
    public static bool IsLast(AppSettings settings) => settings.SupportAskLater is not null;

    /// <summary>Remembers the answer: the first "Maybe later" waits a month; anything else ends the asking.</summary>
    public static void Remember(AppSettings settings, SupportAnswer answer, DateTimeOffset now)
    {
        if (answer == SupportAnswer.Later && settings.SupportAskLater is null) settings.SupportAskLater = now;
        else settings.SupportAskDone = true;
    }

    /// <summary>Saves the answer to the settings, and for a tip opens the Ko-fi page with <paramref name="openUrl"/>.</summary>
    public static void Answer(AppHost host, SupportAnswer answer, Action<string> openUrl, DateTimeOffset now)
    {
        host.Save(s => Remember(s, answer, now));
        if (answer == SupportAnswer.Tip) openUrl(Page);
    }
}
