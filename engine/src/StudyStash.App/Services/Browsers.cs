using System.Text;
using System.Text.RegularExpressions;
using StudyStash.Core;
using StudyStash.Core.Canvas;

namespace StudyStash.App.Services;

/// <summary>What a browser is built on, which is how the Canvas extension gets into it: Chrome's family loads the
/// folder Study Stash writes ("Load unpacked"); Firefox's family takes a signed add-on, connected by a pasted code;
/// Safari takes neither.</summary>
public enum BrowserFamily
{
    Chromium,
    Firefox,
    Safari,
}

/// <summary>
/// A browser Study Stash knows: its name for a sentence ("Edge"), its family, the Mac app's names (the usual one
/// first) and bundle id, where its exe lives on Windows (under Program Files, Program Files (x86) or the account's
/// local app data), and how Windows names it as the default browser (the start of its ProgId). <see cref="Says"/> is
/// what its extension tells the library it runs in, where that isn't its own name: Arc says Chrome; Zen and Firefox
/// Developer Edition say Firefox.
/// </summary>
public sealed record Browser(string Name, BrowserFamily Family, IReadOnlyList<string> MacApps, string MacBundle, IReadOnlyList<string> WindowsExe,
    IReadOnlyList<string> ProgIds, string? SaysItIs = null)
{
    public string Says => SaysItIs ?? Name;
}

/// <summary>
/// What the extension step tells a student it can't offer their usual browser to: that browser (<see cref="Usual"/>:
/// Safari, or Firefox before its add-on is out; null when the system didn't say which they use), and whether nothing
/// on this computer can take the extension (<see cref="NoneHere"/>: then there's one to get).
/// </summary>
public sealed record BrowserAdvice(Browser? Usual, bool NoneHere)
{
    /// <summary>The sentences: why not their usual browser, then the one Study Stash will use instead
    /// (<paramref name="use"/>, the step's browser), or to get Chrome when none here will do.</summary>
    public string Say(Browser use)
    {
        string why = Usual switch
        {
            null => "",
            { Family: BrowserFamily.Firefox } => $"Your usual browser, {Usual.Name}, can’t run the Study Stash extension yet. ",
            _ => $"Your usual browser, {Usual.Name}, can’t run the Study Stash extension. ",
        };
        return NoneHere
            ? $"{why}It runs in {Browsers.Family()}, and none of them is on this computer. Get Chrome, then press Add to Chrome."
            : $"{why}Study Stash will use {use.Name} instead.";
    }
}

/// <summary>
/// The browsers the student may read Canvas in: which are on this computer, which one the system opens links with,
/// which one to add the extension to, and opening one, a URL inside it, or its extensions page — each through one
/// <see cref="Machine.Run"/> call, so a computer without it reads back as one plain sentence instead of a swallowed
/// exception. Tests hand in their own <see cref="Runner"/> and never start a real browser.
/// </summary>
public static class Browsers
{
    public static readonly Browser Chrome = new("Chrome", BrowserFamily.Chromium, ["Google Chrome"], "com.google.Chrome",
        [@"Google\Chrome\Application\chrome.exe"], ["ChromeHTML"]);
    public static readonly Browser Edge = new("Edge", BrowserFamily.Chromium, ["Microsoft Edge"], "com.microsoft.edgemac",
        [@"Microsoft\Edge\Application\msedge.exe"], ["MSEdgeHTM"]);
    public static readonly Browser Brave = new("Brave", BrowserFamily.Chromium, ["Brave Browser"], "com.brave.Browser",
        [@"BraveSoftware\Brave-Browser\Application\brave.exe"], ["BraveHTML"]);
    public static readonly Browser Arc = new("Arc", BrowserFamily.Chromium, ["Arc"], "company.thebrowser.Browser",
        [@"Microsoft\WindowsApps\Arc.exe"], [], SaysItIs: "Chrome");
    public static readonly Browser Opera = new("Opera", BrowserFamily.Chromium, ["Opera"], "com.operasoftware.Opera",
        [@"Programs\Opera\opera.exe", @"Opera\opera.exe", @"Programs\Opera\launcher.exe", @"Opera\launcher.exe"], ["OperaStable"]);
    public static readonly Browser Vivaldi = new("Vivaldi", BrowserFamily.Chromium, ["Vivaldi"], "com.vivaldi.Vivaldi",
        [@"Vivaldi\Application\vivaldi.exe"], ["VivaldiHTM"]);
    public static readonly Browser Chromium = new("Chromium", BrowserFamily.Chromium, ["Chromium"], "org.chromium.Chromium",
        [@"Chromium\Application\chrome.exe"], ["ChromiumHTM"]);
    public static readonly Browser Firefox = new("Firefox", BrowserFamily.Firefox, ["Firefox"], "org.mozilla.firefox",
        [@"Mozilla Firefox\firefox.exe"], ["FirefoxURL", "FirefoxHTML"]);
    public static readonly Browser FirefoxDeveloper = new("Firefox Developer Edition", BrowserFamily.Firefox, ["Firefox Developer Edition"],
        "org.mozilla.firefoxdeveloperedition", [@"Firefox Developer Edition\firefox.exe"], ["FirefoxURL-CA9422711AE1A81C", "FirefoxHTML-CA9422711AE1A81C"],
        SaysItIs: "Firefox");
    public static readonly Browser Zen = new("Zen", BrowserFamily.Firefox, ["Zen", "Zen Browser"], "app.zen-browser.zen",
        [@"Zen Browser\zen.exe"], ["ZenURL", "ZenHTML"], SaysItIs: "Firefox");
    public static readonly Browser LibreWolf = new("LibreWolf", BrowserFamily.Firefox, ["LibreWolf"], "io.gitlab.librewolf-community.librewolf",
        [@"LibreWolf\librewolf.exe"], ["LibreWolfURL", "LibreWolfHTM"]);
    public static readonly Browser Waterfox = new("Waterfox", BrowserFamily.Firefox, ["Waterfox"], "net.waterfox.waterfox",
        [@"Waterfox\waterfox.exe"], ["WaterfoxURL", "WaterfoxHTML"]);
    public static readonly Browser Safari = new("Safari", BrowserFamily.Safari, ["Safari"], "com.apple.Safari", [], []);

    /// <summary>Every browser Study Stash knows, in the order it offers them: Chrome's family, then Firefox's.</summary>
    public static readonly IReadOnlyList<Browser> Known =
        [Chrome, Edge, Brave, Arc, Opera, Vivaldi, Chromium, Firefox, FirefoxDeveloper, Zen, LibreWolf, Waterfox, Safari];

    /// <summary>What the student reads when no browser could be started: the ones the extension goes in.</summary>
    public static string NotInstalled => $"Study Stash reads Canvas through {Family("or")}. Install one, then try again.";

    /// <summary>The browsers the extension goes in, for a sentence: "Chrome, Edge, Brave, Arc, Opera and Vivaldi",
    /// with Firefox once its add-on has somewhere to be fetched from.</summary>
    public static string Family(string joined = "and") => Extension.FirefoxAddOn.Length > 0
        ? $"Chrome, Edge, Brave, Arc, Opera, Vivaldi {joined} Firefox"
        : $"Chrome, Edge, Brave, Arc, Opera {joined} Vivaldi";

    /// <summary>Where a student without any of them gets one: Chrome's own download page.</summary>
    public const string GetChrome = "https://www.google.com/chrome/";

    /// <summary>The browser of that name ("Edge"), or null for one Study Stash doesn't know (and for "").</summary>
    public static Browser? Named(string? name) =>
        string.IsNullOrEmpty(name) ? null : Known.FirstOrDefault(b => b.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Whether the extension can go in a browser: always in Chrome's family; in Firefox's only once there's
    /// somewhere to get the add-on (<see cref="Extension.FirefoxAddOn"/>, or <paramref name="firefoxAddOn"/>); never
    /// in Safari.</summary>
    public static bool CanAdd(Browser browser, string? firefoxAddOn = null) => browser.Family switch
    {
        BrowserFamily.Chromium => true,
        BrowserFamily.Firefox => (firefoxAddOn ?? Extension.FirefoxAddOn).Length > 0,
        _ => false,
    };

    // ---- what the student has chosen, and what the library heard ----

    /// <summary>The browser the student added the extension to in the connect flow (its name, kept in app.json); ""
    /// until they have.</summary>
    public static string Picked { get; set; } = "";

    /// <summary>The browser the library last heard the extension from (<see cref="HeardFrom"/>); "" until one has
    /// checked in.</summary>
    public static string Heard { get; set; } = "";

    /// <summary>The browser an extension runs in, for opening it: the one it names. An extension from before 1.6
    /// doesn't say, and Study Stash only ever added those to Chrome. "" when none has checked in.</summary>
    public static string HeardFrom(CanvasApi.ExtensionInfo? extension) =>
        extension is null ? ""
        : extension.Browser.Length > 0 ? extension.Browser
        : extension.Seen is not null || extension.LastSeen is not null || extension.Version.Length > 0 ? Chrome.Name : "";

    // ---- this computer ----

    /// <summary>The known browsers on this computer, in <see cref="Known"/>'s order. On a Mac the one the system
    /// opens links with (<paramref name="usual"/>) counts wherever its app is: it's opened by its bundle id. Windows
    /// opens a browser by its exe, so there it counts only where that's found.</summary>
    public static IReadOnlyList<Browser> Installed(Browser? usual = null) =>
        [.. Known.Where(b => b == usual && OperatingSystem.IsMacOS() || IsInstalled(b))];

    public static bool IsInstalled(Browser browser) =>
        OperatingSystem.IsMacOS() ? MacApp(browser) is not null : OperatingSystem.IsWindows() && WindowsExe(browser) is not null;

    /// <summary>A Mac: the browser's app in Applications (everyone's, or just this account's), or null.</summary>
    static string? MacApp(Browser browser) =>
        new[] { "/Applications", Path.Combine(Py.UserHome(), "Applications") }
            .SelectMany(dir => browser.MacApps.Select(app => Path.Combine(dir, app + ".app")))
            .FirstOrDefault(Directory.Exists);

    /// <summary>Windows: the browser's exe where its installer puts it (for everyone, or just this account), or null.</summary>
    public static string? WindowsExe(Browser browser) =>
        new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.LocalApplicationData }
            .Select(f => Environment.GetFolderPath(f))
            .Where(d => d.Length > 0)
            .SelectMany(d => browser.WindowsExe.Select(exe => Path.Combine(d, exe)))
            .FirstOrDefault(File.Exists);

    /// <summary>The browser this computer opens links with, when it's one Study Stash knows; null when it isn't, or
    /// the system didn't say. A Mac is asked for LaunchServices' handlers, Windows for the https association the
    /// student chose.</summary>
    public static Browser? Default(Runner? run = null)
    {
        if (OperatingSystem.IsMacOS())
        {
            var asked = (run ?? Machine.Run)("defaults", ["read", "com.apple.LaunchServices/com.apple.launchservices.secure", "LSHandlers"], TimeSpan.FromSeconds(5));
            return MacDefault(asked is { ExitCode: 0 } ? asked.Stdout : "");
        }
        if (OperatingSystem.IsWindows())
        {
            try
            {
                return WindowsDefault(Microsoft.Win32.Registry.GetValue(
                    @"HKEY_CURRENT_USER\Software\Microsoft\Windows\Shell\Associations\UrlAssociations\https\UserChoice", "ProgId", null) as string);
            }
            catch (Exception e) when (e is System.Security.SecurityException or IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }
        return null;
    }

    /// <summary>A Mac's default browser from LaunchServices' handlers, as <c>defaults read … LSHandlers</c> prints
    /// them: the one for https. A Mac that was never told otherwise has none, and opens links in Safari.</summary>
    public static Browser? MacDefault(string handlers) =>
        MacHttpsHandler(handlers) is { Length: > 0 } bundle ? Known.FirstOrDefault(b => b.MacBundle.Equals(bundle, StringComparison.OrdinalIgnoreCase)) : Safari;

    /// <summary>The bundle id that handles https in LaunchServices' handlers ("" when none does). Each handler is a
    /// <c>{ … }</c> entry; the preferred versions nested inside one have a role of their own, which isn't it.</summary>
    public static string MacHttpsHandler(string handlers)
    {
        int depth = 0;
        var entry = new StringBuilder();
        foreach (char c in handlers)
        {
            if (c == '{')
            {
                if (depth++ == 0) entry.Clear();
            }
            else if (c == '}')
            {
                if (--depth != 0) continue;
                string text = entry.ToString();
                if (Regex.IsMatch(text, @"LSHandlerURLScheme\s*=\s*""?https""?\s*;") && Regex.Match(text, @"LSHandlerRoleAll\s*=\s*""?([^"";\s]+)""?\s*;") is { Success: true } role)
                    return role.Groups[1].Value;
            }
            else if (depth == 1)
            {
                entry.Append(c);
            }
        }
        return "";
    }

    /// <summary>Windows' default browser from the ProgId of its https association ("ChromeHTML", "MSEdgeHTM",
    /// "FirefoxURL-308046B0AF4A39CB"): the known browser whose own ProgId it starts with, the longest when two do.</summary>
    public static Browser? WindowsDefault(string? progId) =>
        string.IsNullOrEmpty(progId) ? null
        : Known.SelectMany(b => b.ProgIds.Select(id => (Browser: b, Id: id)))
            .Where(p => progId.StartsWith(p.Id, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(p => p.Id.Length)
            .Select(p => p.Browser)
            .FirstOrDefault();

    // ---- which one ----

    /// <summary>The browsers to offer the extension to: the installed ones it can go in, or just Chrome when there
    /// are none (the student is told to install one when it doesn't open).</summary>
    public static IReadOnlyList<Browser> Choices(IReadOnlyList<Browser> installed, string? firefoxAddOn = null) =>
        installed.Where(b => CanAdd(b, firefoxAddOn)).ToList() is { Count: > 0 } can ? can : [Chrome];

    /// <summary>The browser to add the extension to: the one the student picked before, else the system's default
    /// (<paramref name="usual"/>) when the extension can go in it, else the first installed one it can go in, else
    /// Chrome.</summary>
    public static Browser ToUse(Browser? usual, IReadOnlyList<Browser> installed, string? firefoxAddOn = null, string picked = "")
    {
        var can = Choices(installed, firefoxAddOn);
        return Named(picked) is { } mine && can.Contains(mine) ? mine : usual is not null && can.Contains(usual) ? usual : can[0];
    }

    /// <summary>The browser the student reads Canvas in, for opening it or a Canvas page: the installed one the
    /// extension says it runs in (<paramref name="heard"/>; where several say that name, the one picked, else the
    /// default), else <see cref="ToUse"/>.</summary>
    public static Browser ForCanvas(string heard, string picked, Browser? usual, IReadOnlyList<Browser> installed, string? firefoxAddOn = null)
    {
        var said = heard.Length == 0 ? [] : installed.Where(b => b.Says.Equals(heard, StringComparison.OrdinalIgnoreCase)).ToList();
        if (said.Count == 0) return ToUse(usual, installed, firefoxAddOn, picked);
        var mine = Named(picked);
        return said.FirstOrDefault(b => b == mine) ?? said.FirstOrDefault(b => b == usual) ?? said[0];
    }

    /// <summary>What to tell the student when the extension can't go in the browser they usually use
    /// (<paramref name="usual"/>), or in nothing on this computer; null when it goes in their usual browser, or the
    /// system didn't say which that is and another here will do.</summary>
    public static BrowserAdvice? Advise(Browser? usual, IReadOnlyList<Browser> installed, string? firefoxAddOn = null)
    {
        bool none = !installed.Any(b => CanAdd(b, firefoxAddOn));
        bool fine = usual is null || CanAdd(usual, firefoxAddOn);
        return fine && !none ? null : new BrowserAdvice(fine ? null : usual, none);
    }

    /// <summary>This computer's <see cref="Advise(Browser?, IReadOnlyList{Browser}, string?)"/>.</summary>
    public static BrowserAdvice? Advise()
    {
        var usual = Default();
        return Advise(usual, Installed(usual));
    }

    /// <summary>The browsers to offer the extension to (<see cref="Choices"/>), the one to use (<see cref="ToUse"/>)
    /// first.</summary>
    public static IReadOnlyList<Browser> Offer(Browser? usual, IReadOnlyList<Browser> installed, string? firefoxAddOn = null, string picked = "")
    {
        var first = ToUse(usual, installed, firefoxAddOn, picked);
        return [first, .. Choices(installed, firefoxAddOn).Where(b => b != first)];
    }

    /// <summary>This computer's <see cref="Offer(Browser?, IReadOnlyList{Browser}, string?, string)"/>, the student's
    /// pick first.</summary>
    public static IReadOnlyList<Browser> Offer()
    {
        var usual = Default();
        return Offer(usual, Installed(usual), picked: Picked);
    }

    /// <summary>This computer's <see cref="ForCanvas(string, string, Browser?, IReadOnlyList{Browser}, string?)"/>, from
    /// what the library heard and the student picked.</summary>
    public static Browser ForCanvas()
    {
        var usual = Default();
        return ForCanvas(Heard, Picked, usual, Installed(usual));
    }

    // ---- opening one ----

    /// <summary>What <see cref="Open"/> says of a target that isn't a page a browser shows.</summary>
    public const string NotAnAddress = "That link isn't a web address.";

    /// <summary>Whether <paramref name="target"/> is something to hand a browser: a web page, or one of its own pages
    /// (chrome://extensions). The addresses come from Canvas and from notes, and a browser takes anything else as
    /// an order to itself (a word starting with a dash is a switch, and some switches start programs).</summary>
    public static bool IsAddress(string target) =>
        Uri.TryCreate(target, UriKind.Absolute, out var u) && u.Scheme is "http" or "https" or "chrome";

    /// <summary>Opens the browser, or an address inside it when one is given. Null on success;
    /// <see cref="NotInstalled"/> when nothing could be started, <see cref="NotAnAddress"/> (and nothing started) for
    /// a target that isn't one. A Mac opens it by its bundle id; <paramref name="windowsExe"/> finds its exe on Windows
    /// and <paramref name="start"/> starts it there (tests give their own).</summary>
    public static string? Open(Browser browser, string? target = null, Runner? run = null, Func<Browser, string?>? windowsExe = null,
        Func<string, IReadOnlyList<string>, bool>? start = null)
    {
        if (target is not null && !IsAddress(target)) return NotAnAddress;
        if (OperatingSystem.IsMacOS())
            return (run ?? Machine.Run)("open", target is null ? ["-b", browser.MacBundle] : ["-b", browser.MacBundle, target], TimeSpan.FromSeconds(10))
                is { ExitCode: 0 } ? null : NotInstalled;
        // Windows: the browser's own program, with the address as one argument of its own. Never through cmd, which
        // reads an "&" in an address as the end of the command: the link was cut there, and what followed it was run
        // as a command of its own. Not waited for (with no browser open yet, what starts is the browser itself), and
        // by its path, so a missing one is said here rather than in Windows' own "cannot find" box.
        if (OperatingSystem.IsWindows() && (windowsExe ?? WindowsExe)(browser) is { } exe)
            return (start ?? Machine.Start)(exe, target is null ? [] : [target]) ? null : NotInstalled;
        return NotInstalled;
    }

    /// <summary>Opens a browser of Chrome's family at its own extensions page, where "Load unpacked" picks up the
    /// folder <see cref="Extension"/> wrote: <c>chrome://extensions</c> is that page in every one of them (Edge shows
    /// it as edge://extensions, Brave as brave://extensions).</summary>
    public static string? OpenExtensions(Browser browser, Runner? run = null, Func<Browser, string?>? windowsExe = null,
        Func<string, IReadOnlyList<string>, bool>? start = null) =>
        Open(browser, "chrome://extensions", run, windowsExe, start);
}
