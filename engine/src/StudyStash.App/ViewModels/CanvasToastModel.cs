using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StudyStash.App.Services;

namespace StudyStash.App.ViewModels;

/// <summary>
/// One Canvas notification as a toast (design 12): a new assignment, a due date that moved, a new score, and so on —
/// whatever the library just noticed. Title and text are the library's own words, copied verbatim
/// (<see cref="CanvasApi.NotificationRow.Title"/>/<see cref="CanvasApi.NotificationRow.Text"/>); only
/// <see cref="When"/> is computed here, since it changes with the clock. The freshest of a batch starts
/// <see cref="Expanded"/> with its buttons; the rest sit collapsed underneath until dismissed.
/// </summary>
public sealed partial class CanvasToastModel(CanvasApi.NotificationRow item) : ObservableObject
{
    public CanvasApi.NotificationRow Item { get; } = item;
    public string Title { get; } = item.Title;
    public string Text { get; } = item.Text ?? "";

    [ObservableProperty] public partial string When { get; set; } = "";
    [ObservableProperty] public partial bool Expanded { get; set; }

    /// <summary>"Later" on the Mac, "Dismiss" on Windows — the design's own word for the same button.</summary>
    public string DismissLabel => Skin.Current == SkinKind.Mac ? "Later" : "Dismiss";

    /// <summary>Open also marks the notification seen; Dismiss/Later only marks it seen. Both are host callbacks
    /// (<see cref="CanvasNotifier"/> wires them) so nothing here talks to the library directly.</summary>
    public Func<CanvasApi.NotificationRow, Task>? OnOpen { get; set; }
    public Func<CanvasApi.NotificationRow, Task>? OnDismiss { get; set; }

    [RelayCommand]
    async Task Open()
    {
        if (OnOpen is not null) await OnOpen(Item);
    }

    [RelayCommand]
    async Task Dismiss()
    {
        if (OnDismiss is not null) await OnDismiss(Item);
    }
}
