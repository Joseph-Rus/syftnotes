using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StudyStash.App.Services;
using StudyStash.Core.Ai;

namespace StudyStash.App.ViewModels.Kit;

/// <summary>
/// The box a sign-in's code is pasted into, for an AI whose sign-in page ends on one (<see cref="AgentCliInfo.TakesCode"/>:
/// Gemini's Google page says "Paste this code into your application"). One for every place a sign-in waits: guided
/// setup's sign-in step and the Get-ready window (the sign-in is the app's own), and the small window for a sign-in
/// started from Settings or a problem card (it's the library's). What's pasted is handed to <c>send</c> and kept nowhere
/// else; once it's sent the box says it's signing in, and the screen around it turns ready by itself.
/// </summary>
public sealed partial class SignInCodeModel : ObservableObject
{
    readonly Func<string, CancellationToken, Task<Outcome>> send;

    /// <param name="engineName">The AI as students know it ("Gemini").</param>
    /// <param name="maker">Whose page shows the code ("Google").</param>
    /// <param name="send">Hands the cleaned code to the sign-in waiting for it.</param>
    /// <param name="openAgain">Opens the same page again (its code is for this sign-in); null leaves the link out.</param>
    /// <param name="tryAgain">Starts a new sign-in once this one stopped (its own window's Try again); null where the
    /// screen around the box has its own.</param>
    public SignInCodeModel(string engineName, string maker, Func<string, CancellationToken, Task<Outcome>> send, Action? openAgain = null,
        Action? tryAgain = null)
    {
        EngineName = engineName;
        Maker = maker;
        this.send = send;
        if (openAgain is not null) OpenAgainCommand = new RelayCommand(openAgain);
        if (tryAgain is not null)
            TryAgainCommand = new RelayCommand(() =>
            {
                Reset();
                tryAgain();
            });
        Send = new AsyncAction(SendAsync);
        Send.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AsyncAction.Busy)) OnPropertyChanged(nameof(CanSend));
            if (e.PropertyName == nameof(AsyncAction.Say)) OnPropertyChanged(nameof(Problem));
        };
    }

    public string EngineName { get; }
    public string Maker { get; }
    public string Title => AiWords.CodeTitle(Maker);
    public string WindowTitle => $"Sign in to {EngineName}";
    public string Lede => AiWords.CodeLede(Maker);
    public string OpenAgainLabel => $"Open {Maker}'s page again";
    public IRelayCommand? OpenAgainCommand { get; }
    public bool HasOpenAgain => OpenAgainCommand is not null && !Stopped;

    /// <summary>What's in the box.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSend))]
    public partial string Code { get; set; } = "";

    /// <summary>"Finish signing in": the code goes to the sign-in.</summary>
    public AsyncAction Send { get; }

    /// <summary>It was sent, and the sign-in is finishing ("Signing in to Gemini…").</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSend), nameof(Sending))]
    public partial bool Sent { get; private set; }

    public bool CanSend => !Send.Busy && !Sent && !Stopped && Code.Trim().Length > 0;
    public string Sending => Sent && !Stopped ? AiWords.CodeSending(EngineName) : "";
    /// <summary>Why the code didn't go ("That doesn't look like the code…", the library not answering), or why the
    /// sign-in stopped (<see cref="Stop"/>); null otherwise.</summary>
    public string? Problem => StoppedWords ?? Send.Say;

    /// <summary>The sign-in stopped without signing in (the code didn't work, or it waited too long): nothing more can be
    /// pasted into it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSend), nameof(Sending), nameof(CanTryAgain), nameof(HasOpenAgain))]
    public partial bool Stopped { get; private set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Problem))]
    public partial string? StoppedWords { get; private set; }

    /// <summary>Try again, in the box's own window, once the sign-in stopped.</summary>
    public IRelayCommand? TryAgainCommand { get; }
    public bool CanTryAgain => Stopped && TryAgainCommand is not null;

    /// <summary>The sign-in stopped: <paramref name="words"/> say why ("That code didn't work…").</summary>
    public void Stop(string words)
    {
        StoppedWords = words;
        Stopped = true;
    }

    async Task<Outcome> SendAsync(CancellationToken ct)
    {
        if (AgentSignIn.CleanCode(Code) is not { } code) return Outcome.Failed(new Failure(FailureKind.Refused, AiWords.CodeDoesntLookRight));
        var done = await send(code, ct);
        if (done.Ok)
        {
            Sent = true;
            Code = "";
        }
        return done;
    }

    /// <summary>A new sign-in (Try again): the box is empty and ready again.</summary>
    public void Reset()
    {
        Sent = false;
        Code = "";
        StoppedWords = null;
        Stopped = false;
    }
}
