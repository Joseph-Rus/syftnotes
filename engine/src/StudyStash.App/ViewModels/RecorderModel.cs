using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace StudyStash.App.ViewModels;

/// <summary>A line of the live transcript: when it was said, and what.</summary>
public sealed class HeardLine
{
    public string Time { get; init; } = "";
    public string Text { get; init; } = "";
    /// <summary>The newest line reads in the full text color; older ones fade to secondary.</summary>
    public bool Latest { get; init; }
}

/// <summary>A moment an answer points to: "18:05", to play the recording from there.</summary>
public sealed class SourceChip
{
    public string Label { get; init; } = "";
    public double At { get; init; }
    public string? LectureId { get; init; }
}

/// <summary>One message in the recorder's chat: your question, or its answer with the moments it comes from.</summary>
public sealed partial class ChatMessage : ObservableObject
{
    public bool Mine { get; init; }
    [ObservableProperty] public partial string Text { get; set; } = "";
    [ObservableProperty] public partial bool Thinking { get; set; }
    public ObservableCollection<SourceChip> Sources { get; } = [];
    public bool HasSources => Sources.Count > 0;

    public ChatMessage() => Sources.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasSources));
}

/// <summary>
/// The floating recorder: while it's small, a tiny pill that only says it's recording (the red dot, the time and a
/// small level meter; the pointer over it swaps the meter for Stop, and a click opens it); open, the class, Pause and
/// Stop, the transcript as it comes in, fading upward, and a chat about the lecture so far (with any engine).
/// </summary>
public sealed partial class RecorderModel : ObservableObject
{
    [ObservableProperty] public partial string ClassName { get; set; } = "";
    [ObservableProperty] public partial IBrush ClassDot { get; set; } = Brushes.Gray;
    [ObservableProperty] public partial string Elapsed { get; set; } = "00:00";
    [ObservableProperty] public partial bool IsPaused { get; set; }
    [ObservableProperty] public partial IReadOnlyList<double>? Levels { get; set; }
    [ObservableProperty] public partial bool Expanded { get; set; }
    /// <summary>The pointer is over the small pill: Stop takes the level meter's place.</summary>
    [ObservableProperty] public partial bool Hovered { get; set; }
    /// <summary>Whisper hasn't anything yet: what the transcript area says instead.</summary>
    [ObservableProperty] public partial string Waiting { get; set; } = LiveWaiting;
    /// <summary>This lecture is written down after class (Settings → Recording): it only records, so there's no
    /// transcript to show and no chat about it until it stops.</summary>
    [ObservableProperty] public partial bool AfterClass { get; set; }

    public const string LiveWaiting = "What's said shows here a few seconds after it's said.";
    public const string AfterClassWaiting = "Only recording, to save battery. It's written down after class: the transcript, and asking about it, come once you stop.";

    public ObservableCollection<HeardLine> Lines { get; } = [];
    /// <summary>Asking about the lecture so far, with any engine (design 16's compact chat).</summary>
    [ObservableProperty] public partial AiAskModel? Ask { get; set; }

    public bool IsRunning => !IsPaused;
    /// <summary>The pill's last slot: the level meter while recording, a pause mark while paused, Stop under the pointer.</summary>
    public bool ShowMeter => IsRunning && !Hovered;
    public bool ShowPausedMark => IsPaused && !Hovered;
    public string PauseGlyph => IsPaused ? "play_arrow" : "pause";
    public string PauseTip => IsPaused ? "Resume" : "Pause";
    public bool NoLines => Lines.Count == 0;
    public string DisplayClass => ClassName.Length > 0 ? ClassName : "Lecture";

    public RecorderModel()
    {
        Lines.CollectionChanged += (_, _) => OnPropertyChanged(nameof(NoLines));
    }

    partial void OnIsPausedChanged(bool value)
    {
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(ShowMeter));
        OnPropertyChanged(nameof(ShowPausedMark));
        OnPropertyChanged(nameof(PauseGlyph));
        OnPropertyChanged(nameof(PauseTip));
    }

    partial void OnClassNameChanged(string value) => OnPropertyChanged(nameof(DisplayClass));

    partial void OnAfterClassChanged(bool value)
    {
        Waiting = value ? AfterClassWaiting : LiveWaiting;
        OnPropertyChanged(nameof(Live));
    }

    /// <summary>Written down as it records: the transcript comes in, and the chat asks about it.</summary>
    public bool Live => !AfterClass;

    partial void OnHoveredChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowMeter));
        OnPropertyChanged(nameof(ShowPausedMark));
    }

    public Action? OnPause { get; set; }
    public Action? OnStop { get; set; }
    public Action<bool>? OnExpand { get; set; }
    /// <summary>The window is still changing size from the last open or close: a toggle now waits (is dropped), so
    /// fast clicks can't stack one resize on another.</summary>
    public Func<bool>? Busy { get; set; }

    [RelayCommand] void Pause() => OnPause?.Invoke();
    [RelayCommand] void Stop() => OnStop?.Invoke();

    [RelayCommand]
    void Toggle()
    {
        if (Busy?.Invoke() == true) return;
        Expanded = !Expanded;
        Hovered = false;
        OnExpand?.Invoke(Expanded);
    }
}
