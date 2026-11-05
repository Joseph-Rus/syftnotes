using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StudyStash.App.Services;

namespace StudyStash.App.ViewModels;

/// <summary>
/// The library window's one gentle ask for a tip, a small popup over the window (Views/SupportAskDialog). Each button
/// answers it, once, and it closes; the last time it asks, there's no "Maybe later", since there won't be a later.
/// </summary>
public sealed partial class SupportAskModel(bool last, Action<SupportAnswer> answered) : ObservableObject
{
    /// <summary>"Maybe later" shows: this isn't the last time it asks.</summary>
    public bool CanWait { get; } = !last;

    /// <summary>The ask is over: answered, or put away unanswered (a recording started, setup opened). The popup closes.</summary>
    [ObservableProperty] public partial bool Over { get; private set; }

    [RelayCommand] void Tip() => Answer(SupportAnswer.Tip);
    /// <summary>"Maybe later"; also Escape, a click outside the popup and the window closing on it.</summary>
    [RelayCommand] void Later() => Answer(SupportAnswer.Later);
    [RelayCommand] void Never() => Answer(SupportAnswer.Never);

    /// <summary>Closes it with no answer, so nothing is saved and it may ask another day.</summary>
    public void PutAway() => Over = true;

    void Answer(SupportAnswer answer)
    {
        if (Over) return;
        Over = true;
        answered(answer);
    }
}
