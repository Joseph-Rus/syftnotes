using System.Text;
using Avalonia;
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
/// <see cref="NoteView"/> it's in does the selecting (<see cref="TextSelection"/>), so one drag runs from one piece into
/// the next as on a web page (a <see cref="SelectableTextBlock"/> on its own selects only within itself). Read as
/// <see cref="SpokenText"/> is.
/// </summary>
public class NoteText : SelectableTextBlock
{
    public static readonly StyledProperty<string?> CopyJoinProperty = AvaloniaProperty.Register<NoteText, string?>(nameof(CopyJoin));
    public static readonly StyledProperty<string?> CopyLeadProperty = AvaloniaProperty.Register<NoteText, string?>(nameof(CopyLead));

    public NoteText()
    {
        // What it's in takes the focus (for ⌘C) and gives the right-click menu, not each piece: Tab doesn't stop at
        // every paragraph, and Copy copies the whole selection, not this piece's share of it.
        Focusable = false;
        ContextFlyout = null;
        // Selected, it's on the highlighter in ink, whatever its own colour (a link, the grey of what was added from
        // the recording, a warning, a chat question's white): ink on the highlighter reads in every colour theme, light
        // and dark, where some of those colours on it don't (a light theme's link nearly vanishes).
        this.Bind(SelectionBrushProperty, this.GetResourceObservable("Hl"));
        this.Bind(SelectionForegroundBrushProperty, this.GetResourceObservable("Fg"));
    }

    protected override Type StyleKeyOverride => typeof(SelectableTextBlock);

    protected override AutomationPeer OnCreateAutomationPeer() => new SpokenPeer(this);

    /// <summary>What goes between this piece and the one before it when both are copied: a tab between a table's
    /// cells, ": " between a definition's term and its meaning; null, a new line.</summary>
    public string? CopyJoin
    {
        get => GetValue(CopyJoinProperty);
        set => SetValue(CopyJoinProperty, value);
    }

    /// <summary>What it's copied with in front when the selection takes it from its start: a bullet's "• ", a numbered
    /// item's "3. ", two spaces further in for each list it's inside; a transcript line's "[12:30] ".</summary>
    public string? CopyLead
    {
        get => GetValue(CopyLeadProperty);
        set => SetValue(CopyLeadProperty, value);
    }

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

    // What it's in does the selecting, and the copying and selecting all by the keys.
    protected override void OnPointerPressed(PointerPressedEventArgs e) { }

    protected override void OnKeyDown(KeyEventArgs e) { }

    /// <summary>A piece that selects for itself (<see cref="PageText"/>) keeps its selection when the focus goes
    /// (to a button, a field); a press on other text ends it instead.</summary>
    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        base.OnLostFocus(e);
        TextSelection.Of(this)?.Redraw();
    }

    protected override void OnPointerMoved(PointerEventArgs e) { }

    protected override void OnPointerReleased(PointerReleasedEventArgs e) { }
}

/// <summary>
/// A page's own words that are worth copying (a transcript's line, a chat's question, an assignment's details, an
/// error to paste when asking for help, an address): a <see cref="TextBlock"/> to look at, styled as any other is, that
/// selects as the notes do. In a part of the page marked <c>c:TextSelection.Area="True"</c> one drag runs on through
/// every piece in it; on its own it's a selection of its own (<see cref="TextSelection"/>).
/// </summary>
public sealed class PageText : NoteText
{
    public PageText() => Cursor = new Cursor(StandardCursorType.Ibeam);

    protected override Type StyleKeyOverride => typeof(TextBlock);

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        // Nothing above selects for it: it selects for itself.
        if (TextSelection.Of(this) is null && !TextSelection.InOne(this)) _ = new TextSelection(this);
    }
}

sealed class SpokenPeer(TextBlock owner) : TextBlockAutomationPeer(owner)
{
    protected override string? GetNameCore() => AutomationProperties.GetName(Owner) is { Length: > 0 } named ? named : base.GetNameCore();
}
