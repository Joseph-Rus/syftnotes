using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StudyStash.App.Services;

namespace StudyStash.App.ViewModels.Kit;

/// <summary>
/// "Mark as done" and "Undo" on an assignment's page: the student's own word that it's done, kept in Syft Notes alone
/// (nothing here ever changes anything in Canvas). <see cref="Show"/> tells it which item the page is for, whether the
/// student marked it before, and whether it is still open (nothing handed in). It is busy while the library answers,
/// and says in plain words when it couldn't. The Due list that comes back goes to the app's one copy
/// (<see cref="CanvasFeed.MarkAsync"/>), which every screen reads, so nothing is updated by hand here.
/// </summary>
public sealed partial class DueMarkModel(DueMarker? marker) : ObservableObject
{
    public const string DoneLabel = "Mark as done", UndoLabel = "Undo";
    /// <summary>What the page says once it's marked: "Done" alone would read as Canvas's word.</summary>
    public const string DoneState = "Done (marked by you)";
    /// <summary>A list's word for a row the student marked, where it would otherwise say "Handed in".</summary>
    public const string DoneRow = "Marked done";
    public const string DoneTip = "Only in Syft Notes. Nothing changes in Canvas.";

    string cls = "", kind = "assignment", id = "";

    /// <summary>The one thing running, and what to say when it didn't work.</summary>
    public AsyncAction Action { get; } = new();

    /// <summary>The student marked it done.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanMark), nameof(ShowDone), nameof(Showing))]
    public partial bool Marked { get; private set; }

    /// <summary>Nothing has been handed in, so there is something for the student to say they did.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanMark), nameof(ShowDone), nameof(Showing))]
    public partial bool Open { get; private set; }

    /// <summary>"Mark as done" is offered.</summary>
    public bool CanMark => marker is not null && Open && !Marked;
    /// <summary>"Done (marked by you)" and "Undo" are offered, in place of what Canvas says about the submission.</summary>
    public bool ShowDone => marker is not null && Open && Marked;
    public bool Showing => CanMark || ShowDone;

    /// <summary>Which item the buttons are for: its class, kind and id as the library gave them, whether the student
    /// marked it already, and whether it is still open.</summary>
    public void Show(string cls, string kind, string id, bool marked, bool open)
    {
        this.cls = cls;
        this.kind = kind.Length > 0 ? kind : "assignment";
        this.id = id;
        Marked = marked;
        Open = open;
    }

    [RelayCommand]
    Task MarkDone() => SetAsync("done");

    [RelayCommand]
    Task UndoIt() => SetAsync("");

    /// <summary>Marks the item done, or with "" takes the mark back. A press while one is running is ignored.</summary>
    async Task SetAsync(string mark)
    {
        if (marker is null) return;
        (string cls, string kind, string id) = (this.cls, this.kind, this.id);
        await Action.RunAsync(async ct =>
        {
            var outcome = await marker(cls, kind, id, mark, ct);
            if (outcome.Ok && cls == this.cls && id == this.id) Marked = mark == "done";
            return outcome;
        });
    }
}
