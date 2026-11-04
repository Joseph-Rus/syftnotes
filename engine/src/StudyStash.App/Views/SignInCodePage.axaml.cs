using Avalonia.Controls;

namespace StudyStash.App.Views;

/// <summary>The paste box for a sign-in's code in a small window of its own (<see cref="ViewModels.Kit.SignInCodeModel"/>).</summary>
public partial class SignInCodePage : UserControl
{
    public SignInCodePage() => InitializeComponent();

    /// <summary>A real window has the system's own title-bar buttons, corners and shadow; pictures draw them.</summary>
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
