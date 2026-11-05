using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StudyStash.App.Services;
using StudyStash.Core.Ai;

namespace StudyStash.App.ViewModels;

/// <summary>The first run's screens, one at a time, in order (a student who isn't one, or whose school has no Canvas,
/// goes from <see cref="Student"/> straight to <see cref="Ai"/>).</summary>
public enum FunnelStep
{
    /// <summary>"Record your lectures. Get the notes." and Get started, which makes the library here.</summary>
    Welcome,
    /// <summary>"Are you a student?"</summary>
    Student,
    /// <summary>Connect your classes: Syft Notes added to the browser, and the student's Canvas found.</summary>
    Classes,
    /// <summary>The first sync, a line per stage.</summary>
    Sync,
    /// <summary>Connect your AI: Claude, ChatGPT or Gemini, or Continue with Google when there's no plan.</summary>
    Ai,
}

/// <summary>One dot of the step indicator: the screen it stands for, and whether it's passed or the one showing.</summary>
public sealed partial class FunnelDot : ObservableObject
{
    public FunnelStep Step { get; init; }
    [ObservableProperty] public partial bool Current { get; set; }
    [ObservableProperty] public partial bool Passed { get; set; }
}

/// <summary>Where the funnel reaches beyond itself: the app's own by default (Shell), tests give their own.</summary>
public sealed class FunnelServices
{
    /// <summary>Connect your classes with nothing typed (the add-on finds the library and Canvas); null where it can't
    /// (the stores' add-on is older than that, or no browser here has one), and the step asks for the school instead.</summary>
    public Func<AddOnModel?> MakeAddOn { get; init; } = () => null;
    /// <summary>Whether the add-on in this student's browser store finds Canvas by itself right now (the store's own
    /// answer, asked when the step opens); false takes the step that asks for the school's address. Always true by
    /// default, so a test of the other flow gives its own.</summary>
    public Func<Task<bool>> AddOnFindsCanvas { get; init; } = () => Task.FromResult(true);
    /// <summary>Canvas's connect flow (school, browser, courses), started at the step the library's state needs.</summary>
    public Func<Task<CanvasConnectModel?>> MakeCanvas { get; init; } = () => Task.FromResult<CanvasConnectModel?>(null);
    /// <summary>The sync screen's model over that flow.</summary>
    public Func<CanvasConnectModel, FirstSyncModel?> MakeSync { get; init; } = _ => null;
    /// <summary>Guided setup's two screens for getting an AI ready (its maker's helper installed, then its own sign-in,
    /// hidden), shown in this window.</summary>
    public Func<GuidedSetupModel>? MakeReady { get; init; }
    /// <summary>The library writes the notes, and answers questions, with this AI from now on. True when it took it.</summary>
    public Func<string, Task<bool>> UseAi { get; init; } = _ => Task.FromResult(true);
    /// <summary>What the library is for, once the student says.</summary>
    public Func<IReadOnlyList<string>, Task> SetUses { get; init; } = _ => Task.CompletedTask;
    /// <summary>Settings → Your AI's own Connect for an AI signed in here (<see cref="GuidedServices.Connect"/>), run in the
    /// background once it's picked; null: nothing is connected (a test).</summary>
    public Func<string, Task>? Connect { get; init; }
    /// <summary>The AI Get started found signed in here on a plan ("claude"), already writing the notes; "" for none.</summary>
    public Func<string> AlreadyHere { get; init; } = () => "";
}

/// <summary>How the first run ended, for Home: whether Canvas brought classes in, and which AI writes the notes ("" when
/// the student chose Later).</summary>
public sealed record FunnelEnd(bool Student, bool Canvas, string Ai);

/// <summary>
/// The first run, as one plain funnel: the welcome's one button (which makes the library on this computer, asks for
/// the microphone and starts the speech model, as before), "Are you a student?", connect your classes (the app adds
/// Syft Notes to the browser and finds the student's Canvas), the first sync with a line per stage, connect your AI
/// (Claude, ChatGPT or Gemini, with Continue with Google, free, as the highlighted default, and one more push to it
/// before anyone leaves without an AI), then straight to Home. One screen at a time, a small step indicator, Back on
/// every screen. Getting an AI ready is guided setup's own install and hidden sign-in, through
/// <see cref="ConnectAiAsync"/> alone.
/// </summary>
public sealed partial class FunnelModel : ObservableObject, IDisposable
{
    readonly FunnelServices services;
    bool finishing, readyWatched;

    public FunnelModel(SetupModel setup, FunnelServices services)
    {
        Setup = setup;
        this.services = services;
        // Get started's own ending (the library made, the microphone asked for, an AI already here picked) is the
        // welcome's end now, not the first run's.
        Setup.OnFinish = AfterGetStarted;
        Setup.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(SetupModel.Starting) or nameof(SetupModel.LibraryOk) or nameof(SetupModel.HasStartProblem)) OnPropertyChanged(nameof(WelcomeLabel));
        };
        Dots();
    }

    /// <summary>The welcome's words, the library Get started makes, and "Open Syft Notes when you log in".</summary>
    public SetupModel Setup { get; }

    [NotifyPropertyChangedFor(nameof(OnWelcome), nameof(OnStudent), nameof(OnClasses), nameof(OnSync), nameof(OnAi), nameof(StepLabel), nameof(CanGoBack),
        nameof(OnPage), nameof(ShowAiChoices))]
    [ObservableProperty]
    public partial FunnelStep Step { get; set; }

    public bool OnWelcome => Step == FunnelStep.Welcome;
    public bool OnStudent => Step == FunnelStep.Student;
    public bool OnClasses => Step == FunnelStep.Classes;
    public bool OnSync => Step == FunnelStep.Sync;
    public bool OnAi => Step == FunnelStep.Ai;
    /// <summary>A step past the welcome: the step indicator, the title and Back.</summary>
    public bool OnPage => !OnWelcome;

    /// <summary>Canvas is part of this run: a student whose school has it (until they say otherwise).</summary>
    [NotifyPropertyChangedFor(nameof(StepLabel))]
    [ObservableProperty]
    public partial bool WithCanvas { get; set; } = true;

    /// <summary>The student said they're one (false: not a student; null: not asked yet).</summary>
    [ObservableProperty] public partial bool? Student { get; set; }

    /// <summary>The steps this run goes through, in order.</summary>
    public IReadOnlyList<FunnelStep> Steps => WithCanvas
        ? [FunnelStep.Welcome, FunnelStep.Student, FunnelStep.Classes, FunnelStep.Sync, FunnelStep.Ai]
        : [FunnelStep.Welcome, FunnelStep.Student, FunnelStep.Ai];

    /// <summary>The step indicator's dots, one per step after the welcome.</summary>
    public ObservableCollection<FunnelDot> Indicator { get; } = [];

    /// <summary>"Step 2 of 4", counting from after the welcome.</summary>
    public string StepLabel => $"Step {Steps.ToList().IndexOf(Step)} of {Steps.Count - 1}";

    void Dots()
    {
        var steps = Steps.Skip(1).ToList();
        if (Indicator.Select(d => d.Step).SequenceEqual(steps)) { Mark(); return; }
        Indicator.Clear();
        foreach (var s in steps) Indicator.Add(new FunnelDot { Step = s });
        Mark();
    }

    void Mark()
    {
        int at = Steps.ToList().IndexOf(Step);
        foreach (var d in Indicator)
        {
            int i = Steps.ToList().IndexOf(d.Step);
            d.Current = i == at;
            d.Passed = i < at;
        }
    }

    partial void OnStepChanged(FunnelStep value)
    {
        Mark();
        _ = EnterAsync(value);
    }

    partial void OnWithCanvasChanged(bool value) => Dots();

    public bool CanGoBack => OnPage && !finishing;

    /// <summary>Back: the screen before this one (the welcome's button then just moves on: the library is made).</summary>
    [RelayCommand]
    void Back()
    {
        if (ConnectingAi)
        {
            Ready?.ByHandCommand.Execute(null);
            return;
        }
        if (OnAi && AskedAgain)
        {
            AskedAgain = false;
            return;
        }
        int i = Steps.ToList().IndexOf(Step);
        if (i > 0) Step = Steps[i - 1];
    }

    // --- 1 · the welcome -----------------------------------------------------------------------------------------------

    /// <summary>The welcome's button: Get started (making the library, as the one-page welcome did), or Continue when
    /// it's made and the student came Back to it.</summary>
    public string WelcomeLabel => Setup.LibraryOk && !Setup.Starting ? "Continue" : Setup.ContinueLabel;

    [RelayCommand]
    async Task GetStarted()
    {
        if (Setup.LibraryOk && !Setup.Starting && passedWelcome)
        {
            Step = FunnelStep.Student;
            return;
        }
        await Setup.NextCommand.ExecuteAsync(null);
    }

    bool passedWelcome;

    /// <summary>Get started finished: on to "Are you a student?".</summary>
    void AfterGetStarted()
    {
        passedWelcome = true;
        OnPropertyChanged(nameof(WelcomeLabel));
        AlreadyHere = services.AlreadyHere();
        Step = FunnelStep.Student;
    }

    // --- 2 · are you a student? ----------------------------------------------------------------------------------------

    [RelayCommand]
    async Task IAmAStudent()
    {
        Student = true;
        WithCanvas = true;
        Step = FunnelStep.Classes;
        await services.SetUses(["student"]);
    }

    /// <summary>Not a student: no Canvas; classes (folders) are added by hand on Home, which says so.</summary>
    [RelayCommand]
    void NotAStudent()
    {
        Student = false;
        WithCanvas = false;
        Step = FunnelStep.Ai;
    }

    /// <summary>A student whose school doesn't use Canvas (from either screen): their classes are added on Home.</summary>
    [RelayCommand]
    void NoCanvas()
    {
        Student = true;
        WithCanvas = false;
        Step = FunnelStep.Ai;
    }

    // --- 3 · connect your classes --------------------------------------------------------------------------------------

    /// <summary>Connect your classes with nothing typed (the add-on finds everything); null when the step asks for the
    /// school's address instead (<see cref="Canvas"/>'s own rows).</summary>
    [NotifyPropertyChangedFor(nameof(FindsItself), nameof(AsksSchool))]
    [ObservableProperty]
    public partial AddOnModel? AddOn { get; set; }

    /// <summary>Canvas's connect flow: the school's address, the browser and its code, on the step that asks for the
    /// school; on the one that doesn't, made once the add-on has found Canvas, to find the courses.</summary>
    [NotifyPropertyChangedFor(nameof(AsksSchool))]
    [ObservableProperty]
    public partial CanvasConnectModel? Canvas { get; set; }

    public bool FindsItself => AddOn is not null;
    public bool AsksSchool => AddOn is null && Canvas is not null;

    /// <summary>The first sync's screen.</summary>
    [ObservableProperty] public partial FirstSyncModel? Sync { get; set; }

    async Task EnterAsync(FunnelStep step)
    {
        switch (step)
        {
            case FunnelStep.Classes:
                if (AddOn is null && Canvas is null)
                {
                    if (await services.AddOnFindsCanvas() && services.MakeAddOn() is { } addOn)
                    {
                        addOn.OnDone = () =>
                        {
                            if (OnClasses) Step = FunnelStep.Sync;
                        };
                        // The add-on turned out older than 1.8 (or the student chose to type): the school's address instead.
                        addOn.OnTypeInstead = () => _ = TypeSchoolInsteadAsync();
                        AddOn = addOn;
                        await addOn.StartAsync();
                    }
                    else await StartTypingAsync();
                }
                else if (AddOn is { IsDone: true } || AddOn is null && Canvas is { Current: >= 3 })
                {
                    // Back to it after it's done: it says so, and Continue moves on.
                }
                break;
            case FunnelStep.Sync:
                Canvas ??= await services.MakeCanvas();
                if (Canvas is not null && Sync is null && services.MakeSync(Canvas) is { } sync)
                {
                    Sync = sync;
                    await sync.StartAsync();
                }
                break;
        }
    }

    /// <summary>The step that asks for the school's address once and gives the code the add-on takes.</summary>
    async Task StartTypingAsync()
    {
        if (await services.MakeCanvas() is { } canvas)
        {
            canvas.PropertyChanged += (_, e) =>
            {
                // The browser checked in: the courses are found next, on the sync screen.
                if (e.PropertyName == nameof(CanvasConnectModel.Current) && canvas.Current >= 3 && OnClasses) Step = FunnelStep.Sync;
            };
            Canvas = canvas;
            if (canvas.Current >= 3) Step = FunnelStep.Sync;
        }
    }

    /// <summary>From the no-typing step to the typing one, in the same place.</summary>
    async Task TypeSchoolInsteadAsync()
    {
        if (AddOn is not { } addOn || !OnClasses) return;
        AddOn = null;
        addOn.Dispose();
        await StartTypingAsync();
        // The add-on is in that browser from before: its code goes into its own button, with nothing to add.
        if (addOn.FromBefore) Canvas?.HasItAlready(addOn.Browser);
    }

    /// <summary>Continue on a step that's done (back on it after Back): the next one.</summary>
    [RelayCommand]
    void Next()
    {
        int i = Steps.ToList().IndexOf(Step);
        if (i >= 0 && i < Steps.Count - 1) Step = Steps[i + 1];
    }

    // --- 5 · connect your AI -------------------------------------------------------------------------------------------

    /// <summary>An AI Get started found signed in here on a plan, writing the notes already ("claude"); "" for none.</summary>
    [NotifyPropertyChangedFor(nameof(HasAlreadyHere), nameof(AlreadyHereName), nameof(AlreadyHereLine))]
    [ObservableProperty]
    public partial string AlreadyHere { get; set; } = "";
    public bool HasAlreadyHere => AlreadyHere.Length > 0;
    public string AlreadyHereName => HasAlreadyHere ? Engines.Name(AlreadyHere) : "";
    public string AlreadyHereLine => HasAlreadyHere ? $"{AlreadyHereName} is signed in on this {Setup.DeviceWord}, so it writes your notes." : "";

    public const string AiTitle = "Connect your AI";
    public const string AiLede = "Do you pay for Claude, ChatGPT or Gemini? Connect it, and Syft Notes uses it to write your notes.";
    public const string GoogleButton = "Continue with Google";
    public const string GoogleLine = "No plan? Gemini is free with your Google account.";
    public const string AgainTitle = "Notes need an AI";
    public const string AgainLede = "Gemini is free with your Google account. Syft Notes uses it to write your notes after every class.";

    /// <summary>The student tried to go on without an AI: one more push to Gemini, with Later quietly under it.</summary>
    [NotifyPropertyChangedFor(nameof(ShowAiChoices))]
    [ObservableProperty]
    public partial bool AskedAgain { get; set; }

    /// <summary>Guided setup's install and sign-in screens are showing in this window, for the AI picked.</summary>
    [NotifyPropertyChangedFor(nameof(ShowAiChoices), nameof(ShowFunnel))]
    [ObservableProperty]
    public partial bool ConnectingAi { get; set; }

    /// <summary>The choices (Claude, ChatGPT, Gemini, Continue with Google) show.</summary>
    public bool ShowAiChoices => OnAi && !AskedAgain && !ConnectingAi;
    /// <summary>The funnel's own pages show (not guided setup's screens in their place).</summary>
    public bool ShowFunnel => !ConnectingAi;

    /// <summary>Why the last AI didn't get connected (Google turned the account down), over the choices; null otherwise.</summary>
    [NotifyPropertyChangedFor(nameof(HasAiProblem))]
    [ObservableProperty]
    public partial string? AiProblem { get; set; }
    public bool HasAiProblem => !string.IsNullOrEmpty(AiProblem);

    /// <summary>Guided setup, for its two screens (made the first time an AI is picked).</summary>
    [ObservableProperty] public partial GuidedSetupModel? Ready { get; set; }

    /// <summary>The AI that's connected now ("gemini"), once it is.</summary>
    [ObservableProperty] public partial string Connected { get; set; } = "";

    /// <summary>Setup is over: Home, with how it ended.</summary>
    public Action<FunnelEnd>? OnFinish { get; set; }

    [RelayCommand] Task ContinueWithGoogle() => ConnectAiAsync("gemini");
    [RelayCommand] Task UseClaude() => ConnectAiAsync("claude");
    [RelayCommand] Task UseChatGpt() => ConnectAiAsync("codex");
    [RelayCommand] Task UseGemini() => ConnectAiAsync("gemini");

    /// <summary>
    /// Connects <paramref name="id"/> ("claude", "codex" or "gemini"): its maker's helper installed if it isn't here, then
    /// its own sign-in, hidden (the browser opens; no terminal), on guided setup's screens shown in this window. Once it's
    /// ready, the library writes the notes with it and setup ends on Home. This is the funnel's one way to connect an AI:
    /// whatever connects an AI fully goes here.
    /// </summary>
    public async Task ConnectAiAsync(string id)
    {
        if (finishing || services.MakeReady is null) return;
        AiProblem = null;
        AskedAgain = false;
        var ready = Ready ??= services.MakeReady();
        WatchReady(ready);
        ConnectingAi = true;
        await ready.GetReadyForNotesAsync(id);
    }

    void WatchReady(GuidedSetupModel ready)
    {
        if (readyWatched) return;
        readyWatched = true;
        ready.ReadyForNotes = id => _ = ReadyAsync(id);
        ready.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(GuidedSetupModel.Screen) && ready.Screen == GuidedScreen.Manual && !ready.AiReady && !finishing)
            {
                // Back, or "Carry on without it": the choices again, nothing changed.
                ConnectingAi = false;
            }
            if (e.PropertyName != nameof(GuidedSetupModel.PlanProblem) || ready.Cli.Id != "gemini" || finishing) return;
            if (ready.PlanProblem == ChatProblem.Plan)
            {
                // Google turned the account down (a school or work one): said over the choices.
                ConnectingAi = false;
                AiProblem = "Google doesn't let school and work accounts use Gemini from other apps. Continue with a personal Google account (like Gmail), or connect Claude or ChatGPT.";
            }
            else if (ready.PlanProblem == ChatProblem.Limit)
            {
                // Signed in, with this week's allowance used up: Gemini writes the notes once it's back.
                _ = ReadyAsync("gemini");
            }
        };
    }

    /// <summary>The AI is ready: the library writes with it, and setup ends on Home.</summary>
    async Task ReadyAsync(string id)
    {
        if (finishing) return;
        if (!await services.UseAi(id))
        {
            ConnectingAi = false;
            AiProblem = "Your library didn't answer. Try again in a moment.";
            return;
        }
        Connected = id;
        Finish(id);
    }

    /// <summary>"Continue with Claude": the AI Get started found here is the one, already writing the notes.</summary>
    [RelayCommand]
    void UseAlreadyHere()
    {
        if (!HasAlreadyHere) return;
        string id = AlreadyHere;
        Finish(id);
        // Connected all the way, as an AI signed in on setup's own screens is (in the background).
        if (services.Connect is { } connect) UiTasks.Run(() => connect(id), $"setup: connect {id}");
    }

    /// <summary>"Skip" under the choices: never straight out with no AI; one more push to Gemini first.</summary>
    [RelayCommand]
    void Skip() => AskedAgain = true;

    /// <summary>"Later", under that push: Home, with a Gemini card on it until an AI is connected.</summary>
    [RelayCommand]
    void Later() => Finish("");

    void Finish(string ai)
    {
        if (finishing) return;
        finishing = true;
        OnPropertyChanged(nameof(CanGoBack));
        OnFinish?.Invoke(new FunnelEnd(Student == true, WithCanvas && Sync is not null, ai));
    }

    public void Dispose()
    {
        AddOn?.Dispose();
        Sync?.Dispose();
        Canvas?.Dispose();
        Ready?.Dispose();
    }
}
