using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;

namespace StudyStash.App.Controls;

/// <summary>
/// A question field of one line (Ask, a chat's, Settings' Ask your AI, the quick panel's search) takes lines pasted into
/// it as one line, a space between each, where a field that takes no Return keeps only the first of them: a few lines
/// copied from a transcript or the notes go in whole, to ask about.
/// </summary>
public static class OneLinePaste
{
    public static readonly AttachedProperty<bool> IsOnProperty = AvaloniaProperty.RegisterAttached<TextBox, bool>("IsOn", typeof(OneLinePaste));

    public static bool GetIsOn(TextBox box) => box.GetValue(IsOnProperty);

    public static void SetIsOn(TextBox box, bool on) => box.SetValue(IsOnProperty, on);

    static OneLinePaste() => IsOnProperty.Changed.AddClassHandler<TextBox>((box, e) =>
    {
        box.RemoveHandler(TextBox.PastingFromClipboardEvent, Pasting);
        if (e.NewValue is true) box.AddHandler(TextBox.PastingFromClipboardEvent, Pasting);
    });

    static async void Pasting(object? sender, RoutedEventArgs e)
    {
        if (sender is not TextBox { AcceptsReturn: false } box || TopLevel.GetTopLevel(box)?.Clipboard is not { } clipboard) return;
        // The field's own paste would keep only the first line: this one puts them all in, once they're read.
        e.Handled = true;
        if (await clipboard.TryGetTextAsync() is { Length: > 0 } text) box.SelectedText = OneLine(text);
    }

    /// <summary>The lines of <paramref name="text"/> as one, a space between each (a table's tabs too).</summary>
    internal static string OneLine(string text) =>
        string.Join(' ', text.Split(['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
}
