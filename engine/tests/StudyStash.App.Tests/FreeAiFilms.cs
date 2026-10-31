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
using StudyStash.App.ViewModels;
using StudyStash.App.Views;
using StudyStash.Core;
using StudyStash.Core.Ai;
using Path = Avalonia.Controls.Shapes.Path;

namespace StudyStash.App.Tests;

/// <summary>
/// Pictures and films of notes from an AI Syft Notes doesn't run, drawn off screen into STUDYSTASH_SHOTS (the way the
/// Semester films were made: a pointer drawn over the window, each frame saved). The films show what real runs made:
/// STUDYSTASH_FREE_AI_RUN names the folder a lane's real runs left (run1: the notes real Claude Code saved through the
/// connection; run2: what it wrote as a plain chat, given "Copy for your AI"'s text, and those notes as the library
/// saved them pasted). Only with STUDYSTASH_FILM=1: they'd add hundreds of pictures to every run.
/// </summary>
public class FreeAiFilms
{
    const int Fps = 12;

    static bool On => Environment.GetEnvironmentVariable("STUDYSTASH_FILM") == "1";

    static string Run(string file) => System.IO.Path.Combine(Environment.GetEnvironmentVariable("STUDYSTASH_FREE_AI_RUN") ?? "", file);

    sealed class Film
    {
        public readonly Window Window;
        readonly Canvas over = new() { IsHitTestVisible = false };
        readonly Path arrow = new()
        {
            Data = Geometry.Parse("M0,0 L0,17 L4.6,13 L7.6,20 L10.4,18.8 L7.4,12 L13,12 Z"), Fill = Brushes.Black, Stroke = Brushes.White, StrokeThickness = 1.2,
        };
        readonly Ellipse ring = new() { Width = 26, Height = 26, Stroke = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0)), StrokeThickness = 3, IsVisible = false };
        readonly Border keys = new()
        {
            CornerRadius = new CornerRadius(10), Padding = new Thickness(14, 6), Background = new SolidColorBrush(Color.FromArgb(225, 24, 24, 27)), IsVisible = false,
            Child = new TextBlock { FontSize = 20, FontWeight = FontWeight.SemiBold, Foreground = Brushes.White },
        };
        /// <summary>A caption over the bottom of the frame: what's happening off the page, and who did it.</summary>
        readonly Border caption = new()
        {
            CornerRadius = new CornerRadius(14), Padding = new Thickness(18, 12), Background = new SolidColorBrush(Color.FromArgb(235, 24, 24, 27)), IsVisible = false,
            MaxWidth = 760,
            Child = new StackPanel { Spacing = 4 },
        };
        readonly string name;
        int frames;
        Point at;

        /// <summary>The look and theme are set before <paramref name="build"/> makes the view: a view takes the look it's
        /// made in.</summary>
        public Film(string name, Func<Control> build, SkinKind skin, ThemeVariant variant, double width, double height, ColourTheme? theme = null)
        {
            this.name = name;
            Skin.UseTheme(theme ?? ColourThemes.Default);
            ((App)Application.Current!).UseSkin(skin);
            var view = build();
            over.Children.Add(ring);
            over.Children.Add(keys);
            over.Children.Add(caption);
            over.Children.Add(arrow);
            at = new Point(width * 0.72, height * 0.55);
            Window = new Window { Width = width, Height = height, RequestedThemeVariant = variant, Content = new Panel { Children = { view, over } } };
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
            Canvas.SetLeft(ring, at.X - 13);
            Canvas.SetTop(ring, at.Y - 13);
        }

        public void NoPointer() => arrow.IsVisible = false;
        public void Pointer() => arrow.IsVisible = true;

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

        public void Glide(Point to, int steps = 10)
        {
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

        public void Glide(Control c, double fx = 0.5, double fy = 0.5, int steps = 10)
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

        /// <summary>Keys shown under the pointer for a moment (what the student pressed), without pressing them.</summary>
        public void Keys(string shown)
        {
            ((TextBlock)keys.Child!).Text = shown;
            keys.IsVisible = true;
            Settle();
            Canvas.SetLeft(keys, Math.Clamp(at.X - keys.Bounds.Width / 2, 8, Window.Width - keys.Bounds.Width - 8));
            Canvas.SetTop(keys, Math.Min(at.Y + 34, Window.Height - keys.Bounds.Height - 8));
            Hold(0.7);
            keys.IsVisible = false;
        }

        /// <summary>The caption's lines (the first bold), or none to take it away.</summary>
        public void Caption(params string[] lines)
        {
            var stack = (StackPanel)caption.Child!;
            stack.Children.Clear();
            for (int i = 0; i < lines.Length; i++)
                stack.Children.Add(new TextBlock
                {
                    Text = lines[i], TextWrapping = TextWrapping.Wrap, Foreground = i == 0 ? Brushes.White : new SolidColorBrush(Color.FromArgb(215, 255, 255, 255)),
                    FontSize = i == 0 ? 17 : 14, FontWeight = i == 0 ? FontWeight.SemiBold : FontWeight.Normal,
                    FontFamily = lines[i].StartsWith("  ", StringComparison.Ordinal) ? new FontFamily("Menlo, monospace") : FontFamily.Default,
                });
            caption.IsVisible = lines.Length > 0;
            Settle();
            Canvas.SetLeft(caption, (Window.Width - caption.Bounds.Width) / 2);
            Canvas.SetTop(caption, Window.Height - caption.Bounds.Height - 28);
        }

        /// <summary>The note page scrolls down by <paramref name="by"/> over <paramref name="seconds"/>.</summary>
        public void Scroll(double by, double seconds)
        {
            var scroll = Window.GetVisualDescendants().OfType<ScrollViewer>().First(s => s.Name == "NoteScroll");
            double from = scroll.Offset.Y;
            int n = Math.Max(1, (int)(seconds * Fps));
            for (int i = 1; i <= n; i++)
            {
                double t = i / (double)n, eased = t * t * (3 - 2 * t);
                scroll.Offset = new Vector(0, from + by * eased);
                Frame();
            }
        }
    }

    static readonly (string Name, string Hue, int Count)[] Classes = [("BIO 110", "Pink", 4), ("CHEM 121", "Teal", 3), ("MATH 221", "Blue", 4), ("PSYC 101", "Violet", 2)];

    /// <summary>A free student's library (notes switched off: no AI on the library's computer), open at a lecture with
    /// no notes, only what the student typed during it, and the offer of the Claude app and any free chat.</summary>
    static (LibraryModel Page, FreeAiModel Free, FakeAiLibrary Ai) Library(string cls, string id, string title, string when, string typed)
    {
        IBrush Dot(string hue) => Skin.ClassDot(Array.IndexOf(Skin.ClassHues, hue));
        var page = new LibraryModel { Status = "Library on this Mac", DrawChrome = true, OnAddClass = () => { } };
        foreach (var (name, hue, count) in Classes) page.Classes.Add(new ClassItem { Name = name, Dot = Dot(hue), Count = count, Selected = name == cls });
        var ai = new FakeAiLibrary
        {
            Overview = new AiOverview([], Notes: "ollama", Ask: "ollama", Fallback: false, Problems: []),
            OnRewrite = lecture => new RewriteInfo(lecture, "none") { Current = new NotesVersion("", "", "") },
        };
        page.Note = new NoteModel
        {
            Id = id, ClassName = cls, Dot = Dot(Classes.First(c => c.Name == cls).Hue), Noun = Nouns.Lecture, When = when, Title = title,
            Markdown = TypedNotes.AllMine(typed), Mine = true, Pending = NoteModel.NotesAreOff(Nouns.Lecture, transcript: true), NotesOff = true,
        }.Titled();
        var typedNotes = new AiNotesModel(ai);
        typedNotes.Load(id, TypedNotes.AllMine(typed), "", "", typed: true).GetAwaiter().GetResult();
        page.Notes = typedNotes;
        var free = new FreeAiModel(ai, id) { Offer = true };
        page.FreeAi = free;
        return (page, free, ai);
    }

    /// <summary>The page once the library has the notes: as the app shows a lecture read again.</summary>
    static void Written(LibraryModel page, string notes, string writer, string updated)
    {
        var was = page.Note!;
        page.Note = new NoteModel
        {
            Id = was.Id, ClassName = was.ClassName, Dot = was.Dot, Noun = was.Noun, When = was.When, Title = was.Title, Markdown = notes, NotesOff = true,
        }.Titled();
        var ai = new FakeAiLibrary
        {
            Overview = new AiOverview([], Notes: "ollama", Ask: "ollama", Fallback: false, Problems: []),
            OnRewrite = lecture => new RewriteInfo(lecture, "none") { Current = new NotesVersion(notes, Engines.WhoWrote(writer), updated) },
        };
        var model = new AiNotesModel(ai);
        model.Load(was.Id, notes, writer, updated).GetAwaiter().GetResult();
        page.Notes = model;
        if (page.FreeAi is { } free)
        {
            free.Offer = false;
            free.HasNotes = true;
            free.Say = null;
        }
    }

    static Control View(LibraryModel page, SkinKind skin) => skin == SkinKind.Mac ? new MacLibrary { DataContext = page } : new WinLibrary { DataContext = page };

    [AvaloniaFact]
    public void The_Claude_app_writes_a_lectures_notes_and_they_appear()
    {
        if (!On) return;
        string notes = File.ReadAllText(Run("run1/notes.md")).TrimEnd();
        string typed = File.ReadAllText(Run("run1/typed.md")).TrimEnd();
        var (page, _, _) = Library("BIO 110", "rec-20261012-093012-a41c07", "The Cardiac Cycle: Phases, Valves, and Cardiac Output",
            "Monday 12 October at 9:30 AM, 50 min", typed);
        var f = new Film("free-claude-app", () => View(page, SkinKind.Mac), SkinKind.Mac, ThemeVariant.Light, 1280, 820);
        f.NoPointer();
        f.Hold(1.2);
        f.Caption("No paid AI, notes switched off: this lecture has only what Sam typed.");
        f.Hold(2.0);
        f.Caption("In the Claude app: “write notes for my last BIO 110 lecture and save them”", "real Claude Code standing in for the Claude app, connected only to this library");
        f.Hold(2.4);
        // The calls it really made, in order (run1/stream.jsonl), and what save_notes answered.
        string[] calls =
        [
            "  list_lectures(class_name: \"BIO 110\", limit: 1)",
            "  notes_brief(rec-20261012-093012-a41c07)",
            "  get_transcript(rec-20261012-093012-a41c07)",
            "  get_lecture(rec-20261012-093012-a41c07)",
            "  save_notes(rec-20261012-093012-a41c07, markdown: 4,033 characters)",
            "  → Saved. The notes are on the lecture's page in Syft Notes now, written by Claude.",
        ];
        for (int i = 1; i <= calls.Length; i++)
        {
            f.Caption(["Claude, through Syft Notes' connection:", .. calls[..i]]);
            f.Hold(0.7);
        }
        f.Hold(1.0);
        f.Caption("Back in Syft Notes, the lecture's page is read again:");
        f.Hold(1.0);
        Written(page, notes, "Claude Code", "2026-10-21T16:56:00-07:00");
        DiagramsReady.Wait(f.Window);
        f.Hold(1.0);
        f.Caption();
        f.Hold(1.6);
        f.Scroll(560, 3.0);
        f.Hold(1.6);
        f.Scroll(-560, 1.0);
        f.Hold(0.8);
        f.Window.Close();
    }

    [AvaloniaFact]
    public void Copy_for_your_AI_then_paste_its_notes()
    {
        if (!On) return;
        string copy = File.ReadAllText(Run("run2/copy.txt"));
        string answer = File.ReadAllText(Run("run2/answer.md"));
        string saved = File.ReadAllText(Run("run2/saved.md")).TrimEnd();
        string typed = File.ReadAllText(Run("run2/typed.md")).TrimEnd();
        var (page, free, ai) = Library("MATH 221", "rec-20261012-130011-d72a40", "Curve Sketching with First and Second Derivatives",
            "Monday 12 October at 1:00 PM, 51 min", typed);
        ai.OnNotesBrief = _ => new NotesBrief("", [copy]);
        ai.OnSaveNotes = (lecture, _, _, _) => new RewriteInfo(lecture, "none") { Current = new NotesVersion(saved, "your AI", "2026-10-21T16:57:34-07:00") };
        string? copied = null;
        free.Copy = text =>
        {
            copied = text;
            return Task.CompletedTask;
        };
        free.Saved += () => Written(page, saved, Engines.PastedWriter, "2026-10-21T16:57:34-07:00");
        Control view = null!;
        var f = new Film("free-copy-paste", () => view = View(page, SkinKind.Mac), SkinKind.Mac, ThemeVariant.Light, 1280, 820);
        f.Hold(1.0);
        f.Glide(view.Part<Button>("CopyForAi"), steps: 12);
        f.Click();
        f.Hold(1.8);
        if (copied != copy) throw new InvalidOperationException("Copy for your AI didn't copy the library's request");
        f.Caption($"Pasted into a chat: the library's own request and the transcript ({copy.Length:N0} characters)",
            "real Claude Code standing in for a free chat, with no tools: it wrote the notes from that text alone");
        f.Hold(3.2);
        f.Caption();
        f.Glide(view.Part<Button>("PasteNotes"), steps: 10);
        f.Click();
        f.Hold(0.6);
        f.Glide(view.Part<TextBox>("PasteBox"), 0.3, 0.3, steps: 10);
        f.Click();
        free.PasteText = answer;
        f.Keys("⌘V");
        f.Hold(1.6);
        f.Scroll(240, 0.9);
        f.Glide(view.Part<Button>("SavePaste"), steps: 12);
        f.Click();
        if (free.Pasting) throw new InvalidOperationException("Save notes wasn't pressed");
        DiagramsReady.Wait(f.Window);
        f.NoPointer();
        f.Scroll(-240, 0.6);
        f.Hold(1.8);
        f.Scroll(420, 2.2);
        f.Hold(1.6);
        f.Window.Close();
    }

    /// <summary>Version control on a real library (a throwaway one on this computer): the Claude app's notes (what real
    /// Claude Code saved in run 1) replace the student's, through the connection's own save; Undo puts theirs back;
    /// History lists every version; and an older one is restored, as a new version.</summary>
    [AvaloniaFact]
    public async Task Undo_and_history_after_an_AI_app_replaces_notes()
    {
        if (!On) return;
        string claudes = File.ReadAllText(Run("run1/notes.md")).TrimEnd();
        await using var rig = await LibraryRig.StartAsync();
        const string Id = "rec-20261012-093012-a41c07";
        const string Own = "## Summary\nThe cardiac cycle: five phases, always in the same order.\n\n## Key points\n- Atrial systole, then isovolumetric contraction.\n- Ejection, relaxation, filling.\n- S1 is the AV valves closing, S2 the semilunar valves.";
        rig.AddLecture(new Meeting(Id) { Title = "The Cardiac Cycle: Phases, Valves, and Cardiac Output", Date = "2026-10-12T09:30:12-07:00", Transcript = "[00:04] Today: the cardiac cycle." },
            "BIO 110", Own, summaryModel: "qwen3:8b");
        var ai = new AiRemote(rig.Url, LibraryRig.Password);
        await ai.EditNotesAsync(Id, Own + "\n- **CO = HR × SV**, about 5 L/min at rest.", basedOn: null);
        string said = await ClaudeTools.SaveNotesAsync(new RemoteLibrary(rig.Url, LibraryRig.Password), Id, claudes, replace: true, "Claude (in the Claude app)");
        if (!said.StartsWith("Saved.", StringComparison.Ordinal)) throw new InvalidOperationException(said);

        var (page, _, _) = Library("BIO 110", Id, "The Cardiac Cycle: Phases, Valves, and Cardiac Output", "Monday 12 October at 9:30 AM, 50 min", "");
        page.FreeAi = null;
        page.Note = new NoteModel
        {
            Id = Id, ClassName = "BIO 110", Dot = page.Note!.Dot, Noun = Nouns.Lecture, When = page.Note.When, Title = page.Note.Title, Markdown = claudes,
        }.Titled();
        var notes = new AiNotesModel(ai);
        await notes.Load(Id, claudes, "Claude (in the Claude app)", DateTime.Now.ToString("o"));
        page.Notes = notes;
        Control view = null!;
        var f = new Film("free-version-history", () => view = View(page, SkinKind.Mac), SkinKind.Mac, ThemeVariant.Light, 1280, 820);
        f.Caption("The Claude app replaced Sam's notes", "real Claude Code's notes from run 1, saved through the connection");
        f.Hold(2.4);
        f.Caption();
        f.Glide(view.Part<Button>("UndoButton"), steps: 12);
        f.Click();
        await Settled(f, () => notes.Change?.Kind == NotesChangeKind.Undo);
        f.Hold(1.8);
        f.Glide(view.Part<Button>("HistoryButton"), steps: 12);
        f.Click();
        await Settled(f, () => notes.HistoryOpen && notes.PickedChanges.Count > 0);
        f.Hold(1.6);
        var oldest = view.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("version-row") && b.IsEffectivelyVisible).Last();
        f.Glide(oldest, 0.3, 0.5, steps: 12);
        f.Click();
        await Settled(f, () => notes.Picked is { Words: "Written by Ollama" } && notes.PickedChanges.Count > 0);
        f.Hold(2.2);
        f.Glide(view.Part<Button>("RestoreVersion"), steps: 12);
        f.Click();
        await Settled(f, () => !notes.HistoryOpen && notes.Change?.Kind == NotesChangeKind.Restore);
        f.NoPointer();
        f.Hold(2.4);
        f.Window.Close();
    }

    /// <summary>Lets the library answer (a real one, over HTTP) until <paramref name="done"/>, a frame at a time.</summary>
    static async Task Settled(Film f, Func<bool> done)
    {
        for (int i = 0; i < 100 && !done(); i++)
        {
            await Task.Delay(30);
            f.Settle();
        }
        if (!done()) throw new InvalidOperationException("the library didn't answer as expected");
        DiagramsReady.Wait(f.Window);
    }

    /// <summary>Stills of each piece in both looks, light and dark, and in colour themes with a pale accent and a dark
    /// one: the offer over a lecture with no notes, a long lecture's parts, the paste box over notes it would replace,
    /// the notes' ⋯ menu, and Settings → AI apps' switch.</summary>
    [AvaloniaFact]
    public void Free_AI_in_every_look_and_theme()
    {
        if (!On) return;
        var named = ColourThemes.All.ToDictionary(t => t.Name);
        string typed = "## Cardiac cycle\n- 5 phases, **always the same order** (midterm!)\n- S1 = lub, S2 = dub";
        foreach (var (skin, variant, theme) in new[]
                 {
                     (SkinKind.Mac, ThemeVariant.Light, "Lagoon"), (SkinKind.Mac, ThemeVariant.Dark, "Lagoon"), (SkinKind.Mac, ThemeVariant.Light, "Mint"),
                     (SkinKind.Win, ThemeVariant.Light, "Highlighter"), (SkinKind.Win, ThemeVariant.Dark, "Midnight"), (SkinKind.Win, ThemeVariant.Dark, "Lagoon"),
                 })
        {
            string tag = $"{skin.ToString().ToLowerInvariant()}-{variant.Key.ToString()!.ToLowerInvariant()}-{theme.ToLowerInvariant()}";

            var (page, free, _) = Library("BIO 110", "n1", "The Cardiac Cycle: Phases, Valves, and Cardiac Output", "Monday 12 October at 9:30 AM, 50 min", typed);
            var f = new Film($"still-offer-{tag}", () => View(page, skin), skin, variant, 1280, 820, named[theme]);
            f.NoPointer();
            f.Frame();
            free.Parts.Add(new CopyPart(1, "") { Copied = true });
            free.Parts.Add(new CopyPart(2, ""));
            free.Parts.Add(new CopyPart(3, ""));
            free.Say = "Part 1 copied. Paste it into the chat, then copy part 2 when it answers.";
            f.Frame();
            free.HasNotes = true;
            free.Paste();
            free.PasteText = "Sure! Here are your study notes:\n\n## Summary\nThe cardiac cycle has five phases...";
            f.Frame();
            f.Window.Close();

            // Right after an AI app replaced the notes: the Undo line; then History, an older version picked.
            string now = DateTime.Now.ToString("o");
            const string Claudes = "## Summary\nThe cardiac cycle has five phases.\n\n## Key points\n- Atrial systole pushes the last blood in.\n- S1 is the AV valves closing.";
            var versioned = new FakeAiLibrary
            {
                Overview = new AiOverview([], Notes: "ollama", Ask: "ollama", Fallback: false, Problems: []),
                OnRewrite = id => new RewriteInfo(id, "none")
                {
                    Current = new NotesVersion(Claudes, "Claude", now), Change = new NotesChange("4", NotesChangeKind.App, "Claude", now) { CanUndo = true },
                },
                OnNotesVersions = _ =>
                [
                    new NotesKept("4", NotesChangeKind.App, "Claude", now) { Current = true }, new NotesKept("3", NotesChangeKind.Edit, "", now),
                    new NotesKept("2", NotesChangeKind.Paste, "your AI", "2026-10-20T19:05:00"), new NotesKept("1", NotesChangeKind.Written, "Ollama", "2026-10-20T09:30:00"),
                ],
                OnNotesVersion = (_, v) => new NotesKept(v, NotesChangeKind.Written, "Ollama", now)
                {
                    Markdown = "## Summary\nThe cardiac cycle: five phases.\n\n## Key points\n- Atrial systole pushes the last blood in.\n- S1 = lub, S2 = dub.",
                },
            };
            var (vpage, _, _) = Library("BIO 110", "n1", "The Cardiac Cycle: Phases, Valves, and Cardiac Output", "Monday 12 October at 9:30 AM, 50 min", "");
            vpage.FreeAi = null;
            var vnotes = new AiNotesModel(versioned);
            vnotes.Load("n1", Claudes, "Claude (in the Claude app)", now).GetAwaiter().GetResult();
            vpage.Notes = vnotes;
            var h = new Film($"still-history-{tag}", () => View(vpage, skin), skin, variant, 1280, 820, named[theme]);
            h.NoPointer();
            h.Frame();
            vnotes.OpenHistory().GetAwaiter().GetResult();
            vnotes.Pick(vnotes.Versions[^1]).GetAwaiter().GetResult();
            h.Frame();
            h.Window.Close();

            // The notes' ⋯ menu, drawn where it opens (a real one is a window of its own).
            var notes = AiDemo.LectureNotes("## Summary\nNotes.", "Claude");
            notes.OnCopyForAi = () => { };
            notes.OnPasteNotes = () => { };
            Control Menu()
            {
                Control menu = skin == SkinKind.Mac ? new MacAiEngineMenu { DataContext = notes.Menu } : new WinAiEngineMenu { DataContext = notes.Menu };
                menu.HorizontalAlignment = HorizontalAlignment.Center;
                menu.VerticalAlignment = VerticalAlignment.Center;
                return new Border { Background = variant == ThemeVariant.Dark ? Brushes.Black : Brushes.LightGray, Child = menu };
            }
            var m = new Film($"still-menu-{tag}", Menu, skin, variant, 420, 560, named[theme]);
            m.NoPointer();
            m.Frame();
            m.Window.Close();

            // Settings → AI apps, at its switch.
            var access = new AiAccessModel(new FakeAiLibrary { Access = new ToolAccessInfo(true, new ReadingScopes(), []) });
            access.Apps.Add(new AiAppRow { Id = "claude-desktop", Name = "Claude", First = true, Status = "Connected · started 9:12", Ok = true });
            var s = new Film($"still-settings-{tag}", () => skin == SkinKind.Mac ? new MacAiAccess { DataContext = access } : new WinAiAccess { DataContext = access },
                skin, variant, 760, 1000, named[theme]);
            s.NoPointer();
            var scroll = s.Window.GetVisualDescendants().OfType<ScrollViewer>().First();
            var row = s.Window.GetVisualDescendants().OfType<Control>().First(c => c.Name == "SaveNotesToggle");
            scroll.Offset = new Vector(0, Math.Max(0, row.TranslatePoint(new Point(0, 0), scroll)!.Value.Y - 360));
            s.Frame();
            s.Window.Close();
        }
    }
}
