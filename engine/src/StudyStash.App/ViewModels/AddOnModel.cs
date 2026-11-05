using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StudyStash.App.Services;
using StudyStash.Core.Canvas;

namespace StudyStash.App.ViewModels;

/// <summary>
/// The first run's Connect your classes with nothing to type (the add-on from 1.8): Add Syft Notes to Chrome (or the
/// student's own browser) opens the add-on's page in that browser's store, and opens the library's door to it for a
/// few minutes. Once added, the add-on finds Syft Notes on this computer by itself and checks in; then it finds the
/// student's Canvas from their own sign-in and tells the library. Each shows here as it happens, read from the library's
/// own state through the shared <see cref="CanvasWatch"/> (every three seconds while the step shows), and when both
/// have happened the step is done (<see cref="OnDone"/>). No school address, no code, no word the student wouldn't use.
/// The store's add-on may turn out older than 1.8 (a stale answer, a store that rolled back): one that checks in with an
/// older version sends the student to the step that asks for the school's address at once (<see cref="OnTypeInstead"/>),
/// and when nothing has come a minute and a half after the store's page opened, that step is offered right there
/// (<see cref="OfferTyping"/>).
/// </summary>
public sealed partial class AddOnModel : ObservableObject, IDisposable
{
    readonly CanvasContext context;
    readonly CanvasWatch watch;
    readonly Func<DateTimeOffset> clock;
    IDisposable? hurry;
    bool told;
    DateTimeOffset? addedAt;

    /// <summary>How long after the store's page opened, with no add-on heard from, the typing way is offered.</summary>
    public static readonly TimeSpan SlowAfter = TimeSpan.FromSeconds(90);

    public AddOnModel(CanvasContext context, CanvasWatch watch, Func<DateTimeOffset>? clock = null)
    {
        this.context = context;
        this.watch = watch;
        this.clock = clock ?? (() => DateTimeOffset.UtcNow);
        watch.Changed += OnWatchChanged;
        OfferBrowsers();
    }

    /// <summary>The add-on that checked in is older than 1.8 (it needs the school's address and a code): the funnel moves
    /// to the step that asks for it.</summary>
    public Action? OnTypeInstead { get; set; }

    /// <summary>Nothing has come since the store's page opened (a store still serving an older add-on, or the student
    /// stuck): "Type your school's Canvas address instead" shows in the step.</summary>
    [ObservableProperty] public partial bool OfferTyping { get; set; }

    /// <summary>Both are done: the add-on checked in, and the library knows the student's Canvas.</summary>
    public Action? OnDone { get; set; }

    /// <summary>The browsers here that get the add-on from a store (Chrome, Edge, Brave, Arc, Vivaldi, Firefox), the
    /// student's own first.</summary>
    public ObservableCollection<BrowserChoice> BrowserChoices { get; } = [];

    [NotifyPropertyChangedFor(nameof(AddLabel), nameof(BrowserName), nameof(StoreSteps), nameof(AllowStep), nameof(AddedLine), nameof(CanvasLine), nameof(CanvasWaitLine), nameof(AskingLine), nameof(PasteLine))]
    [ObservableProperty]
    public partial Browser Browser { get; set; } = Browsers.Chrome;

    public string BrowserName => Browser.Name;
    public string AddLabel => $"Add Syft Notes to {BrowserName}";
    public bool CanSwitchBrowser => BrowserChoices.Count > 1 && !Connected;

    /// <summary>The student's usual browser can't take it (Safari): which one Syft Notes uses instead. "" otherwise.</summary>
    [NotifyPropertyChangedFor(nameof(HasAdvice))]
    [ObservableProperty]
    public partial string Advice { get; set; } = "";
    public bool HasAdvice => Advice.Length > 0 && !Added;

    /// <summary>Add was pressed: the store's page is open in the browser.</summary>
    [NotifyPropertyChangedFor(nameof(HasAdvice), nameof(ShowAdd), nameof(Waiting), nameof(AddedLine), nameof(ShowStoreSteps), nameof(ShowPaste))]
    [ObservableProperty]
    public partial bool Added { get; set; }

    [ObservableProperty] public partial bool Adding { get; set; }

    /// <summary>An add-on has asked the library to connect and waits for the student's answer (<see cref="Follow"/>
    /// from the library's state): "firefox" or "chrome" by its kind of browser; "" when none is waiting.</summary>
    [NotifyPropertyChangedFor(nameof(IsAsking), nameof(AskingLine), nameof(ShowAdd), nameof(ShowStoreSteps))]
    [ObservableProperty]
    public partial string Asking { get; set; } = "";
    public bool IsAsking => Asking.Length > 0 && !Connected;

    /// <summary>"Your browser is asking to connect to Syft Notes." In Firefox's name when it's Firefox asking, so a
    /// student who is adding it to Chrome sees at once that it isn't theirs.</summary>
    public string AskingLine => Asking == "firefox" ? "Firefox is asking to connect to Syft Notes."
        : Browser.Family == BrowserFamily.Chromium ? $"{BrowserName} is asking to connect to Syft Notes." : "Your browser is asking to connect to Syft Notes.";

    /// <summary>The add-on checked in with the library.</summary>
    [NotifyPropertyChangedFor(nameof(ShowAdd), nameof(Waiting), nameof(AddedLine), nameof(CanSwitchBrowser), nameof(LookingForCanvas), nameof(CanvasLine),
        nameof(CanvasWaitLine), nameof(IsDone), nameof(IsAsking), nameof(ShowStoreSteps), nameof(ShowPaste))]
    [ObservableProperty]
    public partial bool Connected { get; set; }

    /// <summary>The browser has the add-on from before, still connected to an earlier library: its key was turned away
    /// since Add was pressed, and it never finds this library by itself. It takes this library's code instead, pasted
    /// into its own button (<see cref="ShowPaste"/>). A code needs the Canvas address: without one yet, the step that
    /// asks for the school's address follows at once (<see cref="OnTypeInstead"/>), in its "I already have it" mode.</summary>
    [NotifyPropertyChangedFor(nameof(ShowPaste), nameof(ShowStoreSteps), nameof(AddedLine), nameof(ConnectionCode))]
    [ObservableProperty]
    public partial bool FromBefore { get; set; }

    /// <summary>The student's Canvas, as the add-on found it ("https://school.instructure.com"); "" until then.</summary>
    [NotifyPropertyChangedFor(nameof(Found), nameof(LookingForCanvas), nameof(CanvasLine), nameof(CanvasWaitLine), nameof(IsDone))]
    [ObservableProperty]
    public partial string CanvasUrl { get; set; } = "";

    /// <summary>Why Add didn't open the browser, or the library didn't answer.</summary>
    [NotifyPropertyChangedFor(nameof(HasProblem))]
    [ObservableProperty]
    public partial string? Problem { get; set; }
    public bool HasProblem => !string.IsNullOrEmpty(Problem);

    public bool ShowAdd => !Added && !Connected && !IsAsking;
    /// <summary>What to do in the store, while the add-on isn't asking yet.</summary>
    public bool ShowStoreSteps => Waiting && !IsAsking && !ShowPaste;
    /// <summary>The add-on from before takes this library's code, in place of the store's steps.</summary>
    public bool ShowPaste => Waiting && FromBefore && ConnectionCode.Length > 0;
    /// <summary>Waiting for the add-on to be added and check in.</summary>
    public bool Waiting => Added && !Connected;
    public bool Found => CanvasUrl.Length > 0;
    public bool LookingForCanvas => Connected && !Found;
    public bool IsDone => Connected && Found;

    /// <summary>What to do on the store's page, in that browser's own words (its blue button, then its question).</summary>
    public string StoreSteps => Browser.Family == BrowserFamily.Firefox
        ? $"On the page that just opened in {BrowserName}, click Add to Firefox, then Add."
        : Browser == Browsers.Edge
            ? "On the page that just opened in Edge, click Get extension. If Edge asks to allow extensions from other stores, click Allow. Then click Add extension."
            : $"On the page that just opened in {BrowserName}, click {Browser.StoreButton ?? "Add to Chrome"}, then Add extension.";
    /// <summary>What comes after the store: the add-on's own page, and the browser's question about reaching Canvas.</summary>
    public string AllowStep => $"A Syft Notes page opens in {BrowserName}: click Allow, then Allow again when {BrowserName} asks.";

    public string AddedLine => Connected ? $"Added to {BrowserName}" : ShowPaste ? $"Connect {BrowserName} again" : Added ? $"Waiting for Syft Notes to be added to {BrowserName}" : $"Add Syft Notes to {BrowserName}";

    /// <summary>What to do with an add-on from before (<see cref="FromBefore"/>): its own button, then the code.</summary>
    public string PasteLine => $"{BrowserName} already has Syft Notes from before. Click the Syft Notes button in {BrowserName}'s toolbar, choose Paste a new code, and paste this:";

    /// <summary>The code an add-on from a store connects with, as the watch's <see cref="ExtensionKeeper"/> keeps it.</summary>
    public string ConnectionCode => watch.Extension?.ConnectionCode ?? "";

    /// <summary>The second line: the student's Canvas, found by itself.</summary>
    public string CanvasLine => Found ? $"Found your Canvas: {School}" : "Sign in to Canvas";
    public string CanvasWaitLine => LookingForCanvas
        ? $"Open Canvas in {BrowserName} and sign in, the way you usually do. Syft Notes finds it by itself."
        : $"Syft Notes finds your school's Canvas by itself once you're signed in to it in {BrowserName}.";
    public string School => Uri.TryCreate(CanvasUrl, UriKind.Absolute, out var u) ? u.Host : CanvasUrl;

    /// <summary>The step shows: what the library knows already (an add-on added before, a Canvas found before), and the
    /// door opened for an add-on that's here already and only needs to find this library.</summary>
    public async Task StartAsync()
    {
        hurry ??= watch.Hurry();
        await PairAsync();
        if (watch.State is { } s) Follow(s);
    }

    async Task PairAsync()
    {
        if (context.Client is not { } client) return;
        try
        {
            await client.PairAsync();
        }
        catch (Exception e) when (e is CanvasLibraryException or HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            // Add tries again, and says so if it can't.
        }
    }

    void OnWatchChanged()
    {
        if (watch.State is { } s) Follow(s);
    }

    /// <summary>What the library says now: the add-on checking in (with this library's key), and the Canvas it found.</summary>
    public void Follow(CanvasApi.State s)
    {
        Asking = s.Extension?.Asking ?? "";
        if (s.Extension?.Connected == true) Connected = true;
        if (s.Url.Length > 0) CanvasUrl = s.Url;
        // An add-on from before knocked with an earlier library's key since Add was pressed (it knocks every 30 s).
        FromBefore = Added && !Connected && addedAt is { } pressed && s.Extension?.RefusedAt >= pressed;
        if (FromBefore && ConnectionCode.Length == 0 && !told)
        {
            told = true;
            OnTypeInstead?.Invoke();
            return;
        }
        // An add-on that says its version and it's older than 1.8 can't find Canvas: the typing way, at once.
        // Only a check-in after the store's page opened counts: a version the library kept from an earlier install says
        // nothing about the add-on being added now.
        if (!told && !IsDone && addedAt is { } opened && s.Extension is { LastSeen: { } heard, Version: { Length: > 0 } v } && heard >= opened
            && Extension.IsOlder(v, StoreVersions.FindsCanvasFrom))
        {
            told = true;
            OnTypeInstead?.Invoke();
            return;
        }
        if (Waiting && !IsAsking && addedAt is { } at && clock() - at >= SlowAfter) OfferTyping = true;
        if (IsDone && !told)
        {
            told = true;
            OnDone?.Invoke();
        }
    }

    /// <summary>Add Syft Notes to Chrome: the add-on's page in that browser's store, and the library's door open for it.</summary>
    [RelayCommand]
    async Task Add()
    {
        if (context.Client is not { } client) return;
        Adding = true;
        Problem = null;
        try
        {
            await client.PairAsync();
            if (context.Actions.OpenAddOn(Browser) is { } problem)
            {
                Problem = problem;
                return;
            }
            context.Actions.RememberBrowser(Browser);
            addedAt = clock();
            OfferTyping = false;
            Added = true;
        }
        catch (Exception e) when (e is CanvasLibraryException or HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            Problem = "Your library didn't answer. Try again in a moment.";
        }
        finally
        {
            Adding = false;
        }
    }

    /// <summary>Allow: the add-on that is asking gets the library's key, and checks in by itself a moment later.</summary>
    [RelayCommand]
    Task Allow() => AnswerAsync(true);

    /// <summary>"That wasn't me": nothing is handed over, and the door stays shut until Add is pressed again.</summary>
    [RelayCommand]
    async Task NotMe()
    {
        await AnswerAsync(false);
        Added = false;
    }

    async Task AnswerAsync(bool allow)
    {
        if (context.Client is not { } client) return;
        try
        {
            await client.AnswerPairAsync(allow);
            Asking = "";
        }
        catch (Exception e) when (e is CanvasLibraryException or HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            Problem = "Your library didn't answer. Try again in a moment.";
        }
    }

    /// <summary>"Type your school's Canvas address instead": the step that asks for it, with the code the add-on takes.</summary>
    [RelayCommand]
    void TypeInstead()
    {
        if (told) return;
        told = true;
        OnTypeInstead?.Invoke();
    }

    /// <summary>Copy, beside the code for an add-on from before.</summary>
    [RelayCommand]
    void CopyCode() => context.Actions.Copy(ConnectionCode);

    /// <summary>"Open the page again", for a student who closed it.</summary>
    [RelayCommand]
    void OpenAgain()
    {
        if (context.Actions.OpenAddOn(Browser) is { } problem) Problem = problem;
    }

    /// <summary>"Open Canvas": the browser, to sign in to Canvas there as they usually do.</summary>
    [RelayCommand]
    void OpenBrowser() => context.Actions.OpenBrowser();

    /// <summary>The browsers here that get the add-on from a store, the student's own first; Safari (or a computer with
    /// none) is said, with the one used instead.</summary>
    void OfferBrowsers()
    {
        var offer = context.Actions.Browsers().Where(b => Browsers.StorePage(b).Length > 0).ToList();
        if (offer.Count == 0) offer = [Browsers.Chrome];
        BrowserChoices.Clear();
        foreach (var b in offer) BrowserChoices.Add(new BrowserChoice(b) { Pick = new RelayCommand(() => Use(b)) });
        var advice = context.Actions.Advise?.Invoke();
        Advice = advice switch
        {
            { NoneHere: true } => "Syft Notes works in Chrome, Edge, Brave, Arc or Firefox. Get Chrome, then come back here.",
            { Usual: { } usual } when usual != offer[0] => $"Syft Notes doesn't work in {usual.Name}, so it uses {offer[0].Name}.",
            _ => "",
        };
        Use(offer[0]);
        OnPropertyChanged(nameof(CanSwitchBrowser));
    }

    void Use(Browser browser)
    {
        if (Browser != browser) Added = false;
        Browser = browser;
        foreach (var c in BrowserChoices) c.Current = c.Browser == browser;
    }

    /// <summary>Whether a browser here can have the add-on from its store (else setup asks for the school's address, the
    /// way a pasted code needs it).</summary>
    public static bool AnyFromStore(CanvasContext context) => context.Actions.Browsers().Any(b => Browsers.StorePage(b).Length > 0);

    /// <summary>The store the student's browser gets the add-on from (the first browser here that has one, as the step
    /// picks it); null with none.</summary>
    public static AddOnStore? StoreOf(CanvasContext context) =>
        context.Actions.Browsers().FirstOrDefault(b => Browsers.StorePage(b).Length > 0) is { } b
            ? b.Family == BrowserFamily.Firefox ? AddOnStore.Firefox : AddOnStore.Chrome
            : null;

    /// <summary>Whether the store this student's browser gets the add-on from serves one that finds Canvas by itself
    /// right now; false with no such browser, or when the store can't be asked and was never heard.</summary>
    public static async Task<bool> FindsCanvasAsync(CanvasContext context, StoreVersions stores) =>
        StoreOf(context) is { } store && await stores.FindsCanvasAsync(store);

    public void Dispose()
    {
        watch.Changed -= OnWatchChanged;
        hurry?.Dispose();
        hurry = null;
    }
}
