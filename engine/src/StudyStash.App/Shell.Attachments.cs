using StudyStash.App.ViewModels;

namespace StudyStash.App;

/// <summary>The shell's attachments: the open lecture's files and the class's, each a list that talks to the library
/// over its API (the same whether the library is this computer or another one).</summary>
public static partial class Shell
{
    /// <summary>The open lecture's attachments, fresh for each lecture; none when no lecture is open.</summary>
    static void ShowLectureFiles(NoteModel? note)
    {
        if (note is null || host.Remote() is not { } lib)
        {
            library.LectureFiles = null;
            return;
        }
        var files = new AttachmentsModel(lib, note.ClassName, note.Id)
        {
            // Once the library is writing them again, the lecture shows that it is.
            Rewriting = () => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (openLecture == note.Id) _ = ShowLectureAsync(note.Id);
            }),
        };
        library.LectureFiles = files;
        _ = files.LoadAsync();
    }

    /// <summary>The class's attachments (its own, and its lectures'); none on Due.</summary>
    static void ShowClassFiles(string? className)
    {
        if (className is null || host.Remote() is not { } lib)
        {
            library.ClassFiles = null;
            return;
        }
        var files = new AttachmentsModel(lib, className, null);
        library.ClassFiles = files;
        _ = files.LoadAsync();
    }
}
