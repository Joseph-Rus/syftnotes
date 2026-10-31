using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using StudyStash.App.ViewModels;

namespace StudyStash.App.Views;

public partial class MacAiQuotes : UserControl
{
    public MacAiQuotes()
    {
        InitializeComponent();
        AiQuotes.ReadOnHover(this);
    }

    void OnChip(object? sender, RoutedEventArgs e) => AiQuotes.Chip(sender)?.Go();

    void OnChipEnter(object? sender, PointerEventArgs e) => AiQuotes.Chip(sender)?.Hover(true);

    void OnChipLeave(object? sender, PointerEventArgs e) => AiQuotes.Chip(sender)?.Hover(false);
}
