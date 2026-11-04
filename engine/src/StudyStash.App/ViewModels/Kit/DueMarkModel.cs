using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StudyStash.App.Services;

namespace StudyStash.App.ViewModels.Kit;

/// <summary>
/// "Mark done", "Hide" and "Undo" for Due items, in Syft Notes alone: nothing here ever changes anything in Canvas. One
/// object for a page that has these buttons (<see cref="Show"/> tells it which item it is for, and whether the item is
/// still open), and for a list that undoes marks row by row (<see cref="SetAsync"/>). It is busy while the library
/// answers, and says in plain words when it couldn't. The Due list that comes back goes to the app's one copy
/// (<see cref="CanvasFeed.MarkAsync"/>), which every screen reads, so nothing is updated by hand here.
/// </summary>
public sealed partial class DueMarkModel(DueMarker? marker) : ObservableObject
{
    public const string DoneLabel = "Mark done", HideLabel = "Hide", UndoLabel = "Undo", ShowHiddenLabel = "Show hidden", HideListLabel = "Hide the hidden list";
    public const string DoneTip = "Tick it off here. Nothing changes in Canvas.";
    public const string HideTip = "Take it off your Due list. Nothing changes in Canvas.";
    public const string DoneState = "Marked done", HiddenState = "Hidden from your Due list";

    string cls = "", kind = "assignment", id = "";

    /// <summary>The one thing running, and what to say when it didn't work.</summary>
    public AsyncAction Action { get; } = new();

    /// <summary>What was done to the item here: "done", "hidden", or "".</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanMark), nameof(CanUndo), nameof(Ticked), nameof(State), nameof(Showing))]
    public partial string Mark { get; private set; } = "";

    /// <summary>The item is still to do, so it can be ticked off or hidden.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanMark), nameof(Showing))]
    public partial bool Open { get; private set; }

    public bool CanMark => marker is not null && Open && Mark.Length == 0;
    public bool CanUndo => marker is not null && Mark.Length > 0;
    public bool Showing => CanMark || CanUndo;
    public bool Ticked => Mark == "done";
    public string State => Mark switch { "done" => DoneState, "hidden" => HiddenState, _ => "" };

    /// <summary>Which item the buttons are for: its class, kind and id as the Due list gave them, what was done to it
    /// already, and whether it is still open (to do, overdue or missing).</summary>
    public void Show(string cls, string kind, string id, string mark, bool open)
    {
        this.cls = cls;
        this.kind = kind.Length > 0 ? kind : "assignment";
        this.id = id;
        Mark = mark;
        Open = open;
    }

    [RelayCommand]
    Task MarkDone() => SetAsync(cls, kind, id, "done");

    [RelayCommand]
    Task HideIt() => SetAsync(cls, kind, id, "hidden");

    [RelayCommand]
    Task UndoIt() => SetAsync(cls, kind, id, "");

    /// <summary>Marks an item, or with "" takes its mark back. A press while one is running is ignored.</summary>
    public async Task SetAsync(string cls, string kind, string id, string mark)
    {
        if (marker is null) return;
        await Action.RunAsync(async ct =>
        {
            var outcome = await marker(cls, kind, id, mark, ct);
            if (outcome.Ok && cls == this.cls && id == this.id) Mark = mark;
            return outcome;
        });
    }
}
