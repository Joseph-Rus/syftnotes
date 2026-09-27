using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace StudyStash.App.ViewModels;

/// <summary>A class in the sidebar: its dot, name and how many lectures (Unsorted has an inbox instead of a dot).</summary>
public sealed partial class ClassItem : ObservableObject
{
    public string Name { get; init; } = "";
    public IBrush Dot { get; init; } = Brushes.Gray;
    public bool IsUnsorted { get; init; }
    /// <summary>Not a class: what's due soon in every class, from Canvas.</summary>
    public bool IsDue { get; init; }
    [ObservableProperty] public partial int Count { get; set; }
    [ObservableProperty] public partial bool Selected { get; set; }
    public bool HasDot => !IsUnsorted;
}

/// <summary>A lecture in the middle column: title, "Tue 23 Sep · 1 h 12 min", and the lecture in a sentence.</summary>
public sealed partial class LectureCard : ObservableObject
{
    public string Id { get; init; } = "";
    public string Title { get; init; } = "";
    public string Meta { get; init; } = "";
    public string Summary { get; init; } = "";
    public bool Last { get; init; }
    [ObservableProperty] public partial bool Selected { get; set; }
    public bool HasSummary => Summary.Length > 0;
}

/// <summary>Lectures under a heading: "This week", "Last week", "September".</summary>
public sealed class LectureGroup
{
    public string Label { get; init; } = "";
    public bool First { get; init; }
    public ObservableCollection<LectureCard> Items { get; } = [];
}

/// <summary>The lecture open on the right: its notes (Markdown) or its transcript.</summary>
public sealed partial class NoteModel : ObservableObject
{
    public string Id { get; init; } = "";
    public string ClassName { get; init; } = "";
    public IBrush Dot { get; init; } = Brushes.Gray;
    /// <summary>"CS 101 · Tuesday 23 September · 1 h 12 min".</summary>
    public string Meta { get; init; } = "";
    public string Title { get; init; } = "";
    public string Markdown { get; init; } = "";
    /// <summary>The notes aren't written yet (the library is on it), or couldn't be.</summary>
    public string? Pending { get; init; }
    public ObservableCollection<HeardLine> Transcript { get; } = [];
    [ObservableProperty] public partial bool ShowTranscript { get; set; }

    public bool ShowNotes => !ShowTranscript;
    public bool HasPending => !string.IsNullOrEmpty(Pending);
    public bool NoTranscript => Transcript.Count == 0;

    partial void OnShowTranscriptChanged(bool value) => OnPropertyChanged(nameof(ShowNotes));

    [RelayCommand] void Notes() => ShowTranscript = false;
    [RelayCommand] void Transcripts() => ShowTranscript = true;
}

/// <summary>What the middle column lists: a class's lectures by week, Canvas's Due list, or a class linked to
/// Canvas (its tabs, or its sections in a narrow window).</summary>
public enum LibraryList { Lectures, Due, CanvasClass }

/// <summary>
/// The full app: classes (and Due) on the left, a class's lectures by week (or Canvas's lists) in the middle, and on
/// the right the lecture (its notes, which another engine can rewrite, and an ask bar floating over it), an
/// assignment, or a Canvas page. Below 900 px there's no room for the right column: the list fills the window, and
/// opening something shows it in the list's place, with a way back.
/// </summary>
public sealed partial class LibraryModel : ObservableObject
{
    /// <summary>Below this width the window shows one column after the sidebar, not two.</summary>
    public const double NarrowBelow = 900;

    public ObservableCollection<ClassItem> Classes { get; } = [];
    /// <summary>What's due soon, if Canvas has anything: the sidebar draws it above "Classes".</summary>
    public ClassItem? Due => Classes.FirstOrDefault(c => c.IsDue);
    public bool HasDue => Due is not null;
    [ObservableProperty] public partial ClassItem? Unsorted { get; set; }
    public ObservableCollection<LectureGroup> Groups { get; } = [];
    [ObservableProperty] public partial string ClassTitle { get; set; } = "";
    [ObservableProperty] public partial string ClassCount { get; set; } = "";
    [ObservableProperty] public partial NoteModel? Note { get; set; }
    [ObservableProperty] public partial string Status { get; set; } = "";
    [ObservableProperty] public partial bool StatusGood { get; set; } = true;
    [ObservableProperty] public partial string SearchText { get; set; } = "";
    /// <summary>Draw the window's own buttons (screenshots; a real window has the system's).</summary>
    [ObservableProperty] public partial bool DrawChrome { get; set; }
    /// <summary>The class has no lectures yet, or nothing is chosen: what the middle column says.</summary>
    [ObservableProperty] public partial string? Empty { get; set; }

    [ObservableProperty] public partial LibraryList List { get; set; } = LibraryList.Lectures;
    /// <summary>Canvas's Due list, while Due is open.</summary>
    [ObservableProperty] public partial CanvasDueModel? DueList { get; set; }
    /// <summary>What's wrong with Canvas (not set up, signed out, Chrome away…), atop the Due list; null while it's
    /// connected or syncing.</summary>
    [ObservableProperty] public partial CanvasStatusModel? DueStatus { get; set; }
    public bool HasDueStatus => DueStatus is not null;
    /// <summary>A class linked to Canvas, while it's open.</summary>
    [ObservableProperty] public partial CanvasClassModel? CanvasClass { get; set; }
    /// <summary>An assignment open on the right (from Due, or a class's own lists); null shows the lecture.</summary>
    [ObservableProperty] public partial AssignmentModel? Assignment { get; set; }
    /// <summary>A Canvas page or announcement open on the right; null shows the lecture.</summary>
    [ObservableProperty] public partial CanvasReaderModel? Reader { get; set; }
    /// <summary>The open lecture's notes, with "Rewrite notes"; null while the notes aren't written yet.</summary>
    [ObservableProperty] public partial AiNotesModel? Notes { get; set; }
    /// <summary>The ask bar under the open lecture, with its engine picker.</summary>
    [ObservableProperty] public partial AiAskModel? Ask { get; set; }

    /// <summary>The window is too narrow for the right column (below <see cref="NarrowBelow"/>).</summary>
    [ObservableProperty] public partial bool Narrow { get; set; }
    /// <summary>Narrow, and showing what was opened in the list's place.</summary>
    [ObservableProperty] public partial bool NarrowDetail { get; set; }

    public bool ShowLectures => List == LibraryList.Lectures;
    public bool ShowDueList => List == LibraryList.Due;
    public bool ShowCanvasClass => List == LibraryList.CanvasClass;

    public bool ShowAssignment => Assignment is not null;
    public bool ShowReader => Assignment is null && Reader is not null;
    /// <summary>The lecture's page, with its toolbar and ask bar: whenever no assignment or Canvas page is open.</summary>
    public bool ShowLecturePage => Assignment is null && Reader is null;
    public bool HasNotes => Notes is not null;
    public bool HasAsk => Ask is not null && HasNote && ShowLecturePage;

    /// <summary>The list column: the design's 312 (Mac) or 320 (Windows) for lectures, 340 for Canvas's lists (360
    /// for a Windows class page), and the whole window when it's narrow.</summary>
    public double ListWidth => Narrow ? double.NaN : List switch
    {
        LibraryList.Lectures => Skin.Current == SkinKind.Mac ? 312 : 320,
        LibraryList.CanvasClass when Skin.Current == SkinKind.Win => 360,
        _ => 340,
    };
    public bool ShowListColumn => !Narrow || !NarrowDetail;
    public bool ShowDetailColumn => !Narrow || NarrowDetail;
    /// <summary>Where the right column sits: its own column, or the list's when it's narrow.</summary>
    public int DetailColumn => Narrow ? 1 : 2;
    public int ColumnSpan => Narrow ? 2 : 1;

    public LibraryModel()
    {
        Classes.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(Due));
            OnPropertyChanged(nameof(HasDue));
        };
    }

    partial void OnListChanged(LibraryList value)
    {
        OnPropertyChanged(nameof(ShowLectures));
        OnPropertyChanged(nameof(ShowDueList));
        OnPropertyChanged(nameof(ShowCanvasClass));
        OnPropertyChanged(nameof(ListWidth));
    }

    partial void OnDueStatusChanged(CanvasStatusModel? value) => OnPropertyChanged(nameof(HasDueStatus));
    partial void OnAssignmentChanged(AssignmentModel? value) => DetailChanged();
    partial void OnReaderChanged(CanvasReaderModel? value) => DetailChanged();
    partial void OnNotesChanged(AiNotesModel? value) => OnPropertyChanged(nameof(HasNotes));
    partial void OnAskChanged(AiAskModel? value) => OnPropertyChanged(nameof(HasAsk));

    void DetailChanged()
    {
        OnPropertyChanged(nameof(ShowAssignment));
        OnPropertyChanged(nameof(ShowReader));
        OnPropertyChanged(nameof(ShowLecturePage));
        OnPropertyChanged(nameof(HasAsk));
    }

    partial void OnNarrowChanged(bool value)
    {
        if (!value) NarrowDetail = false;
        if (CanvasClass is { } c) c.Layout = value ? ClassLayout.Sections : ClassLayout.Tabs;
        OnPropertyChanged(nameof(ListWidth));
        OnPropertyChanged(nameof(DetailColumn));
        OnPropertyChanged(nameof(ColumnSpan));
        OnPropertyChanged(nameof(ShowListColumn));
        OnPropertyChanged(nameof(ShowDetailColumn));
    }

    partial void OnNarrowDetailChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowListColumn));
        OnPropertyChanged(nameof(ShowDetailColumn));
    }

    partial void OnCanvasClassChanged(CanvasClassModel? value)
    {
        if (value is not null) value.Layout = Narrow ? ClassLayout.Sections : ClassLayout.Tabs;
    }

    /// <summary>Shows what the student opened (a lecture, an assignment, a page): in a narrow window it takes the
    /// list's place until they go back.</summary>
    public void Opened()
    {
        if (Narrow) NarrowDetail = true;
    }

    [RelayCommand] void Back() => NarrowDetail = false;

    public bool HasNote => Note is not null;
    public bool NoNote => Note is null;
    public bool HasEmpty => !string.IsNullOrEmpty(Empty);

    partial void OnNoteChanged(NoteModel? value)
    {
        OnPropertyChanged(nameof(HasNote));
        OnPropertyChanged(nameof(NoNote));
        OnPropertyChanged(nameof(HasAsk));
    }

    partial void OnEmptyChanged(string? value) => OnPropertyChanged(nameof(HasEmpty));

    public Action<ClassItem>? OnClass { get; set; }
    public Action<LectureCard>? OnLecture { get; set; }
    public Action? OnMove { get; set; }
    public Action? OnExport { get; set; }
    public Action? OnSearch { get; set; }
    public Action? OnSettings { get; set; }
    public Action? OnMore { get; set; }

    [RelayCommand] void PickClass(ClassItem c) => OnClass?.Invoke(c);
    [RelayCommand] void PickLecture(LectureCard l) => OnLecture?.Invoke(l);
    [RelayCommand] void Move() => OnMove?.Invoke();
    [RelayCommand] void Export() => OnExport?.Invoke();
    [RelayCommand] void Search() => OnSearch?.Invoke();
    [RelayCommand] void Settings() => OnSettings?.Invoke();
    [RelayCommand] void More() => OnMore?.Invoke();
}
