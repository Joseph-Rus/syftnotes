using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StudyStash.App.Services;

namespace StudyStash.App.ViewModels;

/// <summary>One rubric row: its criterion, the points it's worth (or how it was marked), and the grader's comment,
/// already quoted the way the design sets it.</summary>
public sealed record RubricRow(string Criterion, string Right, string? Comment);

/// <summary>One file on an assignment or its submission: its glyph (by file type), name and size, and how to open it
/// (downloaded into the library's cache the first time, then handed to the OS).</summary>
public sealed partial class FileChip : ObservableObject
{
    public required string Name { get; init; }
    /// <summary>The path the library's files API knows it by (its folder, if any, plus its name).</summary>
    public required string Path { get; init; }
    public string SizeText { get; init; } = "";
    public string Glyph { get; init; } = "draft";
    /// <summary>The file on Canvas, for when the library hasn't saved it.</summary>
    public string? Url { get; init; }

    [ObservableProperty] public partial bool Opening { get; set; }

    /// <summary>Set by <see cref="AssignmentModel"/> when it builds the chip.</summary>
    public Action<FileChip>? OnOpen { get; set; }

    [RelayCommand]
    void Open() => OnOpen?.Invoke(this);
}

/// <summary>
/// One assignment's detail (design 09/10): its meta, three tiles, instructions, rubric and submission. Downloads a
/// file into the library's cache under the student's home the first time it's opened, then just opens the saved copy
/// — <see cref="LoadAsync"/> is the only place that talks to the library.
/// </summary>
public sealed partial class AssignmentModel(CanvasContext context) : ObservableObject
{
    string cls = "";
    string folder = "";

    [ObservableProperty] public partial string Meta { get; set; } = "";
    [ObservableProperty] public partial IBrush Dot { get; set; } = Brushes.Transparent;
    [ObservableProperty] public partial string Title { get; set; } = "";

    [ObservableProperty] public partial string DueValue { get; set; } = "";
    [ObservableProperty] public partial string PointsValue { get; set; } = "";
    [ObservableProperty] public partial string ThirdLabel { get; set; } = "Status";
    [ObservableProperty] public partial string ThirdValue { get; set; } = "";

    [ObservableProperty] public partial string Instructions { get; set; } = "";
    public ObservableCollection<RubricRow> Rubric { get; } = [];
    public bool HasRubric => Rubric.Count > 0;

    [ObservableProperty] public partial string SubmissionStatus { get; set; } = "";
    [ObservableProperty] public partial string SubmissionDetail { get; set; } = "";
    public bool HasSubmissionDetail => SubmissionDetail.Length > 0;
    /// <summary>The start of what was typed into Canvas for a text entry, so the student sees what they sent.</summary>
    [ObservableProperty] public partial string SubmissionBody { get; set; } = "";
    public bool HasSubmissionBody => SubmissionBody.Length > 0;
    [ObservableProperty] public partial string? CommentText { get; set; }
    [ObservableProperty] public partial string? CommentAuthor { get; set; }
    public bool HasComment => !string.IsNullOrEmpty(CommentText);
    [ObservableProperty] public partial bool ShowHandInLink { get; set; }
    /// <summary>A planner to-do (a page or note Canvas flagged), not an assignment: nothing to score or hand in.</summary>
    [ObservableProperty] public partial bool IsTodo { get; set; }
    public bool IsAssignment => !IsTodo;
    /// <summary>"Your submission" shows when there's something to say about one: not for a to-do, nor for an
    /// assignment that takes nothing through Canvas (no submission, on paper, not graded) and has none.</summary>
    [ObservableProperty] public partial bool ShowSubmission { get; set; }
    public ObservableCollection<FileChip> Files { get; } = [];
    public bool HasFiles => Files.Count > 0;

    string? url;

    /// <summary>The host's global search, opened from the detail toolbar's search button.</summary>
    public Action? OnSearch { get; set; }

    /// <summary>Shapes every field from the library's answer.</summary>
    public void Show(CanvasApi.AssignmentDetail d)
    {
        var zone = context.Clock.Zone;
        var now = context.Clock.Now();
        cls = d.Class;
        folder = d.Folder ?? "";
        url = d.Url;

        Dot = context.DotOf(d.Class);
        Meta = CanvasWords.DetailHeader(d.Class, d.Kind);
        Title = d.Name;

        DueValue = d.DueAt is { } due ? CanvasWords.Full(due, zone, now) : "No due date";
        PointsValue = CanvasWords.PointsText(d.Points);
        // Canvas sends a submission for every assignment, "unsubmitted" until something's handed in: only one with
        // something in it (a time, a grade, a state past unsubmitted) is the student's.
        var handedIn = d.Submission is { } given && (given.SubmittedAt is not null || given.GradedAt is not null
            || given.State is { Length: > 0 } state && state != "unsubmitted") ? given : null;
        bool closed = handedIn is null && !d.Excused && d.LockAt is { } lockAt && lockAt < now;
        bool graded = d.Status == "graded" || d.GradedAt is not null;
        ThirdLabel = graded ? "Score" : "Status";
        ThirdValue = graded ? CanvasWords.ScoreOrGradeText(d.Score, d.Points, d.Grade, d.GradingType)
            : closed ? "Closed" : handedIn is not null && d.Label == "To do" ? "Submitted" : d.Label;

        Instructions = d.Instructions ?? "";

        Rubric.Clear();
        foreach (var r in d.Rubric)
            Rubric.Add(new RubricRow(r.Criterion, CanvasWords.RubricPoints(r), r.Mark?.Comment is { Length: > 0 } c ? CanvasWords.Quote(c) : null));
        OnPropertyChanged(nameof(HasRubric));

        var copy = closed ? new CanvasWords.SubmissionCopy("Not handed in", $"Closed {CanvasWords.Full(d.LockAt!.Value, zone, now)}")
            : CanvasWords.SubmissionText(handedIn, d.Score, d.Points, d.Grade, d.GradingType, d.Excused, d.DueAt, zone, now);
        SubmissionStatus = copy.Status;
        SubmissionDetail = handedIn?.Attempt is > 1 && copy.Detail.Length > 0 ? $"{copy.Detail} · attempt {handedIn.Attempt}" : copy.Detail;
        OnPropertyChanged(nameof(HasSubmissionDetail));
        SubmissionBody = CanvasWords.BodyPreview(handedIn?.Body);
        OnPropertyChanged(nameof(HasSubmissionBody));

        var comment = d.Comments.Count > 0 ? d.Comments[^1] : null;
        CommentText = comment?.Text;
        CommentAuthor = comment?.Author;
        OnPropertyChanged(nameof(HasComment));

        bool takesNothing = d.SubmissionTypes.Count > 0 && d.SubmissionTypes.All(t => t is "none" or "on_paper" or "not_graded");
        IsTodo = false;
        ShowSubmission = !(takesNothing && handedIn is null && !d.Excused);
        ShowHandInLink = handedIn is null && !d.Excused && !takesNothing && !closed;

        Files.Clear();
        var source = handedIn is { Files.Count: > 0 } sub ? sub.Files : d.Files;
        foreach (var f in source)
        {
            var chip = new FileChip { Name = f.Name, SizeText = CanvasWords.Size(f.Size), Glyph = FileGlyph(f.Format), Path = f.Local is { Length: > 0 } saved ? saved : CombinePath(folder, f.Name), Url = f.Url };
            chip.OnOpen = c => _ = OpenFileAsync(c);
            Files.Add(chip);
        }
        OnPropertyChanged(nameof(HasFiles));
    }

    /// <summary>A planner to-do's page, from its Due row: when it's for, and a way to open it in Canvas. It has no
    /// assignment behind it, so no points, status or submission.</summary>
    public void ShowTodo(CanvasApi.Item item)
    {
        var zone = context.Clock.Zone;
        var now = context.Clock.Now();
        cls = item.Class;
        folder = "";
        url = item.Url;
        Dot = context.DotOf(item.Class);
        Meta = CanvasWords.DetailHeader(item.Class, "todo");
        Title = item.Name;
        DueValue = item.DueAt is { } due ? CanvasWords.Full(due, zone, now) : "No date";
        PointsValue = ThirdValue = "";
        Instructions = "";
        Rubric.Clear();
        OnPropertyChanged(nameof(HasRubric));
        SubmissionStatus = SubmissionDetail = SubmissionBody = "";
        OnPropertyChanged(nameof(HasSubmissionDetail));
        OnPropertyChanged(nameof(HasSubmissionBody));
        CommentText = CommentAuthor = null;
        OnPropertyChanged(nameof(HasComment));
        Files.Clear();
        OnPropertyChanged(nameof(HasFiles));
        IsTodo = true;
        ShowSubmission = ShowHandInLink = false;
    }

    public const string TodoText = "A to-do from your Canvas planner. There's nothing to hand in: open it in Canvas to read it, and mark it done there.";

    partial void OnIsTodoChanged(bool value) => OnPropertyChanged(nameof(IsAssignment));

    static string CombinePath(string folder, string name) => folder.Length > 0 ? $"{folder}/{name}" : name;

    /// <summary>The chip's icon by the file's format: a document type the design draws its own way, code by
    /// extension, everything else a plain draft page.</summary>
    static string FileGlyph(string? format) => format?.ToLowerInvariant() switch
    {
        "pdf" => "picture_as_pdf",
        "py" or "js" or "ts" or "java" or "c" or "cpp" or "cs" or "ipynb" or "rb" or "go" => "code",
        "png" or "jpg" or "jpeg" or "gif" or "heic" or "webp" => "image",
        "ppt" or "pptx" or "key" => "slideshow",
        "xls" or "xlsx" or "csv" => "table_chart",
        "doc" or "docx" or "txt" or "md" => "description",
        _ => "draft",
    };

    /// <summary>False when the library has no such assignment (a to-do, or one gone since the list was read).</summary>
    public async Task<bool> LoadAsync(string classId, string id, CancellationToken stop = default)
    {
        if (context.Client is not { } client) return false;
        if (await client.AssignmentAsync(classId, id, stop) is not { } detail) return false;
        Show(detail);
        return true;
    }

    async Task OpenFileAsync(FileChip chip)
    {
        chip.Opening = true;
        try
        {
            string dest = System.IO.Path.Combine(context.Home, "cache", "canvas", cls, chip.Path.Replace('/', System.IO.Path.DirectorySeparatorChar));
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(dest)!);
            if (context.Client is { } client && await client.DownloadAsync(cls, chip.Path, dest))
                context.Actions.OpenFile(dest);
            else if (chip.Url is { Length: > 0 } fileUrl) context.Actions.OpenInBrowser(fileUrl);
            else if (url is { Length: > 0 }) context.Actions.OpenInBrowser(url);
        }
        catch (Exception e) when (e is HttpRequestException or CanvasLibraryException or System.IO.IOException or TaskCanceledException)
        {
            if (url is { Length: > 0 }) context.Actions.OpenInBrowser(url);
        }
        finally
        {
            chip.Opening = false;
        }
    }

    /// <summary>A link in the instructions: a web address opens in the browser (Canvas's in the one the student reads Canvas in); one into the
    /// assignment's own saved files ("files/slides.pdf", "../Lab 1/files/policy.pdf") opens the library's copy, or
    /// the assignment on Canvas when the library hasn't got it.</summary>
    public Action<string> LinkHandler => OpenLink;

    public void OpenLink(string link)
    {
        if (Uri.TryCreate(link, UriKind.Absolute, out var abs) && abs.Scheme is "http" or "https" or "mailto")
        {
            Controls.NoteView.OpenLink(link);
            return;
        }
        string path = ResolveRelative(folder, Uri.UnescapeDataString(link.Split('#', '?')[0]));
        var chip = new FileChip { Name = System.IO.Path.GetFileName(path), Path = path };
        _ = OpenFileAsync(chip);
    }

    /// <summary><paramref name="relative"/> (with its ./ and ../) against <paramref name="baseFolder"/>, as the
    /// library's forward-slash path.</summary>
    static string ResolveRelative(string baseFolder, string relative)
    {
        var parts = new List<string>(baseFolder.Split('/', StringSplitOptions.RemoveEmptyEntries));
        foreach (var piece in relative.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (piece == ".") continue;
            if (piece == "..") { if (parts.Count > 0) parts.RemoveAt(parts.Count - 1); continue; }
            parts.Add(piece);
        }
        return string.Join('/', parts);
    }

    [RelayCommand]
    void OpenInCanvas()
    {
        if (url is { Length: > 0 } u) context.Actions.OpenUrl(u);
    }

    [RelayCommand]
    void HandIn()
    {
        if (url is { Length: > 0 } u) context.Actions.OpenUrl(u);
    }

    [RelayCommand]
    void Search() => OnSearch?.Invoke();
}
