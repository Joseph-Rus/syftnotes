using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using StudyStash.Core.Rich;

namespace StudyStash.App.Controls.Rich;

/// <summary>
/// A formula on its own line in a lecture's notes, with "Show what this looks like" beside it while the pointer is on
/// it (on a lecture's page, where a question can be asked): it asks the lecture's Ask bar for the formula as a plot to
/// play with, and the answer draws one. The formula itself is untouched; the button is made the first time it's wanted.
/// </summary>
sealed class PlotFormula : Panel
{
    readonly string latex;
    Button? show;

    public PlotFormula(Control formula, string latex)
    {
        this.latex = latex;
        Children.Add(formula);
        HorizontalAlignment = formula.HorizontalAlignment;
        Margin = formula.Margin;
        formula.Margin = default;
        Background = Brushes.Transparent;
        PointerEntered += (_, _) => Show(true);
        PointerExited += (_, _) => Show(IsKeyboardFocusWithin);
    }

    /// <summary>The words on its button.</summary>
    public const string Words = "Show what this looks like";

    void Show(bool wanted)
    {
        if (wanted && DiagramHost.Find(this) is not { CanAsk: true }) wanted = false;
        if (wanted && show is null)
        {
            show = MakeButton();
            Children.Add(show);
        }
        if (show is not null) show.IsVisible = wanted;
    }

    Button MakeButton()
    {
        bool mac = Skin.Current == SkinKind.Mac;
        var icon = new Icon { Glyph = "auto_awesome", Size = 14, VerticalAlignment = VerticalAlignment.Center };
        icon.Bind(Icon.ForegroundProperty, icon.GetResourceObservable("AccentText"));
        var words = new TextBlock { Text = Words, FontSize = 12, FontWeight = FontWeight.Medium, VerticalAlignment = VerticalAlignment.Center };
        words.Bind(TextBlock.ForegroundProperty, words.GetResourceObservable("Fg"));
        var b = new Button
        {
            Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, Children = { icon, words } },
            Height = 26, Padding = new Thickness(8, 0), CornerRadius = new CornerRadius(mac ? 7 : 4),
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, -4, -4, 0),
        };
        if (Application.Current?.TryFindResource("Surface", out var t) == true && t is ControlTheme theme) b.Theme = theme;
        b.Bind(Button.BackgroundProperty, b.GetResourceObservable("PopupBg"));
        ToolTip.SetTip(b, "Ask for this formula as a plot you can play with");
        AutomationProperties.SetName(b, Words);
        b.Click += (_, e) =>
        {
            e.Handled = true;
            if (DiagramHost.Find(this) is { CanAsk: true } host) host.Ask(PlotDesign.ShowQuestion(latex));
            b.IsVisible = false;
        };
        return b;
    }

    /// <summary>The formula, with the button where the formula's worth plotting; else just the formula.</summary>
    public static Control Wrap(Control formula, string latex) => PlotDesign.Plottable(latex) ? new PlotFormula(formula, latex) : formula;
}
