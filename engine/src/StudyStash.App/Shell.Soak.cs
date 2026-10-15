using Avalonia.Controls;
using StudyStash.App.Services;
using StudyStash.App.ViewModels;

namespace StudyStash.App;

// THROWAWAY (memory soak, branch memory-soak-0.16, never merged): doors onto the shell's own navigation, so the soak walks
// the app the way clicks would.
public static partial class Shell
{
    internal static class Soak
    {
        /// <summary>STUDYSTASH_SOAK=&lt;rounds&gt;: the self-test goes on to the memory soak.</summary>
        public static bool On => Environment.GetEnvironmentVariable("STUDYSTASH_SOAK") is { Length: > 0 };
        /// <summary>STUDYSTASH_SOAK_QUIET=1 (on the owner's own Mac): no window takes the keyboard or brings the app
        /// forward, and nothing pictures the screen.</summary>
        public static bool Quiet => On && Environment.GetEnvironmentVariable("STUDYSTASH_SOAK_QUIET") == "1";

        public static Task Home() => ShowHomeAsync();
        public static Task Due() => ShowDuePageAsync();
        public static Task Folder(string name) => ShowFolderAsync(name);
        public static Task Lecture(string id, bool transcript = false) => ShowLectureAsync(id, transcript);
        public static Task Ask(string question) => AskQuick(question);
        public static void Back() => GoBack();
        public static void Forward() => GoForward();
        public static void CloseMain() => mainWindow?.Close();
        public static LibraryModel Library => library;
        public static RecorderModel Recorder => recorder;
        public static Window? BrowserChoice => browserChoiceWindow;
        public static Window? PillCard => pillCard;
        public static void ChooseBrowser(params string[] browsers) =>
            AskWhichBrowser(new CanvasApi.BrowserQuestion { Where = "this_computer", Browsers = browsers }, byHand: true);
        public static void Stop() => StopRecording();
        /// <summary>Record, as Home's or a folder's own button does: its page opens.</summary>
        public static Task<bool> Record(string cls) => StartRecordingAsync(cls, null, openPage: true);
        public static string? LiveId => liveId;

        /// <summary>A window comes to the front, unless the soak runs on someone's own screen.</summary>
        public static void Front(Window? w)
        {
            if (!Quiet) w?.Activate();
        }
    }
}
