using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Microsoft.Extensions.Time.Testing;
using StudyStash.App.Services;
using StudyStash.App.ViewModels;
using StudyStash.App.Views;
using static StudyStash.App.ViewModels.OverviewModel;

namespace StudyStash.App.Pictures;

/// <summary>
/// "Lectures that already happened", the first look (feature 9), as pictures drawn off screen from the demo semester, Mac
/// and Windows, light and dark: the two ways in (a button beside Attach and Record, or a small menu on Record), the
/// form, a drop hint, the lecture afterwards, and a file that can't be read. Only when STUDYSTASH_SHOTS names the folder:
/// <code>STUDYSTASH_SHOTS=/tmp/shots dotnet test engine/tests/StudyStash.Pictures --filter FromFileShots</code>
/// </summary>
public sealed class FromFileShots
{
    static readonly ThemeVariant[] Themes = [ThemeVariant.Light, ThemeVariant.Dark];
    static readonly SkinKind[] Looks = [SkinKind.Mac, SkinKind.Win];
    static readonly Size Wide = new(1408, 988);

    /// <summary>The demo semester's classes, with their highlighters, as the class menu lists them.</summary>
    static readonly (string Name, int Color)[] Classes =
    [
        ("Machine Learning", 4), ("Capstone", 1), ("Computer Architecture", 6), ("Data Mining", 2), ("Theory of Computation", 3), ("ACM", 5),
    ];

    static string Look(SkinKind skin) => skin == SkinKind.Mac ? "mac" : "win";

    static Control Library(SkinKind skin, LibraryModel m) =>
        skin == SkinKind.Mac ? new MacLibrary { DataContext = m, Width = 1280, Height = 860 } : new WinLibrary { DataContext = m, Width = 1280, Height = 860 };

    /// <summary>The demo's Home or Machine Learning page with the new way in switched on.</summary>
    static LibraryModel Page(Demo.Page page, AddRecordingEntry entry)
    {
        var m = Demo.Semester(page);
        m.Overview!.OnAddRecording = _ => { };
        m.Overview.Entry = entry;
        return m;
    }

    static Button Named(Window w, string name) => w.GetVisualDescendants().OfType<Button>().First(b => b.Name == name && b.IsEffectivelyVisible);

    static void ShowFlyout(Window w, string button) => Named(w, button).Flyout!.ShowAt(Named(w, button));

    /// <summary>The form for a recording of a lecture, as it opens: from a class's page, or from Home (no class).</summary>
    static FromFileModel Form(string file, string cls, long bytes = 48_300_000, Func<NewFileLecture, CancellationToken, Task<Outcome>>? add = null)
    {
        var now = new FakeTimeProvider(new DateTimeOffset(2026, 10, 12, 13, 34, 0, TimeSpan.Zero));
        now.SetLocalTimeZone(TimeZoneInfo.Utc);
        return new FromFileModel(new RecordingFile(file, bytes, new DateTime(2026, 10, 9, 9, 31, 0)), cls, Classes, add ?? ((_, _) => Task.FromResult(Outcome.Done)), () => { }, now);
    }

    static FromFilePage FormView(FromFileModel m) => new() { DataContext = m, DrawChrome = true };

    /// <summary>One picture in each look and each theme, named <c>&lt;name&gt;-&lt;mac|win&gt;-&lt;light|dark&gt;.png</c>.</summary>
    static void Shots(string name, Func<SkinKind, Control> scene, Size size, Action<Window>? open = null)
    {
        // STUDYSTASH_ONLY=2- draws just the pictures whose names start so, to look at one thing again.
        if (Environment.GetEnvironmentVariable("STUDYSTASH_ONLY") is { Length: > 0 } only && !name.StartsWith(only)) return;
        foreach (var skin in Looks)
            foreach (var t in Themes)
                Shot.Take($"{name}-{Look(skin)}", skin, t, () => scene(skin), size: size, open: open);
    }

    [AvaloniaFact]
    public async Task The_first_look()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("STUDYSTASH_SHOTS") is { Length: > 0 }, "Only with STUDYSTASH_SHOTS naming a folder.");

        // 1. Two ways in, on the class page and on Home. A: a button beside Attach and Record. B: a small menu on Record.
        Shots("1A-class-page-button", s => Library(s, Page(Demo.Page.FolderClass, AddRecordingEntry.Button)), Wide);
        Shots("1A-home-button", s => Library(s, Page(Demo.Page.Home, AddRecordingEntry.Button)), Wide);
        Shots("1B-class-page-menu-closed", s => Library(s, Page(Demo.Page.FolderClass, AddRecordingEntry.Menu)), Wide);
        Shots("1B-class-page-menu-open", s => Library(s, Page(Demo.Page.FolderClass, AddRecordingEntry.Menu)), Wide, w => ShowFlyout(w, "RecordMenu"));
        Shots("1B-home-menu-closed", s => Library(s, Page(Demo.Page.Home, AddRecordingEntry.Menu)), Wide);
        Shots("1B-home-menu-open", s => Library(s, Page(Demo.Page.Home, AddRecordingEntry.Menu)), Wide, w => ShowFlyout(w, "RecordMenu"));

        // 2. The form: a voice memo from the class page; a lecture video from Home; the class menu and the calendar open.
        var form = new Size(608, 600);
        Shots("2-form-class-page-voice-memo", s => FormView(Form("Lecture_6_gradient_descent.m4a", "Machine Learning")), form);
        Shots("2-form-home-video", s => FormView(Form("Zoom Recording - Week 6.mp4", "", 212_000_000)), form);
        Shots("2-form-class-menu-open", s => FormView(Form("Zoom Recording - Week 6.mp4", "", 212_000_000)), new Size(608, 800),
            w => w.GetVisualDescendants().OfType<Button>().First(b => b.Name == "ClassButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
        Shots("2-form-date-open", s => FormView(Form("Lecture_6_gradient_descent.m4a", "Machine Learning")), new Size(608, 880),
            w => ShowFlyout(w, "DateButton"));

        // 3. A recording dragged over the page: it says it will be added (other files attach, as before).
        Shots("3-drop-hint-class-page", s =>
        {
            var m = Page(Demo.Page.FolderClass, AddRecordingEntry.Button);
            m.Overview!.DroppingName = "Lecture_6_gradient_descent.m4a";
            return Library(s, m);
        }, Wide);
        Shots("3-drop-hint-home", s =>
        {
            var m = Page(Demo.Page.Home, AddRecordingEntry.Button);
            m.Overview!.DroppingName = "Zoom Recording - Week 6.mp4";
            return Library(s, m);
        }, Wide);

        // 4. Afterwards: the lecture in the list, being written down, as a recording made in class shows.
        Shots("4-after-class-page", s => Library(s, Added(Demo.Page.FolderClass)), Wide);
        Shots("4-after-home", s => Library(s, Added(Demo.Page.Home)), Wide);

        // 5. A file it can't read: the library's words in the form, and a kind of file it won't take.
        var damaged = Form("Lecture_6_gradient_descent.m4a", "Machine Learning", add: (_, _) =>
            Task.FromResult(Outcome.Failed(new Failure(FailureKind.Refused, "Syft Notes couldn't read that recording. It may be damaged. Voice Memos' .m4a, .mp3 and .wav all work."))));
        await damaged.Add.Command.ExecuteAsync(null);
        Shots("5-form-cant-read-damaged-file", s => FormView(damaged), new Size(608, 660));
        Shots("5-form-cant-read-wrong-kind-of-file", s => FormView(Form("Syllabus.pdf", "Machine Learning", 380_000)), new Size(608, 660));
    }

    /// <summary>The demo page with the new lecture at its place in the list, as the shell words a recording this computer
    /// is still writing down: "Being written down", with no length yet.</summary>
    static LibraryModel Added(Demo.Page page)
    {
        var m = Page(page, AddRecordingEntry.Button);
        var o = m.Overview!;
        var now = new DateTimeOffset(2026, 10, 12, 13, 34, 0, TimeSpan.FromHours(-4));
        var sources = new Sources { Now = now, Zone = TimeZoneInfo.CreateCustomTimeZone("Demo", TimeSpan.FromHours(-4), "Demo", "Demo") };
        bool home = page == Demo.Page.Home;
        // From a class's page it's the Friday lecture; from Home it's this morning's video, not sorted yet.
        var row = home
            ? Row(new LectureFacts("added", "Zoom Recording - Week 6", "Unsorted", new DateTimeOffset(2026, 10, 12, 8, 15, 0, TimeSpan.FromHours(-4)), null, "", false, Status: "Being written down"),
                sources, _ => Avalonia.Media.Brushes.Gray, showFolder: true)
            : Row(new LectureFacts("added", "Lecture 6 gradient descent", "Machine Learning", new DateTimeOffset(2026, 10, 9, 9, 31, 0, TimeSpan.FromHours(-4)), null, "", false,
                Status: "Being written down"), sources, _ => Avalonia.Media.Brushes.Gray, showFolder: false);
        if (home)
        {
            // Home lists every note, newest first: this morning's goes in under the 9:30 class.
            o.Notes.Insert(1, row);
        }
        else
        {
            row.Dot = Skin.ClassDot(4);
            var last = o.Groups[1];
            foreach (var n in last.Notes) n.First = false;
            row.First = true;
            o.Groups[1] = last with { Notes = [row, .. last.Notes] };
        }
        return m;
    }
}
