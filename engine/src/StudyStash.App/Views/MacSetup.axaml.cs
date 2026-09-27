using Avalonia.Controls;
using StudyStash.App.ViewModels;

namespace StudyStash.App.Views;

public partial class MacSetup : UserControl
{
    public MacSetup()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is not SetupModel m) return;
            Fit(m);
            m.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(SetupModel.Step) or nameof(SetupModel.HasCourses)) Fit(m);
            };
        };
    }

    /// <summary>The design's own size for each step: 900×660 for Canvas, 900×640 for the AI engines, 720×600 for
    /// Classes made from Canvas courses, 720×480 for the rest.</summary>
    void Fit(SetupModel m)
    {
        Width = m.Wide ? 900 : 720;
        Height = m.OnCanvas ? 660 : m.OnAi ? 640 : m.OnClasses && m.HasCourses ? 600 : 480;
    }

    /// <summary>A real window has the system's traffic lights (in its title bar), rounded corners and shadow;
    /// screenshots draw all three.</summary>
    public bool DrawChrome
    {
        get => Header.DrawChrome;
        set
        {
            Header.DrawChrome = value;
            Chrome.Classes.Set("chrome", value);
        }
    }
}
