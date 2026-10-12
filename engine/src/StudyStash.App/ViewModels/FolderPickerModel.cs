using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StudyStash.Core;

namespace StudyStash.App.ViewModels;

/// <summary>A row of the folder picker: Unsorted, a group's name ("Classes"), a folder (its swatch, how many notes it
/// holds, a check on the one the note is in), the line over New folder, or New folder itself.</summary>
public sealed partial class FolderPick : ObservableObject
{
    public const string Unsorted = "unsorted", Label = "label", Folder = "folder", Rule = "rule", New = "new";

    public string Kind { get; init; } = Folder;
    /// <summary>The folder as the library knows it (Unsorted's own name for Unsorted).</summary>
    public string Name { get; init; } = "";
    public string Shown { get; init; } = "";
    public IBrush Dot { get; init; } = Brushes.Gray;
    public int Count { get; init; }
    /// <summary>The folder the note is in now.</summary>
    public bool Current { get; init; }
    /// <summary>The row the arrow keys are on: Return picks it.</summary>
    [ObservableProperty] public partial bool Active { get; set; }

    public bool IsUnsorted => Kind == Unsorted;
    public bool IsLabel => Kind == Label;
    public bool IsFolder => Kind == Folder;
    public bool IsRule => Kind == Rule;
    public bool IsNew => Kind == New;
    /// <summary>A row that can be picked.</summary>
    public bool IsRow => IsUnsorted || IsFolder || IsNew;
    public bool ShowCount => IsFolder && !Current && Count > 0;
    /// <summary>What a screen reader says: the folder, how many notes, and that the note is in it.</summary>
    public string Said => IsNew ? Shown : Current ? $"{Shown}, the folder it's in" : IsFolder && Count > 0 ? $"{Shown}, {Count} note{(Count == 1 ? "" : "s")}" : Shown;
}

/// <summary>
/// The folder picker: where a note goes. A search field ("Move to folder") that narrows the list as it's typed in,
/// Unsorted, every folder under its group's name with its swatch and how many notes it holds (a check on the one the
/// note is in), and New folder, which makes one (named by what's typed, or asked for) and moves the note into it.
/// Arrows move, Return picks, Esc closes. The same picker opens from a Home row's folder label, a note page's
/// breadcrumb and its toolbar's Move.
/// </summary>
public sealed partial class FolderPickerModel : ObservableObject
{
    readonly List<(string Name, string Group, IBrush Dot, int Count)> folders;
    readonly string current;

    /// <param name="folders">Every folder: its name, its sidebar group ("" for Classes), its swatch and its count.</param>
    /// <param name="current">The folder the note is in ("" or Unsorted for none).</param>
    public FolderPickerModel(IEnumerable<(string Name, string Group, IBrush Dot, int Count)> folders, string current)
    {
        this.folders = [.. folders];
        this.current = current.Length == 0 ? Configs.Unsorted : current;
        Build();
    }

    public ObservableCollection<FolderPick> Rows { get; } = [];
    /// <summary>What's typed: it narrows the folders, or (after New folder) names the new one.</summary>
    [ObservableProperty] public partial string Query { get; set; } = "";
    /// <summary>New folder was picked with nothing typed: the field now takes its name.</summary>
    [ObservableProperty] public partial bool Naming { get; set; }
    public string Placeholder => Naming ? "Name the new folder" : "Move to folder";
    public string SearchGlyph => Naming ? "add" : "search";

    /// <summary>Moves the note to a folder (its name; Unsorted's own for none).</summary>
    public Action<string>? OnPick { get; set; }
    /// <summary>Makes a folder by this name and moves the note into it.</summary>
    public Action<string>? OnNew { get; set; }
    /// <summary>Closes the menu (the view sets it).</summary>
    public Action? OnClose { get; set; }

    partial void OnQueryChanged(string value) => Build();

    partial void OnNamingChanged(bool value)
    {
        OnPropertyChanged(nameof(Placeholder));
        OnPropertyChanged(nameof(SearchGlyph));
        Build();
    }

    void Build()
    {
        string q = Query.Trim();
        bool Matches(string shown) => q.Length == 0 || shown.Contains(q, StringComparison.OrdinalIgnoreCase);
        Rows.Clear();
        if (Naming)
        {
            if (q.Length > 0) Rows.Add(new FolderPick { Kind = FolderPick.New, Shown = $"Make “{q}” and move it there", Active = true });
            return;
        }
        if (Matches("Unsorted")) Rows.Add(new FolderPick { Kind = FolderPick.Unsorted, Name = Configs.Unsorted, Shown = "Unsorted", Current = current == Configs.Unsorted });
        foreach (var group in folders.GroupBy(f => f.Group).OrderBy(g => g.Key.Length > 0))
        {
            var found = group.Where(f => Matches(ClassNames.Display(f.Name))).ToList();
            if (found.Count == 0) continue;
            Rows.Add(new FolderPick { Kind = FolderPick.Label, Shown = group.Key.Length == 0 ? "Classes" : group.Key });
            foreach (var (name, _, dot, count) in found)
                Rows.Add(new FolderPick { Kind = FolderPick.Folder, Name = name, Shown = ClassNames.Display(name), Dot = dot, Count = count, Current = name == current });
        }
        if (Rows.Count > 0) Rows.Add(new FolderPick { Kind = FolderPick.Rule });
        // A name that's typed and isn't a folder yet can be made one in the same press.
        bool exists = folders.Any(f => string.Equals(f.Name, q, StringComparison.OrdinalIgnoreCase) || string.Equals(ClassNames.Display(f.Name), q, StringComparison.OrdinalIgnoreCase));
        Rows.Add(new FolderPick { Kind = FolderPick.New, Shown = q.Length > 0 && !exists ? $"New folder “{q}”" : "New folder" });
        // Typing puts the keys on the first folder found, so Return files it there.
        if (q.Length > 0 && Rows.FirstOrDefault(r => r.IsRow) is { } first) first.Active = true;
    }

    List<FolderPick> Pickable => [.. Rows.Where(r => r.IsRow)];

    void Step(int by)
    {
        var rows = Pickable;
        if (rows.Count == 0) return;
        int at = rows.FindIndex(r => r.Active);
        int next = at < 0 ? (by > 0 ? 0 : rows.Count - 1) : (at + by + rows.Count) % rows.Count;
        foreach (var r in rows) r.Active = false;
        rows[next].Active = true;
    }

    [RelayCommand] void Down() => Step(1);
    [RelayCommand] void Up() => Step(-1);

    /// <summary>Return: the row the keys are on, or with none the first one found.</summary>
    [RelayCommand]
    void Enter()
    {
        var rows = Pickable;
        if ((rows.FirstOrDefault(r => r.Active) ?? (Query.Trim().Length > 0 ? rows.FirstOrDefault() : null)) is { } row) Pick(row);
    }

    [RelayCommand]
    void Pick(FolderPick row)
    {
        if (!row.IsRow) return;
        if (row.IsNew)
        {
            string name = Query.Trim();
            bool exists = folders.Any(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
            if (name.Length == 0 || (exists && !Naming))
            {
                // Nothing typed yet (or what's typed is a folder already): the field takes the new one's name.
                Query = "";
                Naming = true;
                return;
            }
            if (exists) return;
            OnClose?.Invoke();
            OnNew?.Invoke(name);
            return;
        }
        OnClose?.Invoke();
        if (!row.Current) OnPick?.Invoke(row.Name);
    }

    /// <summary>Esc: out of naming a new folder first, then out of the menu.</summary>
    [RelayCommand]
    void Escape()
    {
        if (Naming)
        {
            Query = "";
            Naming = false;
        }
        else OnClose?.Invoke();
    }
}
