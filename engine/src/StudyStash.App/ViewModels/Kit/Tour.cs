using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StudyStash.App.Services;
using StudyStash.App.Services.Kit;

namespace StudyStash.App.ViewModels.Kit;

/// <summary>One stop of a <see cref="TourModel"/>: the control it points at (by its <c>Name</c> in the view; an items
/// list points at its first item), what it's called, what it does, and how to use it. Plain words, one sentence each.</summary>
public sealed record TourStep(string Target, string Title, string What, string How);

/// <summary>
/// A short guided tour over a window (<c>Controls/TourLayer</c> draws it): the window dims, one real button shows through
/// a hole, and a card beside it says what it is, what it does and how to use it, with Next, Back and Skip tour. A step
/// whose button isn't on screen (no lectures yet, no AI) is left out, so the count only counts what it will show. The
/// button in the hole still works: using it ends the tour there (<see cref="Used"/>), as Done does.
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

    /// <summary>Over, once: true when the student saw it through (Done, Skip tour, Escape, or the button used), false
    /// when nothing it points at was on screen, so it never started.</summary>
    public event Action<bool>? Ended;

    /// <summary>How long a page just opened has to draw the controls the tour points at.</summary>
    public static readonly TimeSpan WaitForPage = TimeSpan.FromSeconds(10);

    /// <summary>Begins once something it points at is on screen (<paramref name="shows"/>), on the first such step. Nothing
    /// in <see cref="WaitForPage"/>: over, unseen, so it can run another time.</summary>
    public void Start(Func<string, bool> shows) => UiTasks.Run(async () =>
    {
        await Poll.UntilAsync(() => ended || steps.Any(s => shows(s.Target)), TimeSpan.FromMilliseconds(100), WaitForPage);
        if (!ended) Begin(shows);
    }, "start the tour");

    void Begin(Func<string, bool> shows)
    {
        showing = [.. steps.Where(s => shows(s.Target))];
        if (showing.Count == 0)
        {
            End(seen: false);
            return;
        }
        Current = showing[0];
    }

    [RelayCommand]
    void Next()
    {
        if (Index >= 0 && Index < showing.Count - 1) Current = showing[Index + 1];
        else End(seen: true);
    }

    [RelayCommand]
    void Back()
    {
        if (CanGoBack) Current = showing[Index - 1];
    }

    [RelayCommand]
    void Skip() => End(seen: true);

    /// <summary>The student used the button in the hole. Home's all leave Home, open something over it, or start the
    /// typing (Ask), so the tour ends there rather than pointing at what's now behind.</summary>
    public void Used() => End(seen: true);

    void End(bool seen)
    {
        if (ended) return;
        ended = true;
        Current = null;
        Ended?.Invoke(seen);
    }
}
