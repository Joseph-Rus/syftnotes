using Avalonia;
using Avalonia.Controls;

namespace StudyStash.App;

/// <summary>
/// Fluent's own popups the app hasn't replaced (a ComboBox's list, a search box's suggestions, a date picker) draw
/// their panel from Fluent's names, set inside their templates where a style can't reach. Pointing those names at the
/// look's popup tokens makes them the same one rounded panel as the menus: the popup fill and hairline, the menu's
/// radius, and the rows inset from its edge, light and dark.
/// </summary>
static class FluentPopups
{
    /// <summary>Adds the names to a look's tokens (<paramref name="d"/>, with its light and dark dictionaries).</summary>
    public static void Add(ResourceDictionary d, ResourceDictionary light, ResourceDictionary dark)
    {
        foreach (var r in new[] { light, dark })
        {
            r["ComboBoxDropDownBackground"] = r["PopupBg"];
            r["ComboBoxDropDownBorderBrush"] = r["PopupStroke"];
            r["AutoCompleteBoxSuggestionsListBackground"] = r["PopupBg"];
            r["AutoCompleteBoxSuggestionsListBorderBrush"] = r["PopupStroke"];
        }
        d["OverlayCornerRadius"] = d["RadiusMenu"];
        d["ComboBoxDropdownBorderThickness"] = d["PopupStrokeWidth"];
        d["ComboBoxDropdownBorderPadding"] = new Thickness(5);
        d["ComboBoxDropdownContentMargin"] = new Thickness(0);
        d["AutoCompleteListMargin"] = new Thickness(5);
        d["AutoCompleteListPadding"] = new Thickness(0);
    }
}
