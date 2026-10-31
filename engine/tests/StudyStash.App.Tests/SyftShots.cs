using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using StudyStash.App.Controls;
using StudyStash.App.Services;
using StudyStash.App.ViewModels;
using StudyStash.App.Views;
using StudyStash.Core;
using static StudyStash.App.Controls.Keyframes;

namespace StudyStash.App.Tests;

/// <summary>
/// Syft Notes' pixel s where the app shows it (Controls/Syft): the keyframes are the motion reference's, the loaders keep
/// moving with the UI thread busy, the splash goes when it should, and pictures into STUDYSTASH_SHOTS of each piece (the
/// splash screens and loaders frame by frame, every look of the mark) and of the screens they're on, light and dark.
/// </summary>
public class SyftShots
{
    static readonly Size Wide = new(1408, 988);

    // ---- The keyframes, against syft-motion.html's own numbers ----

    [Fact]
    public void The_keyframes_are_the_references()
    {
        // L1: tr 2s linear, opacity 0 → 1 by 6%, held to 30%, out by 52%; p5 starts 0.375 s late.
        Assert.Equal(1, SyftLoader.Traces[0].At(0.12).Opacity, 3);
        Assert.Equal(0.5, SyftLoader.Traces[0].At(0.82).Opacity, 3);
        Assert.Equal(0, SyftLoader.Traces[0].At(1.5).Opacity, 3);
        Assert.Equal(0, SyftLoader.Traces[5].At(0.3).Opacity, 3); // still in its delay (backwards: the 0% keyframe)
        Assert.Equal(1, SyftLoader.Traces[0].At(2.12).Opacity, 3); // round again
        // L2: bb 1.2s, the top of the hop at 30%: 14 up and a quarter turn; the third bit 0.24 s behind.
        var top = SyftLoader.Hops[0].At(0.36);
        Assert.Equal(-14, top.Y, 3);
        Assert.Equal(90, top.Turn, 3);
        Assert.Equal(Pose.Rest, SyftLoader.Hops[2].At(0.2));
        // L3: pg 3s ease-out, scale .4 → 1 by 6%.
        Assert.Equal(0.4, SyftLoader.Fills[0].At(0).Scale, 3);
        Assert.Equal(1, SyftLoader.Fills[0].At(0.18).Opacity, 3);
        // S1: p18 lands first (no delay), from 110 above, with its 1.5 bounce at 29%.
        Assert.Equal(-110, SyftSplash.SortCells[18].At(0).Y, 3);
        Assert.Equal(0, SyftSplash.SortCells[18].At(1.3).Y, 3);
        Assert.Equal(-1.5, SyftSplash.SortCells[18].At(1.45).Y, 3);
        Assert.Equal(1, SyftSplash.SortCells[18].At(30).Opacity, 3); // played once, it holds
        // S2: a cell overshoots to 1.18 at 7%, path order 0.05 s apart.
        Assert.Equal(1.18, SyftSplash.SieveCells[0].At(0.5 + 0.35).Scale, 3);
        Assert.Equal(0, SyftSplash.SieveCells[3].At(0.6).Opacity, 3);
        // S3: the coral bit's arc peaks 22 up at 40% and it's home by 50%.
        Assert.Equal(-22, SyftSplash.PopHop.At(1.8).Y, 3);
        Assert.Equal(0, SyftSplash.PopHop.At(2.25).Y, 3);
        // CSS's ease-out and a bezier solved for x.
        Assert.Equal(0.5, Ease.Linear.Apply(0.5), 6);
        Assert.InRange(Ease.Out.Apply(0.5), 0.68, 0.69);
    }

    // ---- What would break for a student ----

    static byte[] Frame(Window window)
    {
        var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("nothing rendered");
        using var fb = frame.Lock();
        var bytes = new byte[fb.RowBytes * fb.Size.Height];
        Marshal.Copy(fb.Address, bytes, 0, bytes.Length);
        return bytes;
    }

    /// <summary>The loaders are moved by the render thread: with the UI thread held up (a lecture's notes being built),
    /// the trail still runs on.</summary>
    [AvaloniaFact]
    public void A_loader_moves_on_while_the_UI_thread_is_busy()
    {
        string? still = Environment.GetEnvironmentVariable("STUDYSTASH_STILL");
        Environment.SetEnvironmentVariable("STUDYSTASH_STILL", null);
        Platform.Motion.Override = false;
        try
        {
            var loader = new SyftLoader { Kind = SyftLoaderKind.PathTrace, Size = 48 };
            var window = new Window { Width = 80, Height = 80, Content = loader };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.True(loader.Turning);
            var first = Frame(window);
            Thread.Sleep(250); // the UI thread busy, nothing posted to it
            var later = Frame(window);
            Assert.NotEqual(first, later);
            window.Close();
            Assert.False(loader.Turning);
        }
        finally
        {
            Platform.Motion.Override = null;
            Environment.SetEnvironmentVariable("STUDYSTASH_STILL", still);
        }
    }

    /// <summary>The library window's splash never stays: ready at once, it still plays its 1.2 s; never ready, it goes
    /// at 4 s anyway.</summary>
    [AvaloniaFact]
    public async Task The_splash_goes_once_ready_but_not_before_its_minimum_and_by_its_maximum()
    {
        foreach (bool ready in new[] { true, false })
        {
            var splash = new SyftSplash { IsReady = ready };
            var window = new Window { Width = 300, Height = 400, Content = splash };
            var clock = Stopwatch.StartNew();
            double? finished = null;
            splash.Finished += () => finished = clock.Elapsed.TotalSeconds;
            window.Show();
            while (finished is null && clock.Elapsed.TotalSeconds < 6)
                await Task.Delay(10, TestContext.Current.CancellationToken);
            window.Close();
            Assert.NotNull(finished);
            if (ready) Assert.InRange(finished!.Value, SyftSplash.MinSeconds, SyftSplash.MinSeconds + 0.8);
            else Assert.InRange(finished!.Value, SyftSplash.MaxSeconds, SyftSplash.MaxSeconds + 0.8);
        }
    }

    /// <summary>The mark and loaders say what they are: "Syft Notes", and "Loading", politely.</summary>
    [AvaloniaFact]
    public void The_mark_and_loaders_say_what_they_are()
    {
        static string Said(Control c) => Avalonia.Automation.Peers.ControlAutomationPeer.CreatePeerForElement(c).GetName() ?? "";
        Assert.Equal("Syft Notes", Said(new SyftMark()));
        Assert.Equal("Syft Notes", Said(new AppIcon()));
        var loader = new SyftLoader();
        Assert.Equal("Loading", Said(loader));
        Assert.Equal(Avalonia.Automation.AutomationLiveSetting.Polite, Avalonia.Automation.AutomationProperties.GetLiveSetting(loader));
    }

    // ---- Pictures of the pieces ----

    /// <summary>A sheet like Joey's boards: each control made by <paramref name="make"/> at each moment, in a row, on the
    /// boards' paper (or the dark canvas), saved as <c>name-light|dark.png</c>.</summary>
    static void Sheet(string name, ThemeVariant variant, IEnumerable<(string Label, Control Piece)> pieces, Size cell)
    {
        bool dark = variant == ThemeVariant.Dark;
        var row = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(24), MaxWidth = 1500 };
        foreach (var (label, piece) in pieces)
            row.Children.Add(new StackPanel
            {
                Margin = new Thickness(0, 0, 20, 20), Spacing = 6,
                Children =
                {
                    new Border { Width = cell.Width, Height = cell.Height, Child = piece, ClipToBounds = true },
                    new TextBlock { Text = label, FontSize = 12, Foreground = new SolidColorBrush(dark ? Color.Parse("#A4A9B1") : Color.Parse("#5C5B63")) },
                },
            });
        var root = new Border { Background = new SolidColorBrush(dark ? Color.Parse("#0E0F11") : Color.Parse("#F4F4F1")), Child = row };
        var window = new Window { Width = 1560, Height = 1200, SizeToContent = SizeToContent.WidthAndHeight, RequestedThemeVariant = variant, Content = root };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("nothing rendered");
        var size = root.Bounds.Size;
        var rtb = new RenderTargetBitmap(new PixelSize((int)size.Width, (int)size.Height));
        using (var ctx = rtb.CreateDrawingContext())
            ctx.DrawImage(frame, new Rect(size), new Rect(size));
        using (var file = File.Create(Path.Combine(Shot.Dir, $"{name}-{(dark ? "dark" : "light")}.png"))) rtb.Save(file, PngBitmapEncoderOptions.Default);
        window.Close();
    }

    static readonly ThemeVariant[] Themes = [ThemeVariant.Light, ThemeVariant.Dark];

    [AvaloniaFact]
    public void Piece_shots()
    {
        Platform.Motion.Override = false;
        try
        {
            foreach (var t in Themes)
            {
                Sheet("syft-01-marks", t,
                [
                    ("Auto 160", new SyftMark { Size = 160 }),
                    ("Light, tile", new SyftMark { Look = SyftLook.Light, Tile = SyftTile.Square, Size = 160 }),
                    ("Dark, tile (app icon)", new SyftMark { Look = SyftLook.Dark, Tile = SyftTile.Square, Size = 160 }),
                    ("Dark, avatar", new SyftMark { Look = SyftLook.Dark, Tile = SyftTile.Circle, Size = 160 }),
                    ("Light, avatar", new SyftMark { Look = SyftLook.Light, Tile = SyftTile.Circle, Size = 160 }),
                    ("Segments + light mesh", new SyftMark { Look = SyftLook.Segments, Mesh = true, Size = 160 }),
                    ("Odd One Out", new SyftMark { Look = SyftLook.OddOneOut, Size = 160 }),
                    ("Ink", new SyftMark { Look = SyftLook.Ink, Size = 160 }),
                    ("AppIcon 64", new AppIcon { Width = 64, Height = 64, Size = 64 }),
                    ("AppIcon 32", new AppIcon { Width = 32, Height = 32, Size = 32 }),
                    ("AppIcon 16", new AppIcon()),
                    ("Auto 34 · 26 · 18, no bits", new StackPanel
                    {
                        Orientation = Orientation.Horizontal, Spacing = 12, VerticalAlignment = VerticalAlignment.Center,
                        Children = { new SyftMark { Size = 34, Bits = false }, new SyftMark { Size = 26, Bits = false }, new SyftMark { Size = 18, Bits = false } },
                    }),
                ], new Size(180, 180));

                Sheet("syft-02-splash-sort-in", t, Frames([0, 0.3, 0.6, 0.9, 1.2, 1.6, 2.0, 2.4, 3.0, 60], s => new SyftSplash { Kind = SyftSplashKind.SortIn, At = s }), new Size(200, 400));
                Sheet("syft-03-splash-sieve-drop", t, Frames([0, 0.4, 0.8, 1.2, 1.6, 2.0, 2.6, 3.4, 60], s => new SyftSplash { Kind = SyftSplashKind.SieveDrop, At = s }), new Size(200, 400));
                Sheet("syft-04-splash-pop-back", t, Frames([0.3, 0.6, 1.0, 1.4, 1.8, 2.1, 2.3, 2.5, 3.0, 60], s => new SyftSplash { Kind = SyftSplashKind.PopBack, At = s }), new Size(200, 400));
                Sheet("syft-05-pop-back-inline", t, Frames([0.6, 1.8, 2.3, 60], s => new SyftSplash { Kind = SyftSplashKind.PopBack, Inline = true, Size = 150, At = s }), new Size(110, 160));
                Sheet("syft-06-loader-path-trace", t, Frames([0, 0.25, 0.5, 0.75, 1.0, 1.25, 1.5, 1.75], s => new SyftLoader { Kind = SyftLoaderKind.PathTrace, Size = 130, At = s }), new Size(130, 130));
                Sheet("syft-07-loader-hopping-bits", t, Frames([0, 0.15, 0.3, 0.45, 0.6, 0.75, 0.9, 1.05], s => new SyftLoader { Kind = SyftLoaderKind.HoppingBits, Size = 60, At = s }), new Size(150, 70));
                Sheet("syft-08-loader-pixel-progress", t,
                [
                    .. Frames([0.1, 0.5, 1.0, 1.5, 2.0, 2.4, 2.7, 2.9], s => new SyftLoader { Kind = SyftLoaderKind.PixelProgress, Size = 16, At = s }),
                    ("progress 0.35", new SyftLoader { Kind = SyftLoaderKind.PixelProgress, Size = 16, IsIndeterminate = false, Progress = 0.35 }),
                    ("progress 0.8", new SyftLoader { Kind = SyftLoaderKind.PixelProgress, Size = 16, IsIndeterminate = false, Progress = 0.8 }),
                ], new Size(200, 24));
                Sheet("syft-09-loaders-small", t,
                [
                    ("Hopping Bits 12", new SyftLoader { Kind = SyftLoaderKind.HoppingBits, Size = 12, At = 0.45 }),
                    ("14", new SyftLoader { Kind = SyftLoaderKind.HoppingBits, Size = 14, At = 0.45 }),
                    ("16", new SyftLoader { Kind = SyftLoaderKind.HoppingBits, Size = 16, At = 0.45 }),
                    ("Path Trace 30", new SyftLoader { Kind = SyftLoaderKind.PathTrace, Size = 30, At = 0.9 }),
                    ("38", new SyftLoader { Kind = SyftLoaderKind.PathTrace, Size = 38, At = 0.9 }),
                    ("paused", new SyftLoader { Kind = SyftLoaderKind.PathTrace, Size = 38, IsActive = false }),
                    ("Pixel Progress 8", new SyftLoader { Kind = SyftLoaderKind.PixelProgress, Size = 8, At = 1.3 }),
                    ("9", new SyftLoader { Kind = SyftLoaderKind.PixelProgress, Size = 9, At = 1.3 }),
                ], new Size(110, 44));
            }
        }
        finally
        {
            Platform.Motion.Override = null;
        }
    }

    static IEnumerable<(string, Control)> Frames(double[] moments, Func<double, Control> make) =>
        moments.Select(s => (s >= 60 ? "held" : $"{s:0.00} s", make(s)));

    // ---- Pictures of the screens it's on ----

    /// <summary>Each screen with the loaders at a moment of their motion, and the splash played out.</summary>
    [AvaloniaFact]
    public void Screen_shots()
    {
        string? still = Environment.GetEnvironmentVariable("STUDYSTASH_STILL");
        Environment.SetEnvironmentVariable("STUDYSTASH_STILL", "1"); // nothing moves on its own: a set moment each
        Platform.Motion.Override = false; // ...but moments of motion, not the still versions
        try
        {
            foreach (var t in Themes)
            {
                Shot.Take("mac-50-home-no-notes", SkinKind.Mac, t, () =>
                {
                    var m = Demo.Semester();
                    m.Overview!.Notes.Clear();
                    m.Overview.NotesEmpty = "No notes yet. Record something and its notes land here.";
                    return new MacLibrary { DataContext = m, Width = 1280, Height = 860 };
                }, size: Wide);
                Shot.Take("mac-51-notes-being-written", SkinKind.Mac, t, () =>
                {
                    var m = Demo.Semester(Demo.Page.Note);
                    m.Note = new NoteModel
                    {
                        Id = "n2", ClassName = m.Note!.ClassName, Dot = m.Note.Dot, Noun = Nouns.Lecture, When = "Wednesday 14 October at 9:30 AM, 50 min",
                        Title = "Attention, Continued", Writing = true,
                        Pending = "Your library is writing the notes for this lecture. They show here when they're done.",
                    }.Titled();
                    m.Notes = null;
                    return new MacLibrary { DataContext = m, Width = 1280, Height = 860 };
                }, size: Wide);
                Shot.Take("mac-52-library-unreachable", SkinKind.Mac, t, () =>
                {
                    var m = Demo.Semester();
                    m.Overview = null;
                    m.Empty = "Can't reach your library. Make sure the computer it's on is awake and on the same network.";
                    return new MacLibrary { DataContext = m, Width = 1280, Height = 860 };
                }, size: Wide);
                Shot.Take("mac-53-dropdown-recording", SkinKind.Mac, t, () => new MacPanel { DataContext = Demo.Panel(recording: true), VerticalAlignment = VerticalAlignment.Top }, size: new Size(520, 560));
                Shot.Take("mac-54-setup-welcome", SkinKind.Mac, t, () =>
                {
                    var m = SetupModel.For(SkinKind.Mac, AppRole.Laptop);
                    m.Go(SetupStep.Intro);
                    return new MacSetup { DataContext = m, DrawChrome = true, Width = 1100, Height = 760 };
                }, size: new Size(1228, 888));
                Shot.Take("mac-55-ai-answers", SkinKind.Mac, t, () => Shot.Side(
                    new MacAiAskChat { DataContext = AiDemo.Chat(), Width = 360, Height = 420 },
                    new MacAiAskChat { DataContext = Thinking(), Width = 360, Height = 420 }), size: new Size(960, 548));
                Shot.Take("mac-56-recording-note", SkinKind.Mac, t, () => new MacLibrary { DataContext = Demo.Semester(Demo.Page.Recording), Width = 1280, Height = 860 }, size: Wide);
                Shot.Take("mac-57-recording-pill", SkinKind.Mac, t, () => Shot.Side(
                    new MacRecorder { DataContext = Demo.Recorder() },
                    new MacRecorder { DataContext = Demo.Recorder(paused: true) }), size: new Size(300, 200));
                Shot.Take("mac-58-folder-no-notes", SkinKind.Mac, t, () =>
                {
                    var m = Demo.Semester(Demo.Page.FolderClass);
                    m.Overview!.Notes.Clear();
                    foreach (var g in m.Overview.Groups.ToList()) m.Overview.Groups.Remove(g);
                    m.Overview.NotesEmpty = "No lectures in this class yet.";
                    return new MacLibrary { DataContext = m, Width = 1280, Height = 860 };
                }, size: Wide);
                Shot.Take("mac-59-splash-over-library", SkinKind.Mac, t, () => new Panel
                {
                    Width = 1280, Height = 860,
                    Children = { new MacLibrary { DataContext = Demo.Semester() }, new SyftSplash { At = 1.6 } },
                }, size: Wide);
                Shot.Take("win-50-dropdown-recording", SkinKind.Win, t, () => new WinPanel { DataContext = Demo.Panel(recording: true), VerticalAlignment = VerticalAlignment.Top }, size: new Size(520, 640));
                Shot.Take("win-51-setup-welcome", SkinKind.Win, t, () =>
                {
                    var m = SetupModel.For(SkinKind.Win, AppRole.Laptop);
                    m.Go(SetupStep.Intro);
                    return new WinSetup { DataContext = m, DrawChrome = true, Width = 1100, Height = 760 };
                }, size: new Size(1228, 888));
                Shot.Take("win-52-setup-taskbar", SkinKind.Win, t, () => new WinSetup { DataContext = Demo.Setup(SkinKind.Win), DrawChrome = true, Width = 1100, Height = 760 }, size: new Size(1228, 888));
                Shot.Take("win-53-library-title-bar", SkinKind.Win, t, () => new WinLibrary { DataContext = Demo.Semester(), Width = 1280, Height = 860 }, size: Wide);
            }
        }
        finally
        {
            Platform.Motion.Override = null;
            Environment.SetEnvironmentVariable("STUDYSTASH_STILL", still);
        }
    }

    /// <summary>The recorder's chat with a question asked and the answer on its way.</summary>
    static AiAskModel Thinking()
    {
        var m = AiDemo.Chat();
        m.Turns.Add(new AiTurn("And which chapters?", "Ollama"));
        m.Busy = true;
        return m;
    }
}
