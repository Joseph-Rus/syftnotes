using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using DiffEngine;
using VerifyTests;

namespace StudyStash.App.Tests.Snapshots;

/// <summary>
/// Picture checks: a screen is drawn off screen and compared with the picture kept beside this folder's tests
/// (<c>*.verified.png</c>). The pictures are Linux's: the fonts ship with the app, so a picture is the same on every
/// run there, and CI's Linux job is the only one that checks them (<see cref="Enabled"/>). A difference too small to
/// see is allowed (SSIM, not a pixel-for-pixel match); a real one fails the test and leaves the picture it drew as
/// <c>*.received.png</c> (CI keeps those, with the verified ones, as an artifact).
/// <para>On another system, <c>STUDYSTASH_SNAPSHOTS=1</c> runs them too, to look at a change; keep what a Mac or
/// Windows draws out of git.</para>
/// </summary>
public static class SnapshotSetup
{
    /// <summary>Linux, or any system with STUDYSTASH_SNAPSHOTS=1.</summary>
    public static bool Enabled => OperatingSystem.IsLinux() || Environment.GetEnvironmentVariable("STUDYSTASH_SNAPSHOTS") == "1";

    /// <summary>Skips the test it's called from (as a skip, not a pass) anywhere pictures aren't checked.</summary>
    public static void Require() => Assert.SkipUnless(Enabled, NotHere);

    public const string NotHere = "Pictures are checked on Linux (STUDYSTASH_SNAPSHOTS=1 runs them anywhere)";

    /// <summary>How alike (1 is identical) a picture must be to its verified one: a font's edge pixel may differ with
    /// the graphics driver, a moved line or a changed word may not.</summary>
    public const double Similarity = 0.995;

    /// <summary>Checks what <paramref name="window"/> shows (as it's drawn, to the pixel) against the verified picture,
    /// then closes it.</summary>
    public static async Task Check(Window window, [CallerMemberName] string? name = null)
    {
        using var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("nothing rendered");
        var png = new MemoryStream();
        frame.Save(png, PngBitmapEncoderOptions.Default);
        png.Position = 0;
        window.Close();
        await VerifyXunit.Verifier.Verify(png, "png");
    }

    [ModuleInitializer]
    public static void Init()
    {
        VerifierSettings.UseSsimForPng(Similarity);
        // A failed picture never opens a diff tool (a window on whoever's computer this runs on): the received picture
        // is written beside the verified one, and CI keeps both.
        DiffRunner.Disabled = true;
    }
}
