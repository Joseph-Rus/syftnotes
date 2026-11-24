using Avalonia.Controls;
using Avalonia.Interactivity;
using StudyStash.App.ViewModels;

namespace StudyStash.App.Views;

/// <summary>Which class a lecture a friend shared goes in (<see cref="SharedLectureModel"/>).</summary>
public partial class SharedLecturePage : UserControl
{
    public SharedLecturePage() => InitializeComponent();

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

    void OnPickClass(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SharedLectureModel m) ClassPicker.OpenUnder(ClassButton, m.Classes, m.Class, name => m.Class = name);
    }
}
