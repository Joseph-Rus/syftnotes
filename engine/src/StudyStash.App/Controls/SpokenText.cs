using System.Text;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using StudyStash.App.Controls.Rich;

namespace StudyStash.App.Controls;

/// <summary>
/// A paragraph of a note, for a screen reader: Avalonia reads a text's own words and gives each inline formula's place
/// in it as one object-replacement character ("￼"), whatever the paragraph is named. This one reads the name it's been
/// given (<c>NoteView</c> names a paragraph with formulas with them in words) before its text.
/// </summary>
public sealed class SpokenText : TextBlock
{
    protected override Type StyleKeyOverride => typeof(TextBlock);

    protected override AutomationPeer OnCreateAutomationPeer() => new SpokenPeer(this);
}

/// <summary>
/// A piece of a note's text on screen (a paragraph, a heading, a bullet's words, a table's cell), selectable; but the
/// <see cref="NoteView"/> it's in does the selecting, so one drag runs from one piece into the next as on a web page
/// (a <see cref="SelectableTextBlock"/> on its own selects only within itself). Read as <see cref="SpokenText"/> is.
/// </summary>
public sealed class NoteText : SelectableTextBlock
{
    public NoteText()
    {
        // The notes take the focus (for ⌘C) and give the right-click menu, not each piece of them: Tab doesn't stop at
        // every paragraph, and Copy copies the whole selection, not this piece's share of it.
        Focusable = false;
        ContextFlyout = null;
    }

    protected override Type StyleKeyOverride => typeof(SelectableTextBlock);

    protected override AutomationPeer OnCreateAutomationPeer() => new SpokenPeer(this);

    /// <summary>What goes between this piece and the one before it when both are copied: a tab between a table's
    /// cells, ": " between a definition's term and its meaning; null, a new line.</summary>
    public string? CopyJoin { get; set; }

    /// <summary>What it's copied with in front when the selection takes it from its start: a bullet's "• ", a numbered
    /// item's "3. ", two spaces further in for each list it's inside.</summary>
    public string? CopyLead { get; set; }

    /// <summary>How many characters it holds, as its selection counts them (a formula in it is one).</summary>
    public int Length => Inlines is { Count: > 0 } inlines ? inlines.Text?.Length ?? 0 : Text?.Length ?? 0;

    /// <summary>Its text from <paramref name="from"/> to <paramref name="to"/> as it's copied: a formula in it as its
    /// LaTeX in $…$.</summary>
    public string Copied(int from, int to)
    {
        if (Inlines is not { Count: > 0 } inlines)
        {
            string text = Text ?? "";
            from = Math.Clamp(from, 0, text.Length);
            return text[from..Math.Clamp(to, from, text.Length)];
        }
        var copied = new StringBuilder();
        int at = 0;
        foreach (var inline in inlines)
        {
            // The same count Avalonia keeps: a run's own characters, one for anything else (a formula).
            int length = inline is Run r ? r.Text?.Length ?? 0 : 1;
            int start = Math.Max(from, at), end = Math.Min(to, at + length);
            if (start < end)
                copied.Append(inline switch
                {
                    Run run => run.Text![(start - at)..(end - at)],
                    InlineUIContainer { Child: MathView { Latex: { } latex } } => $"${latex}$",
                    _ => "",
                });
            at += length;
        }
        return copied.ToString();
    }

    // Its NoteView does the selecting.
    protected override void OnPointerPressed(PointerPressedEventArgs e) { }

    protected override void OnPointerMoved(PointerEventArgs e) { }

    protected override void OnPointerReleased(PointerReleasedEventArgs e) { }
}

sealed class SpokenPeer(TextBlock owner) : TextBlockAutomationPeer(owner)
{
    protected override string? GetNameCore() => AutomationProperties.GetName(Owner) is { Length: > 0 } named ? named : base.GetNameCore();
}
