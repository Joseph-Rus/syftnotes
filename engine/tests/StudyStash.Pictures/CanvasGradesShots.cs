using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using StudyStash.App.Views;

namespace StudyStash.App.Pictures;

/// <summary>A class's grade under its name, and the scores on the handed-in work in Due's week and month. Drawn off
/// screen into STUDYSTASH_SHOTS; only with STUDYSTASH_FILM=1 (demo data only).</summary>
public class CanvasGradesShots
{
    [AvaloniaFact]
    public void A_class_page_says_its_grade_and_due_says_what_was_scored()
    {
        if (Environment.GetEnvironmentVariable("STUDYSTASH_FILM") != "1") return;
        foreach (var skin in new[] { SkinKind.Mac, SkinKind.Win })
            foreach (var t in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            {
                string prefix = skin == SkinKind.Mac ? "mac" : "win";
                foreach (var (name, page, height) in new[]
                {
                    ("class-grade", Demo.Page.FolderClass, 860), ("due-week-scores", Demo.Page.DueWeek, 900), ("due-month-scores", Demo.Page.Due, 900),
                })
                    Shot.Take($"{prefix}-{name}", skin, t, () =>
                    {
                        var m = Demo.Semester(page, graded: true);
                        return skin == SkinKind.Mac ? new MacLibrary { DataContext = m, Width = 1280, Height = height } : new WinLibrary { DataContext = m, Width = 1280, Height = height };
                    }, size: new Size(1408, height + 128));
            }
    }
}
