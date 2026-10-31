using Avalonia.Controls;

namespace StudyStash.App.Views;

/// <summary>Getting an AI ready from a lecture's page, in the Mac look: guided setup's Install and Sign in screens on
/// their own (<see cref="ViewModels.GuidedSetupModel.ForNotes"/>).</summary>
public partial class MacGetReady : UserControl
{
    public MacGetReady()
    {
        InitializeComponent();
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
