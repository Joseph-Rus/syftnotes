using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StudyStash.App.Services;
using StudyStash.App.ViewModels.Kit;
using StudyStash.Core.Filing;

namespace StudyStash.App.ViewModels;

/// <summary>What the tidy card asks of the app: the system's file picker, opened in a folder to look in (the file's
/// path, or null when the student closed it), and that the hand-in was tidied (it leaves "Ready to tidy").</summary>
public sealed record TidyServices(Func<string, string, Task<string?>> PickFile, Action<TidyItem> Tidied);

/// <summary>A file on the tidy card: an icon by its kind, its name, and why it's thought to be one (the tip). On means
/// it moves with Tidy up; a tap turns it off (faded) or on again.</summary>
public sealed partial class TidyFile : ObservableObject
{
    public TidyFile(string path, string why, bool on)
    {
        Path = path;
        Why = why;
        On = on;
    }

    public string Path { get; private set; }
    public string Name => FilingFolderRow.LastPart(Path);
    public string Glyph => AssignmentModel.FileGlyph(System.IO.Path.GetExtension(Name).TrimStart('.'));
    public string Why { get; }
    [ObservableProperty] public partial bool On { get; set; }

    [RelayCommand]
    void Toggle() => On = !On;
}

/// <summary>
/// The tidy card (Submit &amp; Organize, test builds only), opened from Ready to tidy, the notification or the assignment's
/// page: the assignment, its class in its colour, the file handed in (sure; "Yes / Choose another"; or "Choose the
/// file"), the files worked from as icons (the sure ones on), and Tidy up (Clean: "Tidy up and clear the rest") or Later.
/// Nothing moves before Tidy up. Then the view runs the files into the class's folder and the card says Filed, with
/// Undo. <see cref="Over"/> closes it.
/// </summary>
public sealed partial class TidyCardModel(CanvasContext context, TidyItem item, IBrush dot, TidyServices services) : ObservableObject
{
    public TidyItem Item => item;
    public string Title => item.Waiting.Assignment;
    public string Class => ClassNames.Display(item.Waiting.Class);
    public IBrush Dot => dot;
    public bool Clean => item.Check.Mode == FilingSettings.Clean;
    public string TidyLabel => Clean ? FilingWords.TidyUpAndClear : FilingWords.TidyUp;

    /// <summary>The file handed in, or null when none was found (or the one found isn't it).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasHandedIn), nameof(NoHandedIn))]
    public partial TidyFile? HandedIn { get; private set; } = item.Check.File is { Found: not "none", Path: { } p } ? new TidyFile(p, FilingWords.IsThisIt, true) : null;

    /// <summary>The file found has the name and size of the one handed in, but Syft Notes couldn't be sure: Yes / Choose another.</summary>
    [ObservableProperty] public partial bool Unsure { get; private set; } = item.Check.File.Found == "unsure";
    public bool HasHandedIn => HandedIn is not null;
    public bool NoHandedIn => HandedIn is null;

    public ObservableCollection<TidyFile> Materials { get; } = [.. item.Check.Materials.Select(m => new TidyFile(m.Path, m.Why, m.Sure == "high"))];
    public bool HasMaterials => Materials.Count > 0;

    /// <summary>Why a file chosen isn't one Syft Notes may move (it isn't in a folder to look in).</summary>
    [ObservableProperty] public partial string? PickSay { get; private set; }

    public AsyncAction Tidying { get; } = new();
    public AsyncAction Undoing { get; } = new();

    /// <summary>It's filed: the card says so, with Undo.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowChoosing), nameof(ShowFiled))]
    public partial bool Filed { get; private set; }
    /// <summary>The view is running the files into the folder (set back by the view when it's done).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowChoosing), nameof(ShowFiled))]
    public partial bool Moving { get; set; }
    public bool ShowChoosing => !Filed || Moving;
    public bool ShowFiled => Filed && !Moving;
    /// <summary>What Undo did, once it has.</summary>
    [ObservableProperty] public partial string? Undone { get; private set; }
    [ObservableProperty] public partial bool Over { get; private set; }

    string? run;

    /// <summary>"Yes": the file found is the one handed in.</summary>
    [RelayCommand]
    void ItIs() => Unsure = false;

    /// <summary>"Choose another" / "Choose the file": the picker, in the first folder to look in. A file outside those
    /// folders isn't taken: Syft Notes moves nothing from anywhere else.</summary>
    [RelayCommand]
    async Task Choose()
    {
        if (item.Check.Folders.Count == 0) return;
        if (await services.PickFile(item.Check.Folders[0], FilingWords.PickTitle(Title)) is not { } path) return;
        if (!item.Check.Folders.Any(f => new FilingFolderRow(f).Holds(path)))
        {
            PickSay = FilingWords.OutsideFolders([.. item.Check.Folders.Select(f => new FilingFolderRow(f).Name)]);
            return;
        }
        PickSay = null;
        HandedIn = new TidyFile(path, FilingWords.IsThisIt, true);
        Unsure = false;
        if (Materials.FirstOrDefault(m => m.Path == path) is { } same) Materials.Remove(same);
    }

    [RelayCommand]
    void Later() => Over = true;

    /// <summary>Tidy up: the file handed in (unless it's still unsure) and the files that are on. Only now does anything
    /// move, and the Trash only in Clean.</summary>
    [RelayCommand]
    Task TidyUp() => Tidying.RunAsync(async ct =>
    {
        if (context.Client is not { } client) return Outcome.Failed(Failure.Offline);
        string? file = HandedIn is { On: true } h && !Unsure ? h.Path : null;
        var on = Materials.Where(m => m.On).Select(m => m.Path).ToList();
        var done = await LibraryCall.Run(() => client.FileHandInAsync(item.Waiting.Class, item.Waiting.Key, file, on, Clean, ct));
        if (done.Value is not { } filed) return done;
        run = filed.Run;
        Moving = true;
        Filed = true;
        services.Tidied(item);
        return done;
    });

    [RelayCommand]
    Task Undo() => Undoing.RunAsync(async ct =>
    {
        if (context.Client is not { } client || run is null) return Outcome.Failed(Failure.Offline);
        var done = await LibraryCall.Run(() => client.UndoFilingAsync(run, ct));
        if (done.Value is { } undone) Undone = FilingWords.Undone(undone.Items.Select(i => i.How));
        return done;
    });

    /// <summary>The card faded away after it said Filed.</summary>
    public void Close() => Over = true;
}
