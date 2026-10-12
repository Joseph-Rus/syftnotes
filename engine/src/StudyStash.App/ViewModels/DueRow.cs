using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StudyStash.App.Services;

namespace StudyStash.App.ViewModels;

/// <summary>One row of a class's assignments on Canvas: an assignment, quiz or discussion, worded the way
/// <see cref="CanvasWords"/> shapes it. <see cref="Select"/> tells the list the student picked it; the list owns
/// which row (if any) is selected, so only one lights up at a time.</summary>
public sealed partial class DueRow : ObservableObject
{
    public required string Class { get; init; }
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Right { get; init; }
    /// <summary>The right side reads heavier for a missing item ("Missing").</summary>
    public required bool Strong { get; init; }
    public required string Sub { get; init; }
    public required IBrush Dot { get; init; }
    /// <summary>The library's own row, for a page that has no assignment to ask for (a to-do).</summary>
    public CanvasApi.Item? Item { get; init; }

    [ObservableProperty] public partial bool Selected { get; set; }

    /// <summary>Set by the list when it builds the row.</summary>
    public Action<DueRow>? OnSelectRow { get; set; }

    [RelayCommand]
    void Select() => OnSelectRow?.Invoke(this);
}
