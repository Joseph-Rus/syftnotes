namespace StudyStash.App.ViewModels;

/// <summary>Submit &amp; Organize's words: Settings → Canvas's "Tidy up after you hand in" and "Filed work", the checklist
/// that opens after a hand-in, and its notification. The Trash is the Recycle Bin on Windows.</summary>
public static class FilingWords
{
    static bool Win => OperatingSystem.IsWindows();
    static string Trash => Win ? "Recycle Bin" : "Trash";

    // Settings
    public const string Heading = "After you hand in";
    public const string SwitchTitle = "Tidy up after you hand in";
    public const string SwitchSub = "Files what you handed in on Canvas in its class's folder, with the files you worked from.";
    public const string FoldersTitle = "Folders to look in";
    public const string FoldersSub = "Syft Notes looks only in these, and only moves what you say.";
    public const string NoFolders = "None yet. Add the folders you save your work in.";
    public const string AddFolder = "Add a folder…";
    public const string Remove = "Remove";
    public const string WhenTitle = "When";
    public const string AskMe = "Ask me";
    public const string Automatically = "Automatically, after 12 hours";
    public const string MaterialsTitle = "The files you worked from";
    public const string KeepTogether = "Keep them together";
    public static string ToTrash => $"Move them to the {Trash}";
    public const string KeepTogetherSub = "In a Materials folder beside what you handed in.";
    public const string ToTrashSub = "Only the ones you tick. Undo in Filed work brings them back.";
    public const string FiledWork = "Filed work";
    public const string NothingFiled = "Nothing filed yet.";
    public const string Undo = "Undo";
    public const string PutBack = "Put back";
    public const string LibraryOffline = "Your library didn't answer. Try again in a moment.";

    /// <summary>"Add Downloads": a folder offered first.</summary>
    public static string AddSuggested(string name) => $"Add {name}";

    /// <summary>"CS 101 · Today, 10:42 AM · 3 files".</summary>
    public static string RunLine(string cls, string when, int files) => $"{cls} · {when} · {CanvasWords.FileCountText(files)}";

    // The checklist
    public static string Title(string assignment) => $"You handed in {assignment}";
    public static string Where(string cls, string assignment) => $"{cls} / {assignment}";
    public static string Intro(string cls, string assignment) => $"Syft Notes found these in your folders. They go in {Where(cls, assignment)}. Nothing moves until you say.";
    public const string Looking = "Looking in your folders…";
    public const string HandedInLabel = "What you handed in";
    public const string IsThisIt = "Is this the file you handed in?";
    public const string Yes = "Yes";
    public const string ChooseAnother = "Choose another";
    public const string ChooseTheFile = "Choose the file";
    public static string NotFound(string name) => $"Syft Notes didn't find {name} in your folders.";
    public const string MaterialsLabel = "Files you worked from";
    public static string MaterialsLabelClean => $"Files you worked from: the ticked ones go to the {Trash}";
    public const string NoMaterials = "Nothing else looked like it was for this assignment.";
    public const string FileIt = "File it";
    public const string FileItAndClear = "File it and clear the rest";
    public const string NotNow = "Not now";
    public static string PickTitle(string assignment) => $"Choose the file you handed in for {assignment}";
    public static string OutsideFolders(IReadOnlyList<string> names) =>
        $"Choose a file in one of your folders to look in: {CanvasWords.JoinAnd(names)}.";

    // The notification
    public static string FiledTitle(string cls, string assignment) => $"Filed in {Where(cls, assignment)}";
    public static string FiledText(int filed, int left) =>
        left == 0 ? $"{CanvasWords.FileCountText(filed)} filed." : $"{CanvasWords.FileCountText(filed)} filed. {CanvasWords.FileCountText(left)} stayed where {(left == 1 ? "it was" : "they were")}.";
    public const string PutBackTitle = "Put back";

    /// <summary>What Undo did, one sentence per outcome: "2 files put back where they were. 1 is in the Trash: restore it
    /// from the Trash." <paramref name="how"/> is each file's outcome as the library says it.</summary>
    public static string Undone(IEnumerable<string> how)
    {
        var counts = how.GroupBy(h => h).ToDictionary(g => g.Key, g => g.Count());
        int N(string k) => counts.GetValueOrDefault(k);
        var said = new List<string>();
        if (N("back") > 0) said.Add($"{CanvasWords.FileCountText(N("back"))} put back where {(N("back") == 1 ? "it was" : "they were")}.");
        if (N("beside") > 0) said.Add($"{CanvasWords.FileCountText(N("beside"))} put back beside a file now in {(N("beside") == 1 ? "its" : "their")} place.");
        if (N("in_trash") > 0) said.Add($"{(N("in_trash") == 1 ? "1 is" : $"{N("in_trash")} are")} in the {Trash}: restore {(N("in_trash") == 1 ? "it" : "them")} from the {Trash}.");
        if (N("missing") > 0) said.Add($"{(N("missing") == 1 ? "1 isn't" : $"{N("missing")} aren't")} where {(N("missing") == 1 ? "it was" : "they were")} filed any more.");
        if (N("failed") > 0) said.Add($"{(N("failed") == 1 ? "1 couldn't" : $"{N("failed")} couldn't")} be put back ({(N("failed") == 1 ? "it" : "they")} may be open). Try Undo again.");
        return said.Count == 0 ? "Everything was already back." : string.Join(" ", said);
    }
}
