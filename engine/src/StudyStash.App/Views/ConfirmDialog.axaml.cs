using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using StudyStash.App.ViewModels.Kit;

namespace StudyStash.App.Views;

/// <summary>The question over a window (<see cref="ConfirmLayer"/> shows it): the answer goes out once, as
/// <see cref="Answered"/>.</summary>
public partial class ConfirmDialog : UserControl
{
    bool answered;

    public ConfirmDialog(ConfirmAsk ask)
    {
        InitializeComponent();
        DataContext = ask;
        No.Click += (_, _) => Answer(false);
        Yes.Click += (_, _) => Answer(true);
        // The safe button has the keyboard, so Return and Space leave things as they were.
        Loaded += OnLoaded;
    }

    public ConfirmDialog() : this(new ConfirmAsk("", "", "")) { }

    public event Action<bool>? Answered;

    void Answer(bool yes)
    {
        if (answered) return;
        answered = true;
        Answered?.Invoke(yes);
    }

    void OnLoaded(object? sender, RoutedEventArgs e) => Dispatcher.UIThread.Post(() => No.Focus(NavigationMethod.Tab), DispatcherPriority.Loaded);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key != Key.Escape) return;
        e.Handled = true;
        Answer(false);
    }
}
