using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using StudyStash.App.ViewModels;

namespace StudyStash.App.Views;

/// <summary>The small form for a lecture that already happened (<see cref="FromFileModel"/>).</summary>
public partial class FromFilePage : UserControl
{
    public FromFilePage()
    {
        InitializeComponent();
        // The title is the likeliest thing to change: the keyboard starts in it.
        Loaded += (_, _) => Dispatcher.UIThread.Post(() => TitleField.Focus(), DispatcherPriority.Input);
    }

    /// <summary>A real window has the system's own title-bar buttons, corners and shadow; pictures draw them.</summary>
    public bool DrawChrome
    {
        get => Header.DrawChrome;
        set
        {
            Header.DrawChrome = value;
            Chrome.Classes.Set("chrome", value);
        }
    }

    /// <summary>A day picked in the calendar closes it.</summary>
    void OnDayPicked(object? sender, SelectionChangedEventArgs e) => Menus.Close(sender as Control);

    /// <summary>The class menu the dropdown's picker uses: "Let the library sort it", then each class.</summary>
    void OnPickClass(object? sender, RoutedEventArgs e)
    {
        if (DataContext is FromFileModel m) ClassPicker.OpenUnder(ClassButton, m.Classes, m.Class, name => m.Class = name);
    }
}
