using StudyStash.App.ViewModels.Kit;

namespace StudyStash.App.ViewModels;

/// <summary>The first look round the library window, on Home: shown once after a first run (or the first start of an
/// update that never had it), and again from Settings → General's Show me around. Each target is a control's Name in
/// MacLibrary/WinLibrary; one that isn't on screen is left out.</summary>
public static class HomeTour
{
    public static readonly IReadOnlyList<TourStep> Steps =
    [
        new("Record", "Record a lecture",
            "Records the class and writes down what's said, on this computer.",
            "Press it when class starts and press Stop when it ends. Your notes follow."),
        new("AddRecording", FromFileWords.AddButton,
            "Brings in a lecture you recorded before, or a video of one.",
            "Pick the file, and Syft Notes writes it down and makes notes from it."),
        new("NoteRows", "Your lectures",
            "Every lecture lands here, newest first.",
            "Open one to read its notes. Share is at the top right of its page."),
        new("AskBar", "Ask about your notes",
            "Your AI answers from your own lectures and shows where it found it.",
            "Type a question here and send it."),
        new("StudyRow", "Study",
            "Turns your notes into review cards and says which ones are due.",
            "Open it when you have a few minutes and go through the cards."),
        new("Settings", "Settings",
            "Your AI, Canvas, microphone and the app's look are here.",
            "Open it any time. You can take this tour again from General."),
    ];
}
