namespace StudyStash.App.Services;

/// <summary>Where a tip for Study Stash goes, and the words that ask for one, the same everywhere they show.</summary>
public static class SupportAsk
{
    /// <summary>The team's Ko-fi page, which "Buy us more Claude usage" opens in the browser.</summary>
    public const string Page = "https://ko-fi.com/studystashteam";

    /// <summary>The button's words, in Settings → About and in the library window's ask alike.</summary>
    public const string Button = "Buy us more Claude usage";

    /// <summary>Settings → About's line beside the button.</summary>
    public const string AboutLine = "Study Stash is free and open source, built with Claude.";
}
