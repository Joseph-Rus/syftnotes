using Avalonia;
using Avalonia.Controls;

namespace StudyStash.App;

/// <summary>
/// Fluent's own parts the app hasn't replaced draw from Fluent's names, set inside their templates where a style can't
/// reach. Pointing those names at the look's tokens makes them the app's, light and dark:
/// <list type="bullet">
/// <item>its popups (a ComboBox's list, a search box's suggestions, a date picker): the same one rounded panel as the
/// menus, the popup fill and hairline, the menu's radius, and the rows inset from its edge;</item>
/// <item>a plain text field (an exam's name on Study, a new folder's name): the page's fill and a hairline, the hairline
/// a shade darker under the pointer and ink while the caret's in it, as the app's own fields (Border.field). Fluent's
/// were a near-black well in dark, and its corners (Styles.axaml rounds them) tighter than the buttons beside them.</item>
/// </list>
/// </summary>
static class FluentParts
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
            r["TextControlBackground"] = r["TextControlBackgroundPointerOver"] = r["TextControlBackgroundFocused"] = r["Surface"];
            r["TextControlBorderBrush"] = r["Line"];
            r["TextControlBorderBrushPointerOver"] = r["InkSubtle"];
            r["TextControlBorderBrushFocused"] = r["Ink"];
        }
        d["OverlayCornerRadius"] = d["RadiusMenu"];
        d["ComboBoxDropdownBorderThickness"] = d["PopupStrokeWidth"];
        d["ComboBoxDropdownBorderPadding"] = new Thickness(5);
        d["ComboBoxDropdownContentMargin"] = new Thickness(0);
        d["AutoCompleteListMargin"] = new Thickness(5);
        d["AutoCompleteListPadding"] = new Thickness(0);
        d["TextControlBorderThemeThickness"] = new Thickness(1);
        d["TextControlBorderThemeThicknessFocused"] = new Thickness(1.5);
    }
}
