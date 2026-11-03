using System.Runtime.InteropServices;

namespace StudyStash.Core.Tests;

/// <summary>Which copies the bridge release moves over to Velopack's install (Bridge.cs).</summary>
public class BridgeTests
{
    static Release With(string version, params string[] assets) =>
        new($"v{version}", Updates.ParseVersion(version), "", "", assets.ToDictionary(a => a, a => "https://example.com/" + a));

    [Fact]
    public void An_old_style_install_moves_to_a_release_with_its_download_at_its_own_version_or_newer()
    {
        var inno = new UpdateHost { System = "Windows", AppDir = @"C:\Users\sam\AppData\Local\Programs\Study Stash" };
        var tarball = new UpdateHost { System = "Linux", AppDir = "/home/sam/.local/share/syft-notes" };
        Assert.True(Bridge.Offers(With("0.23.0", "SyftNotes-win-x64-Setup.exe"), inno, "0.23.0", Architecture.X64));
        Assert.True(Bridge.Offers(With("0.24.0", "SyftNotes-win-arm64-Setup.exe"), inno, "0.23.0", Architecture.Arm64));
        Assert.True(Bridge.Offers(With("0.23.0", "SyftNotes-linux-x64.AppImage"), tarball, "0.23.0", Architecture.X64));
        // Not: an older release, a release without this processor's download, a copy Velopack installed (no old
        // install folder), or a Mac (its DMG is already Velopack's bundle).
        Assert.False(Bridge.Offers(With("0.22.1", "SyftNotes-win-x64-Setup.exe"), inno, "0.23.0", Architecture.X64));
        Assert.False(Bridge.Offers(With("0.23.0", "SyftNotes-win-arm64-Setup.exe"), inno, "0.23.0", Architecture.X64));
        Assert.False(Bridge.Offers(With("0.23.0", "SyftNotes-win-x64-Setup.exe"), new UpdateHost { System = "Windows" }, "0.23.0", Architecture.X64));
        Assert.False(Bridge.Offers(With("0.23.0", "SyftNotes-win-x64-Setup.exe"), new UpdateHost { System = "Darwin", AppDir = "/Applications/Syft Notes.app" }, "0.23.0", Architecture.X64));
    }

    [Fact]
    public void The_Windows_move_script_is_in_the_app()
    {
        using var s = typeof(Bridge).Assembly.GetManifestResourceStream("bridge.ps1");
        Assert.NotNull(s);
        string text = new StreamReader(s!).ReadToEnd();
        Assert.Contains("--silent", text);
        Assert.Contains(@"User Pinned\TaskBar", text);
    }
}
