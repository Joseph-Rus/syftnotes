using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.VisualTree;
using StudyStash.App.ViewModels;

namespace StudyStash.App.Views;

/// <summary>
/// One notification, in the system's own style for the look: a title, a few lines of plain words (never an error
/// code), and up to two quiet buttons. <see cref="Acted"/> fires for the first button, and for a click anywhere else on
/// it (as the system's own open what they're about); <see cref="Dismissed"/> for the × and the second button (Later).
/// The commands, when set, run as well, so a view model can drive it (<see cref="For(CanvasToastModel)"/>).
/// </summary>
public partial class ToastView : UserControl
{
    public static readonly StyledProperty<string> TitleProperty = AvaloniaProperty.Register<ToastView, string>(nameof(Title), "");
    public static readonly StyledProperty<string> TextProperty = AvaloniaProperty.Register<ToastView, string>(nameof(Text), "");
    public static readonly StyledProperty<string> WhenProperty = AvaloniaProperty.Register<ToastView, string>(nameof(When), "");
    public static readonly StyledProperty<string?> ActionLabelProperty = AvaloniaProperty.Register<ToastView, string?>(nameof(ActionLabel));
    public static readonly StyledProperty<string?> SecondLabelProperty = AvaloniaProperty.Register<ToastView, string?>(nameof(SecondLabel));
    public static readonly StyledProperty<bool> ShowActionsProperty = AvaloniaProperty.Register<ToastView, bool>(nameof(ShowActions), true);
    public static readonly StyledProperty<ICommand?> ActionCommandProperty = AvaloniaProperty.Register<ToastView, ICommand?>(nameof(ActionCommand));
    public static readonly StyledProperty<ICommand?> SecondCommandProperty = AvaloniaProperty.Register<ToastView, ICommand?>(nameof(SecondCommand));
    public static readonly StyledProperty<ICommand?> CloseCommandProperty = AvaloniaProperty.Register<ToastView, ICommand?>(nameof(CloseCommand));

    readonly bool mac = Skin.Current == SkinKind.Mac;

    public ToastView()
    {
        InitializeComponent();
        Classes.Set("mac", mac);
        Classes.Set("win", !mac);
        ActionButton.Click += (_, _) => Act();
        SecondButton.Click += (_, _) =>
        {
            Run(SecondCommand);
            Dismissed?.Invoke();
        };
        MacClose.Click += (_, _) => Close();
        WinClose.Click += (_, _) => Close();
        // A click on the panel itself (not one of its buttons) does what its button would.
        bool pressed = false;
        Card.PointerPressed += (_, e) => pressed = !OnAButton(e.Source) && e.GetCurrentPoint(Card).Properties.IsLeftButtonPressed;
        Card.PointerReleased += (_, e) =>
        {
            if (!pressed) return;
            pressed = false;
            if (!OnAButton(e.Source) && !string.IsNullOrEmpty(ActionLabel)) Act();
        };

        Card.CornerRadius = new CornerRadius(mac ? 16 : 8);
        Card.Padding = mac ? new Thickness(12, 11, 14, 12) : new Thickness(16, 12, 16, 16);
        Bind(Card, Border.BackgroundProperty, "PopupBg");
        Bind(Card, Border.BorderBrushProperty, "PopupStroke");
        Bind(Card, Border.BorderThicknessProperty, "PopupStrokeWidth");
        Bind(Card, Border.BoxShadowProperty, "PopupShadow");
        WinHeader.IsVisible = !mac;
        MacIcon.IsVisible = mac;
        // Windows 11 leaves a little more air between a toast's title, words and buttons than a Mac banner does.
        Words.Spacing = mac ? 1 : 2;
        Actions.Margin = new Thickness(0, mac ? 8 : 12, 0, 0);
        LayOutActions();
    }

    static bool OnAButton(object? source) => source is Visual v && v.GetSelfAndVisualAncestors().OfType<Button>().Any();

    void Act()
    {
        Run(ActionCommand);
        Acted?.Invoke();
    }

    void Close()
    {
        Run(CloseCommand);
        Dismissed?.Invoke();
    }

    static void Run(ICommand? command)
    {
        if (command?.CanExecute(null) == true) command.Execute(null);
    }

    static void Bind(Control c, AvaloniaProperty p, string key) => c.Bind(p, c.GetResourceObservable(key));

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ActionLabelProperty || change.Property == SecondLabelProperty || change.Property == ShowActionsProperty)
            LayOutActions();
    }

    /// <summary>The buttons under the words: right-aligned on a Mac (like a banner's), sharing the width on Windows.</summary>
    void LayOutActions()
    {
        if (Actions is null) return;
        bool first = !string.IsNullOrEmpty(ActionLabel), second = !string.IsNullOrEmpty(SecondLabel);
        ActionButton.IsVisible = first;
        SecondButton.IsVisible = second;
        Actions.IsVisible = ShowActions && (first || second);
        Actions.ColumnDefinitions.Clear();
        if (mac)
        {
            Actions.ColumnDefinitions.AddRange([new(GridLength.Star), new(GridLength.Auto), new(new GridLength(first && second ? 8 : 0)), new(GridLength.Auto)]);
            Grid.SetColumn(ActionButton, 1);
            Grid.SetColumn(SecondButton, 3);
        }
        else
        {
            Actions.ColumnDefinitions.AddRange(first && second
                ? [new(GridLength.Star), new(new GridLength(8)), new(GridLength.Star)]
                : [new(GridLength.Star), new(new GridLength(0)), new(GridLength.Auto)]);
            Grid.SetColumn(ActionButton, 0);
            Grid.SetColumn(SecondButton, first ? 2 : 0);
        }
    }

    public string Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>"now", "2 min ago": shown where a Mac banner shows its time (Canvas notifications have one).</summary>
    public string When
    {
        get => GetValue(WhenProperty);
        set => SetValue(WhenProperty, value);
    }

    public string? ActionLabel
    {
        get => GetValue(ActionLabelProperty);
        set => SetValue(ActionLabelProperty, value);
    }

    public string? SecondLabel
    {
        get => GetValue(SecondLabelProperty);
        set => SetValue(SecondLabelProperty, value);
    }

    /// <summary>Whether the buttons show at all: a stack of Canvas notifications shows them on the freshest only.</summary>
    public bool ShowActions
    {
        get => GetValue(ShowActionsProperty);
        set => SetValue(ShowActionsProperty, value);
    }

    public ICommand? ActionCommand
    {
        get => GetValue(ActionCommandProperty);
        set => SetValue(ActionCommandProperty, value);
    }

    public ICommand? SecondCommand
    {
        get => GetValue(SecondCommandProperty);
        set => SetValue(SecondCommandProperty, value);
    }

    public ICommand? CloseCommand
    {
        get => GetValue(CloseCommandProperty);
        set => SetValue(CloseCommandProperty, value);
    }

    public event Action? Acted;
    public event Action? Dismissed;

    /// <summary>A Canvas notification (design 12) in the same panel: Open, and Later (Mac) or Dismiss (Windows), on the
    /// freshest of a batch; the × marks it seen too.</summary>
    public static ToastView For(CanvasToastModel toast)
    {
        var view = new ToastView
        {
            DataContext = toast, Title = toast.Title, Text = toast.Text, ActionLabel = "Open", SecondLabel = toast.DismissLabel,
            ActionCommand = toast.OpenCommand, SecondCommand = toast.DismissCommand, CloseCommand = toast.DismissCommand,
        };
        view.Bind(WhenProperty, new Binding(nameof(CanvasToastModel.When)) { Source = toast });
        view.Bind(ShowActionsProperty, new Binding(nameof(CanvasToastModel.Expanded)) { Source = toast });
        return view;
    }
}
