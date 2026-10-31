using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using StudyStash.App.Controls;
using StudyStash.App.Platform;
using StudyStash.App.Services;
using StudyStash.App.ViewModels;
using StudyStash.App.Views;

namespace StudyStash.App.Tests;

/// <summary>
/// Joey's fit pass (2026-10-30): the recording dot is one of the pixel s's cells and every live level meter is columns of
/// them. With STUDYSTASH_SHOTS set, pictures of each place the desktop app shows them, light and dark: the Mac as a
/// Retina screen draws it (2×), Windows at 100 % (1×), where a cell is fewest pixels.
/// </summary>
public class UiFitShots
{
    static readonly Size Wide = new(1408, 988);
    static readonly ThemeVariant[] Themes = [ThemeVariant.Light, ThemeVariant.Dark];

    [AvaloniaFact]
    public void Ui_fit_shots()
    {
        if (Environment.GetEnvironmentVariable("STUDYSTASH_SHOTS") is not { Length: > 0 }) return;
        string? still = Environment.GetEnvironmentVariable("STUDYSTASH_STILL");
        Environment.SetEnvironmentVariable("STUDYSTASH_STILL", "1"); // nothing moves on its own: each loader at its set moment
        Motion.Override = false;
        try
        {
            foreach (var t in Themes)
            {
                // The note page's recording bar (and the sidebar's recording row, the pinned dot's page)
                Shot.Take("fit-mac-01-recording-note", SkinKind.Mac, t, () => new MacLibrary { DataContext = Demo.Semester(Demo.Page.Recording), Width = 1280, Height = 860 }, size: Wide, scale: 2);
                Shot.Take("fit-win-01-recording-note", SkinKind.Win, t, () => new WinLibrary { DataContext = Demo.Semester(Demo.Page.Recording), Width = 1280, Height = 860 }, size: Wide);
                // Home: Record's red cell, and recording, Stop in it and the meter beside it
                Shot.Take("fit-mac-02-home", SkinKind.Mac, t, () => new MacLibrary { DataContext = Demo.Semester(), Width = 1280, Height = 860 }, size: Wide, scale: 2);
                Shot.Take("fit-mac-03-home-recording", SkinKind.Mac, t, () => new MacLibrary { DataContext = Demo.Semester(recording: true), Width = 1280, Height = 860 }, size: Wide, scale: 2);
                Shot.Take("fit-win-03-home-recording", SkinKind.Win, t, () => new WinLibrary { DataContext = Demo.Semester(recording: true), Width = 1280, Height = 860 }, size: Wide);
                // The floating recorder: recording, paused, the pointer on it (Stop), and open
                Shot.Take("fit-mac-04-recording-pill", SkinKind.Mac, t, () => Shot.Side(
                    new MacRecorder { DataContext = Demo.Recorder() },
                    new MacRecorder { DataContext = Demo.Recorder(paused: true) },
                    new MacRecorder { DataContext = Hovered(Demo.Recorder()) },
                    new MacRecorder { DataContext = Demo.Recorder(expanded: true) }), size: new Size(820, 660), scale: 2);
                Shot.Take("fit-win-04-recording-pill", SkinKind.Win, t, () => Shot.Side(
                    new StackPanel { Spacing = 40, Children = { new WinRecorder { DataContext = Demo.Recorder() }, new WinRecorder { DataContext = Demo.Recorder(paused: true) } } },
                    new WinRecorder { DataContext = Demo.Recorder(expanded: true) }), size: new Size(900, 660));
                // The dropdown: Record, and recording
                Shot.Take("fit-mac-05-dropdown", SkinKind.Mac, t, () => Shot.Side(
                    new MacPanel { DataContext = Demo.Panel(recording: false), VerticalAlignment = VerticalAlignment.Top },
                    new MacPanel { DataContext = Demo.Panel(recording: true), VerticalAlignment = VerticalAlignment.Top }), size: new Size(860, 560), scale: 2);
                Shot.Take("fit-win-05-dropdown", SkinKind.Win, t, () => Shot.Side(
                    new WinPanel { DataContext = Demo.Panel(recording: false), VerticalAlignment = VerticalAlignment.Top },
                    new WinPanel { DataContext = Demo.Panel(recording: true), VerticalAlignment = VerticalAlignment.Top }), size: new Size(960, 640));
                // Setup's microphone check
                Shot.Take("fit-mac-06-setup-microphone", SkinKind.Mac, t, () => new MacSetup { DataContext = Microphone(SkinKind.Mac), DrawChrome = true, Width = 1100, Height = 760 }, size: new Size(1228, 888), scale: 2);
                Shot.Take("fit-win-06-setup-microphone", SkinKind.Win, t, () => new WinSetup { DataContext = Microphone(SkinKind.Win), DrawChrome = true, Width = 1100, Height = 760 }, size: new Size(1228, 888));
                // An answer on its way: the page's card over the ask bar, the recorder's chat, the quick panel
                Shot.Take("fit-mac-07-ai-working", SkinKind.Mac, t, () => AiWorking(SkinKind.Mac), size: new Size(1560, 560), scale: 2);
                Shot.Take("fit-win-07-ai-working", SkinKind.Win, t, () => AiWorking(SkinKind.Win), size: new Size(1640, 600));
                // The pieces: meters at each place's size, the cells, the menu bar's and tray's icon
                Shot.Take("fit-mac-08-pieces", SkinKind.Mac, t, () => Pieces(t), size: new Size(780, 760), scale: 2);
                Shot.Take("fit-win-08-pieces", SkinKind.Win, t, () => Pieces(t), size: new Size(780, 760));
            }
        }
        finally
        {
            Motion.Override = null;
            Environment.SetEnvironmentVariable("STUDYSTASH_STILL", still);
        }
    }

    static RecorderModel Hovered(RecorderModel m)
    {
        m.Hovered = true;
        return m;
    }

    /// <summary>The design's levels for setup's 24 columns: someone saying a few words.</summary>
    static readonly double[] Speaking = [0.05, 0.08, 0.3, 0.55, 0.72, 0.6, 0.45, 0.66, 0.82, 0.7, 0.4, 0.3, 0.52, 0.64, 0.9, 0.75, 0.5, 0.35, 0.58, 0.68, 0.44, 0.3, 0.2, 0.1];

    static SetupModel Microphone(SkinKind skin)
    {
        var m = SetupModel.For(skin, AppRole.Both);
        m.Go(SetupStep.Microphone);
        m.MicAllowed = true;
        m.MicLevels = Speaking;
        return m;
    }

    static AiAskModel Thinking(AiAskModel m, string question)
    {
        m.Engine = "claude";
        m.Turns.Add(new AiTurn(question, "Claude"));
        m.Busy = true;
        return m;
    }

    static Control AiWorking(SkinKind skin)
    {
        bool mac = skin == SkinKind.Mac;
        var card = Thinking(AiDemo.Ask(), "What's a base case?");
        var page = new StackPanel
        {
            Width = 620, Spacing = 16,
            Children =
            {
                mac ? new MacAiAnswer { DataContext = card } : new WinAiAnswer { DataContext = card },
                mac ? new MacAiAskBar { DataContext = card } : new WinAiAskBar { DataContext = card },
            },
        };
        var chat = Thinking(AiDemo.Chat(), "And which chapters?");
        Control chatView = mac ? new MacAiAskChat { DataContext = chat, Width = 360, Height = 420 } : new WinAiAskChat { DataContext = chat, Width = 360, Height = 420 };
        var quick = Demo.Quick(answer: true);
        quick.Answer = "";
        quick.Thinking = true;
        Control quickView = mac ? new MacQuick { DataContext = quick, Width = 400 } : new WinQuick { DataContext = quick, Width = 400 };
        return Shot.Side(page, chatView, quickView);
    }

    static Waveform Meter(int bars, double height, bool lively, IReadOnlyList<double> levels)
    {
        var w = new Waveform { Bars = bars, Height = height, Lively = lively, Levels = levels, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        w.Bind(Waveform.ForegroundProperty, w.GetResourceObservable("Record"));
        return w;
    }

    static SyftCell Dot(double side, bool ring = false, string colour = "Record")
    {
        var c = new SyftCell { Width = side, Height = side, StrokeThickness = ring ? 2 : 0, VerticalAlignment = VerticalAlignment.Center };
        c.Bind(ring ? Avalonia.Controls.Shapes.Shape.StrokeProperty : Avalonia.Controls.Shapes.Shape.FillProperty, c.GetResourceObservable(colour));
        return c;
    }

    static Control Row(string label, params Control[] pieces)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16, MinHeight = 40 };
        var words = new TextBlock { Text = label, Width = 240, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        words.Bind(TextBlock.ForegroundProperty, words.GetResourceObservable("Fg2"));
        row.Children.Add(words);
        foreach (var p in pieces) row.Children.Add(p);
        return row;
    }

    /// <summary>An icon's picture, each of its pixels a square <paramref name="zoom"/> across (no smoothing), to see
    /// the cells land on whole pixels.</summary>
    sealed class Pixels(byte[] png, double zoom) : Control
    {
        readonly Bitmap bitmap = new(new MemoryStream(png));

        protected override Size MeasureOverride(Size availableSize) => new(bitmap.PixelSize.Width * zoom, bitmap.PixelSize.Height * zoom);

        public override void Render(DrawingContext context)
        {
            int w = bitmap.PixelSize.Width, h = bitmap.PixelSize.Height;
            var bytes = new byte[w * h * 4];
            nint buffer = System.Runtime.InteropServices.Marshal.AllocHGlobal(bytes.Length);
            try
            {
                bitmap.CopyPixels(new PixelRect(0, 0, w, h), buffer, bytes.Length, w * 4);
                System.Runtime.InteropServices.Marshal.Copy(buffer, bytes, 0, bytes.Length);
            }
            finally
            {
                System.Runtime.InteropServices.Marshal.FreeHGlobal(buffer);
            }
            bool rgba = bitmap.Format == Avalonia.Platform.PixelFormats.Rgba8888;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = (y * w + x) * 4;
                    if (bytes[i + 3] == 0) continue;
                    // Premultiplied: back to the colour, at its own alpha.
                    byte a = bytes[i + 3];
                    byte r = (byte)Math.Min(255, (rgba ? bytes[i] : bytes[i + 2]) * 255 / a), g = (byte)Math.Min(255, bytes[i + 1] * 255 / a),
                        b = (byte)Math.Min(255, (rgba ? bytes[i + 2] : bytes[i]) * 255 / a);
                    context.FillRectangle(new SolidColorBrush(Color.FromArgb(a, r, g, b)), new Rect(x * zoom, y * zoom, zoom, zoom));
                }
        }
    }

    static Image Tray(byte[] png, int size, double zoom) =>
        new() { Source = new Bitmap(new MemoryStream(png)), Width = size * zoom, Height = size * zoom, VerticalAlignment = VerticalAlignment.Center };

    /// <summary>A strip the colour of a menu bar (or Windows' taskbar), the icon on it at its size and enlarged.</summary>
    static Control Bar(bool dark, int size, bool recording)
    {
        var ink = dark ? Brushes.White : Brushes.Black;
        var png = TrayMark.Png(size, ink, recording);
        return new Border
        {
            Background = new SolidColorBrush(Color.Parse(dark ? "#2B2930" : "#F2F1EE")), CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 4),
            Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { Tray(png, size, 0.5), new Pixels(png, 3) { VerticalAlignment = VerticalAlignment.Center } } },
        };
    }

    static Control Pieces(ThemeVariant variant)
    {
        bool dark = variant == ThemeVariant.Dark;
        var sheet = new StackPanel { Spacing = 4 };
        sheet.Children.Add(Row("Mac pill · 3 columns, 11 tall, lively", Meter(3, 11, true, Demo.PillWave)));
        sheet.Children.Add(Row("Windows pill, Home's Record · 3, 16", Meter(3, 16, true, Demo.PillWave)));
        sheet.Children.Add(Row("Note page's recording bar · 5, 16", Meter(5, 16, true, Demo.PillWave)));
        sheet.Children.Add(Row("Dropdown · 20, 20", Meter(20, 20, false, Demo.Wave)));
        sheet.Children.Add(Row("Setup's microphone check · 24, 28", Meter(24, 28, false, Speaking)));
        sheet.Children.Add(Row("Silence · lively, and plain", Meter(5, 16, true, [0.1, 0.1, 0.1, 0.1, 0.1]), Meter(5, 16, false, [0.1, 0.1, 0.1, 0.1, 0.1])));
        sheet.Children.Add(Row("Recording dots · 6, 8, 9, 10, 12, outline", Dot(6), Dot(8), Dot(9), Dot(10), Dot(12), Dot(12, ring: true)));
        sheet.Children.Add(Row("Menu bar (Mac) · idle, recording", Bar(dark, 36, false), Bar(dark, 36, true)));
        sheet.Children.Add(Row("Tray (Windows) · idle, recording", Bar(dark, 32, false), Bar(dark, 32, true)));
        var card = new Border { Padding = new Thickness(20), CornerRadius = new CornerRadius(12), Child = sheet };
        card.Bind(Border.BackgroundProperty, card.GetResourceObservable("Surface"));
        return card;
    }
}
