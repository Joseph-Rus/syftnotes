using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StudyStash.App.Services;
using StudyStash.Core;
using StudyStash.Core.Ai;

namespace StudyStash.App.ViewModels;

/// <summary>Where Settings' helper reaches beyond itself: finding the student's AI, running a turn, opening the settings
/// tools' door, and setup (to install or sign in to an AI). The app's own by default; tests give fakes.</summary>
public sealed class SettingsChatServices
{
    public required string Home { get; init; }
    public bool Windows { get; init; } = OperatingSystem.IsWindows();
    public Func<AgentCliInfo, AgentFound> Find { get; init; } = cli => AgentInstall.Find(cli);
    public Func<SetupTurn, CancellationToken, IAsyncEnumerable<SetupChatEvent>> Turn { get; init; } = SetupChat.RunAsync;
    /// <summary>The AI picked in guided setup ("claude", "codex" or ""): the helper uses it when it's here.</summary>
    public Func<string> Preferred { get; init; } = () => "";
    /// <summary>What this computer is for, in a line, for the helper's brief.</summary>
    public Func<string> Computer { get; init; } = () => "";
    /// <summary>Opens the settings tools' door (the window opens it once, and closes it with itself); null when it can't.</summary>
    public Func<Task<SetupDoor?>>? OpenDoor { get; set; }
    /// <summary>Guided setup, where an AI is installed and signed in to.</summary>
    public Action? RunSetup { get; set; }
    public Func<DateTime> Now { get; init; } = () => DateTime.Now;
    public Action<string> Log { get; init; } = _ => { };
    /// <summary>Runs something on the window's thread (a CLI's events come from its own).</summary>
    public Action<Action> Post { get; init; } = a => Dispatcher.UIThread.Post(a);
}

/// <summary>Whether Settings' helper can talk yet: still looking for the student's AI, none here, or ready.</summary>
public enum AssistantState
{
    Looking,
    NoAi,
    Ready,
}

/// <summary>
/// Settings' helper ("Ask your AI"): the student says what they want ("make it dark", "record my Zoom lectures too"),
/// and their own Claude Code or Codex, run as guided setup's chat is (locked to Egghead Notes' own tools), finds it in
/// Settings, shows it, and changes what's theirs to change, each change showing in the window as it's made. One
/// conversation a window; New chat starts another.
/// </summary>
public sealed partial class SettingsChatModel(SettingsChatServices services) : ObservableObject, IDisposable
{
    readonly CancellationTokenSource closing = new();
    CancellationTokenSource? turn;
    Task<SetupDoor?>? door;
    string session = "";
    bool opened;

    public SettingsChatServices Services => services;
    public ObservableCollection<ChatEntry> Thread { get; } = [];

    /// <summary>A few things to ask, before anything's been asked.</summary>
    public IReadOnlyList<string> Suggestions { get; } =
    [
        "Make Egghead Notes dark",
        "Record my Zoom lectures too",
        "Keep recordings for a week",
        "Where do I change my library's password?",
    ];

    [ObservableProperty] public partial AssistantState State { get; set; } = AssistantState.Looking;
    /// <summary>"claude" or "codex", once found.</summary>
    [ObservableProperty] public partial string Provider { get; set; } = "";
    [ObservableProperty] public partial string Draft { get; set; } = "";
    [ObservableProperty] public partial bool Busy { get; set; }
    /// <summary>Why the last turn stopped, in words, under the thread.</summary>
    [ObservableProperty] public partial string? Problem { get; set; }
    /// <summary>The problem is one setup fixes (signing in again).</summary>
    [ObservableProperty] public partial bool ProblemNeedsSetup { get; set; }

    AgentFound found = AgentFound.None;

    public AgentCliInfo? Cli => Provider.Length > 0 ? AgentCli.Get(Provider) : null;
    /// <summary>"Claude" or "ChatGPT", or "your AI" before one's found.</summary>
    public string Brand => Cli?.Brand ?? "your AI";
    public string Title => Cli is { } c ? $"Ask {c.Brand}" : "Ask your AI";
    public string Lede => Cli is { } c
        ? $"Tell {c.Brand} what you'd like, and it finds it in Settings, shows you, and changes it for you. It can only reach Egghead Notes' settings, and never your passwords."
        : "Your AI can find things in Settings and change them for you.";
    public string Placeholder => $"Ask {Brand} to change something…";
    public bool IsLooking => State == AssistantState.Looking;
    public bool IsNoAi => State == AssistantState.NoAi;
    public bool IsReady => State == AssistantState.Ready;
    public bool ShowWelcome => IsReady && Thread.Count == 0;
    public bool HasProblem => !string.IsNullOrEmpty(Problem);
    public string NoAiText => "Ask your AI uses Claude Code or Codex on this computer, signed in with your Claude or ChatGPT plan. Setup can install it and sign you in.";

    partial void OnProviderChanged(string value)
    {
        foreach (string p in new[] { nameof(Cli), nameof(Brand), nameof(Title), nameof(Lede), nameof(Placeholder) }) OnPropertyChanged(p);
    }

    partial void OnStateChanged(AssistantState value)
    {
        foreach (string p in new[] { nameof(IsLooking), nameof(IsNoAi), nameof(IsReady), nameof(ShowWelcome) }) OnPropertyChanged(p);
    }

    partial void OnProblemChanged(string? value) => OnPropertyChanged(nameof(HasProblem));

    /// <summary>The page opened: finds the student's AI (the one setup used, if it's here; else whichever is), once.</summary>
    public async Task OpenAsync()
    {
        if (opened) return;
        opened = true;
        await LookAsync();
    }

    async Task LookAsync()
    {
        State = AssistantState.Looking;
        string preferred = services.Preferred();
        var (claude, codex) = await Task.Run(() => (services.Find(AgentCli.Claude), services.Find(AgentCli.Codex)));
        (Provider, found) = preferred == "codex" && codex.Works ? ("codex", codex)
            : preferred == "claude" && claude.Works ? ("claude", claude)
            : claude.Works ? ("claude", claude)
            : codex.Works ? ("codex", codex)
            : ("", AgentFound.None);
        State = found.Works ? AssistantState.Ready : AssistantState.NoAi;
    }

    /// <summary>Sends what's typed.</summary>
    [RelayCommand]
    Task Send()
    {
        string text = Draft.Trim();
        if (text.Length == 0 || Busy) return Task.CompletedTask;
        Draft = "";
        return SendAsync(text);
    }

    [RelayCommand]
    Task Suggest(string text) => SendAsync(text);

    /// <summary>Asks the helper <paramref name="text"/> (from the field, a suggestion, or Settings' search). Waits for
    /// the page to find the AI first; while a turn runs, it's not sent.</summary>
    public async Task SendAsync(string text)
    {
        text = text.Trim();
        if (text.Length == 0 || Busy) return;
        await OpenAsync();
        if (State == AssistantState.Looking) await LookAsync();
        if (!IsReady || found.Exe is not { } exe) return;
        Thread.Add(new StudentEntry(text));
        OnPropertyChanged(nameof(ShowWelcome));
        await RunTurnAsync(exe, text);
    }

    async Task RunTurnAsync(string exe, string prompt)
    {
        Busy = true;
        Problem = null;
        ProblemNeedsSetup = false;
        var cts = CancellationTokenSource.CreateLinkedTokenSource(closing.Token);
        turn = cts;
        var ai = new AiEntry(Brand);
        bool shown = false;
        ChatProblem problem = ChatProblem.None;
        string said = "";
        try
        {
            door ??= services.OpenDoor?.Invoke() ?? Task.FromResult<SetupDoor?>(null);
            if (await door is not { } d)
            {
                door = null;
                problem = ChatProblem.Tools;
                said = "The settings tools' door didn't open.";
            }
            else
            {
                d.NewTurn();
                string work = SettingsChat.Folder(services.Home);
                string brief = SetupChat.Prepare(work, SettingsChat.Brief(services.Windows, services.Computer()), d.Url);
                var t = new SetupTurn
                {
                    Provider = Provider, Exe = exe, Version = found.Version, WorkDir = work, BriefFile = brief, McpUrl = d.Url, Token = d.Token,
                    Session = session, Prompt = prompt, Windows = services.Windows,
                };
                await foreach (var e in services.Turn(t, cts.Token).WithCancellation(cts.Token))
                {
                    switch (e.Kind)
                    {
                        case "Session" when e.Text.Length > 0:
                            session = e.Text;
                            break;
                        case "Text":
                            if (!shown)
                            {
                                Thread.Add(ai);
                                shown = true;
                            }
                            ai.Text = (ai.Text + e.Text).TrimStart();
                            break;
                        case "Tool" when e.Chip.Length > 0:
                            if (!shown)
                            {
                                Thread.Add(ai);
                                shown = true;
                            }
                            ai.Chip(e.Chip);
                            break;
                        case "Problem":
                            problem = e.Problem;
                            said = e.Text;
                            break;
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            Busy = false;
            if (turn == cts) turn = null;
            cts.Dispose();
        }
        if (closing.IsCancellationRequested) return;
        if (problem != ChatProblem.None) ShowTrouble(problem, said);
    }

    void ShowTrouble(ChatProblem kind, string said)
    {
        services.Log($"[settings] {Cli?.Name} turn stopped: {kind}: {Py.Head(said, 300)}");
        ProblemNeedsSetup = kind is ChatProblem.Auth or ChatProblem.Plan;
        Problem = kind switch
        {
            ChatProblem.Auth => $"{Brand} isn't signed in on this computer. Run setup to sign in again.",
            ChatProblem.Plan => $"Your {Brand} plan doesn't include {Cli?.Name}. Run setup to use another AI.",
            ChatProblem.Offline => $"{Brand} couldn't be reached. Check your internet connection.",
            ChatProblem.Limit => $"{Brand} is at its usage limit until {GuidedSetupModel.UntilWords(Engines.UntilFrom(said, services.Now()))}. You can still change anything here yourself.",
            ChatProblem.NoAnswer => $"{Brand} didn't answer. Try again.",
            ChatProblem.Guard => $"Egghead Notes stopped {Brand}: it tried to use a tool it isn't allowed here.",
            ChatProblem.Tools => $"Egghead Notes couldn't connect {Brand} to Settings.",
            _ => $"{Brand} stopped: {Py.Head(said, 160).TrimEnd('.')}.",
        };
    }

    /// <summary>Stops the answer being written.</summary>
    [RelayCommand]
    void Stop() => turn?.Cancel();

    /// <summary>A new conversation: the thread clears, and the AI starts afresh.</summary>
    [RelayCommand]
    void NewChat()
    {
        turn?.Cancel();
        Thread.Clear();
        session = "";
        Problem = null;
        ProblemNeedsSetup = false;
        OnPropertyChanged(nameof(ShowWelcome));
    }

    /// <summary>Guided setup, to install an AI or sign in again; the page looks again when it's next opened.</summary>
    [RelayCommand]
    void SetUp()
    {
        opened = false;
        services.RunSetup?.Invoke();
    }

    /// <summary>Looks for the student's AI again (after installing one).</summary>
    [RelayCommand]
    Task LookAgain() => LookAsync();

    public void Dispose()
    {
        closing.Cancel();
        closing.Dispose();
    }
}
