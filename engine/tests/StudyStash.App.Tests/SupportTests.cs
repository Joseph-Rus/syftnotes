using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StudyStash.App.Controls;
using StudyStash.App.Services;
using StudyStash.App.ViewModels;
using StudyStash.App.Views;

namespace StudyStash.App.Tests;

/// <summary>"Buy us a coffee": Home's button, and the library window's one gentle ask for a tip, a popup over the
/// window — when it asks, what each answer does, and that the Ko-fi page is only ever caught here, never handed to a
/// real browser.</summary>
public class SupportTests
{
    const string KoFi = "https://ko-fi.com/studystashteam";
    static readonly DateTimeOffset Monday = new(2026, 9, 28, 10, 0, 0, TimeSpan.Zero);

    static List<string> Texts(Control view) =>
        [.. view.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text ?? "")];

    // --- when it asks ----------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(0, false)]
    [InlineData(4, false)]
    [InlineData(5, true)]
    [InlineData(120, true)]
    public void It_asks_once_the_library_holds_five_lectures(int lectures, bool asks) =>
        Assert.Equal(asks, SupportAsk.Due(new AppSettings(), lectures, recording: false, settingUp: false, Monday));

    [Fact]
    public void It_never_asks_while_recording_or_during_setup()
    {
        var settings = new AppSettings();
        Assert.False(SupportAsk.Due(settings, 30, recording: true, settingUp: false, Monday));
        Assert.False(SupportAsk.Due(settings, 30, recording: false, settingUp: true, Monday));
        Assert.True(SupportAsk.Due(settings, 30, recording: false, settingUp: false, Monday));
    }

    [Fact]
    public void Maybe_later_asks_once_more_a_month_on_and_then_never_again()
    {
        var settings = new AppSettings();
        Assert.False(SupportAsk.IsLast(settings));

        SupportAsk.Remember(settings, SupportAnswer.Later, Monday);
        Assert.Equal(Monday, settings.SupportAskLater);
        Assert.False(settings.SupportAskDone);
        Assert.False(SupportAsk.Due(settings, 30, false, false, Monday.AddDays(1)));
        Assert.False(SupportAsk.Due(settings, 30, false, false, Monday.AddDays(30).AddMinutes(-1)));
        Assert.True(SupportAsk.Due(settings, 30, false, false, Monday.AddDays(30)));
        Assert.True(SupportAsk.IsLast(settings));

        // The last ask put off too: that's the end of it.
        SupportAsk.Remember(settings, SupportAnswer.Later, Monday.AddDays(31));
        Assert.True(settings.SupportAskDone);
        Assert.False(SupportAsk.Due(settings, 30, false, false, Monday.AddDays(400)));
    }

    [Fact]
    public void A_clock_set_back_doesnt_bring_the_ask_back_early()
    {
        var settings = new AppSettings { SupportAskLater = Monday };
        Assert.False(SupportAsk.Due(settings, 30, false, false, Monday.AddDays(-40)));
    }

    [Theory]
    [InlineData(SupportAnswer.Tip, false)]
    [InlineData(SupportAnswer.Never, false)]
    [InlineData(SupportAnswer.Tip, true)]
    [InlineData(SupportAnswer.Never, true)]
    public void A_tip_or_dont_ask_again_ends_it_the_first_time_or_the_last(SupportAnswer answer, bool putOffBefore)
    {
        var settings = new AppSettings { SupportAskLater = putOffBefore ? Monday.AddDays(-45) : null };
        SupportAsk.Remember(settings, answer, Monday);
        Assert.True(settings.SupportAskDone);
        Assert.False(SupportAsk.Due(settings, 30, false, false, Monday.AddYears(2)));
    }

    // --- what an answer saves --------------------------------------------------------------------------------------

    [AvaloniaFact]
    public void An_answer_is_saved_and_only_a_tip_opens_the_Ko_fi_page()
    {
        using var home = new TempHome();
        using var host = new AppHost(home.Path, log: _ => { });
        var opened = new List<string>();

        SupportAsk.Answer(host, SupportAnswer.Later, opened.Add, Monday);
        var saved = AppSettings.Load(home.Path);
        Assert.Equal(Monday, saved.SupportAskLater);
        Assert.False(saved.SupportAskDone);
        Assert.Empty(opened);

        SupportAsk.Answer(host, SupportAnswer.Tip, opened.Add, Monday.AddDays(30));
        Assert.True(AppSettings.Load(home.Path).SupportAskDone);
        Assert.Equal([KoFi], opened);
    }

    [Fact]
    public void Settings_saved_before_the_ask_existed_load_as_never_asked()
    {
        using var home = new TempHome();
        File.WriteAllText(home["app.json"], """{ "setup_done": true, "theme": "Plum" }""");
        var settings = AppSettings.Load(home.Path);
        Assert.Null(settings.SupportAskLater);
        Assert.False(settings.SupportAskDone);
        Assert.True(SupportAsk.Due(settings, 5, false, false, Monday));
    }

    // --- the popup -------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(SupportAnswer.Tip)]
    [InlineData(SupportAnswer.Later)]
    [InlineData(SupportAnswer.Never)]
    public void Each_button_answers_the_ask_once_and_it_asks_once_a_session(SupportAnswer answer)
    {
        var answers = new List<SupportAnswer>();
        var library = new LibraryModel { OnSupportAnswer = answers.Add };
        var ask = library.AskForSupport(last: false)!;
        Assert.Same(ask, library.Support);
        Assert.True(ask.CanWait);
        Assert.Null(library.AskForSupport(last: false)); // once a session at most

        var press = answer switch
        {
            SupportAnswer.Tip => ask.TipCommand,
            SupportAnswer.Later => ask.LaterCommand,
            _ => ask.NeverCommand,
        };
        press.Execute(null);
        ask.LaterCommand.Execute(null); // a second press (or Escape after it) answers nothing more

        Assert.Equal([answer], answers);
        Assert.True(ask.Over);
        Assert.Null(library.Support);
    }

    [Fact]
    public void Put_away_for_a_recording_or_setup_it_closes_with_no_answer()
    {
        var answers = new List<SupportAnswer>();
        var library = new LibraryModel { OnSupportAnswer = answers.Add };
        var ask = library.AskForSupport(last: false)!;
        library.Support = null; // what Shell does when a recording starts or setup opens
        Assert.True(ask.Over);
        ask.LaterCommand.Execute(null);
        Assert.Empty(answers); // nothing saved: it may ask another day
    }

    [Fact]
    public void Closing_the_window_on_the_ask_counts_as_maybe_later_and_the_last_time_ends_it()
    {
        var settings = new AppSettings();
        var answers = new List<SupportAnswer>();
        LibraryModel Session() => new() { OnSupportAnswer = a => { answers.Add(a); SupportAsk.Remember(settings, a, Monday); } };
        var library = Session();
        library.WalkAwayFromSupport(); // nothing showing: nothing to answer
        Assert.Empty(answers);

        library.AskForSupport(last: SupportAsk.IsLast(settings));
        library.WalkAwayFromSupport();
        Assert.Equal([SupportAnswer.Later], answers);
        Assert.Null(library.Support);
        Assert.False(settings.SupportAskDone);

        library = Session(); // a month on, the app started again
        library.AskForSupport(last: SupportAsk.IsLast(settings));
        library.WalkAwayFromSupport();
        Assert.True(settings.SupportAskDone);
    }

    /// <summary>The ask pops up over the library window on its dialog layer, both looks: the logo, the question, its two
    /// lines and the three answers, "Maybe later" with the keyboard. Escape is "Maybe later" and closes it. The last
    /// time it asks, there's no "Maybe later".</summary>
    [AvaloniaTheory]
    [InlineData(SkinKind.Mac)]
    [InlineData(SkinKind.Win)]
    public async Task It_pops_up_over_the_library_window_and_escape_is_maybe_later(SkinKind skin)
    {
        ((App)Application.Current!).UseSkin(skin);
        var window = new Window { Width = 1280, Height = 800 };
        try
        {
            var library = Demo.Overview();
            var answers = new List<SupportAnswer>();
            library.OnSupportAnswer = answers.Add;
            Control page = skin == SkinKind.Mac ? new MacLibrary { DataContext = library } : new WinLibrary { DataContext = library };
            window.Content = ConfirmLayer.Wrap(window, page);
            window.Show();
            var layer = ConfirmLayer.For(window)!;

            var showing = SupportAskDialog.ShowAsync(layer, library.AskForSupport(last: false)!);
            SupportAskDialog? dialog = null;
            await Wait.UntilAsync(() => (dialog = window.GetVisualDescendants().OfType<SupportAskDialog>().FirstOrDefault()) is { IsEffectivelyVisible: true }
                && dialog.FindControl<Button>("Later")!.IsFocused, "the ask to pop up with Maybe later focused");
            Assert.True(layer.IsOpen);
            var popup = dialog!;
            Assert.Single(popup.GetVisualDescendants().OfType<SyftMark>());
            var texts = Texts(popup);
            Assert.Contains("Enjoying Syft Notes?", texts);
            Assert.Contains("It's free, and two students build it with Claude.\nA coffee keeps us going.", texts);
            foreach (string words in new[] { "Buy us a coffee", "Maybe later", "Don't ask again" })
                Assert.Contains(popup.GetVisualDescendants().OfType<Button>(), b => b.IsEffectivelyVisible && Equals(b.Content, words));

            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            await showing;
            Assert.Equal([SupportAnswer.Later], answers);
            Assert.False(layer.IsOpen);

            var last = new SupportAskModel(last: true, answers.Add);
            showing = SupportAskDialog.ShowAsync(layer, last);
            await Wait.UntilAsync(() => (dialog = window.GetVisualDescendants().OfType<SupportAskDialog>().FirstOrDefault()) is { IsEffectivelyVisible: true }
                && dialog.FindControl<Button>("Never")!.IsFocused, "the last ask to pop up");
            Assert.False(dialog!.FindControl<Button>("Later")!.IsVisible);
            last.NeverCommand.Execute(null);
            await showing;
            Assert.Equal([SupportAnswer.Later, SupportAnswer.Never], answers);
            Assert.False(layer.IsOpen);
        }
        finally
        {
            window.Close();
            ((App)Application.Current!).UseSkin(SkinKind.Mac);
        }
    }

    // --- Home ------------------------------------------------------------------------------------------------------

    /// <summary>"Buy us a coffee" is on Home, a quiet link at the page's foot under the lectures, in both looks, and a
    /// class's home has none. (What pressing it does, the Ko-fi page and no more asking, is <see cref="SupportAsk.Answer"/>'s,
    /// above.)</summary>
    [AvaloniaTheory]
    [InlineData(SkinKind.Mac)]
    [InlineData(SkinKind.Win)]
    public void Home_has_buy_us_a_coffee_at_its_foot_and_a_classs_home_has_none(SkinKind skin)
    {
        ((App)Application.Current!).UseSkin(skin);
        Window? window = null;
        try
        {
            Control Show(LibraryModel library)
            {
                window?.Close();
                Control view = skin == SkinKind.Mac ? new MacLibrary { DataContext = library } : new WinLibrary { DataContext = library };
                window = new Window { Width = 1280, Height = 800, Content = view };
                window.Show();
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                return view;
            }
            static Button? Named(Control view, string name) =>
                view.GetVisualDescendants().OfType<Button>().SingleOrDefault(b => b.Name == name && b.IsEffectivelyVisible);
            static Rect In(Control c, Control view) => new(c.TranslatePoint(default, view)!.Value, c.Bounds.Size);

            var home = Demo.Overview();
            int pressed = 0;
            home.Overview!.OnSupport = () => pressed++;
            var view = Show(home);
            var coffee = view.GetVisualDescendants().OfType<Button>().SingleOrDefault(b => b.Name == "Support" && b.IsVisible);
            Assert.NotNull(coffee);
            Assert.Equal("Buy us a coffee", coffee.Content);
            var notes = view.GetVisualDescendants().OfType<StackPanel>().First(p => p.Name == "Notes");
            Assert.True(In(coffee, view).Top >= In(notes, view).Bottom, $"{skin}: the link isn't under the notes");
            coffee.Command!.Execute(null);
            Assert.Equal(1, pressed);

            var cls = Demo.Overview("CS 101");
            cls.Overview!.OnSupport = () => { };
            Assert.Null(Named(Show(cls), "Support"));
        }
        finally
        {
            window?.Close();
            ((App)Application.Current!).UseSkin(SkinKind.Mac);
        }
    }
}
