using Avalonia.Controls;

namespace StudyStash.App.Controls;

/// <summary>
/// Selecting the notes' text with the mouse, as on a web page (<see cref="TextSelection"/>, which the transcript, the
/// chats and the other pages' words select by too): press and drag from one paragraph into the next, through bullets,
/// tables and formulas; ⌘C or Copy in the right-click menu copies it, a bullet with its "• ", a table's cells a tab apart,
/// a formula as its LaTeX. On paper nothing selects. In something that selects itself (a chat's thread), that does the
/// selecting, so a drag runs on from these notes into what's beside them.
/// </summary>
public sealed partial class NoteView
{
    TextSelection selection = null!;

    void SetUpSelecting() => selection = new TextSelection(this, () => Print);

    /// <summary>The selection as it's copied; empty when nothing is selected.</summary>
    internal string SelectedText => selection.SelectedText;

    /// <summary>Nothing selected: what new notes leave (a selection around them that took some of them ends too).</summary>
    void Unselect() => TextSelection.Rebuilding(this);

    /// <summary>The word a double click at <paramref name="at"/> takes (<see cref="TextSelection.WordAround"/>).</summary>
    internal static (int Start, int End) WordAround(string text, int at) => TextSelection.WordAround(text, at);
}
