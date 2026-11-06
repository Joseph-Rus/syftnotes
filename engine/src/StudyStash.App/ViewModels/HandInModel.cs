using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StudyStash.App.Services;
using StudyStash.App.ViewModels.Kit;
using StudyStash.Core.Filing;

namespace StudyStash.App.ViewModels;

/// <summary>What the checklist asks of the app: the system's file picker, opened in the first of the folders to look in
/// (the file's path, or null when the student closed it), and a notification with an action.</summary>
public sealed record HandInServices(Func<string, string, Task<string?>> PickFile, Action<ToastRequested> Toast);

/// <summary>A file the student may have worked from: its name, its path, why it's thought to be one, and a
/// tick (ticked when Syft Notes is sure).</summary>
public sealed partial class HandInMaterialRow(CanvasApi.HandInMaterial m) : ObservableObject
{
    public string Path => m.Path;
    public string Name => FilingFolderRow.LastPart(m.Path);
    public string Why => m.Why;
    [ObservableProperty] public partial bool Ticked { get; set; } = m.Sure == "high";
}

/// <summary>
/// The checklist that opens after a hand-in (Submit &amp; Organize, "Ask me"; test builds only): "You handed in Lab 3",
/// the file handed in as it was found (sure; "Is this the file you handed in?"; or "Choose the file"), the files worked
/// from with why, the sure ones ticked, and one button: File it (keep them together) or File it and clear the rest
/// (the ticked ones to the Trash). Nothing moves before that press. Afterwards a notification says where it went, with
/// Undo. <see cref="Over"/> closes it.
/// </summary>
public sealed partial class HandInModel(CanvasContext context, CanvasApi.HandInWaiting waiting, HandInServices services) : ObservableObject
{
    CanvasApi.HandInCheck? check;

    public string Title => FilingWords.Title(waiting.Assignment);
    public string Intro => FilingWords.Intro(waiting.Class, waiting.Assignment);

    public AsyncAction Loading { get; } = new();
    public AsyncAction Filing { get; } = new();

    /// <summary>"exact", "unsure" or "none": how sure the file handed in is the one found (or picked: then "exact").</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsExact), nameof(IsUnsure), nameof(IsNone), nameof(NotFound))]
    public partial string Found { get; private set; } = "";
    [ObservableProperty] public partial string? FilePath { get; private set; }
    [ObservableProperty] public partial string FileName { get; private set; } = "";
    public bool IsExact => Found == "exact";
    public bool IsUnsure => Found == "unsure";
    public bool IsNone => Found == "none";
    public string NotFound => FilingWords.NotFound(FileName);
    /// <summary>Why a file picked isn't one Syft Notes may move (it isn't in a folder to look in).</summary>
    [ObservableProperty] public partial string? PickSay { get; private set; }

    public ObservableCollection<HandInMaterialRow> Materials { get; } = [];
    [ObservableProperty] public partial bool HasMaterials { get; private set; }
    [ObservableProperty] public partial bool Loaded { get; private set; }

    /// <summary>Clean: the ticked files go to the Trash, and the button says so.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FileLabel), nameof(MaterialsLabel))]
    public partial bool Clean { get; private set; }
    public string FileLabel => Clean ? FilingWords.FileItAndClear : FilingWords.FileIt;
    public string MaterialsLabel => Clean ? FilingWords.MaterialsLabelClean : FilingWords.MaterialsLabel;

    /// <summary>It's done with (filed, or Not now): the sheet closes.</summary>
    [ObservableProperty] public partial bool Over { get; private set; }

    /// <summary>Finds the file handed in and the files worked from, in the folders to look in only.</summary>
    public Task LoadAsync() => Loading.RunAsync(async ct =>
    {
        if (context.Client is not { } client) return Outcome.Failed(Failure.Offline);
        var done = await LibraryCall.Run(() => client.CheckHandInAsync(waiting.Class, waiting.Key, ct));
        if (done.Value is { } c) Show(c);
        return done;
    });

    /// <summary>Shows what the library found (a test gives it straight).</summary>
    public void Show(CanvasApi.HandInCheck c)
    {
        check = c;
        FileName = c.File.Name;
        FilePath = c.File.Path;
        Found = c.File.Found;
        Clean = c.Mode == FilingSettings.Clean;
        Materials.Clear();
        foreach (var m in c.Materials) Materials.Add(new HandInMaterialRow(m));
        HasMaterials = Materials.Count > 0;
        Loaded = true;
    }

    /// <summary>"Yes": the file found is the one handed in.</summary>
    [RelayCommand]
    void ItIs() => Found = "exact";

    /// <summary>"Choose another" / "Choose the file": the picker, in the first folder to look in. A file outside those
    /// folders isn't taken: Syft Notes moves nothing from anywhere else.</summary>
    [RelayCommand]
    async Task Choose()
    {
        if (check is not { Folders.Count: > 0 } c) return;
        if (await services.PickFile(c.Folders[0], FilingWords.PickTitle(c.Assignment)) is not { } path) return;
        if (!c.Folders.Any(f => new FilingFolderRow(f).Holds(path)))
        {
            PickSay = FilingWords.OutsideFolders([.. c.Folders.Select(f => new FilingFolderRow(f).Name)]);
            return;
        }
        PickSay = null;
        FilePath = path;
        FileName = FilingFolderRow.LastPart(path);
        Found = "exact";
        if (Materials.FirstOrDefault(m => m.Path == path) is { } same) Materials.Remove(same);
    }

    [RelayCommand]
    void NotNow() => Over = true;

    /// <summary>File it: the file handed in (when it's sure, or the student said so) and the ticked files. Only now does
    /// anything move, and the Trash only in Clean.</summary>
    [RelayCommand]
    Task FileIt() => Filing.RunAsync(async ct =>
    {
        if (context.Client is not { } client || check is not { } c) return Outcome.Failed(Failure.Offline);
        string? file = IsExact ? FilePath : null;
        var ticked = Materials.Where(m => m.Ticked).Select(m => m.Path).ToList();
        var done = await LibraryCall.Run(() => client.FileHandInAsync(c.Class, c.Key, file, ticked, Clean, ct));
        if (done.Value is not { } filed) return done;
        Over = true;
        services.Toast(new ToastRequested(FilingWords.FiledTitle(c.Class, c.Assignment), FilingWords.FiledText(filed.Count, filed.Left.Count),
            FilingWords.Undo, () => UiTasks.Run(() => UndoAsync(client, filed.Run), "UndoFiling")));
        return done;
    });

    async Task UndoAsync(CanvasClient client, string run)
    {
        var done = await LibraryCall.Run(() => client.UndoFilingAsync(run));
        services.Toast(new ToastRequested(FilingWords.PutBackTitle,
            done.Value is { } undone ? FilingWords.Undone(undone.Items.Select(i => i.How)) : done.Failure!.Words));
    }
}
