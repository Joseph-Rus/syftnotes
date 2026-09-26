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
                if (e.PropertyName == nameof(SetupModel.Step)) Fit(m);
            };
        };
    }

    /// <summary>The design's own size for each step: 900×760 for Canvas, 900×640 for the AI engines, 720×480 for the rest.</summary>
    void Fit(SetupModel m)
    {
        Width = m.Wide ? 900 : 720;
        Height = m.OnCanvas ? 760 : m.OnAi ? 640 : 480;
    }

    /// <summary>A real window has the system's traffic lights, rounded corners and shadow; screenshots draw all three.</summary>
    public bool DrawChrome
    {
        get => Lights.IsVisible;
        set
        {
            Lights.IsVisible = value;
            Chrome.Classes.Set("chrome", value);
        }
    }
}
