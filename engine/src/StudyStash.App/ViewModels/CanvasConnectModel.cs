using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StudyStash.App.Services;

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

/// <summary>A course Find my courses found: its Canvas id, its code ("COMP 101", or "" when Canvas has none) and its
/// name ("Intro to Programming").</summary>
public sealed record FoundCourse(string Id, string Code, string Name)
{
    /// <summary>What the course is called as a class: its code, or its name when it has no code.</summary>
    public string ClassName => Code.Length > 0 ? Code : Name;
}

/// <summary>
/// Connecting Canvas (design 07): the school address, adding the extension to Chrome, finding your courses, and — in
/// Settings' own window — matching each class to a course and the first sync; one step open at a time, the rest Done
/// or To do. In setup (<see cref="ForSetup"/>) it stops after Find my courses: setup's next step, Classes, makes the
/// found courses your classes. Shares its <see cref="CanvasWatch"/> with the status card and Settings, and hurries it
/// while Add to Chrome shows, so it moves along the moment Chrome answers rather than polling on its own. Hosted
/// either inside first-run setup (<see cref="ShowFooter"/> false, the wizard's own Next/Finish) or, from the status
/// card's Connect/Show me how, in a small window of its own (<see cref="ShowFooter"/> true).
/// </summary>
public sealed partial class CanvasConnectModel : ObservableObject, IDisposable
{
    readonly CanvasContext context;
    readonly CanvasWatch watch;
    CanvasApi.State state = new();
    CanvasApi.Overview? found;
    string school = "";
    /// <summary>The watch polls every few seconds while this is held: only while Add to Chrome shows.</summary>
    IDisposable? hurry;
    bool checkingChrome;
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
                new ConnectStep { Number = 2, Title = "Chrome extension" },
                new ConnectStep { Number = 3, Title = "Your courses" },
            ]
            :
            [
                new ConnectStep { Number = 1, Title = "School address" },
                new ConnectStep { Number = 2, Title = "Chrome extension" },
                new ConnectStep { Number = 3, Title = "Your courses" },
                new ConnectStep { Number = 4, Title = "Match each class to a course" },
                new ConnectStep { Number = 5, Title = "First sync" },
            ];
        watch.Changed += OnWatchChanged;
    }

    /// <summary>Setup's three parts (school, Chrome, courses); the classes come in setup's own next step.</summary>
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

    // ---- step 2: Add to Chrome ----
    /// <summary>The folder Chrome loads the extension from on this computer, once Add to Chrome has asked for it.</summary>
    [ObservableProperty] public partial string? ExtensionFolder { get; set; }
    /// <summary>Add to Chrome has been pressed: the button gives way to quiet links and the "Waiting for Chrome…" row.</summary>
    [NotifyPropertyChangedFor(nameof(WaitingForChrome), nameof(ShowChromeStatus), nameof(ShowAddToChrome), nameof(ChromeHelp))]
    [ObservableProperty]
    public partial bool AddedToChrome { get; set; }
    [NotifyPropertyChangedFor(nameof(CanFinish))]
    [ObservableProperty]
    public partial bool AddingToChrome { get; set; }
    /// <summary>A Chrome with the extension is talking to the library.</summary>
    [NotifyPropertyChangedFor(nameof(WaitingForChrome), nameof(ShowChromeStatus), nameof(ShowAddToChrome))]
    [ObservableProperty]
    public partial bool ChromeConnected { get; set; }
    [ObservableProperty] public partial string? ChromeError { get; set; }
    public bool ShowAddToChrome => !AddedToChrome && !ChromeConnected;
    /// <summary>The line above the three pictures: what the button does, then (once pressed) what's open now.</summary>
    public string ChromeHelp => AddedToChrome
        ? "Chrome’s extensions page is open, and the folder is showing. In Chrome:"
        : "Study Stash reads Canvas through your own sign-in in Chrome. Add to Chrome shows you the extension’s folder and opens Chrome’s extensions page. There:";
    public bool WaitingForChrome => AddedToChrome && !ChromeConnected;
    public bool ShowChromeStatus => AddedToChrome || ChromeConnected;

    // ---- step 3: find my courses ----
    [ObservableProperty] public partial bool SignedOut { get; set; }
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
    public bool CanFinish => !SavingSchool && !AddingToChrome && !FindingCourses && !Linking && !Syncing;
    /// <summary>Every step is done (setup: the courses are found).</summary>
    public bool AllDone => Current > Steps.Count;
    public Action? OnSkip { get; set; }
    public Action? OnBack { get; set; }
    public Action? OnFinish { get; set; }

    [RelayCommand] void Skip() => OnSkip?.Invoke();
    [RelayCommand] void Back() => OnBack?.Invoke();
    [RelayCommand] void Finish() => OnFinish?.Invoke();

    /// <summary>Opens the flow at whichever step the library's state still needs: not connected at all → the
    /// school address; no Chrome checking in now with the library's current key → the extension (a Chrome that was
    /// connected once, or has an old key, doesn't skip it); signed out, or no class linked yet → find courses;
    /// otherwise the whole thing already works, so → sync now.</summary>
    public Task StartAsync(CanvasApi.State s, IReadOnlyList<CanvasApi.ClassRow> classes, CancellationToken stop = default)
    {
        state = s;
        school = s.School.Length > 0 ? s.School : s.Url;
        SchoolField = school;
        return GoToAsync(DecideStep(s, classes), classes, stop);
    }

    /// <summary>The step the library's state still needs. Setup always finds courses once Chrome is connected (its next
    /// step makes them classes), so it never goes past 3 on its own.</summary>
    int DecideStep(CanvasApi.State s, IReadOnlyList<CanvasApi.ClassRow> classes) => s.Status switch
    {
        "not_set_up" => 1,
        _ when !ExtensionConnected(s) => 2,
        "signed_out" => 3,
        _ when ForSetup => 3,
        _ => classes.Count > 0 && classes.All(c => !c.Linked) ? 3 : 5,
    };

    /// <summary>Chrome is checking in now with the library's current key. Only that moves past the extension step:
    /// an old registration, or a Chrome that was here once, can't find courses. A library older than the
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
        if (!entering) return;
        switch (step)
        {
            case 2: EnterExtensionStep(); break;
            case 3: await EnterCoursesStepAsync(stop); break;
            case 4 when !ForSetup: EnterMatchStep(classes ?? []); break;
        }
    }

    partial void OnCurrentChanged(int value) => OnPropertyChanged(nameof(AllDone));

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

    /// <summary>Add to Chrome shows: nothing on the computer changes until the button is pressed, but the watch asks
    /// the library every few seconds, so an extension already loaded turns up by itself.</summary>
    void EnterExtensionStep()
    {
        ChromeConnected = false;
        ChromeError = null;
        hurry ??= watch.Hurry();
    }

    async Task EnterCoursesStepAsync(CancellationToken stop)
    {
        SignedOut = state.Status == "signed_out";
        CoursesSay = null;
        if (!SignedOut) await FindCoursesInternalAsync(stop);
    }

    void EnterMatchStep(IReadOnlyList<CanvasApi.ClassRow> classes)
    {
        var choices = (found?.Available ?? new Dictionary<string, string>())
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => new CourseChoice(kv.Key, kv.Value)).ToList();
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
            _ = CheckChromeAsync(s);
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
    /// Add to Chrome: gets the extension's folder ready on this computer (the library's own when it runs here, else
    /// one made here pointing at the library), shows it in Finder/Explorer, and opens Chrome's extensions page — the
    /// three illustrated steps say what to do there. After that the button gives way to quiet links, and the watch
    /// says "Connected" as soon as Chrome checks in.
    /// </summary>
    [RelayCommand]
    async Task AddToChrome()
    {
        if (context.Client is not { } client) return;
        AddingToChrome = true;
        ChromeError = null;
        try
        {
            var status = await client.ExtensionStatusAsync(context.Actions.PrepareExtension);
            if (status?.Folder is not { } folder)
            {
                ChromeError = status is null
                    ? "Your library runs an older Study Stash: update it to add Canvas."
                    : "The extension's folder isn't ready yet. Try again in a moment.";
                return;
            }
            ExtensionFolder = folder;
            context.Actions.RevealFolder(folder);
            context.Actions.OpenChromeExtensions();
            AddedToChrome = true;
            if (Current == 2) hurry ??= watch.Hurry();
            if (status.Connected) await ChromeIsConnectedAsync();
        }
        catch (Exception e) when (e is CanvasLibraryException or HttpRequestException or System.Text.Json.JsonException or IOException or UnauthorizedAccessException
                                      or TaskCanceledException)
        {
            ChromeError = e is CanvasLibraryException { Message.Length: > 0 } ? e.Message : "Your library didn't answer. Try again in a moment.";
        }
        finally
        {
            AddingToChrome = false;
        }
    }

    /// <summary>"Show the folder again", after Add to Chrome.</summary>
    [RelayCommand]
    void ShowFolder()
    {
        if (ExtensionFolder is { } f) context.Actions.RevealFolder(f);
    }

    /// <summary>The watch heard from the library while Add to Chrome shows: Chrome counts as connected once the state
    /// is past "no extension", or the library says a Chrome is checking in now.</summary>
    async Task CheckChromeAsync(CanvasApi.State s)
    {
        if (checkingChrome || ChromeConnected) return;
        checkingChrome = true;
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
            if (connected && Current == 2) await ChromeIsConnectedAsync();
        }
        catch (Exception e) when (e is CanvasLibraryException or HttpRequestException or System.Text.Json.JsonException or TaskCanceledException)
        {
            // The library stopped answering part-way: the watch's next round tries again.
        }
        finally
        {
            checkingChrome = false;
        }
    }

    /// <summary>Chrome answered: the row says Connected (a flat check, no glow) and the flow moves on to finding
    /// courses by itself.</summary>
    async Task ChromeIsConnectedAsync(CancellationToken stop = default)
    {
        ChromeConnected = true;
        Steps[1].Summary = "Connected";
        StopHurrying();
        if (context.Client is { } client && await client.StateAsync(stop) is { } s)
        {
            state = s;
            school = s.School.Length > 0 ? s.School : s.Url;
        }
        await GoToAsync(3, null, stop); // whatever else the library has, a Chrome that's just connected finds courses next
    }

    [RelayCommand]
    void OpenChromeExtensions() => context.Actions.OpenChromeExtensions();

    [RelayCommand]
    void OpenCanvas() => context.Actions.OpenInChrome(state.Url);

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
            Found = FoundFrom(answer);
            CoursesSay = answer.Available.Count == 1 ? "Found 1 course." : $"Found {answer.Available.Count} courses.";
            Steps[2].Summary = CoursesSay.TrimEnd('.');
            if (ForSetup)
            {
                await GoToAsync(Steps.Count + 1, null, stop);
                return;
            }
            var classes = await client.ClassesAsync(stop) ?? [];
            await GoToAsync(4, classes, stop);
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

    /// <summary>The found courses, each with its code: from <c>course_info</c> when the library sent it, else the part
    /// of its name before " · " when it has one ("COMP 101 · Intro to Programming").</summary>
    static IReadOnlyList<FoundCourse> FoundFrom(CanvasApi.Overview o)
    {
        var info = o.CourseInfo.Where(c => c.Id.Length > 0).GroupBy(c => c.Id).ToDictionary(g => g.Key, g => g.First());
        var list = new List<FoundCourse>();
        foreach (var (id, label) in o.Available)
        {
            string code = info.TryGetValue(id, out var c) ? c.Code.Trim() : "";
            string name = label;
            int dot = label.IndexOf(" · ", StringComparison.Ordinal);
            if (dot > 0)
            {
                if (code.Length == 0) code = label[..dot].Trim();
                name = label[(dot + 3)..].Trim();
            }
            list.Add(new FoundCourse(id, code, name));
        }
        return [.. list.OrderBy(f => f.ClassName, StringComparer.OrdinalIgnoreCase)];
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
