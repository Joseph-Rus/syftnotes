using Avalonia.Controls;

namespace StudyStash.App.Views;

/// <summary>The first run's funnel in the Windows look (<see cref="ViewModels.FunnelModel"/>).</summary>
public partial class WinFunnel : UserControl
{
    public WinFunnel()
    {
        InitializeComponent();
        // The library window's look: no accent anywhere, so the tick box gets ink.
        Resources.MergedDictionaries.Add(Skin.NoAccent());
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
