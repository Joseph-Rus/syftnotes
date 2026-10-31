using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StudyStash.App.Services;
using StudyStash.App.Platform;

namespace StudyStash.App.ViewModels;

/// <summary>Setup's pages, in the order they come in any flow (each flow uses some of them).</summary>
public enum SetupStep
{
    /// <summary>A first run's only page: "Record your lectures. Get the notes." and Get started, which makes the library
    /// here, asks for the microphone and leaves everything else to defaults, or to the moment it's needed.</summary>
    Start,
    /// <summary>The first page: what Syft Notes does, in four steps, before any choice.</summary>
    Intro,
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
    /// <summary>What it's for (student, work, research, creative, personal): not a student, and there's no Canvas step
    /// and classes are folders.</summary>
    Uses,
    /// <summary>Optional: bring in Canvas (design 07).</summary>
    Canvas,
    Classes,
    /// <summary>The library: start Syft Notes at login, so the laptop can always reach it (off unless ticked).</summary>
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
    /// <summary>An optional step passed without doing it (Canvas, left unconnected): it keeps its number and says
    /// "Skipped", where a step that was done gets the check.</summary>
    [ObservableProperty] public partial bool Skipped { get; set; }

    public bool Todo => !Done && !Current;
    public bool ShowCheck => Done && !Skipped;
    public bool ShowNumber => !ShowCheck;
    /// <summary>"Optional" until the step's been passed (then its check, or "Skipped", says enough).</summary>
    public bool ShowOptional => Optional && !Done;
    public bool ShowSkipped => Done && Skipped;

    partial void OnDoneChanged(bool value) => NotifyShown();

    partial void OnSkippedChanged(bool value) => NotifyShown();

    void NotifyShown()
    {
        OnPropertyChanged(nameof(Todo));
        OnPropertyChanged(nameof(ShowCheck));
        OnPropertyChanged(nameof(ShowNumber));
        OnPropertyChanged(nameof(ShowOptional));
        OnPropertyChanged(nameof(ShowSkipped));
    }

    partial void OnCurrentChanged(bool value) => OnPropertyChanged(nameof(Todo));
}

/// <summary>One step of "how it works" on setup's first page, or of what to do next on its last.</summary>
public sealed record IntroPoint(string Glyph, string Title, string About);

/// <summary>One thing Syft Notes can be for, ticked or not, on setup's "What's it for?" page.</summary>
public sealed partial class UseChoice : ObservableObject
{
    public string Kind { get; init; } = "";
    public string Title { get; init; } = "";
    public string About { get; init; } = "";
    public string Glyph { get; init; } = "";
    [ObservableProperty] public partial bool Ticked { get; set; }

    public static UseChoice[] All() =>
    [
        new() { Kind = "student", Title = "School", About = "Lectures, classes, Canvas and what's due", Glyph = "school" },
        new() { Kind = "work", Title = "Work", About = "Meetings, calls and projects", Glyph = "forum" },
        new() { Kind = "research", Title = "Research", About = "Interviews, seminars and reading groups", Glyph = "travel_explore" },
        new() { Kind = "creative", Title = "Creative", About = "Critiques, studio time and ideas", Glyph = "palette" },
        new() { Kind = "personal", Title = "Personal", About = "Talks, podcasts, notes to self", Glyph = "headphones" },
    ];
}

/// <summary>A class in your library, as setup lists it: its name and what it covers ("CS 101 · Recursion, stacks and
/// Big-O"), which the library's AI reads to sort each lecture.</summary>
public sealed partial class SetupClass : ObservableObject
{
    [ObservableProperty] public partial string Name { get; set; } = "";
    [ObservableProperty] public partial string About { get; set; } = "";
    public IBrush Dot { get; init; } = Brushes.Gray;
}

/// <summary>A Canvas course setup found, as the class it would become: ticked to add it. Its name on Canvas becomes what
/// the class covers, so the library's AI knows what it's about even when the class is named by its code.</summary>
public sealed partial class SetupCourse : ObservableObject
{
    public string Id { get; init; } = "";
    /// <summary>The class's name: the course's name on Canvas ("Intro to Programming"), cleaned of term codes.</summary>
    public string Name { get; init; } = "";
    /// <summary>The course's short code ("CS 101"), under the class's name; "" when it has none worth showing.</summary>
    public string Code { get; init; } = "";
    public bool HasCode => Code.Length > 0 && !Name.Contains(Code, StringComparison.OrdinalIgnoreCase);
    [ObservableProperty] public partial bool Ticked { get; set; } = true;
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

    /// <summary>What it's for, as ticked on its page.</summary>
    public UseChoice[] UseChoices { get; } = UseChoice.All();
    /// <summary>For school (school ticked, or nothing yet): Canvas and classes. Otherwise folders, and no Canvas step.</summary>
    public bool ForSchool => UseChoices.All(u => !u.Ticked) || UseChoices.Any(u => u.Ticked && u.Kind == "student");
    public IEnumerable<string> Uses => UseChoices.Where(u => u.Ticked).Select(u => u.Kind);
    public bool OnUses => Step == SetupStep.Uses;
    public string ClassesTitle => ForSchool ? "Add your classes" : "Add your folders";

    bool usesWatched;

    /// <summary>A tick changed: Canvas comes and goes with school, and Classes is Folders without it.</summary>
    void WatchUses()
    {
        if (usesWatched) return;
        usesWatched = true;
        foreach (var u in UseChoices)
            u.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName != nameof(UseChoice.Ticked)) return;
                SetRole(Role);
                OnPropertyChanged(nameof(ForSchool));
                OnPropertyChanged(nameof(ClassesTitle));
            };
    }
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
    public string HeaderTitle => IsLibrary ? "Set up your library" : $"Set up Syft Notes on this {DeviceWord}";
    public string FlowName => Role switch { AppRole.Library => "Computer at home", AppRole.Laptop => "Laptop for class", _ => "Setup" };
    public string FlowIcon => Role switch { AppRole.Library => "dns", AppRole.Laptop => "laptop_mac", _ => "desktop_windows" };
    /// <summary>Setup run again from Settings, on a computer that's already set up: it walks the same steps with
    /// what's there filled in, and stays what it is (a library, a laptop or just this computer). Changing that moves
    /// lectures between computers, which only Settings → Connection does.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WelcomeTitle), nameof(WelcomeBody), nameof(IntroTitle), nameof(CanChooseOneComputer), nameof(CanChooseLaptop), nameof(CanChooseLibrary))]
    public partial bool Again { get; set; }

    /// <summary>Run again: no How it works page, and it opens on This Mac (or This PC).</summary>
    partial void OnAgainChanged(bool value)
    {
        if (Steps.Count > 0) SetRole(Role);
    }

    /// <summary>A first run on a computer that's just this one (not a library, not a laptop already connected to one):
    /// one page, one button (<see cref="SetupStep.Start"/>). Everything setup by hand asks is defaulted, or asked when
    /// it's needed: which AI on the first lecture's page, Canvas from Due, the phone from Settings.</summary>
    [ObservableProperty] public partial bool OneButton { get; set; }

    partial void OnOneButtonChanged(bool value)
    {
        // Just this computer, and opening when the student logs in unless they untick it (the welcome shows the tick).
        if (value)
        {
            StartAtLogin = true;
            if (Role != AppRole.Both) Role = AppRole.Both;
        }
        if (Steps.Count > 0) SetRole(AppRole.Both);
    }

    // The welcome's words (the first run's one page).
    public const string StartTitle = "Record your lectures. Get the notes.";
    /// <summary>The title as the page sets it: a line each, so neither half is left with one word.</summary>
    public const string StartTitleLines = "Record your lectures.\nGet the notes.";
    public string StartLede => $"Press Record when class starts. Syft Notes writes down what's said, here on this {DeviceWord}, and turns it into study notes.";
    /// <summary>Under Get started: on a Mac, that the microphone is asked for next (one sentence before the system's own
    /// question); on Windows, which asks nothing, only where recordings stay.</summary>
    public string StartHint => skin == SkinKind.Mac
        ? "Your Mac will ask next to let Syft Notes use the microphone. Recordings stay on this Mac."
        : $"Recordings stay on this {DeviceWord}.";
    public const string StartAtLoginLabel = "Open Syft Notes when you log in";
    public bool OnStart => Step == SetupStep.Start;
    /// <summary>Get started is under way: the library is being made, the microphone asked for, an AI looked for.</summary>
    [ObservableProperty] public partial bool Starting { get; set; }
    /// <summary>Get started couldn't make the library: why (the line under the button), and the button says Try again.</summary>
    public bool HasStartProblem => OnStart && HasLibraryResult && !LibraryOk && !Starting && !Connecting;

    partial void OnStartingChanged(bool value)
    {
        OnPropertyChanged(nameof(ContinueLabel));
        OnPropertyChanged(nameof(CanContinue));
        OnPropertyChanged(nameof(HasStartProblem));
    }
    public string WelcomeTitle => Again ? "Set up Syft Notes again" : "Where your lectures are kept";
    public string WelcomeBody => Again
        ? "Go through the steps again; what's already set up stays as it is. To change what this computer is for, use Settings → Advanced → Connection."
        : $"Most people keep everything on this {DeviceWord}: it records your lectures, writes the notes and keeps them here. Your phone can join any time.";

    /// <summary>The welcome's Advanced, a laptop for class and a computer at home, is open. Folded away at first, so
    /// a student with one computer isn't asked about two; open from the start when this computer is already one of
    /// them (the library download, or setup run again on a laptop or a library).</summary>
    [ObservableProperty] public partial bool OtherComputersOpen { get; set; }

    /// <summary>The first page: what happens to a lecture, from Record to notes on every device.</summary>
    public string IntroTitle => Again ? "Set up Syft Notes again" : "Welcome to Syft Notes";
    public string IntroBody => "Your lectures, written up for you. Here's how it works, then a few steps to set it up: about three minutes.";
    public IntroPoint[] IntroPoints { get; } =
    [
        new("mic", "Record when class starts", "Press Record, or its shortcut, and Syft Notes writes down what's said, on this computer. Type your own notes as it records, if you like."),
        new("auto_awesome", "Notes are written for you", "After class your AI turns the transcript into study notes: a summary, the key points, definitions, formulas and diagrams, and questions to review."),
        new("folder_open", "Everything filed, and searchable", "Each lecture goes in its class on its own. Search every word that was said, or ask a question across all your notes."),
        new("smartphone", "With you everywhere", "Your calendar and Canvas say what's next and what's due, and your iPhone or iPad has it all, Pencil notes too."),
    ];
    public bool OnIntro => Step == SetupStep.Intro;

    /// <summary>The last page's next steps, for a computer that records.</summary>
    public IntroPoint[] NextSteps => [
        new("mic", "Record your first class", "Type your own notes as it records if you like: the notes are written around yours, and yours stay word for word."),
        new("smartphone", "Get it on your phone", "Install Syft Notes on your iPhone or iPad, then open Settings → Phone → Add a phone here and scan the code."),
        new("forum", "Ask anything", "Search finds every lecture, word for word; Ask answers from your own notes, and says which lecture it used."),
    ];
    /// <summary>The welcome's choices: any of them the first time; only the one this computer already is when run again.</summary>
    public bool CanChooseOneComputer => !Again || IsOneComputer;
    public bool CanChooseLaptop => !Again || IsLaptop;
    public bool CanChooseLibrary => !Again || IsLibrary;
    // The welcome's three choices, in the student's words: just this computer, then under Advanced the two that go
    // together, a laptop for class and a computer at home.
    public string OneComputerTitle => $"Just this {DeviceWord}";
    public string OneComputerAbout => $"Records, writes your notes and keeps them all on this {DeviceWord}. No other computer needed.";
    public const string LaptopTitle = "My laptop for class", LibraryTitle = "My computer at home";
    public const string OtherComputersLabel = "Advanced: a laptop and a computer at home";
    public static string LaptopAbout => "Records your lectures and sends them to your computer at home, which keeps them and writes the notes. Set that one up first.";
    public string LibraryAbout => skin == SkinKind.Mac
        ? "Stays on, like a Mac mini, and becomes your library: it keeps every lecture and writes the notes for your laptop."
        : "Stays on at home and becomes your library: it keeps every lecture and writes the notes for your laptop.";
    public const string OtherComputersAbout = "Only if you record on a laptop and keep your lectures on a computer at home that stays on. Most people don't need this.";

    // Microphone
    [ObservableProperty] public partial bool MicAllowed { get; set; }
    [ObservableProperty] public partial bool MicDenied { get; set; }
    /// <summary>The mic check's bars, oldest first, 0 to 1 (24 of them).</summary>
    [ObservableProperty] public partial IReadOnlyList<double>? MicLevels { get; set; }
    /// <summary>A level has passed the "hears you" mark since the mic check opened.</summary>
    [ObservableProperty] public partial bool MicHeard { get; set; }
    /// <summary>Guided setup's microphone card is showing: the microphone opens for it as it does on the step.</summary>
    public bool MicCheckOpen { get; set; }
    public string MicLine => MicHeard ? "Syft Notes hears you." : "Say something. The bars move when Syft Notes hears you.";
    /// <summary>Why the mic check couldn't open the microphone (none plugged in, refused, another app has it); null
    /// while it's fine.</summary>
    [ObservableProperty] public partial Audio.MicTrouble? MicTrouble { get; set; }
    /// <summary>The waveform and "allowed" line: the microphone is allowed and opened.</summary>
    public bool ShowMicCheck => MicAllowed && MicTrouble is null;
    public bool ShowAllowMic => !MicAllowed && MicTrouble is null;
    public bool ShowMicProblem => MicDenied || MicTrouble is not null;
    public bool HasMicProblemTitle => MicTrouble is not null;
    public string MicProblemTitle => MicTrouble?.Title ?? "";
    public string MicProblemText => MicTrouble?.Detail ?? (skin == SkinKind.Mac
        ? "macOS is keeping the microphone from Syft Notes. Turn it on in System Settings → Privacy & Security → Microphone."
        : "Windows is keeping the microphone from desktop apps. Turn on Settings → Privacy & security → Microphone → Let desktop apps access your microphone.");
    public bool HasMicProblemButton => MicTrouble is null || MicTrouble.HasAction;
    public string MicProblemButton => MicTrouble is { HasAction: true } t ? t.ActionLabel
        : skin == SkinKind.Mac ? "Open System Settings" : "Open microphone settings";

    // The library: created here (library flow) or connected to (laptop flow)
    [ObservableProperty] public partial string Address { get; set; } = "";
    /// <summary>The password a library made here is given (or the one it already has).</summary>
    [ObservableProperty] public partial string Password { get; set; } = "";
    /// <summary>The laptop's Connect page: the password of the library it connects to, on another computer. Not the
    /// same field as <see cref="Password"/>: typed here, it doesn't turn up as dots in "Give your library a password"
    /// when the student goes back and makes this computer the library instead.</summary>
    [ObservableProperty] public partial string ConnectPassword { get; set; } = "";
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
    /// <summary>Start Syft Notes when the student logs in: off unless they tick it (or it already started at login).</summary>
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
    // The step's words: the switch goes by the name Settings → General gives it, "Open when you log in", in the sidebar,
    // on the page and on the switch itself.
    public string StartAtLoginTitle => IsLibrary ? "Keep your library running" : "Keep Syft Notes running";
    public string StartAtLoginLede => IsLibrary
        ? "Open Syft Notes when you log in to this computer, so your laptop can always reach it."
        : $"Open Syft Notes when you log in, so your library is ready and your notes get written whenever this {DeviceWord} is on.";
    public string StartAtLoginHint => IsLibrary ? "Recommended for a library." : "Recommended.";
    /// <summary>Just this computer's last page: growing into a laptop and a library later.</summary>
    public string AddLaptopLater => "Want to record on a laptop too? Add it any time in Settings → Advanced → Library.";
    /// <summary>The last page's shortcuts, to change them before starting (null in a test that doesn't need them).</summary>
    public ShortcutsModel? Keys { get; set; }
    public bool HasKeys => Keys is not null;

    /// <summary>The last page's "Change shortcuts" is open.</summary>
    [ObservableProperty] public partial bool KeysOpen { get; set; }

    [RelayCommand]
    void ToggleKeys()
    {
        KeysOpen = !KeysOpen;
        if (!KeysOpen) Keys?.Stop();
    }

    partial void OnRoleChanged(AppRole value) => Keys?.ShowRecord(value != AppRole.Library);

    public string RecordHint => skin == SkinKind.Mac
        ? $"Press {Keybindings.Show(KeyAction.Record, mac: true)} or click Record in the menu bar when class starts."
        : $"Press {Keybindings.Show(KeyAction.Record, mac: false)} or click Record in the tray when class starts.";

    // Model
    [ObservableProperty] public partial string ModelName { get; set; } = "Whisper large-v3";
    [ObservableProperty] public partial string ModelSize { get; set; } = "3 GB";
    [ObservableProperty] public partial double ModelProgress { get; set; }
    [ObservableProperty] public partial string ModelDone { get; set; } = "";
    [ObservableProperty] public partial string ModelLeft { get; set; } = "";
    [ObservableProperty] public partial bool ModelReady { get; set; }
    [ObservableProperty] public partial string? ModelProblem { get; set; }
    /// <summary>The models to pick from, the one for this computer marked (with one line why: "This PC has no
    /// graphics card Whisper can use, so the compact model keeps up with a lecture.") and picked to begin with.</summary>
    public ObservableCollection<ModelChoice> Models { get; } = [];
    /// <summary>The one downloading: shown as a card with Change, which opens the list.</summary>
    [ObservableProperty] public partial ModelChoice? ChosenModel { get; set; }
    /// <summary>The list is open (Change was pressed): the card and the download make way for it, until a model is
    /// picked, Cancel is pressed or the step is left.</summary>
    [ObservableProperty] public partial bool ChoosingModel { get; set; }
    public bool ShowModelCard => ChosenModel is not null && !ChoosingModel;
    public bool ShowModelDownload => !ChoosingModel;
    partial void OnChosenModelChanged(ModelChoice? value)
    {
        OnPropertyChanged(nameof(ShowModelCard));
        OnPropertyChanged(nameof(ModelBody));
    }
    partial void OnChoosingModelChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowModelCard));
        OnPropertyChanged(nameof(ShowModelDownload));
    }
    /// <summary>The laptop's last page: the model's state in a few words.</summary>
    public string ModelSummary => ModelReady ? "Ready" : HasModelProblem ? "Not downloaded yet" : "Downloading in the background";

    // Classes
    public ObservableCollection<SetupClass> Classes { get; } = [];
    /// <summary>The courses the Canvas step found, each a class to add (ticked); empty when Canvas was skipped, and
    /// then Classes is the plain add-your-own step.</summary>
    public ObservableCollection<SetupCourse> Courses { get; } = [];
    public bool HasCourses => Courses.Count > 0;
    /// <summary>What classes are for. With "No AI for now" chosen two steps back nothing reads the lectures, so it
    /// says what happens instead: a lecture goes under the class it's recorded in, or waits in Unsorted.</summary>
    public string ClassesLede => (HasCourses, Ai is { NoAi: true }) switch
    {
        (true, false) => "Your Canvas courses become your classes. The library reads each lecture and files it under the one it's about. Untick any you don't record.",
        (true, true) => "Your Canvas courses become your classes. With no AI for now, a lecture goes under the class you record it in, or waits in Unsorted. Untick any you don't record.",
        (false, false) => "The library reads each lecture and files it under the class it's about. Name your classes and say what each covers. You can skip this.",
        (false, true) => "With no AI for now, a lecture goes under the class you record it in, or waits in Unsorted. Name your classes here. You can skip this.",
    };
    /// <summary>Continue on Classes is adding the ticked courses and linking them to Canvas.</summary>
    [ObservableProperty] public partial bool AddingCourses { get; set; }
    [ObservableProperty] public partial string NewClass { get; set; } = "";
    /// <summary>What the new class covers (optional): it helps the library's AI sort.</summary>
    [ObservableProperty] public partial string NewAbout { get; set; } = "";
    /// <summary>The library couldn't take the class: why.</summary>
    [ObservableProperty] public partial string? ClassProblem { get; set; }
    public bool HasClassProblem => !string.IsNullOrEmpty(ClassProblem);
    partial void OnClassProblemChanged(string? value) => OnPropertyChanged(nameof(HasClassProblem));

    // AI engines and Canvas: each step's own model, made by the host when the step opens (the library is known then).
    [ObservableProperty] public partial AiSetupModel? Ai { get; set; }
    [ObservableProperty] public partial CanvasConnectModel? Canvas { get; set; }

    public string DeviceWord => StudyStash.Core.Machine.DeviceForLook(skin == SkinKind.Mac);
    public int Count => Steps.Count;
    public int Index => Steps.ToList().FindIndex(s => s.Step == Step) + 1;
    public bool IsLast => Index == Count;
    public string StepLabel =>
        OnIntro ? "Welcome"
        : OnAi && IsLibrary ? $"Library setup · step {Index} of {Count}"
        : $"Step {Index} of {Count}";
    public string ContinueLabel =>
        OnStart ? (Starting || Connecting ? "Getting ready…" : HasStartProblem ? "Try again" : "Get started")
        : IsLast ? "Open Syft Notes"
        : OnIntro ? "Get started"
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
    public bool CanContinue => (!OnCanvas || Canvas?.CanFinish != false) && !Connecting && !AddingCourses && !Starting;
    public bool HasLibraryResult => !string.IsNullOrEmpty(LibraryResult);
    /// <summary>The library's name and password can't change once it's made here (Settings changes them later).</summary>
    public bool CanEditLibrary => !LibraryOk && !Connecting;
    public bool HasModelProblem => !string.IsNullOrEmpty(ModelProblem);
    public bool ModelDownloading => !ModelReady && !HasModelProblem;
    /// <summary>The download bar's fill, out of its track: the step's column, which is 416 wide in the Mac look (720,
    /// less the 224 sidebar and 40 each side) and 440 in the Windows one (less its 208 sidebar and 36 each side).</summary>
    public double ModelBarWidth => Math.Clamp(ModelProgress, 0, 1) * 416;
    public double ModelBarWidthWin => Math.Clamp(ModelProgress, 0, 1) * 440;
    public string ModelBody => ChosenModel is { Model.Bundled: true } bundled
        ? $"Syft Notes turns speech into text on this {DeviceWord}, so your recordings never leave it. {bundled.Name} comes with the app: there's nothing to download."
        : $"Syft Notes turns speech into text on this {DeviceWord}, so your recordings never leave it. The model is about {ModelSize} and only downloads once.";

    /// <summary>Setup in <paramref name="skin"/>'s look, starting on <paramref name="role"/>'s choice (just this
    /// computer unless said).</summary>
    public static SetupModel For(SkinKind skin, AppRole role = AppRole.Both)
    {
        var m = new SetupModel { skin = skin };
        m.SetRole(role);
        return m;
    }

    /// <summary>The welcome's choice to start on: the installer's library download starts on "My computer at home";
    /// the laptop download, or a build that doesn't say, on "Just this Mac" (the one most students want).</summary>
    public static AppRole Suggested(AppRole? installer) => installer == AppRole.Library ? AppRole.Library : AppRole.Both;

    /// <summary>The steps of each flow, in order. Windows adds the taskbar where Syft Notes records.</summary>
    public static IReadOnlyList<SetupStep> StepsFor(AppRole role, SkinKind skin) => StepsFor(role, skin, forSchool: true);

    public static IReadOnlyList<SetupStep> StepsFor(AppRole role, SkinKind skin, bool forSchool, bool intro = true, bool oneButton = false)
    {
        // The first run on just this computer: one page.
        if (oneButton) return [SetupStep.Start];
        // How it works comes first, the first time; run again, it's known.
        var steps = intro ? new List<SetupStep> { SetupStep.Intro, SetupStep.Welcome } : new List<SetupStep> { SetupStep.Welcome };
        steps.AddRange(role switch
        {
            // No step for the transcription model: the one for this computer (WhisperModels.Advise) is downloaded in
            // the background from the microphone's step on, and the last page says how it's getting on.
            AppRole.Laptop => [SetupStep.Library, SetupStep.Microphone],
            AppRole.Library => [SetupStep.Password, SetupStep.Ai],
            _ => [SetupStep.Microphone, SetupStep.Ai],
        });
        steps.Add(SetupStep.Uses);
        if (forSchool) steps.Add(SetupStep.Canvas);
        steps.Add(SetupStep.Classes);
        if (role != AppRole.Laptop) steps.Add(SetupStep.StartAtLogin);
        if (role != AppRole.Library && skin == SkinKind.Win) steps.Add(SetupStep.Taskbar);
        steps.Add(SetupStep.Done);
        return steps;
    }

    string TitleOf(SetupStep step) => step switch
    {
        SetupStep.Start => "Welcome",
        SetupStep.Intro => "Welcome",
        SetupStep.Welcome => $"This {DeviceWord}",
        SetupStep.Password => "Password",
        SetupStep.Library => "Your library",
        SetupStep.Ai => IsLibrary ? "Your AI" : "Notes",
        SetupStep.Microphone => "Microphone",
        SetupStep.Model => "Transcription model",
        SetupStep.Canvas => "Canvas",
        SetupStep.Classes => ForSchool ? "Classes" : "Folders",
        SetupStep.Uses => "What it's for",
        SetupStep.StartAtLogin => "Open when you log in",
        SetupStep.Taskbar => "Taskbar",
        _ => IsLibrary ? "Connect your laptop" : "Done",
    };

    /// <summary>What this computer is for: rebuilds <see cref="Steps"/> for that flow and stays on the current step when
    /// it's still one of them, else goes to the first. Moving to another flow forgets whether the library was ready (a
    /// laptop connects to one, just this computer keeps its own to itself, a library opens it to laptops, so each makes
    /// or reaches it again); what was typed stays.</summary>
    public void SetRole(AppRole role)
    {
        WatchUses();
        bool otherFlow = role != Role;
        Role = role;
        if (role != AppRole.Both) OtherComputersOpen = true;
        if (otherFlow && Steps.Count > 0)
        {
            LibraryOk = false;
            LibraryResult = null;
        }
        var wanted = Step;
        Steps.Clear();
        int n = 1;
        foreach (var step in StepsFor(role, skin, ForSchool, intro: !Again, oneButton: OneButton && !Again))
            Steps.Add(new StepItem { Step = step, Number = n++, Title = TitleOf(step), Optional = step is SetupStep.Canvas or SetupStep.Classes });
        Go(Steps.Any(s => s.Step == wanted) ? wanted : Steps[0].Step);
        foreach (string p in new[] { nameof(IsOneComputer), nameof(IsLibrary), nameof(IsLaptop), nameof(LibraryIsHere), nameof(HeaderTitle), nameof(FlowName),
                     nameof(FlowIcon), nameof(StartAtLoginTitle), nameof(StartAtLoginLede), nameof(StartAtLoginHint), nameof(Count), nameof(HasWelcomeProblem), nameof(ShowWelcomeStatus),
                     nameof(ShowNotesSummary), nameof(LibrarySummary) })
            OnPropertyChanged(p);
        NotifyStepDerived();
        // A password already in the field (the one a library here has) that can't be used is said on its page.
        SayPasswordProblem();
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
        MarkSkipped();
    }

    /// <summary>Canvas passed with no browser connected ("Skip for now", or Continue past it) reads as skipped, not as
    /// done.</summary>
    void MarkSkipped()
    {
        foreach (var s in Steps) s.Skipped = s.Step == SetupStep.Canvas && s.Done && Canvas?.BrowserConnected != true;
    }

    void NotifyStepDerived()
    {
        foreach (string p in new[] { nameof(Index), nameof(IsLast), nameof(StepLabel), nameof(ContinueLabel), nameof(CanGoBack), nameof(OnWelcome), nameof(OnStart), nameof(HasStartProblem), nameof(OnPassword),
                     nameof(OnMicrophone), nameof(OnUses), nameof(OnIntro), nameof(OnLibrary), nameof(OnModel), nameof(OnClasses), nameof(OnStartAtLogin), nameof(OnTaskbar), nameof(OnAi),
                     nameof(OnCanvas), nameof(OnDone), nameof(OnLibraryDone), nameof(OnLaptopDone), nameof(OnOneComputerDone), nameof(OnPlainStep),
                     nameof(Wide), nameof(CanContinue), nameof(AiHelpOpen) })
            OnPropertyChanged(p);
    }

    partial void OnStepChanged(SetupStep value)
    {
        // The list of models doesn't stay open behind Back or Continue.
        if (value != SetupStep.Model) ChoosingModel = false;
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
        OnPropertyChanged(nameof(ClassesLede));
    }

    void OnAiPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AiSetupModel.HelpOpen)) OnPropertyChanged(nameof(AiHelpOpen));
        if (e.PropertyName == nameof(AiSetupModel.NoAi)) OnPropertyChanged(nameof(ClassesLede));
    }

    partial void OnCanvasChanged(CanvasConnectModel? oldValue, CanvasConnectModel? newValue)
    {
        if (oldValue is not null) oldValue.PropertyChanged -= OnCanvasPropertyChanged;
        if (newValue is not null) newValue.PropertyChanged += OnCanvasPropertyChanged;
        if (newValue is { Found.Count: > 0 }) TakeCourses(newValue.Found);
        OnPropertyChanged(nameof(CanContinue));
        MarkSkipped();
    }

    void OnCanvasPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CanvasConnectModel.CanFinish)) OnPropertyChanged(nameof(CanContinue));
        if (e.PropertyName == nameof(CanvasConnectModel.Found) && sender is CanvasConnectModel c) TakeCourses(c.Found);
        if (e.PropertyName == nameof(CanvasConnectModel.BrowserConnected)) MarkSkipped();
    }

    partial void OnAddingCoursesChanged(bool value) => OnPropertyChanged(nameof(CanContinue));

    /// <summary>The Canvas step found courses: each becomes a ticked row on Classes. A course already shown keeps its
    /// tick; one no longer found goes.</summary>
    public void TakeCourses(IReadOnlyList<FoundCourse> found)
    {
        var had = Courses.ToDictionary(c => c.Id);
        Courses.Clear();
        foreach (var f in found)
            Courses.Add(had.TryGetValue(f.Id, out var row) ? row : new SetupCourse { Id = f.Id, Name = f.ClassName, Code = f.Code });
        OnPropertyChanged(nameof(HasCourses));
        OnPropertyChanged(nameof(ClassesLede));
    }

    partial void OnMicHeardChanged(bool value) => OnPropertyChanged(nameof(MicLine));

    partial void OnMicAllowedChanged(bool value) => MicShowsChanged();

    partial void OnMicDeniedChanged(bool value) => MicShowsChanged();

    partial void OnMicTroubleChanged(Audio.MicTrouble? value) => MicShowsChanged();

    void MicShowsChanged()
    {
        foreach (var name in new[] { nameof(ShowMicCheck), nameof(ShowAllowMic), nameof(ShowMicProblem), nameof(HasMicProblemTitle),
                     nameof(MicProblemTitle), nameof(MicProblemText), nameof(HasMicProblemButton), nameof(MicProblemButton) })
            OnPropertyChanged(name);
    }

    partial void OnLibraryResultChanged(string? value)
    {
        OnPropertyChanged(nameof(HasStartProblem));
        OnPropertyChanged(nameof(HasLibraryResult));
        OnPropertyChanged(nameof(HasWelcomeProblem));
        OnPropertyChanged(nameof(ShowWelcomeStatus));
        OnPropertyChanged(nameof(LibrarySummary));
    }

    partial void OnLibraryOkChanged(bool value)
    {
        OnPropertyChanged(nameof(HasStartProblem));
        OnPropertyChanged(nameof(LibrarySummary));
        OnPropertyChanged(nameof(HasWelcomeProblem));
        OnPropertyChanged(nameof(ShowWelcomeStatus));
        OnPropertyChanged(nameof(ContinueLabel));
        OnPropertyChanged(nameof(CanEditLibrary));
    }

    partial void OnConnectingChanged(bool value)
    {
        OnPropertyChanged(nameof(HasStartProblem));
        OnPropertyChanged(nameof(ContinueLabel));
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

    partial void OnConnectPasswordChanged(string value) => Retyped();

    partial void OnPasswordChanged(string value)
    {
        SayPasswordProblem();
        OnPropertyChanged(nameof(PasswordText));
    }

    /// <summary>The line under the fields is the typed password's problem, not something else setup said.</summary>
    string? passwordProblem;

    /// <summary>A library's new password with a letter that can't be used (<see cref="Core.Configs.PasswordProblem"/>)
    /// is said as it's typed, under the field, and the line goes again once the letter does.</summary>
    void SayPasswordProblem()
    {
        if (!IsLibrary || LibraryOk || Connecting) return;
        string? problem = Core.Configs.PasswordProblem(Password.Trim());
        if (problem is not null) LibraryResult = problem;
        else if (LibraryResult is not null && LibraryResult == passwordProblem) LibraryResult = null;
        passwordProblem = problem;
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
    /// <summary>Opens a settings page (a problem's own button).</summary>
    public Action<string>? OnOpenUrl { get; set; }
    /// <summary>Creates the library here (library flow) or connects to it (laptop flow).</summary>
    public Func<Task>? OnConnect { get; set; }
    public Func<Task>? OnFind { get; set; }
    /// <summary>Get started's part after the library is made: the microphone asked for, the model's download started, an
    /// AI already signed in here picked for the notes (or notes left for the first lecture's page). Never fails Get
    /// started: what it couldn't do is asked later, where it's needed.</summary>
    public Func<Task>? OnGetStarted { get; set; }
    public Action? OnRetryModel { get; set; }
    /// <summary>Another model picked: it's the one that downloads now (what came of the other stays).</summary>
    public Action<ModelChoice>? OnPickModel { get; set; }
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
    /// <summary>Guided setup is in the same window: its link back ("Set up with Claude instead").</summary>
    public Action? OnGuided { get; set; }
    [ObservableProperty] public partial string GuidedLabel { get; set; } = "";
    public bool HasGuidedLink => OnGuided is not null && GuidedLabel.Length > 0;
    partial void OnGuidedLabelChanged(string value) => OnPropertyChanged(nameof(HasGuidedLink));

    [RelayCommand] void BackToGuided() => OnGuided?.Invoke();

    [RelayCommand] Task AllowMic() => OnAllowMic?.Invoke() ?? Task.CompletedTask;
    /// <summary>The problem's own button (Sound settings for a missing microphone), else the privacy settings.</summary>
    [RelayCommand] void MicSettings()
    {
        if (MicTrouble is { HasAction: true } t) OnOpenUrl?.Invoke(t.ActionUrl);
        else OnMicSettings?.Invoke();
    }
    [RelayCommand] void RetryModel() => OnRetryModel?.Invoke();

    /// <summary>The card's Change opens the list of models; the list's own Cancel shuts it again, the model as it was.</summary>
    [RelayCommand] void ChangeModel() => ChoosingModel = !ChoosingModel;

    [RelayCommand]
    void PickModel(ModelChoice choice)
    {
        foreach (var m in Models) m.Chosen = m == choice;
        ChosenModel = choice;
        ChoosingModel = false;
        ModelName = choice.Name;
        ModelSize = choice.Size;
        OnPickModel?.Invoke(choice);
    }
    [RelayCommand] void TaskbarSettings() => OnTaskbarSettings?.Invoke();
    [RelayCommand] void ToggleShowPassword() => ShowPassword = !ShowPassword;
    [RelayCommand] void Copy(string? text) => OnCopy?.Invoke(text ?? "");
    [RelayCommand] void CopyPassword() => OnCopy?.Invoke(Password);

    [RelayCommand] void ToggleOtherComputers() => OtherComputersOpen = !OtherComputersOpen;

    /// <summary>The welcome's three choices; picking one shows its steps in the sidebar, keeping what's been typed.</summary>
    [RelayCommand]
    void ChooseOneComputer() => Choose(AppRole.Both);

    [RelayCommand]
    void ChooseLaptop() => Choose(AppRole.Laptop);

    [RelayCommand]
    void ChooseLibrary() => Choose(AppRole.Library);

    void Choose(AppRole role)
    {
        if (!Again) SetRole(role);
    }

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
        if (OnStart)
        {
            // Get started: the library made here (once: Try again after a problem makes it again), then the host's
            // part (the microphone, an AI already here), then the window closes on the library.
            if (Starting) return;
            Starting = true;
            try
            {
                if (!LibraryOk && OnConnect is not null) await OnConnect();
                if (!LibraryOk) return;
                if (OnGetStarted is not null) await OnGetStarted();
            }
            finally
            {
                Starting = false;
            }
            OnFinish?.Invoke();
            return;
        }
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
