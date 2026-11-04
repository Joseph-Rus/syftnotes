using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using StudyStash.App.ViewModels;

namespace StudyStash.App.Views;

/// <summary>A page that adds a lecture from a recording dropped on it: <see cref="Add"/> opens the form for the file,
/// <see cref="Hover"/> says a recording is over the page (its name) or isn't any more (null).</summary>
public sealed record RecordingDrop(Action<string> Add, Action<string?> Hover);

/// <summary>Choosing files to attach, and taking files dropped on a lecture or a class: the same for the Mac and
/// Windows views, which only say where.</summary>
public static class AttachFiles
{
    /// <summary>The system's file picker, several files at once: PDFs, pictures (an iPad's handwriting), slides, documents.</summary>
    public static async Task<IReadOnlyList<string>> PickAsync(Control near)
    {
        if (TopLevel.GetTopLevel(near)?.StorageProvider is not { CanOpen: true } storage) return [];
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Attach your notes, slides or a handout",
            AllowMultiple = true,
            FileTypeFilter =
            [
                new FilePickerFileType("Notes, slides and handouts")
                {
                    Patterns = ["*.pdf", "*.png", "*.jpg", "*.jpeg", "*.heic", "*.pptx", "*.ppt", "*.key", "*.docx", "*.doc", "*.txt", "*.md"],
                    AppleUniformTypeIdentifiers = ["com.adobe.pdf", "public.image", "org.openxmlformats.presentationml.presentation",
                        "com.microsoft.powerpoint.ppt", "com.apple.keynote.key", "org.openxmlformats.wordprocessingml.document", "public.plain-text"],
                    MimeTypes = ["application/pdf", "image/*"],
                },
                FilePickerFileTypes.All,
            ],
        });
        return [.. files.Select(f => f.TryGetLocalPath()).OfType<string>()];
    }

    /// <summary>The system's file picker for one recording or lecture video (a voice memo, a Zoom recording).</summary>
    public static async Task<string?> PickRecordingAsync(Control near)
    {
        if (TopLevel.GetTopLevel(near)?.StorageProvider is not { CanOpen: true } storage) return null;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Pick a recording or a video of the lecture",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Recordings and lecture videos")
                {
                    Patterns = [.. RecordingFile.Endings.Select(e => "*" + e)],
                    AppleUniformTypeIdentifiers = ["public.audio", "public.movie"],
                    MimeTypes = ["audio/*", "video/*"],
                },
                FilePickerFileTypes.All,
            ],
        });
        return files.Select(f => f.TryGetLocalPath()).OfType<string>().FirstOrDefault();
    }

    /// <summary>Files dragged over <paramref name="target"/> are taken by the list <paramref name="model"/> gives
    /// (the lecture's, or the class's); nothing happens while there's no list, or for something that isn't a file. A page
    /// that also adds lectures from recordings gives <paramref name="recording"/>: one recording or video dropped on it
    /// opens that form (and is said to, while it's dragged over); anything else is attached as before.</summary>
    public static void AcceptDrops(Control target, Func<AttachmentsModel?> model, Func<RecordingDrop?>? recording = null)
    {
        DragDrop.SetAllowDrop(target, true);
        target.AddHandler(DragDrop.DragOverEvent, (_, e) =>
        {
            var drop = recording?.Invoke();
            if (drop is not null && OneRecording(e) is { } named)
            {
                drop.Hover(named.Name);
                e.DragEffects = DragDropEffects.Copy;
                return;
            }
            drop?.Hover(null);
            e.DragEffects = model() is not null && e.DataTransfer.TryGetFiles() is { Length: > 0 } ? DragDropEffects.Copy : DragDropEffects.None;
        });
        target.AddHandler(DragDrop.DragLeaveEvent, (_, _) => recording?.Invoke()?.Hover(null));
        target.AddHandler(DragDrop.DropEvent, async (_, e) =>
        {
            var drop = recording?.Invoke();
            drop?.Hover(null);
            if (drop is not null && OneRecording(e) is { } one)
            {
                drop.Add(one.Path);
                return;
            }
            if (model() is not { } list || e.DataTransfer.TryGetFiles() is not { Length: > 0 } items) return;
            var paths = items.Select(i => i.TryGetLocalPath()).OfType<string>().Where(File.Exists).ToList();
            if (paths.Count > 0) await list.AddAsync(paths);
        });
    }

    /// <summary>The one file being dragged, when it's a recording or a video (a bundle of files isn't one).</summary>
    static (string Name, string Path)? OneRecording(DragEventArgs e) =>
        e.DataTransfer.TryGetFiles() is { Length: 1 } items && items[0].TryGetLocalPath() is { } path && RecordingFile.Is(path) ? (items[0].Name, path) : null;

    /// <summary>A view's Attach button opens the picker next to it.</summary>
    public static void Wire(Control view)
    {
        view.DataContextChanged += (_, _) =>
        {
            if (view.DataContext is AttachmentsModel m) m.PickFiles ??= () => PickAsync(view);
        };
    }
}
