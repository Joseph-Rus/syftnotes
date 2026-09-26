using Avalonia.Controls;
using StudyStash.App.ViewModels;

namespace StudyStash.App.Views;

public partial class WinSetup : UserControl
{
    public WinSetup()
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

    /// <summary>The design's own size for each step: 900×800 for Canvas, 900×680 for the AI engines, 720×480 for the rest.</summary>
    void Fit(SetupModel m)
    {
        Width = m.Wide ? 900 : 720;
        Height = m.OnCanvas ? 800 : m.OnAi ? 680 : 480;
    }

    /// <summary>A real window has the system's caption buttons; screenshots draw their own.</summary>
    public bool DrawChrome
    {
        get => Captions.IsVisible;
        set => Captions.IsVisible = value;
    }
}
