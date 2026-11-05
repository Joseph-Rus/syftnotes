using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using StudyStash.App.Controls;
using StudyStash.App.ViewModels;

namespace StudyStash.App.Views;

/// <summary>The ask for a tip as a popup over a window's dialog layer (<see cref="ShowAsync"/>). It closes when the ask
/// is over: answered, or put away unanswered.</summary>
public partial class SupportAskDialog : UserControl
{
    public SupportAskDialog()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    /// <summary>Shows <paramref name="ask"/> over <paramref name="layer"/>'s window, without bringing the window forward,
    /// until it's over. Escape or a click outside answers "Maybe later". With something already over the window, it's
    /// put away unanswered.</summary>
    public static async Task ShowAsync(ConfirmLayer layer, SupportAskModel ask)
    {
        if (layer.IsOpen)
        {
            ask.PutAway();
            return;
        }
        var dialog = new SupportAskDialog { DataContext = ask };
        void Close(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SupportAskModel.Over) && ask.Over) layer.Close(null);
        }
        ask.PropertyChanged += Close;
        // Put away in the moment before it showed: it goes as soon as it's there.
        dialog.Loaded += (_, _) => { if (ask.Over) layer.Close(null); };
        try
        {
            await layer.ShowAsync(dialog, raise: false);
        }
        finally
        {
            ask.PropertyChanged -= Close;
        }
        ask.LaterCommand.Execute(null); // a click outside; nothing if it was answered or put away
    }

    // "Maybe later" has the keyboard (or, the last time, "Don't ask again", which ends it the same way).
    void OnLoaded(object? sender, RoutedEventArgs e) =>
        Dispatcher.UIThread.Post(() => (Later.IsVisible ? Later : Never).Focus(NavigationMethod.Tab), DispatcherPriority.Loaded);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key != Key.Escape || DataContext is not SupportAskModel ask) return;
        e.Handled = true;
        ask.LaterCommand.Execute(null);
    }
}
