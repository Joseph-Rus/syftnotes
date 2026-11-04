using StudyStash.App.ViewModels;

namespace StudyStash.App;

/// <summary>The shell's attachments: the open note's files, a list that talks to the library over its API (the same
/// whether the library is this computer or another one). A folder's own files are its page's (Shell.Overview.cs).</summary>
public static partial class Shell
{
    /// <summary>The open lecture's attachments, fresh for each lecture; none when no lecture is open.</summary>
    static void ShowLectureFiles(NoteModel? note)
    {
        if (note is null || host.Remote() is not { } lib)
        {
            library.LectureFiles = null;
            Controls.NoteView.PhotoSource = null;
            return;
        }
        // A slide's card takes the page to its moment in the transcript, and the transcript shows the slides above their lines.
        var files = new AttachmentsModel(lib, note.ClassName, note.Id, note.ShowAt, note.PinSlides)
        {
            // Once the library is writing them again, the lecture shows that it is.
            Rewriting = () => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (pageState.Lecture == note.Id) Run(() => ShowLectureAsync(note.Id), "ShowLecture");
            }),
        };
        library.LectureFiles = files;
        Controls.NoteView.PhotoSource = files.PhotoFileAsync;
        Run(() => files.LoadAsync(), "files.Load");
    }
}
