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
using StudyStash.Core.Tests;
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
        /// <summary>What a flyout shows, drawn in place: a popup isn't part of the window's own picture.</summary>
        readonly Canvas popups = new();
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
                Content = new Panel { Children = { view, popups, over } },
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

        /// <summary>A flyout's content drawn above <paramref name="anchor"/>, centred on it, as its Placement="Top" puts it.</summary>
        public void Popup(Control content, Control anchor)
        {
            popups.Children.Add(content);
            Settle();
            var at = anchor.TranslatePoint(new Point(anchor.Bounds.Width / 2, 0), Window)!.Value;
            Canvas.SetLeft(content, at.X - content.Bounds.Width / 2);
            Canvas.SetTop(content, at.Y - 12 - content.Bounds.Height);
            Settle();
        }

        public void ClosePopups() => popups.Children.Clear();

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
        public string Folder { get; init; } = "";

        public static async Task<RealLibrary> StartAsync(string folder, int port, Func<string, AiProvider>? standIn = null)
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
                Latest = _ => Task.FromResult<Release?>(null),
                Ai = standIn is null ? new AiJobs(cfg.Home) : new AiJobs(cfg.Home) { Providers = standIn, Checks = new FakeChecks().Installed("claude").Build() },
            });
            await app.StartAsync();
            return new RealLibrary { App = app, Store = store, Reader = new LibraryReader(cfg, store), Url = $"http://127.0.0.1:{port}", Folder = standIn is null ? "" : folder };
        }

        bool disposed;

        public async ValueTask DisposeAsync()
        {
            if (disposed) return;
            disposed = true;
            await App.StopAsync();
            await App.DisposeAsync();
            Store.Dispose();
        }
    }

    const string Lecture = "rec-20261008-093000-c00000";

    /// <summary>A stand-in library for checking the film for free: the demo lecture alone, in a folder of its own.</summary>
    static string StandInFolder()
    {
        string folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ask-film-" + Guid.NewGuid().ToString("N")[..8]);
        var cfg = new Config(System.IO.Path.Combine(folder, "home"), System.IO.Path.Combine(folder, "notes")) { Classes = [new ClassDef("BIO 110")] };
        using var store = new Store(cfg.DbPath, cfg.PoolDir);
        store.Save(new Meeting(Lecture) { Title = "Blood vessels and blood pressure", Date = "2026-10-08", Transcript = "Arteries, capillaries and veins." },
            new Classification("BIO 110", 1, "folder"),
            "## Summary\nHow arteries, capillaries and veins differ, and how blood pressure is measured and worked out.\n\n### Three kinds of vessel\n- **Arteries** carry blood away from the heart under high pressure; thick, elastic walls.\n- **Capillaries** are one cell thick: this is where gases and nutrients are exchanged.\n- **Veins** carry blood back at low pressure and have valves to stop backflow.",
            "Claude");
        return folder;
    }

    /// <summary>A stand-in Claude that writes <paramref name="answer"/> a few words at a time, as JSON.</summary>
    static void Says(ScriptedAi ai, string answer)
    {
        string json = System.Text.Json.JsonSerializer.Serialize(new { answer, sources = new[] { 1 } });
        ai.Pause = TimeSpan.FromMilliseconds(60);
        ai.Script = [.. Enumerable.Range(0, (json.Length + 11) / 12).Select(i => new AiEvent("text", json.Substring(i * 12, Math.Min(12, json.Length - i * 12)))), new AiEvent("final", json)];
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
    public async Task Ask_close_the_card_open_the_chat_again_from_Chats_and_carry_it_on()
    {
        if (!On) return;
        string home = Env("STUDYSTASH_FILM_HOME");
        int port = int.Parse(Env("STUDYSTASH_FILM_PORT", "9471"), System.Globalization.CultureInfo.InvariantCulture);
        var standIn = home.Length > 0 ? null : new ScriptedAi("claude", "Claude");
        await using var library = await RealLibrary.StartAsync(home.Length > 0 ? home : StandInFolder(), port, standIn is null ? null : id => id == "claude" ? standIn : new ScriptedAi(id));
        string markdown = library.Reader.Lecture(Lecture)!["notes"]!.GetValue<string>();
        var ask = new AiAskModel(new AiRemote(library.Url, "film"))
        {
            LectureId = Lecture, ClassName = "BIO 110", OpenSettings = () => { }, Place = new AskPlace("lecture", Lecture, "BIO 110"),
        };
        ask.NameScopes(Nouns.Lecture, "BIO 110");
        await ask.Load();
        ask.Engine = Env("STUDYSTASH_FILM_ENGINE", "claude");
        Log($"library {library.Url}{(standIn is null ? "" : " (stand-in AI)")}, engine {ask.Engine} ({ask.EngineName}), {ask.Chats.Count} chats before");

        var model = Page(Lecture, "Blood vessels and blood pressure", "Thursday 8 October at 9:30 AM, 50 min", markdown, ask);
        var view = new MacLibrary { DataContext = model };
        using var f = new Film("ask-history", view, Env("STUDYSTASH_FILM_CLI", "STAND-IN: not a real AI"), 1180, 820);
        var field = view.GetVisualDescendants().OfType<TextBox>().First(t => t.Name == "AskInput" && t.IsEffectivelyVisible);
        ask.ChatsDone += f.ClosePopups;
        f.Hold(0.8);

        async Task AskAsync(string question, string standInAnswer)
        {
            if (standIn is not null) Says(standIn, standInAnswer);
            f.Glide(field, 0.3, 0.5);
            f.Click();
            f.Type(question);
            f.Hold(0.3);
            f.Return();
            var turn = ask.Turns[^1];
            await f.WhileAsync(() => !ask.Busy, () => turn.Answer, TimeSpan.FromMinutes(3));
            Log($"Q: {question}\nA ({turn.Engine}): {turn.Answer}\n   byline: {turn.Byline}{(turn.Failed is { } why ? "\n   failed: " + why : "")}{(turn.FellBackNote is { } fb ? "\n   " + fb : "")}\n   chat {ask.ChatId}");
            f.Hold(2.2);
        }

        await AskAsync("What are the three kinds of blood vessel?",
            "There are three: **arteries** carry blood away from the heart under high pressure, **capillaries** are one cell thick where gases and nutrients are exchanged, and **veins** carry blood back at low pressure, with valves.");

        // Closed: the notes get their room back, and the conversation is kept.
        var close = view.GetVisualDescendants().OfType<Button>().First(b => b.Classes.Contains("ai-close") && b.IsEffectivelyVisible);
        f.Glide(close);
        f.Click();
        f.Hold(1.0);

        // Chats, in the ask bar: this lecture's conversations; the one from before opens again.
        var chats = view.GetVisualDescendants().OfType<Button>().First(b => b.Name == "ChatsButton" && b.IsEffectivelyVisible);
        f.Glide(chats);
        f.Click();
        chats.Flyout?.Hide(); // the real flyout opened off the picture: it's drawn in place instead
        f.Popup(new AiChatsMenu { DataContext = ask }, chats);
        f.Hold(1.0);
        var row = f.Window.GetVisualDescendants().OfType<AiChatsMenu>().Single().GetVisualDescendants().OfType<Button>()
            .First(b => b.DataContext is ChatRow r && b.Command == r.Open);
        f.Glide(row, 0.3, 0.5);
        f.Click();
        await Task.Delay(300);
        f.Hold(1.4);
        Log($"opened again from Chats: {ask.Turns.Count} turn(s), chat {ask.ChatId}, {ask.Chats.Count} in the list");

        await AskAsync("And the second one?",
            "The second one is **capillaries**: their walls are just one cell thick, so oxygen and nutrients pass straight through to the tissues.");
        Log($"after: {ask.Turns.Count} turns in chat {ask.ChatId}, {ask.Chats.Count} in the list");
        if (home.Length == 0) await library.DisposeAsync(); // the stand-in's folder goes too
        if (home.Length == 0 && library.Folder.Length > 0) Directory.Delete(library.Folder, recursive: true);
    }

    /// <summary>A still of Home's Chats list, with a stand-in's chats from a lecture, a folder and Home itself, each saying
    /// where it was asked (ask-chats-home.png): no AI is asked.</summary>
    [AvaloniaFact]
    public void Homes_Chats_list()
    {
        if (!On) return;
        Skin.UseTheme(ColourThemes.Default);
        ((App)Application.Current!).UseSkin(SkinKind.Mac);
        var ask = new AiAskModel(new FakeAiLibrary { Overview = AiTestData.MixedOverview() }) { Place = new AskPlace("home") };
        var now = new DateTimeOffset(2026, 10, 21, 16, 40, 0, TimeSpan.FromHours(-7));
        foreach (var (title, scope, cls, lecture, minutes) in new[]
                 {
                     ("What are the three kinds of blood vessel?", "lecture", "BIO 110", "Blood vessels and blood pressure", 5),
                     ("What's on the CHEM 121 midterm?", "class", "CHEM 121", "", 60 * 20),
                     ("Which lectures talk about memory?", "home", "", "", 60 * 50),
                     ("How do I use the chain rule here?", "lecture", "MATH 221", "The chain rule", 60 * 24 * 5),
                 })
            ask.Chats.Add(new ChatRow(new AskChat("c" + minutes, title, now.AddMinutes(-minutes).ToString("o")) { Scope = scope, Class = cls, LectureTitle = lecture },
                scope == "lecture" ? lecture : scope == "class" ? cls : "", now, ask));
        var window = new Window
        {
            Width = 380, Height = 300, RequestedThemeVariant = ThemeVariant.Light, Background = new SolidColorBrush(Color.Parse("#ECEEF1")),
            Content = new AiChatsMenu { DataContext = ask, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        Look.Apply(window);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        using (var file = File.Create(System.IO.Path.Combine(Shot.Dir, "ask-chats-home.png")))
            window.CaptureRenderedFrame()!.Save(file, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        window.Close();
    }

    /// <summary>A still of the conversation card in the Windows look, light and dark, its words a stand-in's
    /// (ask-card-win-*.png): no AI is asked.</summary>
    [AvaloniaFact]
    public void The_conversation_card_in_the_Windows_look()
    {
        if (!On) return;
        foreach (var variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            var ask = new AiAskModel(new FakeAiLibrary { Overview = AiTestData.MixedOverview() });
            ask.Turns.Add(new AiTurn("What are the three kinds of blood vessel?", "Ollama")
            {
                Answer = "Arteries carry blood away from the heart under high pressure, capillaries are one cell thick where gases and nutrients are exchanged, and veins bring blood back at low pressure, with valves.",
                Byline = "Ollama", Engine = "ollama",
            });
            ask.Turns.Add(new AiTurn("And the second one?", "Claude")
            {
                Answer = "The second one is **capillaries**: their walls are one cell thick, so oxygen and nutrients pass straight through to the tissues.",
                Byline = "Claude" + AiWords.ReadChat, Engine = "claude",
            });
            Skin.UseTheme(ColourThemes.Default);
            ((App)Application.Current!).UseSkin(SkinKind.Win);
            var card = new WinAiAnswer { DataContext = ask, VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 24) };
            var window = new Window
            {
                Width = 760, Height = 700, RequestedThemeVariant = variant, Content = card,
                Background = new SolidColorBrush(variant == ThemeVariant.Dark ? Color.Parse("#1F1F1F") : Color.Parse("#F3F3F3")),
            };
            Look.Apply(window);
            window.Show();
            DiagramsReady.Wait(window);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            using (var file = File.Create(System.IO.Path.Combine(Shot.Dir, $"ask-card-win-{variant.Key.ToString()!.ToLowerInvariant()}.png")))
                window.CaptureRenderedFrame()!.Save(file, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            window.Close();
        }
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
