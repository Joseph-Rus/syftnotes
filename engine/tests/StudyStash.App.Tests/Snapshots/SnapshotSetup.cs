using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using DiffEngine;
using SkiaSharp;
using VerifyTests;

namespace StudyStash.App.Tests.Snapshots;

/// <summary>
/// Picture checks: a screen is drawn off screen and compared with the picture kept beside this folder's tests
/// (<c>*.verified.png</c>). The pictures are Linux's: the fonts ship with the app, so a picture is the same on every
/// run there, and CI's Linux job is the only one that checks them (<see cref="Enabled"/>). The comparison is strict
/// (<see cref="Differences"/>): a few stray pixels are allowed, a changed word, colour or line is not (an SSIM score over
/// the whole picture let a changed word and a changed colour token through). A failure leaves the picture it drew as
/// <c>*.received.png</c> (CI keeps those, with the verified ones, as an artifact).
/// <para>The verified pictures are Linux's, taken from the <c>snapshot-pictures</c> artifact of a CI run (a Mac or
/// Windows draws letters a pixel differently, so its pictures never match them). To change one on purpose: let CI fail,
/// look at the artifact's <c>*.received.png</c>, and commit it as the <c>*.verified.png</c>.
/// <c>STUDYSTASH_SNAPSHOTS=1</c> runs the checks on another system to look at a change, but a Mac will report every
/// picture as different.</para>
/// </summary>
public static class SnapshotSetup
{
    /// <summary>Linux, or any system with STUDYSTASH_SNAPSHOTS=1.</summary>
    public static bool Enabled => OperatingSystem.IsLinux() || Environment.GetEnvironmentVariable("STUDYSTASH_SNAPSHOTS") == "1";

    /// <summary>Skips the test it's called from (as a skip, not a pass) anywhere pictures aren't checked.</summary>
    public static void Require() => Assert.SkipUnless(Enabled, NotHere);

    public const string NotHere = "Pictures are checked on Linux (STUDYSTASH_SNAPSHOTS=1 runs them anywhere)";

    /// <summary>What <paramref name="window"/> shows, as a PNG, as it's drawn; closes the window.</summary>
    public static byte[] Png(Window window)
    {
        using var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("nothing rendered");
        var png = new MemoryStream();
        frame.Save(png, PngBitmapEncoderOptions.Default);
        window.Close();
        return png.ToArray();
    }

    /// <summary>Checks what <paramref name="window"/> shows against the verified picture, then closes it.</summary>
    public static async Task Check(Window window) => await VerifyXunit.Verifier.Verify(new MemoryStream(Png(window)), "png");

    /// <summary>A pixel counts as different when any of its colour channels is off by more than this.</summary>
    public const int ChannelDelta = 16;

    /// <summary>How many different pixels a picture may have and still match: an edge pixel or two where the
    /// graphics differ, never a changed letter (a word is hundreds of pixels).</summary>
    public const int PixelsAllowed = 30;

    /// <summary>The pixels of <paramref name="received"/> that differ from <paramref name="verified"/> by more than
    /// <see cref="ChannelDelta"/>, with the box round them; <c>null</c> when the two aren't the same size.</summary>
    public static (int Count, int Left, int Top, int Right, int Bottom)? Differences(byte[] received, byte[] verified)
    {
        using var a = SKBitmap.Decode(received);
        using var b = SKBitmap.Decode(verified);
        if (a.Width != b.Width || a.Height != b.Height) return null;
        int count = 0, left = int.MaxValue, top = int.MaxValue, right = -1, bottom = -1;
        var (pa, pb) = (a.Pixels, b.Pixels);
        for (int i = 0; i < pa.Length; i++)
        {
            var (x, y) = (pa[i], pb[i]);
            if (Math.Abs(x.Red - y.Red) <= ChannelDelta && Math.Abs(x.Green - y.Green) <= ChannelDelta
                && Math.Abs(x.Blue - y.Blue) <= ChannelDelta && Math.Abs(x.Alpha - y.Alpha) <= ChannelDelta) continue;
            count++;
            int px = i % a.Width, py = i / a.Width;
            (left, top, right, bottom) = (Math.Min(left, px), Math.Min(top, py), Math.Max(right, px), Math.Max(bottom, py));
        }
        return (count, left, top, right, bottom);
    }

    static async Task<CompareResult> Compare(Stream received, Stream verified, IReadOnlyDictionary<string, object> context)
    {
        using var r = new MemoryStream();
        using var v = new MemoryStream();
        await received.CopyToAsync(r);
        await verified.CopyToAsync(v);
        if (Differences(r.ToArray(), v.ToArray()) is not { } d) return CompareResult.NotEqual("The picture is not the size of the verified one.");
        return d.Count <= PixelsAllowed
            ? CompareResult.Equal
            : CompareResult.NotEqual($"{d.Count} pixels differ (more than {PixelsAllowed} are allowed), inside x {d.Left}-{d.Right}, y {d.Top}-{d.Bottom}.");
    }

    [ModuleInitializer]
    public static void Init()
    {
        VerifierSettings.RegisterStreamComparer("png", Compare);
        // A failed picture never opens a diff tool (a window on whoever's computer this runs on): the received picture
        // is written beside the verified one, and CI keeps both.
        DiffRunner.Disabled = true;
    }
}
