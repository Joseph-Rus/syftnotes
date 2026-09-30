using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StudyStash.App.Services;

namespace StudyStash.App.ViewModels;

/// <summary>Which shape the class page draws in: tabs (a wide, 3-pane window) or one scrolling list of sections
/// (a narrow window with no separate detail column) — the host decides, from the window's width.</summary>
public enum ClassLayout { Tabs, Sections }

/// <summary>The tabs layout's own four tabs.</summary>
public enum ClassTab { Lectures, Assignments, Modules, Announcements }

/// <summary>One lecture row, exactly as the shell already has it (its title and day) — <see cref="CanvasClassModel"/>
/// never asks the library for lectures.</summary>
public sealed record LectureRow(string Title, string When, Action Open);

/// <summary>One item inside a module: its glyph (by kind, and by format for a file), its title, the word for where
/// it stands (§1's <see cref="CanvasWords.ModuleItemWord"/>), and how to open it.</summary>
public sealed record ModuleItemRow(string Glyph, string Title, string Right, Action Open);

/// <summary>One file under a class's Files section. <see cref="FolderHeader"/> is set only on the first row of a
/// new folder, so the view can draw that folder's grey label just above it.</summary>
public sealed record FileRow(string Name, string SizeText, string? FolderHeader, Action Open);

/// <summary>One module, collapsed to its name and item count until the student opens it (the newest — the one
/// still "started" — opens on its own).</summary>
public sealed partial class ModuleNode : ObservableObject
{
    public required string Name { get; init; }
    public required string ItemsCountText { get; init; }
    [ObservableProperty] public partial bool Expanded { get; set; }
    public ObservableCollection<ModuleItemRow> Items { get; } = [];

    [RelayCommand]
    void Toggle() => Expanded = !Expanded;
}

/// <summary>One announcement: its title, day and whether it's new — reading it clears <see cref="New"/> here and,
/// through <see cref="CanvasClassModel"/>, in the section's own count.</summary>
public sealed partial class AnnouncementRow : ObservableObject
{
    public required string Title { get; init; }
    public required string When { get; init; }
    [ObservableProperty] public partial bool New { get; set; }
    public Action<AnnouncementRow>? OnOpen { get; set; }

    [RelayCommand]
    void Open() => OnOpen?.Invoke(this);
}

/// <summary>
/// A Canvas-linked class's own page (design 10 tabs, design 11 sections): its header and Scout line, the to-hand-in
/// and done assignments, its recent lectures, and its modules, files and announcements. Never fetches lectures —
/// <see cref="SetLectures"/> is fed whatever the shell already has for this class.
/// </summary>
public sealed partial class CanvasClassModel(CanvasContext context) : ObservableObject
{
    string cls = "";
    string? code;
    string name = "";
    int lectureTotal;
    DueRow? selectedAssignment;

    [ObservableProperty] public partial string Title { get; set; } = "";
    [ObservableProperty] public partial string HeaderLine { get; set; } = "";
    [ObservableProperty] public partial string ScoutLine { get; set; } = "";
    public bool HasScoutLine => ScoutLine.Length > 0;

    [ObservableProperty] public partial ClassLayout Layout { get; set; } = ClassLayout.Tabs;
    public bool IsTabsLayout => Layout == ClassLayout.Tabs;
    public bool IsSectionsLayout => Layout == ClassLayout.Sections;
    partial void OnLayoutChanged(ClassLayout value)
    {
        OnPropertyChanged(nameof(IsTabsLayout));
        OnPropertyChanged(nameof(IsSectionsLayout));
    }

    [ObservableProperty] public partial ClassTab Tab { get; set; } = ClassTab.Lectures;
    public bool IsLecturesTab => Tab == ClassTab.Lectures;
    public bool IsAssignmentsTab => Tab == ClassTab.Assignments;
    public bool IsModulesTab => Tab == ClassTab.Modules;
    public bool IsAnnouncementsTab => Tab == ClassTab.Announcements;
    partial void OnTabChanged(ClassTab value)
    {
        OnPropertyChanged(nameof(IsLecturesTab));
        OnPropertyChanged(nameof(IsAssignmentsTab));
        OnPropertyChanged(nameof(IsModulesTab));
        OnPropertyChanged(nameof(IsAnnouncementsTab));
    }

    public ObservableCollection<DueRow> ToHandIn { get; } = [];
    public bool HasToHandIn => ToHandIn.Count > 0;
    public ObservableCollection<DueRow> Done { get; } = [];

    public ObservableCollection<LectureRow> RecentLectures { get; } = [];
    [ObservableProperty] public partial string AllLecturesText { get; set; } = "";
    public Action? OnAllLectures { get; set; }

    [ObservableProperty] public partial int ModulesCount { get; set; }
    public ObservableCollection<ModuleNode> Modules { get; } = [];
    [ObservableProperty] public partial bool ModulesExpanded { get; set; } = true;

    /// <summary>The Sections layout's own compact preview — the current module (open) and the one just before it
    /// (collapsed), newest first, the way the design draws it (the Modules tab, in the Tabs layout, still lists
    /// every module).</summary>
    public ObservableCollection<ModuleNode> RecentModules { get; } = [];

    [ObservableProperty] public partial int FilesCount { get; set; }
    public ObservableCollection<FileRow> Files { get; } = [];
    [ObservableProperty] public partial bool FilesExpanded { get; set; }

    [ObservableProperty] public partial int AnnouncementsCount { get; set; }
    [ObservableProperty] public partial int AnnouncementsNew { get; set; }
    [ObservableProperty] public partial string AnnouncementsCountText { get; set; } = "";
    public bool HasAnnouncementsNew => AnnouncementsNew > 0;
    public ObservableCollection<AnnouncementRow> Announcements { get; } = [];
    [ObservableProperty] public partial bool AnnouncementsExpanded { get; set; }

    /// <summary>The host opens the item's detail (Assignment) when a to-hand-in/done row, or a module's assignment
    /// item, is picked.</summary>
    public Action<string, string>? OnAssignment { get; set; }

    /// <summary>The host shows a page or announcement's reader (in the detail column, or its own pane).</summary>
    public Action<CanvasReaderModel>? OnReader { get; set; }

    /// <summary>Shapes every field from the library's answers. Never asks for lectures.</summary>
    public void Show(CanvasApi.ClassRow classInfo, CanvasApi.AssignmentsResponse assignments, CanvasApi.ModulesResponse modules,
        CanvasApi.FilesResponse files, CanvasApi.AnnouncementsResponse announcements)
    {
        var zone = context.Clock.Zone;
        var now = context.Clock.Now();
        cls = classInfo.Class;
        code = classInfo.Canvas is { } course
            ? course.ShortCode.Length > 0 ? course.ShortCode : StudyStash.Core.Canvas.CourseNames.ShortCode(course.Code, course.Name)
            : null;
        name = classInfo.Canvas is { } c ? c.Title.Length > 0 ? c.Title : StudyStash.Core.Canvas.CourseNames.Title(c.Name, c.Code) : cls;
        Title = cls;
        UpdateHeaderLine();
        ScoutLine = classInfo.Scout is { State.Length: > 0 } s ? CanvasWords.ScoutHeaderLine(s, zone) : "";
        OnPropertyChanged(nameof(HasScoutLine));

        ToHandIn.Clear();
        foreach (var item in assignments.ToHandIn) ToHandIn.Add(MakeAssignmentRow(item, zone, now));
        OnPropertyChanged(nameof(HasToHandIn));

        Done.Clear();
        foreach (var item in assignments.Done) Done.Add(MakeAssignmentRow(item, zone, now));

        ModulesCount = modules.Count;
        Modules.Clear();
        foreach (var m in modules.Modules)
        {
            var node = new ModuleNode { Name = m.Name, ItemsCountText = CanvasWords.ItemsCountText(m.ItemsCount), Expanded = m.State == "started" };
            foreach (var it in m.Items) node.Items.Add(new ModuleItemRow(ModuleGlyph(it), it.Title, CanvasWords.ModuleItemWord(it), () => _ = OpenModuleItemAsync(it)));
            Modules.Add(node);
        }
        RecentModules.Clear();
        foreach (var node in RecentModuleNodes()) RecentModules.Add(node);

        FilesCount = files.Count;
        Files.Clear();
        string? lastFolder = null;
        foreach (var f in files.Files)
        {
            string? header = f.Folder != lastFolder ? f.Folder : null;
            lastFolder = f.Folder;
            string path = f.Local is { Length: > 0 } saved ? saved : CombinePath(f.Folder, f.Name);
            Files.Add(new FileRow(f.Name, CanvasWords.Size(f.Size), header, () => _ = OpenSavedFileAsync(path)));
        }

        AnnouncementsCount = announcements.Count;
        AnnouncementsNew = announcements.New;
        AnnouncementsCountText = CanvasWords.AnnouncementsCountText(AnnouncementsCount, AnnouncementsNew);
        OnPropertyChanged(nameof(HasAnnouncementsNew));
        Announcements.Clear();
        announcementItems.Clear();
        foreach (var a in announcements.Items)
        {
            var row = new AnnouncementRow { Title = a.Title, When = CanvasWords.Day(a.PostedAt, zone), New = a.New };
            var apiItem = a;
            row.OnOpen = _ => OpenAnnouncement(row, apiItem);
            Announcements.Add(row);
            announcementItems[a.Id] = (row, a);
        }
    }

    /// <summary>The lectures the shell already has for this class: the newest few, plus how many there are in all
    /// ("All 12 lectures").</summary>
    public void SetLectures(IReadOnlyList<LectureRow> recent, int total)
    {
        lectureTotal = total;
        RecentLectures.Clear();
        foreach (var r in recent) RecentLectures.Add(r);
        AllLecturesText = total == 1 ? "All 1 lecture" : $"All {total} lectures";
        UpdateHeaderLine();
    }

    void UpdateHeaderLine() => HeaderLine = $"{CanvasWords.LectureCountText(lectureTotal)} · {CanvasWords.ClassCanvasLine(code, name)}";

    /// <summary>The current (still "started") module first, then the one right before it — or, when nothing's in
    /// progress, the newest two — the compact preview the Sections layout draws under "Modules".</summary>
    IEnumerable<ModuleNode> RecentModuleNodes()
    {
        if (Modules.Count == 0) yield break;
        int current = Modules.ToList().FindLastIndex(m => m.Expanded);
        if (current < 0) current = Modules.Count - 1;
        yield return Modules[current];
        int previous = current > 0 ? current - 1 : current + 1 < Modules.Count ? current + 1 : -1;
        if (previous >= 0) yield return Modules[previous];
    }

    DueRow MakeAssignmentRow(CanvasApi.Item item, TimeZoneInfo zone, DateTimeOffset now)
    {
        var row = new DueRow
        {
            Class = item.Class, Id = item.Id, Title = item.Name,
            Right = CanvasWords.RightLabel(item, zone, now), Strong = item.Missing,
            Sub = CanvasWords.ClassTabRow(item, zone, now), Dot = context.DotOf(item.Class),
        };
        row.OnSelectRow = SelectAssignment;
        return row;
    }

    void SelectAssignment(DueRow row)
    {
        if (selectedAssignment is { } prior) prior.Selected = false;
        row.Selected = true;
        selectedAssignment = row;
        OnAssignment?.Invoke(row.Class, row.Id);
    }

    /// <summary>The design draws a saved Drive link with its own file's icon, but a saved Box link keeps the plain
    /// link glyph — a quirk of the mock, not a rule; matched here so the shot lines up with it.</summary>
    static string ModuleGlyph(CanvasApi.ModuleItem item) => item.Kind switch
    {
        "file" => FormatGlyph(item.Format),
        "page" => "description",
        "link" => item.Source == "box" ? "link" : FormatGlyph(item.Format),
        "assignment" => "assignment",
        "quiz" => "quiz",
        "discussion" => "forum",
        "tool" => "extension",
        _ => "description",
    };

    static string FormatGlyph(string? format) => format?.ToLowerInvariant() switch
    {
        "pdf" => "picture_as_pdf",
        "ppt" or "pptx" or "key" => "slideshow",
        "xls" or "xlsx" or "csv" => "table_chart",
        "png" or "jpg" or "jpeg" or "gif" or "heic" or "webp" => "image",
        _ => "description",
    };

    static string CombinePath(string? folder, string name) => string.IsNullOrEmpty(folder) ? name : $"{folder}/{name}";

    async Task OpenModuleItemAsync(CanvasApi.ModuleItem item)
    {
        if (item.Locked || item.Skipped) return;
        switch (item.Kind)
        {
            case "assignment" when item.AssignmentId is { Length: > 0 } id:
                OnAssignment?.Invoke(cls, id);
                break;
            case "page":
                if (item.Saved && context.Client is { } client)
                {
                    string? text = await client.TextAsync(cls, item.Title);
                    var reader = new CanvasReaderModel(context);
                    reader.ShowPage(cls, item.Title, text ?? "", item.Url);
                    OnReader?.Invoke(reader);
                }
                else if (item.Url is { Length: > 0 } pageUrl) context.Actions.OpenUrl(pageUrl);
                break;
            case "link":
                if (item.Saved && await OpenSavedFileAsync(item.Local is { Length: > 0 } savedLink ? savedLink : item.Title)) break;
                if (item.ExternalUrl is { Length: > 0 } externalUrl) context.Actions.OpenUrl(externalUrl);
                break;
            case "file":
                // The library's copy when the sync saved one; otherwise the file itself on Canvas, in the Chrome
                // that's signed in to it (a slide deck too big to save, or one not synced yet).
                if (item.Local is not null && await OpenSavedFileAsync(item.Local.Length > 0 ? item.Local : item.Title)) break;
                if (item.Url is { Length: > 0 } fileUrl) context.Actions.OpenInChrome(fileUrl);
                break;
            default:
                if (item.Url is { Length: > 0 } url) context.Actions.OpenUrl(url);
                break;
        }
    }

    /// <summary>Opens the library's copy of a file (<paramref name="path"/> is relative to the class's folder, as the
    /// library gives it); false when the library hasn't got it, so the caller can fall back to Canvas.</summary>
    async Task<bool> OpenSavedFileAsync(string path)
    {
        if (context.Client is not { } client) return false;
        string dest = Path.Combine(context.Home, "cache", "canvas", cls, path.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        try
        {
            if (!await client.DownloadAsync(cls, path, dest)) return false;
        }
        catch (Exception e) when (e is HttpRequestException or CanvasLibraryException or IOException or TaskCanceledException)
        {
            return false;
        }
        context.Actions.OpenFile(dest);
        return true;
    }

    /// <summary>Opens the announcement with Canvas id <paramref name="id"/> in its reader, on the Announcements tab
    /// (a notification's Open); false when this class hasn't got it (any more).</summary>
    public bool OpenAnnouncement(string id)
    {
        if (!announcementItems.TryGetValue(id, out var found)) return false;
        Tab = ClassTab.Announcements;
        OpenAnnouncement(found.Row, found.Item);
        return true;
    }

    readonly Dictionary<string, (AnnouncementRow Row, CanvasApi.AnnouncementRow Item)> announcementItems = [];

    void OpenAnnouncement(AnnouncementRow row, CanvasApi.AnnouncementRow a)
    {
        var reader = new CanvasReaderModel(context);
        reader.ShowAnnouncement(a);
        OnReader?.Invoke(reader);
        if (!row.New) return;
        row.New = false;
        AnnouncementsNew = Math.Max(0, AnnouncementsNew - 1);
        AnnouncementsCountText = CanvasWords.AnnouncementsCountText(AnnouncementsCount, AnnouncementsNew);
        OnPropertyChanged(nameof(HasAnnouncementsNew));
        if (context.Client is { } client) _ = client.MarkAnnouncementsSeenAsync(cls, [a.Id]);
    }

    public async Task LoadAsync(CanvasApi.ClassRow classInfo, CancellationToken stop = default)
    {
        if (context.Client is not { } client)
        {
            Show(classInfo, new CanvasApi.AssignmentsResponse { Class = classInfo.Class }, new CanvasApi.ModulesResponse(), new CanvasApi.FilesResponse(), new CanvasApi.AnnouncementsResponse());
            return;
        }
        var assignmentsTask = client.AssignmentsAsync(classInfo.Class, stop);
        var modulesTask = client.ModulesAsync(classInfo.Class, stop);
        var filesTask = client.FilesAsync(classInfo.Class, stop);
        var announcementsTask = client.AnnouncementsAsync(classInfo.Class, stop);
        await Task.WhenAll(assignmentsTask, modulesTask, filesTask, announcementsTask);
        Show(classInfo,
            await assignmentsTask ?? new CanvasApi.AssignmentsResponse { Class = classInfo.Class },
            await modulesTask ?? new CanvasApi.ModulesResponse(),
            await filesTask ?? new CanvasApi.FilesResponse(),
            await announcementsTask ?? new CanvasApi.AnnouncementsResponse());
    }

    [RelayCommand]
    void SelectTab(ClassTab tab) => Tab = tab;

    [RelayCommand]
    void AllLectures() => OnAllLectures?.Invoke();

    [RelayCommand]
    void OpenLecture(LectureRow row) => row.Open();

    [RelayCommand]
    void OpenModuleItemRow(ModuleItemRow row) => row.Open();

    [RelayCommand]
    void OpenFileRow(FileRow row) => row.Open();

    [RelayCommand]
    void ToggleModules() => ModulesExpanded = !ModulesExpanded;

    [RelayCommand]
    void ToggleFiles() => FilesExpanded = !FilesExpanded;

    [RelayCommand]
    void ToggleAnnouncements() => AnnouncementsExpanded = !AnnouncementsExpanded;
}
