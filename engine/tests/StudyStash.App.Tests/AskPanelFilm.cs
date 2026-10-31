using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StudyStash.App.ViewModels;
using StudyStash.App.Views;
using StudyStash.Core;
using StudyStash.Core.Ai;
using StudyStash.Core.Tests;

namespace StudyStash.App.Tests;

/// <summary>
/// Ask's conversation opening beside the notes, drawn off screen into STUDYSTASH_SHOTS (only with STUDYSTASH_FILM=1): a
/// film of the bar at rest, a question typed and sent, the bar gliding into the panel as it opens, the answer arriving,
/// a follow-up, and Close gliding it all back (frames.tsv marks the glides' frames "glide", drawn a thirtieth of a second
/// apart); and stills of the panel in both looks, light and dark, at three window widths, on a lecture and on Home, with
/// long answers, a long question and a long folder name, its menus drawn where they open. A stand-in answers.
/// </summary>
public class AskPanelFilm
{
    /// <summary>The glide, stepped by the film a frame at a time instead of the screen's clock.</summary>
    sealed class Stepper(Action<double> step, Action? done) : IDisposable
    {
        public bool Stopped;
        public void Run(AskRemembersFilm.Film f)
        {
            const int frames = 10; // 320 ms at 30 frames a second
            for (int i = 1; i <= frames && !Stopped; i++)
            {
                double t = i / (double)frames;
                step(1 - Math.Pow(1 - t, 3));
                f.Frame("glide");
            }
            if (!Stopped) done?.Invoke();
            Stopped = true;
        }

        public void Begin() => step(0);
        public void Dispose() => Stopped = true;
    }

    const string LongAnswer = """
        Attention scores come from comparing each token's query with every token's key:

        1. **Project** each token into a query $q$, a key $k$ and a value $v$.
        2. **Score** every pair: $s_{ij} = \frac{q_i \cdot k_j}{\sqrt{d_k}}$, so the scores don't grow with the size of the vectors.
        3. **Weigh** the scores with softmax and add up the values with those weights.

        $$\text{Attention}(Q,K,V) = \text{softmax}\left(\frac{QK^T}{\sqrt{d_k}}\right)V$$

        | Piece | Shape | What it does |
        |---|---|---|
        | $Q$ | $n \times d_k$ | asks what to look for |
        | $K$ | $n \times d_k$ | says what each token offers |
        | $V$ | $n \times d_v$ | carries what's passed on |

        In code it's three lines:

        ```python
        scores = Q @ K.T / math.sqrt(d_k)
        weights = softmax(scores, axis=-1)
        out = weights @ V
        ```

        Dividing by $\sqrt{d_k}$ keeps softmax out of its flat ends, where the gradients all but vanish.
        """;

    const string LongQuestion = "Can you explain, step by step and with the formula she wrote on the board, how the attention scores are worked out and why we divide by the square root of d_k?";

    static AiTurn Turn(string q, string a) => new(q, "Claude")
    {
        Answer = a, Engine = "claude",
        BylineParts = AiWords.AskBylineParts("Claude", [new AskSource("n1", "Transformers", null, null, 10 * 60 + 48, ""), new AskSource("n1", "Transformers", null, null, 31 * 60 + 5, "")], _ => { }, false),
        Byline = "Claude · from 10:48 and 31:05",
    };

    [AvaloniaFact]
    public async Task The_bar_glides_into_the_panel_and_back()
    {
        if (!AskRemembersFilm.On) return;
        var standIn = new ScriptedAi("claude", "Claude");
        await using var library = await AskRemembersFilm.RealLibrary.StartAsync(AskRemembersFilm.StandInFolder(), 9473, id => id == "claude" ? standIn : new ScriptedAi(id));
        string markdown = library.Reader.Lecture(AskRemembersFilm.Lecture)!["notes"]!.GetValue<string>();
        var ask = new AiAskModel(new AiRemote(library.Url, "film"))
        {
            LectureId = AskRemembersFilm.Lecture, ClassName = "BIO 110", OpenSettings = () => { }, Place = new AskPlace("lecture", AskRemembersFilm.Lecture, "BIO 110"),
        };
        ask.NameScopes(Nouns.Lecture, "BIO 110");
        await ask.Load();
        ask.Engine = "claude";
        var model = AskRemembersFilm.Page(AskRemembersFilm.Lecture, "Blood vessels and blood pressure", "Thursday 8 October at 9:30 AM, 50 min", markdown, ask);
        var view = new MacLibrary { DataContext = model };
        Stepper? glide = null;
        var was = AskSplit.Animate;
        AskSplit.Animate = (_, _, step, done) => (glide = new Stepper(step, done)).Also(s => s.Begin());
        try
        {
            using var f = new AskRemembersFilm.Film("ask-panel", view, "STAND-IN: not a real AI", 1180, 820);
            var field = view.GetVisualDescendants().OfType<TextBox>().First(t => t.Name == "AskInput" && t.IsEffectivelyVisible);
            f.Hold(0.8);

            async Task AskAsync(string question, string answer)
            {
                AskRemembersFilm.Says(standIn, answer);
                f.Glide(field, 0.3, 0.5);
                f.Click();
                f.Type(question);
                f.Hold(0.3);
                glide = null;
                f.Return();
                glide?.Run(f);
                var turn = ask.Turns[^1];
                await f.WhileAsync(() => !ask.Busy, () => turn.Answer, TimeSpan.FromMinutes(1));
                f.Hold(1.6);
            }

            await AskAsync("What are the three kinds of blood vessel?",
                "There are three, and they differ in which way the blood goes and how hard it's pushed:\n\n- **Arteries** carry blood away from the heart under high pressure, so their walls are thick and elastic.\n- **Capillaries** are one cell thick: this is where oxygen, nutrients and waste cross to and from the tissues.\n- **Veins** bring blood back at low pressure, with valves that stop it flowing backwards.\n\nBlood pressure is highest in the arteries and falls all the way round: $MAP \\approx DBP + \\frac{1}{3}(SBP - DBP)$.");
            await AskAsync("Why do veins need valves?",
                "Because by the time blood reaches the veins most of the heart's push is spent. In the legs it has to climb back up against gravity, so the valves let it move only towards the heart, and the leg muscles squeeze the veins as you walk, which pumps it up a valve at a time.");

            // Close: the panel goes, the notes take the page back, the bar glides home.
            var close = view.GetVisualDescendants().OfType<Button>().First(b => b.Name == "Close" && b.IsEffectivelyVisible);
            f.Glide(close);
            glide = null;
            f.Click();
            glide?.Run(f);
            f.Hold(1.4);
        }
        finally
        {
            AskSplit.Animate = was;
        }
        await library.DisposeAsync();
        if (library.Folder.Length > 0) Directory.Delete(library.Folder, recursive: true);
    }

    /// <summary>The panel open at <paramref name="width"/>, in <paramref name="skin"/>, light or dark.</summary>
    static void Still(string name, SkinKind skin, ThemeVariant theme, double width, LibraryModel m, Action<Window, Control>? also = null, double height = 820)
    {
        Skin.UseTheme(ColourThemes.Default);
        ((App)Application.Current!).UseSkin(skin);
        Control view = skin == SkinKind.Mac ? new MacLibrary { DataContext = m } : new WinLibrary { DataContext = m };
        var popups = new Canvas();
        var window = new Window { Width = width, Height = height, RequestedThemeVariant = theme, Content = new Panel { Children = { view, popups } } };
        Look.Apply(window);
        window.Show();
        DiagramsReady.Wait(window);
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        if (also is not null)
        {
            also(window, view);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
        }
        using (var file = File.Create(Path.Combine(Shot.Dir, $"{name}-{(skin == SkinKind.Mac ? "mac" : "win")}-{(theme == ThemeVariant.Dark ? "dark" : "light")}-{width}.png")))
            window.CaptureRenderedFrame()!.Save(file, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        window.Close();
    }

    /// <summary>A menu's content drawn where its flyout puts it (see <see cref="AskRemembersFilm.Film.Popup(Control, Control, bool)"/>).</summary>
    static void DrawPopup(Window window, Control content, Control anchor, bool below)
    {
        var canvas = ((Panel)window.Content!).Children.OfType<Canvas>().Single();
        canvas.Children.Add(content);
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        var box = new Rect(anchor.TranslatePoint(default, window)!.Value, anchor.Bounds.Size);
        Canvas.SetLeft(content, below ? box.Left : box.Right - content.Bounds.Width);
        Canvas.SetTop(content, below ? box.Bottom + 4 : box.Top - 12 - content.Bounds.Height);
    }

    static LibraryModel Lecture(bool longFolder = false)
    {
        var m = Demo.Semester(Demo.Page.Note);
        m.Ask!.Turns.Add(Turn(LongQuestion, LongAnswer));
        m.Ask.Turns.Add(Turn("And the second one?", "The second step is the **score**: each query against every key, divided by $\\sqrt{d_k}$."));
        if (longFolder)
        {
            m.Ask.NameScopes(Nouns.Lecture, "Introduction to Machine Learning and Neural Networks (CSCI 4220)");
            m.Ask.Scope = "class";
        }
        return m;
    }

    static LibraryModel Home()
    {
        var m = Demo.Semester(Demo.Page.Home);
        var ask = m.Overview!.Ask!;
        ask.Turns.Add(Turn("Which lectures talk about attention?", "Three of them do:\n\n- **Transformers, Self-Attention, and LLM Fundamentals** (Machine Learning, Monday) works it out from scratch.\n- **Sequence models** (Machine Learning, last Wednesday) brings it in as a fix for RNNs forgetting.\n- **Memory hierarchies** (Computer Architecture) uses the word for something else: which cache lines get kept."));
        ask.Turns.Add(Turn(LongQuestion, LongAnswer));
        return m;
    }

    [AvaloniaFact]
    public void Stills_of_the_panel()
    {
        if (!AskRemembersFilm.On) return;
        Platform.Motion.Override = true;
        try
        {
            foreach (var skin in new[] { SkinKind.Mac, SkinKind.Win })
                foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
                    foreach (double width in new[] { 900.0, 1180, 1600 })
                    {
                        Still("panel-lecture", skin, theme, width, Lecture());
                        Still("panel-home", skin, theme, width, Home());
                    }

            foreach (var skin in new[] { SkinKind.Mac, SkinKind.Win })
            {
                // At rest, a long folder name in the scope and a long question typed.
                var rest = Lecture(longFolder: true);
                rest.Ask!.CloseAnswerCommand.Execute(null);
                rest.Ask.Question = LongQuestion;
                Still("rest-long", skin, ThemeVariant.Light, 1180, rest);
                Still("rest-long", skin, ThemeVariant.Light, 900, rest);

                // Docked: a long folder name and a long question in the bar.
                var docked = Lecture(longFolder: true);
                docked.Ask!.Question = LongQuestion;
                Still("docked-long", skin, ThemeVariant.Light, 1180, docked);

                // The sidebar hidden at 900: the page area is wide enough to split.
                var hidden = Lecture();
                hidden.SidebarHidden = true;
                Still("panel-nosidebar", skin, ThemeVariant.Light, 900, hidden);

                // The menus, drawn where they open: Chats from the panel's header, Answer with and the scope from the docked bar.
                var menus = Lecture(longFolder: true);
                ((AiAskModel)menus.Ask!).Chats.Add(new ChatRow(new AskChat("c1", LongQuestion, DateTimeOffset.Now.ToString("o")), "", DateTimeOffset.Now, menus.Ask));
                Still("menu-chats", skin, ThemeVariant.Light, 1180, menus, (w, v) =>
                    DrawPopup(w, new AiChatsMenu { DataContext = menus.Ask }, v.GetVisualDescendants().OfType<Button>().First(b => b.Name == "ChatsButton" && b.IsEffectivelyVisible), below: true));
                Still("menu-engine", skin, ThemeVariant.Light, 1180, menus, (w, v) =>
                {
                    Control menu = skin == SkinKind.Mac ? new MacAiEngineMenu { DataContext = menus.Ask.Menu } : new WinAiEngineMenu { DataContext = menus.Ask.Menu };
                    DrawPopup(w, menu, v.GetVisualDescendants().OfType<Button>().First(b => b.Name == "EngineChip" && b.IsEffectivelyVisible), below: false);
                });

                // The transcript tab, notes being edited, and a lecture that's recording, each with the panel open.
                var transcript = Lecture();
                transcript.Note!.ShowTranscript = true;
                Still("panel-transcript", skin, ThemeVariant.Light, 1180, transcript);
                var editing = Lecture();
                editing.Notes!.EditCommand.Execute(null);
                Still("panel-editing", skin, ThemeVariant.Light, 1180, editing);
                var recording = Demo.Semester(Demo.Page.Recording);
                recording.Ask!.Turns.Add(Turn("What did she say about the key vectors?", "That each token's **key** says what it offers, and the query is compared against every key."));
                Still("panel-recording", skin, ThemeVariant.Light, 1180, recording);
                // The recorder's compact chat with long words in it (it isn't the panel: only checked for overlaps).
                var chat = AiDemo.Chat();
                chat.Turns.Add(Turn(LongQuestion, LongAnswer));
                chat.Question = LongQuestion;
                Skin.UseTheme(ColourThemes.Default);
                ((App)Application.Current!).UseSkin(skin);
                var chatWindow = new Window
                {
                    Width = 420, Height = 640, RequestedThemeVariant = ThemeVariant.Light,
                    Content = skin == SkinKind.Mac ? new MacAiAskChat { DataContext = chat, Width = 360, Height = 560 } : new WinAiAskChat { DataContext = chat, Width = 360, Height = 560 },
                };
                Look.Apply(chatWindow);
                chatWindow.Show();
                DiagramsReady.Wait(chatWindow);
                Dispatcher.UIThread.RunJobs();
                chatWindow.UpdateLayout();
                using (var file = File.Create(Path.Combine(Shot.Dir, $"recorder-chat-{(skin == SkinKind.Mac ? "mac" : "win")}.png")))
                    chatWindow.CaptureRenderedFrame()!.Save(file, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
                chatWindow.Close();

                // Scrolled, with Notes | Transcript pinned at the top beside the panel.
                Still("panel-pinned", skin, ThemeVariant.Light, 1180, Lecture(), (_, v) =>
                {
                    var scroll = v.GetVisualDescendants().OfType<ScrollViewer>().First(s => s.Name == "NoteScroll");
                    scroll.Offset = new Vector(0, 600);
                });
            }
        }
        finally
        {
            Platform.Motion.Override = null;
        }
    }
}

