using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Path = Avalonia.Controls.Shapes.Path;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StudyStash.App.Controls;
using StudyStash.App.ViewModels;
using StudyStash.App.Views;

namespace StudyStash.App.Tests;

/// <summary>
/// Films and pictures of text being selected and copied, drawn off screen into STUDYSTASH_SHOTS: a pointer drawn over
/// the window drags across a lecture's transcript and a chat, and each frame is the window as the app draws it (the
/// way the Semester films were made). Only with STUDYSTASH_FILM=1: they'd add hundreds of pictures to every run.
/// </summary>
public class SelectFilms
{
    const int Fps = 12;

    static bool On => Environment.GetEnvironmentVariable("STUDYSTASH_FILM") == "1";

    sealed class Film
    {
        public readonly Window Window;
        readonly Canvas over = new() { IsHitTestVisible = false };
        readonly Path arrow = new()
        {
            Data = Geometry.Parse("M0,0 L0,17 L4.6,13 L7.6,20 L10.4,18.8 L7.4,12 L13,12 Z"), Fill = Brushes.Black, Stroke = Brushes.White, StrokeThickness = 1.2,
        };
        // Over text that selects the pointer is an I-beam, as the app's own is.
        readonly Path beamUnder = new() { Data = Geometry.Parse("M-4,-9 L4,-9 M0,-9 L0,9 M-4,9 L4,9"), Stroke = Brushes.White, StrokeThickness = 3.4, IsVisible = false };
        readonly Path beam = new() { Data = Geometry.Parse("M-4,-9 L4,-9 M0,-9 L0,9 M-4,9 L4,9"), Stroke = Brushes.Black, StrokeThickness = 1.4, IsVisible = false };
        readonly Ellipse ring = new() { Width = 26, Height = 26, Stroke = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0)), StrokeThickness = 3, IsVisible = false };
        readonly Border keys = new()
        {
            CornerRadius = new CornerRadius(10), Padding = new Thickness(14, 6), Background = new SolidColorBrush(Color.FromArgb(225, 24, 24, 27)), IsVisible = false,
            Child = new TextBlock { FontSize = 20, FontWeight = FontWeight.SemiBold, Foreground = Brushes.White },
        };
        readonly string name;
        int frames;
        Point at;

        public Film(string name, Control view, SkinKind skin, ThemeVariant variant, double width, double height, ColourTheme? theme = null, IBrush? ground = null)
        {
            this.name = name;
            Skin.UseTheme(theme ?? ColourThemes.Default);
            ((App)Application.Current!).UseSkin(skin);
            over.Children.Add(ring);
            over.Children.Add(keys);
            over.Children.Add(beamUnder);
            over.Children.Add(beam);
            over.Children.Add(arrow);
            at = new Point(width * 0.7, height * 0.6);
            Window = new Window
            {
                Width = width, Height = height, RequestedThemeVariant = variant, Background = ground,
                Content = new Panel { Children = { view, over } },
            };
            Look.Apply(Window);
            Place();
            Window.Show();
            DiagramsReady.Wait(Window);
            Settle();
        }

        public void Settle()
        {
            Dispatcher.UIThread.RunJobs();
            Window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
        }

        void Place()
        {
            Canvas.SetLeft(arrow, at.X);
            Canvas.SetTop(arrow, at.Y);
            Canvas.SetLeft(beam, at.X);
            Canvas.SetTop(beam, at.Y);
            Canvas.SetLeft(beamUnder, at.X);
            Canvas.SetTop(beamUnder, at.Y);
            Canvas.SetLeft(ring, at.X - 13);
            Canvas.SetTop(ring, at.Y - 13);
        }

        /// <summary>The I-beam over text, the arrow elsewhere.</summary>
        public bool Beam
        {
            set
            {
                beam.IsVisible = beamUnder.IsVisible = value;
                arrow.IsVisible = !value;
            }
        }

        /// <summary>No pointer drawn (a still).</summary>
        public void NoPointer() => arrow.IsVisible = beam.IsVisible = beamUnder.IsVisible = false;

        public void Frame()
        {
            Settle();
            var frame = Window.CaptureRenderedFrame() ?? throw new InvalidOperationException("nothing rendered");
            using var file = File.Create(System.IO.Path.Combine(Shot.Dir, $"{name}-{frames++:0000}.png"));
            frame.Save(file, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        }

        public void Hold(double seconds)
        {
            for (int i = 0; i < Math.Max(1, (int)(seconds * Fps)); i++) Frame();
        }

        /// <summary>The pointer glides to <paramref name="to"/> (in the window), the left button held down for a drag.</summary>
        public void Glide(Point to, int steps = 9, bool held = false)
        {
            var from = at;
            for (int i = 1; i <= steps; i++)
            {
                double t = i / (double)steps, eased = t * t * (3 - 2 * t);
                at = new Point(from.X + (to.X - from.X) * eased, from.Y + (to.Y - from.Y) * eased);
                Place();
                Window.MouseMove(at, held ? RawInputModifiers.LeftMouseButton : RawInputModifiers.None);
                Frame();
            }
        }

        public void Glide(Control c, double fx = 0.5, double fy = 0.5, int steps = 9)
        {
            Settle();
            Glide(c.TranslatePoint(new Point(c.Bounds.Width * fx, c.Bounds.Height * fy), Window)!.Value, steps);
        }

        public void Click()
        {
            ring.IsVisible = true;
            Frame();
            Frame();
            ring.IsVisible = false;
            Window.MouseDown(at, MouseButton.Left);
            Window.MouseUp(at, MouseButton.Left);
            Settle();
            Window.MouseMove(at);
            Frame();
        }

        /// <summary>Press here, drag to <paramref name="to"/> over <paramref name="steps"/> frames, let go.</summary>
        public void Drag(Point to, int steps = 20)
        {
            Window.MouseDown(at, MouseButton.Left);
            Frame();
            Glide(to, steps, held: true);
            Window.MouseUp(at, MouseButton.Left);
            Settle();
            Frame();
        }

        /// <summary>A key combination, pressed for real, with its keys shown under the pointer for a moment.</summary>
        public void Press(KeyGesture gesture, string shown)
        {
            ((TextBlock)keys.Child!).Text = shown;
            keys.IsVisible = true;
            Settle();
            Canvas.SetLeft(keys, Math.Clamp(at.X - keys.Bounds.Width / 2, 8, Window.Width - keys.Bounds.Width - 8));
            Canvas.SetTop(keys, Math.Min(at.Y + 34, Window.Height - keys.Bounds.Height - 8));
            Frame();
            Window.KeyPress(gesture.Key, (RawInputModifiers)gesture.KeyModifiers, PhysicalKey.None, null);
            Settle();
            Hold(0.9);
            keys.IsVisible = false;
            Frame();
        }
    }

    static NoteText Piece(Control root, string starting) =>
        root.GetVisualDescendants().OfType<NoteText>().First(t => t.IsEffectivelyVisible && Text(t).StartsWith(starting, StringComparison.Ordinal));

    static string Text(NoteText t) => t.Inlines is { Count: > 0 } inlines ? inlines.Text ?? "" : t.Text ?? "";

    static Point At(NoteText t, string word, Window window, bool after = false)
    {
        int index = Text(t).IndexOf(word, StringComparison.Ordinal) + (after ? word.Length : 0);
        var box = t.TextLayout.HitTestTextPosition(index);
        return t.TranslatePoint(new Point(box.X + t.Padding.Left, box.Y + box.Height / 2 + t.Padding.Top), window)!.Value;
    }

    static readonly (string Time, double Start, string Text)[] Heard =
    [
        ("10:12", 612, "Okay, so last week we stopped at recurrent networks, and the problem with them was that they read one word at a time."),
        ("10:48", 648, "So the query for this token is compared against the key of every other token in the sentence."),
        ("11:30", 690, "You take the dot product, and softmax turns those scores into weights that add up to one."),
        ("12:01", 721, "And those weights are applied to the value vectors, so each token ends up as a mix of the ones it attends to."),
        ("12:40", 760, "That's self-attention. Every token looks at every other token at once, which is why it trains so well on GPUs."),
        ("13:22", 802, "The catch is that attention alone has no idea about word order, so we add a position signal to each embedding."),
        ("14:05", 845, "For the exam, be able to write out the attention formula and say what each of the three matrices is for."),
    ];

    static LibraryModel Lecture()
    {
        var m = Demo.Semester(Demo.Page.Note);
        m.DrawChrome = false;
        foreach (var (time, start, text) in Heard) m.Note!.Transcript.Add(new HeardLine { Time = time, Start = start, Text = text });
        return m;
    }

    [AvaloniaFact]
    public void Select_a_transcript_and_paste_it_into_Ask()
    {
        if (!On) return;
        var view = new MacLibrary { DataContext = Lecture() };
        var f = new Film("select-transcript", view, SkinKind.Mac, ThemeVariant.Light, 1280, 860);
        f.Hold(0.8);
        f.Glide(view.Part<Button>("TranscriptTab"));
        f.Click();
        f.Hold(1.0);
        var page = view.GetVisualDescendants().OfType<NotePage>().Single();
        var from = Piece(page, "So the query");
        var to = Piece(page, "That's self-attention");
        f.Glide(At(from, "the query", f.Window), 12);
        f.Beam = true;
        f.Hold(0.3);
        f.Drag(At(to, "at once,", f.Window, after: true), 26);
        f.Hold(1.2);
        var keys = Application.Current!.PlatformSettings!.HotkeyConfiguration;
        f.Press(keys.Copy[0], "⌘C");
        f.Hold(0.4);
        f.Beam = false;
        var ask = view.GetVisualDescendants().OfType<TextBox>().First(t => t.Name == "AskInput" && t.IsEffectivelyVisible);
        f.Glide(ask, 0.3, 0.5, 12);
        f.Click();
        f.Hold(0.3);
        f.Press(keys.Paste[0], "⌘V");
        f.Hold(2.4);
        f.Window.Close();
    }

    [AvaloniaFact]
    public void Select_a_chat_question_and_its_answer()
    {
        if (!On) return;
        var model = Demo.Recorder(expanded: true);
        model.Ask!.Turns[0].Answer = "Recursion traces and call-stack diagrams, like the ones from last week. Big-O proofs won't be on it, she said, so spend the time on tracing by hand.";
        var view = new WinRecorder { DataContext = model, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var f = new Film("select-chat", view, SkinKind.Win, ThemeVariant.Dark, 520, 640, ground: new SolidColorBrush(Color.Parse("#0E0F11")));
        f.Hold(0.8);
        var question = Piece(view, "What did she say");
        var answer = Piece(view, "Recursion traces and");
        f.Glide(At(question, "What", f.Window), 12);
        f.Beam = true;
        f.Hold(0.3);
        f.Drag(At(answer, "last week.", f.Window, after: true), 24);
        f.Hold(1.2);
        var keys = Application.Current!.PlatformSettings!.HotkeyConfiguration;
        f.Press(keys.Copy[0], "Ctrl+C");
        f.Hold(0.4);
        f.Beam = false;
        var field = view.GetVisualDescendants().OfType<TextBox>().First(t => t.IsEffectivelyVisible);
        f.Glide(field, 0.3, 0.5, 10);
        f.Click();
        f.Press(keys.Paste[0], "Ctrl+V");
        f.Hold(2.0);
        f.Window.Close();
    }

    /// <summary>Stills of a selection in each look, light and dark, in colour themes whose accent is pale (a link in
    /// it nearly vanishes on the highlighter) and dark: is what's selected still readable?</summary>
    [AvaloniaFact]
    public void Selections_read_in_every_look_and_theme()
    {
        if (!On) return;
        var named = ColourThemes.All.ToDictionary(t => t.Name);
        foreach (var (skin, variant, theme) in new[]
                 {
                     (SkinKind.Mac, ThemeVariant.Light, "Lagoon"), (SkinKind.Mac, ThemeVariant.Dark, "Lagoon"),
                     (SkinKind.Mac, ThemeVariant.Light, "Mint"), (SkinKind.Win, ThemeVariant.Light, "Highlighter"),
                     (SkinKind.Win, ThemeVariant.Dark, "Midnight"), (SkinKind.Win, ThemeVariant.Dark, "Lagoon"),
                 })
        {
            string tag = $"{skin.ToString().ToLowerInvariant()}-{variant.Key.ToString()!.ToLowerInvariant()}-{theme.ToLowerInvariant()}";

            // The recorder: its transcript, then its chat, a link in the answer.
            var model = Demo.Recorder(expanded: true);
            model.Ask!.Turns[0].Answer = "Recursion traces and call-stack diagrams; see [the review sheet](https://example.edu/review). Big-O proofs won't be on it.";
            Control recorder = skin == SkinKind.Mac ? new MacRecorder { DataContext = model } : new WinRecorder { DataContext = model };
            recorder.HorizontalAlignment = HorizontalAlignment.Center;
            recorder.VerticalAlignment = VerticalAlignment.Center;
            var ground = new SolidColorBrush(variant == ThemeVariant.Dark ? Color.Parse("#0E0F11") : Color.Parse("#DADCE0"));
            var f = new Film($"still-chat-{tag}", recorder, skin, variant, 520, 640, named[theme], ground);
            f.NoPointer();
            var question = Piece(recorder, "What did she say");
            var answer = Piece(recorder, "Recursion traces and");
            f.Window.MouseMove(At(question, "she", f.Window));
            f.Window.MouseDown(At(question, "she", f.Window), MouseButton.Left);
            f.Window.MouseMove(At(answer, "be on it", f.Window), RawInputModifiers.LeftMouseButton);
            f.Window.MouseUp(At(answer, "be on it", f.Window), MouseButton.Left);
            f.Frame();
            var line = Piece(recorder, "Think of each");
            f.Window.MouseMove(At(line, "call", f.Window));
            f.Window.MouseDown(At(line, "call", f.Window), MouseButton.Left);
            f.Window.MouseMove(At(Piece(recorder, "And when we hit"), "base case", f.Window), RawInputModifiers.LeftMouseButton);
            f.Window.MouseUp(At(Piece(recorder, "And when we hit"), "base case", f.Window), MouseButton.Left);
            f.Frame();
            f.Window.Close();

            // A lecture's notes (the grey of what was added from the recording, the ink of what was typed) and its
            // transcript, in the library window.
            var lecture = Lecture();
            Control library = skin == SkinKind.Mac ? new MacLibrary { DataContext = lecture } : new WinLibrary { DataContext = lecture };
            var w = new Film($"still-notes-{tag}", library, skin, variant, 1280, 860, named[theme]);
            w.NoPointer();
            var note = library.GetVisualDescendants().OfType<NoteView>().First(n => n.IsEffectivelyVisible);
            var pieces = note.GetVisualDescendants().OfType<NoteText>().Where(t => t.IsEffectivelyVisible && Text(t).Length > 20).ToList();
            var start = pieces[1];
            var end = pieces[Math.Min(6, pieces.Count - 1)];
            var down = start.TranslatePoint(new Point(start.Bounds.Width * 0.3, start.Bounds.Height / 2), w.Window)!.Value;
            var up = end.TranslatePoint(new Point(end.Bounds.Width * 0.5, end.Bounds.Height / 2), w.Window)!.Value;
            w.Window.MouseMove(down);
            w.Window.MouseDown(down, MouseButton.Left);
            w.Window.MouseMove(up, RawInputModifiers.LeftMouseButton);
            w.Window.MouseUp(up, MouseButton.Left);
            w.Frame();
            lecture.Note!.ShowTranscript = true;
            w.Settle();
            var page = library.GetVisualDescendants().OfType<NotePage>().Single();
            var first = Piece(page, "So the query");
            var last = Piece(page, "The catch is");
            w.Window.MouseMove(At(first, "token", w.Window));
            w.Window.MouseDown(At(first, "token", w.Window), MouseButton.Left);
            w.Window.MouseMove(At(last, "no idea", w.Window), RawInputModifiers.LeftMouseButton);
            w.Window.MouseUp(At(last, "no idea", w.Window), MouseButton.Left);
            w.Frame();
            w.Window.Close();
        }
    }
}
