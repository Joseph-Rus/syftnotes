using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using StudyStash.App.Controls;

namespace StudyStash.App.Views;

/// <summary>
/// The dropdown's class picker: "Let the library sort it" (the default: the library files the lecture by what was said
/// in it), then each class (its dot and name) for when you'd rather choose, with a check on what Record will do now.
/// Every label starts at the same place, whether or not its row has a dot.
/// </summary>
public static class ClassPicker
{
    public const string SortLabel = "Let the library sort it";
    /// <summary>The line under Record while the library will sort the lecture.</summary>
    public const string SortHint = "The library sorts it by what's said";

    /// <summary>The menu for <paramref name="classes"/>, <paramref name="chosen"/> checked: "" lets the library sort
    /// it, a class's name records into that class. Picking a row hands <paramref name="pick"/> the same kind of value.</summary>
    public static ContextMenu Build(IReadOnlyList<(string Name, int Color)> classes, string chosen, Action<string> pick)
    {
        var menu = new ContextMenu { WindowManagerAddShadowHint = OperatingSystem.IsMacOS() };
        menu.Items.Add(Row(SortLabel, null, chosen.Length == 0 || classes.All(c => c.Name != chosen), () => pick("")));
        if (classes.Count > 0) menu.Items.Add(new Separator());
        foreach (var (name, color) in classes) menu.Items.Add(Row(name, color, chosen == name, () => pick(name)));
        return menu;
    }

    static MenuItem Row(string label, int? color, bool picked, Action act)
    {
        var dot = new Ellipse { Width = 8, Height = 8, VerticalAlignment = VerticalAlignment.Center };
        if (color is int c) dot.Fill = Skin.ClassDot(c);
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("8,8,*") };
        header.Children.Add(dot);
        var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(text, 2);
        header.Children.Add(text);
        var item = new MenuItem
        {
            Header = header,
            Tag = label,
            Icon = picked ? new Icon { Glyph = "check", Size = 13 } : null,
        };
        item.Click += (_, _) => act();
        return item;
    }
}
