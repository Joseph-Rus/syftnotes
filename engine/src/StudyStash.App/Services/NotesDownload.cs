using System.Text;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using StudyStash.Core;
using StudyStash.Core.Rich;

namespace StudyStash.App.Services;

/// <summary>
/// Saves a lecture, or a whole class, as Markdown a student can keep and open in Obsidian, Typora or VS Code: through
/// the native Save and folder dialogs, its diagrams drawn as pictures beside the file (or, with nowhere on disk to
/// put them, folded straight into the Markdown). The same code runs on a laptop and on the library computer, since
/// it only ever talks to the library through the <see cref="RemoteLibrary"/> it's given. The dialogs are behind a
/// thin picker so a test can hand the save a path of its own instead of showing one.
/// </summary>
public static class NotesDownload
{
    static string S(JsonNode? n) => n is JsonValue v && v.TryGetValue(out string? s) ? s ?? "" : "";

    /// <summary>One lecture, chosen with the Save dialog.</summary>
    public static Task LectureAsync(Window window, RemoteLibrary lib, string lectureId, string suggestedName, bool transcript, Func<string, string?> mermaidSvg) =>
        LectureAsync(async () =>
        {
            var file = await window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Download as Markdown", SuggestedFileName = suggestedName, DefaultExtension = "md",
                FileTypeChoices = [new FilePickerFileType("Markdown") { Patterns = ["*.md"] }],
            });
            return file is null ? null : ((Func<string, Task>)(text => WriteTextAsync(file, text)), file.TryGetLocalPath());
        }, lib, lectureId, transcript, mermaidSvg);

    /// <summary>The same save, with <paramref name="pick"/> standing in for the Save dialog: null cancels it (as
    /// picking Cancel would), otherwise how to write the chosen file's text and its local path, if it has one (a
    /// sandboxed picker's file may not).</summary>
    internal static async Task LectureAsync(Func<Task<(Func<string, Task> WriteText, string? LocalPath)?>> pick, RemoteLibrary lib, string lectureId,
        bool transcript, Func<string, string?> mermaidSvg)
    {
        if (await pick() is not { } chosen) return;
        var (writeText, localPath) = chosen;
        JsonObject? lecture;
        try
        {
            lecture = await lib.LectureAsync(lectureId);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or LibraryRefusedException)
        {
            Shell.Toast("Couldn't download it", e.Message, null, null);
            return;
        }
        if (lecture is null) return;
        string? dir = localPath is { Length: > 0 } ? Path.GetDirectoryName(localPath) : null;
        // Away from the window: drawing a big flowchart's picture takes a moment.
        var export = await Task.Run(() => NoteExport.Lecture(lecture, transcript, mermaidSvg, dir is not null ? Path.GetFileName(localPath) : null));
        await writeText(dir is not null ? export.Markdown : Embedded(export));
        if (dir is not null)
            foreach (var asset in export.Assets)
                await WriteAssetAsync(dir, asset);
    }

    /// <summary>Every lecture of a class, into a folder of its own chosen with the folder dialog.</summary>
    public static Task ClassAsync(Window window, RemoteLibrary lib, string className, bool transcript, Func<string, string?> mermaidSvg) =>
        ClassAsync(async () =>
        {
            var folders = await window.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = $"Choose where to save {className}" });
            return folders.Count == 0 ? null : folders[0].TryGetLocalPath();
        }, lib, className, transcript, mermaidSvg);

    /// <summary>The same save, with <paramref name="pick"/> standing in for the folder dialog: null cancels it, or
    /// the local folder chosen (a folder that isn't really on this computer counts as none).</summary>
    internal static async Task ClassAsync(Func<Task<string?>> pick, RemoteLibrary lib, string className, bool transcript, Func<string, string?> mermaidSvg)
    {
        string? chosen = await pick();
        if (chosen is not { Length: > 0 })
        {
            if (chosen is not null) Shell.Toast("Couldn't download it", "Choose a folder on this computer.", null, null);
            return;
        }
        string classDir = UniqueClassDir(chosen, className);

        JsonArray list;
        try
        {
            list = await lib.LecturesAsync(className, 500, null);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or LibraryRefusedException)
        {
            Shell.Toast("Couldn't download it", e.Message, null, null);
            return;
        }
        Directory.CreateDirectory(classDir);

        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int saved = 0, failed = 0;
        foreach (var row in list.OfType<JsonObject>())
        {
            JsonObject? lecture;
            try
            {
                lecture = await lib.LectureAsync(S(row["id"]));
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or LibraryRefusedException)
            {
                lecture = null;
            }
            if (lecture is null) { failed++; continue; }
            // Numbered first, so a lecture's diagrams go in a folder named after its own file: two "Lecture 3"s on one
            // day, or two titles that read the same once a file name can hold them, never share (and overwrite) one.
            string name = NoteExport.UniqueName(NoteExport.FileName(lecture), taken);
            var export = await Task.Run(() => NoteExport.Lecture(lecture, transcript, mermaidSvg, name));
            await File.WriteAllTextAsync(Path.Combine(classDir, export.FileName), export.Markdown, Encoding.UTF8);
            foreach (var asset in export.Assets) await WriteAssetAsync(classDir, asset);
            saved++;
        }

        if (saved == 0 && failed > 0)
        {
            Shell.Toast("Couldn't download it", "Your library couldn't be reached.", null, null);
            return;
        }
        string title = $"Saved {saved} lecture{(saved == 1 ? "" : "s")}" + (failed > 0 ? $" of {saved + failed}" : "");
        string text = failed == 0 ? $"In {className}"
            : failed == 1 ? "One couldn't be read from your library." : $"{failed} couldn't be read from your library.";
        Shell.Toast(title, text, "Show", () => Machine.Open(classDir));
    }

    /// <summary>A class's own folder under <paramref name="parent"/>: its name, or "Name (2)" when a folder of that
    /// name is already there and has something in it (an empty one, left from a run that found nothing, is reused).</summary>
    static string UniqueClassDir(string parent, string className)
    {
        string baseName = Notes.Slugify(className, 100);
        for (int n = 1; ; n++)
        {
            string path = Path.Combine(parent, n == 1 ? baseName : $"{baseName} ({n})");
            if (!Directory.Exists(path) || !Directory.EnumerateFileSystemEntries(path).Any()) return path;
        }
    }

    static async Task WriteTextAsync(IStorageFile file, string text)
    {
        await using var stream = await file.OpenWriteAsync();
        await using var w = new StreamWriter(stream);
        await w.WriteAsync(text);
    }

    static async Task WriteAssetAsync(string baseDir, ExportAsset asset)
    {
        string path = Path.Combine(baseDir, asset.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, asset.Text, Encoding.UTF8);
    }

    /// <summary>A single-file save with nowhere on disk for its pictures: every image link to a saved asset becomes
    /// the picture itself, inline.</summary>
    static string Embedded(ExportFile export)
    {
        string md = export.Markdown;
        foreach (var asset in export.Assets)
        {
            string link = $"]({NoteExport.EncodePath(asset.RelativePath)})";
            string data = $"](data:image/svg+xml;base64,{Convert.ToBase64String(Encoding.UTF8.GetBytes(asset.Text))})";
            md = md.Replace(link, data, StringComparison.Ordinal);
        }
        return md;
    }
}
