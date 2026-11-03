using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StudyStash.Core;
using StudyStash.Core.Study;

namespace StudyStash.App.ViewModels;

/// <summary>
/// Kokoro, Listen's downloadable voices, in Settings → Notes and under a lecture's Listen: what they are, how big and
/// under what licence, one button to download them into the library (how far it's got, while it does), and Remove.
/// Shows nothing for a library that has none to offer (one too old, or the engine on its own).
/// </summary>
public sealed partial class ListenVoicesModel : ObservableObject, IDisposable
{
    readonly Func<IStudyLibrary?> library;
    CancellationTokenSource? watching;

    public ListenVoicesModel(Func<IStudyLibrary?> library) => this.library = library;

    public ListenVoicesModel(IStudyLibrary library) : this(() => library) { }

    /// <summary>What the library said; null before it's been asked, or when it has none.</summary>
    [ObservableProperty] public partial VoicesInfo? Info { get; set; }
    [ObservableProperty] public partial string? Problem { get; set; }
    /// <summary>Remove was pressed once: it asks before it goes.</summary>
    [ObservableProperty] public partial bool ConfirmingRemove { get; set; }

    /// <summary>How often a download is looked at again.</summary>
    public TimeSpan LookEvery { get; set; } = TimeSpan.FromSeconds(1);

    string State => Info?.State ?? "";
    public bool Available => Info is not null && State != VoicesInfo.Unavailable;
    public bool CanDownload => State is VoicesInfo.None or VoicesInfo.Failed;
    public bool Downloading => State == VoicesInfo.Downloading;
    public bool Here => State == VoicesInfo.Ready;
    public bool HasProblem => !string.IsNullOrEmpty(Problem);

    public string Title => $"{Info?.Name ?? "Kokoro"} voices";
    public string Size => Info is { } i ? SizeOf(i.Bytes) : "";
    /// <summary>"149 MB · Apache-2.0 licence · open source", under the title.</summary>
    public string Facts => Info is { } i ? $"{Size} · {i.License} licence · open source" : "";
    public string Line => Here
        ? "Listens are read by Kokoro's natural voices, on your library's computer. Ones made before keep the voices they had."
        : Info?.About is { Length: > 0 } about ? about + " Until then your computer's own voices read them." : "";
    public string DownloadSaid => State == VoicesInfo.Failed ? "Try again" : $"Download ({Size})";
    /// <summary>"42 of 149 MB".</summary>
    public string ProgressSaid => Info is { } i ? $"{i.Done / 1_000_000} of {Size}" : "";
    public double Fraction => Info is { Bytes: > 0 } i ? Math.Clamp(i.Done / (double)i.Bytes, 0, 1) : 0;
    /// <summary>Remove asks once before it goes.</summary>
    public string RemoveSaid => ConfirmingRemove ? "Remove them" : "Remove…";
    /// <summary>On the Listen card: one line that offers them, until they're here.</summary>
    public string OfferSaid => $"Better voices: download Kokoro ({Size}, open source)";

    static string SizeOf(long bytes) => bytes >= 1_000_000_000 ? $"{bytes / 1e9:0.0} GB" : $"{bytes / 1e6:0} MB";

    partial void OnInfoChanged(VoicesInfo? value)
    {
        foreach (string p in new[] { nameof(Available), nameof(CanDownload), nameof(Downloading), nameof(Here), nameof(Title), nameof(Size),
                     nameof(Facts), nameof(Line), nameof(DownloadSaid), nameof(ProgressSaid), nameof(Fraction), nameof(OfferSaid) })
            OnPropertyChanged(p);
        DownloadCommand.NotifyCanExecuteChanged();
        RemoveCommand.NotifyCanExecuteChanged();
    }

    partial void OnProblemChanged(string? value) => OnPropertyChanged(nameof(HasProblem));

    partial void OnConfirmingRemoveChanged(bool value) => OnPropertyChanged(nameof(RemoveSaid));

    /// <summary>Asks the library (when the page opens); keeps looking while a download is going.</summary>
    public async Task LoadAsync()
    {
        if (library() is not { } lib) return;
        try
        {
            Show(await lib.VoicesAsync());
        }
        catch (Exception e) when (LibraryTrouble.Handled(e))
        {
            // An older library takes "voices" for a lecture it hasn't got, or it isn't answering: nothing shows.
            Info = null;
        }
    }

    void Show(VoicesInfo? info)
    {
        Info = info;
        Problem = info?.State == VoicesInfo.Failed && info.Error.Length > 0 ? $"They didn't download: {info.Error}" : null;
        if (Downloading) Watch();
    }

    [RelayCommand(CanExecute = nameof(CanDownload))]
    async Task Download()
    {
        if (library() is not { } lib) return;
        Problem = null;
        try
        {
            Show(await lib.DownloadVoicesAsync());
        }
        catch (LibraryRefusedException e)
        {
            Problem = e.Message;
        }
        catch (Exception e) when (LibraryTrouble.Handled(e))
        {
            Problem = "Your library isn't answering. Try again in a moment.";
        }
    }

    [RelayCommand(CanExecute = nameof(Here))]
    async Task Remove()
    {
        if (!ConfirmingRemove)
        {
            ConfirmingRemove = true;
            return;
        }
        ConfirmingRemove = false;
        if (library() is not { } lib) return;
        try
        {
            Show(await lib.DeleteVoicesAsync());
        }
        catch (LibraryRefusedException e)
        {
            Problem = e.Message;
        }
        catch (Exception e) when (LibraryTrouble.Handled(e))
        {
            Problem = "Your library isn't answering. Try again in a moment.";
        }
    }

    [RelayCommand] void CancelRemove() => ConfirmingRemove = false;

    /// <summary>Looks again every second while they download, until they're here or it stops.</summary>
    void Watch()
    {
        if (watching is { IsCancellationRequested: false }) return;
        var stop = watching = new CancellationTokenSource();
        _ = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(LookEvery, stop.Token);
                    if (library() is not { } lib || await lib.VoicesAsync() is not { } info || stop.IsCancellationRequested) return;
                    bool done = info.State != VoicesInfo.Downloading;
                    if (done) stop.Cancel();
                    Post(() => Show(info));
                    if (done) return;
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception e) when (LibraryTrouble.Handled(e))
                {
                    // The library didn't answer this time: look again next time.
                }
            }
        });
    }

    /// <summary>On the UI thread when there is one (tests have none).</summary>
    static void Post(Action a)
    {
        if (Avalonia.Application.Current is null) a();
        else Avalonia.Threading.Dispatcher.UIThread.Post(a);
    }

    public void Dispose() => watching?.Cancel();
}
