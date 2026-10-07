using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StudyStash.App.Services;
using StudyStash.Core.Canvas;

namespace StudyStash.App.ViewModels;

/// <summary>Where one of connect flow's five steps stands.</summary>
public enum StepState { Done, Current, ToDo }

/// <summary>One step of connecting Canvas (design 07): its number and title, and, once past it, a short summary
/// ("school.instructure.com").</summary>
public sealed partial class ConnectStep : ObservableObject
{
    public required int Number { get; init; }
    public required string Title { get; init; }

    [NotifyPropertyChangedFor(nameof(IsDone), nameof(IsCurrent), nameof(IsToDo))]
    [ObservableProperty]
    public partial StepState State { get; set; } = StepState.ToDo;

    [ObservableProperty] public partial string Summary { get; set; } = "";

    public bool IsDone => State == StepState.Done;
    public bool IsCurrent => State == StepState.Current;
    public bool IsToDo => State == StepState.ToDo;
}

/// <summary>A course Find my courses found: its Canvas id, its short code ("COMP 101", or "" when its code has none
/// worth showing) and its name ("Intro to Programming"), cleaned of term and SIS codes.</summary>
public sealed record FoundCourse(string Id, string Code, string Name)
{
    /// <summary>What the course is called as a class: its name, never its code (the code is only a label beside it).</summary>
    public string ClassName => Name.Length > 0 ? Name : Code;
}

/// <summary>One browser the extension step can add the extension to, in its quiet "Use another browser" menu: its
/// name, and a check on the one chosen now.</summary>
public sealed partial class BrowserChoice(Browser browser) : ObservableObject
{
    public Browser Browser { get; } = browser;
    public string Name => Browser.Name;
    public IRelayCommand Pick { get; internal set; } = null!;
    [ObservableProperty] public partial bool Current { get; set; }
}

/// <summary>
/// Connecting Canvas (design 07): the school address, adding the extension to the student's browser, finding your
/// courses, and — in Settings' own window — matching each class to a course and the first sync; one step open at a
/// time, the rest Done or To do. In setup (<see cref="ForSetup"/>) it stops after Find my courses: setup's next step,
/// Classes, makes the found courses your classes. Shares its <see cref="CanvasWatch"/> with the status card and
/// Settings, and hurries it while Add to Chrome (or whichever browser) shows, so it moves along the moment the browser
/// answers rather than polling on its own. Hosted either inside first-run setup (<see cref="ShowFooter"/> false, the
/// wizard's own Next/Finish) or, from the status card's Connect/Show me how, in a small window of its own
/// (<see cref="ShowFooter"/> true).
/// </summary>
public sealed partial class CanvasConnectModel : ObservableObject, IDisposable
{
    readonly CanvasContext context;
    readonly CanvasWatch watch;
    CanvasApi.State state = new();
    CanvasApi.Overview? found;
    string school = "";
    /// <summary>The watch polls every few seconds while this is held: only while the extension step shows.</summary>
    IDisposable? hurry;
    bool checkingBrowser;
    /// <summary>The student chose the browser here ("Use another browser"): it stays chosen when the step opens again.</summary>
    bool pickedHere;
    /// <summary>What to say about the student's usual browser, when the extension can't go in it; null when it can.</summary>
    BrowserAdvice? advice;
    static readonly ConnectStep Hidden = new() { Number = 0, Title = "" };

    public CanvasConnectModel(CanvasContext context, CanvasWatch watch, bool forSetup = false)
    {
        this.context = context;
        this.watch = watch;
        ForSetup = forSetup;
        Steps = forSetup
            ?
            [
                new ConnectStep { Number = 1, Title = "School address" },
                new ConnectStep { Number = 2, Title = "Browser extension" },
                new ConnectStep { Number = 3, Title = "Your courses" },
            ]
            :
            [
                new ConnectStep { Number = 1, Title = "School address" },
                new ConnectStep { Number = 2, Title = "Browser extension" },
                new ConnectStep { Number = 3, Title = "Your courses" },
                new ConnectStep { Number = 4, Title = "Match each class to a course" },
                new ConnectStep { Number = 5, Title = "First sync" },
            ];
        watch.Changed += OnWatchChanged;
        Picker = new CoursePickerModel(context);
        Picker.OnChanged = OnPickerChanged;
        OfferBrowsers();
    }

    /// <summary>Which of the found courses to bring in: shown under Your courses once they're found. In setup the
    /// ticked ones are what <see cref="Found"/> hands to the Classes step; in Settings' window "Bring these in" makes
    /// them classes before matching.</summary>
    public CoursePickerModel Picker { get; }

    /// <summary>The picker shows: courses were found, and Your courses is open (or setup's Canvas is done).</summary>
    public bool ShowPicker => Picker.HasCourses && (Current == 3 || ForSetup && AllDone);

    /// <summary>The line over the picker: what Find my courses found (Settings' window), or in setup how to use it.</summary>
    public string PickerHint => ForSetup ? "Tick the courses to bring in. Only these become classes and sync." : CoursesSay ?? "";
    /// <summary>What Find my courses said shows on its own only while there's no list to tick under it.</summary>
    public bool ShowCoursesSay => !string.IsNullOrEmpty(CoursesSay) && !ShowPicker;

    partial void OnCoursesSayChanged(string? value)
    {
        OnPropertyChanged(nameof(PickerHint));
        OnPropertyChanged(nameof(ShowCoursesSay));
    }

    /// <summary>"Bring in 5 courses" (Settings' window, on Your courses).</summary>
    public string BringInLabel => Picker.TickedCount == 1 ? "Bring in 1 course" : $"Bring in {Picker.TickedCount} courses";
    public bool ShowBringIn => !ForSetup && Current == 3 && Picker.HasCourses && !FindingCourses && !SignedOut;
    /// <summary>Find my courses (or "Looking…") shows on Your courses until there's a list to tick.</summary>
    public bool ShowFind => !SignedOut && !ShowBringIn;
    /// <summary>The picker's list scrolls past this: setup's window is shorter than Settings' connect window.</summary>
    public double PickerHeight => ForSetup ? 300 : 200;

    partial void OnSignedOutChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowBringIn));
        OnPropertyChanged(nameof(ShowFind));
    }

    void OnPickerChanged()
    {
        if (found is not null) Found = [.. FoundFrom(found).Where(f => Picker.Courses.Any(p => p.Id == f.Id && p.Ticked))];
        OnPropertyChanged(nameof(ShowPicker));
        OnPropertyChanged(nameof(ShowCoursesSay));
        OnPropertyChanged(nameof(BringInLabel));
        OnPropertyChanged(nameof(ShowBringIn));
        OnPropertyChanged(nameof(ShowFind));
    }

    /// <summary>Setup's three parts (school, browser, courses); the classes come in setup's own next step.</summary>
    public bool ForSetup { get; }
    /// <summary>Settings' window also matches classes to courses and runs the first sync.</summary>
    public bool ShowMatchAndSync => !ForSetup;

    public IReadOnlyList<ConnectStep> Steps { get; }
    // Named steps for the views: simpler for compiled bindings than an indexer. Setup has no 4 and 5 (their rows hide).
    public ConnectStep Step1 => Steps[0];
    public ConnectStep Step2 => Steps[1];
    public ConnectStep Step3 => Steps[2];
    public ConnectStep Step4 => Steps.Count > 3 ? Steps[3] : Hidden;
    public ConnectStep Step5 => Steps.Count > 4 ? Steps[4] : Hidden;

    [ObservableProperty] public partial int Current { get; set; } = 1;

    public const string Title = "Bring in Canvas";
    public const string Intro =
        "Assignments, due dates and course files, filed next to your lectures. Study Stash only reads from Canvas. Nothing there changes.";

    // ---- step 1: school address ----
    [ObservableProperty] public partial string SchoolField { get; set; } = "";
    [ObservableProperty] public partial string? SchoolError { get; set; }
    [NotifyPropertyChangedFor(nameof(CanFinish))]
    [ObservableProperty]
    public partial bool SavingSchool { get; set; }

    // ---- step 2: the browser extension ----
    /// <summary>The browsers on this computer the extension can be added to, the one to use first (just Chrome when
    /// none is installed).</summary>
    public ObservableCollection<BrowserChoice> BrowserChoices { get; } = [];
    /// <summary>The browser this step adds the extension to: the one to use, until the student picks another.</summary>
    [NotifyPropertyChangedFor(nameof(BrowserName), nameof(IsFirefox), nameof(IsChromium), nameof(AddLabel), nameof(WaitingLabel), nameof(OpenExtensionsLabel),
        nameof(BrowserHelp), nameof(FirefoxStep1), nameof(FirefoxStep3), nameof(ShowCode), nameof(ShowFolderLinks), nameof(ShowAddOnLink), nameof(AdviceText))]
    [ObservableProperty]
    public partial Browser Browser { get; set; } = Browsers.Chrome;
    /// <summary>Its name, for every sentence in the step ("Add to Edge", "Waiting for Edge…").</summary>
    public string BrowserName => Browser.Name;
    /// <summary>Firefox and the browsers built on it: the add-on is fetched and connected with a pasted code, so the
    /// step shows three steps and the code where Chrome's family has its three pictures.</summary>
    public bool IsFirefox => Browser.Family == BrowserFamily.Firefox;
    public bool IsChromium => !IsFirefox;
    /// <summary>More than one browser here can take the extension: the step offers a quiet way to use another.</summary>
    public bool CanSwitchBrowser => BrowserChoices.Count > 1 && !BrowserConnected;
    public string AddLabel => $"Add to {BrowserName}";
    public string WaitingLabel => $"Waiting for {BrowserName}…";
    public string OpenExtensionsLabel => $"Open {BrowserName} extensions";

    /// <summary>The folder a browser of Chrome's family loads the extension from on this computer, once Add to Chrome
    /// has asked for it.</summary>
    [NotifyPropertyChangedFor(nameof(FolderName), nameof(PickCaption))]
    [ObservableProperty]
    public partial string? ExtensionFolder { get; set; }
    /// <summary>The folder's own name, as the browser's picker and Finder/Explorer show it ("Chrome extension" on a
    /// Mac, in the Study Stash folder in your home); "Chrome extension" until Add to Chrome has found it.</summary>
    public string FolderName => ExtensionFolder is { Length: > 0 } f ? Path.GetFileName(f.TrimEnd('/', '\\')) : "Chrome extension";
    /// <summary>The third picture's caption: pick the folder, or (with Developer mode on) drag it onto the page.</summary>
    public string PickCaption => $"Pick the {FolderName} folder, or drag it onto the page";
    /// <summary>Add to Chrome has been pressed: the button gives way to quiet links and the "Waiting for Chrome…" row.</summary>
    [NotifyPropertyChangedFor(nameof(WaitingForBrowser), nameof(ShowBrowserStatus), nameof(ShowAddToBrowser), nameof(ShowAddRow), nameof(BrowserHelp),
        nameof(ShowFolderLinks), nameof(ShowAddOnLink), nameof(ShowLinks), nameof(ShowSwitchAfterLinks), nameof(ShowAdvice), nameof(ShowGetBrowser))]
    [ObservableProperty]
    public partial bool AddedToBrowser { get; set; }
    [NotifyPropertyChangedFor(nameof(CanFinish))]
    [ObservableProperty]
    public partial bool AddingToBrowser { get; set; }
    /// <summary>A browser with the extension is talking to the library.</summary>
    [NotifyPropertyChangedFor(nameof(WaitingForBrowser), nameof(ShowBrowserStatus), nameof(ShowAddToBrowser), nameof(ShowAddRow), nameof(CanSwitchBrowser),
        nameof(ShowLinks), nameof(ShowSwitchAfterLinks), nameof(ShowAdvice), nameof(ShowGetBrowser))]
    [ObservableProperty]
    public partial bool BrowserConnected { get; set; }
    [ObservableProperty] public partial string? BrowserError { get; set; }
    public bool ShowAddToBrowser => !AddedToBrowser && !BrowserConnected;
    /// <summary>Guided setup's helper card has a foot while there's something in it: its button, or the way to
    /// another browser.</summary>
    public bool ShowAddRow => ShowAddToBrowser || CanSwitchBrowser;
    /// <summary>The line above the three pictures (or Firefox's three steps): what the button does, then (once
    /// pressed) what's open now.</summary>
    public string BrowserHelp => (IsFirefox, AddedToBrowser) switch
    {
        (false, false) => $"Study Stash reads Canvas through your own sign-in in {BrowserName}. Add to {BrowserName} shows you the extension’s folder and opens {BrowserName}’s extensions page. There:",
        (false, true) => $"{BrowserName}’s extensions page is open, and the folder is showing. In {BrowserName}:",
        (true, false) => $"Study Stash reads Canvas through your own sign-in in {BrowserName}. Add to {BrowserName} opens the Study Stash add-on there. Then:",
        (true, true) => $"The Study Stash add-on is open in {BrowserName}. There:",
    };
    /// <summary>The student's usual browser can't take the extension (Safari; Firefox before its add-on is out), or
    /// nothing on this computer can: said above the step's help, with the browser Study Stash will use instead, until
    /// the extension has been added to one.</summary>
    public bool ShowAdvice => advice is not null && !AddedToBrowser && !BrowserConnected;
    public string AdviceText => advice?.Say(Browser) ?? "";
    /// <summary>Nothing here can take the extension: the step links to where Chrome is got.</summary>
    public bool ShowGetBrowser => ShowAdvice && advice!.NoneHere;
    public bool WaitingForBrowser => AddedToBrowser && !BrowserConnected;
    public bool ShowBrowserStatus => AddedToBrowser || BrowserConnected;
    /// <summary>The browser that's connected, for a sentence: the one that checked in, by its own name; else the one
    /// the extension was just added to here; else "your browser" ("Your browser" to <paramref name="start"/> one).</summary>
    public string ConnectedIn(bool start = false) =>
        CanvasSettings.BrowserName(state.Extension?.Browser is { Length: > 0 } named ? named : AddedToBrowser ? BrowserName : "", start);

    /// <summary>Firefox's three steps, where Chrome's family has its three pictures.</summary>
    public string FirefoxStep1 => $"In {BrowserName}, click Add when it asks to add Study Stash for Canvas.";
    public string FirefoxStep2 => "Click the Study Stash button (it may be under the puzzle-piece Extensions button), paste this code and click Connect.";
    public string FirefoxStep3 => $"Click Allow when {BrowserName} asks.";
    /// <summary>The code a Firefox copy is connected with (<see cref="Extension.ConnectionCode"/>: the library as this
    /// computer reaches it, the extension's key and the Canvas address); "" until the library has said.</summary>
    [NotifyPropertyChangedFor(nameof(ShowCode))]
    [ObservableProperty]
    public partial string ConnectionCode { get; set; } = "";
    public bool ShowCode => IsFirefox && ConnectionCode.Length > 0;
    /// <summary>Copy was pressed: the button says so.</summary>
    [NotifyPropertyChangedFor(nameof(CopyLabel))]
    [ObservableProperty]
    public partial bool CodeCopied { get; set; }
    public string CopyLabel => CodeCopied ? "Copied" : "Copy";

    // The quiet links under the step, once the button's been pressed: the folder and the extensions page again
    // (Chrome's family) or the add-on again (Firefox's), and the way to another browser when there is one.
    public bool ShowFolderLinks => AddedToBrowser && IsChromium;
    public bool ShowAddOnLink => AddedToBrowser && IsFirefox;
    public bool ShowLinks => AddedToBrowser || CanSwitchBrowser;
    public bool ShowSwitchAfterLinks => AddedToBrowser && CanSwitchBrowser;

    // ---- step 3: find my courses ----
    [ObservableProperty] public partial bool SignedOut { get; set; }
    /// <summary>What to do while signed out: "Sign in to Canvas in Edge. Study Stash looks again by itself once you have."</summary>
    [ObservableProperty] public partial string SignInHelp { get; set; } = "";
    [NotifyPropertyChangedFor(nameof(CanFinish))]
    [ObservableProperty]
    public partial bool FindingCourses { get; set; }
    [ObservableProperty] public partial string? CoursesSay { get; set; }
    /// <summary>Every course Find my courses found, in the library's order of names (setup makes them classes).</summary>
    [ObservableProperty] public partial IReadOnlyList<FoundCourse> Found { get; set; } = [];

    // ---- step 4: match each class to a course ----
    public ObservableCollection<CanvasCourseRow> Courses { get; } = [];
    [NotifyPropertyChangedFor(nameof(CanFinish))]
    [ObservableProperty]
    public partial bool Linking { get; set; }
    [ObservableProperty] public partial string? LinkError { get; set; }

    // ---- step 5: sync now ----
    [NotifyPropertyChangedFor(nameof(CanFinish), nameof(HasProgress))]
    [ObservableProperty]
    public partial bool Syncing { get; set; }
    [ObservableProperty] public partial double? SyncProgress { get; set; }
    [ObservableProperty] public partial string? SyncedLine { get; set; }
    public bool HasProgress => Syncing;

    // ---- what the host (setup, or the small Connect window) controls ----
    [ObservableProperty] public partial string StepLabel { get; set; } = "";
    [ObservableProperty] public partial bool ShowFooter { get; set; }
    [ObservableProperty] public partial string FinishLabel { get; set; } = "Finish";
    public bool CanFinish => !SavingSchool && !AddingToBrowser && !FindingCourses && !Linking && !Syncing;
    /// <summary>Every step is done (setup: the courses are found).</summary>
    public bool AllDone => Current > Steps.Count;
    public Action? OnSkip { get; set; }
    public Action? OnBack { get; set; }
    public Action? OnFinish { get; set; }

    [RelayCommand] void Skip() => OnSkip?.Invoke();
    [RelayCommand] void Back() => OnBack?.Invoke();
    [RelayCommand] void Finish() => OnFinish?.Invoke();

    /// <summary>Opens the flow at whichever step the library's state still needs: not connected at all → the
    /// school address; no browser checking in now with the library's current key → the extension (one that was
    /// connected once, or has an old key, doesn't skip it); signed out, or no class linked yet → find courses;
    /// otherwise the whole thing already works, so → sync now.</summary>
    public Task StartAsync(CanvasApi.State s, IReadOnlyList<CanvasApi.ClassRow> classes, CancellationToken stop = default)
    {
        state = s;
        school = s.School.Length > 0 ? s.School : s.Url;
        SchoolField = school;
        return GoToAsync(DecideStep(s, classes), classes, stop);
    }

    /// <summary>The step the library's state still needs. Setup always finds courses once the browser is connected (its next
    /// step makes them classes), so it never goes past 3 on its own.</summary>
    int DecideStep(CanvasApi.State s, IReadOnlyList<CanvasApi.ClassRow> classes) => s.Status switch
    {
        "not_set_up" => 1,
        _ when !ExtensionConnected(s) => 2,
        "signed_out" => 3,
        _ when ForSetup => 3,
        _ => classes.Any(c => c.Linked) ? 5 : 3,
    };

    /// <summary>A browser is checking in now with the library's current key. Only that moves past the extension step:
    /// an old registration, or a browser that was here once, can't find courses. A library older than the
    /// <c>connected</c> field goes by its state instead.</summary>
    static bool ExtensionConnected(CanvasApi.State s) => s.Extension?.Connected ?? s.Status is not ("no_extension" or "chrome_away");

    async Task GoToAsync(int step, IReadOnlyList<CanvasApi.ClassRow>? classes, CancellationToken stop)
    {
        bool entering = step != Current;
        for (int i = 0; i < Steps.Count; i++) Steps[i].State = i + 1 < step ? StepState.Done : i + 1 == step ? StepState.Current : StepState.ToDo;
        if (step > 1) Steps[0].Summary = school;
        if (step > 2) Steps[1].Summary = "Connected";
        Current = step;
        if (step != 2) StopHurrying();
        if (step == 5 && state.LastSync is { } synced) SyncedLine = $"Canvas synced {CanvasWords.Clock(synced, context.Clock.Zone)}.";
        if (!entering)
        {
            if (step == 2 && IsFirefox) _ = ShowCodeAsync(); // the school's address is in the code, and may just have changed
            return;
        }
        switch (step)
        {
            case 2: EnterExtensionStep(); break;
            case 3: await EnterCoursesStepAsync(stop); break;
            case 4 when !ForSetup: EnterMatchStep(classes ?? []); break;
        }
    }

    partial void OnCurrentChanged(int value)
    {
        OnPropertyChanged(nameof(AllDone));
        OnPropertyChanged(nameof(ShowPicker));
        OnPropertyChanged(nameof(ShowCoursesSay));
        OnPropertyChanged(nameof(ShowBringIn));
        OnPropertyChanged(nameof(ShowFind));
    }

    partial void OnFindingCoursesChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowBringIn));
        OnPropertyChanged(nameof(ShowFind));
    }

    void StopHurrying()
    {
        hurry?.Dispose();
        hurry = null;
    }

    /// <summary>Re-reads the library and moves on if it now needs a later step than the one showing (a step never
    /// moves backward on its own — "Change" is the only way back).</summary>
    async Task RefreshAndAdvanceAsync(CancellationToken stop = default)
    {
        if (context.Client is not { } client) return;
        if (await client.StateAsync(stop) is not { } s) return;
        state = s;
        school = s.School.Length > 0 ? s.School : s.Url;
        var classes = await client.ClassesAsync(stop) ?? [];
        int step = Math.Max(DecideStep(s, classes), Current);
        await GoToAsync(step, classes, stop);
    }

    /// <summary>Add to Chrome (or whichever browser) shows: nothing on the computer changes until the button is
    /// pressed, but the watch asks the library every few seconds, so an extension already loaded turns up by itself.</summary>
    void EnterExtensionStep()
    {
        BrowserConnected = false;
        BrowserError = null;
        OfferBrowsers();
        hurry ??= watch.Hurry();
    }

    /// <summary>Reads which browsers the extension can go in here, and takes the one to use (the student's own
    /// choice in this step, while it's still among them).</summary>
    void OfferBrowsers()
    {
        advice = context.Actions.Advise?.Invoke();
        OnPropertyChanged(nameof(ShowAdvice));
        OnPropertyChanged(nameof(AdviceText));
        OnPropertyChanged(nameof(ShowGetBrowser));
        var offer = context.Actions.Browsers();
        if (offer.Count == 0) offer = [Browsers.Chrome];
        var chosen = pickedHere && offer.Contains(Browser) ? Browser : offer[0];
        if (!offer.SequenceEqual(BrowserChoices.Select(c => c.Browser)))
        {
            BrowserChoices.Clear();
            foreach (var b in offer) BrowserChoices.Add(new BrowserChoice(b) { Pick = new RelayCommand(() => PickBrowser(b)) });
            OnPropertyChanged(nameof(CanSwitchBrowser));
            OnPropertyChanged(nameof(ShowAddRow));
            OnPropertyChanged(nameof(ShowLinks));
            OnPropertyChanged(nameof(ShowSwitchAfterLinks));
        }
        Use(chosen);
    }

    /// <summary>"Use another browser": the step is that browser's from here (its button, its words, its pictures or
    /// its code), and it's the one Study Stash opens Canvas in.</summary>
    void PickBrowser(Browser browser)
    {
        if (browser == Browser) return;
        pickedHere = true;
        AddedToBrowser = false;
        BrowserError = null;
        Use(browser);
        context.Actions.RememberBrowser(browser);
    }

    void Use(Browser browser)
    {
        Browser = browser;
        foreach (var c in BrowserChoices) c.Current = c.Browser == browser;
        if (IsFirefox && Current == 2) _ = ShowCodeAsync();
    }

    /// <summary>The code for Firefox's second step, as soon as the open step is Firefox's. A library that doesn't
    /// answer just now leaves it out: Add to Firefox asks again, and says so.</summary>
    async Task ShowCodeAsync()
    {
        if (context.Client is not { } client) return;
        try
        {
            await ReadCodeAsync(client);
        }
        catch (Exception e) when (e is CanvasLibraryException or HttpRequestException or System.Text.Json.JsonException or TaskCanceledException)
        {
            // No code for now: pressing Add to Firefox asks again, and says why not.
        }
    }

    /// <summary>Asks the library for what a Firefox copy connects with and makes <see cref="ConnectionCode"/> from it
    /// ("" when there's no Canvas address or key yet). Null from a library too old to say.</summary>
    async Task<CanvasApi.ExtensionKey?> ReadCodeAsync(CanvasClient client)
    {
        var info = await client.ExtensionAsync();
        string code = info is null ? "" : Extension.ConnectionCode(client.ServerUrl, info.Key, info.Canvas);
        if (code != ConnectionCode) CodeCopied = false;
        ConnectionCode = code;
        return info;
    }

    async Task EnterCoursesStepAsync(CancellationToken stop)
    {
        SignInHelp = $"Sign in to Canvas in {ConnectedIn()}. Study Stash looks again by itself once you have.";
        SignedOut = state.Status == "signed_out";
        CoursesSay = null;
        if (!SignedOut) await FindCoursesInternalAsync(stop);
    }

    void EnterMatchStep(IReadOnlyList<CanvasApi.ClassRow> classes)
    {
        var choices = CourseChoice.From(found);
        Courses.Clear();
        foreach (var c in classes)
        {
            var row = new CanvasCourseRow { Class = c.Class, Dot = context.DotOf(c.Class) };
            row.Choices.Add(new CourseChoice(null, "Choose a course…"));
            foreach (var choice in choices) row.Choices.Add(choice);
            string? preselect = c.Linked ? c.Canvas?.Id : c.Suggested;
            row.Selected = preselect is not null ? row.Choices.FirstOrDefault(x => x.Id == preselect) ?? row.Choices[0] : row.Choices[0];
            Courses.Add(row);
        }
    }

    /// <summary>Stops following the shared watch once the step or window closes: the watch outlives this model.</summary>
    public void Dispose()
    {
        watch.Changed -= OnWatchChanged;
        StopHurrying();
    }

    void OnWatchChanged()
    {
        if (watch.State is not { } s) return;
        state = s;
        if (Current == 2)
        {
            _ = CheckBrowserAsync(s);
        }
        else if (Current == 3 && SignedOut && s.Status != "signed_out")
        {
            SignedOut = false;
            _ = FindCoursesInternalAsync();
        }
        else if (Current == 5 && !ForSetup)
        {
            Syncing = s.Status == "syncing";
            SyncProgress = s.Syncing is { Total: > 0 } sy ? (double)(sy.Total - sy.Left) / sy.Total : null;
            if (!Syncing && s.LastSync is { } t) SyncedLine = $"Canvas synced {CanvasWords.Clock(t, context.Clock.Zone)}.";
        }
    }

    [RelayCommand]
    async Task Continue()
    {
        if (context.Client is not { } client) return;
        SavingSchool = true;
        SchoolError = null;
        try
        {
            await client.SaveAsync(url: SchoolField);
            await RefreshAndAdvanceAsync();
        }
        catch (CanvasLibraryException e)
        {
            SchoolError = e.Message;
        }
        finally
        {
            SavingSchool = false;
        }
    }

    /// <summary>The design's "Change" link: back to editing the school address, whatever step is showing now.</summary>
    [RelayCommand]
    void ChangeSchool()
    {
        SchoolField = school;
        SchoolError = null;
        for (int i = 1; i < Steps.Count; i++) Steps[i].State = StepState.ToDo;
        Steps[0].State = StepState.Current;
        Current = 1;
        StopHurrying();
    }

    /// <summary>
    /// Add to Chrome, or any browser of its family: gets the extension's folder ready on this computer (the library's
    /// own when it runs here, else one made here pointing at the library), shows it in Finder/Explorer, and opens that
    /// browser's extensions page — the three illustrated steps say what to do there. Add to Firefox, or any browser
    /// of its family: opens the Study Stash add-on in it, with the code to paste showing here; no folder. After
    /// either the button gives way to quiet links, and the watch says "Connected" as soon as the browser checks in.
    /// A browser that couldn't be started is said, and the button stays.
    /// </summary>
    [RelayCommand]
    async Task AddToBrowser()
    {
        if (context.Client is not { } client) return;
        AddingToBrowser = true;
        BrowserError = null;
        try
        {
            bool connected = false;
            string? problem;
            if (IsFirefox)
            {
                var info = await ReadCodeAsync(client);
                if (ConnectionCode.Length == 0)
                {
                    BrowserError = info is null
                        ? "Your library runs an older Study Stash: update it to add Canvas."
                        : "The code to connect it isn't ready yet. Try again in a moment.";
                    return;
                }
                problem = context.Actions.OpenAddOn(Browser);
            }
            else
            {
                var status = await client.ExtensionStatusAsync(context.Actions.PrepareExtension);
                if (status?.Folder is not { } folder)
                {
                    BrowserError = status is null
                        ? "Your library runs an older Study Stash: update it to add Canvas."
                        : "The extension's folder isn't ready yet. Try again in a moment.";
                    return;
                }
                ExtensionFolder = folder;
                context.Actions.RevealFolder(folder);
                problem = context.Actions.OpenExtensions(Browser);
                connected = status.Connected;
            }
            if (problem is not null)
            {
                BrowserError = problem;
                return;
            }
            context.Actions.RememberBrowser(Browser);
            AddedToBrowser = true;
            if (Current == 2) hurry ??= watch.Hurry();
            if (connected) await BrowserIsConnectedAsync();
        }
        catch (Exception e) when (e is CanvasLibraryException or HttpRequestException or System.Text.Json.JsonException or IOException or UnauthorizedAccessException
                                      or TaskCanceledException)
        {
            BrowserError = e is CanvasLibraryException { Message.Length: > 0 } ? e.Message : "Your library didn't answer. Try again in a moment.";
        }
        finally
        {
            AddingToBrowser = false;
        }
    }

    /// <summary>"Get Chrome", when no browser here can take the extension: its download page, in whatever this
    /// computer opens links with.</summary>
    [RelayCommand]
    void GetBrowser() => context.Actions.OpenUrl(Browsers.GetChrome);

    /// <summary>"Show the folder again", after Add to Chrome.</summary>
    [RelayCommand]
    void ShowFolder()
    {
        if (ExtensionFolder is { } f) context.Actions.RevealFolder(f);
    }

    /// <summary>The watch heard from the library while the extension step shows: the browser counts as connected once
    /// the state is past "no extension", or the library says one is checking in now.</summary>
    async Task CheckBrowserAsync(CanvasApi.State s)
    {
        if (checkingBrowser || BrowserConnected) return;
        checkingBrowser = true;
        try
        {
            bool connected = s.Status is not ("not_set_up" or "") && ExtensionConnected(s);
            if (!connected && context.Client is { } client)
            {
                try
                {
                    connected = (await client.ExtensionStatusAsync())?.Connected == true;
                }
                catch (Exception e) when (e is CanvasLibraryException or HttpRequestException or System.Text.Json.JsonException or TaskCanceledException)
                {
                    // Ask again on the watch's next round.
                }
            }
            if (connected && Current == 2) await BrowserIsConnectedAsync();
        }
        catch (Exception e) when (e is CanvasLibraryException or HttpRequestException or System.Text.Json.JsonException or TaskCanceledException)
        {
            // The library stopped answering part-way: the watch's next round tries again.
        }
        finally
        {
            checkingBrowser = false;
        }
    }

    /// <summary>The browser answered: the row says Connected (a flat check, no glow) and the flow moves on to finding
    /// courses by itself.</summary>
    async Task BrowserIsConnectedAsync(CancellationToken stop = default)
    {
        BrowserConnected = true;
        Steps[1].Summary = "Connected";
        StopHurrying();
        if (context.Client is { } client && await client.StateAsync(stop) is { } s)
        {
            state = s;
            school = s.School.Length > 0 ? s.School : s.Url;
        }
        await GoToAsync(3, null, stop); // whatever else the library has, a browser that's just connected finds courses next
    }

    /// <summary>"Open Chrome extensions", after Add to Chrome.</summary>
    [RelayCommand]
    void OpenExtensions()
    {
        if (context.Actions.OpenExtensions(Browser) is { } problem) BrowserError = problem;
    }

    /// <summary>"Open the add-on again", after Add to Firefox.</summary>
    [RelayCommand]
    void OpenAddOn()
    {
        if (context.Actions.OpenAddOn(Browser) is { } problem) BrowserError = problem;
    }

    /// <summary>Copy, beside Firefox's code.</summary>
    [RelayCommand]
    void CopyCode()
    {
        if (ConnectionCode.Length == 0) return;
        context.Actions.Copy(ConnectionCode);
        CodeCopied = true;
    }

    [RelayCommand]
    void OpenCanvas() => context.Actions.OpenInBrowser(state.Url);

    [RelayCommand]
    Task FindCourses() => FindCoursesInternalAsync();

    async Task FindCoursesInternalAsync(CancellationToken stop = default)
    {
        if (context.Client is not { } client) return;
        FindingCourses = true;
        CoursesSay = null;
        try
        {
            var answer = await client.FindCoursesAsync(stop);
            if (answer is null)
            {
                CoursesSay = "Your library didn't answer.";
                return;
            }
            found = answer;
            if (answer.Error.Length > 0)
            {
                CoursesSay = answer.Error;
                return;
            }
            Picker.Fill(answer);
            CoursesSay = answer.Available.Count == 1 ? "Found 1 course. Tick the ones to bring in." : $"Found {answer.Available.Count} courses. Tick the ones to bring in.";
            Steps[2].Summary = answer.Available.Count == 1 ? "Found 1 course" : $"Found {answer.Available.Count} courses";
            if (ForSetup)
            {
                await GoToAsync(Steps.Count + 1, null, stop);
                return;
            }
            // Settings' window stays on Your courses: "Bring in" makes the ticked courses classes, then matching.
            OnPickerChanged();
        }
        catch (CanvasLibraryException e)
        {
            CoursesSay = e.Message;
        }
        catch (Exception e) when (e is System.Text.Json.JsonException or HttpRequestException or TaskCanceledException)
        {
            // Said, never swallowed: a step that fails quietly just looks stuck.
            CoursesSay = "Your library didn't answer.";
        }
        finally
        {
            FindingCourses = false;
        }
    }

    /// <summary>The found courses, each named as its class will be. The library sends each course's class name
    /// (<c>available</c>, already cleaned and unique) and its short code (<c>course_info</c>); from an older library
    /// the names are cleaned here, and a " · " label ("COMP 101 · Intro to Programming") splits into code and name.</summary>
    public static IReadOnlyList<FoundCourse> FoundFrom(CanvasApi.Overview o)
    {
        var info = o.CourseInfo.Where(c => c.Id.Length > 0).GroupBy(c => c.Id).ToDictionary(g => g.Key, g => g.First());
        var courses = new List<(string Id, string Name, string Code)>();
        var codes = new Dictionary<string, string>();
        foreach (var (id, label) in o.Available)
        {
            info.TryGetValue(id, out var c);
            string rawCode = c?.Code.Trim() ?? "", name = label;
            int dot = label.IndexOf(" · ", StringComparison.Ordinal);
            if (dot > 0 && c is not { Title.Length: > 0 })
            {
                if (rawCode.Length == 0) rawCode = label[..dot].Trim();
                name = label[(dot + 3)..].Trim();
            }
            courses.Add((id, c is { Title.Length: > 0 } ? c.Title : name, rawCode));
            codes[id] = c is { ShortCode.Length: > 0 } ? c.ShortCode : CourseNames.ShortCode(rawCode, c?.Name ?? name);
        }
        // The library's names are clean and unique already, and cleaning them again changes nothing.
        var names = CourseNames.Unique(courses);
        var list = courses.Select(c => new FoundCourse(c.Id, codes[c.Id], names[c.Id]));
        return [.. list.OrderBy(f => f.ClassName, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>Settings' window: the ticked courses become classes (a class this library already has takes the course
    /// it seems to be), then on to matching the rest, or straight to the sync when every class has its course.</summary>
    [RelayCommand]
    async Task BringIn()
    {
        if (context.Client is not { } client) return;
        Linking = true;
        try
        {
            if (!await Picker.ChooseAsync(keep: true, match: true)) return;
            var classes = await client.ClassesAsync() ?? [];
            await GoToAsync(classes.Any(c => !c.Linked) ? 4 : 5, classes, default);
        }
        catch (Exception e) when (e is CanvasLibraryException or HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            Picker.Say = "Your library didn't answer.";
        }
        finally
        {
            Linking = false;
        }
    }

    [RelayCommand]
    async Task LinkThese()
    {
        if (context.Client is not { } client) return;
        Linking = true;
        LinkError = null;
        try
        {
            var body = new Dictionary<string, double>();
            foreach (var row in Courses)
                body[row.Class] = row.Selected?.Id is { } id ? double.Parse(id, CultureInfo.InvariantCulture) : 0;
            await client.SaveAsync(courses: body);
            await GoToAsync(5, null, default);
        }
        catch (CanvasLibraryException e)
        {
            LinkError = e.Message;
        }
        finally
        {
            Linking = false;
        }
    }

    [RelayCommand]
    async Task SyncNow()
    {
        if (context.Client is not { } client) return;
        Syncing = true;
        SyncedLine = null;
        await client.SaveAsync(sync: true);
    }
}
