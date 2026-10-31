using StudyStash.App.Platform;
using StudyStash.App.Services;

namespace StudyStash.App.Tests;

/// <summary>The Linux desktop around the app: starting at login from an XDG autostart entry, the student's browsers
/// found by their commands and the default one by its desktop entry, and the Windows look Linux shares.</summary>
public class LinuxDesktopTests
{
    [Fact]
    public void The_autostart_entry_lives_where_every_desktop_looks_for_it()
    {
        Assert.Equal(Path.Combine("/home/zoë/.config", "autostart", "com.study-stash.app.desktop"),
            Desktop.AutostartEntry("com.study-stash.app", "/home/zoë/.config"));
        // A second settings folder gets an entry of its own, as it gets a LaunchAgent and a Run value of its own.
        var (label, _) = Desktop.LoginName("/tmp/another profile");
        Assert.StartsWith("com.study-stash.app.", label);
        Assert.EndsWith(label + ".desktop", Desktop.AutostartEntry(label, "/c"));
    }

    [Fact]
    public void The_autostart_entry_starts_the_app_quietly_with_its_arguments_quoted_as_the_spec_says()
    {
        string file = Desktop.AutostartDesktopFile(["/home/zoë/.local/share/syft-notes/StudyStash", "--background", "--home", "/home/zoë/My \"Notes\" $5 100%"]);
        var lines = file.Split('\n');
        Assert.Equal("[Desktop Entry]", lines[0]);
        Assert.Contains("Type=Application", lines);
        Assert.Contains("Name=Syft Notes", lines);
        Assert.Contains("NoDisplay=true", lines);
        Assert.Contains("X-GNOME-Autostart-enabled=true", lines);
        // Letters of any language need no quotes; spaces, quotes, $ and % do.
        Assert.Contains("Exec=/home/zoë/.local/share/syft-notes/StudyStash --background --home \"/home/zoë/My \\\"Notes\\\" \\$5 100%%\"", lines);
        // A plain path needs no quotes.
        Assert.Contains("Exec=/opt/syft-notes/StudyStash --background", Desktop.AutostartDesktopFile(["/opt/syft-notes/StudyStash", "--background"]).Split('\n'));
    }

    [Fact]
    public void A_linux_browser_is_found_by_the_command_its_package_puts_on_the_path()
    {
        var path = new Dictionary<string, string> { ["google-chrome"] = "/usr/bin/google-chrome", ["chromium-browser"] = "/snap/bin/chromium-browser", ["firefox"] = "/usr/bin/firefox" };
        string? Which(string name) => path.GetValueOrDefault(name);
        Assert.Equal("/usr/bin/google-chrome", Browsers.LinuxCommand(Browsers.Chrome, Which));
        Assert.Equal("/snap/bin/chromium-browser", Browsers.LinuxCommand(Browsers.Chromium, Which));
        Assert.Null(Browsers.LinuxCommand(Browsers.Edge, Which));
        Assert.Null(Browsers.LinuxCommand(Browsers.Safari, Which)); // no Linux Safari, nor Arc
        Assert.Null(Browsers.LinuxCommand(Browsers.Arc, Which));
        // Every browser that has a Linux build says how it's started and how it's named as the default.
        Assert.All(Browsers.Known.Where(b => b != Browsers.Safari && b != Browsers.Arc), b =>
        {
            Assert.NotEmpty(b.LinuxCommands);
            Assert.NotEmpty(b.LinuxDesktopIds);
        });
    }

    [Theory]
    [InlineData("google-chrome.desktop", "Chrome")]
    [InlineData("firefox_firefox.desktop\n", "Firefox")]
    [InlineData("org.mozilla.firefox.desktop", "Firefox")]
    [InlineData("brave-browser.desktop", "Brave")]
    [InlineData("chromium-browser.desktop", "Chromium")]
    [InlineData("microsoft-edge.desktop", "Edge")]
    public void The_default_linux_browser_is_known_by_its_desktop_entry(string said, string browser)
    {
        Assert.Equal(browser, Browsers.LinuxDefault(said)?.Name);
    }

    [Fact]
    public void A_default_browser_syft_notes_doesnt_know_is_no_browser()
    {
        Assert.Null(Browsers.LinuxDefault("epiphany.desktop"));
        Assert.Null(Browsers.LinuxDefault(""));
        Assert.Null(Browsers.LinuxDefault(null));
    }

    [Fact]
    public void Linux_wears_the_windows_look()
    {
        if (!OperatingSystem.IsLinux()) return;
        var saved = Environment.GetEnvironmentVariable("STUDYSTASH_SKIN");
        Environment.SetEnvironmentVariable("STUDYSTASH_SKIN", null);
        try
        {
            Assert.Equal(SkinKind.Win, Skin.FromEnvironment());
        }
        finally
        {
            Environment.SetEnvironmentVariable("STUDYSTASH_SKIN", saved);
        }
    }

    [Fact]
    public void The_welcome_on_linux_says_this_computer()
    {
        Assert.Equal("computer", Core.Machine.DeviceForLook(mac: false, linux: true));
        Assert.Equal("PC", Core.Machine.DeviceForLook(mac: false, linux: false));
    }
}
