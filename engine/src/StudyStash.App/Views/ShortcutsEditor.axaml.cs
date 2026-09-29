using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using StudyStash.App.ViewModels;

namespace StudyStash.App.Views;

/// <summary>The shortcut list's keys: while a row listens, every key pressed in the list goes to it first (Tunnel, so
/// a focused button's Space or Enter is a shortcut, not a click). Leaving the page stops listening.</summary>
public partial class ShortcutsEditor : UserControl
{
    public ShortcutsEditor()
    {
        InitializeComponent();
        if (Skin.Current == SkinKind.Mac) Classes.Add("mac"); else Classes.Add("win");
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (DataContext is ShortcutsModel m && m.Press(e.Key, e.KeyModifiers)) e.Handled = true;
        }, RoutingStrategies.Tunnel);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        (DataContext as ShortcutsModel)?.Stop();
        base.OnDetachedFromVisualTree(e);
    }
}
