using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Media;
using StudyStash.Core;

namespace StudyStash.App.Services;

/// <summary>"Now", in the zone the student reads times in. Real code reads the system clock; tests fix it to the
/// design's moment so no assertion depends on when it runs.</summary>
public sealed record CanvasClock(Func<DateTimeOffset> Now, TimeZoneInfo Zone);

/// <summary>Everything a Canvas view model does to the machine, or asks of it, as delegates: real code looks for the
/// browsers on this computer, launches one and reveals files in the Finder/Explorer; tests just record what was asked
/// for and launch nothing. <see cref="OpenInBrowser"/> and <see cref="OpenBrowser"/> open the browser the student
/// reads Canvas in. <see cref="Browsers"/> is the browsers the extension can be added to here, the one to use first;
/// <see cref="RememberBrowser"/> keeps the one the student added it to. <see cref="OpenExtensions"/> (a browser of
/// Chrome's family at its extensions page) and <see cref="OpenAddOn"/> (the Firefox copy's page, in a browser of
/// Firefox's family) hand back a sentence when the browser couldn't be started, null when it could. <see cref="Advise"/> says when the
/// student's usual browser can't take the extension, or nothing here can (null when there's nothing to say).</summary>
public sealed record CanvasActions(
    Action<string> OpenUrl,
    Action<string> OpenInBrowser,
    Action OpenBrowser,
    Func<IReadOnlyList<Browser>> Browsers,
    Action<Browser> RememberBrowser,
    Func<Browser, string?> OpenExtensions,
    Func<Browser, string?> OpenAddOn,
    Action<string> RevealFolder,
    Action<string> OpenFile,
    Func<string, string, string> PrepareExtension,
    Action<string> Copy,
    Func<BrowserAdvice?>? Advise = null);

/// <summary>What every Canvas view model reads: the library's Canvas client (null before the laptop is paired with
/// one), the clock, a class's dot colour, and the actions above. Tests build one whole so nothing a view model does
/// ever depends on the real machine or the real time.</summary>
public sealed record CanvasContext(CanvasClient? Client, CanvasClock Clock, Func<string, IBrush> DotOf, CanvasActions Actions, string Home)
{
    /// <summary>The real context: a client built from the app's own library connection (null when the laptop isn't
    /// connected to one), the system clock, and actions that really launch the browser and reveal files. The browser
    /// the student added the extension to last time (app.json) is the one to use again.</summary>
    public static CanvasContext For(AppHost host)
    {
        var cc = host.Client();
        var client = cc.ServerUrl.Length > 0 ? new CanvasClient(cc.ServerUrl, cc.PoolKey) : null;
        Browsers.Picked = host.Settings.CanvasBrowser;
        var actions = new CanvasActions(
            OpenUrl: url => Dialogs.OpenUrl(url),
            OpenInBrowser: OpenInBrowser,
            OpenBrowser: () => OpenInBrowser(null),
            Browsers: () => Browsers.Offer(),
            RememberBrowser: browser =>
            {
                Browsers.Picked = browser.Name;
                if (host.Settings.CanvasBrowser != browser.Name) host.Save(s => s.CanvasBrowser = browser.Name);
            },
            OpenExtensions: browser => Browsers.OpenExtensions(browser),
            OpenAddOn: browser => Browsers.Open(browser, Core.Canvas.Extension.FirefoxAddOn),
            RevealFolder: Reveal,
            OpenFile: path => Machine.Open(path),
            PrepareExtension: (key, canvasUrl) => Core.Canvas.Extension.EnsureFor(host.Home, cc.ServerUrl, key, canvasUrl).Path,
            Copy: Copy,
            Advise: () => Browsers.Advise());
        return new CanvasContext(client, new CanvasClock(() => DateTimeOffset.Now, TimeZoneInfo.Local), cls => Skin.ClassDot(host.ColorOf(cls)), actions, host.Home);
    }

    /// <summary>Shows the extension's folder for the browser's Load unpacked. On a Mac and on Windows it's shown
    /// selected in the folder that holds it (Finder, Explorer), so it can be dragged onto the browser's extensions page
    /// or its path copied into the browser's picker; elsewhere the folder opens.</summary>
    static void Reveal(string dir)
    {
        var select = OperatingSystem.IsWindows() ? new System.Diagnostics.ProcessStartInfo("explorer.exe", $"/select,\"{dir}\"")
            : OperatingSystem.IsMacOS() ? new System.Diagnostics.ProcessStartInfo("open", ["-R", dir])
            : null;
        if (select is not null)
        {
            try
            {
                select.UseShellExecute = false;
                using var _ = System.Diagnostics.Process.Start(select);
                return;
            }
            catch (System.ComponentModel.Win32Exception)
            {
            }
        }
        Machine.Open(dir);
    }

    /// <summary>Opens the browser the student reads Canvas in (<see cref="Browsers.ForCanvas()"/>), at a URL when one
    /// is given. A link still opens when that browser can't be started: in whatever the system opens links with.</summary>
    static void OpenInBrowser(string? url)
    {
        if (Browsers.Open(Browsers.ForCanvas(), url) is not null && url is not null) Dialogs.OpenUrl(url);
    }

    /// <summary>Puts text on the clipboard, through the window the student is in.</summary>
    static void Copy(string text)
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime { Windows: var windows }) return;
        if ((windows.FirstOrDefault(w => w.IsActive) ?? windows.FirstOrDefault(w => w.IsVisible))?.Clipboard is { } clipboard) _ = clipboard.SetTextAsync(text);
    }
}
