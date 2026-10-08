using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StudyStash.App.Controls;
using StudyStash.App.Controls.Rich;
using StudyStash.App.Services;
using StudyStash.App.ViewModels;
using StudyStash.App.Views;
using StudyStash.Core.Rich;

namespace StudyStash.App.Tests;

/// <summary>
/// What VoiceOver and Narrator say for the controls a student reaches first: a card, a switch, a recorder button each
/// says what it is, the words on it, not the name of the control that happens to be inside it ("Avalonia.Controls.Panel"),
/// and never the same for two different ones. Both looks, Mac and Windows.
/// </summary>
public class AccessibleNamesTests
{
    static string Said(Control c) => ControlAutomationPeer.CreatePeerForElement(c).GetName() ?? "";

    /// <summary>Nothing, or a control's type ("Avalonia.Controls.Panel"): what a button says when its content isn't text.</summary>
    static bool Nameless(string said) => said.Length == 0 || Regex.IsMatch(said, @"^[A-Za-z]+(\.[A-Za-z]+)+$");

    static List<T> Shown<T>(Visual root) where T : Control => [.. root.GetVisualDescendants().OfType<T>().Where(c => c.IsEffectivelyVisible && c.Bounds.Width > 0)];

    static void AllNamed(IEnumerable<Control> controls, string where)
    {
        var bad = controls.Where(c => Nameless(Said(c))).Select(c => $"{c.GetType().Name} {c.Name} ({string.Join(".", c.Classes)})").ToList();
        Assert.True(bad.Count == 0, $"{where}: nothing to say for {string.Join(", ", bad)}");
    }

    static async Task<(SettingsModel Model, AppHost Host, TempHome Home)> SettingsAsync()
    {
        var home = new TempHome();
        var host = new AppHost(home.Path);
        var library = new FakeLibrarySettings();
        var model = SettingsModel.Make(host, ai: AiDemo.DemoLibrary(), canvas: CanvasFixtures.Context(CanvasShots.ConnectedLibrary()), library: () => library.Call);
        await model.Engines.Load();
        await model.Access.Load();
        await model.Canvas.LoadAsync();
        return (model, host, home);
    }

    [AvaloniaTheory]
    [InlineData(SkinKind.Mac)]
    [InlineData(SkinKind.Win)]
    public async Task Every_switch_in_settings_says_what_it_switches_and_the_two_written_down_cards_say_which_they_are(SkinKind skin)
    {
        try
        {
            ((App)Application.Current!).UseSkin(skin);
            var (model, host, home) = await SettingsAsync();
            using (home)
            using (host)
            using (model)
            {
                var view = new SettingsView { DataContext = model };
                var window = new Window { Width = 900, Height = 1400, Content = view };
                window.Show();
                foreach (string section in new[] { "General", "Recording", "Library", "Notes", "AI", "Access", "Canvas", "Folders" })
                {
                    model.Section = section;
                    Dispatcher.UIThread.RunJobs();
                    window.UpdateLayout();
                    AllNamed(Shown<ToggleButton>(view), $"Settings, {section}");
                    AllNamed(Shown<Button>(view).Where(b => b.Classes.Contains("nav")), $"Settings, the sections beside {section}");
                }
                model.Section = "Recording";
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                string asYouRecord = Said(view.FindControl<Button>("WrittenAsYouRecord")!), afterClass = Said(view.FindControl<Button>("WrittenAfterClass")!);
                Assert.Equal(("As you record", "After class"), (asYouRecord, afterClass));
                var models = Shown<Button>(view).Where(b => b.Classes.Contains("card") && b.DataContext is ModelChoice).ToList();
                Assert.NotEmpty(models);
                AllNamed(models, "Settings, Recording, the models");
                window.Close();
            }
        }
        finally
        {
            ((App)Application.Current!).UseSkin(SkinKind.Mac);
        }
    }

    [AvaloniaTheory]
    [InlineData(SkinKind.Mac)]
    [InlineData(SkinKind.Win)]
    public async Task Setups_cards_and_switch_say_what_they_are(SkinKind skin)
    {
        try
        {
            ((App)Application.Current!).UseSkin(skin);
            var m = SetupModel.For(skin, AppRole.Both);
            m.Ai = AiDemo.Setup();
            m.Canvas = await CanvasShots.SetupStepAsync("found");
            var view = skin == SkinKind.Mac ? (Control)new MacSetup { DataContext = m } : new WinSetup { DataContext = m };
            var window = new Window { Width = 900, Height = 900, Content = view };
            window.Show();
            foreach (var step in m.Steps.Select(s => s.Step).ToList())
            {
                m.Go(step);
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                var cards = Shown<Button>(view).Where(b => b.Classes.Contains("choice") || b.Classes.Contains("ai-choice")).Cast<Control>();
                AllNamed(cards.Concat(Shown<ToggleButton>(view)), $"Setup, {step}");
            }
            m.Go(m.Steps.First().Step);
            Dispatcher.UIThread.RunJobs();
            var welcome = Shown<Button>(view).Where(b => b.Classes.Contains("choice")).Select(Said).ToList();
            Assert.Equal(["Just this computer", "This is my laptop", "This is my library"], welcome);
            window.Close();
        }
        finally
        {
            ((App)Application.Current!).UseSkin(SkinKind.Mac);
        }
    }

    [AvaloniaTheory]
    [InlineData(SkinKind.Mac)]
    [InlineData(SkinKind.Win)]
    public void The_guided_setups_picks_and_steps_say_what_they_are(SkinKind skin)
    {
        try
        {
            ((App)Application.Current!).UseSkin(skin);
            var g = GuidedSetupShots.Guided(skin);
            var view = skin == SkinKind.Mac ? (Control)new MacGuidedSetup { DataContext = g } : new WinGuidedSetup { DataContext = g };
            var window = new Window { Width = 900, Height = 800, Content = view };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Assert.Equal(("Claude", "ChatGPT", "I don't have a paid plan"),
                (Said(view.FindControl<Button>("PickClaude")!), Said(view.FindControl<Button>("PickCodex")!), Said(view.FindControl<Button>("PickFree")!)));
            // In the chat, the checklist beside it.
            foreach (string title in new[] { "Sign in", "Pick a model", "Add your classes" }) g.Checklist.Add(new ChecklistRow(title) { Title = title });
            g.Screen = GuidedScreen.Chat;
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var steps = Shown<Button>(view).Where(b => b.Classes.Contains("item")).ToList();
            Assert.NotEmpty(steps);
            AllNamed(steps, "Guided setup, the steps");
            window.Close();
        }
        finally
        {
            ((App)Application.Current!).UseSkin(SkinKind.Mac);
        }
    }

    [AvaloniaTheory]
    [InlineData(SkinKind.Mac)]
    [InlineData(SkinKind.Win)]
    public void The_recorders_buttons_say_make_it_small_pause_or_resume_and_stop(SkinKind skin)
    {
        try
        {
            ((App)Application.Current!).UseSkin(skin);
            foreach (bool paused in new[] { false, true })
            {
                var model = Demo.Recorder(paused: paused, expanded: true);
                Control view = skin == SkinKind.Mac ? new MacRecorder { DataContext = model } : new WinRecorder { DataContext = model };
                var window = new Window { Width = 500, Height = 600, RequestedThemeVariant = ThemeVariant.Light, Content = view };
                window.Show();
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                var icons = Shown<Button>(view).Where(b => b.Content is Icon).ToList();
                Assert.Equal(["Make it small", paused ? "Resume" : "Pause", "Stop"], icons.Take(3).Select(Said));
                AllNamed(icons, "the recorder");

                // Small: Stop shows when the pointer is over it, and says so.
                model.Expanded = false;
                model.Hovered = true;
                Dispatcher.UIThread.RunJobs();
                Assert.Contains("Stop", Shown<Button>(view).Select(Said));
                window.Close();
            }
        }
        finally
        {
            ((App)Application.Current!).UseSkin(SkinKind.Mac);
        }
    }

    [AvaloniaFact]
    public void A_plots_play_button_says_pause_while_it_plays_in_its_name_and_its_tooltip()
    {
        var note = new NoteView { Markdown = "## Plot\n\n```plot\n" + PlotDesign.Binomial + "\n```\n\nAfter it.", Width = 620 };
        var window = new Window { Width = 700, Height = 1400, Content = note };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var view = Assert.Single(note.GetLogicalDescendants().OfType<PlotView>());
        var play = view.GetVisualDescendants().OfType<Button>().First(b => Said(b).StartsWith("Play ", StringComparison.Ordinal));
        string before = Said(play);
        Assert.Equal(before, ToolTip.GetTip(play));

        play.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.True(view.Playing);
        Assert.StartsWith("Pause ", Said(play));
        Assert.Equal(Said(play), ToolTip.GetTip(play));

        play.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.False(view.Playing);
        Assert.Equal(before, Said(play));
        window.Close();
    }

    [AvaloniaFact]
    public void A_formula_in_a_sentence_reads_in_words_not_as_an_object_replacement_character()
    {
        var note = new NoteView { Markdown = "The slope is $\\frac{a}{b}$ and the energy is $E = mc^2$ here." };
        var window = new Window { Width = 700, Height = 400, Content = note };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var formulas = note.GetLogicalDescendants().OfType<InlineUIContainer>().Select(c => c.Child).OfType<MathView>().ToList();
        Assert.Equal(["a/b", "E = mc²"], formulas.Select(Said));

        var paragraph = note.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Inlines?.Any(i => i is InlineUIContainer) == true);
        string said = Said(paragraph);
        Assert.DoesNotContain('￼', said);
        Assert.Equal("The slope is a/b and the energy is E = mc² here.", said);
        window.Close();
    }
}
