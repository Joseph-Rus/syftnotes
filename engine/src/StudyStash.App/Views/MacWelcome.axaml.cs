using Avalonia.Controls;

namespace StudyStash.App.Views;

/// <summary>A first run's one page in the Mac look (<see cref="ViewModels.SetupStep.Start"/>).</summary>
public partial class MacWelcome : UserControl
{
    public MacWelcome()
    {
        InitializeComponent();
    }

    /// <summary>A real window has the system's own title-bar buttons, corners and shadow; screenshots draw them.</summary>
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
