using Avalonia.Controls;
using StudyStash.App.ViewModels;

namespace StudyStash.App.Views;

public partial class WinSetup : UserControl
{
    public WinSetup()
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

    /// <summary>Each step's size: the Mac's (MacSetup.Fit), 20 shorter for Windows' shorter title bar.</summary>
    void Fit(SetupModel m)
    {
        Width = m.Wide ? 900 : 760;
        Height = m.OnCanvas ? 760 : m.OnAi ? (m.AiHelpOpen ? 880 : 760) : m.OnClasses && m.HasCourses ? 620 : m.OnWelcome && m.ShowWelcomeStatus ? 600 : 560;
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
