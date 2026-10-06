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
        Assert.Equal(Browsers.NotInstalled, Browsers.Open(Browsers.Chrome, null, (_, _, _) => null, _ => @"C:\Chrome\chrome.exe"));
        // Started, but it said it couldn't (a Mac without it: `open -b` exits 1).
        Assert.Equal(Browsers.NotInstalled, Browsers.Open(Browsers.Chrome, null, (_, _, _) => new ProcResult(1, ""), _ => @"C:\Chrome\chrome.exe"));
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
        }, _ => null));
        Assert.False(ran);
    }

    [Fact]
    public void Edge_opens_its_own_extensions_page_and_a_url_opens_in_the_browser_asked_for()
    {
        if (!OperatingSystem.IsMacOS() && !OperatingSystem.IsWindows()) return; // the app opens a browser on a Mac or Windows
        IReadOnlyList<string> args = [];
        ProcResult Run(string exe, IReadOnlyList<string> given, TimeSpan timeout)
        {
            args = given;
            return new ProcResult(0, "");
        }
        static string Exe(Browser browser) => @"C:\Program Files\" + browser.WindowsExe[0];

        Assert.Null(Browsers.OpenExtensions(Browsers.Edge, Run, Exe));
        // Edge itself, never Chrome: by its bundle id on a Mac, by its own exe on Windows. chrome://extensions is its page too.
        string[] edge = OperatingSystem.IsMacOS()
            ? ["-b", "com.microsoft.edgemac", "chrome://extensions"]
            : ["/c", "start", "", @"C:\Program Files\Microsoft\Edge\Application\msedge.exe", "chrome://extensions"];
        Assert.Equal(edge, args);

        Assert.Null(Browsers.Open(Browsers.Firefox, "https://school.instructure.com", Run, Exe));
        string[] firefox = OperatingSystem.IsMacOS()
            ? ["-b", "org.mozilla.firefox", "https://school.instructure.com"]
            : ["/c", "start", "", @"C:\Program Files\Mozilla Firefox\firefox.exe", "https://school.instructure.com"];
        Assert.Equal(firefox, args);
    }
}
