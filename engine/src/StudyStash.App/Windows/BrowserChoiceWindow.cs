using Avalonia;
using Avalonia.Controls;
using StudyStash.App.Controls;
using StudyStash.App.ViewModels;
using StudyStash.App.Views;

namespace StudyStash.App.Windows;

/// <summary>
/// The small window that asks which browser to use for Canvas (<see cref="BrowserChoiceModel"/>): the same title bar
/// as the app's other windows, the question under it. It closes when the student picks one or says Not now.
/// It comes up by itself, so it stays over other windows and leaves the keyboard where it was.
/// <para>Made to go away whole: closing it clears what it showed and what its model would call back, so nothing a
/// closed one leaves behind (a binding, a command still running its answer) can keep the window, its view or its
/// model in memory.</para>
/// </summary>
public static class BrowserChoiceWindow
{
    /// <summary>Its width, and about how tall it comes out with two browsers (to place it before it's shown): it's as
    /// tall as what it says, so a third browser's longer line or a problem's doesn't push Not now out of it.</summary>
    public static readonly Size Wanted = new(460, 270);

    /// <summary>The window for <paramref name="model"/>, not yet shown. <paramref name="done"/> hears the pick (or
    /// null for Not now) once, as it closes for that.</summary>
    public static Window Make(BrowserChoiceModel model, Action<string?>? done = null)
    {
        bool mac = Skin.Current == SkinKind.Mac;
        Control view = mac ? new MacBrowserChoice { DataContext = model } : new WinBrowserChoice { DataContext = model };
        var header = new WindowHeader { Title = "Canvas" };
        DockPanel.SetDock(header, Dock.Top);
        var body = new Border { Padding = new Thickness(28, 10, 28, 22), Child = view };
        if (!mac)
        {
            // Windows: the header on the window's Mica, the question on a content layer under it (as setup's is).
            body.Bind(Border.BackgroundProperty, body.GetResourceObservable("Layer"));
            body.Bind(Border.BorderBrushProperty, body.GetResourceObservable("LayerStroke"));
            body.BorderThickness = new Thickness(0, 1, 0, 0);
        }
        var w = new Window
        {
            // It opens by itself, not from a click: over whatever the student is doing, so it's seen, and without
            // taking the keyboard from it, so nothing they're typing lands on a button here.
            Title = "Canvas", CanResize = false, ShowActivated = false, Topmost = true,
            Width = Wanted.Width, SizeToContent = SizeToContent.Height,
            ExtendClientAreaToDecorationsHint = true, ExtendClientAreaTitleBarHeightHint = mac ? WindowHeader.MacHeight : 32,
            Content = new DockPanel { Children = { header, body } },
        };
        if (mac) w.Bind(Window.BackgroundProperty, w.GetResourceObservable("Win"));
        model.OnDone = picked =>
        {
            model.OnDone = null;
            done?.Invoke(picked);
            w.Close();
        };
        w.Closed += (_, _) =>
        {
            // Closed by its own button, by the student, or because the question went away: either way nothing of it
            // stays wired to anything else.
            model.OnDone = null;
            view.DataContext = null;
            w.Content = null;
        };
        return w;
    }
}
