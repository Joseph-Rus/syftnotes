using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using StudyStash.App.ViewModels;
using StudyStash.App.Views;
using StudyStash.Core;
using StudyStash.Core.Ai;
using StudyStash.Library;
using Path = Avalonia.Controls.Shapes.Path;

namespace StudyStash.App.Tests;

/// <summary>
/// A film of Ask remembering a conversation, drawn off screen into STUDYSTASH_SHOTS (with frames.tsv marking each frame
/// act, hold, stream or wait, for the GIF maker to squeeze the waits): a lecture's page in the library window, a
/// question, a follow-up that only makes sense with the one before, and New chat. Only with STUDYSTASH_FILM=1.
/// <para>With STUDYSTASH_FILM_HOME (a throwaway library: home/state.db and its notes folder) the questions go to a real
/// library run here, on STUDYSTASH_FILM_PORT (never 8787), and the real AI it picks (STUDYSTASH_FILM_ENGINE): every
/// answer is the AI's own. Without it a stand-in answers, to check the film itself without spending any of the plan.</para>
/// </summary>
public class AskRemembersFilm
{
    const int Fps = 12;

    static bool On => Environment.GetEnvironmentVariable("STUDYSTASH_FILM") == "1";

    static string Env(string key, string fallback = "") => Environment.GetEnvironmentVariable(key) is { Length: > 0 } v ? v : fallback;

    static void Log(string line)
    {
        if (Env("STUDYSTASH_FILM_LOG") is { Length: > 0 } log) File.AppendAllText(log, $"{DateTime.Now:HH:mm:ss} {line}\n");
    }

    sealed class Film : IDisposable
    {
        public readonly Window Window;
        readonly Canvas over = new() { IsHitTestVisible = false };
        readonly Path arrow = new()
        {
            Data = Geometry.Parse("M0,0 L0,17 L4.6,13 L7.6,20 L10.4,18.8 L7.4,12 L13,12 Z"), Fill = Brushes.Black, Stroke = Brushes.White, StrokeThickness = 1.2,
        };
        readonly Ellipse ring = new() { Width = 26, Height = 26, Stroke = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0)), StrokeThickness = 3, IsVisible = false };
        readonly string name;
        readonly StreamWriter index;
        int frames;
        Point at;

        public Film(string name, Control view, string caption, double width, double height)
        {
            this.name = name;
            Skin.UseTheme(ColourThemes.Default);
            ((App)Application.Current!).UseSkin(SkinKind.Mac);
            over.Children.Add(ring);
            over.Children.Add(arrow);
            var label = new Border
            {
                CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 4), Background = new SolidColorBrush(Color.FromArgb(215, 24, 24, 27)),
                Child = new TextBlock { Text = caption, FontSize = 12, FontWeight = FontWeight.Medium, Foreground = Brushes.White },
            };
            over.Children.Add(label);
            Canvas.SetLeft(label, 16);
            Canvas.SetBottom(label, 40);
            at = new Point(width * 0.62, height * 0.45);
            Window = new Window
            {
                Width = width, Height = height, RequestedThemeVariant = ThemeVariant.Light,
                Content = new Panel { Children = { view, over } },
            };
            Look.Apply(Window);
            index = new StreamWriter(System.IO.Path.Combine(Shot.Dir, "frames.tsv"), append: false) { AutoFlush = true };
            Place();
            Window.Show();
            DiagramsReady.Wait(Window);
            Settle();
        }

        public void Dispose()
        {
            index.Dispose();
            Window.Close();
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
            Canvas.SetLeft(ring, at.X - 13);
            Canvas.SetTop(ring, at.Y - 13);
        }

        /// <summary>One frame, marked for the GIF maker: act (the student's moves), hold, stream (an answer arriving) or wait.</summary>
        public void Frame(string kind = "act")
        {
            Settle();
            var frame = Window.CaptureRenderedFrame() ?? throw new InvalidOperationException("nothing rendered");
            string file = $"{name}-{frames++:0000}.png";
            using (var stream = File.Create(System.IO.Path.Combine(Shot.Dir, file)))
                frame.Save(stream, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            index.WriteLine($"{file}\t{kind}");
        }

        public void Hold(double seconds)
        {
            for (int i = 0; i < Math.Max(1, (int)(seconds * Fps)); i++) Frame("hold");
        }

        public void Glide(Control c, double fx = 0.5, double fy = 0.5, int steps = 10)
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

        /// <summary>Typed into the focused field a few letters a frame, as a student types.</summary>
        public void Type(string text)
        {
            for (int i = 0; i < text.Length; i += 2)
            {
                Window.KeyTextInput(text.Substring(i, Math.Min(2, text.Length - i)));
                Frame();
            }
        }

        /// <summary>Return, pressed for real in the focused field.</summary>
        public void Return()
        {
            Window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");
            Settle();
            Frame();
        }

        /// <summary>Frames while the AI works, until <paramref name="done"/>: a frame each time the answer grows (stream),
        /// one every half second meanwhile (wait). The GIF maker keeps few of the waits.</summary>
        public async Task WhileAsync(Func<bool> done, Func<string?> answer, TimeSpan most)
        {
            var until = DateTime.UtcNow + most;
            string? shown = answer();
            var lastFrame = DateTime.UtcNow;
            while (!done())
            {
                if (DateTime.UtcNow > until) throw new TimeoutException("the answer took too long");
                await Task.Delay(60);
                Settle();
                string? now = answer();
                if (now != shown)
                {
                    shown = now;
                    Frame("stream");
                    lastFrame = DateTime.UtcNow;
                }
                else if (DateTime.UtcNow - lastFrame > TimeSpan.FromMilliseconds(500))
                {
                    Frame("wait");
                    lastFrame = DateTime.UtcNow;
                }
            }
            await Task.Delay(100);
            Settle();
            Frame("stream");
        }
    }

    /// <summary>A throwaway library in this test, on its own port, over a copy of a demo library (its state.db and notes
    /// folder), with the real AIs on this computer: the app asks it over HTTP as it asks any library.</summary>
    sealed class RealLibrary : IAsyncDisposable
    {
        public required WebApplication App { get; init; }
        public required Store Store { get; init; }
        public required LibraryReader Reader { get; init; }
        public required string Url { get; init; }

        public static async Task<RealLibrary> StartAsync(string folder, int port)
        {
            if (port is 8787 or 8788) throw new InvalidOperationException("never 8787: that's the owner's real library");
            var cfg = new Config(System.IO.Path.Combine(folder, "home"), System.IO.Path.Combine(folder, "notes"))
            {
                PoolPassword = "film", OllamaEnabled = true, OllamaModel = Ollama.SmallModel,
                Classes = [new ClassDef("BIO 110"), new ClassDef("CHEM 121"), new ClassDef("MATH 221"), new ClassDef("PSYC 101")],
            };
            var store = new Store(cfg.DbPath, cfg.PoolDir);
            var builder = WebApplication.CreateSlimBuilder();
            builder.WebHost.UseUrls($"http://127.0.0.1:{port}");
            builder.Logging.ClearProviders();
            var app = LibraryWeb.Build(builder, cfg, store, new Pipeline(cfg, store, log: _ => { }), new LibraryWebOptions
            {
                ListModels = _ => Task.FromResult<List<(string, double)>?>(null), Tailscale = () => new TailscaleInfo(),
                Latest = _ => Task.FromResult<Release?>(null), Ai = new AiJobs(cfg.Home),
            });
            await app.StartAsync();
            return new RealLibrary { App = app, Store = store, Reader = new LibraryReader(cfg, store), Url = $"http://127.0.0.1:{port}" };
        }

        public async ValueTask DisposeAsync()
        {
            await App.StopAsync();
            await App.DisposeAsync();
            Store.Dispose();
        }
    }

    /// <summary>A stand-in for the library's AI: it answers the film's questions the way a library that remembers does,
    /// a few words at a time.</summary>
    static FakeAiLibrary StandIn()
    {
        var lib = new FakeAiLibrary { Overview = AiTestData.MixedOverview() };
        lib.OnAskStream = async (r, soFar, stop) =>
        {
            string answer = r.History is { Count: > 0 }
                ? "The second one is **capillaries**: tiny vessels with walls just one cell thick, so oxygen, carbon dioxide and nutrients pass straight through to the tissues."
                : "There are three: **arteries** carry blood away from the heart under high pressure, **capillaries** are one cell thick where gases and nutrients are exchanged, and **veins** carry blood back at low pressure, with valves to stop backflow.";
            await Task.Delay(900, stop);
            for (int i = 12; i < answer.Length; i += 12)
            {
                soFar(answer[..i]);
                await Task.Delay(70, stop);
            }
            return new AskReply(answer, [new AskSource("v1", "Blood vessels", "BIO 110", null, 4, "")], "claude", "Claude") { Remembered = r.History?.Count ?? 0 };
        };
        return lib;
    }

    static LibraryModel Page(string id, string title, string when, string markdown, AiAskModel ask)
    {
        string[] classes = ["BIO 110", "CHEM 121", "MATH 221", "PSYC 101"];
        var m = new LibraryModel { Status = "Library on this Mac", DrawChrome = false };
        for (int i = 0; i < classes.Length; i++)
            m.Classes.Add(new ClassItem { Name = classes[i], Dot = Skin.ClassDot(i), Count = 4, Selected = i == 0 });
        m.Note = new NoteModel
        {
            Id = id, ClassName = "BIO 110", Dot = Skin.ClassDot(0), Noun = Nouns.Lecture, When = when, Title = title, Markdown = markdown,
            OnRename = _ => Task.FromResult(true),
        }.Titled();
        m.Notes = AiDemo.LectureNotes(markdown, "Claude");
        m.Ask = ask;
        return m;
    }

    [AvaloniaFact]
    public async Task A_follow_up_that_needs_the_question_before_it_then_New_chat()
    {
        if (!On) return;
        string home = Env("STUDYSTASH_FILM_HOME");
        int port = int.Parse(Env("STUDYSTASH_FILM_PORT", "9471"), System.Globalization.CultureInfo.InvariantCulture);
        await using var library = home.Length > 0 ? await RealLibrary.StartAsync(home, port) : null;
        const string lecture = "rec-20261008-093000-c00000";
        string markdown = library?.Reader.Lecture(lecture)?["notes"]?.GetValue<string>()
            ?? "## Summary\nHow arteries, capillaries and veins differ, and how blood pressure is measured and worked out.\n\n### Three kinds of vessel\n- **Arteries** carry blood away from the heart under high pressure; thick, elastic walls.\n- **Capillaries** are one cell thick: this is where gases and nutrients are exchanged.\n- **Veins** carry blood back at low pressure and have valves to stop backflow.";
        IAiLibrary ai = library is null ? StandIn() : new AiRemote(library.Url, "film");
        var ask = new AiAskModel(ai) { LectureId = lecture, ClassName = "BIO 110", OpenSettings = () => { } };
        ask.NameScopes(Nouns.Lecture, "BIO 110");
        await ask.Load();
        ask.Engine = Env("STUDYSTASH_FILM_ENGINE", "claude");
        Log($"library {(library is null ? "stand-in" : library.Url)}, engine {ask.Engine} ({ask.EngineName})");

        var model = Page(lecture, "Blood vessels and blood pressure", "Thursday 8 October at 9:30 AM, 50 min", markdown, ask);
        var view = new MacLibrary { DataContext = model };
        using var f = new Film("ask-remembers", view, Env("STUDYSTASH_FILM_CLI", "STAND-IN: not a real AI"), 1180, 820);
        var field = view.GetVisualDescendants().OfType<TextBox>().First(t => t.Name == "AskInput" && t.IsEffectivelyVisible);
        f.Hold(0.8);

        async Task AskAsync(string question)
        {
            f.Glide(field, 0.3, 0.5);
            f.Click();
            f.Type(question);
            f.Hold(0.3);
            f.Return();
            var turn = ask.Turns[^1];
            await f.WhileAsync(() => !ask.Busy, () => turn.Answer, TimeSpan.FromMinutes(3));
            Log($"Q: {question}\nA ({turn.Engine}): {turn.Answer}\n   byline: {turn.Byline}{(turn.Failed is { } why ? "\n   failed: " + why : "")}{(turn.FellBackNote is { } fb ? "\n   " + fb : "")}");
            f.Hold(2.4);
        }

        await AskAsync("What are the three kinds of blood vessel?");
        await AskAsync("And the second one?");

        // New chat: the conversation is put away, and the field is ready for a question that starts afresh.
        var newChat = view.GetVisualDescendants().OfType<Button>().First(b => b.Name == "NewChat" && b.IsEffectivelyVisible);
        f.Glide(newChat);
        f.Click();
        f.Hold(1.6);
        Log($"after New chat: {ask.Turns.Count} turns, the card {(ask.HasLatest ? "shows" : "is gone")}");
    }

    /// <summary>The free AI answers the first questions, then Claude a follow-up: the chat above goes to Claude with it,
    /// and its byline says so. A still of the page at the end (ask-switch.png), and every answer in the log. Only with
    /// STUDYSTASH_FILM_SWITCH=1 as well: it asks the free AI three times and Claude once.</summary>
    [AvaloniaFact]
    public async Task The_chat_goes_to_Claude_after_the_free_AI()
    {
        if (!On || Env("STUDYSTASH_FILM_SWITCH") != "1") return;
        int port = int.Parse(Env("STUDYSTASH_FILM_PORT", "9471"), System.Globalization.CultureInfo.InvariantCulture);
        await using var library = await RealLibrary.StartAsync(Env("STUDYSTASH_FILM_HOME"), port);
        const string lecture = "rec-20261008-093000-c00000";
        string markdown = library.Reader.Lecture(lecture)!["notes"]!.GetValue<string>();
        var ask = new AiAskModel(new AiRemote(library.Url, "film")) { LectureId = lecture, ClassName = "BIO 110", OpenSettings = () => { } };
        ask.NameScopes(Nouns.Lecture, "BIO 110");
        await ask.Load();
        var model = Page(lecture, "Blood vessels and blood pressure", "Thursday 8 October at 9:30 AM, 50 min", markdown, ask);
        var view = new MacLibrary { DataContext = model };
        using var f = new Film("ask-switch", view, Env("STUDYSTASH_FILM_CLI", "STAND-IN: not a real AI"), 1180, 820);
        foreach (var (engine, question) in new[]
                 {
                     ("ollama", "What are the three kinds of blood vessel?"), ("ollama", "And the second one?"), ("ollama", "Explain that more simply"),
                     ("claude", "Which of those has valves?"),
                 })
        {
            ask.Engine = engine;
            ask.Question = question;
            var asking = ask.AskCommand.ExecuteAsync(null);
            var turn = ask.Turns[^1];
            await f.WhileAsync(() => asking.IsCompleted, () => null, TimeSpan.FromMinutes(3));
            Log($"Q ({engine}): {question}\nA ({turn.Engine}): {turn.Answer}\n   byline: {turn.Byline}{(turn.Failed is { } why ? "\n   failed: " + why : "")}{(turn.FellBackNote is { } fb ? "\n   " + fb : "")}");
        }
        f.Frame("hold");
        var still = f.Window.CaptureRenderedFrame()!;
        using var file = File.Create(System.IO.Path.Combine(Shot.Dir, "ask-switch.png"));
        still.Save(file, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }
}
