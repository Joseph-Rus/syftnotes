using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StudyStash.App.Services;
using StudyStash.App.ViewModels.Kit;
using StudyStash.Core.Filing;

namespace StudyStash.App.ViewModels;

/// <summary>A folder Submit &amp; Organize may look in: its name, and its whole path for the tip.</summary>
public sealed record FilingFolderRow(string Path)
{
    public string Name => LastPart(Path);

    /// <summary>A path's last part: a file's or folder's name, whichever way its slashes go (the library may be on
    /// another system than the one drawing it, as in the pictures).</summary>
    public static string LastPart(string path) => path.Split('/', '\\').LastOrDefault(p => p.Length > 0) ?? path;

    /// <summary><paramref name="file"/> is somewhere in this folder (in any case, but on Linux).</summary>
    public bool Holds(string file)
    {
        string root = System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(Path)) + System.IO.Path.DirectorySeparatorChar;
        return System.IO.Path.GetFullPath(file).StartsWith(root, OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>One run in "Filed work": its assignment, a line (class, when, how many files), and Undo, which says what it
/// did (<see cref="Said"/>) once it has.</summary>
public sealed partial class FiledRunRow : ObservableObject
{
    public FiledRunRow(CanvasApi.FiledRun run, string line, Func<FiledRunRow, CancellationToken, Task<Outcome>> undo)
    {
        Run = run;
        Line = line;
        Undo = new AsyncAction(ct => undo(this, ct));
        CanUndo = run.Undone < run.Files;
    }

    public CanvasApi.FiledRun Run { get; }
    public string Title => Run.Assignment;
    public string Line { get; }
    public AsyncAction Undo { get; }
    /// <summary>A file of the run is still filed, so Undo has something to put back.</summary>
    [ObservableProperty] public partial bool CanUndo { get; set; }
    /// <summary>What Undo did (<see cref="FilingWords.Undone"/>); null before it ran.</summary>
    [ObservableProperty] public partial string? Said { get; set; }
}

/// <summary>
/// Settings → Canvas's "After you hand in" (Submit &amp; Organize, test builds only): the switch, the folders it may look
/// in (Downloads, Desktop and Documents offered, none added until the student picks; a whole disk, the home folder and
/// the library refused by the library with the reason), when, what happens to the files worked from, and "Filed work",
/// the one place every run can be undone. Every choice is one save and shows what the library has after it.
/// </summary>
public sealed partial class SubmitOrganizeModel(CanvasContext context) : ObservableObject
{
    /// <summary>The system's folder picker, from the view (it needs the window); null in a test that doesn't pick.</summary>
    public Func<Task<string?>>? PickFolder { get; set; }

    /// <summary>Busy and what went wrong for every choice here, and the reason a folder was refused.</summary>
    public AsyncAction Saving { get; } = new();

    bool applying;

    [ObservableProperty] public partial bool On { get; set; }
    public ObservableCollection<FilingFolderRow> Folders { get; } = [];
    public ObservableCollection<CanvasApi.FolderSuggestion> Suggested { get; } = [];
    [ObservableProperty] public partial bool HasFolders { get; private set; }
    [ObservableProperty] public partial bool AskFirst { get; private set; } = true;
    [ObservableProperty] public partial bool Organize { get; private set; } = true;
    public ObservableCollection<FiledRunRow> Runs { get; } = [];
    [ObservableProperty] public partial bool HasRuns { get; private set; }

    public string ToTrash => FilingWords.ToTrash;

    CanvasClient Client => context.Client ?? throw new InvalidOperationException("No library is set up.");

    public Task LoadAsync() => Saving.RunAsync(ct => SaveAsync(c => c.FilingAsync(ct)));

    /// <summary>One call that answers with the library's choices, shown as they now stand.</summary>
    async Task<Outcome> SaveAsync(Func<CanvasClient, Task<CanvasApi.Filing?>> call)
    {
        if (context.Client is null) return Outcome.Failed(Failure.Offline);
        var done = await LibraryCall.Run(() => call(Client));
        if (done.Value is { } filing) Show(filing);
        return done;
    }

    void Show(CanvasApi.Filing f)
    {
        applying = true;
        try
        {
            On = f.On;
        }
        finally
        {
            applying = false;
        }
        Folders.Clear();
        foreach (string p in f.Folders) Folders.Add(new FilingFolderRow(p));
        HasFolders = Folders.Count > 0;
        Suggested.Clear();
        foreach (var s in f.Suggested) Suggested.Add(s);
        AskFirst = f.When != FilingSettings.Later;
        Organize = f.Mode != FilingSettings.Clean;
        Runs.Clear();
        var zone = context.Clock.Zone;
        var now = context.Clock.Now();
        // Newest first, whatever order they came in.
        foreach (var r in f.Runs.OrderByDescending(r => r.At ?? DateTimeOffset.MinValue))
            Runs.Add(new FiledRunRow(r, FilingWords.RunLine(r.Class, r.At is { } at ? CanvasWords.When(at, zone, now) : "", r.Files), UndoAsync));
        HasRuns = Runs.Count > 0;
    }

    partial void OnOnChanged(bool value)
    {
        if (!applying) UiTasks.Run(() => Saving.RunAsync(ct => SaveAsync(c => c.SaveFilingAsync(on: value, stop: ct))), "SaveFiling");
    }

    /// <summary>Adds a folder: the library looks in it there and then (a Mac asks the student about Downloads, Desktop
    /// and Documents right after this click), or says why it can't be one.</summary>
    public Task AddAsync(string path) => Saving.RunAsync(ct => SaveAsync(c => c.SaveFilingAsync(add: path, stop: ct)));

    [RelayCommand]
    Task AddSuggested(CanvasApi.FolderSuggestion s) => AddAsync(s.Path);

    [RelayCommand]
    async Task AddFolder()
    {
        if (PickFolder is not null && await PickFolder() is { } path) await AddAsync(path);
    }

    [RelayCommand]
    Task RemoveFolder(FilingFolderRow row) => Saving.RunAsync(ct => SaveAsync(c => c.SaveFilingAsync(remove: row.Path, stop: ct)));

    [RelayCommand]
    Task PickWhen(string when) => Saving.RunAsync(ct => SaveAsync(c => c.SaveFilingAsync(when: when, stop: ct)));

    [RelayCommand]
    Task PickMode(string mode) => Saving.RunAsync(ct => SaveAsync(c => c.SaveFilingAsync(mode: mode, stop: ct)));

    async Task<Outcome> UndoAsync(FiledRunRow row, CancellationToken ct)
    {
        if (context.Client is null) return Outcome.Failed(Failure.Offline);
        var done = await LibraryCall.Run(() => Client.UndoFilingAsync(row.Run.Run, ct));
        if (done.Value is not { } undone) return done;
        row.Said = FilingWords.Undone(undone.Items.Select(i => i.How));
        row.CanUndo = undone.Items.Any(i => i.How == "failed");
        return done;
    }
}
