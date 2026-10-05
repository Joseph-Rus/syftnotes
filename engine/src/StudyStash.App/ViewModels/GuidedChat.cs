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
    public void Chip(string chip)
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

    /// <summary>The model card's list of other models is showing (its Change).</summary>
    [ObservableProperty] public partial bool Changing { get; set; }

    public bool Folded => !Open;
    /// <summary>What the card is about, as its heading (and its line once folded, before anything came of it).</summary>
    public string Title => Kind switch
    {
        "computer_setup" => "How will you use Study Stash?",
        "library_password" => "Name your library and give it a password",
        "library_connection" => "Connect to your library",
        "microphone_check" => "Let Study Stash hear your lectures",
        "model_download" => ModelTitle,
        "chrome_helper" => "Add Study Stash's helper to Chrome",
        "course_picker" => "Pick your courses",
        "start_at_login" => "Start Study Stash when you log in?",
        "taskbar_tip" => "Keep Study Stash on the taskbar",
        _ => "You're set up",
    };
    public string FoldedLine => Outcome.Length > 0 ? Outcome : Title;
    public string JustThis => $"Just this {Setup.DeviceWord}";
    public string PasswordNote => $"Your laptop asks for this password once, to connect. It stays on your computers; {owner.Brand} never sees it.";
    public string StartAtLoginBody => Setup.IsLibrary
        ? "Your library then starts by itself, so your laptop can always reach it and notes get written."
        : "Recommended: Study Stash then starts by itself, so your notes get written and Record is always one click away.";
    public string TaskbarBody => "Windows tucks new icons away under ^. Select ^ in the taskbar corner, then drag the Study Stash icon down beside the clock.";
    /// <summary>The Chrome helper card's Canvas connection (made when the AI offered it).</summary>
    public CanvasConnectModel? Canvas => Setup.Canvas;
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
        ? (m.Model.Bundled ? "It comes with the app, so there's nothing to download. " : $"{m.Size}, once. ") + $"It turns speech into text on this {Setup.DeviceWord}, so recordings never leave it."
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
    partial void OnOutcomeChanged(string value)
    {
        OnPropertyChanged(nameof(HasOutcome));
        OnPropertyChanged(nameof(FoldedLine));
    }
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

    /// <summary>The model card's list: another model picked instead.</summary>
    [RelayCommand]
    Task PickModel(Services.ModelChoice choice) => owner.PressAsync(this, "pick:" + choice.Model.Id);

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
    public string Tag => ShowOptional ? "Optional" : "";
    public bool HasTag => Tag.Length > 0;
    /// <summary>The second line: its detail, or "Recommended" for a recommended one not done yet.</summary>
    public string DetailLine => HasDetail ? Detail : ShowRecommended ? "Recommended" : "";
    public bool HasDetailLine => DetailLine.Length > 0;

    partial void OnStateChanged(ChecklistState value)
    {
        foreach (string p in new[] { nameof(Clickable), nameof(IsDone), nameof(IsNow), nameof(IsTodo), nameof(IsSkipped), nameof(IsProblem), nameof(ShowOptional),
                     nameof(ShowRecommended), nameof(Tag), nameof(HasTag), nameof(DetailLine), nameof(HasDetailLine) })
            OnPropertyChanged(p);
    }

    partial void OnDetailChanged(string value)
    {
        OnPropertyChanged(nameof(HasDetail));
        OnPropertyChanged(nameof(DetailLine));
        OnPropertyChanged(nameof(HasDetailLine));
    }

    partial void OnOptionalChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowOptional));
        OnPropertyChanged(nameof(Tag));
        OnPropertyChanged(nameof(HasTag));
    }

    partial void OnRecommendedChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowRecommended));
        OnPropertyChanged(nameof(DetailLine));
        OnPropertyChanged(nameof(HasDetailLine));
    }
}
