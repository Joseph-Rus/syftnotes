using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Data.Converters;
using StudyStash.App.ViewModels;

namespace StudyStash.App.Controls;

public static class Converters
{
    /// <summary>The quick panel's rows but its actions (which the Mac draws as chips below them).</summary>
    public static readonly IValueConverter NotActions =
        new FuncValueConverter<ObservableCollection<QuickRow>?, FilteredRows?>(rows => rows is null ? null : new FilteredRows(rows, r => !r.IsAction));

    /// <summary>Only the quick panel's actions: Record, Open library and the rest.</summary>
    public static readonly IValueConverter Actions =
        new FuncValueConverter<ObservableCollection<QuickRow>?, FilteredRows?>(rows => rows is null ? null : new FilteredRows(rows, r => r.IsAction));

    /// <summary>A count that isn't nothing: show what holds the rows only when there are some.</summary>
    public static readonly IValueConverter Some = new FuncValueConverter<int, bool>(n => n > 0);

    /// <summary>The action that records (or stops recording): the Mac draws its icon filled, in the accent.</summary>
    public static readonly IValueConverter Records = new FuncValueConverter<string?, bool>(glyph => glyph is "mic" or "stop");
    /// <summary>Windows card lists: the first card in a list sits flush; the rest get a gap above them.</summary>
    public static readonly IValueConverter FirstCardMargin = new FuncValueConverter<bool, Thickness>(first => first ? new Thickness(0) : new Thickness(0, 8, 0, 0));

    /// <summary>Windows toggle label: "On" or "Off" next to the switch.</summary>
    public static readonly IValueConverter OnOff = new FuncValueConverter<bool, string>(on => on ? "On" : "Off");

    /// <summary>A row that only shows when it has something to say: an engine's subtitle, a menu's footer.</summary>
    public static readonly IValueConverter NotEmpty = new FuncValueConverter<string?, bool>(s => !string.IsNullOrEmpty(s));
}
