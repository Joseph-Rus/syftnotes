using Avalonia.Controls;

namespace StudyStash.App.Views;

/// <summary>A first run's one page in the Mac look (<see cref="ViewModels.SetupStep.Start"/>).</summary>
public partial class MacWelcome : UserControl
{
    public MacWelcome()
    {
        InitializeComponent();
        // The library window's look: no accent anywhere, so the tick box gets ink.
        Resources.MergedDictionaries.Add(Skin.NoAccent());
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
