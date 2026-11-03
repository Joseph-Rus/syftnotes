using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace StudyStash.App.Controls;

/// <summary>
/// Buttons are the pixel s's cells: a rounded square (or, for words, a rounded rectangle) whose corners are the
/// logo's, 1.8 of a 6.6 cell, about 27 % of its height (<see cref="Skin.CellRound"/>). Set it on a button or a border,
/// usually from a style: <c>&lt;Setter Property="c:Cell.Round" Value="True" /&gt;</c>, and its corners follow its
/// size, so a 24 px icon button, a 32 px Stop and a 48 px Record are all the same shape. No view picks a pill's radius
/// by hand any more.
/// </summary>
public static class Cell
{
    public static readonly AttachedProperty<bool> RoundProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("Round", typeof(Cell));

    public static bool GetRound(Control c) => c.GetValue(RoundProperty);
    public static void SetRound(Control c, bool value) => c.SetValue(RoundProperty, value);

    static Cell() => RoundProperty.Changed.AddClassHandler<Control>((c, e) =>
    {
        c.SizeChanged -= Fit;
        if (e.NewValue is not true) return;
        c.SizeChanged += Fit;
        Fit(c);
    });

    static void Fit(object? sender, SizeChangedEventArgs e) => Fit((Control)sender!);

    /// <summary>The corner for a control this size: from its laid-out size once it has one, else the size it asks for.</summary>
    public static CornerRadius For(double width, double height)
    {
        double side = width > 0 && height > 0 ? Math.Min(width, height) : Math.Max(width, height);
        // To the half pixel, so a cell's edge stays crisp.
        return new CornerRadius(Math.Round(side * Skin.CellRound * 2) / 2);
    }

    static void Fit(Control c)
    {
        double w = c.Bounds.Width > 0 ? c.Bounds.Width : double.IsNaN(c.Width) ? 0 : c.Width;
        double h = c.Bounds.Height > 0 ? c.Bounds.Height : double.IsNaN(c.Height) ? 0 : c.Height;
        if (w <= 0 && h <= 0) return;
        var corner = For(w, h);
        switch (c)
        {
            case TemplatedControl t when t.CornerRadius != corner: t.CornerRadius = corner; break;
            case Border b when b.CornerRadius != corner: b.CornerRadius = corner; break;
        }
    }
}
