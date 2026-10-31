using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using StudyStash.App.Services;
using StudyStash.App.ViewModels;
using StudyStash.App.Views;
using StudyStash.Core;
using StudyStash.Core.Ai;

namespace StudyStash.App.Tests;

/// <summary>The one-button first run's pictures, both looks, light and dark, drawn off screen: the welcome, Getting
/// ready, Home's "Ready when class starts", the lecture page's "Get study notes" card with Continue with Google, its
/// fallbacks (a school account, a used-up allowance), and the small window that gets Gemini ready. Set STUDYSTASH_SHOTS
/// to put them somewhere to look at.</summary>
public sealed class FirstRunShots
{
    static readonly ThemeVariant[] Themes = [ThemeVariant.Light, ThemeVariant.Dark];
    static readonly SkinKind[] Looks = [SkinKind.Mac, SkinKind.Win];
    static string Look(SkinKind skin) => skin == SkinKind.Mac ? "mac" : "win";

    static SetupModel Welcome(SkinKind skin, Action<SetupModel>? state = null)
    {
        var m = SetupModel.For(skin);
        m.OneButton = true;
        state?.Invoke(m);
        return m;
    }

    static Control WelcomeView(SkinKind skin, SetupModel m) =>
        skin == SkinKind.Mac ? new MacWelcome { DataContext = m, DrawChrome = true } : new WinWelcome { DataContext = m, DrawChrome = true };

    [AvaloniaFact]
    public void The_welcome_getting_ready_and_a_problem()
    {
        foreach (var skin in Looks)
            foreach (var t in Themes)
            {
                Shot.Take($"{Look(skin)}-welcome", skin, t, () => WelcomeView(skin, Welcome(skin)), size: new Size(688, 748));
                Shot.Take($"{Look(skin)}-welcome-getting-ready", skin, t, () => WelcomeView(skin, Welcome(skin, m => m.Starting = true)), size: new Size(688, 748));
                Shot.Take($"{Look(skin)}-welcome-problem", skin, t,
                    () => WelcomeView(skin, Welcome(skin, m => m.LibraryResult = "Syft Notes couldn't start its library: port 8787 and the next ten are taken by other apps.")),
                    size: new Size(688, 748));
            }
    }

    /// <summary>The library window, as a first run leaves it: no classes, no lectures yet.</summary>
    static LibraryModel EmptyLibrary(Demo.Page page)
    {
        var m = Demo.Semester(page);
        m.DrawChrome = true;
        m.Classes.Clear();
        return m;
    }

    static Control LibraryView(SkinKind skin, LibraryModel m) =>
        skin == SkinKind.Mac ? new MacLibrary { DataContext = m, Width = 1180, Height = 760 } : new WinLibrary { DataContext = m, Width = 1180, Height = 760 };

    [AvaloniaFact]
    public void Homes_card_after_Get_started()
    {
        foreach (var skin in Looks)
            foreach (var t in Themes)
                Shot.Take($"{Look(skin)}-home-ready-card", skin, t, () =>
                {
                    var m = EmptyLibrary(Demo.Page.Home);
                    var home = OverviewModel.Home(new OverviewModel.Sources { Now = new DateTimeOffset(2026, 10, 30, 9, 12, 0, TimeSpan.FromHours(-7)), Zone = TimeZoneInfo.Utc, NotesOff = true });
                    home.OnRecord = () => { };
                    m.Overview = home;
                    var card = new WelcomeCardModel(skin == SkinKind.Mac) { RecordKeys = skin == SkinKind.Mac ? "⌃⌥R" : "Ctrl+Alt+R" };
                    card.ShowModel(ready: false, fraction: 0.42, problem: null, device: skin == SkinKind.Mac ? "Mac" : "PC");
                    m.Welcome = card;
                    return LibraryView(skin, m);
                }, size: new Size(1308, 888));
    }

    /// <summary>A first lecture's page with no AI writing notes yet, the card in <paramref name="state"/>.</summary>
    static LibraryModel FirstLecture(Action<FreeAiModel> state)
    {
        var m = EmptyLibrary(Demo.Page.Note);
        m.Notes = null;
        m.Note = new NoteModel
        {
            Id = "n1", ClassName = "", Noun = Nouns.Lecture, Title = "Lecture, Friday 30 October at 9:30 AM",
            When = "Friday 30 October at 9:30 AM, 50 min", Pending = NoteModel.NotesAreOff(Nouns.Lecture, transcript: true),
        }.Titled();
        var free = new FreeAiModel(new FakeAiLibrary(), "n1") { Offer = true, Choose = true };
        state(free);
        m.FreeAi = free;
        return m;
    }

    [AvaloniaFact]
    public void The_lecture_pages_card_and_its_fallbacks()
    {
        var states = new (string Name, Action<FreeAiModel> State)[]
        {
            ("note-get-notes", _ => { }),
            ("note-get-notes-copy", f => f.CopyWayOpen = true),
            ("note-school-account", f => f.Fallback = NotesFallback.GoogleAccount),
            ("note-allowance-used-up", f =>
            {
                f.LimitedAi = "Gemini";
                f.LimitedUntil = "Tue 3 Nov, 9:00 am";
                f.Fallback = NotesFallback.Limit;
            }),
        };
        foreach (var skin in Looks)
            foreach (var (name, state) in states)
                foreach (var t in Themes)
                    Shot.Take($"{Look(skin)}-{name}", skin, t, () => LibraryView(skin, FirstLecture(state)), size: new Size(1308, 888));
    }

    /// <summary>Continue with Google: the small window that gets Gemini ready (Google's own installer, then Google's own
    /// sign-in page).</summary>
    [AvaloniaFact]
    public async Task The_window_that_gets_Gemini_ready()
    {
        foreach (var skin in Looks)
            foreach (var screen in new[] { GuidedScreen.Install, GuidedScreen.SignIn })
                foreach (var t in Themes)
                {
                    var g = GuidedSetupShots.Guided(skin);
                    await g.GetReadyForNotesAsync("gemini");
                    g.Screen = screen;
                    Shot.Take($"{Look(skin)}-get-ready-gemini-{(screen == GuidedScreen.Install ? "install" : "sign-in")}", skin, t,
                        () => skin == SkinKind.Mac ? new MacGetReady { DataContext = g, DrawChrome = true } : new WinGetReady { DataContext = g, DrawChrome = true },
                        size: new Size(748, 728));
                }
    }
}
