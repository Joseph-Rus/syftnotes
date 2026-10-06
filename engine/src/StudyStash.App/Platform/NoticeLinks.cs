using System.Text.RegularExpressions;

namespace StudyStash.App.Platform;

/// <summary>
/// What a click on one of Windows' notifications comes back as. Windows opens a link for it
/// (<c>studystash:notice?id=…&amp;do=act</c>), which starts a second copy of the app with <c>--open LINK</c>; that copy
/// hands the running one a word (<c>notice:ID:act</c>) and goes. Only this exact shape is ever acted on: anything can
/// ask Windows to open a studystash: link, so nothing else in one means anything.
/// </summary>
public static partial class NoticeLinks
{
    public const string Scheme = "studystash";

    [GeneratedRegex(@"^studystash:notice\?id=(ss-[0-9a-f]{24})(?:&do=(act|later))?$")]
    private static partial Regex Link();

    [GeneratedRegex(@"^notice:(ss-[0-9a-f]{24}):(act|later|)$")]
    private static partial Regex Word();

    /// <summary>The link a click on notification <paramref name="id"/> opens (<paramref name="what"/>: a button's id,
    /// or "" for the notification itself).</summary>
    public static string For(string id, string what = "") => $"{Scheme}:notice?id={id}{(what.Length > 0 ? "&do=" + what : "")}";

    /// <summary>A notification as Windows reads one: the title and the words, a button for each of
    /// <paramref name="buttons"/>, and the link a click on it, or on a button, opens. No picture and no sound.</summary>
    public static string WindowsXml(string id, string title, string body, IReadOnlyList<NotificationButton> buttons)
    {
        static string X(string s) => System.Security.SecurityElement.Escape(s) ?? "";
        var xml = new System.Text.StringBuilder();
        xml.Append($"<toast launch=\"{X(For(id))}\" activationType=\"protocol\"><visual><binding template=\"ToastGeneric\">");
        xml.Append($"<text>{X(title)}</text>");
        if (body.Length > 0) xml.Append($"<text>{X(body)}</text>");
        xml.Append("</binding></visual>");
        if (buttons.Count > 0)
        {
            xml.Append("<actions>");
            foreach (var b in buttons)
                xml.Append($"<action content=\"{X(b.Label)}\" arguments=\"{X(For(id, b.Id))}\" activationType=\"protocol\"/>");
            xml.Append("</actions>");
        }
        return xml.Append("<audio silent=\"true\"/></toast>").ToString();
    }

    /// <summary>The word for a link Windows opened, or null when it isn't one of these.</summary>
    public static string? WordFor(string link) => Link().Match(link) is { Success: true } m ? $"notice:{m.Groups[1].Value}:{m.Groups[2].Value}" : null;

    public static bool IsWord(string word) => Word().IsMatch(word);

    /// <summary>A word's notification and what was done with it ("" for a click on the notification itself).</summary>
    public static (string Id, string What)? Parse(string word) => Word().Match(word) is { Success: true } m ? (m.Groups[1].Value, m.Groups[2].Value) : null;
}
