using Avalonia.Controls;
using StudyStash.App.ViewModels;

namespace StudyStash.App.Views;

public partial class MacSetup : UserControl
{
    public MacSetup()
    {
        InitializeComponent();
        // The library window's look: no accent anywhere, so whatever inside asks for it (Canvas's steps, a ticked box,
        // the shortcuts) gets ink.
        Resources.MergedDictionaries.Add(Skin.NoAccent());
        PickerRoom.Follow(Page);
        DataContextChanged += (_, _) =>
        {
            if (DataContext is not SetupModel m) return;
            Fit(m);
            m.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(SetupModel.Step) or nameof(SetupModel.HasCourses) or nameof(SetupModel.AiHelpOpen) or nameof(SetupModel.ShowWelcomeStatus)) Fit(m);
            };
        };
    }

    /// <summary>Each step's size: 760×580 for most (the library's type, a 30 px title and a 48 px Continue, needs the
    /// room), 900×780 for the AI engines and Canvas (900 tall while an engine's setup steps show), 640 tall for Classes
    /// made from Canvas courses, and a little taller while the welcome says it's making the library, or why it
    /// couldn't.</summary>
    void Fit(SetupModel m)
    {
        Width = m.Wide ? 900 : 760;
        Height = m.OnCanvas ? 780 : m.OnAi ? (m.AiHelpOpen ? 900 : 780) : m.OnClasses && m.HasCourses ? 640 : m.OnWelcome && m.ShowWelcomeStatus ? 620 : 580;
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
