using StudyStash.App.Services;
using StudyStash.Core.Ai;
using StudyStash.Core.Setup;

namespace StudyStash.App.ViewModels;

/// <summary>What guided setup knows beyond <see cref="SetupModel"/>: which AI, whether it's ready, what the student
/// chose or left for later, and what's under way. Only real state goes in: nothing the AI says.</summary>
public sealed record ChecklistFacts
{
    /// <summary>"claude" or "codex".</summary>
    public string Ai { get; init; } = "claude";
    /// <summary>Signed in, and the plan check passed.</summary>
    public bool AiReady { get; init; }
    public bool Windows { get; init; }
    /// <summary>The student pressed Set up on the computer card (or setup is run again, or the library's already
    /// made or reached).</summary>
    public bool RoleChosen { get; init; }
    /// <summary>Who writes the notes: "claude", "codex", "ollama", "none", or "" not set yet.</summary>
    public string NotesWriter { get; init; } = "";
    public IReadOnlySet<string> Skipped { get; init; } = new HashSet<string>();
    public bool TaskbarDone { get; init; }
    /// <summary>Start at login was turned on from its card.</summary>
    public bool StartsAtLogin { get; init; }
    /// <summary>How far the model's download has got, while it downloads.</summary>
    public double? Downloading { get; init; }
    /// <summary>The card showing now ("microphone_check"…), or "".</summary>
    public string OpenCard { get; init; } = "";
    /// <summary>The student's browser is connected to the library (the Canvas helper checked in).</summary>
    public bool BrowserConnected { get; init; }
    /// <summary>The browser the helper is being added to ("Edge"); "" before the helper's card has been offered.</summary>
    public string Browser { get; init; } = "";
    /// <summary>How many courses Canvas found.</summary>
    public int CoursesFound { get; init; }
}

/// <summary>
/// Guided setup's checklist, worked out from real state alone (<see cref="From"/> is pure): which items this computer
/// needs, and which are done, under way, left for later or in trouble. The AI can only read it.
/// </summary>
public static class SetupChecklist
{
    /// <summary>The checklist for <paramref name="m"/> as it is now.</summary>
    public static IReadOnlyList<ChecklistItem> From(SetupModel m, ChecklistFacts f)
    {
        string device = m.DeviceWord;
        bool chosen = f.RoleChosen || m.Again || m.LibraryOk;
        bool records = m.IsOneComputer || m.IsLaptop;
        var items = new List<ChecklistItem>
        {
            new("ai", $"{AgentCli.Get(f.Ai).Brand} is ready", f.AiReady ? ChecklistState.Done : ChecklistState.Todo),
        };

        string title = !chosen ? "How you'll use it" : m.IsLaptop ? "This is my laptop" : m.IsLibrary ? "This is my library" : $"Just this {device}";
        bool computerDone = chosen && (m.IsLibrary || m.LibraryOk);
        string computerDetail = !chosen ? "" : m.IsLaptop
            ? m.LibraryOk ? (m.LibraryResult ?? "").TrimEnd('.') : m.Connecting ? "Connecting…" : "Not connected yet"
            : m.IsOneComputer && m.Connecting ? "Making your library…" : "";
        items.Add(new("computer", title, computerDone ? ChecklistState.Done : Now(f, "computer_setup", "library_connection") || m.Connecting ? ChecklistState.Now : ChecklistState.Todo,
            computerDetail));

        if (chosen && m.IsLibrary)
            items.Add(new("library_password", "Library password", m.LibraryOk ? ChecklistState.Done : Now(f, "library_password") || m.Connecting ? ChecklistState.Now : ChecklistState.Todo));

        if (records)
        {
            var mic = m.MicAllowed && m.MicHeard ? ChecklistState.Done
                : m.ShowMicProblem ? ChecklistState.Problem
                : Now(f, "microphone_check") ? ChecklistState.Now : ChecklistState.Todo;
            string micDetail = mic switch
            {
                ChecklistState.Problem => m.MicTrouble?.Title ?? (f.Windows ? "Blocked in Settings" : "Blocked in System Settings"),
                ChecklistState.Now when m.MicAllowed => "Allowed · say something",
                _ => "",
            };
            items.Add(new("microphone", "Microphone", mic, micDetail));

            var model = m.ModelReady ? ChecklistState.Done
                : m.HasModelProblem && f.Downloading is null ? ChecklistState.Problem
                : f.Downloading is not null ? ChecklistState.Now : ChecklistState.Todo;
            string modelDetail = model switch
            {
                ChecklistState.Done => m.ModelName,
                ChecklistState.Problem => "Not downloaded yet",
                _ when f.Downloading is { } d => $"Downloading · {Math.Clamp((int)Math.Round(d * 100), 0, 99)}%",
                _ => "Not downloaded yet",
            };
            items.Add(new("model", "Transcription model", model, modelDetail));
        }

        if (m.IsOneComputer || m.IsLibrary)
        {
            string writer = f.NotesWriter switch
            {
                "" => "",
                "none" => "Transcripts only for now",
                "ollama" => "Written by Ollama",
                var e => $"Written by {AgentCli.Get(e).Brand}",
            };
            items.Add(new("notes", "Notes", f.NotesWriter.Length > 0 ? ChecklistState.Done : ChecklistState.Todo, writer));
        }

        int classes = m.Classes.Count;
        items.Add(new("classes", "Classes", classes > 0 ? ChecklistState.Done : Left(f, "classes") ?? ChecklistState.Todo,
            classes > 0 ? $"{classes} class{(classes == 1 ? "" : "es")}" : "", Optional: true));

        var canvas = f.BrowserConnected && f.CoursesFound > 0 ? ChecklistState.Done
            : Left(f, "canvas") ?? (Now(f, "chrome_helper", "course_picker") ? ChecklistState.Now : ChecklistState.Todo);
        string canvasDetail = canvas == ChecklistState.Done ? $"{f.CoursesFound} course{(f.CoursesFound == 1 ? "" : "s")} found"
            : canvas == ChecklistState.Now && !f.BrowserConnected ? $"Waiting for {Core.Canvas.CanvasSettings.BrowserName(f.Browser)}…" : "";
        items.Add(new("canvas", "Canvas", canvas, canvasDetail, Optional: true));

        if (m.IsOneComputer || m.IsLibrary)
            items.Add(new("start_at_login", "Start at login", f.StartsAtLogin ? ChecklistState.Done
                : Left(f, "start_at_login") ?? (Now(f, "start_at_login") ? ChecklistState.Now : ChecklistState.Todo), Recommended: true));

        if (f.Windows && records)
            items.Add(new("taskbar", "Taskbar", f.TaskbarDone ? ChecklistState.Done : Left(f, "taskbar") ?? (Now(f, "taskbar_tip") ? ChecklistState.Now : ChecklistState.Todo),
                Optional: true));
        return items;
    }

    static bool Now(ChecklistFacts f, params string[] cards) => cards.Contains(f.OpenCard);

    static ChecklistState? Left(ChecklistFacts f, string id) => f.Skipped.Contains(id) ? ChecklistState.Skipped : null;

    /// <summary>Every item that isn't optional is done: left for later counts (start at login's "Not now"), and so
    /// does the model while it downloads (the only time it's under way).</summary>
    public static bool ReadyToFinish(IReadOnlyList<ChecklistItem> items) =>
        items.All(i => i.Optional || i.State is ChecklistState.Done or ChecklistState.Skipped || i.Id == "model" && i.State == ChecklistState.Now);

    /// <summary>The page of setup by hand to open for an item (or, for none, the first not done).</summary>
    public static SetupStep StepFor(SetupModel m, IReadOnlyList<ChecklistItem> items, string id = "")
    {
        string pick = id.Length > 0 ? id : items.FirstOrDefault(i => !i.Optional && i.State is not (ChecklistState.Done or ChecklistState.Skipped))?.Id ?? "";
        var step = pick switch
        {
            "computer" => m.IsLaptop && m.Steps.Any(s => s.Step == SetupStep.Library) && (m.Again || m.LibraryOk || m.Connecting) ? SetupStep.Library : SetupStep.Welcome,
            "library_password" => SetupStep.Password,
            "microphone" => SetupStep.Microphone,
            "model" => SetupStep.Model,
            "notes" => SetupStep.Ai,
            "classes" => SetupStep.Classes,
            "canvas" => SetupStep.Canvas,
            "start_at_login" => SetupStep.StartAtLogin,
            "taskbar" => SetupStep.Taskbar,
            "" => SetupStep.Done,
            _ => SetupStep.Welcome,
        };
        return m.Steps.Any(s => s.Step == step) ? step : m.Steps[0].Step;
    }
}
