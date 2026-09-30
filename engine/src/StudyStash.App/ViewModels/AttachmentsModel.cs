using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StudyStash.Core;

namespace StudyStash.App.ViewModels;

/// <summary>Every word the attachments list says.</summary>
public static class AttachmentWords
{
    public const string Heading = "Attachments";
    public const string Attach = "Attach";
    public const string AttachTip = "Attach your notes, slides or a handout (or drop files here)";
    public const string Reading = "Reading your handwriting…";
    public const string Attaching = "Attaching…";
    public const string Rewrite = "Rewrite notes with your attachments";
    public const string RewriteWhy = "Your notes were written before some of these came.";
    public const string Rewriting = "Rewriting your notes with your attachments. They show here when they're done.";
    public const string NoWords = "No words to read in it";
    public const string OlderLibrary = "Your library runs an older Study Stash: update it to attach files.";
    public const string Unreachable = "Can't reach your library right now.";

    /// <summary>"Slides · 2.4 MB", "The student's own notes · 830 KB · Reading your handwriting…" (said as "Your notes").</summary>
    public static string About(string name, string type, long size, bool reading, bool hasText)
    {
        string kind = Attachments.KindOf(name, type) is var k && k == Attachments.OwnNotes ? "Your notes" : k;
        string state = reading ? Reading : hasText ? "" : NoWords;
        return string.Join(" · ", new[] { kind, Attachments.SizeLabel(size), state }.Where(s => s.Length > 0));
    }

    /// <summary>The icon a file shows: a slide deck, a picture, or a page.</summary>
    public static string Icon(string name, string type) =>
        Attachments.KindOf(name, type) == Attachments.Slides ? "slideshow" : type.StartsWith("image/", StringComparison.Ordinal) ? "image" : "description";
}

/// <summary>One file in the list: its name, what it is and how big, and whether its words are still being read.</summary>
public sealed partial class AttachmentItem : ObservableObject
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Type { get; init; } = "";
    public string Icon { get; init; } = "description";
    [ObservableProperty] public partial string About { get; set; } = "";
    [ObservableProperty] public partial bool Reading { get; set; }
    [ObservableProperty] public partial bool Opening { get; set; }
}

/// <summary>
/// What's attached to a lecture (or a class): the files, an Attach button and a place to drop files, "Reading your
/// handwriting…" while the library reads them, and for a lecture whose notes came first, "Rewrite notes with your
/// attachments". It talks to the library over its API, whether that's this computer or another one.
/// </summary>
public sealed partial class AttachmentsModel(IAttachmentLibrary library, string? className, string? lectureId) : ObservableObject, IDisposable
{
    readonly CancellationTokenSource stop = new();

    public string? ClassName { get; } = className;
    public string? LectureId { get; } = lectureId;
    public ObservableCollection<AttachmentItem> Items { get; } = [];

    [ObservableProperty] public partial bool Reading { get; set; }
    [ObservableProperty] public partial bool Busy { get; set; }
    [ObservableProperty] public partial bool OfferRewrite { get; set; }
    [ObservableProperty] public partial string? Problem { get; set; }
    [ObservableProperty] public partial string? Said { get; set; }

    public bool HasItems => Items.Count > 0;
    /// <summary>The "Attachments" row with its Attach button, once something's attached; until then the page's own
    /// Attach (beside a lecture's Notes and Transcript, beside a class's name) is the way in.</summary>
    public bool ShowHeader => HasItems;
    public bool HasProblem => !string.IsNullOrEmpty(Problem);
    public bool HasSaid => !string.IsNullOrEmpty(Said);

    /// <summary>How often the list looks again while something is being read.</summary>
    public TimeSpan PollEvery { get; set; } = TimeSpan.FromSeconds(3);
    /// <summary>The file picker (the view sets it): the paths chosen, none when cancelled.</summary>
    public Func<Task<IReadOnlyList<string>>>? PickFiles { get; set; }
    /// <summary>Opens a file with its own app.</summary>
    public Action<string> OpenFile { get; set; } = Machine.Open;
    /// <summary>Where a file opened from the library is copied to first.</summary>
    public string CacheDir { get; set; } = Path.Combine(Path.GetTempPath(), "Study Stash attachments");
    /// <summary>The notes are being written again: the host reloads the lecture when they're done.</summary>
    public Action? Rewriting { get; set; }

    partial void OnProblemChanged(string? value) => OnPropertyChanged(nameof(HasProblem));
    partial void OnSaidChanged(string? value) => OnPropertyChanged(nameof(HasSaid));

    /// <summary>Lists what's attached; while anything is being read, looks again every few seconds until it's done.</summary>
    public async Task LoadAsync()
    {
        await RefreshAsync();
        while (Reading && !stop.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(PollEvery, stop.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            await RefreshAsync();
        }
    }

    async Task RefreshAsync()
    {
        JsonObject? list;
        try
        {
            list = await library.AttachmentListAsync(LectureId is null ? ClassName : null, LectureId, stop.Token);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or LibraryRefusedException)
        {
            if (!stop.IsCancellationRequested) Problem = AttachmentWords.Unreachable;
            Reading = false;
            return;
        }
        if (list is null)
        {
            Problem = AttachmentWords.OlderLibrary;
            Reading = false;
            return;
        }
        Problem = null;
        Show(list["attachments"] as JsonArray ?? []);
        OfferRewrite = list["rewrite"] is JsonValue r && r.TryGetValue(out bool offer) && offer;
    }

    static string S(JsonNode? n) => n is JsonValue v && v.TryGetValue(out string? s) ? s ?? "" : "";
    static bool B(JsonNode? n) => n is JsonValue v && v.TryGetValue(out bool b) && b;

    void Show(JsonArray list)
    {
        Items.Clear();
        foreach (var a in list.OfType<JsonObject>())
        {
            string name = S(a["name"]), type = S(a["type"]);
            long size = a["size"] is JsonValue sv ? sv.TryGetValue(out long bytes) ? bytes : sv.TryGetValue(out int small) ? small : 0 : 0;
            Items.Add(new AttachmentItem
            {
                Id = S(a["id"]), Name = name, Type = type, Icon = AttachmentWords.Icon(name, type), Reading = B(a["reading"]),
                About = AttachmentWords.About(name, type, size, B(a["reading"]), B(a["hasText"])),
            });
        }
        Reading = Items.Any(i => i.Reading);
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(ShowHeader));
    }

    [RelayCommand]
    async Task Attach()
    {
        if (PickFiles is null) return;
        var paths = await PickFiles();
        if (paths.Count > 0) await AddAsync(paths);
    }

    /// <summary>Sends files to the library (chosen, or dropped on the lecture or class), then lists them with the rest.</summary>
    public async Task AddAsync(IReadOnlyList<string> paths)
    {
        var files = paths.Where(File.Exists).ToList();
        if (files.Count == 0 || Busy) return;
        Busy = true;
        Problem = null;
        string? problem = null;
        try
        {
            await library.AttachAsync(files, ClassName, LectureId, stop.Token);
        }
        catch (LibraryRefusedException e)
        {
            problem = e.Message;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException)
        {
            problem = stop.IsCancellationRequested ? null : e is IOException or UnauthorizedAccessException ? $"Couldn't read that file: {e.Message}" : AttachmentWords.Unreachable;
        }
        finally
        {
            Busy = false;
        }
        if (problem is not null)
        {
            Problem = problem; // what went wrong stays said; the list is as it was
            return;
        }
        if (!stop.IsCancellationRequested) await LoadAsync();
    }

    /// <summary>Opens a file: copied from the library, then opened with its own app.</summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    async Task Open(AttachmentItem item)
    {
        string path = Path.Combine(CacheDir, item.Id, Attachments.SafeName(item.Name));
        item.Opening = true;
        try
        {
            if (!File.Exists(path)) await library.DownloadAttachmentAsync(item.Id, path, stop.Token);
            OpenFile(path);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or LibraryRefusedException or IOException or UnauthorizedAccessException)
        {
            Problem = e is LibraryRefusedException ? e.Message : $"Couldn't open {item.Name}.";
        }
        finally
        {
            item.Opening = false;
        }
    }

    /// <summary>Removes a file from the library (it leaves the list at once).</summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    async Task Remove(AttachmentItem item)
    {
        Items.Remove(item);
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(ShowHeader));
        try
        {
            await library.RemoveAttachmentAsync(item.Id);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or LibraryRefusedException)
        {
            Problem = $"Couldn't remove {item.Name}.";
            await RefreshAsync();
        }
    }

    /// <summary>"Rewrite notes with your attachments": the library writes the lecture's notes again, with them.</summary>
    [RelayCommand]
    async Task Rewrite()
    {
        if (LectureId is null) return;
        OfferRewrite = false;
        try
        {
            await library.RewriteAsync(LectureId);
            Said = AttachmentWords.Rewriting;
            Rewriting?.Invoke();
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or LibraryRefusedException)
        {
            OfferRewrite = true;
            Problem = AttachmentWords.Unreachable;
        }
    }

    /// <summary>The lecture or class was left: its list stops looking again, and nothing it was doing lands anywhere.</summary>
    public void Dispose() => stop.Cancel();
}
