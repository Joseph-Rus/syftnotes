using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using StudyStash.App.Services;
using StudyStash.App.ViewModels;
using StudyStash.App.Views;
using StudyStash.Core;

namespace StudyStash.App.Pictures;

/// <summary>
/// Adding a lecture a friend shared (a .syft file): the one question, which class, Mac and Windows, light and dark. Only
/// when STUDYSTASH_SHOTS names the folder to put them in:
/// <code>STUDYSTASH_SHOTS=/tmp/shots dotnet test engine/tests/StudyStash.Pictures --filter SharedLectureShots</code>
/// </summary>
public sealed class SharedLectureShots
{
    static readonly (string Name, int Color)[] Classes = [("Cell Biology", 2), ("Organic Chemistry", 5), ("Statistics", 8)];

    static SharedLecturePage Page(string fromClass) => new()
    {
        DataContext = new SharedLectureModel(new SharedLecture("Lecture 4: The cell membrane", fromClass, "2026-11-03T09:30:00-05:00", "## Summary", "[00:05] Today."),
            "", Classes, (_, _) => Task.FromResult(Outcome.Done), () => { }),
        DrawChrome = true,
    };

    [AvaloniaFact]
    public void Which_class()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("STUDYSTASH_SHOTS") is { Length: > 0 }, "Only with STUDYSTASH_SHOTS naming a folder.");
        foreach (var skin in new[] { SkinKind.Mac, SkinKind.Win })
            foreach (var t in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            {
                string look = skin == SkinKind.Mac ? "mac" : "win";
                // A class of the same name here: it starts there. None: the library sorts it.
                Shot.Take($"{look}-shared-lecture-same-class", skin, t, () => Page("Cell Biology"), size: new Size(608, 480));
                Shot.Take($"{look}-shared-lecture-sorted", skin, t, () => Page("BIO 201"), size: new Size(608, 480));
            }
    }
}
