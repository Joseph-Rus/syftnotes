using Avalonia.Controls;
using Avalonia.VisualTree;
using StudyStash.App.ViewModels;

namespace StudyStash.App.Views;

/// <summary>What the two looks of the chips under an answer (<c>MacAiQuotes</c>, <c>WinAiQuotes</c>) share.</summary>
static class AiQuotes
{
    public static QuoteChip? Chip(object? sender) => (sender as Control)?.DataContext as QuoteChip;

    /// <summary>The pointer coming over the answer the chips are under makes it the one the notes highlight (when it
    /// quotes them): <paramref name="chips"/>'s parent is that answer's own column.</summary>
    public static void ReadOnHover(Control chips) => chips.AttachedToVisualTree += (_, _) =>
    {
        if (chips.GetVisualParent() is Control column)
            column.PointerEntered += (_, _) => (chips.DataContext as AiTurn)?.Reading();
    };
}
