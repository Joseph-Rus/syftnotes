using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StudyStash.Core;
using StudyStash.Core.Ai;

namespace StudyStash.App.ViewModels;

/// <summary>One part of a long lecture's copy for a free chat: "Part 2", ticked once it's been copied.</summary>
public sealed partial class CopyPart(int number, string text) : ObservableObject
{
    public int Number { get; } = number;
    public string Text { get; } = text;
    public string Words => $"Part {Number}";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Glyph))]
    public partial bool Copied { get; set; }
    /// <summary>A tick once it's been copied.</summary>
    public string Glyph => Copied ? "check" : "content_copy";
}

/// <summary>
/// Notes from an AI Syft Notes doesn't run, on a lecture's page: over a lecture with no notes, the line that says a
/// student with no paid AI can ask the Claude app for them; "Copy for your AI" (what to ask any free chat for the notes,
/// and the transcript, in parts when the lecture is long for one message); and "Paste notes", a box for what the chat
/// wrote, saved as the lecture's notes (tidied of what chats put round them, the student's typed lines kept, any notes
/// it had kept to put back). Reads and drives one library's AI (<see cref="IAiLibrary"/>); the clipboard is the host's.
/// </summary>
public sealed partial class FreeAiModel : ObservableObject
{
    readonly IAiLibrary library;

    public FreeAiModel(IAiLibrary library, string lectureId, string noun = Nouns.Lecture)
    {
        this.library = library;
        LectureId = lectureId;
        Noun = noun;
        Parts.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasParts));
            OnPropertyChanged(nameof(PartsLine));
        };
    }

    public string LectureId { get; }
    public string Noun { get; }

    /// <summary>The lecture has no notes yet, nothing is writing them, and there's a transcript to write them from:
    /// the page offers this over where they'd be.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowOffer))]
    public partial bool Offer { get; set; }

    /// <summary>The lecture has notes of its own (an AI's, or ones it came with): pasted ones replace them, and the
    /// paste box says so (they're kept, to put back).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ReplacesWords), nameof(HasReplacesWords))]
    public partial bool HasNotes { get; set; }

    /// <summary>The <see cref="Notes.Fingerprint"/> of the library's own notes as the page shows them, when it has
    /// them: what a paste is based on, so it never replaces notes that arrived meanwhile unseen. Null (or none set)
    /// saves over whatever is there (it's kept, to put back).</summary>
    public Func<string?>? BasedOn { get; set; }
    /// <summary>The notes changed under the paste box: the next Save puts the pasted ones in their place anyway.</summary>
    bool overChanged;

    /// <summary>Puts text on the clipboard (write-only: nothing here reads it).</summary>
    public Func<string, Task>? Copy { get; set; }

    /// <summary>Once pasted notes are saved: the host reads the lecture again.</summary>
    public event Action? Saved;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSavePaste))]
    public partial bool Busy { get; set; }

    /// <summary>What just happened, in a line ("Copied. Paste it into…"), or why it couldn't.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSay))]
    public partial string? Say { get; set; }
    public bool HasSay => Say is { Length: > 0 };

    public bool ShowOffer => Offer && !Pasting;

    /// <summary>The one line a student with no paid AI is told, over a lecture with no notes.</summary>
    public string OfferLine => $"No paid AI? Ask the Claude app to “write notes for this {Noun}” and they'll appear here.";
    public const string OfferMore = "Or use any free chat: Copy for your AI, paste it into ChatGPT, Claude or Gemini, then bring back what it writes with Paste notes.";

    // --- Copy for your AI -------------------------------------------------------------------------------------------

    /// <summary>A lecture too long for one message of a free chat: its parts, each copied on its own.</summary>
    public ObservableCollection<CopyPart> Parts { get; } = [];
    public bool HasParts => Parts.Count > 1;
    public string PartsLine => $"This {Noun} is long for one message, so it's in {Parts.Count} parts. Paste part 1 into the chat, then each next part when it answers.";

    [RelayCommand]
    public async Task CopyForAi()
    {
        if (Busy || Copy is null) return;
        Busy = true;
        Say = null;
        try
        {
            var brief = await library.NotesBriefAsync(LectureId);
            if (brief is null || brief.Parts.Count == 0)
            {
                Say = "Your library runs an older Syft Notes. Update it to copy a lecture for another AI.";
                return;
            }
            Parts.Clear();
            for (int i = 0; i < brief.Parts.Count; i++) Parts.Add(new CopyPart(i + 1, brief.Parts[i]));
            await CopyPartAsync(Parts[0]);
        }
        catch (LibraryRefusedException e)
        {
            Say = Sentence(e.Message);
        }
        catch
        {
            Say = "Your library isn't answering. Try again in a moment.";
        }
        finally
        {
            Busy = false;
        }
    }

    [RelayCommand]
    async Task CopyPartOf(CopyPart part)
    {
        if (Copy is null) return;
        await CopyPartAsync(part);
    }

    async Task CopyPartAsync(CopyPart part)
    {
        await Copy!(part.Text);
        part.Copied = true;
        Say = Parts.Count == 1
            ? "Copied. Paste it into ChatGPT, Claude or Gemini, then bring the notes back with Paste notes."
            : part.Number < Parts.Count
                ? $"Part {part.Number} copied. Paste it into the chat, then copy part {part.Number + 1} when it answers."
                : $"Part {part.Number} copied: the last one. When the chat has written the notes, bring them back with Paste notes.";
    }

    // --- Paste notes ------------------------------------------------------------------------------------------------

    /// <summary>The paste box is open, where the notes would be.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowOffer))]
    public partial bool Pasting { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSavePaste))]
    public partial string PasteText { get; set; } = "";

    /// <summary>Why the last Save didn't save (what was pasted is still there); "" otherwise.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPasteProblem))]
    public partial string PasteProblem { get; set; } = "";
    public bool HasPasteProblem => PasteProblem.Length > 0;

    public bool CanSavePaste => !Busy && PasteText.Trim().Length > 0;

    public const string PasteHint = "Copy the chat's whole answer and paste it here. Syft Notes tidies away what chats add around notes.";
    public string ReplacesWords => HasNotes ? $"These replace this {Noun}'s notes. You can put those back from the ⋯ menu by the notes." : "";
    public bool HasReplacesWords => HasNotes;

    [RelayCommand]
    public void Paste()
    {
        PasteProblem = "";
        Say = null;
        Pasting = true;
    }

    [RelayCommand]
    public void CancelPaste()
    {
        overChanged = false;
        Pasting = false;
        PasteText = "";
        PasteProblem = "";
    }

    [RelayCommand]
    public async Task SavePaste()
    {
        if (!CanSavePaste) return;
        Busy = true;
        PasteProblem = "";
        try
        {
            var info = await library.SaveNotesAsync(LectureId, PasteText, replace: HasNotes || overChanged, basedOn: overChanged ? null : BasedOn?.Invoke());
            if (info is null)
            {
                PasteProblem = "Your library runs an older Syft Notes. Update it to save notes from another AI.";
                return;
            }
            Pasting = false;
            PasteText = "";
            Offer = false;
            HasNotes = true;
            overChanged = false;
            Say = null;
            Saved?.Invoke();
        }
        catch (LibraryRefusedException e) when (e.Status == 412 || (e.Status == 409 && !HasNotes && e.Message.Contains("already has notes", StringComparison.Ordinal)))
        {
            // Notes arrived (or changed) while the box was open: said, and Save again puts these in their place.
            PasteProblem = $"This {Noun} has new notes since you opened this. Save again to put these in their place (you can put those back).";
            overChanged = true;
            HasNotes = true;
        }
        catch (LibraryRefusedException e)
        {
            PasteProblem = Sentence(e.Message);
        }
        catch
        {
            PasteProblem = "Your library isn't answering, so nothing was saved. What you pasted is still here.";
        }
        finally
        {
            Busy = false;
        }
    }

    /// <summary>The library's words ("the notes are empty.") as a sentence of their own.</summary>
    static string Sentence(string said) => said.Length == 0 ? "That didn't work. Try again." : char.ToUpperInvariant(said[0]) + said[1..];
}
