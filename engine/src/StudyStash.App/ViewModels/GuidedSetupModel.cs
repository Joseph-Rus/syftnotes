using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StudyStash.App.Services;
using StudyStash.Core;
using StudyStash.Core.Ai;
using StudyStash.Core.Setup;

namespace StudyStash.App.ViewModels;

/// <summary>Guided setup's screens, in order: pick an AI, install its CLI (skipped when it's here), sign in, then the
/// chat. <see cref="Manual"/> is today's setup by hand, in the same window.</summary>
public enum GuidedScreen
{
    PickAi,
    Install,
    SignIn,
    Chat,
    Manual,
}

/// <summary>The setup tools' door for this window: its address, its token, and a way to start counting a new turn.</summary>
public sealed record SetupDoor(string Url, string Token, Action NewTurn);

/// <summary>Where guided setup reaches beyond itself: finding, installing and signing in to a CLI, the plan check,
/// running a chat turn, opening pages and a terminal. The app's own by default; tests give fakes.</summary>
public sealed class GuidedServices
{
    public required string Home { get; init; }
    public bool Windows { get; init; } = OperatingSystem.IsWindows();
    public Func<AgentCliInfo, AgentFound> Find { get; init; } = cli => AgentInstall.Find(cli);
    public Func<AgentCliInfo, AgentInstall>? Installer { get; init; }
    public Func<AgentCliInfo, string, AgentSignIn> SignIn { get; init; } = (cli, exe) => new AgentSignIn(cli, exe);
    public Func<string, string, Version?, CancellationToken, Task<PlanCheck>>? CheckPlan { get; init; }
    public Func<SetupTurn, CancellationToken, IAsyncEnumerable<SetupChatEvent>> Turn { get; init; } = SetupChat.RunAsync;
    public Action<string> OpenUrl { get; init; } = url => Dialogs.OpenUrl(url);
    /// <summary>Runs a command in a terminal window for the student (the sign-in, when it needs one).</summary>
    public Action<string, IReadOnlyList<string>>? OpenTerminal { get; init; }
    /// <summary>How far the model's download has got, while it downloads.</summary>
    public Func<double?> Downloading { get; init; } = () => null;
    public Func<DateTime> Now { get; init; } = () => DateTime.Now;
    /// <summary>A line in the app's log (a turn Study Stash stopped, and why).</summary>
    public Action<string> Log { get; init; } = _ => { };
    /// <summary>Runs something on the window's thread (a CLI's events come from its own).</summary>
    public Action<Action> Post { get; init; } = a => Dispatcher.UIThread.Post(a);
    /// <summary>The ChatGPT and Claude desktop apps on this computer, as their own settings stand (read only).</summary>
    public Func<IReadOnlyList<AiAppState>> AiApps { get; init; } = () => [];
    /// <summary>Adds Study Stash to one of those apps' settings, as Settings → AI tool access's Connect does.</summary>
    public Func<string, AiAppChange>? ConnectAiApp { get; init; }

    public AgentInstall MakeInstaller(AgentCliInfo cli) =>
        Installer?.Invoke(cli) ?? new AgentInstall(cli, Windows, Path.Combine(Home, "logs", "setup-install.log"));

    public Task<PlanCheck> CheckPlanAsync(string provider, string exe, Version? version, CancellationToken ct) =>
        CheckPlan?.Invoke(provider, exe, version, ct) ?? SetupChat.CheckPlanAsync(provider, exe, version, SetupChat.Folder(Home), ct: ct);
}

/// <summary>What the student's presses in a card do: the guided setup driver's.</summary>
public interface IGuidedActions
{
    Task PressAsync(CardEntry card, string action);
}

/// <summary>
/// Guided setup (the design's Pick your AI → Install → Sign in → chat): the student picks Claude or ChatGPT, Study
/// Stash installs its CLI with its maker's installer only when they press Install, they sign in on the provider's own
/// page, and then their own AI, run headless and locked to Study Stash's setup tools, walks them through the rest in a
/// chat, with a checklist beside it that only Study Stash ticks. "Set up by hand" (today's setup, over the same
/// <see cref="SetupModel"/>, so nothing done is lost) is one click away throughout.
/// </summary>
public sealed partial class GuidedSetupModel : ObservableObject, IDisposable
{
    readonly GuidedServices services;
    readonly Func<AppSettings> settings;
    readonly Action<Action<AppSettings>> save;
    readonly List<string> pending = [];
    readonly HashSet<string> skipped = [];
    readonly CancellationTokenSource closing = new();
    CancellationTokenSource? work;
    AgentSignIn? signIn;
    string session = "";
    string lastPrompt = "";
    string? question;
    bool chatStarted, opened;
    /// <summary>The ChatGPT and Claude apps on this computer: asked when the window opens and after a Connect.</summary>
    IReadOnlyList<AiAppState> aiApps = [];
    /// <summary>Codex's plan check worked only with the student's own config.toml read (its sign-in is kept there): the
    /// chat's turns read it too, or each would say the sign-in didn't work.</summary>
    bool keepUserConfig;

    public GuidedSetupModel(SetupModel setup, GuidedServices services, Func<AppSettings> settings, Action<Action<AppSettings>> save)
    {
        Setup = setup;
        this.services = services;
        this.settings = settings;
        this.save = save;
        Setup.PropertyChanged += (_, _) => Refresh();
        Setup.Classes.CollectionChanged += (_, _) => Refresh();
        BuildSteps();
    }

    public SetupModel Setup { get; }
    public ObservableCollection<ChatEntry> Thread { get; } = [];
    public ObservableCollection<ChecklistRow> Checklist { get; } = [];
    public ObservableCollection<string> QuickReplies { get; } = [];
    /// <summary>The setup tools' door, while the window's open.</summary>
    public SetupDoor? Door { get; set; }
    /// <summary>What the student's presses in a card do.</summary>
    public IGuidedActions? Actions { get; set; }
    /// <summary>Open Study Stash: setup is finished (the window saves it and closes).</summary>
    public Action? OnFinish { get; set; }

    [ObservableProperty] public partial GuidedScreen Screen { get; set; } = GuidedScreen.PickAi;
    /// <summary>"claude", "codex", or "" (nothing picked yet: nothing is, on a first run).</summary>
    [ObservableProperty] public partial string Picked { get; set; } = "";
    [ObservableProperty] public partial AgentFound ClaudeFound { get; set; } = AgentFound.None;
    [ObservableProperty] public partial AgentFound CodexFound { get; set; } = AgentFound.None;

    // Install
    [ObservableProperty] public partial bool Installing { get; set; }
    [ObservableProperty] public partial string Phase { get; set; } = "";
    [ObservableProperty] public partial bool Installed { get; set; }
    [ObservableProperty] public partial string InstalledWords { get; set; } = "";
    [ObservableProperty] public partial string? InstallProblem { get; set; }
    [ObservableProperty] public partial InstallFailure InstallFailure { get; set; }
    [ObservableProperty] public partial string InstallOutput { get; set; } = "";
    [ObservableProperty] public partial bool ShowDetails { get; set; }

    // Sign in
    [ObservableProperty] public partial bool WaitingSignIn { get; set; }
    [ObservableProperty] public partial bool OfferTerminal { get; set; }
    [ObservableProperty] public partial bool SignedIn { get; set; }
    [ObservableProperty] public partial bool CheckingPlan { get; set; }
    /// <summary>Signed in, and the plan check passed: the AI is ready.</summary>
    [ObservableProperty] public partial bool AiReady { get; set; }
    [ObservableProperty] public partial string? SignInProblem { get; set; }
    [ObservableProperty] public partial ChatProblem PlanProblem { get; set; }
    [ObservableProperty] public partial string PlanProblemTitle { get; set; } = "";
    [ObservableProperty] public partial string PlanProblemText { get; set; } = "";

    // Chat
    [ObservableProperty] public partial string Draft { get; set; } = "";
    /// <summary>A turn is running: the field waits (what's typed goes with the next turn).</summary>
    [ObservableProperty] public partial bool Busy { get; set; }
    [ObservableProperty] public partial string? ChatProblemText { get; set; }
    [ObservableProperty] public partial ChatProblem ChatProblemKind { get; set; }
    [ObservableProperty] public partial bool ReadyToFinish { get; set; }
    [ObservableProperty] public partial CardEntry? OpenCard { get; set; }

    // Facts only Study Stash sets (the checklist reads them)
    [ObservableProperty] public partial bool RoleChosen { get; set; }
    [ObservableProperty] public partial string NotesWriter { get; set; } = "";
    [ObservableProperty] public partial bool TaskbarDone { get; set; }
    [ObservableProperty] public partial bool StartsAtLogin { get; set; }
    /// <summary>Setup by hand should have the free model on this computer picked for notes.</summary>
    public bool PreferOllama { get; private set; }

    public bool Again => Setup.Again;
    public string Device => Setup.DeviceWord;
    public AgentCliInfo Cli => AgentCli.Get(Picked == "codex" ? "codex" : "claude");
    public AgentCliInfo Other => Cli.Id == "codex" ? AgentCli.Claude : AgentCli.Codex;
    public string Brand => Cli.Brand;
    public AgentFound Found => Cli.Id == "codex" ? CodexFound : ClaudeFound;
    public IReadOnlySet<string> Skipped => skipped;

    // --- the words each screen shows (the design's) ------------------------------------------------------------------

    public string PickTitle => Again ? "Set up Study Stash again" : "Welcome to Study Stash";
    public static string PickLede => "An AI sets up Study Stash with you, writes your notes and answers your questions. Which do you have?";
    // The three answers, in the student's own terms: what they pay for, never the tools underneath (Claude Code, Codex,
    // Ollama), which the next screens name once, where they're installed.
    public static string ClaudeLine => "I have a paid Claude plan (Pro or Max).";
    public static string CodexLine => "I have a paid ChatGPT plan (Plus or higher).";
    public static string FreeTitle => "I don't have a paid plan";
    public string FreeLine => $"That's fine. A free AI that runs on this {Device} can write your notes, or Study Stash can just record and transcribe.";
    public string AlreadyHere => $"Already on this {Device}";
    public bool ClaudeHere => ClaudeFound.Works;
    public bool CodexHere => CodexFound.Works;
    public bool PickedClaude => Picked == "claude";
    public bool PickedCodex => Picked == "codex";
    /// <summary>No paid plan: Continue goes to setup by hand, with the free AI on this computer picked for the notes.</summary>
    public bool PickedFree => Picked == "free";

    /// <summary>Setup by hand's notes step asked for Claude or ChatGPT to be got ready on this computer: the two
    /// screens guided setup has for that (download and set up, sign in) show, and then it's back to that step with the
    /// AI picked, not on to the chat. "" otherwise.</summary>
    string forNotes = "";
    public bool ForNotes => forNotes.Length > 0;
    /// <summary>It's ready (its id): the by-hand notes step looks at it again and picks it.</summary>
    public Action<string>? ReadyForNotes { get; set; }
    /// <summary>The quiet link at the foot of those screens: back to the step they came from.</summary>
    public string ByHandLabel => ForNotes ? "Back" : "Set up by hand";
    public bool CanContinue => Screen switch
    {
        GuidedScreen.PickAi => Picked.Length > 0,
        GuidedScreen.Install => Installed,
        GuidedScreen.SignIn => AiReady,
        _ => false,
    };

    public string StepLabel => Screen switch
    {
        GuidedScreen.Install => $"Step 1 of 3 · {Brand}",
        GuidedScreen.SignIn => $"Step 2 of 3 · {Brand}",
        _ => "Step 3 of 3",
    };

    public string InstallTitle => $"Get {Brand} ready";
    public string InstallLede => $"Study Stash talks to {Brand} through {Cli.Name}, {Cli.Maker}'s own helper for apps. Study Stash will download it and set it up in your account. You won't need an administrator password.";
    public string InstallButton => "Download and set up";
    public string InstallSource => $"Installs with {Cli.Maker}'s own installer from {Cli.Site}";
    public string InstallLine => (Windows ? Cli.WindowsInstaller : Cli.MacInstaller).Line;
    public bool ShowInstallButton => !Installing && !Installed && InstallProblem is null;
    public bool HasInstallProblem => InstallProblem is not null;
    public bool InstallRegion => InstallFailure == InstallFailure.Region;
    public bool InstallOther => InstallFailure is InstallFailure.Other or InstallFailure.WontStart;
    public string UseOtherLabel => $"Use {Other.Brand} instead";

    public string SignInTitle => SignedIn ? $"You're signed in to {Brand}" : $"Sign in to {Brand}";
    public string SignInLede => SignedIn ? $"{Brand} is signed in on this {Device}. There's nothing more to do here."
        : Cli.Id == "codex"
        ? "Your browser will open ChatGPT's sign-in page. Sign in with the account that has your Plus plan (or higher), then come back here."
        : "Your browser will open Claude's sign-in page. Sign in with the account that has your Pro or Max plan, then come back here.";
    public string Privacy => $"Study Stash never sees your password. The sign-in is {Brand}'s own, and stays on this {Device}.";
    public string PlanNote => $"Your chat with {Brand} uses your {Brand} plan.";
    public string ReadyWords => $"{Brand} is ready.";
    /// <summary>Signed in, but on a plan other apps can't use (the free one): said as that, with what would work.</summary>
    public string PlanTooSmallTitle => $"This {Brand} account is on a plan that other apps can't use";
    public string PlanTooSmallText => Cli.Id == "codex"
        ? "Study Stash needs ChatGPT Plus or higher. Without one, a free AI can write your notes instead."
        : "Study Stash needs Claude Pro or Max. Without one, a free AI can write your notes instead.";
    public bool ShowOpenSignIn => !WaitingSignIn && !SignedIn && PlanProblem == ChatProblem.None;
    public bool HasSignInProblem => !string.IsNullOrEmpty(SignInProblem);
    public bool HasPlanProblem => PlanProblem != ChatProblem.None;
    public bool PlanIsPlan => PlanProblem == ChatProblem.Plan;

    public string HeaderTitle => Again ? "Set up Study Stash again" : "Let's set up Study Stash";
    public string SidebarTitle => $"Setup with {Brand}";
    public string FieldHint => $"Reply to {Brand}…";
    public string Thinking => $"{Brand} is thinking…";
    public bool HasChatProblem => !string.IsNullOrEmpty(ChatProblemText);
    public bool CanType => !Busy;
    public bool HasQuickReplies => QuickReplies.Count > 0;
    public bool Windows => services.Windows;

    public bool OnPick => Screen == GuidedScreen.PickAi;
    public bool OnInstall => Screen == GuidedScreen.Install;
    public bool OnSignIn => Screen == GuidedScreen.SignIn;
    public bool OnChat => Screen == GuidedScreen.Chat;
    public bool HasStepLabel => !OnPick;
    /// <summary>The design's size for each screen: 720×480, and 900×560 for the chat.</summary>
    public double ViewWidth => OnChat ? 900 : 720;
    public double ViewHeight => OnChat ? 560 : 480;
    public bool ShowInstalled => Installed;
    public bool ShowReady => AiReady && OnSignIn;
    public bool ShowCheckingPlan => CheckingPlan;
    public string CheckingWords => "Signed in. Checking your plan…";

    /// <summary>The sidebar's steps before the chat: your AI, installing it, signing in, then setting up.</summary>
    public ObservableCollection<StepItem> Steps { get; } = [];

    void BuildSteps()
    {
        int at = Screen switch { GuidedScreen.PickAi => 0, GuidedScreen.Install => 1, GuidedScreen.SignIn => 2, _ => 3 };
        // Without a paid plan there's nothing to install or sign in to: setup by hand comes next.
        string[] titles = PickedFree ? ["Pick your AI", "Set up Study Stash"] : ["Pick your AI", $"Get {Brand} ready", $"Sign in to {Brand}", "Set up Study Stash"];
        if (PickedFree) at = Math.Min(at, 1);
        if (Steps.Count != titles.Length)
        {
            Steps.Clear();
            for (int i = 0; i < titles.Length; i++) Steps.Add(new StepItem { Number = i + 1, Title = titles[i] });
        }
        for (int i = 0; i < titles.Length; i++)
        {
            if (Steps[i].Title != titles[i]) Steps[i] = new StepItem { Number = i + 1, Title = titles[i] };
            Steps[i].Current = i == at;
            Steps[i].Done = i < at || i == 1 && !PickedFree && at > 0 && Found.Works && !OnInstall;
        }
    }

    partial void OnScreenChanged(GuidedScreen value)
    {
        Notify(nameof(CanContinue), nameof(StepLabel), nameof(OnPick), nameof(OnInstall), nameof(OnSignIn), nameof(OnChat), nameof(HasStepLabel),
            nameof(ViewWidth), nameof(ViewHeight), nameof(ShowReady));
        BuildSteps();
    }

    partial void OnPickedChanged(string value)
    {
        BuildSteps();
        Notify(nameof(Cli), nameof(Other), nameof(Brand), nameof(Found), nameof(PickedClaude), nameof(PickedCodex), nameof(PickedFree), nameof(CanContinue), nameof(StepLabel),
            nameof(InstallTitle), nameof(InstallLede), nameof(InstallButton), nameof(InstallSource), nameof(InstallLine), nameof(UseOtherLabel), nameof(SignInTitle),
            nameof(SignInLede), nameof(Privacy), nameof(PlanNote), nameof(ReadyWords), nameof(SidebarTitle), nameof(FieldHint), nameof(Thinking));
    }

    partial void OnClaudeFoundChanged(AgentFound value)
    {
        Notify(nameof(ClaudeHere), nameof(Found));
        BuildSteps();
    }

    partial void OnCodexFoundChanged(AgentFound value)
    {
        Notify(nameof(CodexHere), nameof(Found));
        BuildSteps();
    }
    partial void OnInstallingChanged(bool value) => Notify(nameof(ShowInstallButton));
    partial void OnInstalledChanged(bool value) => Notify(nameof(ShowInstallButton), nameof(CanContinue), nameof(ShowInstalled));
    partial void OnInstallProblemChanged(string? value) => Notify(nameof(ShowInstallButton), nameof(HasInstallProblem));
    partial void OnInstallFailureChanged(InstallFailure value) => Notify(nameof(InstallRegion), nameof(InstallOther));
    partial void OnWaitingSignInChanged(bool value) => Notify(nameof(ShowOpenSignIn));
    partial void OnSignedInChanged(bool value) => Notify(nameof(ShowOpenSignIn), nameof(SignInTitle), nameof(SignInLede));
    partial void OnCheckingPlanChanged(bool value) => Notify(nameof(ShowCheckingPlan));

    partial void OnAiReadyChanged(bool value)
    {
        Notify(nameof(CanContinue), nameof(ShowReady));
        Refresh();
    }
    partial void OnSignInProblemChanged(string? value) => Notify(nameof(HasSignInProblem));
    partial void OnPlanProblemChanged(ChatProblem value) => Notify(nameof(HasPlanProblem), nameof(PlanIsPlan), nameof(ShowOpenSignIn));
    partial void OnBusyChanged(bool value) => Notify(nameof(CanType));
    partial void OnChatProblemTextChanged(string? value) => Notify(nameof(HasChatProblem));
    partial void OnRoleChosenChanged(bool value) => Refresh();
    partial void OnNotesWriterChanged(string value) => Refresh();
    partial void OnTaskbarDoneChanged(bool value) => Refresh();
    partial void OnStartsAtLoginChanged(bool value) => Refresh();
    partial void OnOpenCardChanged(CardEntry? value) => Refresh();

    void Notify(params string[] names)
    {
        foreach (string n in names) OnPropertyChanged(n);
    }

    // --- opening -------------------------------------------------------------------------------------------------------

    /// <summary>
    /// The window's opened: finds each CLI (and whether it works), then starts where it should. The first run, or no
    /// AI picked before: Pick your AI. An AI picked before that's still here: signed in, straight to the chat (picking
    /// the conversation up where it was); else its sign-in.
    /// </summary>
    public async Task OpenAsync()
    {
        opened = true;
        var (claude, codex, apps) = await Task.Run(() => (services.Find(AgentCli.Claude), services.Find(AgentCli.Codex), ReadAiApps()));
        ClaudeFound = claude;
        CodexFound = codex;
        aiApps = apps;
        Refresh();
        var s = settings();
        if (s.SetupAi is "claude" or "codex" && (s.SetupAi == "codex" ? codex : claude).Works)
        {
            Picked = s.SetupAi;
            if (s.SetupChat is { } saved && saved.Provider == Picked) session = saved.Session;
            RoleChosen = Again;
            var now = await CheckSignedInAsync();
            if (now.SignedIn)
            {
                SignedIn = true;
                AiReady = true;
                StartChat(Again ? "[Study Stash] Setup was opened again from Settings." : session.Length > 0 ? "[Study Stash] Setup was reopened." : "[Study Stash] Setup was opened.");
                return;
            }
            Screen = GuidedScreen.SignIn;
            return;
        }
        RoleChosen = Again;
        // One of them is on this computer already and the other isn't: it starts picked, so it's just Continue.
        if (Picked.Length == 0 && claude.Works != codex.Works) Picked = claude.Works ? "claude" : "codex";
        Screen = GuidedScreen.PickAi;
        Refresh();
    }

    /// <summary>Looks again at which of the ChatGPT and Claude apps are here, and whether each has Study Stash.</summary>
    public void LoadAiApps()
    {
        aiApps = ReadAiApps();
        Refresh();
    }

    IReadOnlyList<AiAppState> ReadAiApps()
    {
        try
        {
            return services.AiApps();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>The ChatGPT and Claude apps on this computer, by name, for the checklist and the card.</summary>
    public IReadOnlyList<string> AiAppNames => [.. aiApps.Select(a => ClaudeSetup.DesktopApp(a.Id)?.Name ?? a.Name)];
    public bool AiAppsConnected => aiApps.Count > 0 && aiApps.All(a => a.Added && !a.OtherCopy);

    /// <summary>The AI app card's Connect: Study Stash goes into each app's own settings (as Settings → AI tool access
    /// does it, keeping everything else in the file). Null when they're all connected; else why not, for the card.</summary>
    public string? ConnectAiApps()
    {
        const string later = "You can connect it later in Settings → AI tool access.";
        if (services.ConnectAiApp is not { } connect) return "Study Stash can't change that app's settings here. " + later;
        foreach (var app in aiApps.Where(a => !a.Added || a.OtherCopy || a.Outdated))
            if (!connect(app.Id).Ok) services.Log($"[setup] connecting {app.Name} didn't work");
        LoadAiApps();
        return AiAppsConnected ? null : "Study Stash couldn't change that app's settings. " + later;
    }

    [RelayCommand]
    void Pick(string id)
    {
        if (id is "claude" or "codex" or "free") Picked = id;
    }

    [RelayCommand]
    void OpenPlans(string id) => services.OpenUrl(AgentCli.Get(id.Length > 0 ? id : Picked).PlansUrl);

    [RelayCommand]
    void OpenDocs() => services.OpenUrl((Windows ? Cli.WindowsInstaller : Cli.MacInstaller).DocUrl);

    /// <summary>"No subscription?": today's setup by hand, with the free model on this computer picked for notes.</summary>
    [RelayCommand]
    void UseOllama()
    {
        if (ForNotes)
        {
            // Asked for from the notes step, which has the free AI's own button: back there.
            StopWork();
            BackToNotes(ready: false);
            return;
        }
        save(s => s.SetupAi = "ollama");
        PreferOllama = true;
        StopWork();
        Setup.Go(Setup.Steps[0].Step);
        Screen = GuidedScreen.Manual;
    }

    /// <summary>"Set up by hand", from any screen: today's setup in the same window, at the first step not done.
    /// Before an AI's ready, it's how this computer is set up; after, the chat stays to come back to.</summary>
    [RelayCommand]
    void ByHand() => ByHandAt("");

    /// <summary>From setup by hand's notes step, "Set it up" on Claude or ChatGPT: what guided setup does for them,
    /// with buttons (the maker's installer, then its own sign-in page), where that step used to give commands to paste
    /// into Terminal. Already here and signed in, it's straight back with it picked.</summary>
    public async Task GetReadyForNotesAsync(string id)
    {
        if (id is not ("claude" or "codex")) return;
        StopWork();
        forNotes = id;
        Picked = id;
        Notify(nameof(ForNotes), nameof(ByHandLabel));
        var found = await Task.Run(() => services.Find(AgentCli.Get(id)));
        if (id == "codex") CodexFound = found;
        else ClaudeFound = found;
        if (!found.Works)
        {
            ToInstall();
            return;
        }
        await ToSignInAsync();
        if (AiReady && Screen == GuidedScreen.SignIn) BackToNotes();
    }

    /// <summary>Back to the by-hand step that asked, the AI ready (or the student gave up on it: <paramref name="ready"/>
    /// false, and nothing is picked).</summary>
    void BackToNotes(bool ready = true)
    {
        string id = forNotes;
        forNotes = "";
        Notify(nameof(ForNotes), nameof(ByHandLabel));
        Screen = GuidedScreen.Manual;
        if (ready) ReadyForNotes?.Invoke(id);
    }

    void ByHandAt(string item)
    {
        if (ForNotes)
        {
            StopWork();
            BackToNotes(ready: false);
            return;
        }
        if (!AiReady && settings().SetupAi is not ("claude" or "codex" or "ollama")) save(s => s.SetupAi = "manual");
        if (!AiReady) StopWork();
        var items = SetupChecklist.From(Setup, Facts());
        Setup.Go(chatStarted ? SetupChecklist.StepFor(Setup, items, item) : Setup.Steps[0].Step);
        Screen = GuidedScreen.Manual;
    }

    /// <summary>Setup by hand's "Set up with Claude instead": back to the chat, which hears what was done meanwhile.</summary>
    [RelayCommand]
    void BackToChat()
    {
        if (!opened)
        {
            // Setup by hand came first (Settings' link): guided setup starts from the top.
            Screen = GuidedScreen.PickAi;
            _ = OpenAsync();
            return;
        }
        if (!AiReady || !chatStarted)
        {
            Screen = Picked.Length > 0 && AiReady ? GuidedScreen.Chat : GuidedScreen.PickAi;
            if (Screen == GuidedScreen.Chat) StartChat("[Study Stash] Setup was opened.");
            return;
        }
        Screen = GuidedScreen.Chat;
        Refresh();
        Note("You did some steps by hand", "The student did some steps by hand. Call get_setup_status to see where things are.");
    }

    /// <summary>Continue, on whichever screen: Pick → install (or sign in, when it's here), Install → Sign in, Sign in
    /// → the chat.</summary>
    [RelayCommand]
    async Task Continue()
    {
        switch (Screen)
        {
            case GuidedScreen.PickAi when PickedFree:
                UseOllama();
                break;
            case GuidedScreen.PickAi when Picked.Length > 0:
                save(s => s.SetupAi = Picked);
                if (Found.Works) await ToSignInOrChatAsync();
                else ToInstall();
                break;
            case GuidedScreen.Install when Installed:
                await ToSignInOrChatAsync();
                break;
            case GuidedScreen.SignIn when AiReady && ForNotes:
                BackToNotes();
                break;
            case GuidedScreen.SignIn when AiReady:
                StartChat("[Study Stash] Setup was opened.");
                break;
        }
    }

    /// <summary>The ChatGPT/Claude swap offered when one can't be used (its country, its plan).</summary>
    [RelayCommand]
    async Task UseOther()
    {
        StopWork();
        Picked = Other.Id;
        if (ForNotes) forNotes = Picked; // the notes step asked: it's the other one that goes back to it
        else save(s => s.SetupAi = Picked);
        session = ""; // the other AI can't carry on this one's conversation
        ResetSignIn();
        if (Found.Works) await ToSignInAsync();
        else ToInstall();
    }

    // --- install -------------------------------------------------------------------------------------------------------

    void ToInstall()
    {
        Installed = false;
        InstallProblem = null;
        InstallFailure = InstallFailure.None;
        Phase = "";
        Screen = GuidedScreen.Install;
    }

    /// <summary>Install: the maker's installer runs now (never before this press), then the CLI is checked.</summary>
    [RelayCommand]
    async Task Install()
    {
        if (Installing) return;
        var cts = Fresh();
        var install = services.MakeInstaller(Cli);
        Installing = true;
        InstallProblem = null;
        InstallFailure = InstallFailure.None;
        InstallOutput = "";
        Phase = $"Downloading {Cli.Name}…";
        install.Phase += p => services.Post(() => Phase = p);
        install.Printed += _ => services.Post(() => InstallOutput = install.Output);
        var outcome = await install.RunAsync(cts.Token);
        Installing = false;
        InstallOutput = install.Output;
        if (outcome.Ok)
        {
            Installed = true;
            InstalledWords = outcome.Words;
            if (Cli.Id == "codex") CodexFound = outcome.Found;
            else ClaudeFound = outcome.Found;
            return;
        }
        if (outcome.Failure == InstallFailure.Cancelled) return;
        InstallFailure = outcome.Failure;
        InstallProblem = outcome.Words;
    }

    [RelayCommand]
    Task TryAgain() => Screen switch
    {
        GuidedScreen.Install => InstallCommand.ExecuteAsync(null),
        GuidedScreen.SignIn => PlanProblem != ChatProblem.None ? CheckPlanAsync() : OpenSignInAsync(),
        _ => RetryTurnAsync(),
    };

    [RelayCommand]
    void ToggleDetails() => ShowDetails = !ShowDetails;

    // --- sign in -------------------------------------------------------------------------------------------------------

    void ResetSignIn()
    {
        signIn?.Dispose();
        signIn = null;
        keepUserConfig = false;
        SignedIn = false;
        AiReady = false;
        WaitingSignIn = false;
        OfferTerminal = false;
        CheckingPlan = false;
        SignInProblem = null;
        PlanProblem = ChatProblem.None;
    }

    async Task<AgentSignedIn> CheckSignedInAsync()
    {
        if (Found.Exe is not { } exe) return new AgentSignedIn(false);
        signIn ??= services.SignIn(Cli, exe);
        var s = signIn;
        return await Task.Run(s.Check);
    }

    /// <summary>On from picking (or installing): the sign-in screen, unless the AI turns out to be signed in already
    /// and its plan checks out (the copy inside the ChatGPT or Claude app usually is). Then there's nothing for the
    /// student to do there, so the chat starts.</summary>
    async Task ToSignInOrChatAsync()
    {
        await ToSignInAsync();
        if (!AiReady || Screen != GuidedScreen.SignIn) return;
        if (ForNotes) BackToNotes();
        else StartChat("[Study Stash] Setup was opened.");
    }

    /// <summary>The sign-in screen: already signed in goes straight to the plan check.</summary>
    async Task ToSignInAsync()
    {
        ResetSignIn();
        Screen = GuidedScreen.SignIn;
        if ((await CheckSignedInAsync()).SignedIn)
        {
            SignedIn = true;
            await CheckPlanAsync();
        }
    }

    /// <summary>"Open sign-in page": the CLI's own sign-in, hidden (it opens the browser itself), and a check every
    /// few seconds until it's done.</summary>
    [RelayCommand]
    Task OpenSignIn() => OpenSignInAsync();

    async Task OpenSignInAsync()
    {
        if (Found.Exe is not { } exe) return;
        SignInProblem = null;
        PlanProblem = ChatProblem.None;
        signIn ??= services.SignIn(Cli, exe);
        var s = signIn;
        if (!s.Start()) OfferTerminal = true;
        WaitingSignIn = true;
        var cts = Fresh();
        bool done = await s.WaitAsync(cts.Token, _ => services.Post(() => OfferTerminal |= s.NeedsTerminal));
        WaitingSignIn = false;
        if (!done) return;
        SignedIn = true;
        await CheckPlanAsync();
    }

    /// <summary>"Didn't open? Open it again": Codex's printed page finishes signing in by itself, so that page opens
    /// again; Claude Code's ends on a code to paste (there's no field for it here), so its sign-in runs once more and
    /// opens the page that finishes by itself.</summary>
    [RelayCommand]
    void OpenAgain()
    {
        if (Cli.Id == "codex" && signIn?.Url is { } url) services.OpenUrl(url);
        else signIn?.Restart();
    }

    /// <summary>"Sign in in Terminal instead": the same sign-in in a terminal window; the checks carry on.</summary>
    [RelayCommand]
    void SignInInTerminal()
    {
        signIn?.Stop();
        if (Found.Exe is { } exe) services.OpenTerminal?.Invoke(exe, Cli.SignInArgs);
    }

    [RelayCommand]
    Task CheckAgain() => CheckPlanAsync();

    /// <summary>One tiny turn with no tools, to catch a plan that doesn't include the CLI before the chat starts.</summary>
    async Task CheckPlanAsync()
    {
        if (Found.Exe is not { } exe) return;
        var cts = Fresh();
        CheckingPlan = true;
        PlanProblem = ChatProblem.None;
        SignInProblem = null;
        var check = await services.CheckPlanAsync(Cli.Id, exe, Found.Version, cts.Token);
        CheckingPlan = false;
        if (cts.IsCancellationRequested) return;
        if (check.Ok)
        {
            keepUserConfig = check.KeptConfig;
            AiReady = true;
            return;
        }
        switch (check.Problem)
        {
            case ChatProblem.Auth:
                SignedIn = false;
                SignInProblem = "That sign-in didn't work. Try again.";
                break;
            case ChatProblem.Plan:
                PlanProblemTitle = PlanTooSmallTitle;
                PlanProblemText = PlanTooSmallText;
                PlanProblem = ChatProblem.Plan;
                break;
            case ChatProblem.Limit:
                PlanProblemTitle = $"{Brand} is at its usage limit until {UntilWords(Engines.UntilFrom(check.Said, services.Now()))}.";
                PlanProblemText = "Set up by hand now, or come back then.";
                PlanProblem = ChatProblem.Limit;
                break;
            case ChatProblem.Offline:
                PlanProblemTitle = $"Study Stash couldn't reach {Brand}. Check your internet connection.";
                PlanProblemText = "";
                PlanProblem = ChatProblem.Offline;
                break;
            default:
                PlanProblemTitle = $"{Brand} didn't answer the check.";
                PlanProblemText = Py.Head(check.Said, 200);
                PlanProblem = ChatProblem.Other;
                break;
        }
    }

    /// <summary>"3 pm", "3:30 pm".</summary>
    public static string UntilWords(DateTime t) =>
        (t.Minute == 0 ? t.ToString("h tt", CultureInfo.InvariantCulture) : t.ToString("h:mm tt", CultureInfo.InvariantCulture)).ToLowerInvariant();

    // --- the chat --------------------------------------------------------------------------------------------------------

    /// <summary>The chat opens (the AI's ready): the checklist beside it, and the first turn.</summary>
    void StartChat(string first)
    {
        Screen = GuidedScreen.Chat;
        chatStarted = true;
        Refresh();
        _ = SendAsync(first);
    }

    /// <summary>Enter in the field: the student's words, as a turn now, or with the next one while one runs.</summary>
    [RelayCommand]
    Task Send()
    {
        string text = Draft.Trim();
        if (text.Length == 0) return Task.CompletedTask;
        Draft = "";
        return Say(text);
    }

    /// <summary>A tap on one of the AI's quick replies: sent as the student's own answer.</summary>
    [RelayCommand]
    Task Reply(string text) => Say(text);

    Task Say(string text)
    {
        QuickReplies.Clear();
        Notify(nameof(HasQuickReplies));
        var entry = new StudentEntry(text) { Queued = Busy };
        Thread.Add(entry);
        return SendAsync(text, entry);
    }

    /// <summary>A checklist line clicked: the AI is asked to go there.</summary>
    [RelayCommand]
    Task ClickItem(ChecklistRow row) => row.Clickable ? SendAsync($"[Study Stash] The student clicked {row.Title} in the checklist.") : Task.CompletedTask;

    /// <summary>
    /// A note from Study Stash about what happened (a card's button, a download that finished): a quiet line in the
    /// thread, and the AI hears it, as a turn now or with the next one.
    /// </summary>
    public void Note(string shown, string forAi, bool good = true)
    {
        Thread.Add(new NoteEntry(shown, good));
        Refresh();
        _ = SendAsync("[Study Stash] " + forAi);
    }

    readonly List<StudentEntry> queued = [];

    async Task SendAsync(string prompt, StudentEntry? typed = null)
    {
        if (Busy || HasChatProblem && ChatProblemKind == ChatProblem.Guard)
        {
            pending.Add(prompt);
            if (typed is not null)
            {
                typed.Queued = true;
                queued.Add(typed);
            }
            return;
        }
        await RunTurnAsync(prompt);
    }

    Task RetryTurnAsync()
    {
        ChatProblemText = null;
        ChatProblemKind = ChatProblem.None;
        string prompt = string.Join("\n\n", [lastPrompt, .. pending]);
        pending.Clear();
        return RunTurnAsync(prompt);
    }

    /// <summary>One turn: one run of the CLI with the prompt, its words and tools shown as they come.</summary>
    async Task RunTurnAsync(string prompt)
    {
        if (Found.Exe is not { } exe) return;
        if (Door is not { } door)
        {
            // The setup tools' door didn't open (the window logged why): say so, with the way out beside it.
            ShowTrouble(ChatProblem.Tools, "The setup tools' door isn't open.");
            return;
        }
        Busy = true;
        ChatProblemText = null;
        ChatProblemKind = ChatProblem.None;
        lastPrompt = prompt;
        question = null;
        door.NewTurn();
        string work = SetupChat.Folder(services.Home);
        string brief = SetupChat.Prepare(work, SetupChat.Brief(services.Windows, Again, Setup.IsLibrary && !RoleChosen && !Setup.LibraryOk), door.Url);
        var turn = new SetupTurn
        {
            Provider = Cli.Id, Exe = exe, Version = Found.Version, WorkDir = work, BriefFile = brief, McpUrl = door.Url, Token = door.Token,
            Session = session, Prompt = prompt, Windows = services.Windows, IgnoreUserConfig = !keepUserConfig,
        };
        var ai = new AiEntry(Brand);
        bool shown = false;
        ChatProblem problem = ChatProblem.None;
        string said = "";
        var cts = Fresh();
        try
        {
            await foreach (var e in services.Turn(turn, cts.Token).WithCancellation(cts.Token))
            {
                switch (e.Kind)
                {
                    case "Session" when e.Text.Length > 0:
                        session = e.Text;
                        string id = Cli.Id, sid = session;
                        save(s => s.SetupChat = new SetupChatSaved(id, sid));
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
        catch (OperationCanceledException)
        {
        }
        if (question is { } q && !(ai.Text.Contains(q, StringComparison.Ordinal)))
        {
            if (!shown) Thread.Add(ai);
            shown = true;
            ai.Text = ai.Text.Length > 0 ? ai.Text.TrimEnd() + "\n\n" + q : q;
        }
        Busy = false;
        if (cts.IsCancellationRequested) return;
        foreach (var t in queued) t.Queued = false;
        queued.Clear();
        if (problem != ChatProblem.None)
        {
            ShowTrouble(problem, said);
            return;
        }
        if (pending.Count > 0)
        {
            string next = string.Join("\n\n", pending);
            pending.Clear();
            await RunTurnAsync(next);
        }
    }

    void ShowTrouble(ChatProblem kind, string said)
    {
        // What the CLI said holds no secrets (the token only ever reaches its environment): kept for support.
        services.Log($"[setup] {Cli.Name} turn stopped: {kind}: {Py.Head(said, 300)}");
        switch (kind)
        {
            case ChatProblem.Auth:
                SignedIn = false;
                AiReady = false;
                Screen = GuidedScreen.SignIn;
                SignInProblem = "That sign-in didn't work. Try again.";
                return;
            case ChatProblem.Plan:
                AiReady = false;
                Screen = GuidedScreen.SignIn;
                PlanProblemTitle = PlanTooSmallTitle;
                PlanProblemText = PlanTooSmallText;
                PlanProblem = ChatProblem.Plan;
                return;
        }
        ChatProblemKind = kind;
        ChatProblemText = kind switch
        {
            ChatProblem.Offline => $"{Brand} couldn't be reached. Check your internet connection.",
            ChatProblem.Limit => $"{Brand} is at its usage limit until {UntilWords(Engines.UntilFrom(said, services.Now()))}. You can finish by hand; what's done stays done.",
            ChatProblem.NoAnswer => $"{Brand} didn't answer.",
            ChatProblem.Guard => $"Study Stash stopped {Brand}: it tried to use a tool it isn't allowed here.",
            ChatProblem.Tools => $"Study Stash couldn't connect {Brand} to setup.",
            _ => $"{Brand} stopped: {Py.Head(said, 160).TrimEnd('.')}.",
        };
    }

    // --- what the setup tools show ----------------------------------------------------------------------------------------

    /// <summary>ask_student: the question's answers as buttons (the question itself shows in the AI's words).</summary>
    public void ShowQuestion(string text, IReadOnlyList<string> choices)
    {
        question = text;
        QuickReplies.Clear();
        foreach (string c in choices) QuickReplies.Add(c);
        Notify(nameof(HasQuickReplies));
    }

    /// <summary>offer_…: the card shows; the one before folds to its outcome.</summary>
    public CardEntry ShowCard(SetupCard card)
    {
        if (OpenCard is { } before) before.Open = false;
        var entry = new CardEntry(this, card);
        Thread.Add(entry);
        OpenCard = entry;
        return entry;
    }

    /// <summary>A card's button: what it does is the driver's.</summary>
    public async Task PressAsync(CardEntry card, string action)
    {
        if (Actions is not { } actions || card.Busy) return;
        await actions.PressAsync(card, action);
        Refresh();
    }

    /// <summary>skip_step, or a card's "Not now".</summary>
    public void Skip(string step)
    {
        skipped.Add(step);
        // Setup's Finish turns start at login on when its box is ticked (a library that was here before ticks it): left
        // for later means it stays as it is.
        if (step == "start_at_login") Setup.StartAtLogin = false;
        Refresh();
    }

    /// <summary>open_manual_setup: setup by hand at that item, the chat kept to come back to.</summary>
    public void OpenManual(string item) => ByHandAt(item);

    /// <summary>Open Study Stash (the finish card, or the checklist's own button once everything needed is done).</summary>
    [RelayCommand]
    void Finish()
    {
        if (!ReadyToFinish) return;
        StopWork();
        OnFinish?.Invoke();
    }

    // --- the checklist ------------------------------------------------------------------------------------------------------

    public ChecklistFacts Facts() => new()
    {
        Ai = Cli.Id, AiReady = AiReady, Windows = services.Windows, RoleChosen = RoleChosen, NotesWriter = NotesWriter, Skipped = skipped,
        TaskbarDone = TaskbarDone, StartsAtLogin = StartsAtLogin, Downloading = Setup.ModelReady ? null : services.Downloading(),
        OpenCard = OpenCard is { Open: true } c ? c.Kind : "", BrowserConnected = Setup.Canvas?.BrowserConnected == true, Browser = Setup.Canvas?.BrowserName ?? "",
        CoursesFound = Setup.Canvas?.Found.Count ?? Setup.Courses.Count,
        AiApps = AiAppNames, AiAppsConnected = AiAppsConnected,
    };

    public IReadOnlyList<ChecklistItem> Items() => SetupChecklist.From(Setup, Facts());

    /// <summary>Works the checklist out again from what's so now (it only ever moves on real state).</summary>
    public void Refresh()
    {
        var items = Items();
        if (Checklist.Select(r => r.Id).SequenceEqual(items.Select(i => i.Id)))
        {
            for (int i = 0; i < items.Count; i++) Fill(Checklist[i], items[i]);
        }
        else
        {
            Checklist.Clear();
            foreach (var item in items)
            {
                var row = new ChecklistRow(item.Id);
                Fill(row, item);
                Checklist.Add(row);
            }
        }
        ReadyToFinish = SetupChecklist.ReadyToFinish(items);
    }

    static void Fill(ChecklistRow row, ChecklistItem item)
    {
        row.Title = item.Title;
        row.Detail = item.Detail;
        row.State = item.State;
        row.Optional = item.Optional;
        row.Recommended = item.Recommended;
    }

    /// <summary>The finish card's recap: each item done, in a few words.</summary>
    public IReadOnlyList<string> Recap() =>
        [.. Items().Where(i => i.State is ChecklistState.Done or ChecklistState.Now && i.Id != "ai").Select(i => i.Detail.Length > 0 ? $"{i.Title} · {i.Detail}" : i.Title)];

    // --- ending ------------------------------------------------------------------------------------------------------------------

    CancellationTokenSource Fresh()
    {
        work?.Cancel();
        work = CancellationTokenSource.CreateLinkedTokenSource(closing.Token);
        return work;
    }

    void StopWork()
    {
        work?.Cancel();
        signIn?.Stop();
    }

    /// <summary>The window's closing: whatever's running (an installer, a sign-in, a turn) stops.</summary>
    public void Dispose()
    {
        closing.Cancel();
        signIn?.Dispose();
        signIn = null;
    }
}
