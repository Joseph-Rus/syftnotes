using StudyStash.App.ViewModels;
using StudyStash.App.Views;
using StudyStash.App.Windows;

namespace StudyStash.App;

/// <summary>A lecture that already happened: a recording or a lecture video on this computer (picked with Add a
/// recording, or dropped on Home or a folder's page) goes through its small form, and is then written
/// down here and sent on like any recording.</summary>
public static partial class Shell
{
    const string FromFileKey = "from-file";

    /// <summary>The form that's open, if one is: one at a time.</summary>
    static (FromFileModel Form, Avalonia.Controls.Window Window)? fromFile;

    /// <summary>The form for <paramref name="path"/>, started on <paramref name="cls"/>'s page ("" on Home, where the
    /// library sorts it). A file that's gone by now says so instead.</summary>
    static void AddFromFile(string path, string cls)
    {
        if (Core.SyftFile.Is(path))
        {
            AddShared(path, cls);
            return;
        }
        if (RecordingFile.Read(path) is not { } file)
        {
            Toast("That file isn't there any more", "It may have been moved or renamed. Pick it again where it is now.", null, null);
            return;
        }
        if (fromFile is { } open)
        {
            // Still getting the last one ready: that one finishes first. Otherwise the new file takes its place.
            if (open.Form.Add.Busy)
            {
                AppWindows.Raise(open.Window);
                Toast("One recording at a time", "The last one is still getting ready. Add this one when it's done.", null, null);
                return;
            }
            open.Window.Close();
        }
        string named = FromFileModel.TitleFrom(file.Name);
        Avalonia.Controls.Window? shown = null;
        var form = new FromFileModel(file, cls, [.. host.Classes().Select(c => (c.Name, c.Color))],
            // A title left as the file's name lets the library name the lecture from what was said in it.
            async (lecture, ct) => await host.AddLectureFromFileAsync(lecture.Path, lecture.Class, lecture.Title == named ? null : lecture.Title, lecture.Day, ct),
            close: () => shown?.Close());
        shown = AppWindows.Open(new WindowSpec(FromFileKey, FromFileWords.WindowTitle, new FromFilePage { DataContext = form }), w =>
            w.Closed += (_, _) =>
            {
                // Closed while a file was still being got ready: it isn't added, and nothing is left of it.
                form.Add.Cancel();
                if (fromFile is { } f && ReferenceEquals(f.Form, form)) fromFile = null;
            });
        fromFile = (form, shown);
    }

    /// <summary>A lecture a friend shared (a .syft file: dropped, picked with Add a recording, or opened from Finder or
    /// Explorer), after the one question of which class. A file that isn't one says why, in plain words.</summary>
    static void AddShared(string path, string cls)
    {
        if (!host.Settings.SetupDone)
        {
            ShowSetup();
            return;
        }
        Core.SharedLecture lecture;
        try
        {
            lecture = Core.SyftFile.Read(path);
        }
        catch (Core.SyftFileException e)
        {
            Toast(SharedWords.CantOpen, e.Message, null, null);
            return;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Program.Log($"[shared] couldn't read {Path.GetFileName(path)}: {e.GetType().Name}: {e.Message}");
            Toast(SharedWords.CantOpen, SharedWords.CantRead, null, null);
            return;
        }
        Avalonia.Controls.Window? shown = null;
        var form = new SharedLectureModel(lecture, cls, [.. host.Classes().Select(c => (c.Name, c.Color))], async (className, ct) =>
        {
            var added = await host.AddSharedLectureAsync(lecture, className, ct);
            if (added.Value is { } it)
            {
                Toast(SharedWords.Added(lecture.Title), SharedWords.AddedTo(it.ClassName), SharedWords.Open, () => OpenLecture(it.Id));
                RequestLibraryReload();
            }
            return added;
        }, close: () => shown?.Close());
        // One at a time: a second file takes the place of the first.
        AppWindows.Find(SharedKey)?.Close();
        shown = AppWindows.Open(new WindowSpec(SharedKey, SharedWords.WindowTitle, new SharedLecturePage { DataContext = form }), w =>
            w.Closed += (_, _) => form.Add.Cancel());
    }

    const string SharedKey = "shared-lecture";
}
