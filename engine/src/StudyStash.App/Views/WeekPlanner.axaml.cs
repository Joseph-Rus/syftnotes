using System.Collections;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;

namespace StudyStash.App.Views;

public partial class WeekPlanner : UserControl
{
    public static readonly StyledProperty<IEnumerable?> DaysProperty = AvaloniaProperty.Register<WeekPlanner, IEnumerable?>(nameof(Days));
    public static readonly StyledProperty<ICommand?> OpenDueProperty = AvaloniaProperty.Register<WeekPlanner, ICommand?>(nameof(OpenDue));

    public WeekPlanner()
    {
        InitializeComponent();
        WeekFit.Keep(WeekScroll, Week);
    }

    /// <summary>The week's columns (<see cref="ViewModels.WeekDay"/>), Late first when there is one.</summary>
    public IEnumerable? Days
    {
        get => GetValue(DaysProperty);
        set => SetValue(DaysProperty, value);
    }

    /// <summary>Opens something due (its <see cref="ViewModels.OverviewDue"/>).</summary>
    public ICommand? OpenDue
    {
        get => GetValue(OpenDueProperty);
        set => SetValue(OpenDueProperty, value);
    }

    /// <summary>A pick from a full day's "+2 more" closes that list.</summary>
    void OnPicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Menus.Close(sender as Control);
}
