using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using StudyStash.App.Services;

namespace StudyStash.App;

/// <summary>Which look: the Mac's or Windows'. Both are the same design, in the same colours and type; each keeps its
/// system's conventions (where the window's buttons are, how a menu opens).</summary>
public enum SkinKind
{
    Mac,
    Win,
}

/// <summary>How the Mac's glass treats what's behind it: the design's <c>blur(22px) saturate(200%) brightness(1.04)</c>
/// (a blur radius in pixels, then the saturation and brightness as multipliers).</summary>
public sealed record GlassFilter(double Blur, double Saturate, double Brightness);

/// <summary>
/// The design's tokens (the Semester look: one palette for both systems, light and dark), as resources the views ask
/// for by name: <c>{DynamicResource InkMuted}</c>. Everything is black, grey and white but a class's highlighter and
/// the two reds (recording, late). The names from the look before (Fg2, Group, Card) still stand, holding the new
/// colours, for the views that ask for them; only the accent comes from the colour theme, for the controls that still
/// take one. Where the design stacks several box-shadows on one element, one token holds them all (ShadowFloat = the
/// hairline round it + its shadow).
/// </summary>
public static class Skin
{
    public static SkinKind Current { get; set; } = OperatingSystem.IsWindows() ? SkinKind.Win : SkinKind.Mac;

    /// <summary>The colour theme the accent is made from: Lagoon until the student picks another.</summary>
    public static ColourTheme Theme { get; private set; } = ColourThemes.Default;

    /// <summary>STUDYSTASH_SKIN=mac|win shows the other system's look (for screenshots, or to compare).</summary>
    public static SkinKind FromEnvironment() =>
        Environment.GetEnvironmentVariable("STUDYSTASH_SKIN")?.ToLowerInvariant() switch
        {
            "win" or "windows" => SkinKind.Win,
            "mac" or "macos" => SkinKind.Mac,
            _ => OperatingSystem.IsWindows() ? SkinKind.Win : SkinKind.Mac,
        };

    /// <summary>The design's colours for light or for dark (tokens.css): the window behind everything
    /// (<see cref="Canvas"/>, which the sidebar sits straight on), the page (<see cref="Surface"/>), the week's grid
    /// and today's column lifted out of it, hairlines, the wash under the pointer, the three inks, the red that means
    /// recording and the one that means late, and each class colour as a highlighter (behind ink) and as a swatch
    /// (nothing on it, so brighter in dark).</summary>
    public sealed record Palette(Color Canvas, Color Surface, Color SurfaceSunken, Color SurfaceToday, Color Line, Color Hover, Color Press,
        Color Ink, Color InkMuted, Color InkSubtle, Color Record, Color OnRecord, Color Late, Color Focus,
        IReadOnlyDictionary<string, Color> Highlighters, IReadOnlyDictionary<string, Color> Swatches, double TodayShade, double FloatShade);

    static Dictionary<string, Color> Hues(params string[] hex) => new()
    {
        ["Yellow"] = Color.Parse(hex[0]), ["Green"] = Color.Parse(hex[1]), ["Pink"] = Color.Parse(hex[2]), ["Blue"] = Color.Parse(hex[3]),
        ["Orange"] = Color.Parse(hex[4]), ["Violet"] = Color.Parse(hex[5]), ["Teal"] = Color.Parse(hex[6]), ["Lime"] = Color.Parse(hex[7]),
    };

    static readonly Dictionary<string, Color> LightHues = Hues("#FFE46B", "#B5EEA6", "#FFC2DF", "#BCDCFF", "#FFCF98", "#DACBFF", "#A6EBE1", "#E2F28C");

    public static readonly Palette Light = new(
        Canvas: Color.Parse("#E9EBEE"), Surface: Color.Parse("#FFFFFF"), SurfaceSunken: Color.Parse("#F5F6F7"), SurfaceToday: Color.Parse("#FFFFFF"),
        Line: Color.Parse("#DDE0E4"), Hover: Color.FromArgb(0x1F, 127, 127, 127), Press: Color.FromArgb(0x33, 127, 127, 127),
        Ink: Color.Parse("#1D1F23"), InkMuted: Color.Parse("#565C66"), InkSubtle: Color.Parse("#626973"),
        Record: Color.Parse("#D42A1A"), OnRecord: Color.Parse("#FFFFFF"), Late: Color.Parse("#B3261E"), Focus: Color.Parse("#1D1F23"),
        Highlighters: LightHues, Swatches: LightHues, TodayShade: 0.18, FloatShade: 0.25);

    public static readonly Palette Dark = new(
        Canvas: Color.Parse("#0E0F11"), Surface: Color.Parse("#191A1D"), SurfaceSunken: Color.Parse("#202226"), SurfaceToday: Color.Parse("#2A2C31"),
        Line: Color.Parse("#2D3035"), Hover: Color.FromArgb(0x29, 127, 127, 127), Press: Color.FromArgb(0x42, 127, 127, 127),
        Ink: Color.Parse("#ECEEF1"), InkMuted: Color.Parse("#A4A9B1"), InkSubtle: Color.Parse("#8D939C"),
        Record: Color.Parse("#FF5A47"), OnRecord: Color.Parse("#1A0603"), Late: Color.Parse("#FF8A80"), Focus: Color.Parse("#ECEEF1"),
        Highlighters: Hues("#5A4C00", "#1F4F2C", "#5B2244", "#1D4263", "#5B380F", "#3D2E68", "#134F48", "#44500E"),
        Swatches: Hues("#E9CF4A", "#7FD27A", "#F08CBF", "#7DB8F2", "#F0A95A", "#B39CF5", "#62D3C3", "#C4DC55"), TodayShade: 0.6, FloatShade: 0.7);

    /// <summary>The highlighter each class has, in the order classes take them as they're added (the class's place
    /// in the library): the one bold colour in the app, behind an assignment's name or a class's own, and as the small
    /// swatch beside a class.</summary>
    public static readonly string[] ClassHues = ["Yellow", "Green", "Orange", "Pink", "Blue", "Teal", "Violet", "Lime"];

    /// <summary>A class's colour as view models carry it: one brush per highlighter, the light swatch's own colour.
    /// <see cref="Controls.Swatch"/> and <see cref="Controls.Highlight"/> know these and draw the look's own shade of
    /// that highlighter, light or dark (<see cref="HueOf"/>); anything else that's handed one (a PDF) gets the colour
    /// itself.</summary>
    public static readonly IBrush[] ClassDots = [.. ClassHues.Select(h => (IBrush)new SolidColorBrush(Light.Highlighters[h]).ToImmutable())];

    public static IBrush ClassDot(int index) => ClassDots[((index % ClassDots.Length) + ClassDots.Length) % ClassDots.Length];

    /// <summary>Which highlighter a class's brush stands for ("Yellow"), or null for any other brush (a calendar's
    /// own colour, Unsorted's grey).</summary>
    public static string? HueOf(IBrush? brush)
    {
        int i = brush is null ? -1 : Array.IndexOf(ClassDots, brush);
        return i < 0 ? null : ClassHues[i];
    }

    /// <summary>Switch colour themes while running: every open view follows at once (its colours are
    /// DynamicResources), no restart.</summary>
    public static void UseTheme(ColourTheme theme)
    {
        Theme = theme;
        if (Application.Current is App app) app.UseSkin(Current);
    }

    /// <summary>Settings → Appearance's mode as Avalonia's own tri-state: System follows the computer's own
    /// light/dark setting; Light and Dark always show one.</summary>
    public static ThemeVariant VariantFor(AppAppearance appearance) => appearance switch
    {
        AppAppearance.Light => ThemeVariant.Light,
        AppAppearance.Dark => ThemeVariant.Dark,
        _ => ThemeVariant.Default,
    };

    /// <summary>Apply an appearance mode to the whole running app: every open window follows at once (it's the
    /// application's own <c>RequestedThemeVariant</c>, and no window sets its own), and so does anything that reads a
    /// window's <c>ActualThemeVariant</c>, such as <see cref="Controls.GlassBackdrop"/>'s Mac blur and the Windows
    /// tray icon's ink.</summary>
    public static void UseAppearance(AppAppearance appearance)
    {
        if (Application.Current is { } app) app.RequestedThemeVariant = VariantFor(appearance);
    }

    /// <summary>A look's tokens in the current colour theme.</summary>
    public static ResourceDictionary Build(SkinKind kind) => Build(kind, Theme);

    /// <summary>A look's tokens: the colours and shadows light and dark (theme dictionaries), the font and corner radii
    /// the same in both. Made again on every switch of colour theme, so it stays cheap.</summary>
    public static ResourceDictionary Build(SkinKind kind, ColourTheme theme)
    {
        var d = new ResourceDictionary();
        ResourceDictionary light = new(), dark = new();
        Semester(light, Light);
        Semester(dark, Dark);
        // One family, Atkinson Hyperlegible Next (the app's own files), and the system's font for what it has no
        // letter for. Weight carries the hierarchy, so headings and reading text are the same face.
        const string atkinson = "avares://StudyStash/Assets/Fonts#Atkinson Hyperlegible Next";
        if (kind == SkinKind.Mac)
        {
            MacTokens(light, theme, dark: false);
            MacTokens(dark, theme, dark: true);
            // Apple Symbols: key signs like ↩ as text, never an emoji
            d["TextFont"] = d["DisplayFont"] = d["SerifFont"] = new FontFamily($"{atkinson}, .AppleSystemUIFont, SF Pro Text, Apple Symbols");
            d["PopupStrokeWidth"] = new Thickness(1);
        }
        else
        {
            WinTokens(light, theme, dark: false);
            WinTokens(dark, theme, dark: true);
            // Segoe UI Variable is Windows 11's; Windows 10 has Segoe UI.
            d["TextFont"] = d["DisplayFont"] = d["SerifFont"] = new FontFamily($"{atkinson}, Segoe UI Variable Text, Segoe UI");
            d["PopupStrokeWidth"] = new Thickness(1);
            d["CaptionButtonWidth"] = 46.0;
        }
        // Nested, not one value everywhere: a control inside the week inside the page.
        d["RadiusControl"] = new CornerRadius(8);
        d["RadiusDay"] = new CornerRadius(10);
        d["RadiusMenu"] = new CornerRadius(10);
        d["RadiusPanel"] = new CornerRadius(12);
        d["RadiusPane"] = new CornerRadius(14);
        d.ThemeDictionaries[ThemeVariant.Light] = light;
        d.ThemeDictionaries[ThemeVariant.Dark] = dark;
        FluentPopups.Add(d, light, dark);
        return d;
    }

    /// <summary>The design's own tokens, the same in both looks (tokens.css): the surfaces, the three inks, the two
    /// reds, a highlighter and a swatch for each class colour, and the two shadows anything is lifted on.</summary>
    static void Semester(ResourceDictionary r, Palette p)
    {
        void B(string key, Color c) => r[key] = new SolidColorBrush(c);
        B("Canvas", p.Canvas);
        B("Surface", p.Surface);
        B("SurfaceSunken", p.SurfaceSunken);
        B("SurfaceToday", p.SurfaceToday);
        B("Line", p.Line);
        B("Hover", p.Hover);
        B("Press", p.Press);
        B("Ink", p.Ink);
        B("InkMuted", p.InkMuted);
        B("InkSubtle", p.InkSubtle);
        B("Record", p.Record);
        B("OnRecord", p.OnRecord);
        B("Late", p.Late);
        B("Focus", p.Focus);
        foreach (string hue in ClassHues)
        {
            B("Hl" + hue, p.Highlighters[hue]);
            B("Swatch" + hue, p.Swatches[hue]);
        }
        r["ShadowToday"] = Shadows([Outer(0, 0, 0, 1, p.Line), Outer(0, 6, 14, -8, Black(p.TodayShade))]);
        r["ShadowFloat"] = Shadows([Outer(0, 0, 0, 1, p.Line), Outer(0, 10, 30, -12, Black(p.FloatShade))]);
        // The same, ringed in ink: the ask bar with the caret in it.
        r["ShadowFloatFocus"] = Shadows([Outer(0, 0, 0, 1.5, p.Ink), Outer(0, 10, 30, -12, Black(p.FloatShade))]);
        r["LineRing"] = Shadows([Outer(0, 0, 0, 1, p.Line)]);
        r["SubtleRing"] = Shadows([Outer(0, 0, 0, 1, p.InkSubtle)]);
        r["RecordRing"] = Shadows([Outer(0, 0, 0, 1.5, p.Record)]);
    }

    /// <summary>The accent a colour theme gives the controls that still take one (a filled button, a switch that's on,
    /// a link in Settings and setup). The library window uses none: there the only colour is a class's highlighter.</summary>
    static void Accents(ResourceDictionary r, ColourTheme t, bool dark, bool mac)
    {
        void B(string key, Color c) => r[key] = new SolidColorBrush(c);
        double L = t.L, c = t.C;
        bool lt = t.IsLight;
        if (mac)
        {
            B("Accent", dark && !lt ? t.O(Math.Min(L + 0.08, 0.72), c) : t.O(L, c));
            B("AccentText", dark ? (lt ? t.O(L, c) : t.O(0.8, c * 0.8)) : lt ? t.O(0.45, c * 0.9) : t.O(Math.Min(L - 0.1, 0.48), c));
            B("Tint", dark ? (lt ? t.O(L, c, 0.9) : t.O(Math.Min(L + 0.04, 0.68), c, 0.85)) : lt ? t.O(L, c, 0.92) : t.O(L, c, 0.88));
            B("AccentTint", dark ? t.O(0.7, c, 0.26) : t.O(L, c, 0.2));
            B("OnAccent", lt ? Ink : Colors.White);
        }
        else
        {
            B("Accent", dark ? (lt ? t.O(L, c) : t.O(0.8, c * 0.6)) : lt ? t.O(L, c) : t.O(Math.Max(L - 0.06, 0.42), c));
            B("AccentText", dark ? (lt ? t.O(L, c) : t.O(0.8, c * 0.6)) : lt ? t.O(0.42, c * 0.9) : t.O(Math.Max(L - 0.12, 0.4), c));
            B("OnAccent", dark || lt ? Ink : Colors.White);
        }
    }

    /// <summary>
    /// The accent's tokens with no accent in them, for the library window: there the design has none (a link is ink
    /// over a grey underline, the chosen row a chip, and the only colour a class's highlighter), so whatever inside
    /// it asks for the accent (a Canvas list's chosen row, a filled button) gets ink, with the page's white on it.
    /// A view adds this to its own resources, and everything in it follows.
    /// </summary>
    public static ResourceDictionary NoAccent()
    {
        var d = new ResourceDictionary();
        foreach (var (variant, p) in new[] { (ThemeVariant.Light, Light), (ThemeVariant.Dark, Dark) })
        {
            var r = new ResourceDictionary
            {
                ["Accent"] = new SolidColorBrush(p.Ink), ["AccentText"] = new SolidColorBrush(p.Ink), ["Tint"] = new SolidColorBrush(p.Ink),
                ["OnAccent"] = new SolidColorBrush(p.Surface), ["AccentTint"] = new SolidColorBrush(p.Hover), ["IcInfo"] = new SolidColorBrush(p.Ink),
                ["OnAccentRing"] = Shadows([Outer(0, 0, 0, 1.5, p.Surface)]), ["SystemAccentColor"] = p.Ink,
            };
            d.ThemeDictionaries[variant] = r;
        }
        return d;
    }

    /// <summary>The Mac look: the design's tokens under the names its views ask for (a window is the page's white, a
    /// grouped box the sunken grey, the three text colours the three inks).</summary>
    static void MacTokens(ResourceDictionary r, ColourTheme t, bool dark)
    {
        void B(string key, Color c) => r[key] = new SolidColorBrush(c);
        var p = dark ? Dark : Light;
        bool lt = t.IsLight;
        Accents(r, t, dark, mac: true);
        // What was glass is a plain sheet: opaque, outlined, and only the floating ones have a shadow.
        B("Glass", p.Surface);
        B("GlassSolid", p.Surface);
        B("Win", p.Surface);
        B("Raised", p.Surface);
        B("Group", p.SurfaceSunken);
        B("Fg", p.Ink);
        B("Fg2", p.InkMuted);
        B("Fg3", p.InkSubtle);
        B("Sep", p.Line);
        B("Fill", p.Hover);
        B("Fill2", p.Press);
        B("Mini", p.Surface);
        B("Hl", p.Highlighters["Yellow"]);
        B("Warn", dark ? Oklch.ToColor(0.8, 0.14, 75) : Oklch.ToColor(0.68, 0.15, 65));
        B("Ok", dark ? Oklch.ToColor(0.74, 0.15, 150) : Oklch.ToColor(0.62, 0.14, 150));
        B("Rec", p.Record);
        B("PopupBg", dark ? p.SurfaceToday : p.Surface);
        B("PopupStroke", p.Line);
        B("Good", Good);
        B("Ground", dark ? Hex("#161616") : Hex("#E4E2DF"));
        r["GlassFilter"] = new GlassFilter(0, 1, 1);
        var ring = Outer(0, 0, 0, 1, p.Line);
        var drop = Outer(0, 10, 30, -12, Black(p.FloatShade));
        r["PopupShadow"] = Shadows([Outer(0, 8, 24, 0, Black(dark ? 0.4 : 0.12)), Outer(0, 1, 3, 0, Black(dark ? 0.3 : 0.06))]);
        r["Edge"] = None;
        r["EdgeSoft"] = None;
        r["EdgeTint"] = None;
        r["GShadow"] = Shadows([drop]);
        r["SideShadow"] = None;
        r["CtlShadow"] = None;
        r["GlassShadow"] = Shadows([ring, drop]);
        r["SideGlassShadow"] = None;
        r["CtlGlassShadow"] = Shadows([ring]);
        r["RaisedShadow"] = Shadows([ring]);
        r["SegShadow"] = Shadows([ring]);
        r["WindowShadow"] = Shadows([Outer(0, 18, 50, 0, Black(dark ? 0.5 : 0.16)), Outer(0, 2, 8, 0, Black(dark ? 0.3 : 0.06)), ring]);
        r["PillShadow"] = Shadows([ring, drop]);
        // The dot's ring on a highlighted row: white on a dark accent, near-black ink on a light one — never a tint.
        r["OnAccentRing"] = Shadows([Outer(0, 0, 0, 1.5, lt ? Rgba(0x17, 0x17, 0x17, 1) : Colors.White)]);
    }

    /// <summary>The Windows look: the same tokens under the names its views ask for (the window behind is the
    /// canvas, the content layer the page's white, a card the sunken grey with a hairline round it).</summary>
    static void WinTokens(ResourceDictionary r, ColourTheme t, bool dark)
    {
        void B(string key, Color c) => r[key] = new SolidColorBrush(c);
        var p = dark ? Dark : Light;
        double L = t.L, c = t.C;
        Accents(r, t, dark, mac: false);
        B("Ground", dark ? Hex("#0E0F11") : Hex("#DADCE0"));
        B("Mica", p.Canvas);
        B("Layer", p.Surface);
        B("LayerStroke", p.Line);
        B("Acrylic", dark ? p.SurfaceToday : p.Surface);
        B("FlyStroke", p.Line);
        B("Footer", p.SurfaceSunken);
        B("Card", p.SurfaceSunken);
        B("CardStroke", p.Line);
        B("Fg", p.Ink);
        B("Fg2", p.InkMuted);
        B("Fg3", p.InkSubtle);
        B("Sep", p.Line);
        B("Subtle", p.Hover);
        B("Subtle2", p.Press);
        B("Ctrl", p.Surface);
        B("CtrlStroke", p.Line);
        B("CtrlBottom", p.Line);
        B("CtrlBorder", p.Line);
        B("Hl", p.Highlighters["Yellow"]);
        B("Taskbar", p.Canvas);
        B("PopupBg", dark ? p.SurfaceToday : p.Surface);
        B("PopupStroke", p.Line);
        B("Mini", p.Surface);
        B("Rec", p.Record);
        B("Fill", p.Hover);
        B("Fill2", p.Press);
        B("Good", Good);
        if (!dark)
        {
            B("Ok", Hex("#0F7B0F"));
            B("Warn", Hex("#9D5D00"));
            B("IbInfo", Hex("#F6F6F6"));
            B("IbOk", Hex("#DFF6DD"));
            B("IbWarn", Hex("#FFF4CE"));
            B("IbErr", Hex("#FDE7E9"));
            B("IcOk", Hex("#0F7B0F"));
            B("IcWarn", Hex("#9D5D00"));
            B("IcErr", Hex("#C42B1C"));
            B("IbGlyph", Colors.White);
        }
        else
        {
            B("Ok", Hex("#6CCB5F"));
            B("Warn", Hex("#FCE100"));
            B("IbInfo", Hex("#2B2B2B"));
            B("IbOk", Hex("#393D1B"));
            B("IbWarn", Hex("#433519"));
            B("IbErr", Hex("#442726"));
            B("IcOk", Hex("#6CCB5F"));
            B("IcWarn", Hex("#FCE100"));
            B("IcErr", Hex("#FF99A4"));
            B("IbGlyph", Colors.Black);
        }
        r["IcInfo"] = r["AccentText"];
        var ring = Outer(0, 0, 0, 1, p.Line);
        var drop = Outer(0, 10, 30, -12, Black(p.FloatShade));
        r["PopupShadow"] = Shadows([Outer(0, 8, 16, 0, Black(dark ? 0.3 : 0.14))]);
        r["Shadow"] = Shadows([drop]);
        r["ShadowLg"] = Shadows([Outer(0, 32, 64, 0, Black(dark ? 0.4 : 0.18)), Outer(0, 2, 21, 0, Black(dark ? 0.3 : 0.14))]);
        r["PillShadow"] = Shadows([ring, drop]);
        // The caption buttons Avalonia draws in a Windows window's title bar (Fluent's keys): our ink, hover and press.
        r["CaptionButtonForeground"] = r["Fg"];
        r["CaptionButtonBackground"] = r["Hover"];
        r["CaptionButtonBorderBrush"] = r["Press"];
        // Fluent's own controls (a text field's focus line, selected text) take the colour theme's accent, not the
        // one Windows is set to.
        var accent = ((SolidColorBrush)r["Accent"]!).Color;
        r["SystemAccentColor"] = accent;
        double al = Math.Clamp(dark ? 0.8 : L, 0.3, 0.9);
        for (int i = 1; i <= 3; i++)
        {
            r[$"SystemAccentColorLight{i}"] = t.O(Math.Min(0.95, al + 0.07 * i), c);
            r[$"SystemAccentColorDark{i}"] = t.O(Math.Max(0.2, al - 0.07 * i), c);
        }
    }

    /// <summary>The design's own tokens, in both looks.</summary>
    static readonly string[] SemesterBrushes =
    [
        "Canvas", "Surface", "SurfaceSunken", "SurfaceToday", "Line", "Hover", "Press", "Ink", "InkMuted", "InkSubtle", "Record",
        "OnRecord", "Late", "Focus",
    ];
    static readonly string[] SemesterShadows = ["ShadowToday", "ShadowFloat", "ShadowFloatFocus", "LineRing", "SubtleRing", "RecordRing"];
    static readonly string[] MacBrushes =
    [
        "Glass", "GlassSolid", "Win", "Raised", "Group", "Fg", "Fg2", "Fg3", "Sep", "Fill", "Fill2", "Mini", "Accent", "AccentText",
        "Tint", "OnAccent", "AccentTint", "Hl", "Warn", "Ok", "Rec", "Good", "PopupBg", "PopupStroke",
    ];
    static readonly string[] MacShadows =
    [
        "Edge", "EdgeSoft", "EdgeTint", "GShadow", "SideShadow", "CtlShadow", "GlassShadow", "SideGlassShadow", "CtlGlassShadow",
        "RaisedShadow", "SegShadow", "WindowShadow", "OnAccentRing", "PopupShadow", "PillShadow",
    ];
    static readonly string[] WinBrushes =
    [
        "Ground", "Mica", "Layer", "LayerStroke", "Acrylic", "FlyStroke", "Footer", "Card", "CardStroke", "Fg", "Fg2", "Fg3", "Sep",
        "Subtle", "Subtle2", "Ctrl", "CtrlStroke", "CtrlBottom", "CtrlBorder", "Accent", "AccentText", "OnAccent", "Hl", "Taskbar",
        "Mini", "Ok", "Warn", "Rec", "PopupBg", "PopupStroke", "IbInfo", "IbOk", "IbWarn", "IbErr", "IcInfo", "IcOk", "IcWarn", "IcErr", "IbGlyph", "Good",
    ];
    static readonly string[] WinShadows = ["Shadow", "ShadowLg", "PopupShadow", "PillShadow"];

    /// <summary>Every token a look has, and what it is (a brush, shadows, the glass's filter, a font, a radius): what
    /// <see cref="Build(SkinKind, ColourTheme)"/> must make, light and dark.</summary>
    public static IEnumerable<(string Key, Type Type)> Keys(SkinKind kind)
    {
        bool mac = kind == SkinKind.Mac;
        foreach (var k in SemesterBrushes) yield return (k, typeof(IBrush));
        foreach (var hue in ClassHues)
        {
            yield return ("Hl" + hue, typeof(IBrush));
            yield return ("Swatch" + hue, typeof(IBrush));
        }
        foreach (var k in SemesterShadows) yield return (k, typeof(BoxShadows));
        foreach (var k in mac ? MacBrushes : WinBrushes) yield return (k, typeof(IBrush));
        foreach (var k in mac ? MacShadows : WinShadows) yield return (k, typeof(BoxShadows));
        if (mac) yield return ("GlassFilter", typeof(GlassFilter));
        foreach (var k in new[] { "TextFont", "DisplayFont", "SerifFont" }) yield return (k, typeof(FontFamily));
        foreach (var k in new[] { "RadiusPanel", "RadiusControl", "RadiusMenu", "RadiusDay", "RadiusPane" }) yield return (k, typeof(CornerRadius));
        yield return ("PopupStrokeWidth", typeof(Thickness));
    }

    /// <summary>"Library connected" green, oklch(0.7 0.15 150): the same in both looks, light and dark.</summary>
    static readonly Color Good = Oklch.ToColor(0.7, 0.15, 150);
    /// <summary>The dark ink text takes on a light accent (Chalkboard, Highlighter).</summary>
    static readonly Color Ink = Color.FromRgb(0x16, 0x17, 0x1A);

    /// <summary>No shadow at all: where the look before had glass's edge highlights.</summary>
    static readonly BoxShadows None = default;

    static Color Hex(string hex) => Color.Parse(hex);
    static Color Black(double a) => Rgba(0, 0, 0, a);
    static Color Rgba(byte r, byte g, byte b, double a) => Color.FromArgb(Oklch.Byte(a), r, g, b);

    static BoxShadow Outer(double x, double y, double blur, double spread, Color c) =>
        new() { OffsetX = x, OffsetY = y, Blur = Blur(blur), Spread = spread, Color = c };

    /// <summary>
    /// A CSS box-shadow blur as Avalonia's. CSS blurs by a Gaussian of half the blur radius; Avalonia takes the radius
    /// the way Skia once did (0.2887 × radius + 0.5), which is only about 0.6 of that, so the design's shadows would
    /// come out tight and hard.
    /// </summary>
    public static double Blur(double css) => Math.Max(0, (css / 2 - 0.5) / 0.288675);

    static BoxShadows Shadows(BoxShadow[] s) => s.Length == 1 ? new BoxShadows(s[0]) : new BoxShadows(s[0], s[1..]);
}
