using System.Text.Json.Nodes;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using StudyStash.App.Services;
using StudyStash.App.ViewModels;
using StudyStash.Core;
using StudyStash.Core.Calendar;

namespace StudyStash.App;

/// <summary>The library window's overviews: Home (every class at once, where the window opens) and each class's home
/// (what a class is picked in the sidebar opens), with the ways further in to its lectures and its Canvas lists.</summary>
public static partial class Shell
{
    /// <summary>The overview showing: "" for Home, a class's name for its home, null for neither.</summary>
    static string? overviewOf;

    /// <summary>Home ("") or a class's home.</summary>
    static Task ShowOverviewAsync(string of) => of.Length == 0 ? ShowHomeAsync() : ShowClassHomeAsync(of);

    /// <summary>Whatever opens next (a class's lectures, Due) takes the overview's place.</summary>
    static void LeaveOverview()
    {
        overviewOf = null;
        library.Overview = null;
        library.Home.Selected = false;
    }

    static void SelectInSidebar(string? cls)
    {
        library.Home.Selected = cls is null;
        foreach (var c in library.Classes) c.Selected = cls is not null && c.Name == cls && !c.IsDue;
        library.Unsorted.Selected = false;
    }

    /// <summary>Everything on the page beside the lecture goes, and the right column with it.</summary>
    static void ClearForOverview()
    {
        dueOpen = false;
        dueSelection = null;
        library.NarrowDetail = false;
        ClearDetail();
        library.Notes?.Dispose();
        library.Notes = null;
        library.Note = null;
    }

    static async Task ShowHomeAsync()
    {
        int turn = ++libraryTurn;
        overviewOf = "";
        SelectInSidebar(null);
        ClearForOverview();
        if (host.Remote() is not { } lib) return;
        var lectures = await OverviewLecturesAsync(lib, null, 60);
        if (turn != libraryTurn) return;
        library.Overview = OverviewModel.Home(OverviewSources(lectures, events: library.ComingUp.HasCalendars ? [.. library.ComingUp.Rows] : null));
    }

    static async Task ShowClassHomeAsync(string name)
    {
        if (name == Configs.Unsorted)
        {
            await ShowClassAsync(name);
            return;
        }
        int turn = ++libraryTurn;
        overviewOf = name;
        if (openClass != name) openLecture = null;
        openClass = name;
        allLectures = false;
        SelectInSidebar(name);
        ClearForOverview();
        if (host.Remote() is not { } lib) return;
        var lectures = await OverviewLecturesAsync(lib, name, OverviewModel.Rows);
        if (turn != libraryTurn) return;
        var row = CanvasClassRow(name);
        var info = (host.Overview?["classes"] as JsonArray ?? []).OfType<JsonObject>().FirstOrDefault(c => S(c["name"]) == name);
        var known = host.Classes().FirstOrDefault(c => c.Name == name);
        var facts = new ClassHomeFacts(name, DotFor(name), known.Lectures, row?.Canvas?.ShortCode is { Length: > 0 } code ? code : null,
            row?.Canvas?.Title is { Length: > 0 } title ? title : null, info is null ? null : S(info["description"]), row?.Counts);
        var page = OverviewModel.ForClass(facts, OverviewSources(lectures, events: ClassEvents(name)));
        page.AddLinks(facts, () =>
        {
            Remember();
            allLectures = true;
            _ = ShowClassAsync(name);
        }, row is null ? null : tab =>
        {
            Remember();
            _ = ShowClassTabAsync(name, tab);
        });
        page.SetFolders(LinkedFolders(lib, name, info), host.Settings.LibraryHere ? () => _ = LinkFolderAsync(lib, name) : null);
        var files = new AttachmentsModel(lib, name, null);
        page.Files = files;
        _ = files.LoadAsync();
        library.Overview = page;
    }

    /// <summary>The folders linked to a class, as the library's overview lists them.</summary>
    static IEnumerable<LinkedFolder> LinkedFolders(RemoteLibrary lib, string cls, JsonObject? info) =>
        (info?["folders"] as JsonArray ?? []).OfType<JsonObject>().Select(f =>
        {
            string path = S(f["path"]);
            return new LinkedFolder(S(f["name"]), path, f["here"]?.GetValue<bool>() ?? true,
                () => Dialogs.OpenUrl(path),
                () => _ = ChangeFolderAsync(lib, cls, path, remove: true));
        });

    /// <summary>Link a folder: the folder dialog, then the library lists it on the class's home.</summary>
    static async Task LinkFolderAsync(RemoteLibrary lib, string cls)
    {
        if (mainWindow is null) return;
        var picked = await mainWindow.StorageProvider.OpenFolderPickerAsync(new Avalonia.Platform.Storage.FolderPickerOpenOptions
        {
            Title = $"Link a folder to {cls}",
        });
        if (picked.Count == 0 || picked[0].TryGetLocalPath() is not { } path) return;
        await ChangeFolderAsync(lib, cls, path, remove: false);
    }

    static async Task ChangeFolderAsync(RemoteLibrary lib, string cls, string path, bool remove)
    {
        try
        {
            await lib.ClassFolderAsync(cls, path, remove);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or LibraryRefusedException)
        {
            Toast("Couldn't link the folder", e.Message, null, null);
            return;
        }
        await host.CheckLibraryAsync();
        if (overviewOf == cls) await ShowClassHomeAsync(cls);
    }

    /// <summary>A class's Canvas page, on one of its tabs.</summary>
    static async Task ShowClassTabAsync(string name, ClassTab tab)
    {
        allLectures = false;
        await ShowClassAsync(name);
        if (openClass == name && library.CanvasClass is { } page) page.Tab = tab;
    }

    static async Task<List<OverviewModel.LectureFacts>> OverviewLecturesAsync(ILibrarySource lib, string? cls, int limit)
    {
        JsonArray list;
        try
        {
            list = await lib.LecturesAsync(cls, limit, null);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or LibraryRefusedException)
        {
            return [];
        }
        return [.. list.OfType<JsonObject>().Select(l => new OverviewModel.LectureFacts(S(l["id"]), S(l["title"]), S(l["class"]), Date(S(l["date"])),
            l["seconds"] is JsonValue v && v.TryGetValue(out double sec) ? sec : null, S(l["summary"]), S(l["status"]) is "queued" or "working"))];
    }

    /// <summary>This class on the student's calendars in the coming week (not only today and tomorrow, as the
    /// sidebar's Coming up is); null without calendars.</summary>
    static List<ComingUpRow>? ClassEvents(string name)
    {
        if (host.Settings.Role == AppRole.Library || !host.HasCalendars) return null;
        var now = DateTimeOffset.Now;
        var classes = host.CalendarClasses();
        var dot = DotFor(name);
        return [.. host.Calendars.Events
            .Where(e => !e.AllDay && !e.Cancelled && e.End > e.Start && e.End > now && e.Start < now + Upcoming.Ahead && EventClass.For(e.Title, classes) == name)
            .OrderBy(e => e.Start).Take(OverviewModel.Rows)
            .Select(e => new ComingUpRow(e.Id, CalendarWords.When(e, now, TimeZoneInfo.Local), e.Title, e.Location, name, dot, e.Start <= now))];
    }

    static OverviewModel.Sources OverviewSources(IReadOnlyList<OverviewModel.LectureFacts> lectures, IReadOnlyList<ComingUpRow>? events)
    {
        var ctx = Canvas();
        return new OverviewModel.Sources
        {
            Now = ctx.Clock.Now(),
            Zone = ctx.Clock.Zone,
            Classes = [.. host.Classes().Select(c => (c.Name, (IBrush)Skin.ClassDot(c.Color), c.Lectures))],
            Lectures = lectures,
            Due = CanvasLinked ? canvasDue : null,
            Canvas = canvasClasses,
            Events = events,
            Unsorted = host.Overview?["unsorted"] is JsonValue u && u.TryGetValue(out int unsorted) ? unsorted : 0,
            Writing = host.Overview?["writing"] is JsonValue w && w.TryGetValue(out int writing) ? writing : 0,
            OpenClass = cls =>
            {
                Remember();
                _ = ShowClassHomeAsync(cls);
            },
            OpenAllLectures = cls =>
            {
                Remember();
                allLectures = true;
                _ = ShowClassAsync(cls);
            },
            OpenLecture = (cls, id) =>
            {
                Remember();
                openLecture = id;
                allLectures = false;
                _ = cls.Length > 0 ? ShowClassAsync(cls) : OpenLectureAsync(id, transcript: false);
            },
            OpenAssignment = (cls, id) =>
            {
                Remember();
                // From a class's home, the assignment opens on its class's page; from Home, on the Due page.
                if (overviewOf is { Length: > 0 } && CanvasClassRow(cls) is not null) _ = OpenClassAssignmentAsync(cls, id);
                else
                {
                    dueSelection = (cls, id);
                    _ = ShowDueAsync();
                }
            },
            OpenDueList = () =>
            {
                Remember();
                dueSelection = null;
                _ = ShowDueAsync();
            },
            OpenUnsorted = () =>
            {
                Remember();
                _ = ShowClassAsync(Configs.Unsorted);
            },
        };
    }

    static async Task OpenClassAssignmentAsync(string cls, string id)
    {
        await ShowClassTabAsync(cls, ClassTab.Assignments);
        if (openClass != cls) return;
        openAssignment = (cls, id);
        await ShowAssignmentAsync(cls, id);
    }
}
