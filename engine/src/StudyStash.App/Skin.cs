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
/// The Syft look's design tokens, as resources the views ask for by name (<c>{DynamicResource Accent}</c>). One look
/// for both systems, light or dark (or following the computer): the brand's palette (ink, paper, coral, lilac, sky,
/// mustard and the meshes, <see cref="Controls.Syft"/>), the meanings built from it (Accent, Recording, Selected,
/// Progress, Highlight, SurfaceRaised, Border, TextMuted…), one type scale, a 4-point spacing set and four radii. The
/// names from the looks before (Fg2, Group, Card, Tint…) still stand, holding the Syft colours, for the views that ask
/// for them. There are no colour themes any more: everything is ink and paper, coral only while recording, lilac for
/// what's picked, sky for progress, and a class's own highlighter. Where the design stacks several box-shadows on one
/// element, one token holds them all (ShadowFloat = the hairline round it + its shadow).
/// </summary>
public static class Skin
{
    public static SkinKind Current { get; set; } = OperatingSystem.IsMacOS() ? SkinKind.Mac : SkinKind.Win;

    /// <summary>The one look there is now. Kept so a saved colour theme ("Lagoon", "Plum") still loads: it simply
    /// gets the Syft look.</summary>
    public static ColourTheme Theme => ColourThemes.Default;

    /// <summary>STUDYSTASH_SKIN=mac|win shows the other system's look (for screenshots, or to compare).</summary>
    public static SkinKind FromEnvironment() =>
        StudyStash.Core.TestSwitches.Get("STUDYSTASH_SKIN")?.ToLowerInvariant() switch
        {
            "win" or "windows" => SkinKind.Win,
            "mac" or "macos" => SkinKind.Mac,
            _ => OperatingSystem.IsMacOS() ? SkinKind.Mac : SkinKind.Win,
        };

    /// <summary>The surfaces and inks for light or for dark: the window behind everything (<see cref="Canvas"/>, which
    /// the sidebar sits straight on), the page (<see cref="Surface"/>), a card on the page (<see cref="Card"/>: the
    /// page's own paper in light, a step lighter than the page in dark, so it lifts off it), the week's grid (sunken),
    /// today's column lifted out of it, hairlines, the wash under the pointer, the three inks, the coral that means
    /// recording and the red that means late, and each class colour as a highlighter (behind ink, the same bright one
    /// light and dark), as a quiet one under the page's own ink (a folder title's line, History's changed lines: deep
    /// in dark, under paper words), and as a swatch (nothing on it, so brighter in dark).</summary>
    public sealed record Palette(Color Canvas, Color Surface, Color Card, Color SurfaceSunken, Color SurfaceToday, Color Line, Color Hover, Color Press,
        Color Ink, Color InkMuted, Color InkSubtle, Color Record, Color OnRecord, Color Late, Color Focus,
        IReadOnlyDictionary<string, Color> Highlighters, IReadOnlyDictionary<string, Color> QuietHighlighters, IReadOnlyDictionary<string, Color> Swatches,
        double TodayShade, double FloatShade);

    static Dictionary<string, Color> Hues(params string[] hex) => new()
    {
        ["Yellow"] = Color.Parse(hex[0]), ["Green"] = Color.Parse(hex[1]), ["Pink"] = Color.Parse(hex[2]), ["Blue"] = Color.Parse(hex[3]),
        ["Orange"] = Color.Parse(hex[4]), ["Violet"] = Color.Parse(hex[5]), ["Teal"] = Color.Parse(hex[6]), ["Lime"] = Color.Parse(hex[7]),
    };

    static readonly Dictionary<string, Color> LightHues = Hues("#FFE46B", "#B5EEA6", "#FFC2DF", "#BCDCFF", "#FFCF98", "#DACBFF", "#A6EBE1", "#E2F28C");

    /// <summary>Light: paper pages on the light mesh, ink text. The two quieter inks are the brand's muted grey and one
    /// step lighter, both over 4.5:1 on paper and on the mesh.</summary>
    public static readonly Palette Light = new(
        Canvas: Controls.Syft.MeshLight, Surface: Controls.Syft.Paper, Card: Controls.Syft.Paper, SurfaceSunken: Controls.Syft.MeshLight, SurfaceToday: Controls.Syft.White,
        Line: Color.Parse("#E2DFD8"), Hover: Color.FromArgb(0x14, 0x16, 0x13, 0x1D), Press: Color.FromArgb(0x24, 0x16, 0x13, 0x1D),
        Ink: Controls.Syft.Ink, InkMuted: Controls.Syft.Muted, InkSubtle: Color.Parse("#67656E"),
        Record: Controls.Syft.Coral, OnRecord: Controls.Syft.Ink, Late: Color.Parse("#B3261E"), Focus: Controls.Syft.Ink,
        Highlighters: LightHues, QuietHighlighters: LightHues, Swatches: LightHues, TodayShade: 0.18, FloatShade: 0.25);

    /// <summary>Dark: three steps of lightness (the sidebar deepest, the ink page, cards a step lighter), hairlines a
    /// step lighter than the ghost so they show, paper text a touch softer so big headings don't glare, and the class
    /// highlighters the same bright ones as light, with ink words on them (Highlight's :marked).</summary>
    public static readonly Palette Dark = new(
        Canvas: Color.Parse("#0E0C13"), Surface: Controls.Syft.Ink, Card: Color.Parse("#201C2A"), SurfaceSunken: Controls.Syft.MeshDark, SurfaceToday: Controls.Syft.GhostDark,
        Line: Color.Parse("#363143"), Hover: Color.FromArgb(0x1A, 0xFB, 0xFA, 0xF7), Press: Color.FromArgb(0x2B, 0xFB, 0xFA, 0xF7),
        Ink: Color.Parse("#F3F1F7"), InkMuted: Color.Parse("#B4B1BC"), InkSubtle: Color.Parse("#9A97A3"),
        Record: Controls.Syft.Coral, OnRecord: Controls.Syft.Ink, Late: Color.Parse("#FF8A80"), Focus: Controls.Syft.Paper,
        Highlighters: LightHues,
        QuietHighlighters: Hues("#5A4C00", "#1F4F2C", "#5B2244", "#1D4263", "#5B380F", "#3D2E68", "#134F48", "#44500E"),
        Swatches: Hues("#E9CF4A", "#7FD27A", "#F08CBF", "#7DB8F2", "#F0A95A", "#B39CF5", "#62D3C3", "#C4DC55"), TodayShade: 0.6, FloatShade: 0.7);

    /// <summary>
    /// The meanings, light and dark: what a view asks for when it means "the main button", "picked", "recording",
    /// "progress" rather than a colour. Accent is ink on paper in light and paper on ink in dark (the main buttons, a
    /// switch that's on, a link); Recording is coral and only coral, with ink on it (Stop's square); Selected is lilac
    /// with ink text, the same light and dark; Progress is sky on the mesh's track; Highlight is mustard with ink on it
    /// (a quoted passage); SurfaceRaised is the mesh (cards, an AI's answer, controls on a strip); Chip is the mesh in
    /// light and the ghost in dark. TextFade1–3 are the three grey steps an older transcript line fades through, all
    /// over 4.5:1 on the page.
    /// </summary>
    public sealed record Meanings(Color Accent, Color OnAccent, Color Selected, Color OnSelected, Color SelectedMuted, Color Progress,
        Color ProgressTrack, Color Highlight, Color OnHighlight, Color SurfaceRaised, Color Chip, Color TextFade1, Color TextFade2, Color TextFade3,
        Color TextHighlight);

    public static readonly Meanings LightMeans = new(
        Accent: Controls.Syft.Ink, OnAccent: Controls.Syft.Paper, Selected: Controls.Syft.Lilac, OnSelected: Controls.Syft.Ink,
        // Progress is the logo's sky a step deeper, so a bar reads at a glance on the light mesh (the owner's pick, 2026-11-03).
        SelectedMuted: Color.Parse("#3D3849"), Progress: Controls.Syft.SkyDeep, ProgressTrack: Controls.Syft.MeshLight,
        Highlight: Controls.Syft.Mustard, OnHighlight: Controls.Syft.Ink, SurfaceRaised: Controls.Syft.MeshLight, Chip: Controls.Syft.MeshLight,
        TextFade1: Color.Parse("#46444E"), TextFade2: Controls.Syft.Muted, TextFade3: Color.Parse("#706E77"),
        TextHighlight: Controls.Syft.Mustard);

    public static readonly Meanings DarkMeans = new(
        Accent: Controls.Syft.Paper, OnAccent: Controls.Syft.Ink, Selected: Controls.Syft.Lilac, OnSelected: Controls.Syft.Ink,
        // The same step deeper in dark, still bright enough on the dark mesh.
        SelectedMuted: Color.Parse("#3D3849"), Progress: Controls.Syft.SkyDeepDark, ProgressTrack: Controls.Syft.MeshDark,
        Highlight: Controls.Syft.Mustard, OnHighlight: Controls.Syft.Ink, SurfaceRaised: Controls.Syft.MeshDark, Chip: Controls.Syft.GhostDark,
        TextFade1: Color.Parse("#C9C6D0"), TextFade2: Color.Parse("#A8A5B0"), TextFade3: Color.Parse("#8A8794"),
        // Selected text in dark keeps its paper letters, so its mustard is a deep one.
        TextHighlight: Color.Parse("#5C4A0E"));

    /// <summary>
    /// The type scale, one for both systems, in points: Caption (small print, a time, a hint), Body (a row, a button's
    /// words), BodyStrong (Body in SemiBold: a row's title, a button), Subtitle (a card's heading, and the size notes
    /// are read at), Title (a section, a note's heading), Display (a page's title, the recorder's clock).
    /// </summary>
    public static readonly (string Key, double Size)[] TypeScale =
    [
        ("FontCaption", 12), ("FontBody", 14), ("FontBodyStrong", 14), ("FontSubtitle", 16), ("FontTitle", 20), ("FontDisplay", 28),
    ];

    /// <summary>The spacing set, on a 4-point grid: Space1 = 4 … Space8 = 32 (as a gap), and Inset1–Inset6 the same as
    /// padding on every side.</summary>
    public static readonly (string Key, double Size)[] Spacing =
    [
        ("Space1", 4), ("Space2", 8), ("Space3", 12), ("Space4", 16), ("Space5", 20), ("Space6", 24), ("Space8", 32),
    ];

    /// <summary>A cell's corner, of its side: the logo's 1.8 on 6.6, about 27 %. Every square button takes it from its
    /// own height (<see cref="Controls.Cell"/>), so Stop, Record, Pause and a 24 px icon button are all the s's cells.</summary>
    public const double CellRound = 1.8 / 6.6;

    /// <summary>The radii: a cell (a 30 px control at the cell's 27 %), a control (a field, a row), a card, a panel
    /// (a menu, the dropdown), and the larger panes and days kept from the look before.</summary>
    public static readonly (string Key, double Radius)[] Radii =
    [
        ("RadiusCell", 8), ("RadiusControl", 8), ("RadiusCard", 10), ("RadiusPanel", 12), ("RadiusDay", 10), ("RadiusMenu", 10), ("RadiusPane", 14),
    ];

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

    /// <summary>What picking a colour theme did before there was one look. A saved theme still loads (and Settings
    /// still passes it here), and the look stays the same.</summary>
    public static void UseTheme(ColourTheme theme) => _ = theme;

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

    /// <summary>A look's tokens. (The theme a caller from before passes makes no difference.)</summary>
    public static ResourceDictionary Build(SkinKind kind, ColourTheme theme) => Build(kind);

    /// <summary>A look's tokens: the colours and shadows light and dark (theme dictionaries), the fonts, type scale,
    /// spacing and radii the same in both.</summary>
    public static ResourceDictionary Build(SkinKind kind)
    {
        var d = new ResourceDictionary();
        ResourceDictionary light = new(), dark = new();
        Semester(light, Light, LightMeans);
        Semester(dark, Dark, DarkMeans);
        // One family, Atkinson Hyperlegible Next (the app's own files), and the system's font for what it has no
        // letter for. Weight carries the hierarchy, so headings and reading text are the same face.
        const string atkinson = "avares://StudyStash/Assets/Fonts#Atkinson Hyperlegible Next";
        if (kind == SkinKind.Mac)
        {
            MacTokens(light, dark: false);
            MacTokens(dark, dark: true);
            // Apple Symbols: key signs like ↩ as text, never an emoji
            d["TextFont"] = d["DisplayFont"] = d["SerifFont"] = new FontFamily($"{atkinson}, .AppleSystemUIFont, SF Pro Text, Apple Symbols");
            // A shortcut's keys: Lucida Grande draws ⌥ ⇧ ⌘ ⌃ ↩ before the system font does (it was read as "ᵥ" in a
            // Mac's dropdown). Only key labels: TextFont's own fallback (Greek, maths in notes) stays as it is.
            d["KeysFont"] = new FontFamily($"{atkinson}, Lucida Grande, .AppleSystemUIFont");
            d["PopupStrokeWidth"] = new Thickness(1);
        }
        else
        {
            WinTokens(light, dark: false);
            WinTokens(dark, dark: true);
            // Segoe UI Variable is Windows 11's; Windows 10 has Segoe UI.
            d["TextFont"] = d["DisplayFont"] = d["SerifFont"] = d["KeysFont"] = new FontFamily($"{atkinson}, Segoe UI Variable Text, Segoe UI");
            d["PopupStrokeWidth"] = new Thickness(1);
            d["CaptionButtonWidth"] = 46.0;
        }
        foreach (var (key, size) in TypeScale) d[key] = size;
        foreach (var (key, size) in Spacing)
        {
            d[key] = size;
            d["Inset" + key[5..]] = new Thickness(size);
        }
        d["CellRound"] = CellRound;
        // Nested, not one value everywhere: a control inside the week inside the page.
        foreach (var (key, radius) in Radii) d[key] = new CornerRadius(radius);
        // Syft Notes' brand colours (Controls/Syft/Syft.cs), the same light and dark: the colour as SyftLilac, a brush
        // of it as SyftLilacBrush. The mark, the loaders and the splash draw in them.
        foreach (var (key, colour) in Controls.Syft.Tokens)
        {
            d[key] = colour;
            d[key + "Brush"] = new SolidColorBrush(colour);
        }
        d.ThemeDictionaries[ThemeVariant.Light] = light;
        d.ThemeDictionaries[ThemeVariant.Dark] = dark;
        FluentParts.Add(d, light, dark);
        return d;
    }

    /// <summary>The look's own tokens, the same in both systems: the surfaces, the three inks, the reds, a highlighter
    /// and a swatch for each class colour, the meanings, and the two shadows anything is lifted on.</summary>
    static void Semester(ResourceDictionary r, Palette p, Meanings m)
    {
        void B(string key, Color c) => r[key] = new SolidColorBrush(c);
        B("Canvas", p.Canvas);
        B("Surface", p.Surface);
        B("Card", p.Card);
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
            B("HlQuiet" + hue, p.QuietHighlighters[hue]);
            B("Swatch" + hue, p.Swatches[hue]);
        }
        // The meanings, and the plainer names for what the look already had.
        B("Accent", m.Accent);
        B("OnAccent", m.OnAccent);
        B("AccentText", p.Ink);
        B("Tint", m.Accent);
        B("AccentTint", p.Hover);
        B("Recording", p.Record);
        B("OnRecording", p.OnRecord);
        B("Selected", m.Selected);
        B("OnSelected", m.OnSelected);
        B("SelectedMuted", m.SelectedMuted);
        B("Progress", m.Progress);
        B("ProgressTrack", m.ProgressTrack);
        B("Highlight", m.Highlight);
        B("OnHighlight", m.OnHighlight);
        B("Hl", m.TextHighlight);
        B("SurfaceRaised", m.SurfaceRaised);
        B("Chip", m.Chip);
        B("Border", p.Line);
        // The veil over a window while a question waits (Confirm): the window's own ground, three parts in four.
        B("Scrim", Color.FromArgb(0xBF, p.Canvas.R, p.Canvas.G, p.Canvas.B));
        B("Text", p.Ink);
        B("TextMuted", p.InkMuted);
        B("TextSubtle", p.InkSubtle);
        B("TextFade1", m.TextFade1);
        B("TextFade2", m.TextFade2);
        B("TextFade3", m.TextFade3);
        B("IcInfo", p.Ink);
        // Fluent's own controls (a text field's focus line, a ticked box, selected text) take the look's ink (paper in
        // dark), never the accent the computer is set to.
        r["SystemAccentColor"] = m.Accent;
        // Each hairline last, over its soft shadow (see Shadows).
        r["ShadowToday"] = Shadows([Outer(0, 6, 14, -8, Black(p.TodayShade)), Outer(0, 0, 0, 1, p.Line)]);
        r["ShadowFloat"] = Shadows([Outer(0, 10, 30, -12, Black(p.FloatShade)), Outer(0, 0, 0, 1, p.Line)]);
        // The same, ringed in ink: the ask bar with the caret in it.
        r["ShadowFloatFocus"] = Shadows([Outer(0, 10, 30, -12, Black(p.FloatShade)), Outer(0, 0, 0, 1.5, p.Ink)]);
        // The big panes' hairline only. A card's, a field's and a pill's is a border inside its corners (see Border.study-card).
        r["LineRing"] = Shadows([Outer(0, 0, 0, 1, p.Line)]);
        // A switch's knob: lifted a hair off its track.
        r["ShadowKnob"] = Shadows([new BoxShadow { OffsetY = 1, Blur = 2, Color = Black(0.25) }]);
        // The dot's ring on a highlighted row: the page's own colour round it.
        r["OnAccentRing"] = Shadows([Outer(0, 0, 0, 1.5, p.Surface)]);
    }

    /// <summary>The Mac look: the design's tokens under the names its views ask for (a window is the page's white, a
    /// grouped box the sunken grey, the three text colours the three inks).</summary>
    static void MacTokens(ResourceDictionary r, bool dark)
    {
        void B(string key, Color c) => r[key] = new SolidColorBrush(c);
        var p = dark ? Dark : Light;
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
        B("Warn", dark ? Oklch.ToColor(0.8, 0.14, 75) : Oklch.ToColor(0.68, 0.15, 65));
        B("Ok", dark ? Oklch.ToColor(0.74, 0.15, 150) : Oklch.ToColor(0.62, 0.14, 150));
        B("Rec", p.Record);
        B("PopupBg", dark ? p.SurfaceToday : p.Surface);
        B("PopupStroke", p.Line);
        B("Good", Good);
        B("Ground", dark ? Hex("#0B0910") : Hex("#E2DFD8"));
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
        r["GlassShadow"] = Shadows([drop, ring]);
        r["SideGlassShadow"] = None;
        r["CtlGlassShadow"] = Shadows([ring]);
        r["RaisedShadow"] = Shadows([ring]);
        r["SegShadow"] = Shadows([ring]);
        r["WindowShadow"] = Shadows([Outer(0, 18, 50, 0, Black(dark ? 0.5 : 0.16)), Outer(0, 2, 8, 0, Black(dark ? 0.3 : 0.06)), ring]);
        r["PillShadow"] = Shadows([drop, ring]);
    }

    /// <summary>The Windows look: the same tokens under the names its views ask for (the window behind is the
    /// canvas, the content layer the page's white, a card the sunken grey with a hairline round it).</summary>
    static void WinTokens(ResourceDictionary r, bool dark)
    {
        void B(string key, Color c) => r[key] = new SolidColorBrush(c);
        var p = dark ? Dark : Light;
        B("Ground", dark ? Hex("#0B0910") : Hex("#E2DFD8"));
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
        var ring = Outer(0, 0, 0, 1, p.Line);
        var drop = Outer(0, 10, 30, -12, Black(p.FloatShade));
        r["PopupShadow"] = Shadows([Outer(0, 8, 16, 0, Black(dark ? 0.3 : 0.14))]);
        r["Shadow"] = Shadows([drop]);
        r["ShadowLg"] = Shadows([Outer(0, 32, 64, 0, Black(dark ? 0.4 : 0.18)), Outer(0, 2, 21, 0, Black(dark ? 0.3 : 0.14))]);
        r["PillShadow"] = Shadows([drop, ring]);
        // The caption buttons Avalonia draws in a Windows window's title bar (Fluent's keys): our ink, hover and press.
        r["CaptionButtonForeground"] = r["Fg"];
        r["CaptionButtonBackground"] = r["Hover"];
        r["CaptionButtonBorderBrush"] = r["Press"];
        // Fluent's shades of the accent, all the one ink (paper in dark).
        var accent = (dark ? DarkMeans : LightMeans).Accent;
        for (int i = 1; i <= 3; i++)
        {
            r[$"SystemAccentColorLight{i}"] = accent;
            r[$"SystemAccentColorDark{i}"] = accent;
        }
    }

    /// <summary>The design's own tokens, in both looks.</summary>
    static readonly string[] SemesterBrushes =
    [
        "Canvas", "Surface", "Card", "SurfaceSunken", "SurfaceToday", "Line", "Hover", "Press", "Ink", "InkMuted", "InkSubtle", "Record",
        "OnRecord", "Late", "Focus",
    ];
    /// <summary>The meanings (<see cref="Meanings"/>) and the plainer names, in both looks.</summary>
    public static readonly string[] MeaningBrushes =
    [
        "Accent", "OnAccent", "AccentText", "Tint", "AccentTint", "Recording", "OnRecording", "Selected", "OnSelected", "SelectedMuted",
        "Progress", "ProgressTrack", "Highlight", "OnHighlight", "Hl", "SurfaceRaised", "Chip", "Border", "Text", "TextMuted", "TextSubtle",
        "TextFade1", "TextFade2", "TextFade3", "IcInfo",
    ];
    static readonly string[] SemesterShadows = ["ShadowToday", "ShadowFloat", "ShadowFloatFocus", "LineRing", "OnAccentRing", "ShadowKnob"];
    static readonly string[] MacBrushes =
    [
        "Glass", "GlassSolid", "Win", "Raised", "Group", "Fg", "Fg2", "Fg3", "Sep", "Fill", "Fill2", "Mini", "Warn", "Ok", "Rec", "Good",
        "PopupBg", "PopupStroke",
    ];
    static readonly string[] MacShadows =
    [
        "Edge", "EdgeSoft", "EdgeTint", "GShadow", "SideShadow", "CtlShadow", "GlassShadow", "SideGlassShadow", "CtlGlassShadow",
        "RaisedShadow", "SegShadow", "WindowShadow", "PopupShadow", "PillShadow",
    ];
    static readonly string[] WinBrushes =
    [
        "Ground", "Mica", "Layer", "LayerStroke", "Acrylic", "FlyStroke", "Footer", "Card", "CardStroke", "Fg", "Fg2", "Fg3", "Sep",
        "Subtle", "Subtle2", "Ctrl", "CtrlStroke", "CtrlBottom", "CtrlBorder", "Taskbar", "Mini", "Ok", "Warn", "Rec", "PopupBg",
        "PopupStroke", "IbInfo", "IbOk", "IbWarn", "IbErr", "IcOk", "IcWarn", "IcErr", "IbGlyph", "Good",
    ];
    static readonly string[] WinShadows = ["Shadow", "ShadowLg", "PopupShadow", "PillShadow"];

    /// <summary>Every token a look has, and what it is (a brush, shadows, the glass's filter, a font, a radius): what
    /// <see cref="Build(SkinKind)"/> must make, light and dark.</summary>
    public static IEnumerable<(string Key, Type Type)> Keys(SkinKind kind)
    {
        bool mac = kind == SkinKind.Mac;
        foreach (var k in SemesterBrushes) yield return (k, typeof(IBrush));
        foreach (var hue in ClassHues)
        {
            yield return ("Hl" + hue, typeof(IBrush));
            yield return ("HlQuiet" + hue, typeof(IBrush));
            yield return ("Swatch" + hue, typeof(IBrush));
        }
        foreach (var k in MeaningBrushes) yield return (k, typeof(IBrush));
        foreach (var k in SemesterShadows) yield return (k, typeof(BoxShadows));
        foreach (var k in mac ? MacBrushes : WinBrushes) yield return (k, typeof(IBrush));
        foreach (var k in mac ? MacShadows : WinShadows) yield return (k, typeof(BoxShadows));
        if (mac) yield return ("GlassFilter", typeof(GlassFilter));
        foreach (var k in new[] { "TextFont", "DisplayFont", "SerifFont", "KeysFont" }) yield return (k, typeof(FontFamily));
        foreach (var (k, _) in Radii) yield return (k, typeof(CornerRadius));
        foreach (var (k, _) in TypeScale) yield return (k, typeof(double));
        foreach (var (k, _) in Spacing)
        {
            yield return (k, typeof(double));
            yield return ("Inset" + k[5..], typeof(Thickness));
        }
        yield return ("CellRound", typeof(double));
        yield return ("PopupStrokeWidth", typeof(Thickness));
    }

    /// <summary>"Library connected" green, oklch(0.7 0.15 150): the same in both looks, light and dark.</summary>
    static readonly Color Good = Oklch.ToColor(0.7, 0.15, 150);

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

    /// <summary>
    /// Several shadows on one element. Avalonia paints them in the order given, each over the ones before (CSS paints
    /// the first on top), so a hairline ring goes last: listed first, the soft shadow under it was painted over its
    /// lower half, and in dark it read as an edge lit from above.
    /// </summary>
    static BoxShadows Shadows(BoxShadow[] s) => s.Length == 1 ? new BoxShadows(s[0]) : new BoxShadows(s[0], s[1..]);
}
