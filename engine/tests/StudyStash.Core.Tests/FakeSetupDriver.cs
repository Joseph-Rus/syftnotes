using StudyStash.Core.Setup;

namespace StudyStash.Core.Tests;

/// <summary>A setup window that only writes down what the setup tools asked of it. <see cref="MachineChanges"/>
/// counts anything that would change the computer: the tools must never cause one.</summary>
public sealed class FakeSetupDriver : ISetupDriver
{
    public SetupStatus Status { get; set; } = new()
    {
        Role = "one",
        Items =
        [
            new ChecklistItem("ai", "Claude is ready", ChecklistState.Done),
            new ChecklistItem("computer", "Just this Mac", ChecklistState.Done),
            new ChecklistItem("microphone", "Microphone", ChecklistState.Todo),
            new ChecklistItem("model", "Transcription model", ChecklistState.Todo, "Not downloaded yet"),
            new ChecklistItem("notes", "Notes", ChecklistState.Done, "Written by Claude"),
            new ChecklistItem("classes", "Classes", ChecklistState.Todo, Optional: true),
            new ChecklistItem("canvas", "Canvas", ChecklistState.Todo, Optional: true),
            new ChecklistItem("start_at_login", "Start at login", ChecklistState.Todo, Recommended: true),
        ],
    };

    public List<SetupModelOption> Models { get; } =
    [
        new("large-v3-turbo-q5", "Whisper large-v3 turbo (compact)", "574 MB", false, true, "Fast on this Mac and good enough to start"),
        new("large-v3", "Whisper large-v3", "3 GB", false, false),
    ];

    public SetupCourses Courses { get; set; } = new(false, [], "Chrome isn't connected yet.");
    public List<SetupCard> Cards { get; } = [];
    public List<(string Question, IReadOnlyList<string> Choices)> Questions { get; } = [];
    public List<(string Name, string About)> Classes { get; } = [];
    public List<string> Writers { get; } = [];
    public List<string> Skipped { get; } = [];
    public List<string> Manual { get; } = [];
    public int MachineChanges { get; private set; }
    /// <summary>What the next <see cref="OfferAsync"/> says no with (a Canvas address that doesn't answer).</summary>
    public string? RefuseCard { get; set; }
    public string? RefuseWriter { get; set; }

    public Task<SetupStatus> StatusAsync() => Task.FromResult(Status);
    public Task<IReadOnlyList<SetupModelOption>> ModelsAsync() => Task.FromResult<IReadOnlyList<SetupModelOption>>(Models);
    public Task<SetupCourses> CoursesAsync() => Task.FromResult(Courses);

    public Task AskAsync(string question, IReadOnlyList<string> choices)
    {
        Questions.Add((question, choices));
        return Task.CompletedTask;
    }

    public Task<string?> OfferAsync(SetupCard card)
    {
        if (RefuseCard is { } no) return Task.FromResult<string?>(no);
        Cards.Add(card);
        return Task.FromResult<string?>(null);
    }

    public Task<string?> AddClassAsync(string name, string about)
    {
        Classes.Add((name, about));
        return Task.FromResult<string?>(null);
    }

    public Task<string?> SetNotesWriterAsync(string engine)
    {
        if (RefuseWriter is { } no) return Task.FromResult<string?>(no);
        Writers.Add(engine);
        return Task.FromResult<string?>(null);
    }

    public Task<string?> SkipAsync(string step)
    {
        Skipped.Add(step);
        return Task.FromResult<string?>(null);
    }

    public Task OpenManualAsync(string step)
    {
        Manual.Add(step);
        return Task.CompletedTask;
    }

    /// <summary>What a card's button would do: the tools never get here.</summary>
    public void ChangeMachine() => MachineChanges++;
}
