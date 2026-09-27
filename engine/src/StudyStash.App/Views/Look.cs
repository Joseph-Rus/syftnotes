using Avalonia.Controls;
using Avalonia.Media;
using StudyStash.App.Controls;

namespace StudyStash.App.Views;

/// <summary>What every Study Stash window shares.</summary>
public static class Look
{
    /// <summary>Grayscale antialiasing: text as macOS draws it now (and as the design does), not the older, heavier
    /// subpixel smoothing. Call after the skin is chosen, and after a window's transparency is set.</summary>
    public static void Apply(TopLevel window)
    {
        TextOptions.SetTextRenderingMode(window, TextRenderingMode.Antialias);
        // A Mac doesn't hint type (hinting also snaps letters to whole pixels, so words run wide); Windows does.
        TextOptions.SetTextHintingMode(window, Skin.Current == SkinKind.Mac ? TextHintingMode.None : TextHintingMode.Light);
        RenderOptions.SetBitmapInterpolationMode(window, Avalonia.Media.Imaging.BitmapInterpolationMode.HighQuality);
        // A clear window on the Mac (the dropdown, the recorder, the quick panel) stays clear: its glass starts solid,
        // so it's readable over anything, until (if) the system's blur is put under each glass, and only there.
        if (Skin.Current == SkinKind.Mac && window.TransparencyLevelHint.Contains(WindowTransparencyLevel.Transparent))
        {
            window.Classes.Add("solidglass");
            if (window is Window w) GlassBackdrop.Attach(w);
        }
    }
}
