using Avalonia.Controls;

namespace StudyStash.App.Views;

/// <summary>The first run's funnel in the Mac look (<see cref="ViewModels.FunnelModel"/>).</summary>
public partial class MacFunnel : UserControl
{
    public MacFunnel()
    {
        InitializeComponent();
    }

    /// <summary>A real window has the system's own title-bar buttons, corners and shadow; screenshots draw them (the
    /// AI's screens shown in the funnel's place too).</summary>
    public bool DrawChrome
    {
        get => Header.DrawChrome;
        set
        {
            Header.DrawChrome = value;
            Chrome.Classes.Set("chrome", value);
            GetReady.DrawChrome = value;
        }
    }
}
