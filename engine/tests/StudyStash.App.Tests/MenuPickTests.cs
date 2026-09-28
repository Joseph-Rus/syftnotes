using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StudyStash.App.ViewModels;
using StudyStash.App.Views;
using StudyStash.Core.Ai;

namespace StudyStash.App.Tests;

/// <summary>
/// Every popup menu, clicked the way a student clicks it (a pointer press and release on the item, in the popup's
/// own window): the pick takes effect and shows as picked. The dropdown's class picker, the ask bar's
/// "Answer with" and scope and "Rewrite notes with" once did nothing; the causes were a menu drawn with no template (an
/// empty box) and items whose command sat on a MenuItem nested inside the real one.
/// </summary>
public class MenuPickTests
{
    const string Lecture = "lec-1";

    static Window Host(Control content, SkinKind skin)
    {
        ((App)Application.Current!).UseSkin(skin);
        var w = new Window { Width = 1000, Height = 800, RequestedThemeVariant = ThemeVariant.Light, Content = content };
        w.Show();
        Dispatcher.UIThread.RunJobs();
        return w;
    }

    /// <summary>A left click in the middle of <paramref name="target"/>, sent to whichever window (or popup) it's in.</summary>
    static void Click(Visual target)
    {
        var top = TopLevel.GetTopLevel(target) ?? throw new InvalidOperationException("not on screen");
        var at = target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), top)!.Value;
        top.MouseMove(at);
        top.MouseDown(at, MouseButton.Left);
        top.MouseUp(at, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Clicks <paramref name="button"/> and hands back what its flyout shows.</summary>
    static Control OpenFlyout(Button button)
    {
        Click(button);
        var flyout = Assert.IsAssignableFrom<PopupFlyoutBase>(button.Flyout);
        Assert.True(flyout.IsOpen, "the menu didn't open");
        var popup = (Popup)typeof(PopupFlyoutBase).GetProperty("Popup", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public
            | System.Reflection.BindingFlags.Instance)!.GetValue(flyout)!;
        Dispatcher.UIThread.RunJobs();
        var shown = Assert.IsAssignableFrom<Control>(popup.Child);
        Assert.True(shown.Bounds.Height > 20, $"the menu is an empty box ({shown.Bounds})");
        return shown;
    }

    /// <summary>The menu item in <paramref name="menu"/> that reads <paramref name="text"/>.</summary>
    static MenuItem Item(Control menu, string text)
    {
        var label = menu.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault(t => t.Text == text && t.IsEffectivelyVisible)
            ?? throw new InvalidOperationException($"no \"{text}\" in the menu");
        var items = label.GetVisualAncestors().OfType<MenuItem>().ToList();
        Assert.Single(items); // one item, not an item inside another item's header
        return items[0];
    }

    static bool Checked(MenuItem item) => item.GetVisualDescendants().OfType<Controls.Icon>().Any(i => i.Glyph == "check" && i.IsEffectivelyVisible);

    [AvaloniaTheory]
    [InlineData(SkinKind.Mac)]
    [InlineData(SkinKind.Win)]
    public void The_ask_bars_scope_menu_picks_the_scope_you_click(SkinKind skin)
    {
        var m = AiDemo.AskIdle();
        Control bar = skin == SkinKind.Mac ? new MacAiAskBar { DataContext = m } : new WinAiAskBar { DataContext = m };
        var w = Host(bar, skin);
        try
        {
            var chip = bar.GetVisualDescendants().OfType<Button>().First(b => b.Flyout is MenuFlyout);
            var menu = OpenFlyout(chip);
            Assert.True(Checked(Item(menu, "This lecture")));
            Click(Item(menu, "This class"));
            Assert.Equal("class", m.Scope);
            Assert.Contains(chip.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "This class");
            Assert.False(chip.Flyout!.IsOpen);

            menu = OpenFlyout(chip);
            Assert.True(Checked(Item(menu, "This class")));
            Assert.False(Checked(Item(menu, "This lecture")));
        }
        finally
        {
            w.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(SkinKind.Mac)]
    [InlineData(SkinKind.Win)]
    public void Answer_with_picks_the_engine_you_click_and_closes(SkinKind skin)
    {
        var m = AiDemo.AskIdle();
        Control bar = skin == SkinKind.Mac ? new MacAiAskBar { DataContext = m } : new WinAiAskBar { DataContext = m };
        var w = Host(bar, skin);
        try
        {
            var chip = bar.FindControl<Button>("EngineChip")!;
            Assert.Equal("claude", m.Engine);
            var menu = OpenFlyout(chip);
            var ollama = menu.GetVisualDescendants().OfType<Button>().First(b => b.DataContext is EngineMenuItem { Id: "ollama" });
            Click(ollama);
            Assert.Equal("ollama", m.Engine);
            Assert.Equal("Ollama", m.EngineName);
            Assert.False(chip.Flyout!.IsOpen);
            Assert.True(m.Menu.Items.Single(i => i.Id == "ollama").Selected);
        }
        finally
        {
            w.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(SkinKind.Mac)]
    [InlineData(SkinKind.Win)]
    public async Task Rewrite_notes_with_starts_on_the_engine_you_click_and_closes(SkinKind skin)
    {
        var lib = new FakeAiLibrary { Overview = AiDemo.Overview(), OnRewrite = id => new RewriteInfo(id, "none") };
        var m = new AiNotesModel(lib);
        await m.Load(Lecture, "# Notes", "Ollama", "2026-09-22T10:00:00Z");
        Control view = skin == SkinKind.Mac ? new MacAiNotes { DataContext = m } : new WinAiNotes { DataContext = m };
        var w = Host(view, skin);
        try
        {
            var button = view.FindControl<Button>("RewriteButton")!;
            var menu = OpenFlyout(button);
            Assert.True(m.MenuOpen);
            var claude = menu.GetVisualDescendants().OfType<Button>().First(b => b.DataContext is EngineMenuItem { Id: "claude" });
            Click(claude);
            Assert.Contains($"rewrite-start:{Lecture}:claude", lib.Calls);
            Assert.Equal(RewriteState.Rewriting, m.State);
            Assert.False(button.Flyout!.IsOpen);
            Assert.False(m.MenuOpen); // the notes aren't left dimmed
            Assert.Equal("claude", m.Engine);
        }
        finally
        {
            w.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(SkinKind.Mac)]
    [InlineData(SkinKind.Win)]
    public async Task Settings_AI_selects_pick_what_you_click(SkinKind skin)
    {
        var lib = new FakeAiLibrary { Overview = AiDemo.Overview() };
        var m = new AiEnginesModel(lib);
        await m.Load();
        Control view = skin == SkinKind.Mac ? new MacAiEngines { DataContext = m } : new WinAiEngines { DataContext = m };
        var w = Host(new ScrollViewer { Content = view }, skin);
        try
        {
            var notes = view.GetVisualDescendants().OfType<Button>().First(b => b.Flyout is MenuFlyout { ItemsSource: var s } && ReferenceEquals(s, m.NotesChoices));
            var menu = OpenFlyout(notes);
            Assert.True(Checked(Item(menu, "Ollama")));
            Click(Item(menu, "Claude Code"));
            Assert.Equal("claude", m.SelectedNotes);
            Assert.Equal("Claude Code", m.SelectedNotesName);
            Assert.Contains(lib.DefaultsCalls, c => c.Notes == "claude");
            Assert.True(m.NotesChoices.Single(c => c.Id == "claude").Current);
            Assert.False(m.NotesChoices.Single(c => c.Id == "ollama").Current);
        }
        finally
        {
            w.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(SkinKind.Mac)]
    [InlineData(SkinKind.Win)]
    public async Task Setup_AI_step_picks_who_answers(SkinKind skin)
    {
        var m = new AiSetupModel(new FakeAiLibrary { Overview = AiDemo.Overview() });
        await m.Load();
        Control view = skin == SkinKind.Mac ? new MacAiSetup { DataContext = m } : new WinAiSetup { DataContext = m };
        var w = Host(view, skin);
        try
        {
            var select = view.GetVisualDescendants().OfType<Button>().First(b => b.Flyout is MenuFlyout);
            Click(Item(OpenFlyout(select), "Claude Code"));
            Assert.Equal("claude", m.SelectedAsk);
            Assert.True(m.AskChoices.Single(c => c.Id == "claude").Current);
        }
        finally
        {
            w.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(SkinKind.Mac)]
    [InlineData(SkinKind.Win)]
    public void The_dropdowns_class_picker_does_what_you_pick_and_checks_it(SkinKind skin)
    {
        var anchor = new Button { Content = "v", Width = 32, Height = 32, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        var w = Host(anchor, skin);
        try
        {
            (string, int)[] classes = [("CS 101", 0), ("BIO 110", 1)];
            string? picked = "unset";
            ContextMenu Open(string? chosen)
            {
                var menu = ClassPicker.Build(classes, chosen, name => picked = name);
                menu.Open(anchor);
                Dispatcher.UIThread.RunJobs();
                Assert.True(menu.IsOpen);
                return menu;
            }

            // Following the timetable: that's checked, and every row can be picked.
            var menu = Open(null);
            Assert.True(Checked(Item(menu, "Follow my timetable")));
            Assert.All(menu.GetLogicalDescendants().OfType<MenuItem>(), i => Assert.True(i.IsEnabled));
            Click(Item(menu, "Let the library sort it"));
            Assert.Equal("", picked);
            Assert.False(menu.IsOpen);

            menu = Open("");
            Assert.True(Checked(Item(menu, "Let the library sort it")));
            Assert.False(Checked(Item(menu, "Follow my timetable")));
            Click(Item(menu, "BIO 110"));
            Assert.Equal("BIO 110", picked);

            menu = Open("BIO 110");
            Assert.True(Checked(Item(menu, "BIO 110")));
            Click(Item(menu, "Follow my timetable"));
            Assert.Null(picked);

            // Every label starts at the same place, with a dot or without.
            menu = Open(null);
            double Left(string text) => Item(menu, text).GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == text).TranslatePoint(default, menu)!.Value.X;
            Assert.Equal(Left("CS 101"), Left("Let the library sort it"), 1);
            Assert.Equal(Left("CS 101"), Left("Follow my timetable"), 1);
            menu.Close();
        }
        finally
        {
            w.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(SkinKind.Mac)]
    [InlineData(SkinKind.Win)]
    public void The_download_menu_offers_the_open_class_and_checks_transcripts_when_theyre_on(SkinKind skin)
    {
        var anchor = new Button { Content = "Export", Width = 32, Height = 32, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        var w = Host(anchor, skin);
        try
        {
            int downloaded = 0, downloadedClass = 0, toggled = 0;
            var menu = DownloadMenu.Build("BIO 110", includeTranscripts: false, () => downloaded++, () => downloadedClass++, () => toggled++);
            menu.Open(anchor);
            Dispatcher.UIThread.RunJobs();
            Assert.True(menu.IsOpen);
            Item(menu, "Download as Markdown…"); // exists
            Item(menu, "Download all of BIO 110…"); // exists, names the open class
            Assert.False(Checked(Item(menu, "Include transcripts")));
            Click(Item(menu, "Download as Markdown…"));
            Assert.Equal(1, downloaded);
            Assert.Equal(0, downloadedClass + toggled);
            menu.Close();

            // No class open (Due, or nothing picked yet): no "Download all of…" row at all.
            var noClass = DownloadMenu.Build(null, includeTranscripts: true, () => { }, () => { }, () => { });
            Assert.DoesNotContain(noClass.Items.OfType<MenuItem>(), i => (i.Header as string)?.StartsWith("Download all of") == true);
            noClass.Open(anchor);
            Dispatcher.UIThread.RunJobs();
            Assert.True(Checked(Item(noClass, "Include transcripts")));
            noClass.Close();
        }
        finally
        {
            w.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(SkinKind.Mac)]
    [InlineData(SkinKind.Win)]
    public void Your_librarys_selects_pick_what_you_click(SkinKind skin)
    {
        ((App)Application.Current!).UseSkin(skin);
        // The select's own item look and menu, from Settings, over a list like the library's sorting choices.
        var settings = new SettingsView();
        string? picked = null;
        var button = new Button
        {
            Content = "Sorting", HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            Flyout = new MenuFlyout
            {
                ItemsSource = new[] { new SettingChoice("qwen3:30b", "qwen3:30b (19 GB)", id => picked = id), new SettingChoice("llama3:8b", "llama3:8b (5 GB)", id => picked = id) },
                ItemContainerTheme = (ControlTheme)settings.Resources["PickItem"]!,
                FlyoutPresenterTheme = (ControlTheme)settings.Resources["SettingsMenu"]!,
            },
        };
        var w = Host(button, skin);
        try
        {
            Click(Item(OpenFlyout(button), "llama3:8b (5 GB)"));
            Assert.Equal("llama3:8b", picked);
        }
        finally
        {
            w.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(SkinKind.Mac)]
    [InlineData(SkinKind.Win)]
    public async Task An_engines_Models_menu_picks_the_model_you_click(SkinKind skin)
    {
        var overview = AiDemo.Overview() with
        {
            Engines = [new EngineInfo("ollama", "Ollama", "ready") { Installed = true, Model = "qwen3:30b",
                Models = [new ModelOption("qwen3:30b", "qwen3:30b (19 GB)"), new ModelOption("llama3:8b", "llama3:8b (5 GB)")] }],
        };
        var lib = new FakeAiLibrary { Overview = overview };
        var m = new AiEnginesModel(lib);
        await m.Load();
        Control view = skin == SkinKind.Mac ? new MacAiEngines { DataContext = m } : new WinAiEngines { DataContext = m };
        var w = Host(new ScrollViewer { Content = view }, skin);
        try
        {
            var options = view.GetVisualDescendants().OfType<Button>().First(b => b.Flyout is MenuFlyout && b.IsEffectivelyVisible && b.DataContext is AiEngineRow);
            var models = Item(OpenFlyout(options), "Models");
            Click(models);
            Assert.True(models.IsSubMenuOpen, "the Models submenu didn't open");
            var row = Assert.IsType<MenuItem>(models.ContainerFromIndex(1));
            Assert.Equal("llama3:8b (5 GB)", row.Header);
            Assert.NotNull(row.Command);
            Click(row);
            Assert.Contains(lib.Calls, c => c.Contains("llama3:8b", StringComparison.Ordinal));
        }
        finally
        {
            w.Close();
        }
    }
}
