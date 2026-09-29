using Avalonia.Controls;
using StudyStash.App.ViewModels;

namespace StudyStash.App.Views;

public partial class MacGuidedSetup : UserControl
{
    public MacGuidedSetup()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is not GuidedSetupModel g) return;
            Fit(g);
            g.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(GuidedSetupModel.Screen)) Fit(g);
            };
        };
    }

    /// <summary>The design's size for each screen: 720×480 before the chat, 900×560 for it.</summary>
    void Fit(GuidedSetupModel g)
    {
        Width = g.ViewWidth;
        Height = g.ViewHeight;
    }

    /// <summary>A real window has the system's traffic lights (in its title bar), rounded corners and shadow; screenshots draw all three.</summary>
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
