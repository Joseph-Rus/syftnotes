using Avalonia.Controls;
using Avalonia.VisualTree;

namespace StudyStash.App.Views;

/// <summary>The keys that put the caret in the page's Ask bar from anywhere in the library window: ⌘J on a Mac,
/// Ctrl+J on Windows (the window binds them). The bar shows them until there's a question in it.</summary>
public static class AskKeys
{
    public static string Shown => Skin.Current == SkinKind.Mac ? "⌘J" : "Ctrl+J";

    /// <summary>Puts the caret in the Ask bar showing in <paramref name="window"/>'s page; false when none is.</summary>
    public static bool Focus(Control window)
    {
        var input = window.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(t => t.Name == "AskInput" && t.IsEffectivelyVisible);
        return input?.Focus() == true;
    }
}
