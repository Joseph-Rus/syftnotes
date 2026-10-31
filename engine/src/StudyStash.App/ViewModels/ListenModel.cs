using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StudyStash.Core;
using StudyStash.Core.Study;

namespace StudyStash.App.ViewModels;

/// <summary>Plays a WAV from a moment and stops it: the app's own player, or a stand-in in tests.</summary>
public interface IListenPlayer
{
    void Play(string wav, double fromSeconds);
    void Stop();
}

/// <summary>The app's player (AVAudioPlayer, WASAPI, the sound server), as the transcript's moments use it.</summary>
public sealed class AppPlayer : IListenPlayer
{
    public void Play(string wav, double fromSeconds) => Audio.Player.Play(wav, fromSeconds);
    public void Stop() => Audio.Player.Stop();
}

/// <summary>One turn of a listen's script, as the page shows it under the player.</summary>
public sealed class ScriptRow(ScriptLine line)
{
    public string Who => line.Who == "B" ? "Classmate" : "Tutor";
    public string Text => line.Text;
}

/// <summary>
/// Listen, on a lecture's page: the lecture as a short two-voice conversation. Not made yet, a button makes it (the
/// library's AI writes the script and the computer's voices read it, a few minutes); while it's made, the page says so
/// and looks again every few seconds; made, it plays here, pauses, goes back fifteen seconds, and shows its script.
/// </summary>
public sealed partial class ListenModel : ObservableObject, IDisposable
{
    readonly IStudyLibrary library;
    readonly IListenPlayer player;
    readonly string cache;
    readonly Stopwatch clock = new();
    CancellationTokenSource? ticking;
    CancellationTokenSource? watching;

    public ListenModel(IStudyLibrary library, string lectureId, IListenPlayer? player = null, string? cacheDir = null)
    {
        this.library = library;
        LectureId = lectureId;
        this.player = player ?? new AppPlayer();
        cache = Path.Combine(cacheDir ?? Path.Combine(Path.GetTempPath(), "syft-notes-listen"), Safe(lectureId) + ".wav");
    }

    static string Safe(string id) => string.Concat(id.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_'));

    public string LectureId { get; }
    /// <summary>What the library says of it: none, making, ready, failed; "" before it's been asked.</summary>
    [ObservableProperty] public partial string State { get; set; } = "";
    [ObservableProperty] public partial bool CanSpeak { get; set; } = true;
    [ObservableProperty] public partial string? Problem { get; set; }
    [ObservableProperty] public partial double Seconds { get; set; }
    [ObservableProperty] public partial double Position { get; set; }
    [ObservableProperty] public partial bool Playing { get; set; }
    [ObservableProperty] public partial bool ScriptOpen { get; set; }
    public ObservableCollection<ScriptRow> Script { get; } = [];

    public bool Available => State.Length > 0;
    public bool CanMake => State is ListenInfo.None or ListenInfo.Failed && CanSpeak;
    public bool Making => State == ListenInfo.Making;
    public bool Ready => State == ListenInfo.Ready;
    public bool HasProblem => !string.IsNullOrEmpty(Problem);
    public string MakeSaid => State == ListenInfo.Failed ? "Try Listen again" : "Listen";
    public string MakeTip => "A five-minute conversation going over this lecture, read by your computer's voices. Good on a walk.";
    public string MakingSaid => "Making your listen: your AI is writing it and your computer is reading it out. It takes a few minutes.";
    public string Time => $"{TimedText.Clock(Position)} / {TimedText.Clock(Seconds)}";
    public string PlaySaid => Playing ? "Pause" : Position > 0 && Position < Seconds ? "Play on" : "Play";
    public string ScriptToggleSaid => ScriptOpen ? "Hide the script" : "Show the script";
    /// <summary>How far through, 0 to 1.</summary>
    public double Progress => Seconds > 0 ? Math.Clamp(Position / Seconds, 0, 1) : 0;
    /// <summary>Why this computer can't make one (no voices), when it can't.</summary>
    public string CantSpeak => "Your library's computer has no voice to read a listen with. On Linux, install espeak-ng.";
    public bool ShowCantSpeak => Available && !CanSpeak && !Ready;

    partial void OnStateChanged(string value) => Shown();
    partial void OnCanSpeakChanged(bool value) => Shown();
    partial void OnProblemChanged(string? value) => OnPropertyChanged(nameof(HasProblem));
    partial void OnPositionChanged(double value) => Timed();
    partial void OnSecondsChanged(double value) => Timed();
    partial void OnPlayingChanged(bool value) => OnPropertyChanged(nameof(PlaySaid));
    partial void OnScriptOpenChanged(bool value) => OnPropertyChanged(nameof(ScriptToggleSaid));

    void Shown()
    {
        OnPropertyChanged(nameof(Available));
        OnPropertyChanged(nameof(CanMake));
        OnPropertyChanged(nameof(Making));
        OnPropertyChanged(nameof(Ready));
        OnPropertyChanged(nameof(MakeSaid));
        OnPropertyChanged(nameof(ShowCantSpeak));
        MakeCommand.NotifyCanExecuteChanged();
    }

    void Timed()
    {
        OnPropertyChanged(nameof(Time));
        OnPropertyChanged(nameof(Progress));
        OnPropertyChanged(nameof(PlaySaid));
    }

    /// <summary>How often a listen being made is looked at again.</summary>
    public TimeSpan LookEvery { get; init; } = TimeSpan.FromSeconds(4);

    public async Task LoadAsync()
    {
        ListenInfo? info;
        try
        {
            info = await library.ListenAsync(LectureId);
        }
        catch
        {
            return;
        }
        if (info is null) return; // a library too old to make them: nothing shows
        Show(info);
        if (info.State == ListenInfo.Making) Watch();
    }

    void Show(ListenInfo info)
    {
        CanSpeak = info.CanSpeak;
        Seconds = info.Seconds;
        Problem = info.State == ListenInfo.Failed ? $"It couldn't be made: {info.Error}" : null;
        Script.Clear();
        foreach (var line in info.Script) Script.Add(new ScriptRow(line));
        State = info.State;
    }

    [RelayCommand(CanExecute = nameof(CanMake))]
    async Task Make()
    {
        Problem = null;
        try
        {
            if (await library.MakeListenAsync(LectureId) is { } info) Show(info);
        }
        catch (LibraryRefusedException e)
        {
            Problem = e.Message;
            return;
        }
        catch
        {
            Problem = "Your library isn't answering. Try again in a moment.";
            return;
        }
        Watch();
    }

    /// <summary>Looks again every few seconds while it's being made.</summary>
    void Watch()
    {
        watching?.Cancel();
        var stop = watching = new CancellationTokenSource();
        _ = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(LookEvery, stop.Token);
                    var info = await library.ListenAsync(LectureId);
                    if (info is null || stop.IsCancellationRequested) return;
                    if (info.State != ListenInfo.Making)
                    {
                        Avalonia.Threading.Dispatcher.UIThread.Post(() => Show(info));
                        return;
                    }
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch
                {
                    // The library didn't answer this time: look again next time.
                }
            }
        });
    }

    [RelayCommand]
    async Task PlayPause()
    {
        if (Playing)
        {
            Pause();
            return;
        }
        if (!Ready) return;
        if (!File.Exists(cache))
        {
            try
            {
                if (!await library.ListenAudioAsync(LectureId, cache))
                {
                    Problem = "Its sound isn't on your library any more. Make it again.";
                    State = ListenInfo.None;
                    return;
                }
            }
            catch
            {
                Problem = "Your library isn't answering. Try again in a moment.";
                return;
            }
        }
        if (Position >= Seconds - 0.25) Position = 0;
        PlayFrom(Position);
    }

    void PlayFrom(double at)
    {
        player.Play(cache, at);
        startedAt = at;
        clock.Restart();
        Playing = true;
        ticking?.Cancel();
        var stop = ticking = new CancellationTokenSource();
        _ = TickAsync(stop.Token);
    }

    double startedAt;

    async Task TickAsync(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(250, stop);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            Tick();
        }
    }

    /// <summary>Where it's got to, from how long it's been playing; at the end it stops.</summary>
    public void Tick()
    {
        if (!Playing) return;
        Position = Math.Min(Seconds, startedAt + clock.Elapsed.TotalSeconds);
        if (Position >= Seconds)
        {
            Pause();
            Position = Seconds;
        }
    }

    void Pause()
    {
        Tick();
        ticking?.Cancel();
        clock.Stop();
        player.Stop();
        Playing = false;
    }

    /// <summary>Fifteen seconds back (from the start, at the start).</summary>
    [RelayCommand]
    void Back()
    {
        Tick();
        double to = Math.Max(0, Position - 15);
        Position = to;
        if (Playing) PlayFrom(to);
    }

    [RelayCommand] void ToggleScript() => ScriptOpen = !ScriptOpen;

    /// <summary>The page is left: what's playing stops, and nothing is looked at again.</summary>
    public void Dispose()
    {
        watching?.Cancel();
        if (Playing) Pause();
    }
}
