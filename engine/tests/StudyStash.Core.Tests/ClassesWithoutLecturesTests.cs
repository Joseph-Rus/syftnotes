using System.Text.Json.Nodes;

namespace StudyStash.Core.Tests;

/// <summary>
/// "6 classes, 0 lectures" on a Windows PC: every way a lecture can be in the library yet not show under the class
/// a student (or Claude, through list_classes and list_lectures) is looking at.
/// </summary>
public class ClassesWithoutLecturesTests
{
    static (Config Cfg, Store Store, LocalLibrary Lib) Library(TempDir dir, params string[] classes)
    {
        var cfg = new Config(dir["home"], dir["pool"]);
        foreach (string c in classes) cfg.Classes.Add(new ClassDef(c));
        var store = new Store(dir["state.db"], dir["pool"]);
        return (cfg, store, new LocalLibrary(new LibraryReader(cfg, store)));
    }

    static void File(Store store, string id, string title, string className)
    {
        var m = new Meeting(id) { Title = title, Date = "2026-10-20T10:00:00Z", Transcript = "Today we start recursion." };
        store.Save(m, new Classification(className, 0.95, "folder", title));
    }

    [Fact]
    public async Task A_course_name_Windows_forbids_characters_in_still_lists_its_lectures()
    {
        using var dir = new TempDir();
        var (_, store, lib) = Library(dir, "CS 101: Intro / Lab.", "Con");
        using var _s = store;
        File(store, "rec-1", "Week 1: Loops?", "CS 101: Intro / Lab.");
        File(store, "rec-2", "Week 2", "Con");
        Assert.Equal("Library: Lecture notes\n- CS 101: Intro / Lab. (1 lecture)\n- Con (1 lecture)", await ClaudeTools.ListClassesAsync(lib));
        Assert.Contains("[rec-1] Week 1: Loops?", await ClaudeTools.ListLecturesAsync(lib, "CS 101: Intro / Lab.", 20, null));
        Assert.Contains("[rec-2] Week 2", await ClaudeTools.ListLecturesAsync(lib, "Con", 20, null));
        // Neither class's folder name is one Windows can't make (a trailing dot or space, a device name).
        foreach (string name in new[] { "CS 101: Intro / Lab.", "Con", "NUL.", "com1", "Aux ." })
        {
            string folder = Path.GetFileName(store.ClassFolder(name));
            Assert.False(folder.EndsWith('.') || folder.EndsWith(' '), folder);
            Assert.DoesNotContain(folder.Split('.')[0].Trim().ToUpperInvariant(), new[] { "CON", "PRN", "AUX", "NUL", "COM1", "LPT1" });
        }
    }

    [Fact]
    public async Task A_lecture_filed_under_the_class_in_other_letters_still_counts_for_the_class()
    {
        using var dir = new TempDir();
        var (_, store, lib) = Library(dir, "CS 101: Intro");
        using var _s = store;
        // Recorded for "cs 101: intro" before Canvas gave the class its capitals.
        File(store, "rec-1", "Week 1", "cs 101: intro");
        Assert.Equal("Library: Lecture notes\n- CS 101: Intro (1 lecture)", await ClaudeTools.ListClassesAsync(lib));
        Assert.Contains("[rec-1]", await ClaudeTools.ListLecturesAsync(lib, "CS 101: Intro", 20, null));
        Assert.Single(store.ListNotes("CS 101: INTRO"));
    }

    [Fact]
    public async Task Lectures_recorded_before_the_classes_came_are_listed_as_not_in_a_class()
    {
        using var dir = new TempDir();
        var (_, store, lib) = Library(dir, "Biology", "History");
        using var _s = store;
        File(store, "rec-1", "Recording, Mon 19 Oct", Configs.Unsorted);
        string classes = await ClaudeTools.ListClassesAsync(lib);
        Assert.Contains("- Biology (0 lectures)", classes);
        Assert.Contains("Unsorted: 1 lecture not yet in a class", classes);
        Assert.Contains("[rec-1]", await ClaudeTools.ListLecturesAsync(lib, null, 20, null));
        // Asked for a class with none, Claude is told where the others are rather than that there are none.
        string empty = await ClaudeTools.ListLecturesAsync(lib, "Biology", 20, null);
        Assert.Contains("No lectures in Biology", empty);
        Assert.Contains("Unsorted", empty);
    }

    [Fact]
    public async Task A_lecture_the_library_couldnt_file_is_counted_not_hidden()
    {
        using var dir = new TempDir();
        var (_, store, lib) = Library(dir, "Biology");
        using var _s = store;
        store.Enqueue(new Meeting("rec-1") { Title = "Week 1", Date = "2026-10-20T10:00:00Z", Transcript = "Cells." });
        store.Enqueue(new Meeting("rec-2") { Title = "Week 2", Date = "2026-10-21T10:00:00Z", Transcript = "Mitosis." });
        store.ClaimNext();
        store.MarkFailed("rec-1", "Access to the path is denied.");
        string classes = await ClaudeTools.ListClassesAsync(lib);
        Assert.Contains("- Biology (0 lectures)", classes);
        Assert.Contains("1 lecture is waiting for its notes", classes);
        Assert.Contains("1 lecture couldn't be filed: Access to the path is denied.", classes);
        string lectures = await ClaudeTools.ListLecturesAsync(lib, null, 20, null);
        Assert.Contains("couldn't be filed", lectures);
        Assert.Equal(1, (int)(await lib.OverviewAsync())["stuck"]!);
    }
}
