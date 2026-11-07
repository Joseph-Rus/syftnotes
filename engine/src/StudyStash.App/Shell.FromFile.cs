using StudyStash.App.ViewModels;
using StudyStash.App.Views;
using StudyStash.App.Windows;

namespace StudyStash.App;

/// <summary>A lecture that already happened: a recording or a lecture video on this computer (picked with Add a
/// recording or Record's menu, or dropped on Home or a folder's page) goes through its small form, and is then written
/// down here and sent on like any recording.</summary>
public static partial class Shell
{
    const string FromFileKey = "from-file";

    /// <summary>The form for <paramref name="path"/>, started on <paramref name="cls"/>'s page ("" on Home, where the
    /// library sorts it). A file that's gone by now says so instead.</summary>
    static void AddFromFile(string path, string cls)
    {
        if (RecordingFile.Read(path) is not { } file)
        {
            Toast("That file isn't there any more", "It may have been moved or renamed. Pick it again where it is now.", null, null);
            return;
        }
        string named = FromFileModel.TitleFrom(file.Name);
        Avalonia.Controls.Window? shown = null;
        var form = new FromFileModel(file, cls, [.. host.Classes().Select(c => (c.Name, c.Color))],
            // A title left as the file's name lets the library name the lecture from what was said in it.
            async (lecture, ct) => await host.AddLectureFromFileAsync(lecture.Path, lecture.Class, lecture.Title == named ? null : lecture.Title, lecture.Day, ct),
            close: () => shown?.Close());
        shown = AppWindows.Open(new WindowSpec(FromFileKey, FromFileWords.WindowTitle, new FromFilePage { DataContext = form }));
    }
}
