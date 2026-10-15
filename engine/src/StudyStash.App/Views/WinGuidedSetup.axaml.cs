using Avalonia.Controls;
using StudyStash.App.ViewModels;

namespace StudyStash.App.Views;

public partial class WinGuidedSetup : UserControl
{
    public WinGuidedSetup()
    {
        InitializeComponent();
        // The library window's look: no accent anywhere, so whatever inside asks for it (the browser card's pictures, a
        // ticked box, the shortcuts) gets ink.
        Resources.MergedDictionaries.Add(Skin.NoAccent());
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

    /// <summary>Each screen's size (GuidedSetupModel.ViewWidth and ViewHeight): setup by hand's before the chat, bigger for it.</summary>
    void Fit(GuidedSetupModel g)
    {
        Width = g.ViewWidth;
        Height = g.ViewHeight;
    }

    /// <summary>A real window has the system's caption buttons in its title bar, and its corners, edge and shadow; screenshots draw their own.</summary>
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
