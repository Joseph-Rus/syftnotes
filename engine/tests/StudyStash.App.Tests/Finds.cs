using Avalonia.Controls;
using Avalonia.VisualTree;

namespace StudyStash.App.Tests;

/// <summary>Finds a control by its name anywhere under another: the library window's parts are views of their own
/// (the sidebar, a note's page), so a name is looked for through all of them, not in the window's own alone.</summary>
static class Finds
{
    public static T Part<T>(this Control root, string name) where T : Control =>
        root.GetVisualDescendants().OfType<T>().FirstOrDefault(c => c.Name == name) ?? throw new InvalidOperationException($"nothing named {name} is on screen");
}
