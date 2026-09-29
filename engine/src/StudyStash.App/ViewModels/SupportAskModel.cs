using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StudyStash.App.Services;

namespace StudyStash.App.ViewModels;

/// <summary>
/// The library window's one gentle ask for a tip: a small card atop the lecture list that blocks nothing. Each button
/// answers it (and the card goes); the last time it asks, there's no "Maybe later", since there won't be a later.
/// </summary>
public sealed partial class SupportAskModel(bool last, Action<SupportAnswer> answered) : ObservableObject
{
    /// <summary>"Maybe later" shows: this isn't the last time it asks.</summary>
    public bool CanWait { get; } = !last;

    [RelayCommand] void Tip() => answered(SupportAnswer.Tip);
    [RelayCommand] void Later() => answered(SupportAnswer.Later);
    [RelayCommand] void Never() => answered(SupportAnswer.Never);
}
