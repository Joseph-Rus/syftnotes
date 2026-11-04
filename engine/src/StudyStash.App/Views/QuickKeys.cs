using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using StudyStash.App.Platform;
using Avalonia.Input.Platform;
using StudyStash.App.ViewModels;

namespace StudyStash.App.Views;

/// <summary>The quick panel's keys, the same in both looks: ↑↓ move, Return opens, ⌘Return (Ctrl+Enter) asks, Escape
/// closes, ⌘C (Ctrl+C) copies the answer.</summary>
public static class QuickKeys
{
    /// <summary>The chosen row scrolls into view when the results scroll inside the panel.</summary>
    public static void Reveal(UserControl view, QuickModel m)
    {
        if (m.Selected is { } row && view.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.DataContext == row && b.IsEffectivelyVisible) is { } button) button.BringIntoView();
    }

    public static void Attach(UserControl view, TextBox box)
    {
        // A new set of results (another letter typed, the panel opened again, an answer) starts at the top, its first
        // row in view: the results' scroll would otherwise stay where the last set was left.
        QuickModel? watched = null;
        void Top(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Reset && view.FindControl<ScrollViewer>("Results") is { } results) results.Offset = default;
        }
        view.DataContextChanged += (_, _) =>
        {
            if (watched is not null) watched.Rows.CollectionChanged -= Top;
            watched = view.DataContext as QuickModel;
            if (watched is not null) watched.Rows.CollectionChanged += Top;
        };
        box.AddHandler(InputElement.KeyDownEvent, async (_, e) =>
        {
            if (view.DataContext is not QuickModel m) return;
            bool command = e.KeyModifiers.HasFlag(OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control);
            switch (e.Key)
            {
                case Key.Down:
                    m.Move(1);
                    Reveal(view, m);
                    e.Handled = true;
                    break;
                case Key.Up:
                    m.Move(-1);
                    Reveal(view, m);
                    e.Handled = true;
                    break;
                case var _ when Keybindings.Of(KeyAction.Ask).Matches(e.Key, e.KeyModifiers):
                    e.Handled = true;
                    await m.AskCommand.ExecuteAsync(null);
                    break;
                case Key.Enter:
                    e.Handled = true;
                    m.OpenCommand.Execute(null);
                    break;
                case Key.Escape:
                    e.Handled = true;
                    m.CloseCommand.Execute(null);
                    break;
                case Key.C when command && m.Answering && string.IsNullOrEmpty(box.SelectedText):
                    e.Handled = true;
                    if (TopLevel.GetTopLevel(view)?.Clipboard is { } clip) await clip.SetTextAsync(m.Answer);
                    break;
            }
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }
}
