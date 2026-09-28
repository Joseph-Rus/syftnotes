using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json.Nodes;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StudyStash.Core;

namespace StudyStash.App.ViewModels;

/// <summary>A class as the library keeps it: its name, the other names it goes by (comma separated as typed), what
/// it covers (which helps the AI sort), and the folder its lectures are in.</summary>
public sealed partial class LibraryClassRow : ObservableObject
{
    [ObservableProperty] public partial string Name { get; set; } = "";
    [ObservableProperty] public partial string Aliases { get; set; } = "";
    [ObservableProperty] public partial string Description { get; set; } = "";
    public string Folder { get; init; } = "";
    public int Lectures { get; init; }
    public IBrush Dot { get; init; } = Brushes.Gray;
    /// <summary>"12 lectures · …/Lecture notes/CS 101", under the class.</summary>
    public string Meta => (Lectures == 1 ? "1 lecture" : $"{Lectures} lectures") + (Folder.Length > 0 ? " · " + Folder : "");
}

/// <summary>A class "Use Canvas course names" would rename: from its name now to its Canvas course's name.</summary>
public sealed record CourseNameRow(string From, string To, int Lectures)
{
    /// <summary>"Now 202710.TS.CSCI321.A", under the new name.</summary>
    public string Now => $"Now {From}";
    /// <summary>"12 lectures move with it".</summary>
    public string Meta => (Lectures == 1 ? "1 lecture" : $"{Lectures} lectures") + " move with it";
}

/// <summary>A folder the library may read: search finds files in it; the AI reads them unless it's private.</summary>
public sealed partial class ReadFolderRow : ObservableObject
{
    public required string Name { get; init; }
    public required string Path { get; init; }
    [ObservableProperty] public partial bool Ai { get; set; }
    [ObservableProperty] public partial bool Private { get; set; }
    /// <summary>Set by the model: toggling either switch saves the list.</summary>
    internal Action? Changed { get; set; }

    partial void OnAiChanged(bool value) => Changed?.Invoke();

    partial void OnPrivateChanged(bool value) => Changed?.Invoke();
}

/// <summary>One item in a settings select (a model, a terminal, a confidence): what's shown, and picking it.</summary>
public sealed class SettingChoice(string id, string label, Action<string> pick)
{
    public string Id { get; } = id;
    public string Label { get; } = label;
    public IRelayCommand Pick { get; } = new RelayCommand(() => pick(id));
}

/// <summary>Whether the library's settings can be shown: still asking, here, no library yet, can't reach it, or it
/// runs an older Study Stash without these routes.</summary>
public enum LibrarySettingsState
{
    Loading,
    Ready,
    NoLibrary,
    Unreachable,
    Older,
}

/// <summary>
/// Settings → Your library: everything the library's own Settings page holds, read and changed through the library's
/// API with its password, so a laptop changes its library from here (and the library's own computer too). The
/// library: its name, password, how laptops reach it, starting at login on its computer, the notes folder and updates.
/// Classes: names, other names and what each covers. Notes and sorting: writing notes, sorting with AI, the confidence
/// to file, the local models, the terminal "Open in…" uses, rewriting every summary. Folders it may read. Each change
/// is saved as it's made; a refused one goes back and says why.
/// </summary>
public sealed partial class LibrarySettingsModel : ObservableObject
{
    /// <summary>A call to the library's settings routes (GET, a change, or an action): the answer, or null for a library
    /// older than them. Throws <see cref="LibraryRefusedException"/> or <see cref="HttpRequestException"/>.</summary>
    public delegate Task<JsonObject?> Call(HttpMethod method, string path, JsonObject? body);

    readonly Func<Call?> connect;
    /// <summary>True while a loaded answer fills the fields: nothing is sent back.</summary>
    bool filling;

    static readonly double[] Confidences = [0.4, 0.5, 0.6, 0.7, 0.8, 0.9];

    /// <summary>Over the library <paramref name="connect"/> reaches (null: no library set up yet).</summary>
    public LibrarySettingsModel(Func<Call?> connect)
    {
        this.connect = connect;
        ConfidenceChoices = [.. Confidences.Select(c => new SettingChoice(c.ToString(CultureInfo.InvariantCulture), $"{c * 100:0}% sure", PickConfidence))];
    }

    /// <summary>The library is on this computer: its folder can be shown in Finder or Explorer.</summary>
    public bool IsHere { get; init; }
    /// <summary>The library's name changed here: the app keeps its own copy in step.</summary>
    public Action<string>? Renamed { get; set; }
    /// <summary>The library's password changed here: the app connects with the new one from now on.</summary>
    public Action<string>? PasswordChanged { get; set; }
    /// <summary>After the library's classes changed here (one added, renamed or removed), so the app's other windows
    /// show them too.</summary>
    public Action? ClassesChanged { get; set; }
    public Action<string>? Reveal { get; set; }
    public Action<string>? Copy { get; set; }
    /// <summary>The library's own web page (a fallback for a library too old for these settings).</summary>
    public Action? OpenPage { get; set; }

    [ObservableProperty] public partial LibrarySettingsState State { get; set; } = LibrarySettingsState.Loading;
    /// <summary>What happened with the last change, or why it didn't: under the page's title.</summary>
    [ObservableProperty] public partial string? Say { get; set; }
    public bool IsReady => State == LibrarySettingsState.Ready;
    public bool IsLoading => State == LibrarySettingsState.Loading;
    public bool IsNoLibrary => State == LibrarySettingsState.NoLibrary;
    public bool ShowProblem => State is LibrarySettingsState.Unreachable or LibrarySettingsState.Older or LibrarySettingsState.NoLibrary;
    public bool CanRetry => State == LibrarySettingsState.Unreachable;
    public bool CanOpenPage => State == LibrarySettingsState.Older;
    public string Problem => State switch
    {
        LibrarySettingsState.NoLibrary => "There's no library yet. Connect to one in Connection, and its settings show here.",
        LibrarySettingsState.Unreachable => "Can't reach your library right now. Is its computer on, and Tailscale connected?",
        LibrarySettingsState.Older => "Your library runs an older Study Stash. Update it to change its settings here; until then its own page has them.",
        _ => "",
    };

    // The library
    [ObservableProperty] public partial string Name { get; set; } = "";
    [ObservableProperty] public partial bool HasPassword { get; set; }
    [ObservableProperty] public partial bool ChangingPassword { get; set; }
    [ObservableProperty] public partial string NewPassword { get; set; } = "";
    [ObservableProperty] public partial string? PasswordSay { get; set; }
    public ObservableCollection<string> Addresses { get; } = [];
    [ObservableProperty] public partial bool Tailscale { get; set; }
    /// <summary>Null: the library runs without the app on its computer, so this can't be changed from here.</summary>
    [ObservableProperty] public partial bool? StartsAtLogin { get; set; }
    [ObservableProperty] public partial string NotesFolder { get; set; } = "";
    [ObservableProperty] public partial string Version { get; set; } = "";
    [ObservableProperty] public partial string UpdateLine { get; set; } = "";
    [ObservableProperty] public partial bool CanUpdateNow { get; set; }
    [ObservableProperty] public partial bool AutoUpdate { get; set; }

    public string PasswordLine => HasPassword ? "Set. Your laptop connects with it." : "None: anyone who can reach the library can read it.";
    public string ReachLine => Tailscale
        ? "Your laptop reaches it at any of these, at home or away (Tailscale)."
        : "Tailscale isn't running on the library's computer, so a laptop reaches it only on the same Wi-Fi.";
    public bool CanStartAtLogin => StartsAtLogin is not null;
    public bool StartAtLoginOn
    {
        get => StartsAtLogin == true;
        set
        {
            if (StartsAtLogin is null || StartsAtLogin == value) return;
            StartsAtLogin = value;
            _ = SendAsync(new JsonObject { ["start_at_login"] = value }, "Start at login");
        }
    }
    public string StartAtLoginTitle => IsHere ? $"Start the library when this {Device} starts" : "Start when the library's computer starts";
    public string StartAtLoginSub => StartsAtLogin is null
        ? "The library runs without the Study Stash app there, so this is set on that computer."
        : "So your laptop can always reach it. Off until you turn it on.";
    static string Device => OperatingSystem.IsWindows() ? "PC" : "Mac";
    public static string RevealLabel => OperatingSystem.IsWindows() ? "Show in File Explorer" : "Show in Finder";

    // Classes
    public ObservableCollection<LibraryClassRow> Classes { get; } = [];
    [ObservableProperty] public partial string NewClass { get; set; } = "";
    /// <summary>The classes named from Canvas course codes, each with its course's name ("Use Canvas course names").</summary>
    public ObservableCollection<CourseNameRow> CourseNames { get; } = [];
    /// <summary>Why they can't be renamed right now ("Canvas is syncing…"), or null.</summary>
    [ObservableProperty] public partial string? CourseNamesBlocked { get; set; }
    /// <summary>The preview is open: the list, and Cancel / Rename.</summary>
    [ObservableProperty] public partial bool ConfirmingCourseNames { get; set; }
    [ObservableProperty] public partial bool RenamingClasses { get; set; }
    /// <summary>The library renamed classes (old → new): this computer's waiting lectures follow.</summary>
    public Action<IReadOnlyList<(string From, string To)>>? ClassesRenamed { get; set; }
    public bool HasCourseNames => CourseNames.Count > 0;
    public bool CanUseCourseNames => HasCourseNames && CourseNamesBlocked is null && !RenamingClasses;
    public string CourseNamesSub => CourseNamesBlocked ?? (CourseNames.Count == 1
        ? "1 class is named from its course code. Its lectures move with it."
        : $"{CourseNames.Count} classes are named from course codes. Their lectures move with them.");
    public string RenameLabel => CourseNames.Count == 1 ? "Rename 1 class" : $"Rename {CourseNames.Count} classes";

    // Notes and sorting
    [ObservableProperty] public partial bool WriteNotes { get; set; }
    [ObservableProperty] public partial bool SortWithAi { get; set; }
    [ObservableProperty] public partial double Confidence { get; set; } = 0.6;
    [ObservableProperty] public partial string Writer { get; set; } = "";
    [ObservableProperty] public partial bool OllamaAnswering { get; set; }
    [ObservableProperty] public partial string SummaryModel { get; set; } = "";
    [ObservableProperty] public partial string SortModel { get; set; } = "";
    [ObservableProperty] public partial string Recommended { get; set; } = "";
    [ObservableProperty] public partial IReadOnlyList<SettingChoice> SummaryChoices { get; set; } = [];
    [ObservableProperty] public partial IReadOnlyList<SettingChoice> SortChoices { get; set; } = [];
    [ObservableProperty] public partial IReadOnlyList<SettingChoice> TerminalChoices { get; set; } = [];
    /// <summary>Which AI sorts lectures into classes: its id, or "" for the library's main AI.</summary>
    [ObservableProperty] public partial string SortEngine { get; set; } = "";
    [ObservableProperty] public partial IReadOnlyList<SettingChoice> SortEngineChoices { get; set; } = [];
    [ObservableProperty] public partial string Terminal { get; set; } = "";
    [ObservableProperty] public partial int Lectures { get; set; }
    [ObservableProperty] public partial bool ConfirmingRewrite { get; set; }
    public IReadOnlyList<SettingChoice> ConfidenceChoices { get; }

    public string WriteNotesSub => Writer.Length > 0 ? $"With {Writer}, after each lecture" : "After each lecture";
    public string ConfidenceLabel => $"{Math.Round(Confidence * 100).ToString(CultureInfo.InvariantCulture)}% sure";
    public string SummaryLabel => SummaryModel.Length > 0 ? SummaryModel : "Same as the sorting model";
    public string SortLabel => SortModel.Length > 0 ? SortModel : "None";
    public string TerminalLabel => TerminalChoices.FirstOrDefault(c => c.Id == Terminal)?.Label ?? Terminal;
    public string SortEngineLabel => SortEngineChoices.FirstOrDefault(c => c.Id == SortEngine)?.Label ?? SortEngine;
    public bool HasTerminals => TerminalChoices.Count > 0;
    public string OllamaLine => OllamaAnswering
        ? "Ollama runs on the library's computer; notes and sorting stay there and cost nothing."
        : $"Ollama isn't answering on the library's computer, so local notes and sorting wait. Open the Ollama app there (the model to get: {Recommended}).";
    public string RewriteTitle => Lectures == 1 ? "Rewrite the summary" : $"Rewrite all {Lectures} summaries";
    public string RewriteSub => Writer.Length > 0 ? $"With {Writer}, one at a time. Lectures stay readable meanwhile." : "One at a time. Lectures stay readable meanwhile.";

    // Folders it may read
    public ObservableCollection<ReadFolderRow> Folders { get; } = [];
    [ObservableProperty] public partial string NewFolder { get; set; } = "";
    [ObservableProperty] public partial int Files { get; set; }
    public string FilesLine => $"Search finds files in these folders, and the AI can read them when you chat. A private folder is searched by file name only and never shown to the AI. {Files} file{(Files == 1 ? " is" : "s are")} searchable now.";
    public bool NoFolders => Folders.Count == 0;

    // What changes show together.
    partial void OnStateChanged(LibrarySettingsState value)
    {
        foreach (string p in new[] { nameof(IsReady), nameof(IsLoading), nameof(IsNoLibrary), nameof(ShowProblem), nameof(CanRetry), nameof(CanOpenPage), nameof(Problem) }) OnPropertyChanged(p);
    }

    partial void OnHasPasswordChanged(bool value) => OnPropertyChanged(nameof(PasswordLine));

    partial void OnTailscaleChanged(bool value) => OnPropertyChanged(nameof(ReachLine));

    partial void OnStartsAtLoginChanged(bool? value)
    {
        foreach (string p in new[] { nameof(CanStartAtLogin), nameof(StartAtLoginOn), nameof(StartAtLoginSub) }) OnPropertyChanged(p);
    }

    partial void OnWriterChanged(string value)
    {
        OnPropertyChanged(nameof(WriteNotesSub));
        OnPropertyChanged(nameof(RewriteSub));
    }

    partial void OnConfidenceChanged(double value) => OnPropertyChanged(nameof(ConfidenceLabel));

    partial void OnSummaryModelChanged(string value) => OnPropertyChanged(nameof(SummaryLabel));

    partial void OnSortModelChanged(string value) => OnPropertyChanged(nameof(SortLabel));

    partial void OnTerminalChanged(string value) => OnPropertyChanged(nameof(TerminalLabel));

    partial void OnSortEngineChanged(string value) => OnPropertyChanged(nameof(SortEngineLabel));

    partial void OnSortEngineChoicesChanged(IReadOnlyList<SettingChoice> value) => OnPropertyChanged(nameof(SortEngineLabel));

    partial void OnTerminalChoicesChanged(IReadOnlyList<SettingChoice> value)
    {
        OnPropertyChanged(nameof(TerminalLabel));
        OnPropertyChanged(nameof(HasTerminals));
    }

    partial void OnOllamaAnsweringChanged(bool value) => OnPropertyChanged(nameof(OllamaLine));

    partial void OnRecommendedChanged(string value) => OnPropertyChanged(nameof(OllamaLine));

    partial void OnLecturesChanged(int value) => OnPropertyChanged(nameof(RewriteTitle));

    partial void OnFilesChanged(int value) => OnPropertyChanged(nameof(FilesLine));

    partial void OnWriteNotesChanged(bool value)
    {
        if (!filling) _ = SendAsync(new JsonObject { ["notes"] = new JsonObject { ["write"] = value } }, "Writing notes");
    }

    partial void OnSortWithAiChanged(bool value)
    {
        if (!filling) _ = SendAsync(new JsonObject { ["notes"] = new JsonObject { ["sort"] = value } }, "Sorting");
    }

    partial void OnAutoUpdateChanged(bool value)
    {
        if (!filling) _ = SendAsync(new JsonObject { ["auto_update"] = value }, "Automatic updates");
    }

    // --- reading ---------------------------------------------------------------------------------------------------

    /// <summary>Ask the library for its settings (when a "Your library" page opens, and on Try again).</summary>
    [RelayCommand]
    public async Task Load()
    {
        if (connect() is not { } call)
        {
            State = LibrarySettingsState.NoLibrary;
            return;
        }
        if (State != LibrarySettingsState.Ready) State = LibrarySettingsState.Loading;
        try
        {
            var s = await call(HttpMethod.Get, "", null);
            if (s is null)
            {
                State = LibrarySettingsState.Older;
                return;
            }
            Fill(s);
            State = LibrarySettingsState.Ready;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or LibraryRefusedException or InvalidOperationException)
        {
            State = LibrarySettingsState.Unreachable;
        }
    }

    static string Str(JsonNode? n) => n is JsonValue v && v.TryGetValue(out string? s) ? s : "";

    static bool Flag(JsonNode? n) => n is JsonValue v && v.TryGetValue(out bool b) && b;

    static string Tidy(string aliases) => string.Join(", ", aliases.Split(',').Select(a => a.Trim()).Where(a => a.Length > 0).Distinct());

    static bool Same<T>(IList<T> have, IList<T> got, Func<T, string> key) => have.Count == got.Count && have.Select(key).SequenceEqual(got.Select(key));

    /// <summary>Everything the library said, into the fields (without sending any of it back).</summary>
    void Fill(JsonObject s)
    {
        filling = true;
        try
        {
            Name = Str(s["name"]);
            HasPassword = Flag(s["has_password"]);
            NotesFolder = Str(s["notes_folder"]);
            Lectures = s["lectures"] is JsonValue lv && lv.TryGetValue(out int n) ? n : 0;
            Addresses.Clear();
            foreach (var a in (s["reach"]?["addresses"] as JsonArray ?? []).Select(Str).Where(a => a.Length > 0)) Addresses.Add(a);
            Tailscale = Flag(s["reach"]?["tailscale"]);
            StartsAtLogin = s["start_at_login"] is JsonValue sv && sv.TryGetValue(out bool on) ? on : null;
            var u = s["updates"];
            Version = Str(u?["version"]);
            bool newer = Flag(u?["newer"]);
            CanUpdateNow = newer && Flag(u?["can_update"]);
            UpdateLine = newer ? $"Version {Str(u?["latest"]).TrimStart('v')} is out. The library has {Version}." : $"Study Stash {Version}, the newest.";
            AutoUpdate = Flag(u?["auto"]);

            int i = 0;
            var classes = (s["classes"] as JsonArray ?? []).OfType<JsonObject>().Select(c => new LibraryClassRow
            {
                Name = Str(c["name"]), Description = Str(c["description"]), Folder = Str(c["folder"]),
                Aliases = string.Join(", ", (c["aliases"] as JsonArray ?? []).Select(Str).Where(a => a.Length > 0)),
                Lectures = c["lectures"] is JsonValue cl && cl.TryGetValue(out int k) ? k : 0, Dot = Skin.ClassDot(i++),
            }).ToList();
            // What was just typed comes back as it was sent: keep those rows (and the field being typed in).
            if (!Same(Classes, classes, c => $"{c.Name.Trim()}\n{Tidy(c.Aliases)}\n{c.Description.Trim()}"))
            {
                Classes.Clear();
                foreach (var c in classes) Classes.Add(c);
            }
            else
            {
                // A class just added here gets its folder from the library's answer.
                for (int k = 0; k < Classes.Count; k++)
                    if (Classes[k].Folder.Length == 0 && classes[k].Folder.Length > 0) Classes[k] = classes[k];
            }

            FillCourseNames(s["course_names"]);

            var notes = s["notes"];
            WriteNotes = Flag(notes?["write"]);
            SortWithAi = Flag(notes?["sort"]);
            Confidence = notes?["min_confidence"] is JsonValue mv && mv.TryGetValue(out double m) ? m : 0.6;
            Writer = Str(notes?["writer"]);
            var ollama = s["ollama"];
            OllamaAnswering = Flag(ollama?["answering"]);
            SummaryModel = Str(ollama?["summary_model"]);
            SortModel = Str(ollama?["sort_model"]);
            Recommended = Str(ollama?["recommended"]);
            var models = (ollama?["models"] as JsonArray ?? []).OfType<JsonObject>().Select(o => (Name: Str(o["name"]), Size: Str(o["size"]))).ToList();
            List<SettingChoice> ModelChoices(string current, Action<string> pick)
            {
                var list = models.Select(x => new SettingChoice(x.Name, $"{x.Name} ({x.Size})", pick)).ToList();
                if (current.Length > 0 && models.All(x => x.Name != current)) list.Insert(0, new SettingChoice(current, $"{current} (not installed)", pick));
                return list;
            }
            SummaryChoices = [new SettingChoice("", "Same as the sorting model", PickSummary), .. ModelChoices(SummaryModel, PickSummary)];
            SortChoices = ModelChoices(SortModel, PickSort);
            var sorting = s["sorting"];
            SortEngine = Str(sorting?["engine"]);
            SortEngineChoices =
            [
                new SettingChoice("", $"{Str(sorting?["default"])} (the library's main AI)", PickSortEngine),
                .. (sorting?["engines"] as JsonArray ?? []).OfType<JsonObject>()
                    .Select(e => new SettingChoice(Str(e["id"]), Str(e["name"]) + (Flag(e["installed"]) ? "" : " (not installed)"), PickSortEngine)),
            ];
            var terminal = s["terminal"];
            Terminal = Str(terminal?["current"]);
            TerminalChoices = [.. (terminal?["choices"] as JsonArray ?? []).OfType<JsonObject>().Select(t => new SettingChoice(Str(t["id"]), Str(t["name"]), PickTerminal))];

            var folders = (s["folders"] as JsonArray ?? []).OfType<JsonObject>()
                .Select(f => new ReadFolderRow { Name = Str(f["name"]), Path = Str(f["path"]), Ai = Flag(f["ai"]), Private = Flag(f["private"]), Changed = SaveFolders })
                .ToList();
            if (!Same(Folders, folders, f => $"{f.Path}\n{f.Ai}\n{f.Private}"))
            {
                Folders.Clear();
                foreach (var f in folders) Folders.Add(f);
            }
            Files = s["files"] is JsonValue fv && fv.TryGetValue(out int files) ? files : 0;
            OnPropertyChanged(nameof(NoFolders));
        }
        finally
        {
            filling = false;
        }
    }

    // --- changing ----------------------------------------------------------------------------------------------------

    /// <summary>Sends one change. The library answers with all its settings, which fill the page again; refused or
    /// unreachable, the page goes back to what the library last said, and says why.</summary>
    async Task<bool> SendAsync(JsonObject change, string what)
    {
        if (connect() is not { } call) return false;
        try
        {
            var s = await call(HttpMethod.Post, "", change);
            if (s is null)
            {
                State = LibrarySettingsState.Older;
                return false;
            }
            Fill(s);
            Say = null;
            return true;
        }
        catch (LibraryRefusedException e)
        {
            Say = e.Message.Length > 0 ? e.Message : $"{what} didn't change: the library said no.";
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            Say = $"{what} didn't change: the library didn't answer.";
        }
        await Load();
        return false;
    }

    /// <summary>The name, once it's typed (the field loses focus, or Return).</summary>
    [RelayCommand]
    async Task SaveName()
    {
        string name = Name.Trim();
        if (filling || name.Length == 0) return;
        if (await SendAsync(new JsonObject { ["name"] = name }, "The name")) Renamed?.Invoke(Name);
    }

    [RelayCommand]
    void ChangePassword()
    {
        ChangingPassword = !ChangingPassword;
        NewPassword = "";
        PasswordSay = null;
    }

    [RelayCommand]
    async Task SavePassword()
    {
        string password = NewPassword.Trim();
        if (password.Length < 4)
        {
            PasswordSay = "Use a password of at least 4 characters.";
            return;
        }
        if (connect() is not { } call) return;
        try
        {
            await call(HttpMethod.Post, "/password", new JsonObject { ["password"] = password });
            PasswordChanged?.Invoke(password);
            HasPassword = true;
            ChangingPassword = false;
            NewPassword = "";
            PasswordSay = "Changed. Your other computers need the new password to connect.";
        }
        catch (LibraryRefusedException e)
        {
            PasswordSay = e.Message;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            PasswordSay = "The library didn't answer, so the password is as it was.";
        }
    }

    [RelayCommand]
    void CopyAddress(string address)
    {
        Copy?.Invoke(address);
        Say = $"Copied {address}.";
    }

    [RelayCommand]
    void ShowNotesFolder()
    {
        if (NotesFolder.Length > 0) Reveal?.Invoke(NotesFolder);
    }

    [RelayCommand]
    async Task UpdateNow()
    {
        if (connect() is not { } call) return;
        try
        {
            var r = await call(HttpMethod.Post, "/update", new JsonObject());
            bool updating = r?["updating"] is JsonValue v && v.TryGetValue(out bool b) && b;
            UpdateLine = updating ? "Updating: the library restarts on the new version in about a minute." : "The library couldn't update itself: update it on its computer.";
            CanUpdateNow = false;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or LibraryRefusedException or InvalidOperationException)
        {
            Say = "The library didn't answer, so it hasn't updated.";
        }
    }

    [RelayCommand] void OpenLibraryPage() => OpenPage?.Invoke();

    // Classes: sent whole, once a field is left, a class added or removed.

    JsonObject ClassesChange() => new()
    {
        ["classes"] = new JsonArray(Classes.Select(c => (JsonNode?)new JsonObject
        {
            ["name"] = c.Name.Trim(),
            ["aliases"] = new JsonArray(c.Aliases.Split(',').Select(a => a.Trim()).Where(a => a.Length > 0).Select(a => (JsonNode?)a).ToArray()),
            ["description"] = c.Description.Trim(),
        }).ToArray()),
    };

    [RelayCommand]
    async Task SaveClasses()
    {
        if (!filling && State == LibrarySettingsState.Ready && await SendAsync(ClassesChange(), "The classes")) ClassesChanged?.Invoke();
    }

    [RelayCommand]
    async Task AddClass()
    {
        string name = NewClass.Trim();
        if (name.Length == 0) return;
        Classes.Add(new LibraryClassRow { Name = name, Dot = Skin.ClassDot(Classes.Count) });
        if (!await SendAsync(ClassesChange(), $"{name}")) return;
        NewClass = "";
        ClassesChanged?.Invoke();
    }

    [RelayCommand]
    async Task RemoveClass(LibraryClassRow row)
    {
        Classes.Remove(row);
        if (await SendAsync(ClassesChange(), $"{row.Name}")) ClassesChanged?.Invoke();
    }

    // Use Canvas course names: the preview, then the renames.

    void FillCourseNames(JsonNode? names)
    {
        var rows = (names?["renames"] as JsonArray ?? []).OfType<JsonObject>()
            .Select(r => new CourseNameRow(Str(r["from"]), Str(r["to"]), r["lectures"] is JsonValue v && v.TryGetValue(out int n) ? n : 0))
            .Where(r => r.From.Length > 0 && r.To.Length > 0).ToList();
        CourseNames.Clear();
        foreach (var r in rows) CourseNames.Add(r);
        CourseNamesBlocked = Str(names?["blocked"]) is { Length: > 0 } why ? why : null;
        if (rows.Count == 0) ConfirmingCourseNames = false;
        foreach (string p in new[] { nameof(HasCourseNames), nameof(CanUseCourseNames), nameof(CourseNamesSub), nameof(RenameLabel) }) OnPropertyChanged(p);
    }

    partial void OnCourseNamesBlockedChanged(string? value)
    {
        OnPropertyChanged(nameof(CanUseCourseNames));
        OnPropertyChanged(nameof(CourseNamesSub));
    }

    partial void OnRenamingClassesChanged(bool value) => OnPropertyChanged(nameof(CanUseCourseNames));

    [RelayCommand] void AskUseCourseNames() => ConfirmingCourseNames = true;

    [RelayCommand] void CancelUseCourseNames() => ConfirmingCourseNames = false;

    /// <summary>Rename every class in the preview to its Canvas course name, on the library.</summary>
    [RelayCommand]
    async Task UseCourseNames()
    {
        if (connect() is not { } call || !CanUseCourseNames) return;
        RenamingClasses = true;
        try
        {
            var r = await call(HttpMethod.Post, "/course-names", new JsonObject());
            if (r is null)
            {
                Say = "Your library runs an older Study Stash: update it to use Canvas course names.";
                return;
            }
            var renamed = (r["renamed"] as JsonArray ?? []).OfType<JsonObject>().Select(x => (Str(x["from"]), Str(x["to"]))).ToList();
            int moved = r["lectures"] is JsonValue lv && lv.TryGetValue(out int n) ? n : 0;
            string problem = Str(r["problem"]);
            Say = renamed.Count == 0 ? (problem.Length > 0 ? problem : "Nothing was renamed.")
                : $"Renamed {(renamed.Count == 1 ? "1 class" : $"{renamed.Count} classes")} to {(renamed.Count == 1 ? "its Canvas course name" : "their Canvas course names")}; {(moved == 1 ? "1 lecture" : $"{moved} lectures")} moved with them."
                  + (problem.Length > 0 ? " " + problem : "");
            ConfirmingCourseNames = false;
            if (renamed.Count > 0)
            {
                ClassesRenamed?.Invoke(renamed);
                ClassesChanged?.Invoke();
            }
            string said = Say;
            await Load();
            Say = said;
        }
        catch (LibraryRefusedException e)
        {
            Say = e.Message.Length > 0 ? e.Message : "The classes weren't renamed: the library said no.";
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            Say = "The classes weren't renamed: the library didn't answer.";
        }
        finally
        {
            RenamingClasses = false;
        }
    }

    // Notes and sorting

    void PickSummary(string model)
    {
        SummaryModel = model;
        _ = SendAsync(new JsonObject { ["ollama"] = new JsonObject { ["summary_model"] = model } }, "The notes model");
    }

    void PickSort(string model)
    {
        SortModel = model;
        _ = SendAsync(new JsonObject { ["ollama"] = new JsonObject { ["sort_model"] = model } }, "The sorting model");
    }

    void PickSortEngine(string id)
    {
        SortEngine = id;
        _ = SendAsync(new JsonObject { ["sort_engine"] = id }, "The sorting AI");
    }

    void PickTerminal(string id)
    {
        Terminal = id;
        _ = SendAsync(new JsonObject { ["terminal"] = id }, "The terminal");
    }

    void PickConfidence(string value)
    {
        Confidence = double.Parse(value, CultureInfo.InvariantCulture);
        _ = SendAsync(new JsonObject { ["notes"] = new JsonObject { ["min_confidence"] = Confidence } }, "The confidence");
    }

    [RelayCommand] void AskRewriteAll() => ConfirmingRewrite = true;

    [RelayCommand] void CancelRewriteAll() => ConfirmingRewrite = false;

    [RelayCommand]
    async Task RewriteAll()
    {
        ConfirmingRewrite = false;
        if (connect() is not { } call) return;
        try
        {
            var r = await call(HttpMethod.Post, "/rewrite-all", new JsonObject());
            int n = r?["queued"] is JsonValue v && v.TryGetValue(out int q) ? q : 0;
            Say = n == 1 ? "1 lecture is queued for a new summary." : $"{n} lectures are queued for a new summary.";
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or LibraryRefusedException or InvalidOperationException)
        {
            Say = "The library didn't answer, so nothing was rewritten.";
        }
    }

    // Folders it may read

    JsonObject FoldersChange() => new()
    {
        ["folders"] = new JsonArray(Folders.Select(f => (JsonNode?)new JsonObject { ["path"] = f.Path, ["ai"] = f.Ai, ["private"] = f.Private }).ToArray()),
    };

    void SaveFolders()
    {
        if (!filling) _ = SendAsync(FoldersChange(), "The folders");
    }

    [RelayCommand]
    async Task AddFolder()
    {
        string path = NewFolder.Trim();
        if (path.Length == 0) return;
        var change = FoldersChange();
        change["add_folder"] = path;
        if (await SendAsync(change, "The folder")) NewFolder = "";
    }

    [RelayCommand]
    async Task RemoveFolder(ReadFolderRow row)
    {
        Folders.Remove(row);
        OnPropertyChanged(nameof(NoFolders));
        await SendAsync(FoldersChange(), row.Name);
    }
}
