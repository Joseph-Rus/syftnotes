using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace StudyStash.App.ViewModels;

/// <summary>
/// Home's "Ready when class starts", once, after a first run's Get started: how to record (Record, or its keys), where
/// the S. lives (the menu bar, or by the clock), how the model's download is getting on, and why the microphone can't be
/// used yet when it can't. It replaces the notification a first run used to end with ("Syft Notes is in your menu
/// bar"), so no system question comes with it. Got it, or the first recording, puts it away for good.
/// </summary>
public sealed partial class WelcomeCardModel(bool mac) : ObservableObject
{
    public const string Title = "Ready when class starts";

    /// <summary>The student's Record keys ("⌃⌥R"), as the menus show them.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Lede))]
    public partial string RecordKeys { get; set; } = "";

    public string Lede => RecordKeys.Length > 0
        ? $"Press Record, or {RecordKeys}, when the lecture begins. Type your own notes as it records if you like."
        : "Press Record when the lecture begins. Type your own notes as it records if you like.";

    /// <summary>A Mac's menu bar is too full to show the S. (behind the camera, or other icons).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WhereLine), nameof(CanShowIt))]
    public partial bool IconHidden { get; set; }

    public string WhereLine => !mac
        ? "Syft Notes also waits by the clock: click the S. in the taskbar corner. Can't see it? Click ^ and drag it next to the clock."
        : IconHidden
            ? "Your menu bar is full, so the S. is hidden behind other icons."
            : "Syft Notes also waits in your menu bar: click the S. at the top right of your screen to record without opening this window.";

    public bool CanShowIt => mac && IconHidden;
    public const string ShowItLabel = "Show it";

    /// <summary>The model's line: "Getting speech-to-text ready on this Mac · 42%", "Speech-to-text is ready.", why it
    /// couldn't download; "" once it's been ready a while (nothing to say).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasModelLine))]
    public partial string ModelLine { get; set; } = "";
    public bool HasModelLine => ModelLine.Length > 0;

    /// <summary>The download stopped: Try again shows.</summary>
    [ObservableProperty] public partial bool ModelFailed { get; set; }

    /// <summary>Why the microphone can't be used yet (macOS's refusal, Windows' desktop-app switch); "" while it can.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMicLine))]
    public partial string MicLine { get; set; } = "";
    public bool HasMicLine => MicLine.Length > 0;
    [ObservableProperty] public partial string MicButton { get; set; } = "";

    /// <summary>The model's line as the download stands: <paramref name="ready"/>, <paramref name="fraction"/> while it
    /// downloads, or <paramref name="problem"/> when it stopped. <paramref name="device"/>: "Mac" or "PC".</summary>
    public void ShowModel(bool ready, double? fraction, string? problem, string device)
    {
        ModelFailed = !ready && fraction is null && problem is { Length: > 0 };
        ModelLine = ready ? "Speech-to-text is ready."
            : fraction is { } f ? $"Getting speech-to-text ready on this {device} · {Math.Clamp((int)Math.Round(f * 100), 0, 99)}%"
            : problem is { Length: > 0 } why ? $"Speech-to-text couldn't download: {why.TrimEnd('.')}."
            : $"Getting speech-to-text ready on this {device}…";
    }

    public Action? OnShowIt { get; set; }
    public Action? OnRetry { get; set; }
    public Action? OnMic { get; set; }
    public Action? OnGotIt { get; set; }

    [RelayCommand] void ShowIt() => OnShowIt?.Invoke();
    [RelayCommand] void Retry() => OnRetry?.Invoke();
    [RelayCommand] void Mic() => OnMic?.Invoke();
    [RelayCommand] void GotIt() => OnGotIt?.Invoke();
}
