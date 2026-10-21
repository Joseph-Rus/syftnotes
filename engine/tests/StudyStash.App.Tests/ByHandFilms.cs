using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StudyStash.App.Services;
using StudyStash.App.ViewModels;
using StudyStash.App.Views;
using StudyStash.Core.Ai;

namespace StudyStash.App.Tests;

/// <summary>
/// Frames for short films of setup with each AI that goes by hand (Gemini, no paid plan) and of an API key on the notes
/// step, drawn off screen as the app draws them, a pointer over them that moves and really clicks. Only with
/// STUDYSTASH_FILM=1 (each film is a few hundred pictures): into STUDYSTASH_SHOTS, as setup-&lt;name&gt;-0000.png on.
/// Every AI behind them is a stand-in: nothing is installed, signed in to or downloaded.
/// </summary>
public sealed class ByHandFilms
{
    const int Fps = 12;
    static bool On => Environment.GetEnvironmentVariable("STUDYSTASH_FILM") == "1";

    sealed class Film
    {
        public readonly Window Window;
        readonly Panel stage = new() { Margin = new Thickness(36), HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top };
        readonly Canvas over = new() { IsHitTestVisible = false };
        readonly Avalonia.Controls.Shapes.Path pointer = new()
        {
            Data = Geometry.Parse("M0,0 L0,17 L4.6,13 L7.6,20 L10.4,18.8 L7.4,12 L13,12 Z"), Fill = Brushes.Black, Stroke = Brushes.White, StrokeThickness = 1.2,
        };
        readonly Ellipse ring = new() { Width = 26, Height = 26, Stroke = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0)), StrokeThickness = 3, IsVisible = false };
        readonly string name;
        int frames;
        Point at = new(520, 560);

        public Film(string name)
        {
            this.name = name;
            Skin.UseTheme(ColourThemes.Default);
            ((App)Application.Current!).UseSkin(SkinKind.Mac);
            over.Children.Add(ring);
            over.Children.Add(pointer);
            // The design's ground behind the window, as setup's pictures have it.
            Window = new Window
            {
                Width = 980, Height = 980, RequestedThemeVariant = ThemeVariant.Light,
                Content = new Panel { Background = new SolidColorBrush(Color.Parse("#C9D6EA")), Children = { stage, over } },
            };
            Look.Apply(Window);
            Place();
        }

        public void Show(Control view)
        {
            stage.Children.Clear();
            stage.Children.Add(view);
            if (!Window.IsVisible) Window.Show();
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
            Canvas.SetLeft(pointer, at.X);
            Canvas.SetTop(pointer, at.Y);
            Canvas.SetLeft(ring, at.X - 13);
            Canvas.SetTop(ring, at.Y - 13);
        }

        public void Frame()
        {
            Settle();
            var frame = Window.CaptureRenderedFrame() ?? throw new InvalidOperationException("nothing rendered");
            using var file = File.Create(System.IO.Path.Combine(Shot.Dir, $"setup-{name}-{frames++:0000}.png"));
            frame.Save(file, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        }

        public void Hold(double seconds)
        {
            for (int i = 0; i < Math.Max(1, (int)(seconds * Fps)); i++) Frame();
        }

        public void MoveTo(Control c, double fx = 0.5, double fy = 0.5, int steps = 10)
        {
            Settle();
            var to = c.TranslatePoint(new Point(c.Bounds.Width * fx, c.Bounds.Height * fy), Window)!.Value;
            var from = at;
            for (int i = 1; i <= steps; i++)
            {
                double t = i / (double)steps, eased = t * t * (3 - 2 * t);
                at = new Point(from.X + (to.X - from.X) * eased, from.Y + (to.Y - from.Y) * eased);
                Place();
                Window.MouseMove(at);
                Frame();
            }
        }

        /// <summary>A real click where the pointer is, a ring showing it.</summary>
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

        /// <summary>Waits (a frame at a time) for something the click started.</summary>
        public async Task Until(Func<bool> done, double most = 10)
        {
            for (int i = 0; i < most * Fps && !done(); i++)
            {
                await Task.Delay(1);
                Frame();
            }
        }

        public T Named<T>(string name) where T : Control =>
            Window.GetVisualDescendants().OfType<T>().First(c => c.Name == name && c.IsEffectivelyVisible);

        /// <summary>The button showing <paramref name="words"/>, in <paramref name="inside"/> when given.</summary>
        public Button Button(string words, Control? inside = null) =>
            (inside ?? Window).GetVisualDescendants().OfType<Button>().First(b => b.IsEffectivelyVisible
                && (b.Content as string == words || b.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == words)));

        /// <summary>An engine row on the notes step, by the AI's name.</summary>
        public Control Row(string ai) => Window.GetVisualDescendants().OfType<Border>()
            .First(b => b.Classes.Contains("engine") && b.IsEffectivelyVisible && b.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == ai));

        public void Type(TextBox box, string text)
        {
            foreach (char c in text)
            {
                box.Text += c;
                Frame();
            }
        }
    }

    static GuidedSetupModel FirstScreen() => GuidedSetupShots.Guided(SkinKind.Mac);

    /// <summary>Setup by hand on its notes step (the library made), over <paramref name="lib"/>.</summary>
    static async Task<(SetupModel Setup, AiSetupModel Ai)> NotesStep(FakeAiLibrary lib, string prefer, Func<Task>? wait = null)
    {
        var m = SetupModel.For(SkinKind.Mac, AppRole.Both);
        m.LibraryOk = true;
        var ai = new AiSetupModel(lib)
        {
            Windows = false, Prefer = prefer, TakesKeys = true, SetUpPaid = _ => Task.CompletedTask, WriteNotes = _ => Task.FromResult(true),
            Lede = "They run on this Mac, as part of your library. You can change this later in Settings → Your AI.",
            Wait = wait ?? (() => Task.CompletedTask),
        };
        m.Ai = ai;
        m.Go(SetupStep.Ai);
        await ai.Load();
        return (m, ai);
    }

    static AiOverview Library(params (string Id, string State)[] states)
    {
        var o = AiTestData.MixedOverview() with { Ask = "ollama" };
        return o with
        {
            Engines = [.. o.Engines.Select(e => states.FirstOrDefault(s => s.Id == e.Id) is { Id: not null } s
                ? e with { State = s.State, Installed = s.State != "not_installed" } : e)],
        };
    }

    /// <summary>Gemini: picked on the first screen, Continue, and setup by hand's notes step opens on its key; a key
    /// pasted and saved, and Gemini writes the notes.</summary>
    [AvaloniaFact]
    public async Task Gemini()
    {
        if (!On) return;
        var film = new Film("gemini");
        var g = FirstScreen();
        film.Show(new MacGuidedSetup { DataContext = g, DrawChrome = true });
        film.Hold(1.2);
        film.MoveTo(film.Named<Button>("PickGemini"), 0.3);
        film.Click();
        film.Hold(0.8);
        film.MoveTo(film.Named<Button>("Continue"));
        film.Click();
        if (g.Screen != GuidedScreen.Manual) throw new InvalidOperationException($"Continue on Gemini went to {g.Screen}");

        var lib = new FakeAiLibrary { Overview = Library(("gemini", "not_installed"), ("codex", "not_installed")) };
        var (m, ai) = await NotesStep(lib, g.NotesPick);
        film.Show(new MacSetup { DataContext = m, DrawChrome = true });
        film.Hold(2);
        film.MoveTo(film.Button("Open Google AI Studio"), 0.4);
        film.Click();
        film.Hold(0.5);
        var box = film.Named<TextBox>("KeyBox");
        film.MoveTo(box, 0.3);
        film.Click();
        film.Type(box, "AIzaSyD-3xample-k3y-f0r-the-film");
        film.Hold(0.4);
        film.MoveTo(film.Button("Save"));
        film.Click();
        await film.Until(() => ai.SelectedNotes == "gemini");
        film.Hold(2.5);
        film.MoveTo(film.Button("Continue"));
        film.Hold(0.6);
    }

    /// <summary>No paid plan: the first screen's last answer, Continue, and the notes step says where the free AI's
    /// button is; pressed, it's got ready (here a stand-in, in a few seconds) and writes the notes.</summary>
    [AvaloniaFact]
    public async Task No_paid_plan()
    {
        if (!On) return;
        var film = new Film("free");
        var g = FirstScreen();
        film.Show(new MacGuidedSetup { DataContext = g, DrawChrome = true });
        film.Hold(1.2);
        film.MoveTo(film.Named<Button>("PickFree"), 0.3);
        film.Click();
        film.Hold(0.8);
        film.MoveTo(film.Named<Button>("Continue"));
        film.Click();

        // A student with no paid plan: no Claude or ChatGPT on this computer either.
        AiOverview At(string state, PullInfo? pulling) => Library(("ollama", state), ("claude", "not_installed"), ("codex", "not_installed")) with { Pulling = pulling };
        var answers = new Queue<AiOverview>(
        [
            At("not_installed", new PullInfo("qwen3:8b", 0.35, "") { Step = "app" }),
            At("not_installed", new PullInfo("qwen3:8b", 0.8, "") { Step = "app" }),
            At("not_running", new PullInfo("qwen3:8b", 0, "") { Step = "start" }),
            At("model_missing", new PullInfo("qwen3:8b", 0.3, "")),
            At("model_missing", new PullInfo("qwen3:8b", 0.75, "")),
            At("ready", null),
        ]);
        var lib = new FakeAiLibrary { Overview = At("not_installed", null) };
        lib.OnSetUp = _ => new AiSaid("Getting the free AI ready…", At("not_installed", new PullInfo("qwen3:8b", 0.05, "") { Step = "app" }));
        Film? filming = null;
        var (m, ai) = await NotesStep(lib, g.NotesPick, () =>
        {
            filming?.Hold(0.6);
            return Task.CompletedTask;
        });
        // From the press on, each look at the library is a little further along.
        lib.OnEngines = () => answers.Count > 1 ? lib.Overview = answers.Dequeue() : lib.Overview = answers.Peek();
        film.Show(new MacSetup { DataContext = m, DrawChrome = true });
        film.Hold(2.2);
        filming = film;
        film.MoveTo(film.Button("Set it up", film.Row("Free AI on this Mac")));
        film.Click();
        await film.Until(() => ai.Say?.EndsWith("It writes your notes.", StringComparison.Ordinal) == true, 20);
        film.Hold(2.5);
    }

    /// <summary>An API key instead of a plan: Claude isn't on this computer, "Use an API key" on its row, a key pasted
    /// and saved, and Claude writes the notes.</summary>
    [AvaloniaFact]
    public async Task Api_key()
    {
        if (!On) return;
        var film = new Film("api-key");
        var lib = new FakeAiLibrary { Overview = Library(("claude", "not_installed"), ("codex", "not_installed")) };
        var (m, ai) = await NotesStep(lib, "");
        film.Show(new MacSetup { DataContext = m, DrawChrome = true });
        film.Hold(1.5);
        film.MoveTo(film.Button("Use an API key", film.Row("Claude")));
        film.Click();
        film.Hold(1.2);
        var box = film.Named<TextBox>("KeyBox");
        film.MoveTo(box, 0.3);
        film.Click();
        film.Type(box, "sk-ant-api03-f1lm-example");
        film.MoveTo(film.Button("Save"));
        film.Click();
        await film.Until(() => ai.SelectedNotes == "claude");
        film.Hold(2.5);
    }
}
