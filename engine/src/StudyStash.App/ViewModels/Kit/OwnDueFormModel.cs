using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StudyStash.App.Services;

namespace StudyStash.App.ViewModels.Kit;

/// <summary>A class the form offers: the library's name for it, and the words a student reads ("No class" for none).</summary>
public sealed record OwnDueClass(string Name, string Label);

/// <summary>
/// The small form for the student's own items on the Due list (an exam, a study session, a to-do): <see cref="Add"/> opens it
/// blank, <see cref="Edit"/> opens one already there with "Mark done" (the Due marks, <see cref="Marks"/>) and "Delete", which asks
/// first. It is open while <see cref="IsOpen"/>; the view shows it in the window's dialog layer. These items live in Syft Notes
/// only, with no Canvas and nothing synced to a calendar. What Save sends is a <see cref="OwnDueDraft"/>; the library answers with
/// the Due list as it now stands and the app's one copy takes it in (<see cref="CanvasFeed"/>).
/// </summary>
public sealed partial class OwnDueFormModel : ObservableObject
{
    public const string AddLabel = "Add", ExamLabel = "Exam", StudyLabel = "Study session", TaskLabel = "To do";
    public const string NoClass = "No class";
    const string MineKind = Core.Canvas.DueMine.Kind;

    static readonly OwnDueClass None = new("", NoClass);

    readonly OwnDueEditor? editor;
    readonly IConfirm confirm;
    string id = "";

    public OwnDueFormModel(OwnDueEditor? editor, DueMarker? marker, IReadOnlyList<string> classes, IConfirm? confirm = null)
    {
        this.editor = editor;
        this.confirm = confirm ?? Confirm.Default;
        Marks = new DueMarkModel(marker);
        Classes = [None, .. classes.Select(c => new OwnDueClass(c, ClassNames.Display(c)))];
        // Ticking it off is the end of it: the form goes, and the calendar shows it ticked.
        Marks.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(DueMarkModel.Mark) && Marks.Ticked) IsOpen = false;
        };
    }

    /// <summary>The small word that marks an item as the student's own on the calendar, short for a day's narrow cell: "Exam", "Study",
    /// "To do".</summary>
    public static string WordFor(string what) => what switch { "exam" => ExamLabel, "study" => "Study", _ => TaskLabel };

    /// <summary>"Mark done" and "Undo", for an item being changed.</summary>
    public DueMarkModel Marks { get; }

    /// <summary>Save and Delete: busy while the library answers, and what to say when it couldn't.</summary>
    public AsyncAction Action { get; } = new();

    /// <summary>"No class" and every class there is.</summary>
    public IReadOnlyList<OwnDueClass> Classes { get; }

    /// <summary>The form is showing.</summary>
    [ObservableProperty] public partial bool IsOpen { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    public partial string Title { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsExam), nameof(IsStudy), nameof(IsTask), nameof(Heading))]
    public partial string What { get; set; } = "exam";

    [ObservableProperty] public partial OwnDueClass Chosen { get; set; } = None;

    /// <summary>The day (the date picker's), required.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    public partial DateTime? Day { get; set; }

    /// <summary>The time of day, or null for none (then it is due at the end of the day).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTime))]
    public partial TimeSpan? Time { get; set; }

    public bool IsExam => What == "exam";
    public bool IsStudy => What == "study";
    public bool IsTask => What is not ("exam" or "study");
    public bool HasTime => Time is not null;

    /// <summary>It is one already on the list (not a new one).</summary>
    public bool Editing => id.Length > 0;
    public bool Adding => !Editing;
    public string Heading => Editing ? What switch { "exam" => "Change this exam", "study" => "Change this study session", _ => "Change this to-do" } : "Add to your Due list";
    public string SaveLabel => Editing ? "Save" : AddLabel;
    public bool CanSave => editor is not null && Title.Trim().Length > 0 && Day is not null;

    void Reset(string id)
    {
        this.id = id;
        Action.Cancel();
        OnPropertyChanged(nameof(Editing));
        OnPropertyChanged(nameof(Adding));
        OnPropertyChanged(nameof(SaveLabel));
    }

    /// <summary>Opens the form blank, for <paramref name="day"/> (today when none is given).</summary>
    public void Add(DateTime day)
    {
        Reset("");
        Title = "";
        What = "exam";
        Chosen = None;
        Day = day.Date;
        Time = null;
        Marks.Show("", MineKind, "", "", open: false);
        OnPropertyChanged(nameof(Heading));
        IsOpen = true;
    }

    /// <summary>Opens the form on an item of the student's own that is on the list.</summary>
    public void Edit(CanvasApi.Item item, TimeZoneInfo zone)
    {
        Reset(item.Id);
        Title = item.Name;
        What = item.What.Length > 0 ? item.What : "task";
        Chosen = Classes.FirstOrDefault(c => c.Name == item.Class) ?? None;
        var at = item.DueAt is { } due ? TimeZoneInfo.ConvertTime(due, zone) : (DateTimeOffset?)null;
        Day = at?.Date;
        Time = at is { } t && !item.AllDay ? t.TimeOfDay : null;
        Marks.Show("", MineKind, item.Id, item.Mark, open: CanvasWords.ToHandIn(item.Status) || item.Mark.Length > 0);
        OnPropertyChanged(nameof(Heading));
        IsOpen = true;
    }

    [RelayCommand] void SetWhat(string what) => What = what;
    [RelayCommand] void NoTime() => Time = null;
    [RelayCommand] public void Close() => IsOpen = false;

    [RelayCommand]
    async Task Save()
    {
        if (editor is null || !CanSave) return;
        var draft = new OwnDueDraft(id, Title.Trim(), What, Chosen.Name, Day!.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Time is { } t ? t.ToString(@"hh\:mm", CultureInfo.InvariantCulture) : "");
        var outcome = await Action.RunAsync(ct => editor.Save(draft, ct));
        if (outcome is { Ok: true }) IsOpen = false;
    }

    [RelayCommand]
    async Task Delete()
    {
        if (editor is null || !Editing) return;
        string title = Title;
        // The question opens over the window, and a window shows one at a time: the form steps aside, and comes back if they say no.
        IsOpen = false;
        if (!await confirm.AskAsync(new ConfirmAsk($"Delete \"{title}\"?", "It goes from your Due list for good.", "Delete")))
        {
            IsOpen = true;
            return;
        }
        var outcome = await Action.RunAsync(ct => editor.Delete(id, ct));
        if (outcome is not { Ok: true }) IsOpen = true; // it is still there: the form says why
    }
}
