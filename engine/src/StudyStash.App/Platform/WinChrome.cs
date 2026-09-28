using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Styling;

namespace StudyStash.App.Platform;

/// <summary>
/// A Study Stash window on Windows (the library, setup, Settings, Connect Canvas): one title bar, ours — the app's
/// mark and name on the left (<see cref="Controls.WindowHeader"/>), the minimise, maximise and close buttons on the
/// right, drawn by Avalonia so Windows treats them as its own (Snap layouts on maximise) — and Windows 11's Mica behind
/// the window. Where Windows has no Mica (Windows 10, the first Windows 11, transparency effects off), the design's Mica
/// colour stands in, in the colour theme, light or dark.
/// </summary>
public static class WinChrome
{
    /// <summary>Windows 11 22H2, the first to take a system backdrop for any window.</summary>
    const int BackdropBuild = 22621;

    /// <summary>What's behind a window: Windows 11's Mica, or the design's Mica colour, solid.</summary>
    public enum Ground
    {
        Solid,
        Mica,
    }

    /// <summary>Dresses <paramref name="w"/> as a Windows window: call once, before it's shown. No-op in the Mac look.</summary>
    public static void Apply(Window w)
    {
        if (Skin.Current != SkinKind.Win) return;
        if (Decorations() is { } theme) w.WindowDecorationsTheme = theme;
        // Avalonia's caption buttons are as tall as our title bar (the header keeps this up to date as it changes).
        SetTitleBarHeight(w, w.ExtendClientAreaTitleBarHeightHint > 0 ? w.ExtendClientAreaTitleBarHeightHint : 32);
        Use(w, Wanted());
    }

    /// <summary>Our title bar is <paramref name="height"/> tall: the window's drag strip and its caption buttons follow.</summary>
    public static void SetTitleBarHeight(Window w, double height)
    {
        if (w.ExtendClientAreaTitleBarHeightHint != height) w.ExtendClientAreaTitleBarHeightHint = height;
        w.Resources["CaptionButtonHeight"] = height;
    }

    /// <summary>The ground a window has now (the self-test reports it).</summary>
    public static Ground Of(Window w) => w.Resources.TryGetValue("Mica", out var v) && v is ISolidColorBrush { Color.A: 0 } ? Ground.Mica : Ground.Solid;

    /// <summary>Mica where Windows can show it and the student hasn't turned transparency effects off
    /// (STUDYSTASH_BACKDROP=solid always shows the plain colour).</summary>
    public static Ground Wanted()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, BackdropBuild)) return Ground.Solid;
        if (Environment.GetEnvironmentVariable("STUDYSTASH_BACKDROP") is { } forced && forced.Equals("solid", StringComparison.OrdinalIgnoreCase)) return Ground.Solid;
        return TransparencyEffectsOn() ? Ground.Mica : Ground.Solid;
    }

    /// <summary>Puts <paramref name="ground"/> behind <paramref name="w"/>. Mica shows through wherever the design has
    /// its Mica colour (that token becomes clear for this window); if Windows won't take it, the colour stays.</summary>
    public static void Use(Window w, Ground ground)
    {
        if (ground == Ground.Mica && OperatingSystem.IsWindows())
        {
            w.TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
            if (w.ActualTransparencyLevel == WindowTransparencyLevel.Transparent && w.TryGetPlatformHandle()?.Handle is IntPtr hwnd && hwnd != IntPtr.Zero
                && SetBackdrop(hwnd, MainWindowBackdrop))
            {
                w.Resources["Mica"] = Brushes.Transparent;
                w.Background = Brushes.Transparent;
                return;
            }
        }
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, BackdropBuild) && w.TryGetPlatformHandle()?.Handle is IntPtr h && h != IntPtr.Zero) SetBackdrop(h, NoBackdrop);
        w.Resources.Remove("Mica");
        w.TransparencyLevelHint = [WindowTransparencyLevel.None];
        w.Bind(Window.BackgroundProperty, w.GetResourceObservable("Mica"));
    }

    /// <summary>Settings → Personalisation → Colours → Transparency effects (on unless the student turned it off).</summary>
    static bool TransparencyEffectsOn()
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("EnableTransparency") is not int on || on != 0;
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return true;
        }
    }

    const int SystemBackdropType = 38, NoBackdrop = 1, MainWindowBackdrop = 2;

    static bool SetBackdrop(IntPtr hwnd, int type)
    {
        try
        {
            return DwmSetWindowAttribute(hwnd, SystemBackdropType, ref type, sizeof(int)) >= 0;
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    static ControlTheme? decorations;

    /// <summary>
    /// Avalonia's own window decorations (Fluent's), less what would double ours: its title (the header shows the
    /// app's mark and name already, so the two overlapped) and a full-screen button no Windows window has. The caption
    /// buttons keep Windows' red close; its glyph turns white on it, as Windows' does.
    /// </summary>
    public static ControlTheme? Decorations()
    {
        if (decorations is not null) return decorations;
        if (Application.Current?.TryFindResource(typeof(WindowDrawnDecorations), out var found) != true || found is not ControlTheme fluent) return null;
        // Each selector waits on :has-titlebar (always set while there's a title bar): a style with a condition outranks
        // what the template itself sets, which a plain one wouldn't.
        var theme = new ControlTheme(typeof(WindowDrawnDecorations)) { BasedOn = fluent };
        theme.Children.Add(Set(x => Bar(x).OfType<Panel>().Name("PART_TitleTextPanel"), Visual.IsVisibleProperty, false));
        theme.Children.Add(Set(x => Bar(x).OfType<Button>().Name("PART_FullScreenButton"), Visual.IsVisibleProperty, false));
        // Behind our title bar nothing of Avalonia's own shows through (the Mica, or the window's colour, does).
        theme.Children.Add(Set(x => Bar(x).OfType<Panel>().Name("PART_TitleBar"), Panel.BackgroundProperty, Brushes.Transparent));
        theme.Children.Add(Set(x => Bar(x).OfType<Button>().Name("PART_CloseButton").Class(":pointerover").Descendant().OfType<Avalonia.Controls.Shapes.Path>(), Shape.FillProperty, Brushes.White));
        return decorations = theme;
    }

    static Selector Bar(Selector? x) => x.Nesting().Class(":has-titlebar").Template();

    static Style Set(Func<Selector?, Selector> selector, AvaloniaProperty property, object value) => new(selector) { Setters = { new Setter(property, value) } };
}
