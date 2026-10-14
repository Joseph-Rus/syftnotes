using Avalonia.Controls;
using StudyStash.App.ViewModels;

namespace StudyStash.App.Views;

public partial class WinSetup : UserControl
{
    public WinSetup()
    {
        InitializeComponent();
        PickerRoom.Follow(Page);
        DataContextChanged += (_, _) =>
        {
            if (DataContext is not SetupModel m) return;
            Fit(m);
            m.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(SetupModel.Step) or nameof(SetupModel.HasCourses) or nameof(SetupModel.AiHelpOpen) or nameof(SetupModel.HasWelcomeProblem)) Fit(m);
            };
        };
    }

    /// <summary>The design's own size for each step: 900×700 for Canvas, 900×680 for the AI engines, 720×640 for
    /// Classes made from Canvas courses, 720×480 for the rest (a little taller while the welcome says why the library
    /// couldn't be made: its three choices fill the page, and the sentence takes two lines).</summary>
    void Fit(SetupModel m)
    {
        Width = m.Wide ? 900 : 720;
        Height = m.OnCanvas ? 700 : m.OnAi ? (m.AiHelpOpen ? 860 : 680) : m.OnClasses && m.HasCourses ? 640 : m.OnWelcome && m.HasWelcomeProblem ? 504 : 480;
    }

    /// <summary>A real window has the system's caption buttons in its title bar, and its corners, edge and shadow;
    /// screenshots draw their own.</summary>
    public bool DrawChrome
    {
        get => Header.DrawChrome;
        set
        {
            Header.DrawChrome = value;
            Frame.Classes.Set("framed", value);
        }
    }
}
