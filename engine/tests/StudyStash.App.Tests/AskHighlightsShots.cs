using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StudyStash.App.ViewModels;
using StudyStash.App.Views;
using StudyStash.Core.Ai;

namespace StudyStash.App.Tests;

/// <summary>Pictures of a lecture's page after an answer: the words of its notes the answer quoted highlighted, and the
/// From your notes chips under it, both looks, light and dark, with a chip's highlight stronger where the pointer is on it.
/// Drawn off screen into STUDYSTASH_SHOTS; only with STUDYSTASH_FILM=1. The answer is a stand-in's: no AI is asked.</summary>
public class AskHighlightsShots
{
    const string Notes = """
        ## Summary
        How arteries, capillaries and veins differ, and how blood pressure is measured and worked out.

        ### Three kinds of vessel
        - **Arteries** carry blood away from the heart under high pressure; thick, elastic walls.
        - **Capillaries** are one cell thick: this is where gases and nutrients are exchanged.
        - **Veins** carry blood back at low pressure and have valves to stop backflow.

        ### Blood pressure
        Blood pressure is the force of blood on the walls of an artery. It is written as systolic over diastolic, for example 120/80 mmHg.

        - Systolic is the pressure while the heart is contracting.
        - Diastolic is the pressure while the heart relaxes between beats.
        - A reading above 130/80 is called high blood pressure.

        ### Why valves matter
        Veins sit at low pressure, so the muscles around them squeeze blood along and the valves stop it falling back between squeezes.
        """;

    const string Answer = "Arteries carry blood **away** from the heart, under high pressure, so their walls are thick and elastic. Capillaries are where the exchange happens, because their walls are one cell thick, and veins bring blood back at low pressure with valves to stop it falling back.";

    static readonly string[] Quotes =
    [
        "Arteries carry blood away from the heart under high pressure; thick, elastic walls",
        "capillaries are one cell thick: this is where gases and nutrients are exchanged",
        "Veins carry blood back at low pressure and have valves to stop backflow.",
        "Mitochondria are the powerhouse of the cell",
    ];

    internal static async Task<LibraryModel> Page(string notes = Notes, string answer = Answer, string[]? quotes = null, string question = "How do the three kinds of vessel differ?")
    {
        var lib = new FakeAiLibrary { Overview = AiTestData.MixedOverview() };
        lib.OnAsk = _ => new AskReply(answer, [new AskSource("rec-1", "Blood vessels and blood pressure", "BIO 110", "2026-10-08", null, "Three kinds of vessel")], "claude", "Claude")
        {
            Quotes = [.. quotes ?? Quotes], Chat = "c1", Remembered = 0,
        };
        var ask = new AiAskModel(lib) { LectureId = "rec-1", ClassName = "BIO 110", OpenSettings = () => { }, Place = new AskPlace("lecture", "rec-1", "BIO 110") };
        ask.NameScopes(Nouns.Lecture, "BIO 110");
        await ask.Load();
        ask.Engine = "claude";
        ask.Question = question;
        await ask.AskCommand.ExecuteAsync(null);

        string[] classes = ["BIO 110", "CHEM 121", "MATH 221", "PSYC 101"];
        var m = new LibraryModel { Status = "Library on this computer", DrawChrome = false };
        for (int i = 0; i < classes.Length; i++)
            m.Classes.Add(new ClassItem { Name = classes[i], Dot = Skin.ClassDot(i), Count = 4, Selected = i == 0 });
        m.Note = new NoteModel
        {
            Id = "rec-1", ClassName = "BIO 110", Dot = Skin.ClassDot(0), Noun = Nouns.Lecture, When = "Thursday 8 October at 9:30 AM, 50 min",
            Title = "Blood vessels and blood pressure", Markdown = notes, OnRename = _ => Task.FromResult(true),
        }.Titled();
        m.Notes = AiDemo.LectureNotes(notes, "Claude");
        m.Notes.Highlights = ask.Highlights; // as the app's Shell links them
        m.Ask = ask;
        return m;
    }

    internal static void Take(Control view, SkinKind skin, ThemeVariant variant, string name, Action? afterShown = null)
    {
        Skin.UseTheme(ColourThemes.Default);
        ((App)Application.Current!).UseSkin(skin);
        var window = new Window { Width = 1180, Height = 840, RequestedThemeVariant = variant, Content = view };
        Look.Apply(window);
        window.Show();
        DiagramsReady.Wait(window);
        void Settle()
        {
            for (int i = 0; i < 3; i++)
            {
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
            }
        }
        Settle();
        afterShown?.Invoke();
        Settle();
        using (var file = File.Create(System.IO.Path.Combine(Shot.Dir, name + ".png")))
            window.CaptureRenderedFrame()!.Save(file, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        window.Close();
    }

    [AvaloniaFact]
    public async Task A_lecture_page_after_an_answer_with_its_quotes_highlighted()
    {
        if (Environment.GetEnvironmentVariable("STUDYSTASH_FILM") != "1") return;
        foreach (var (skin, label) in new[] { (SkinKind.Mac, "mac"), (SkinKind.Win, "win") })
            foreach (var (variant, tone) in new[] { (ThemeVariant.Light, "light"), (ThemeVariant.Dark, "dark") })
            {
                var model = await Page();
                Control View() => skin == SkinKind.Mac ? new MacLibrary { DataContext = model } : new WinLibrary { DataContext = model };
                // The chips are the three the notes hold, once they've said what they say; the fourth (not in them) was dropped.
                Take(View(), skin, variant, $"{label}-{tone}-answer-quotes", () => Assert.Equal(3, model.Ask!.Turns[0].Chips.Count));
                if ((label == "mac" && tone == "light") || (label == "win" && tone == "dark"))
                {
                    // The pointer on the second chip: its passage is stronger.
                    Take(View(), skin, variant, $"{label}-{tone}-answer-quotes-hover", () => model.Ask!.Turns[0].Chips[1].Hover(true));
                }
            }
    }
}
