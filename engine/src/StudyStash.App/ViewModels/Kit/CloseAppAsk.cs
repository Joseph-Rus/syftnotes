using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StudyStash.App.Services;

namespace StudyStash.App.ViewModels.Kit;

/// <summary>
/// "Close Claude first", with one button: an AI's desktop app (Claude, ChatGPT) is open, and what Syft Notes has to
/// write to its settings waits for it to close, because the app keeps its settings in memory and writes them back
/// whole. The button runs <c>run</c>, which closes the app, writes, and opens it again (<see cref="AiConnect"/>,
/// <see cref="ClaudeSetup.RepairAsync"/>), saying each step here; then <c>done</c>. If it won't close, how to quit it,
/// and the button turns to Connect (the same run, once the student has quit it). Shown by
/// <c>Views/CloseAppFirst.axaml</c> on setup's sign-in screen and on Home.
/// </summary>
public sealed partial class CloseAppAsk(string app, Func<Action<string>, Task<ConnectOutcome>> run, Action done) : ObservableObject
{
    /// <summary>The app as its own menu bar names it ("Claude").</summary>
    public string App { get; } = app;

    [ObservableProperty] public partial string Say { get; set; } = AiConnect.CloseFirstWords(app);
    [ObservableProperty] public partial bool Busy { get; set; }
    /// <summary>It wouldn't close: the button is Connect, for once the student has quit it.</summary>
    [NotifyPropertyChangedFor(nameof(Button))]
    [ObservableProperty]
    public partial bool Failed { get; set; }
    public string Button => Failed ? "Connect" : AiConnect.CloseButtonWords(App);

    [RelayCommand]
    async Task Close()
    {
        if (Busy) return;
        Busy = true;
        Say = AiConnect.ClosingWords(App);
        var ui = SynchronizationContext.Current;
        ConnectOutcome outcome;
        try
        {
            // Said on this thread (the steps are), it shows at once and in order; from another, it's posted here.
            outcome = await run(words =>
            {
                if (ui is null || SynchronizationContext.Current == ui) Say = words;
                else ui.Post(_ => Say = words, null);
            });
        }
        finally
        {
            Busy = false;
        }
        if (outcome.CouldntClose)
        {
            Say = outcome.Words;
            Failed = true;
            return;
        }
        done();
    }
}
