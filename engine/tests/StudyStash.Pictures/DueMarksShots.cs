using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using StudyStash.App.Services;
using StudyStash.App.ViewModels;
using StudyStash.App.Views;

namespace StudyStash.App.Pictures;

/// <summary>"Mark done" and "Hide" on Due items, kept in Syft Notes only (shots/due-marks-*.png): the Due tab with the missing
/// assignment ticked off (a tick) and one item hidden ("Show hidden (1)"), with the hidden list open, the week view, an
/// assignment's page before and after "Mark done", and Home's sentence before and after hiding the missing assignment.
/// Both looks, light and dark. Drawn off screen with the design's sample, never on the student's screen; only with STUDYSTASH_FILM=1.</summary>
public class DueMarksShots
{
    static readonly SkinKind[] Skins = [SkinKind.Mac, SkinKind.Win];
    static readonly ThemeVariant[] Themes = [ThemeVariant.Light, ThemeVariant.Dark];
    static readonly Size Window = new(1408, 1128);

    static bool On => Environment.GetEnvironmentVariable("STUDYSTASH_FILM") == "1";
    static string Name(string what, SkinKind skin) => $"due-marks-{what}-{(skin == SkinKind.Mac ? "mac" : "win")}";

    static Control Library(SkinKind skin, LibraryModel m) =>
        skin == SkinKind.Mac ? new MacLibrary { DataContext = m, Width = 1280, Height = 1000 } : new WinLibrary { DataContext = m, Width = 1280, Height = 1000 };

    [AvaloniaFact]
    public void The_Due_tab_with_one_item_ticked_off_and_one_hidden()
    {
        if (!On) return;
        foreach (var skin in Skins)
            foreach (var t in Themes)
            {
                Shot.Take(Name("due", skin), skin, t, () => Library(skin, Demo.Semester(Demo.Page.Due, done: "d1", hidden: "d9")), size: Window);
                Shot.Take(Name("due-hidden-open", skin), skin, t, () =>
                {
                    var m = Demo.Semester(Demo.Page.Due, done: "d1", hidden: "d9");
                    ((OverviewModel)m.Overview!).Calendar!.ShowingHidden = true;
                    return Library(skin, m);
                }, size: Window);
            }
    }

    [AvaloniaFact]
    public void The_week_with_one_item_ticked_off()
    {
        if (!On) return;
        foreach (var skin in Skins)
            foreach (var t in Themes)
                Shot.Take(Name("week", skin), skin, t, () => Library(skin, Demo.Semester(Demo.Page.DueWeek, done: "d2")), size: Window);
    }

    [AvaloniaFact]
    public void Homes_sentence_before_and_after_hiding_the_missing_assignment()
    {
        if (!On) return;
        foreach (var skin in Skins)
        {
            Shot.Take(Name("home-before", skin), skin, ThemeVariant.Light, () => Library(skin, Demo.Semester(Demo.Page.Home, quiet: true)), size: Window);
            Shot.Take(Name("home-after", skin), skin, ThemeVariant.Light, () => Library(skin, Demo.Semester(Demo.Page.Home, hidden: "d1", quiet: true)), size: Window);
        }
    }

    [AvaloniaFact]
    public async Task An_assignments_page_before_and_after_Mark_done()
    {
        if (!On) return;
        var detail = CanvasFixtures.Load<CanvasApi.AssignmentDetail>("assignment-9001") with
        {
            Class = "Theory of Computation", Name = "Asynchronous Lecture 5", Kind = "assignment", Status = "missing", Label = "Missing", Missing = true,
            Submission = new CanvasApi.SubmissionInfo { State = "unsubmitted" },
        };
        foreach (var skin in Skins)
            foreach (var t in Themes)
                foreach (bool marked in new[] { false, true })
                {
                    var page = new AssignmentModel(CanvasFixtures.Context(), (_, _, _, _, _) => Task.FromResult(Outcome.Done));
                    page.Show(detail);
                    if (marked) await page.Marks.MarkDoneCommand.ExecuteAsync(null);
                    var m = Demo.Semester(Demo.Page.Due);
                    m.Overview = null;
                    m.Assignment = page;
                    Shot.Take(Name(marked ? "assignment-done" : "assignment", skin), skin, t, () => Library(skin, m), size: Window);
                }
    }
}
