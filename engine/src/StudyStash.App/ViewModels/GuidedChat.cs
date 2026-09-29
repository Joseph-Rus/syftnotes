using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StudyStash.Core.Setup;

namespace StudyStash.App.ViewModels;

/// <summary>One thing in guided setup's chat: the AI's words, the student's, a quiet note from Study Stash, or a
/// card the student acts in.</summary>
public abstract class ChatEntry : ObservableObject;

/// <summary>The AI's words (plain text: links in them aren't clickable), with its name above, and chips under them
/// for the setup tools it used ("Checked your setup", "Added BIO 110").</summary>
public sealed partial class AiEntry(string byline) : ChatEntry
{
    public string Byline { get; } = byline;
    [ObservableProperty] public partial string Text { get; set; } = "";
    public ObservableCollection<string> Chips { get; } = [];
    public bool HasChips => Chips.Count > 0;
    internal void Chip(string chip)
    {
        Chips.Add(chip);
        OnPropertyChanged(nameof(HasChips));
    }
}

/// <summary>What the student said. <see cref="Queued"/> while a turn runs: it goes with the next one.</summary>
public sealed partial class StudentEntry(string text) : ChatEntry
{
    public string Text { get; } = text;
    [ObservableProperty] public partial bool Queued { get; set; }
}

/// <summary>A quiet line from Study Stash: "Microphone allowed · Study Stash hears you". <see cref="Good"/> shows a
/// check, else a warning.</summary>
public sealed class NoteEntry(string text, bool good) : ChatEntry
{
    public string Text { get; } = text;
    public bool Good { get; } = good;
    public bool Warn => !Good;
}

/// <summary>
/// A card the AI opened in the chat (<see cref="SetupCard"/>): the student acts in it, and only its buttons change
/// anything. One is open at a time; an older one folds to its outcome line.
/// </summary>
public sealed partial class CardEntry : ChatEntry
{
    readonly GuidedSetupModel owner;

    public CardEntry(GuidedSetupModel owner, SetupCard card)
    {
        this.owner = owner;
        Kind = card.Kind;
        Arg = card.Arg;
        ModelId = card.Kind == "model_download" ? card.Arg : "";
        Choice = card.Kind == "computer_setup" ? card.Arg switch
        {
            "laptop" => "laptop",
            "library" => "library",
            _ => "one",
        } : "";
    }

    public string Kind { get; }
    /// <summary>What the AI gave it: a model's id, the school's Canvas address, a suggested choice.</summary>
    public string Arg { get; }
    public GuidedSetupModel Guided => owner;
    public SetupModel Setup => owner.Setup;

    [ObservableProperty] public partial bool Open { get; set; } = true;
    /// <summary>What came of it, in a few words ("Just this Mac · your library is ready"): shown once it folds.</summary>
    [ObservableProperty] public partial string Outcome { get; set; } = "";
    /// <summary>Its button is doing its work (connecting, adding the classes): the next card waits.</summary>
    [ObservableProperty] public partial bool Busy { get; set; }
    /// <summary>A problem to show in the card ("That password isn't right.").</summary>
    [ObservableProperty] public partial string? Problem { get; set; }
    /// <summary>The computer card's choice: "one", "laptop" or "library".</summary>
    [ObservableProperty] public partial string Choice { get; set; }

    public bool Folded => !Open;
    public bool HasOutcome => Outcome.Length > 0;
    public bool HasProblem => !string.IsNullOrEmpty(Problem);
    public bool IsComputer => Kind == "computer_setup";
    public bool IsPassword => Kind == "library_password";
    public bool IsConnection => Kind == "library_connection";
    public bool IsMicrophone => Kind == "microphone_check";
    public bool IsModel => Kind == "model_download";
    public bool IsChrome => Kind == "chrome_helper";
    public bool IsCourses => Kind == "course_picker";
    public bool IsStartAtLogin => Kind == "start_at_login";
    public bool IsTaskbar => Kind == "taskbar_tip";
    public bool IsFinish => Kind == "finish";
    public bool ChoseOne => Choice == "one";
    public bool ChoseLaptop => Choice == "laptop";
    public bool ChoseLibrary => Choice == "library";

    /// <summary>The model card's model: the one the AI offered, or the one the student changed it to.</summary>
    [ObservableProperty] public partial string ModelId { get; set; }
    public Services.ModelChoice? Model => Setup.Models.FirstOrDefault(m => m.Model.Id == ModelId);
    public string ModelTitle => Model is { } m ? $"Download {m.Name}?" : "Download the transcription model?";
    public string ModelBody => Model is { } m
        ? $"{m.Size}, once. It turns speech into text on this {Setup.DeviceWord}, so recordings never leave it."
        : "";

    /// <summary>The finish card's recap: one line an item.</summary>
    public IReadOnlyList<string> Recap => owner.Recap();

    partial void OnOpenChanged(bool value) => OnPropertyChanged(nameof(Folded));

    partial void OnModelIdChanged(string value)
    {
        OnPropertyChanged(nameof(Model));
        OnPropertyChanged(nameof(ModelTitle));
        OnPropertyChanged(nameof(ModelBody));
    }
    partial void OnOutcomeChanged(string value) => OnPropertyChanged(nameof(HasOutcome));
    partial void OnProblemChanged(string? value) => OnPropertyChanged(nameof(HasProblem));

    partial void OnChoiceChanged(string value)
    {
        OnPropertyChanged(nameof(ChoseOne));
        OnPropertyChanged(nameof(ChoseLaptop));
        OnPropertyChanged(nameof(ChoseLibrary));
    }

    /// <summary>One of the card's buttons ("setup", "download", "notnow"…): the only way a card changes anything.</summary>
    [RelayCommand]
    Task Press(string action) => owner.PressAsync(this, action);

    [RelayCommand]
    void Choose(string choice)
    {
        if (!owner.Setup.Again) Choice = choice;
    }
}

/// <summary>One checklist line as the sidebar shows it.</summary>
public sealed partial class ChecklistRow(string id) : ObservableObject
{
    public string Id { get; } = id;
    [ObservableProperty] public partial string Title { get; set; } = "";
    [ObservableProperty] public partial string Detail { get; set; } = "";
    [ObservableProperty] public partial ChecklistState State { get; set; }
    [ObservableProperty] public partial bool Optional { get; set; }
    [ObservableProperty] public partial bool Recommended { get; set; }
    /// <summary>The student can click it to ask the AI to go there (anything not done yet, but the AI's own line).</summary>
    public bool Clickable => Id != "ai" && State != ChecklistState.Done;
    public bool IsDone => State == ChecklistState.Done;
    public bool IsNow => State == ChecklistState.Now;
    public bool IsTodo => State == ChecklistState.Todo;
    public bool IsSkipped => State == ChecklistState.Skipped;
    public bool IsProblem => State == ChecklistState.Problem;
    public bool HasDetail => Detail.Length > 0;
    public bool ShowOptional => Optional && !IsDone;
    public bool ShowRecommended => Recommended && !IsDone && !IsSkipped;
    public string Tag => ShowOptional ? "Optional" : ShowRecommended ? "Recommended" : "";
    public bool HasTag => Tag.Length > 0;

    partial void OnStateChanged(ChecklistState value)
    {
        foreach (string p in new[] { nameof(Clickable), nameof(IsDone), nameof(IsNow), nameof(IsTodo), nameof(IsSkipped), nameof(IsProblem), nameof(ShowOptional),
                     nameof(ShowRecommended), nameof(Tag), nameof(HasTag) })
            OnPropertyChanged(p);
    }

    partial void OnDetailChanged(string value) => OnPropertyChanged(nameof(HasDetail));

    partial void OnOptionalChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowOptional));
        OnPropertyChanged(nameof(Tag));
        OnPropertyChanged(nameof(HasTag));
    }

    partial void OnRecommendedChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowRecommended));
        OnPropertyChanged(nameof(Tag));
        OnPropertyChanged(nameof(HasTag));
    }
}
