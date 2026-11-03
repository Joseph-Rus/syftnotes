using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StudyStash.App.Services;
using StudyStash.App.ViewModels.Kit;
using StudyStash.Core;

namespace StudyStash.App.ViewModels;

/// <summary>One notes folder to pick: where it is, how many lectures it has and when the last one was, and whether
/// it's the one in use now.</summary>
public sealed partial class NotesFolderRow : ObservableObject
{
    public required LibraryFolder Folder { get; init; }
    public required string Where { get; init; }
    public required string Detail { get; init; }
    public string Path => Folder.Path;
    public bool InUse => Folder.InUse;
    [ObservableProperty] public partial bool IsPicked { get; set; }
    public required IRelayCommand Pick { get; init; }
    public required IRelayCommand Show { get; init; }
}

/// <summary>One other folder on the summary: where it is, and what bringing in its notes does (or did).</summary>
public sealed record NotesFolderPlanRow(string Where, string Says);

/// <summary>The window's three steps: pick a folder; what bringing in the others' notes will do; what it did.</summary>
public enum NotesFolderStep { Pick, Summary, Done }

/// <summary>
/// "You have Syft Notes folders in two places" (<see cref="LibraryFolderPick"/>): a row per library folder found on
/// this computer, the one in use picked to begin with, Use this one, and Bring in notes from the other. That one shows
/// first what it will do ("12 lectures to bring in, 3 already here, 1 different in both (both kept)"), then, once done,
/// what it did. Nothing in the other folders is ever changed. What using a folder, bringing notes in and looking at what
/// that would do are, showing a folder in Finder or File Explorer, and closing the window are handed in.
/// </summary>
public sealed partial class NotesFolderPickModel : ObservableObject
{
    public delegate Task<Outcome<List<BringInCount>>> UseFolder(LibraryFolder picked, bool bringIn);

    readonly UseFolder use;
    readonly Func<LibraryFolder, Task<Outcome<List<BringInCount>>>> plan;
    readonly Dictionary<string, string> whereOf;

    /// <param name="found">The library folders, the one in use first (<see cref="LibraryFolders.Find(string?)"/>).</param>
    /// <param name="userHome">For where each one is, in words.</param>
    /// <param name="use">Makes the pick the library's folder (and remembers the answer), bringing in the others' notes
    /// when asked: what that did.</param>
    /// <param name="plan">What bringing in the others' notes would do, with the pick as the library's folder.</param>
    /// <param name="reveal">Shows a folder in Finder or File Explorer.</param>
    /// <param name="today">For the year: a date this year leaves it out.</param>
    public NotesFolderPickModel(IReadOnlyList<LibraryFolder> found, string userHome, UseFolder use,
        Func<LibraryFolder, Task<Outcome<List<BringInCount>>>> plan, Action<string> reveal, DateOnly? today = null)
    {
        this.use = use;
        this.plan = plan;
        var now = today ?? DateOnly.FromDateTime(DateTime.Now);
        string home = LibraryFolders.Real(userHome);
        var names = found.Select(f => LibraryFolders.Where(f.Path, home)).ToList();
        whereOf = new(LibraryFolders.Comparer);
        Rows = [.. found.Select((f, i) =>
        {
            NotesFolderRow? row = null;
            // Two folders that read the same in words (rare: the same place under two names) are told apart by path.
            string where = names.Count(n => n == names[i]) > 1 ? f.Path : names[i];
            whereOf[f.Path] = where;
            row = new NotesFolderRow
            {
                Folder = f, Where = where, Detail = Detail(f, now), IsPicked = f.InUse,
                Pick = new RelayCommand(() => PickRow(row!)),
                Show = new RelayCommand(() => reveal(f.Path)),
            };
            return row;
        })];
        if (!Rows.Any(r => r.IsPicked) && Rows.Count > 0) Rows[0].IsPicked = true;
        Use = new AsyncAction(_ => UseAsync(bringIn: false));
        Look = new AsyncAction(_ => LookAsync());
        Bring = new AsyncAction(_ => UseAsync(bringIn: true));
        foreach (var a in new[] { Use, Look, Bring })
            a.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(AsyncAction.Busy)) OnPropertyChanged(nameof(Busy));
                if (e.PropertyName == nameof(AsyncAction.Say)) OnPropertyChanged(nameof(Say));
            };
        Finish = new RelayCommand(() => OnDone?.Invoke());
        Back = new RelayCommand(() => Step = NotesFolderStep.Pick);
    }

    public static string Lede => "Pick the one Syft Notes should use. Nothing is moved or deleted.";
    public static string UseLabel => "Use this one";
    public static string RevealLabel => OperatingSystem.IsWindows() ? "Show in File Explorer" : "Show in Finder";
    public static string BackLabel => "Back";
    public static string DoneLabel => "Done";

    /// <summary>"You have Syft Notes folders in two places".</summary>
    public string Title => $"You have Syft Notes folders in {Number(Rows.Count)} places";
    /// <summary>"Bring in notes from the other" (the others, with three or more).</summary>
    public string BringLabel => Rows.Count > 2 ? "Bring in notes from the others" : "Bring in notes from the other";

    public IReadOnlyList<NotesFolderRow> Rows { get; }
    public AsyncAction Use { get; }
    /// <summary>Works out what bringing in the others' notes would do, and shows it.</summary>
    public AsyncAction Look { get; }
    /// <summary>Uses the picked folder and brings the others' notes into it.</summary>
    public AsyncAction Bring { get; }
    public IRelayCommand Finish { get; }
    /// <summary>One of them is running: the buttons wait, and the loader shows.</summary>
    public bool Busy => Use.Busy || Look.Busy || Bring.Busy;
    /// <summary>What went wrong with the last of them, in plain words; null when nothing did.</summary>
    public string? Say => Bring.Say ?? Look.Say ?? Use.Say;
    public IRelayCommand Back { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OnPick), nameof(OnSummary), nameof(OnDoneStep))]
    public partial NotesFolderStep Step { get; set; }
    public bool OnPick => Step == NotesFolderStep.Pick;
    public bool OnSummary => Step == NotesFolderStep.Summary;
    public bool OnDoneStep => Step == NotesFolderStep.Done;

    /// <summary>The summary's or the result's title and lede, and a line per other folder.</summary>
    [ObservableProperty] public partial string StepTitle { get; private set; } = "";
    [ObservableProperty] public partial string StepLede { get; private set; } = "";
    [ObservableProperty] public partial IReadOnlyList<NotesFolderPlanRow> PlanRows { get; private set; } = [];
    /// <summary>The summary's button: "Bring them in", or "Use this one" when there's nothing to bring.</summary>
    [ObservableProperty] public partial string BringButton { get; private set; } = "Bring them in";
    /// <summary>What the summary's button does: <see cref="Bring"/>, or <see cref="Use"/> when there's nothing to bring.</summary>
    [ObservableProperty] public partial AsyncAction? SummaryAction { get; private set; }

    /// <summary>The window's height for this many rows (its width is the view's own).</summary>
    public double WantedHeight => 330 + Rows.Count * 84;

    /// <summary>The pick was used: the window closes.</summary>
    public Action? OnDone { get; set; }

    NotesFolderRow Picked => Rows.FirstOrDefault(r => r.IsPicked) ?? Rows[0];

    void PickRow(NotesFolderRow row)
    {
        foreach (var r in Rows) r.IsPicked = r == row;
    }

    async Task<Outcome> UseAsync(bool bringIn)
    {
        var outcome = await use(Picked.Folder, bringIn);
        if (!outcome.Ok) return outcome;
        if (!bringIn)
        {
            OnDone?.Invoke();
            return outcome;
        }
        var done = outcome.Value ?? [];
        PlanRows = [.. done.Select(d => new NotesFolderPlanRow(WhereOf(d.From), Did(d)))];
        StepTitle = "Your notes are in one place";
        StepLede = Result(done, Picked.Where, WhereOf);
        Step = NotesFolderStep.Done;
        return outcome;
    }

    async Task<Outcome> LookAsync()
    {
        var outcome = await plan(Picked.Folder);
        if (!outcome.Ok) return outcome;
        var plans = outcome.Value ?? [];
        PlanRows = [.. plans.Select(p => new NotesFolderPlanRow(WhereOf(p.From), Says(p)))];
        bool any = plans.Any(p => p.New + p.Both > 0);
        StepTitle = Rows.Count > 2 ? "Bring in notes from the other folders" : "Bring in notes from the other folder";
        StepLede = any
            ? $"They're copied into {Picked.Where}. The other {(Rows.Count > 2 ? "folders stay" : "folder stays")} as it is."
            : "Everything there is already here.";
        BringButton = any ? "Bring them in" : UseLabel;
        SummaryAction = any ? Bring : Use;
        Step = NotesFolderStep.Summary;
        return outcome;
    }

    string WhereOf(string path) => whereOf.TryGetValue(path, out string? w) ? w : path;

    /// <summary>"12 lectures to bring in · 3 already here · 1 different in both (both kept)"; what's 0 is left out.</summary>
    public static string Says(BringInCount p)
    {
        var parts = new List<string>();
        if (p.New > 0) parts.Add($"{Lectures(p.New)} to bring in");
        if (p.Here > 0) parts.Add($"{p.Here} already here");
        if (p.Both > 0) parts.Add($"{p.Both} different in both (both kept)");
        if (p.Deleted > 0) parts.Add($"{p.Deleted} you deleted {(p.Deleted == 1 ? "stays" : "stay")} deleted");
        return parts.Count > 0 ? string.Join(" · ", parts) : "No lecture notes in it";
    }

    /// <summary>What it did, in a line: "12 lectures brought in · 1 kept twice".</summary>
    public static string Did(BringInCount d)
    {
        var parts = new List<string>();
        if (d.New > 0) parts.Add($"{Lectures(d.New)} brought in");
        if (d.Both > 0) parts.Add($"{d.Both} kept twice");
        if (d.Here > 0) parts.Add($"{d.Here} already here");
        return parts.Count > 0 ? string.Join(" · ", parts) : "Nothing new";
    }

    /// <summary>The result's lede: where the notes are now, and how a lecture kept twice is told apart ("… its title
    /// ends with “(from OneDrive – Personal)”").</summary>
    public static string Result(IReadOnlyList<BringInCount> done, string where, Func<string, string> whereOf)
    {
        int both = done.Sum(d => d.Both);
        string lede = $"Syft Notes uses {where} from now on. {(done.Count > 1 ? "The other folders weren't" : "The other folder wasn't")} changed.";
        if (both == 0) return lede;
        // As the library names the copy: the first part of where its folder is.
        string from = string.Join(" or ", done.Where(d => d.Both > 0).Select(d => $"“(from {whereOf(d.From).Split(" › ")[0]})”").Distinct());
        return lede + $" {(both == 1 ? "1 lecture was" : $"{both} lectures were")} different in each folder, so both are kept: the other one's title ends with {from}.";
    }

    static string Lectures(int n) => n == 1 ? "1 lecture" : $"{n.ToString("N0", CultureInfo.InvariantCulture)} lectures";

    static string Number(int n) => n switch { 2 => "two", 3 => "three", 4 => "four", 5 => "five", _ => n.ToString(CultureInfo.InvariantCulture) };

    /// <summary>"12 lectures, last one Oct 2" (with the year when it isn't this one); "5000+ lectures" past the count.</summary>
    public static string Detail(LibraryFolder f, DateOnly today)
    {
        var en = CultureInfo.InvariantCulture;
        string count = f.Capped ? $"{f.Lectures.ToString("N0", en)}+ lectures" : f.Lectures == 1 ? "1 lecture" : $"{f.Lectures.ToString("N0", en)} lectures";
        if (f.Newest is not { } day) return count;
        string when = day.ToString(day.Year == today.Year ? "MMM d" : "MMM d, yyyy", en);
        return $"{count}, last one {when}";
    }
}
