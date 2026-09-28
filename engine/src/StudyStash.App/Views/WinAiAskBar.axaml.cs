using Avalonia;
using Avalonia.Controls;
using StudyStash.App.ViewModels;

namespace StudyStash.App.Views;

public partial class WinAiAskBar : UserControl
{
    /// <summary>The design's width for the bar.</summary>
    public const double Wide = 600;

    public WinAiAskBar()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is AiAskModel m) m.CloseMenu = () => EngineChip.Flyout?.Hide();
        };
    }

    /// <summary>Below this the engine chip shows only its icon, so the question field keeps some room.</summary>
    const double CompactBelow = 480;

    public static readonly StyledProperty<bool> CompactProperty = AvaloniaProperty.Register<WinAiAskBar, bool>(nameof(Compact));

    /// <summary>The bar is too narrow for the engine's name (a small window): the chip is just its icon.</summary>
    public bool Compact
    {
        get => GetValue(CompactProperty);
        private set => SetValue(CompactProperty, value);
    }

    /// <summary>600 wide where there's room, and all the room there is in a small window: narrower, never cut off at
    /// both sides.</summary>
    protected override Size MeasureOverride(Size availableSize)
    {
        double width = Math.Min(Wide, availableSize.Width);
        Compact = width < CompactBelow;
        var inner = base.MeasureOverride(availableSize.WithWidth(width));
        return new Size(width, inner.Height);
    }
}
