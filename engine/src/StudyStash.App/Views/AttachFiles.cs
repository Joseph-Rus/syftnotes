using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using StudyStash.App.ViewModels;

namespace StudyStash.App.Views;

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

    /// <summary>Files dragged over <paramref name="target"/> are taken by the list <paramref name="model"/> gives
    /// (the lecture's, or the class's); nothing happens while there's no list, or for something that isn't a file.</summary>
    public static void AcceptDrops(Control target, Func<AttachmentsModel?> model)
    {
        DragDrop.SetAllowDrop(target, true);
        target.AddHandler(DragDrop.DragOverEvent, (_, e) =>
        {
            e.DragEffects = model() is not null && e.DataTransfer.TryGetFiles() is { Length: > 0 } ? DragDropEffects.Copy : DragDropEffects.None;
        });
        target.AddHandler(DragDrop.DropEvent, async (_, e) =>
        {
            if (model() is not { } list || e.DataTransfer.TryGetFiles() is not { Length: > 0 } items) return;
            var paths = items.Select(i => i.TryGetLocalPath()).OfType<string>().Where(File.Exists).ToList();
            if (paths.Count > 0) await list.AddAsync(paths);
        });
    }

    /// <summary>A view's Attach button opens the picker next to it.</summary>
    public static void Wire(Control view)
    {
        view.DataContextChanged += (_, _) =>
        {
            if (view.DataContext is AttachmentsModel m) m.PickFiles ??= () => PickAsync(view);
        };
    }
}
