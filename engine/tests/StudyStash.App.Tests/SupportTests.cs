using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StudyStash.App.Services;
using StudyStash.App.ViewModels;
using StudyStash.App.Views;

namespace StudyStash.App.Tests;

/// <summary>"Buy us more Claude usage": Settings → About's line and button, and the library window's one gentle ask
/// for a tip — when it asks, what each answer does, and that the Ko-fi page is only ever caught here, never handed to
/// a real browser.</summary>
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

    // --- the card --------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(SupportAnswer.Tip)]
    [InlineData(SupportAnswer.Later)]
    [InlineData(SupportAnswer.Never)]
    public void Each_button_answers_the_ask_and_puts_the_card_away(SupportAnswer answer)
    {
        var answers = new List<SupportAnswer>();
        var library = new LibraryModel { OnSupportAnswer = answers.Add };
        library.AskForSupport(last: false);
        var card = library.Support!;
        Assert.True(library.HasSupport);
        Assert.True(card.CanWait);
        library.AskForSupport(last: false);
        Assert.Same(card, library.Support);

        (answer switch
        {
            SupportAnswer.Tip => card.TipCommand,
            SupportAnswer.Later => card.LaterCommand,
            _ => card.NeverCommand,
        }).Execute(null);

        Assert.Equal([answer], answers);
        Assert.Null(library.Support);
        Assert.False(library.HasSupport);
    }

    [AvaloniaTheory]
    [InlineData(SkinKind.Mac)]
    [InlineData(SkinKind.Win)]
    public void The_card_sits_atop_the_lecture_list_covering_nothing(SkinKind skin)
    {
        ((App)Application.Current!).UseSkin(skin);
        try
        {
            var library = Demo.Library(askingSupport: true);
            var answers = new List<SupportAnswer>();
            library.OnSupportAnswer = answers.Add;
            Control view = skin == SkinKind.Mac ? new MacLibrary { DataContext = library } : new WinLibrary { DataContext = library };
            var window = new Window { Width = 1280, Height = 800, Content = view };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            var card = view.GetVisualDescendants().OfType<Control>().Single(c => c.Name == "SupportAsk");
            Assert.True(card.IsEffectivelyVisible);
            var texts = Texts(card);
            Assert.Contains("Enjoying Study Stash?", texts);
            Assert.Contains("It's free, and two students build it with Claude.\nA tip buys more Claude usage.", texts);
            Assert.Contains("Buy us more Claude usage", texts);
            Assert.Contains("Maybe later", texts);
            Assert.Contains("Don't ask again", texts);

            // In the list's flow: it ends above the first lecture, so it covers none of them.
            var firstLecture = view.GetVisualDescendants().OfType<Button>().First(b => b.Classes.Contains("card") && b.DataContext is LectureCard);
            double cardBottom = card.TranslatePoint(new Point(0, card.Bounds.Height), view)!.Value.Y;
            double lectureTop = firstLecture.TranslatePoint(new Point(0, 0), view)!.Value.Y;
            Assert.True(cardBottom <= lectureTop, $"the card ends at {cardBottom}, below the first lecture's top at {lectureTop}");

            var tip = card.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "Tip");
            tip.Command!.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal([SupportAnswer.Tip], answers);
            Assert.False(card.IsEffectivelyVisible);
            window.Close();
        }
        finally
        {
            ((App)Application.Current!).UseSkin(SkinKind.Mac);
        }
    }

    [Fact]
    public void Closing_the_window_on_the_ask_counts_as_maybe_later_and_the_last_time_ends_it()
    {
        var settings = new AppSettings();
        var answers = new List<SupportAnswer>();
        var library = new LibraryModel { OnSupportAnswer = a => { answers.Add(a); SupportAsk.Remember(settings, a, Monday); } };
        library.WalkAwayFromSupport(); // nothing showing: nothing to answer
        Assert.Empty(answers);

        library.AskForSupport(last: SupportAsk.IsLast(settings));
        library.WalkAwayFromSupport();
        Assert.Equal([SupportAnswer.Later], answers);
        Assert.Null(library.Support);
        Assert.False(settings.SupportAskDone);

        library.AskForSupport(last: SupportAsk.IsLast(settings));
        library.WalkAwayFromSupport();
        Assert.True(settings.SupportAskDone);
    }

    [AvaloniaFact]
    public void The_last_ask_offers_no_later()
    {
        var library = Demo.Library();
        library.AskForSupport(last: true);
        var view = new MacLibrary { DataContext = library };
        var window = new Window { Width = 1280, Height = 800, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var card = view.GetVisualDescendants().OfType<Control>().Single(c => c.Name == "SupportAsk");
        Assert.False(library.Support!.CanWait);
        Assert.DoesNotContain("Maybe later", Texts(card));
        Assert.Contains("Don't ask again", Texts(card));
        window.Close();
    }

    // --- Settings → About ------------------------------------------------------------------------------------------

    [AvaloniaFact]
    public void About_says_Study_Stash_is_free_and_its_button_opens_the_Ko_fi_page()
    {
        using var home = new TempHome();
        using var host = new AppHost(home.Path, log: _ => { });
        using var model = SettingsModel.Make(host);
        var opened = new List<string>();
        model.OpenUrl = opened.Add;
        model.Section = "General";
        var view = new SettingsView { DataContext = model };
        var window = new Window { Width = 900, Height = 900, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var texts = Texts(view);
        Assert.Contains("Study Stash is free and open source, built with Claude.", texts);
        Assert.Contains("Buy us more Claude usage", texts);
        var button = view.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "BuyClaudeUsage");
        Assert.True(button.IsEffectivelyVisible);
        button.Command!.Execute(null);
        Assert.Equal([KoFi], opened);
        // Having bought Claude usage from here, the library window never asks for a tip.
        Assert.True(AppSettings.Load(home.Path).SupportAskDone);
        window.Close();
    }
}
