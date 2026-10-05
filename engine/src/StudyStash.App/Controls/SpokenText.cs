using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;

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

/// <summary><see cref="SpokenText"/> for the selectable paragraphs of a compact answer.</summary>
public sealed class SpokenSelectableText : SelectableTextBlock
{
    protected override Type StyleKeyOverride => typeof(SelectableTextBlock);

    protected override AutomationPeer OnCreateAutomationPeer() => new SpokenPeer(this);
}

sealed class SpokenPeer(TextBlock owner) : TextBlockAutomationPeer(owner)
{
    protected override string? GetNameCore() => AutomationProperties.GetName(Owner) is { Length: > 0 } named ? named : base.GetNameCore();
}
