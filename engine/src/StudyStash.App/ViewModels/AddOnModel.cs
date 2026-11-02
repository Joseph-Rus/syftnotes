using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StudyStash.App.Services;

namespace StudyStash.App.ViewModels;

/// <summary>
/// The first run's Connect your classes with nothing to type (the add-on from 1.8): Add Syft Notes to Chrome (or the
/// student's own browser) opens the add-on's page in that browser's store, and opens the library's door to it for a
/// few minutes. Once added, the add-on finds Syft Notes on this computer by itself and checks in; then it finds the
/// student's Canvas from their own sign-in and tells the library. Each shows here as it happens, read from the library's
/// own state through the shared <see cref="CanvasWatch"/> (every three seconds while the step shows), and when both
/// have happened the step is done (<see cref="OnDone"/>). No school address, no code, no word the student wouldn't use.
/// </summary>
public sealed partial class AddOnModel : ObservableObject, IDisposable
{
    readonly CanvasContext context;
    readonly CanvasWatch watch;
    IDisposable? hurry;
    bool told;

    public AddOnModel(CanvasContext context, CanvasWatch watch)
    {
        this.context = context;
        this.watch = watch;
        watch.Changed += OnWatchChanged;
        OfferBrowsers();
    }

    /// <summary>Both are done: the add-on checked in, and the library knows the student's Canvas.</summary>
    public Action? OnDone { get; set; }

    /// <summary>The browsers here that get the add-on from a store (Chrome, Edge, Brave, Arc, Vivaldi, Firefox), the
    /// student's own first.</summary>
    public ObservableCollection<BrowserChoice> BrowserChoices { get; } = [];

    [NotifyPropertyChangedFor(nameof(AddLabel), nameof(BrowserName), nameof(StoreSteps), nameof(AllowStep), nameof(AddedLine), nameof(CanvasLine), nameof(CanvasWaitLine))]
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
    [NotifyPropertyChangedFor(nameof(HasAdvice), nameof(ShowAdd), nameof(Waiting), nameof(AddedLine))]
    [ObservableProperty]
    public partial bool Added { get; set; }

    [ObservableProperty] public partial bool Adding { get; set; }

    /// <summary>The add-on checked in with the library.</summary>
    [NotifyPropertyChangedFor(nameof(ShowAdd), nameof(Waiting), nameof(AddedLine), nameof(CanSwitchBrowser), nameof(LookingForCanvas), nameof(CanvasLine),
        nameof(CanvasWaitLine), nameof(IsDone))]
    [ObservableProperty]
    public partial bool Connected { get; set; }

    /// <summary>The student's Canvas, as the add-on found it ("https://school.instructure.com"); "" until then.</summary>
    [NotifyPropertyChangedFor(nameof(Found), nameof(LookingForCanvas), nameof(CanvasLine), nameof(CanvasWaitLine), nameof(IsDone))]
    [ObservableProperty]
    public partial string CanvasUrl { get; set; } = "";

    /// <summary>Why Add didn't open the browser, or the library didn't answer.</summary>
    [NotifyPropertyChangedFor(nameof(HasProblem))]
    [ObservableProperty]
    public partial string? Problem { get; set; }
    public bool HasProblem => !string.IsNullOrEmpty(Problem);

    public bool ShowAdd => !Added && !Connected;
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

    public string AddedLine => Connected ? $"Added to {BrowserName}" : Added ? $"Waiting for Syft Notes to be added to {BrowserName}" : $"Add Syft Notes to {BrowserName}";

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
        if (s.Extension?.Connected == true) Connected = true;
        if (s.Url.Length > 0) CanvasUrl = s.Url;
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

    public void Dispose()
    {
        watch.Changed -= OnWatchChanged;
        hurry?.Dispose();
        hurry = null;
    }
}
