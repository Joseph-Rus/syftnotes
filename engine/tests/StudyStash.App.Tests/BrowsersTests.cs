using StudyStash.App.Services;
using StudyStash.Core;

namespace StudyStash.App.Tests;

/// <summary>The browsers Study Stash reads Canvas through: which one this computer opens links with, which one the
/// extension is added to, which one Canvas opens in afterwards, and opening one. Nothing here asks the real computer
/// or starts a real browser.</summary>
public class BrowsersTests
{
    /// <summary>LaunchServices' handlers as <c>defaults read … LSHandlers</c> prints them on a Mac, the one for https
    /// being <paramref name="bundle"/>.</summary>
    static string Handlers(string bundle) => $$"""
        (
                {
                LSHandlerContentType = "public.html";
                LSHandlerPreferredVersions =         {
                    LSHandlerRoleAll = "-";
                };
                LSHandlerRoleAll = "com.apple.safari";
            },
                {
                LSHandlerModificationDate = 780690216;
                LSHandlerPreferredVersions =         {
                    LSHandlerRoleAll = "-";
                };
                LSHandlerRoleAll = "{{bundle}}";
                LSHandlerURLScheme = https;
            },
                {
                LSHandlerPreferredVersions =         {
                    LSHandlerRoleAll = "-";
                };
                LSHandlerRoleAll = "com.apple.mail";
                LSHandlerURLScheme = mailto;
            }
        )
        """;

    [Theory]
    [InlineData("com.google.chrome", "Chrome")]
    [InlineData("org.mozilla.firefoxdeveloperedition", "Firefox Developer Edition")]
    [InlineData("company.thebrowser.browser", "Arc")]
    [InlineData("com.apple.safari", "Safari")]
    public void A_macs_default_browser_is_the_one_that_handles_https(string bundle, string name) =>
        Assert.Equal(name, Browsers.MacDefault(Handlers(bundle))?.Name);

    [Fact]
    public void A_mac_never_told_otherwise_opens_links_in_safari_and_a_browser_study_stash_doesnt_know_is_nobody()
    {
        Assert.Equal(Browsers.Safari, Browsers.MacDefault(""));
        Assert.Equal(Browsers.Safari, Browsers.MacDefault(Handlers("com.google.chrome").Replace("LSHandlerURLScheme = https;", "LSHandlerURLScheme = ftp;")));
        Assert.Null(Browsers.MacDefault(Handlers("com.duckduckgo.macos.browser")));
    }

    [Theory]
    [InlineData("ChromeHTML", "Chrome")]
    [InlineData("MSEdgeHTM", "Edge")]
    [InlineData("BraveHTML.A1B2C3D4", "Brave")] // installed for one account: Windows adds a suffix
    [InlineData("FirefoxURL-308046B0AF4A39CB", "Firefox")]
    [InlineData("FirefoxURL-CA9422711AE1A81C", "Firefox Developer Edition")]
    [InlineData("IE.HTTP", null)]
    [InlineData("", null)]
    public void Windows_default_browser_is_read_from_its_https_association(string progId, string? name) =>
        Assert.Equal(name, Browsers.WindowsDefault(progId)?.Name);

    [Fact]
    public void The_browser_to_use_is_the_default_when_the_extension_goes_in_it_else_the_first_here_that_takes_it_else_chrome()
    {
        IReadOnlyList<Browser> here = [Browsers.Chrome, Browsers.Edge, Browsers.Firefox, Browsers.Safari];
        const string published = "https://addons.mozilla.org/firefox/addon/study-stash-for-canvas/";

        Assert.Equal(Browsers.Edge, Browsers.ToUse(Browsers.Edge, here, firefoxAddOn: ""));
        // Safari takes no extension: the first browser here that does.
        Assert.Equal(Browsers.Chrome, Browsers.ToUse(Browsers.Safari, here, firefoxAddOn: ""));
        // Firefox, while there's nowhere to get its add-on: not the one to use, and not offered at all.
        Assert.Equal(Browsers.Chrome, Browsers.ToUse(Browsers.Firefox, here, firefoxAddOn: ""));
        Assert.Equal([Browsers.Chrome, Browsers.Edge], Browsers.Offer(Browsers.Firefox, here, firefoxAddOn: ""));
        // Once the add-on is somewhere, it is: first, since it's the default.
        Assert.Equal([Browsers.Firefox, Browsers.Chrome, Browsers.Edge], Browsers.Offer(Browsers.Firefox, here, published));
        // The one the student added it to before comes ahead of the default.
        Assert.Equal(Browsers.Chrome, Browsers.ToUse(Browsers.Edge, here, firefoxAddOn: "", picked: "Chrome"));
        // Nothing here takes it: Chrome is offered, and says to install a browser when it doesn't open.
        Assert.Equal([Browsers.Chrome], Browsers.Offer(Browsers.Safari, [Browsers.Safari], firefoxAddOn: ""));
    }

    /// <summary>The default browser is looked at first; when the extension can't go in it the student is told why and
    /// which one is used instead, and when nothing here will do, to get Chrome.</summary>
    [Fact]
    public void A_default_browser_the_extension_cant_go_in_gets_advice_and_one_it_can_go_in_gets_none()
    {
        var safari = Browsers.Advise(Browsers.Safari, [Browsers.Chrome, Browsers.Safari], "")!;
        Assert.Equal("Your usual browser, Safari, can’t run the Study Stash extension. Study Stash will use Chrome instead.", safari.Say(Browsers.Chrome));

        // Firefox, while its add-on has nowhere to be fetched from; once it has, Firefox is simply used.
        var firefox = Browsers.Advise(Browsers.Firefox, [Browsers.Edge, Browsers.Firefox], "")!;
        Assert.Equal("Your usual browser, Firefox, can’t run the Study Stash extension yet. Study Stash will use Edge instead.", firefox.Say(Browsers.Edge));
        Assert.Null(Browsers.Advise(Browsers.Firefox, [Browsers.Edge, Browsers.Firefox], "https://addons.mozilla.org/firefox/addon/study-stash-for-canvas/"));

        Assert.Null(Browsers.Advise(Browsers.Edge, [Browsers.Chrome, Browsers.Edge], ""));
        Assert.Null(Browsers.Advise(null, [Browsers.Brave], "")); // the system didn't say, and Brave will do

        // A Mac with only Safari.
        var only = Browsers.Advise(Browsers.Safari, [Browsers.Safari], "")!;
        Assert.True(only.NoneHere);
        Assert.Equal("Your usual browser, Safari, can’t run the Study Stash extension. It runs in Chrome, Edge, Brave, Arc, Opera and Vivaldi, "
            + "and none of them is on this computer. Get Chrome, then press Add to Chrome.", only.Say(Browsers.Chrome));
        Assert.StartsWith("It runs in Chrome", Browsers.Advise(null, [], "")!.Say(Browsers.Chrome));
    }

    [Fact]
    public void Canvas_opens_in_the_browser_the_extension_checked_in_from()
    {
        IReadOnlyList<Browser> here = [Browsers.Chrome, Browsers.Edge, Browsers.Arc, Browsers.Zen];

        Assert.Equal(Browsers.Edge, Browsers.ForCanvas("Edge", picked: "", usual: Browsers.Chrome, here, firefoxAddOn: ""));
        // Arc's extension says it's Chrome, and Zen's says Firefox: where two here say the same, the one the student picked.
        Assert.Equal(Browsers.Chrome, Browsers.ForCanvas("Chrome", picked: "", usual: null, here, firefoxAddOn: ""));
        Assert.Equal(Browsers.Arc, Browsers.ForCanvas("Chrome", picked: "Arc", usual: Browsers.Chrome, here, firefoxAddOn: ""));
        Assert.Equal(Browsers.Zen, Browsers.ForCanvas("Firefox", picked: "", usual: Browsers.Chrome, here, firefoxAddOn: ""));
        // The extension is in a browser that isn't on this computer (the library's own, say): the one to use here.
        Assert.Equal(Browsers.Edge, Browsers.ForCanvas("Brave", picked: "", usual: Browsers.Edge, here, firefoxAddOn: ""));

        // An extension from before 1.6 doesn't say where it is: Study Stash only ever added those to Chrome.
        Assert.Equal("Chrome", Browsers.HeardFrom(new CanvasApi.ExtensionInfo { Version = "1.5", LastSeen = CanvasFixtures.Now }));
        Assert.Equal("Firefox", Browsers.HeardFrom(new CanvasApi.ExtensionInfo { Version = "1.6", Browser = "Firefox" }));
        Assert.Equal("", Browsers.HeardFrom(new CanvasApi.ExtensionInfo()));
        Assert.Equal("", Browsers.HeardFrom(null));
    }

    [Fact]
    public void A_browser_that_cant_be_started_is_said_with_the_browsers_to_choose_from()
    {
        Assert.Equal(Browsers.NotInstalled, Browsers.Open(Browsers.Chrome, null, (_, _, _) => null, _ => @"C:\Chrome\chrome.exe", (_, _) => false));
        // Started, but it said it couldn't (a Mac without it: `open -b` exits 1).
        Assert.Equal(Browsers.NotInstalled, Browsers.Open(Browsers.Chrome, null, (_, _, _) => new ProcResult(1, ""), _ => @"C:\Chrome\chrome.exe", (_, _) => false));
        Assert.Contains("Chrome, Edge, Brave", Browsers.NotInstalled);
    }

    [Fact]
    public void A_browser_windows_cant_find_is_said_without_starting_anything()
    {
        if (!OperatingSystem.IsWindows()) return; // Windows looks for the exe first; a Mac asks `open -b`
        bool ran = false;
        Assert.Equal(Browsers.NotInstalled, Browsers.OpenExtensions(Browsers.Edge, (_, _, _) =>
        {
            ran = true;
            return new ProcResult(0, "");
        }, _ => null, (_, _) => ran = true));
        Assert.False(ran);
    }

    [Fact]
    public void Edge_opens_its_own_extensions_page_and_a_url_opens_in_the_browser_asked_for()
    {
        if (!OperatingSystem.IsMacOS() && !OperatingSystem.IsWindows()) return; // the app opens a browser on a Mac or Windows
        // What was started, and with what: `open` and its arguments on a Mac; the browser's own exe and its on Windows.
        IReadOnlyList<string> args = [];
        ProcResult Run(string exe, IReadOnlyList<string> given, TimeSpan timeout)
        {
            args = [exe, .. given];
            return new ProcResult(0, "");
        }
        bool Start(string exe, IReadOnlyList<string> given)
        {
            args = [exe, .. given];
            return true;
        }
        static string Exe(Browser browser) => @"C:\Program Files\" + browser.WindowsExe[0];

        Assert.Null(Browsers.OpenExtensions(Browsers.Edge, Run, Exe, Start));
        // Edge itself, never Chrome: by its bundle id on a Mac, by its own exe on Windows. chrome://extensions is its page too.
        string[] edge = OperatingSystem.IsMacOS()
            ? ["open", "-b", "com.microsoft.edgemac", "chrome://extensions"]
            : [@"C:\Program Files\Microsoft\Edge\Application\msedge.exe", "chrome://extensions"];
        Assert.Equal(edge, args);

        Assert.Null(Browsers.Open(Browsers.Firefox, "https://school.instructure.com", Run, Exe, Start));
        string[] firefox = OperatingSystem.IsMacOS()
            ? ["open", "-b", "org.mozilla.firefox", "https://school.instructure.com"]
            : [@"C:\Program Files\Mozilla Firefox\firefox.exe", "https://school.instructure.com"];
        Assert.Equal(firefox, args);

        // A Canvas file's address has "&" in it. It reaches the browser whole, as one argument, and nothing reads it
        // as a command on the way: Windows used to go through cmd, which cut the address at the "&" and ran what
        // followed it, so a link written for the purpose started a program.
        const string file = "https://school.instructure.com/courses/7/files/42/download?verifier=aB3&wrap=1&calc.exe";
        Assert.Null(Browsers.Open(Browsers.Chrome, file, Run, Exe, Start));
        Assert.Equal(file, args[^1]);
        Assert.DoesNotContain("cmd", args);
        Assert.Equal(OperatingSystem.IsMacOS() ? 4 : 2, args.Count);
    }

    [Theory]
    [InlineData("--gpu-launcher=calc.exe")] // a browser reads a word that starts with a dash as an order to itself
    [InlineData("-new-window https://school.instructure.com")]
    [InlineData("/Applications/Calculator.app")]
    [InlineData(@"C:\Windows\System32\calc.exe")]
    [InlineData("file:///etc/passwd")]
    [InlineData("javascript:alert(1)")]
    [InlineData("ms-msdt:/id PCWDiagnostic")]
    [InlineData("")]
    public void Only_an_address_is_handed_to_a_browser_and_only_a_web_page_to_the_system(string link)
    {
        bool started = false;
        Assert.Equal(Browsers.NotAnAddress, Browsers.Open(Browsers.Chrome, link, (_, _, _) =>
        {
            started = true;
            return new ProcResult(0, "");
        }, _ => @"C:\Chrome\chrome.exe", (_, _) => started = true));
        Assert.False(started);

        // The same link clicked in a note, or on a Canvas page, is never handed to the system either.
        Assert.False(Dialogs.IsWebLink(link));
        foreach (string system in new[] { "Darwin", "Windows", "Linux" })
            Dialogs.OpenWebLink(link, (_, _, _) =>
            {
                started = true;
                return new ProcResult(0, "");
            }, system);
        Assert.False(started);
    }

    [Fact]
    public void A_web_page_and_an_email_address_still_open()
    {
        Assert.True(Dialogs.IsWebLink("https://school.instructure.com/courses/7?a=1&b=2"));
        Assert.True(Dialogs.IsWebLink("http://example.com"));
        Assert.True(Dialogs.IsWebLink("mailto:prof@school.edu"));
        Assert.True(Browsers.IsAddress("chrome://extensions"));
        Assert.False(Browsers.IsAddress("mailto:prof@school.edu")); // the system's mail app, not a browser's page
        IReadOnlyList<string> opened = [];
        Dialogs.OpenWebLink("https://example.com/a?b=1&c=2", (exe, args, _) =>
        {
            opened = [exe, .. args];
            return new ProcResult(0, "");
        }, "Darwin");
        Assert.Equal(["open", "https://example.com/a?b=1&c=2"], opened);
    }
}
