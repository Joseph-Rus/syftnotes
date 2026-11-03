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

/// <summary>
/// "Which notes folder should Syft Notes use?" (<see cref="LibraryFolderPick"/>): a row per library folder found on
/// this computer, the one in use picked to begin with, and Use this folder. Keeping the current one is just using its
/// row. What using a folder does, showing one in Finder or File Explorer, and closing the window are handed in.
/// </summary>
public sealed partial class NotesFolderPickModel : ObservableObject
{
    readonly Func<LibraryFolder, Task<Outcome>> use;

    /// <param name="found">The library folders, the one in use first (<see cref="LibraryFolders.Find(string?)"/>).</param>
    /// <param name="userHome">For where each one is, in words.</param>
    /// <param name="use">Makes the pick the library's folder (and remembers the answer).</param>
    /// <param name="reveal">Shows a folder in Finder or File Explorer.</param>
    /// <param name="today">For the year: a date this year leaves it out.</param>
    public NotesFolderPickModel(IReadOnlyList<LibraryFolder> found, string userHome, Func<LibraryFolder, Task<Outcome>> use,
        Action<string> reveal, DateOnly? today = null)
    {
        this.use = use;
        var now = today ?? DateOnly.FromDateTime(DateTime.Now);
        string home = LibraryFolders.Real(userHome);
        var names = found.Select(f => LibraryFolders.Where(f.Path, home)).ToList();
        Rows = [.. found.Select((f, i) =>
        {
            NotesFolderRow? row = null;
            // Two folders that read the same in words (rare: the same place under two names) are told apart by path.
            string where = names.Count(n => n == names[i]) > 1 ? f.Path : names[i];
            row = new NotesFolderRow
            {
                Folder = f, Where = where, Detail = Detail(f, now), IsPicked = f.InUse,
                Pick = new RelayCommand(() => PickRow(row!)),
                Show = new RelayCommand(() => reveal(f.Path)),
            };
            return row;
        })];
        if (!Rows.Any(r => r.IsPicked) && Rows.Count > 0) Rows[0].IsPicked = true;
        Use = new AsyncAction(_ => UseAsync());
    }

    public static string Title => "Which notes folder should Syft Notes use?";
    public static string Lede =>
        "Your lecture notes are in more than one folder on this computer. Pick the one Syft Notes should keep using. Nothing is moved or deleted.";
    public static string RevealLabel => OperatingSystem.IsWindows() ? "Show in File Explorer" : "Show in Finder";

    public IReadOnlyList<NotesFolderRow> Rows { get; }
    public AsyncAction Use { get; }

    /// <summary>The window's height for this many rows (its width is the view's own).</summary>
    public double WantedHeight => 330 + Rows.Count * 84;

    /// <summary>The pick was used: the window closes.</summary>
    public Action? OnDone { get; set; }

    void PickRow(NotesFolderRow row)
    {
        foreach (var r in Rows) r.IsPicked = r == row;
    }

    async Task<Outcome> UseAsync()
    {
        var picked = Rows.FirstOrDefault(r => r.IsPicked) ?? Rows[0];
        var outcome = await use(picked.Folder);
        if (outcome.Ok) OnDone?.Invoke();
        return outcome;
    }

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
