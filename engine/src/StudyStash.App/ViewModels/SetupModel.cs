using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StudyStash.App.Services;

namespace StudyStash.App.ViewModels;

/// <summary>Setup's pages, in the order they come in any flow (each flow uses some of them).</summary>
public enum SetupStep
{
    /// <summary>What this computer is for, and what setting it up involves.</summary>
    Welcome,
    /// <summary>The library: name it and give it a password (Continue creates it).</summary>
    Password,
    /// <summary>The laptop: find and connect to your library.</summary>
    Library,
    /// <summary>The library: who writes the notes and answers questions (design 15).</summary>
    Ai,
    Microphone,
    Model,
    /// <summary>Optional: bring in Canvas (design 07).</summary>
    Canvas,
    Classes,
    /// <summary>The library: start Study Stash at login, so the laptop can always reach it (off unless ticked).</summary>
    StartAtLogin,
    /// <summary>Windows, on a computer that records: pin the tray icon out of the overflow.</summary>
    Taskbar,
    /// <summary>The end: how to connect your laptop (library), or how to record (laptop).</summary>
    Done,
}

/// <summary>One way a laptop can reach this library, on setup's last page: "At home · http://mac-mini.local:8787".</summary>
public sealed record SetupAddress(string Where, string Url);

/// <summary>A step in setup's sidebar: done (a check), the one you're on, or still to come.</summary>
public sealed partial class StepItem : ObservableObject
{
    public SetupStep Step { get; init; }
    public int Number { get; init; }
    public string Title { get; init; } = "";
    public bool Optional { get; init; }
    [ObservableProperty] public partial bool Done { get; set; }
    [ObservableProperty] public partial bool Current { get; set; }

    public bool Todo => !Done && !Current;
    public bool ShowNumber => !Done;
    /// <summary>"Optional" until the step's been passed (then its check says enough).</summary>
    public bool ShowOptional => Optional && !Done;

    partial void OnDoneChanged(bool value)
    {
        OnPropertyChanged(nameof(Todo));
        OnPropertyChanged(nameof(ShowNumber));
        OnPropertyChanged(nameof(ShowOptional));
    }

    partial void OnCurrentChanged(bool value) => OnPropertyChanged(nameof(Todo));
}

/// <summary>A class you're adding in setup, and when it meets: "CS 101, Tue Thu 10:00–11:15".</summary>
public sealed partial class SetupClass : ObservableObject
{
    [ObservableProperty] public partial string Name { get; set; } = "";
    [ObservableProperty] public partial string When { get; set; } = "";
    public IBrush Dot { get; init; } = Brushes.Gray;
}

/// <summary>A Canvas course setup found, as the class it would become: ticked to add it, and when it meets (optional).</summary>
public sealed partial class SetupCourse : ObservableObject
{
    public string Id { get; init; } = "";
    /// <summary>The class's name: the course's code ("CS 101"), or its name when it has no code.</summary>
    public string Name { get; init; } = "";
    /// <summary>The course's name on Canvas ("Intro to Programming"), under the class's.</summary>
    public string CourseName { get; init; } = "";
    public bool HasCourseName => CourseName.Length > 0 && CourseName != Name;
    [ObservableProperty] public partial bool Ticked { get; set; } = true;
    [ObservableProperty] public partial string When { get; set; } = "";
}

/// <summary>
/// First-run setup, as one of three flows the welcome offers. Just this computer (<see cref="AppRole.Both"/>, the
/// recommended one): the microphone, the transcription model, who writes the notes, Canvas, your classes and starting
/// at login, with the library made quietly on this computer and reachable only from it. The laptop: your library
/// elsewhere, the microphone, the model, Canvas, your classes and, on Windows, the taskbar. The library (a computer
/// that stays on): a password, the AI engines, Canvas, your classes, starting at login, and how to connect your laptop.
/// </summary>
public sealed partial class SetupModel : ObservableObject
{
    public ObservableCollection<StepItem> Steps { get; } = [];
    [ObservableProperty] public partial SetupStep Step { get; set; }
    /// <summary>What this computer is for: set by <see cref="SetRole"/>, which also rebuilds <see cref="Steps"/>.</summary>
    [ObservableProperty] public partial AppRole Role { get; set; } = AppRole.Both;
    SkinKind skin = SkinKind.Mac;

    /// <summary>Just this computer: it records, keeps the library and writes the notes, with no server.</summary>
    public bool IsOneComputer => Role == AppRole.Both;
    /// <summary>A library for other computers (the Mac mini at home), which doesn't record.</summary>
    public bool IsLibrary => Role == AppRole.Library;
    public bool IsLaptop => Role == AppRole.Laptop;
    /// <summary>The library lives on this computer (made here in setup): just this computer, or a library.</summary>
    public bool LibraryIsHere => Role != AppRole.Laptop;
    public string HeaderTitle => IsLibrary ? "Set up your library" : $"Set up Study Stash on this {DeviceWord}";
    public string FlowName => Role switch { AppRole.Library => "Library setup", AppRole.Laptop => "Laptop setup", _ => "One-computer setup" };
    public string FlowIcon => Role switch { AppRole.Library => "dns", AppRole.Laptop => "laptop_mac", _ => "desktop_windows" };
    public string WelcomeTitle => "Welcome to Study Stash";
    public string WelcomeBody => "How will you use it? You can add another computer later.";
    // The welcome's three choices, in the student's words.
    public string OneComputerAbout => $"Records, transcribes and writes your notes, all on this {DeviceWord}. No server needed; notes are written while it's on.";
    public static string LaptopAbout => "Records lectures and sends them to your library on another computer.";
    public string LibraryAbout => skin == SkinKind.Mac
        ? "A computer that stays on, like a Mac mini. It keeps your lectures and writes the notes."
        : "A computer that stays on at home. It keeps your lectures and writes the notes.";

    // Microphone
    [ObservableProperty] public partial bool MicAllowed { get; set; }
    [ObservableProperty] public partial bool MicDenied { get; set; }
    /// <summary>The mic check's bars, oldest first, 0 to 1 (24 of them).</summary>
    [ObservableProperty] public partial IReadOnlyList<double>? MicLevels { get; set; }
    /// <summary>A level has passed the "hears you" mark since the mic check opened.</summary>
    [ObservableProperty] public partial bool MicHeard { get; set; }
    public string MicLine => MicHeard ? "Study Stash hears you." : "Say something. The bars move when Study Stash hears you.";

    // The library: created here (library flow) or connected to (laptop flow)
    [ObservableProperty] public partial string Address { get; set; } = "";
    [ObservableProperty] public partial string Password { get; set; } = "";
    [ObservableProperty] public partial string LibraryName { get; set; } = "";
    /// <summary>The password fields show what's typed.</summary>
    [ObservableProperty] public partial bool ShowPassword { get; set; }
    [ObservableProperty] public partial string? LibraryResult { get; set; }
    /// <summary>The last page's library row: "Connected to Ada's library" (a laptop), or "Ready on this Mac" (just this
    /// computer), as a value in a list (no full stop).</summary>
    public string LibrarySummary => LibraryIsHere ? (LibraryOk ? $"Ready on this {DeviceWord}" : "") : LibraryResult?.TrimEnd('.') ?? "";
    /// <summary>Just this computer's welcome couldn't make the library: why, under the choices.</summary>
    public bool HasWelcomeProblem => IsOneComputer && HasLibraryResult && !LibraryOk && !Connecting;
    /// <summary>The welcome's line under the choices shows only while the library is being made, or why it wasn't.</summary>
    public bool ShowWelcomeStatus => IsOneComputer && Connecting || HasWelcomeProblem;
    /// <summary>Who writes the notes, as the AI step left it ("Ollama", "Not yet: just transcripts"), for just this
    /// computer's last page.</summary>
    [ObservableProperty] public partial string NotesSummary { get; set; } = "";
    public bool ShowNotesSummary => IsOneComputer && NotesSummary.Length > 0;
    partial void OnNotesSummaryChanged(string value) => OnPropertyChanged(nameof(ShowNotesSummary));
    /// <summary>This flow's library is ready: created here (library) or connected to (laptop).</summary>
    [ObservableProperty] public partial bool LibraryOk { get; set; }
    [ObservableProperty] public partial bool Connecting { get; set; }
    /// <summary>Looking for a library on this computer or your Tailscale network ("Find it").</summary>
    [ObservableProperty] public partial bool Finding { get; set; }
    /// <summary>Start Study Stash when the student logs in: off unless they tick it (or the library was already here,
    /// or it already started at login).</summary>
    [ObservableProperty] public partial bool StartAtLogin { get; set; }
    /// <summary>This computer already has a library (setup run again): the password step keeps what it has.</summary>
    [ObservableProperty] public partial bool ExistingLibrary { get; set; }
    /// <summary>Where the library keeps its notes: the one it already has, or Documents/Study Stash for a new one.</summary>
    [ObservableProperty] public partial string NotesFolder { get; set; } = "";
    public string PasswordTitle => ExistingLibrary ? "Your library is already here" : "Give your library a password";
    public string PasswordLede => ExistingLibrary
        ? "It keeps its name, password and notes. Change them here if you like; your laptop connects with this password."
        : "Your laptop uses it to connect. Anyone with it can read your notes.";
    /// <summary>"Keeps its notes in ~/Documents/Lecture notes" (a library already here) or "Keeps its notes in
    /// ~/Documents/Study Stash".</summary>
    public string NotesFolderWords => NotesFolder.Length == 0 ? "" : $"{(ExistingLibrary ? "Its notes stay in" : "Keeps its notes in")} {Tilde(NotesFolder)}";

    /// <summary>A folder in the student's home written from "~" (shorter, and it doesn't spell out the account name).</summary>
    public static string Tilde(string path)
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return home.Length > 0 && path.StartsWith(home, StringComparison.Ordinal) ? "~" + path[home.Length..] : path;
    }

    partial void OnExistingLibraryChanged(bool value)
    {
        OnPropertyChanged(nameof(ContinueLabel));
        OnPropertyChanged(nameof(PasswordTitle));
        OnPropertyChanged(nameof(PasswordLede));
        OnPropertyChanged(nameof(NotesFolderWords));
    }

    partial void OnNotesFolderChanged(string value) => OnPropertyChanged(nameof(NotesFolderWords));
    /// <summary>Where a laptop reaches this library, for the library's last page (the host fills it in).</summary>
    public ObservableCollection<SetupAddress> Addresses { get; } = [];
    public string PasswordText => ShowPassword ? Password : new string('•', Math.Clamp(Password.Length, 8, 16));
    public string ShowPasswordText => ShowPassword ? "Hide" : "Show";
    public string StartAtLoginTitle => IsLibrary ? "Keep your library running" : "Keep Study Stash running";
    public string StartAtLoginLede => IsLibrary
        ? "Start Study Stash when this computer starts, so your laptop can always reach it."
        : $"Start Study Stash when you log in, so your library is ready and your notes get written whenever this {DeviceWord} is on.";
    public string StartAtLoginHint => IsLibrary ? "Recommended for a library." : "Recommended.";
    /// <summary>Just this computer's last page: growing into a laptop and a library later.</summary>
    public string AddLaptopLater => "Want to record on a laptop too? Add it any time in Settings → Your library.";
    public string RecordHint => skin == SkinKind.Mac
        ? "Press ⌥⇧R or click Record in the menu bar when class starts."
        : "Press Ctrl+Alt+R or click Record in the tray when class starts.";

    // Model
    [ObservableProperty] public partial string ModelName { get; set; } = "Whisper large-v3";
    [ObservableProperty] public partial string ModelSize { get; set; } = "3 GB";
    [ObservableProperty] public partial double ModelProgress { get; set; }
    [ObservableProperty] public partial string ModelDone { get; set; } = "";
    [ObservableProperty] public partial string ModelLeft { get; set; } = "";
    [ObservableProperty] public partial bool ModelReady { get; set; }
    [ObservableProperty] public partial string? ModelProblem { get; set; }
    /// <summary>The laptop's last page: the model's state in a few words.</summary>
    public string ModelSummary => ModelReady ? "Ready" : HasModelProblem ? "Not downloaded yet" : "Downloading in the background";

    // Classes
    public ObservableCollection<SetupClass> Classes { get; } = [];
    /// <summary>The courses the Canvas step found, each a class to add (ticked) with an optional "when"; empty when
    /// Canvas was skipped, and then Classes is the plain add-your-own step.</summary>
    public ObservableCollection<SetupCourse> Courses { get; } = [];
    public bool HasCourses => Courses.Count > 0;
    public string ClassesLede => HasCourses
        ? "Your Canvas courses become your classes. Add when each meets, so Record picks the class that's on, and untick any you don't record."
        : "With your timetable, Record picks the class that's on, so each lecture lands in the right place. You can skip this.";
    /// <summary>Continue on Classes is adding the ticked courses and linking them to Canvas.</summary>
    [ObservableProperty] public partial bool AddingCourses { get; set; }
    [ObservableProperty] public partial string NewClass { get; set; } = "";
    [ObservableProperty] public partial string NewWhen { get; set; } = "";
    /// <summary>The times typed couldn't be read: how to write them.</summary>
    [ObservableProperty] public partial string? ClassProblem { get; set; }
    public bool HasClassProblem => !string.IsNullOrEmpty(ClassProblem);
    partial void OnClassProblemChanged(string? value) => OnPropertyChanged(nameof(HasClassProblem));

    // AI engines and Canvas: each step's own model, made by the host when the step opens (the library is known then).
    [ObservableProperty] public partial AiSetupModel? Ai { get; set; }
    [ObservableProperty] public partial CanvasConnectModel? Canvas { get; set; }

    public string DeviceWord => skin == SkinKind.Mac ? "Mac" : "PC";
    public int Count => Steps.Count;
    public int Index => Steps.ToList().FindIndex(s => s.Step == Step) + 1;
    public bool IsLast => Index == Count;
    public string StepLabel =>
        OnWelcome ? "Welcome"
        : OnAi && IsLibrary ? $"Library setup · step {Index} of {Count}"
        : $"Step {Index} of {Count}";
    public string ContinueLabel =>
        IsLast ? "Open Study Stash"
        : OnPassword && !LibraryOk ? (ExistingLibrary ? "Start library" : "Create library")
        : OnLibrary && !LibraryOk ? "Connect"
        : "Continue";
    public bool CanGoBack => Index > 1;

    public bool OnWelcome => Step == SetupStep.Welcome;
    public bool OnPassword => Step == SetupStep.Password;
    public bool OnMicrophone => Step == SetupStep.Microphone;
    public bool OnLibrary => Step == SetupStep.Library;
    public bool OnModel => Step == SetupStep.Model;
    public bool OnClasses => Step == SetupStep.Classes;
    public bool OnStartAtLogin => Step == SetupStep.StartAtLogin;
    public bool OnTaskbar => Step == SetupStep.Taskbar;
    public bool OnAi => Step == SetupStep.Ai;
    public bool OnCanvas => Step == SetupStep.Canvas;
    public bool OnDone => Step == SetupStep.Done;
    public bool OnLibraryDone => OnDone && IsLibrary;
    /// <summary>The last page of a computer that records (a laptop, or just this computer): how to record.</summary>
    public bool OnLaptopDone => OnDone && !IsLibrary;
    /// <summary>Just this computer's last page also says how to add a laptop later.</summary>
    public bool OnOneComputerDone => OnDone && IsOneComputer;
    /// <summary>The steps drawn the setup's own way (not the AI and Canvas panes, which bring their own title).</summary>
    public bool OnPlainStep => !OnAi && !OnCanvas;
    /// <summary>The AI and Canvas steps need a bigger window than the rest (the design's 900 wide).</summary>
    public bool Wide => OnAi || OnCanvas;
    /// <summary>Continue waits while Canvas is finding courses or linking one, and while the library is being made or
    /// reached.</summary>
    public bool CanContinue => (!OnCanvas || Canvas?.CanFinish != false) && !Connecting && !AddingCourses;
    public bool HasLibraryResult => !string.IsNullOrEmpty(LibraryResult);
    /// <summary>The library's name and password can't change once it's made here (Settings changes them later).</summary>
    public bool CanEditLibrary => !LibraryOk && !Connecting;
    public bool HasModelProblem => !string.IsNullOrEmpty(ModelProblem);
    public bool ModelDownloading => !ModelReady && !HasModelProblem;
    public double ModelBarWidth => Math.Clamp(ModelProgress, 0, 1) * 440;
    public double ModelBarWidthWin => Math.Clamp(ModelProgress, 0, 1) * 440;
    public string ModelBody => $"Study Stash turns speech into text on this {DeviceWord}, so your recordings never leave it. The model is about {ModelSize} and only downloads once.";

    /// <summary>Setup in <paramref name="skin"/>'s look, starting on <paramref name="role"/>'s choice (just this
    /// computer unless said).</summary>
    public static SetupModel For(SkinKind skin, AppRole role = AppRole.Both)
    {
        var m = new SetupModel { skin = skin };
        m.SetRole(role);
        return m;
    }

    /// <summary>The welcome's choice to start on: the installer's library download starts on "This is my library";
    /// the laptop download, or a build that doesn't say, on "Just this computer" (the one most students want).</summary>
    public static AppRole Suggested(AppRole? installer) => installer == AppRole.Library ? AppRole.Library : AppRole.Both;

    /// <summary>The steps of each flow, in order. Windows adds the taskbar where Study Stash records.</summary>
    public static IReadOnlyList<SetupStep> StepsFor(AppRole role, SkinKind skin)
    {
        var steps = new List<SetupStep> { SetupStep.Welcome };
        steps.AddRange(role switch
        {
            AppRole.Laptop => [SetupStep.Library, SetupStep.Microphone, SetupStep.Model],
            AppRole.Library => [SetupStep.Password, SetupStep.Ai],
            _ => [SetupStep.Microphone, SetupStep.Model, SetupStep.Ai],
        });
        steps.AddRange([SetupStep.Canvas, SetupStep.Classes]);
        if (role != AppRole.Laptop) steps.Add(SetupStep.StartAtLogin);
        if (role != AppRole.Library && skin == SkinKind.Win) steps.Add(SetupStep.Taskbar);
        steps.Add(SetupStep.Done);
        return steps;
    }

    string TitleOf(SetupStep step) => step switch
    {
        SetupStep.Welcome => "Welcome",
        SetupStep.Password => "Password",
        SetupStep.Library => "Your library",
        SetupStep.Ai => IsLibrary ? "AI engines" : "Notes",
        SetupStep.Microphone => "Microphone",
        SetupStep.Model => "Transcription model",
        SetupStep.Canvas => "Canvas",
        SetupStep.Classes => "Classes",
        SetupStep.StartAtLogin => "Start at login",
        SetupStep.Taskbar => "Taskbar",
        _ => IsLibrary ? "Connect your laptop" : "Done",
    };

    /// <summary>What this computer is for: rebuilds <see cref="Steps"/> for that flow and stays on the current step when
    /// it's still one of them, else goes to the first. Moving to another flow forgets whether the library was ready (a
    /// laptop connects to one, just this computer keeps its own to itself, a library opens it to laptops, so each makes
    /// or reaches it again); what was typed stays.</summary>
    public void SetRole(AppRole role)
    {
        bool otherFlow = role != Role;
        Role = role;
        if (otherFlow && Steps.Count > 0)
        {
            LibraryOk = false;
            LibraryResult = null;
        }
        var wanted = Step;
        Steps.Clear();
        int n = 1;
        foreach (var step in StepsFor(role, skin))
            Steps.Add(new StepItem { Step = step, Number = n++, Title = TitleOf(step), Optional = step is SetupStep.Canvas or SetupStep.Classes });
        Go(Steps.Any(s => s.Step == wanted) ? wanted : Steps[0].Step);
        foreach (string p in new[] { nameof(IsOneComputer), nameof(IsLibrary), nameof(IsLaptop), nameof(LibraryIsHere), nameof(HeaderTitle), nameof(FlowName),
                     nameof(FlowIcon), nameof(StartAtLoginTitle), nameof(StartAtLoginLede), nameof(StartAtLoginHint), nameof(Count), nameof(HasWelcomeProblem), nameof(ShowWelcomeStatus),
                     nameof(ShowNotesSummary), nameof(LibrarySummary) })
            OnPropertyChanged(p);
        NotifyStepDerived();
    }

    /// <summary>Steps before this one are done (a check); this one and the ones after are still to come, even if
    /// they were done before: going Back undoes their checks too.</summary>
    public void Go(SetupStep step)
    {
        Step = step;
        int at = Steps.ToList().FindIndex(s => s.Step == step);
        for (int i = 0; i < Steps.Count; i++)
        {
            Steps[i].Current = i == at;
            Steps[i].Done = at >= 0 && i < at;
        }
    }

    void NotifyStepDerived()
    {
        foreach (string p in new[] { nameof(Index), nameof(IsLast), nameof(StepLabel), nameof(ContinueLabel), nameof(CanGoBack), nameof(OnWelcome), nameof(OnPassword),
                     nameof(OnMicrophone), nameof(OnLibrary), nameof(OnModel), nameof(OnClasses), nameof(OnStartAtLogin), nameof(OnTaskbar), nameof(OnAi),
                     nameof(OnCanvas), nameof(OnDone), nameof(OnLibraryDone), nameof(OnLaptopDone), nameof(OnOneComputerDone), nameof(OnPlainStep),
                     nameof(Wide), nameof(CanContinue), nameof(AiHelpOpen) })
            OnPropertyChanged(p);
    }

    partial void OnStepChanged(SetupStep value)
    {
        NotifyStepDerived();
        OnEnter?.Invoke(value);
    }

    /// <summary>The AI step is showing an engine's setup steps: the window grows to fit them.</summary>
    public bool AiHelpOpen => OnAi && Ai?.HelpOpen == true;

    partial void OnAiChanged(AiSetupModel? oldValue, AiSetupModel? newValue)
    {
        if (oldValue is not null) oldValue.PropertyChanged -= OnAiPropertyChanged;
        if (newValue is not null) newValue.PropertyChanged += OnAiPropertyChanged;
        OnPropertyChanged(nameof(AiHelpOpen));
    }

    void OnAiPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AiSetupModel.HelpOpen)) OnPropertyChanged(nameof(AiHelpOpen));
    }

    partial void OnCanvasChanged(CanvasConnectModel? oldValue, CanvasConnectModel? newValue)
    {
        if (oldValue is not null) oldValue.PropertyChanged -= OnCanvasPropertyChanged;
        if (newValue is not null) newValue.PropertyChanged += OnCanvasPropertyChanged;
        if (newValue is { Found.Count: > 0 }) TakeCourses(newValue.Found);
        OnPropertyChanged(nameof(CanContinue));
    }

    void OnCanvasPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CanvasConnectModel.CanFinish)) OnPropertyChanged(nameof(CanContinue));
        if (e.PropertyName == nameof(CanvasConnectModel.Found) && sender is CanvasConnectModel c) TakeCourses(c.Found);
    }

    partial void OnAddingCoursesChanged(bool value) => OnPropertyChanged(nameof(CanContinue));

    /// <summary>The Canvas step found courses: each becomes a ticked row on Classes. A course already shown keeps its
    /// tick and times; one no longer found goes.</summary>
    public void TakeCourses(IReadOnlyList<FoundCourse> found)
    {
        var had = Courses.ToDictionary(c => c.Id);
        Courses.Clear();
        foreach (var f in found)
            Courses.Add(had.TryGetValue(f.Id, out var row) ? row : new SetupCourse { Id = f.Id, Name = f.ClassName, CourseName = f.Name });
        OnPropertyChanged(nameof(HasCourses));
        OnPropertyChanged(nameof(ClassesLede));
    }

    partial void OnMicHeardChanged(bool value) => OnPropertyChanged(nameof(MicLine));

    partial void OnLibraryResultChanged(string? value)
    {
        OnPropertyChanged(nameof(HasLibraryResult));
        OnPropertyChanged(nameof(HasWelcomeProblem));
        OnPropertyChanged(nameof(ShowWelcomeStatus));
        OnPropertyChanged(nameof(LibrarySummary));
    }

    partial void OnLibraryOkChanged(bool value)
    {
        OnPropertyChanged(nameof(LibrarySummary));
        OnPropertyChanged(nameof(HasWelcomeProblem));
        OnPropertyChanged(nameof(ShowWelcomeStatus));
        OnPropertyChanged(nameof(ContinueLabel));
        OnPropertyChanged(nameof(CanEditLibrary));
    }

    partial void OnConnectingChanged(bool value)
    {
        OnPropertyChanged(nameof(HasWelcomeProblem));
        OnPropertyChanged(nameof(ShowWelcomeStatus));
        OnPropertyChanged(nameof(CanContinue));
        OnPropertyChanged(nameof(CanEditLibrary));
    }

    /// <summary>A laptop that's changed the address or password after connecting connects again on Continue.</summary>
    void Retyped()
    {
        if (!IsLaptop || Connecting || !LibraryOk) return;
        LibraryOk = false;
        LibraryResult = null;
    }

    partial void OnAddressChanged(string value) => Retyped();

    partial void OnPasswordChanged(string value)
    {
        Retyped();
        OnPropertyChanged(nameof(PasswordText));
    }

    partial void OnShowPasswordChanged(bool value)
    {
        OnPropertyChanged(nameof(PasswordText));
        OnPropertyChanged(nameof(ShowPasswordText));
    }

    partial void OnModelProgressChanged(double value)
    {
        OnPropertyChanged(nameof(ModelBarWidth));
        OnPropertyChanged(nameof(ModelBarWidthWin));
    }

    partial void OnModelReadyChanged(bool value)
    {
        OnPropertyChanged(nameof(ModelDownloading));
        OnPropertyChanged(nameof(ModelSummary));
    }

    partial void OnModelProblemChanged(string? value)
    {
        OnPropertyChanged(nameof(HasModelProblem));
        OnPropertyChanged(nameof(ModelDownloading));
        OnPropertyChanged(nameof(ModelSummary));
    }

    partial void OnModelSizeChanged(string value) => OnPropertyChanged(nameof(ModelBody));

    public Func<Task>? OnAllowMic { get; set; }
    public Action? OnMicSettings { get; set; }
    /// <summary>Creates the library here (library flow) or connects to it (laptop flow).</summary>
    public Func<Task>? OnConnect { get; set; }
    public Func<Task>? OnFind { get; set; }
    public Action? OnRetryModel { get; set; }
    public Func<Task>? OnAddClass { get; set; }
    public Action? OnTaskbarSettings { get; set; }
    /// <summary>Puts text on the clipboard (the library's address or password, on the last page).</summary>
    public Action<string>? OnCopy { get; set; }
    public Func<SetupStep, bool>? CanLeave { get; set; }
    /// <summary>A step that saves before moving on (the AI engines): false stays, and the step says why.</summary>
    public Func<SetupStep, Task<bool>>? LeaveAsync { get; set; }
    /// <summary>A step just opened: the host makes what it needs (the AI and Canvas steps' models).</summary>
    public Action<SetupStep>? OnEnter { get; set; }
    public Action? OnFinish { get; set; }

    [RelayCommand] Task AllowMic() => OnAllowMic?.Invoke() ?? Task.CompletedTask;
    [RelayCommand] void MicSettings() => OnMicSettings?.Invoke();
    [RelayCommand] void RetryModel() => OnRetryModel?.Invoke();
    [RelayCommand] void TaskbarSettings() => OnTaskbarSettings?.Invoke();
    [RelayCommand] void ToggleShowPassword() => ShowPassword = !ShowPassword;
    [RelayCommand] void Copy(string? text) => OnCopy?.Invoke(text ?? "");
    [RelayCommand] void CopyPassword() => OnCopy?.Invoke(Password);

    /// <summary>The welcome's three choices; picking one shows its steps in the sidebar, keeping what's been typed.</summary>
    [RelayCommand]
    void ChooseOneComputer() => SetRole(AppRole.Both);

    [RelayCommand]
    void ChooseLaptop() => SetRole(AppRole.Laptop);

    [RelayCommand]
    void ChooseLibrary() => SetRole(AppRole.Library);

    [RelayCommand]
    async Task Connect()
    {
        if (OnConnect is not null) await OnConnect();
    }

    [RelayCommand]
    async Task Find()
    {
        if (OnFind is not null) await OnFind();
    }

    [RelayCommand]
    async Task AddClass()
    {
        if (OnAddClass is not null) await OnAddClass();
    }

    [RelayCommand]
    void Back()
    {
        int i = Index - 2;
        if (i >= 0) Go(Steps[i].Step);
    }

    /// <summary>Canvas's "Skip for now": on to the next step (or finish), saving nothing more. Skipping Canvas leaves
    /// Classes as the plain add-your-own step.</summary>
    [RelayCommand]
    void Skip()
    {
        if (OnCanvas) TakeCourses([]);
        if (IsLast)
        {
            OnFinish?.Invoke();
            return;
        }
        Go(Steps[Index].Step);
    }

    /// <summary>Continue: on the library's password page it creates the library, on the laptop's library page it
    /// connects, and on just this computer's welcome it makes the library here (no password to choose); each stays
    /// (saying why) if that didn't work.</summary>
    [RelayCommand]
    async Task Next()
    {
        if ((OnPassword || OnLibrary || OnWelcome && IsOneComputer) && !LibraryOk && OnConnect is not null)
        {
            await OnConnect();
            if (!LibraryOk) return;
        }
        if (CanLeave?.Invoke(Step) == false) return;
        if (LeaveAsync is not null && !await LeaveAsync(Step)) return;
        if (IsLast)
        {
            OnFinish?.Invoke();
            return;
        }
        Go(Steps[Index].Step);
    }
}
