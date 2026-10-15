using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.VisualTree;

namespace StudyStash.App.Controls;

/// <summary>
/// What a screen reader says for a button made of more than a word. Avalonia names a button by its content, and for
/// content that's a panel (an icon beside a label, a switch and its words, a row of a list) that is the panel's own
/// class name: VoiceOver and Narrator read "Avalonia.Controls.StackPanel". Here every button that hasn't been given a
/// name of its own is named by the words it shows, in order, and named again when those words change. A button that
/// says more than it shows (a count, what a click does) still names itself with AutomationProperties.Name.
/// </summary>
public static class SpokenButtons
{
    /// <summary>The button's name is its own words, kept by this class (not a name a view gave it).</summary>
    static readonly AttachedProperty<bool> OursProperty = AvaloniaProperty.RegisterAttached<Button, bool>("Ours", typeof(SpokenButtons));

    /// <summary>The name each button was given last, to take back when it's named again: a name set at a style's
    /// strength stays until it's taken back, so a button whose words keep changing (the sidebar's recording row says
    /// the time every second) kept every name it ever had, about 4,000 an hour of recording.</summary>
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Button, IDisposable> given = new();

    static bool used;

    /// <summary>What a button that is only an icon, with no name and no tooltip, is for: the last resort, for the icons
    /// that mean one thing wherever they are.</summary>
    static readonly Dictionary<string, string> ByIcon = new()
    {
        ["close"] = "Close", ["more_horiz"] = "More", ["more_vert"] = "More", ["delete"] = "Delete", ["add"] = "Add",
        ["search"] = "Search", ["settings"] = "Settings", ["arrow_back"] = "Back", ["arrow_forward"] = "Forward",
        ["content_copy"] = "Copy", ["edit"] = "Edit", ["refresh"] = "Refresh", ["open_in_new"] = "Open",
    };

    /// <summary>Turns it on for the whole app (once, at start, before any window).</summary>
    public static void Use()
    {
        if (used) return;
        used = true;
        Control.LoadedEvent.AddClassHandler<Button>((button, _) => Name(button));
        // Its words changed, or one of them showed or hid: the button that holds them says the new ones.
        TextBlock.TextProperty.Changed.AddClassHandler<TextBlock>((text, _) => Again(text));
        Visual.IsVisibleProperty.Changed.AddClassHandler<TextBlock>((text, _) => Again(text));
    }

    /// <summary>Names a button by its words, unless a view named it or its content is words already (Avalonia reads
    /// those itself).</summary>
    public static void Name(Button button)
    {
        if (button.Content is null or string) return;
        if (!button.GetValue(OursProperty) && AutomationProperties.GetName(button) is { Length: > 0 }) return;
        string words = Words(button);
        // Only an icon: what its tooltip says, when that's plain words; failing that, what the icon itself means.
        if (words.Length == 0 && ToolTip.GetTip(button) is string tip) words = tip;
        if (words.Length == 0 && button.Content is Icon { Glyph: { } glyph } && ByIcon.TryGetValue(glyph, out string? meant)) words = meant;
        if (words.Length == 0) return;
        button.SetValue(OursProperty, true);
        // A style's strength, so a name a view binds later still wins. The new one first, then the last one taken back.
        var named = button.SetValue(AutomationProperties.NameProperty, words, BindingPriority.Style);
        if (given.TryGetValue(button, out var before)) before.Dispose();
        if (named is not null) given.AddOrUpdate(button, named);
        else given.Remove(button);
    }

    static void Again(TextBlock text)
    {
        if (text.FindAncestorOfType<Button>() is { } button && button.GetValue(OursProperty)) Name(button);
    }

    /// <summary>The words a button shows, in the order they're laid out: every text in it that can be seen, leaving out
    /// a button inside it (which says its own).</summary>
    static string Words(Button button)
    {
        var words = new List<string>();
        void In(Visual parent)
        {
            foreach (var child in parent.GetVisualChildren())
            {
                if (child is Button || !child.IsVisible || child.Opacity <= 0) continue;
                if (child is TextBlock { Text: { } said } && said.Trim() is { Length: > 0 } word) words.Add(word);
                else In(child);
            }
        }
        In(button);
        return string.Join(", ", words);
    }
}
