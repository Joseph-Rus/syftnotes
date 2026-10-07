using System.Runtime.Versioning;
using System.Xml.Linq;
using StudyStash.App.Platform;

namespace StudyStash.App.Tests;

/// <summary>Windows' own notifications: the words handed to Windows, the link a click comes back through, and, on a
/// real Windows only, that Windows' notification interfaces answer as this code expects them to.</summary>
public class WinNotificationsTests(ITestOutputHelper output)
{
    const string Id = "ss-0123456789abcdef01234567";

    [Fact]
    public void A_notification_is_its_words_its_buttons_and_the_link_a_click_opens_with_no_picture_or_sound()
    {
        string xml = NoticeLinks.WindowsXml(Id, "Filed in CS 101 & <BIO>", "\"Recursion\" and the call stack",
            [new NotificationButton("act", "Open note"), new NotificationButton("later", "Dismiss", OpensApp: false)]);

        var toast = XElement.Parse(xml); // well formed, whatever the words hold
        Assert.Equal(("studystash:notice?id=" + Id, "protocol"), (toast.Attribute("launch")!.Value, toast.Attribute("activationType")!.Value));
        Assert.Equal(["Filed in CS 101 & <BIO>", "\"Recursion\" and the call stack"], toast.Descendants("text").Select(t => t.Value));
        Assert.Equal([("Open note", $"studystash:notice?id={Id}&do=act"), ("Dismiss", $"studystash:notice?id={Id}&do=later")],
            toast.Descendants("action").Select(a => (a.Attribute("content")!.Value, a.Attribute("arguments")!.Value)));
        Assert.Empty(toast.Descendants("image"));
        Assert.Equal("true", toast.Element("audio")!.Attribute("silent")!.Value);

        // A control character in the words (a Canvas title is whatever the school typed) can't stop Windows reading it.
        Assert.Equal("Quiz 1 due", XElement.Parse(NoticeLinks.WindowsXml(Id, "Quiz\u0001 1\u0008 due", "", [])).Descendants("text").Single().Value);

        // One with nothing but a title has one line and no row of buttons.
        var bare = XElement.Parse(NoticeLinks.WindowsXml(Id, "Recording saved", "", []));
        Assert.Single(bare.Descendants("text"));
        Assert.Empty(bare.Descendants("actions"));
    }

    [Theory]
    [InlineData("studystash:notice?id=ss-0123456789abcdef01234567", "notice:ss-0123456789abcdef01234567:")]
    [InlineData("studystash:notice?id=ss-0123456789abcdef01234567&do=act", "notice:ss-0123456789abcdef01234567:act")]
    [InlineData("studystash:notice?id=ss-0123456789abcdef01234567&do=later", "notice:ss-0123456789abcdef01234567:later")]
    // Anything can ask Windows to open a studystash: link, so nothing but that exact shape means anything.
    [InlineData("studystash:notice?id=ss-0123456789abcdef01234567&do=record", null)]
    [InlineData("studystash:settings", null)]
    [InlineData("studystash:notice?id=../../etc", null)]
    [InlineData("https://example.com/studystash:notice?id=ss-0123456789abcdef01234567", null)]
    [InlineData("studystash:notice?id=ss-0123456789abcdef01234567&do=act --record", null)]
    public void A_click_comes_back_as_a_link_and_only_that_exact_link_becomes_a_word(string link, string? word)
    {
        Assert.Equal(word, NoticeLinks.WordFor(link));
        if (word is null) return;
        Assert.True(Desktop.IsWord(word)); // the running app takes it from the copy Windows started
        var (id, what) = NoticeLinks.Parse(word)!.Value;
        Assert.Equal(link, NoticeLinks.For(id, what));
    }

    [Fact]
    public void On_Windows_itself_its_notification_interfaces_answer_as_expected()
    {
        if (!OperatingSystem.IsWindows()) return;
        OnWindows();
    }

    /// <summary>Under an id of the tests' own (never the app's, and never the link a click opens): Windows hands over
    /// a notifier, says whether notifications are on, takes a notification or says why not, and takes one down. A
    /// wrong interface id or method would fail here, or end the test run, rather than on a student's PC.</summary>
    [SupportedOSPlatform("windows")]
    void OnWindows()
    {
        const string app = "StudyStash.Tests.Notifications";
        try
        {
            var windows = WinNotifications.Make(app, "Study Stash tests");
            Assert.True(windows is not null, "Windows' notifications couldn't be reached: " + WinNotifications.LastError);
            var state = windows.State;
            output.WriteLine($"setting: {state} ({WinNotifications.LastError ?? "no error"})");
            Assert.NotEqual(NotificationState.Unknown, state);

            bool shown = windows.ShowXml(Id, NoticeLinks.WindowsXml(Id, "Filed in CS 101", "Recursion and the call stack", [new NotificationButton("act", "Open note")]));
            output.WriteLine($"shown: {shown} ({WinNotifications.LastError ?? "no error"})");
            // A machine with nobody at it may have nowhere to show one: then Windows says so, which is an answer too.
            Assert.True(shown || WinNotifications.LastError is not null);
            // Words Windows can't read are refused with an error, not a crash.
            Assert.False(windows.ShowXml(Id, "<toast><visual>"));
            windows.Remove(Id);
        }
        finally
        {
            WinNotifications.UnregisterApp(app);
        }
    }
}
