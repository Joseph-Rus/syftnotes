using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Xml;
using Microsoft.Extensions.Time.Testing;
using StudyStash.App.Platform;
using StudyStash.App.Services;
using StudyStash.Core;

namespace StudyStash.App.Tests;

public class DesktopTests
{
    [Fact]
    public void The_login_item_starts_the_app_quietly_with_its_folder()
    {
        string plist = Desktop.LaunchAgentPlist("com.study-stash.app.0a1b2c3d",
            Desktop.LoginArgs("/Applications/Study Stash.app/Contents/MacOS/StudyStash", "/Users/sam/Study & Stash"));

        var doc = new XmlDocument { XmlResolver = null };
        using (var r = XmlReader.Create(new StringReader(plist), new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore }))
            doc.Load(r);
        var strings = doc.SelectNodes("/plist/dict/array/string")!.Cast<XmlNode>().Select(n => n.InnerText).ToList();
        Assert.Equal(["/Applications/Study Stash.app/Contents/MacOS/StudyStash", "--background", "--home", "/Users/sam/Study & Stash"], strings);
        Assert.Equal("com.study-stash.app.0a1b2c3d", doc.SelectSingleNode("/plist/dict/key[.='Label']/following-sibling::string[1]")!.InnerText);
        Assert.Contains("<string>/Users/sam/Study &amp; Stash</string>", plist);
        Assert.Contains("<key>RunAtLoad</key>", plist);
    }

    [Fact]
    public void The_usual_folder_starts_without_naming_it()
    {
        Assert.Equal(["StudyStash", "--background"], Desktop.LoginArgs("StudyStash", Configs.DefaultHome));
        Assert.Equal("\"C:\\Apps\\StudyStash.exe\" --background", Desktop.RunCommand(@"C:\Apps\StudyStash.exe", Configs.DefaultHome));
        using var home = new TempHome();
        Assert.Equal($"\"StudyStash.exe\" --background --home \"{home.Path}\"", Desktop.RunCommand("StudyStash.exe", home.Path));
    }

    [Fact]
    public void Each_folder_has_a_login_item_of_its_own()
    {
        Assert.Equal(("com.study-stash.app", "Study Stash"), Desktop.LoginName(Configs.DefaultHome));
        Assert.Equal(("com.study-stash.app", "Study Stash"), Desktop.LoginName(Configs.DefaultHome + Path.DirectorySeparatorChar));

        using var a = new TempHome();
        using var b = new TempHome();
        var (label, value) = Desktop.LoginName(a.Path);
        Assert.Matches("^com\\.study-stash\\.app\\.[0-9a-f]{8}$", label);
        Assert.Equal($"Study Stash ({label[^8..]})", value);
        Assert.Equal((label, value), Desktop.LoginName(a.Path));
        Assert.NotEqual(label, Desktop.LoginName(b.Path).Label);
    }

    [Fact]
    public void In_tests_nothing_starts_at_login()
    {
        // Checked first: were it off, the next line would write a real login item.
        Assert.True(Desktop.SystemChangesOff);
        using var home = new TempHome();
        var e = Assert.Throws<InvalidOperationException>(() => Desktop.StartAtLogin(true, home.Path));
        Assert.Equal("Start at login is off here.", e.Message);
        Assert.Throws<InvalidOperationException>(() => Desktop.StartAtLogin(false, Configs.DefaultHome));
        Assert.False(Desktop.StartsAtLogin(Configs.DefaultHome));
        Assert.False(LoginItems.System.StartsAtLogin(home.Path));
    }

    [Fact]
    public void One_copy_holds_a_folder_at_a_time()
    {
        using var home = new TempHome();
        try
        {
            Assert.True(Desktop.Claim(home.Path));
            Assert.True(File.Exists(home["app.lock"]));
            Assert.False(Desktop.Claim(home.Path));
            Desktop.Release(home.Path);
            Assert.True(Desktop.Claim(home.Path));
        }
        finally
        {
            Desktop.Release(home.Path);
        }
    }

    // The app.lock holder, as Desktop.TakeOver is handed it: what decides is OlderLibrary.Judge (OlderLibraryTests), so
    // these only check what TakeOver does with each answer. Nothing real is asked or signalled.
    const string Home = "/Users/sam/.study-stash";
    const string Self = "/Applications/Syft Notes.app/Contents/MacOS/StudyStash";

    static OurProcess Holder(string version, string program = "/Applications/Study Stash.app/Contents/MacOS/StudyStash") =>
        new(4242, program, true, Updates.ParseVersion(version), Home);

    [Fact]
    public async Task A_folder_holder_that_cant_be_told_is_handed_off_to_and_never_asked_to_quit()
    {
        var asked = new List<int>();
        var older = new OlderLibrary { AskToQuit = pid => { asked.Add(pid); return true; }, Busy = _ => false };
        Assert.False(await Desktop.TakeOverAsync(Home, null, Self, "0.23.4", older, claim: () => false, log: _ => { }));
        Assert.Empty(asked);
    }

    [Fact]
    public async Task An_older_copy_for_this_folder_is_asked_to_quit_once_and_this_copy_waits_for_the_folder()
    {
        var asked = new List<int>();
        bool quit = false;
        var older = new OlderLibrary { AskToQuit = pid => { asked.Add(pid); quit = true; return true; }, Busy = _ => false };
        // Asked, then the folder is claimed once it has gone: this copy runs.
        Assert.True(await Desktop.TakeOverAsync(Home, Holder("0.22.1"), Self, "0.23.4", older, () => quit, _ => { }));
        Assert.Equal([4242], asked);
        // Asked through the one SIGTERM per process the library's port uses too: never sent again from here.
        Assert.True(older.AskOnce(4242, out bool before));
        Assert.True(before);

        // One that never lets go: asked once, and handed off to when the wait is up.
        var clock = new FakeTimeProvider();
        var stuck = new OlderLibrary { AskToQuit = pid => { asked.Add(pid); return true; }, Busy = _ => false };
        asked.Clear();
        var waiting = Desktop.TakeOverAsync(Home, Holder("0.22.1"), Self, "0.23.4", stuck, () => false, _ => { }, clock);
        Assert.False(waiting.IsCompleted);
        clock.Advance(Desktop.TakeOverWait);
        Assert.False(await waiting.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        Assert.Equal([4242], asked);
    }

    [Fact]
    public async Task Any_other_holder_is_handed_off_to_and_never_asked_to_quit()
    {
        foreach (var (holder, self, busy) in new (OurProcess, string, bool)[]
                 {
                     (Holder("0.23.4"), Self, false), // as new as this copy
                     (Holder("0.24.0"), Self, false), // newer
                     (Holder("0.22.1"), Self, true), // a lecture being recorded or written down there
                     (Holder("0.22.1") with { Home = "/Users/sam/.study-stash-selftest" }, Self, false), // another folder's
                     (Holder("0.22.1") with { Ours = false }, Self, false), // not a Syft Notes program
                     (Holder("0.23.4", program: Self), Self, false), // this very program
                     (Holder("0.22.1"), "/Volumes/Syft Notes/Syft Notes.app/Contents/MacOS/StudyStash", false), // this copy runs from its disk image
                 })
        {
            var asked = new List<int>();
            int claims = 0;
            var older = new OlderLibrary { AskToQuit = pid => { asked.Add(pid); return true; }, Busy = _ => busy };
            Assert.False(await Desktop.TakeOverAsync(Home, holder, self, "0.23.4", older, () => { claims++; return true; }, _ => { }));
            Assert.Empty(asked);
            Assert.Equal(0, claims);
        }
    }

    [Theory]
    [InlineData("show", true)]
    [InlineData("settings", true)]
    [InlineData("settings:Library", true)]
    [InlineData("settings:Notes", true)]
    [InlineData("settings:", false)]
    [InlineData("settings:Nowhere", false)]
    [InlineData("settings:Library; rm", false)]
    [InlineData("delete everything", false)]
    public void Only_known_words_and_settings_pages_are_acted_on(string word, bool known) => Assert.Equal(known, Desktop.IsWord(word));

    [Fact]
    public async Task A_second_copy_hands_off_to_the_first()
    {
        using var home = new TempHome();
        using var stop = new CancellationTokenSource();
        var heard = new BlockingCollection<string>();
        Desktop.Listen(home.Path, heard.Add, stop.Token, post: a => a());
        try
        {
            Assert.True(await Task.Run(() => Desktop.HandOff(home.Path, "record")));
            Assert.True(heard.TryTake(out var word, TimeSpan.FromSeconds(5)));
            Assert.Equal("record", word);

            // Words it doesn't know are ignored.
            Assert.True(await Task.Run(() => Desktop.HandOff(home.Path, "delete everything")));
            Assert.True(await Task.Run(() => Desktop.HandOff(home.Path, "show")));
            Assert.True(heard.TryTake(out word, TimeSpan.FromSeconds(5)));
            Assert.Equal("show", word);

            // "--show panel" / "--show quick" open the dropdown and the quick panel;
            // "--show settings" / "--show settings:Library" open Settings, at one of its pages.
            foreach (string asked in new[] { "panel", "quick", "settings", "settings:Library" })
            {
                Assert.True(await Task.Run(() => Desktop.HandOff(home.Path, asked)));
                Assert.True(heard.TryTake(out word, TimeSpan.FromSeconds(5)));
                Assert.Equal(asked, word);
            }
        }
        finally
        {
            stop.Cancel();
        }
    }

    [Fact]
    public async Task A_copy_that_says_nothing_doesnt_block_the_next()
    {
        using var home = new TempHome();
        using var stop = new CancellationTokenSource();
        var heard = new BlockingCollection<string>();
        Desktop.Listen(home.Path, heard.Add, stop.Token, post: a => a());
        try
        {
            // Wait until it listens, then connect and say nothing.
            Assert.True(await Task.Run(() => Desktop.HandOff(home.Path, "show")));
            Assert.True(heard.TryTake(out _, TimeSpan.FromSeconds(5)));
            using var silent = new NamedPipeClientStream(".", Desktop.PipeName(home.Path), PipeDirection.Out);
            await silent.ConnectAsync(5000, TestContext.Current.CancellationToken);

            Assert.True(await Task.Run(() => Desktop.HandOff(home.Path, "record")));
            Assert.True(heard.TryTake(out var word, TimeSpan.FromSeconds(6)));
            Assert.Equal("record", word);
        }
        finally
        {
            stop.Cancel();
        }
    }

    [Fact]
    public void Nobody_listening_means_no_hand_off()
    {
        using var home = new TempHome();
        var took = System.Diagnostics.Stopwatch.StartNew();
        Assert.False(Desktop.HandOff(home.Path, "show", TimeSpan.FromMilliseconds(600)));
        Assert.InRange(took.Elapsed.TotalSeconds, 0.5, 4);
    }
}
