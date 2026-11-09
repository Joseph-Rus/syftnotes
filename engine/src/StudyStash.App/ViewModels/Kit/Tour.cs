using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace StudyStash.App.ViewModels.Kit;

/// <summary>One stop of a <see cref="TourModel"/>: the control it points at (by its <c>Name</c> in the view; an items
/// list points at its first item), what it's called, what it does, and how to use it. Plain words, one sentence each.</summary>
public sealed record TourStep(string Target, string Title, string What, string How);

/// <summary>
/// A short guided tour over a window (<c>Controls/TourLayer</c> draws it): the window dims, one real button shows through
/// a hole, and a card beside it says what it is, what it does and how to use it, with Next, Back and Skip tour. A step
/// whose button isn't on screen (no lectures yet, no AI) is left out, so the count only counts what it will show.
/// </summary>
public sealed partial class TourModel(IReadOnlyList<TourStep> steps) : ObservableObject
{
    List<TourStep> showing = [];
    bool ended;

    /// <summary>The step showing; null before <see cref="Begin"/> and once it's over.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Counter), nameof(CanGoBack), nameof(NextLabel))]
    public partial TourStep? Current { get; private set; }

    int Index => Current is null ? -1 : showing.IndexOf(Current);
    public string Counter => $"Step {Index + 1} of {showing.Count}";
    public bool CanGoBack => Index > 0;
    public string NextLabel => Index == showing.Count - 1 ? "Done" : "Next";

    /// <summary>Finished, skipped, or nothing to show: once.</summary>
    public event Action? Ended;

    /// <summary>Starts on the first step <paramref name="shows"/> finds on screen. False (and over) when none is.</summary>
    public bool Begin(Func<string, bool> shows)
    {
        showing = [.. steps.Where(s => shows(s.Target))];
        if (showing.Count == 0)
        {
            End();
            return false;
        }
        Current = showing[0];
        return true;
    }

    [RelayCommand]
    void Next()
    {
        if (Index >= 0 && Index < showing.Count - 1) Current = showing[Index + 1];
        else End();
    }

    [RelayCommand]
    void Back()
    {
        if (CanGoBack) Current = showing[Index - 1];
    }

    [RelayCommand]
    void Skip() => End();

    void End()
    {
        if (ended) return;
        ended = true;
        Current = null;
        Ended?.Invoke();
    }
}
