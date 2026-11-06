using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using StudyStash.App.Services;
using Avalonia.VisualTree;
using StudyStash.App.Controls;
using StudyStash.App.Views;

namespace StudyStash.App.Tests;

/// <summary>The Syft look's tokens: every token there in both looks, light and dark; the palette and its meanings the
/// brand's own; text on each surface and each fill readable; a colour theme saved before still loading; light and dark
/// swapped while the app runs. (OKLCH is still checked against Chrome: the status colours are worked out in it.)</summary>
public partial class ThemeTests
{
    static readonly SkinKind[] Looks = [SkinKind.Mac, SkinKind.Win];
    static readonly ThemeVariant[] Variants = [ThemeVariant.Light, ThemeVariant.Dark];

    static bool Close(Color a, Color b) => Math.Abs(a.R - b.R) <= 1 && Math.Abs(a.G - b.G) <= 1 && Math.Abs(a.B - b.B) <= 1;

    [Fact]
    public void Oklch_matches_Chrome()
    {
        foreach (var row in ThemeExpected.Rows)
        {
            var got = Oklch.ToColor(row.L, row.C, row.H, row.A);
            Assert.True(Close(got, Color.Parse(row.Rgb)), $"oklch({row.L} {row.C} {row.H}) is {got}; Chrome drew {row.Rgb}");
            Assert.Equal(row.Alpha, got.A);
        }
    }

    [Fact]
    public void Oklch_clips_like_Chrome_and_the_class_dots()
    {
        // Out of sRGB: each channel clipped, no gamut mapping (headless Chrome draws #B2E71E).
        Assert.Equal(Color.Parse("#B2E71E"), Oklch.ToColor(0.86, 0.21, 125));
        foreach (var dot in StudyStash.Core.ClassColors.Palette)
            Assert.Equal(Color.Parse(StudyStash.Core.ClassColors.Hex(dot)), Oklch.ToColor(dot.L, dot.C, dot.H));
        Assert.Equal(0x80, Oklch.ToColor(0.5, 0, 0, 0.5).A);
    }

    [AvaloniaFact]
    public void Every_token_exists_in_light_and_dark_in_both_looks()
    {
        foreach (var kind in Looks)
        {
            var d = Skin.Build(kind);
            foreach (var variant in Variants)
                foreach (var (key, type) in Skin.Keys(kind))
                {
                    Assert.True(d.TryGetResource(key, variant, out var v), $"{kind} {variant}: no {key}");
                    Assert.True(type.IsInstanceOfType(v), $"{kind} {variant}: {key} is {v?.GetType().Name}, not {type.Name}");
                }
            // A colour is the light and dark dictionaries' own, never one only the other has.
            foreach (var variant in Variants)
            {
                var own = (ResourceDictionary)d.ThemeDictionaries[variant];
                foreach (var key in Skin.MeaningBrushes) Assert.True(own.ContainsKey(key), $"{kind} {variant}: {key} isn't its own");
            }
        }
    }

    static Color Brush(ResourceDictionary d, string key, ThemeVariant variant) =>
        d.TryGetResource(key, variant, out var v) ? ((ISolidColorBrush)v!).Color : throw new Xunit.Sdk.XunitException($"no {key}");

    /// <summary>The palette is the brand's (Controls/Syft/Syft.cs): ink and paper, coral for recording only, lilac for
    /// what's picked, sky for progress, mustard for a quote, the meshes for cards; the same in both looks.</summary>
    [AvaloniaFact]
    public void The_meanings_are_the_brands_colours()
    {
        (string Key, Color Light, Color Dark)[] want =
        [
            ("Surface", Syft.Paper, Syft.Ink), ("Ink", Syft.Ink, Syft.Paper), ("InkMuted", Syft.Muted, Color.Parse("#B4B1BC")),
            ("Accent", Syft.Ink, Syft.Paper), ("OnAccent", Syft.Paper, Syft.Ink),
            ("Recording", Syft.Coral, Syft.Coral), ("OnRecording", Syft.Ink, Syft.Ink), ("Record", Syft.Coral, Syft.Coral),
            ("Selected", Syft.Lilac, Syft.Lilac), ("OnSelected", Syft.Ink, Syft.Ink),
            ("Progress", Syft.SkyDeep, Syft.SkyDeepDark), ("ProgressTrack", Syft.MeshLight, Syft.MeshDark),
            ("Highlight", Syft.Mustard, Syft.Mustard), ("SurfaceRaised", Syft.MeshLight, Syft.MeshDark), ("SurfaceSunken", Syft.MeshLight, Syft.MeshDark),
            ("Chip", Syft.MeshLight, Syft.GhostDark), ("Border", Color.Parse("#E2DFD8"), Syft.GhostDark),
        ];
        foreach (var kind in Looks)
        {
            var d = Skin.Build(kind);
            foreach (var (key, light, dark) in want)
            {
                Assert.Equal(light, Brush(d, key, ThemeVariant.Light));
                Assert.Equal(dark, Brush(d, key, ThemeVariant.Dark));
            }
            // The names from the looks before hold the same colours: text is ink, a hairline the border, Tint the accent.
            foreach (var (old, now) in new[] { ("Fg", "Ink"), ("Fg2", "InkMuted"), ("Fg3", "InkSubtle"), ("Sep", "Line"), ("Rec", "Record"),
                         ("Tint", "Accent"), ("AccentText", "Ink"), ("Text", "Ink"), ("TextMuted", "InkMuted"), ("Border", "Line") })
                foreach (var variant in Variants)
                    Assert.Equal(Brush(d, now, variant), Brush(d, old, variant));
            // No teal left, and coral means recording only.
            foreach (var variant in Variants)
                foreach (var key in Skin.MeaningBrushes.Where(k => k is not ("Recording" or "Record")))
                {
                    var c = Brush(d, key, variant);
                    Assert.NotEqual(Syft.Coral, c);
                    Assert.False(c.R < 100 && c.G > c.R + 40 && c.B > c.R + 40, $"{kind} {variant} {key} is teal: {c}");
                }
        }
        // Each class's highlighter, in the order classes take them; a class's brush is known for which it is.
        Assert.Equal(["Yellow", "Green", "Orange", "Pink", "Blue", "Teal", "Violet", "Lime"], Skin.ClassHues);
        Assert.Equal("Orange", Skin.HueOf(Skin.ClassDot(2)));
        Assert.Equal("Yellow", Skin.HueOf(Skin.ClassDot(8)));
        Assert.Null(Skin.HueOf(Brushes.Gray));
    }

    static double Luminance(Color c)
    {
        static double Lin(byte b) => b / 255.0 <= 0.04045 ? b / 255.0 / 12.92 : Math.Pow((b / 255.0 + 0.055) / 1.055, 2.4);
        return 0.2126 * Lin(c.R) + 0.7152 * Lin(c.G) + 0.0722 * Lin(c.B);
    }

    static double Contrast(Color a, Color b)
    {
        double x = Luminance(a), y = Luminance(b);
        return (Math.Max(x, y) + 0.05) / (Math.Min(x, y) + 0.05);
    }

    /// <summary>Words read: every grey on the page and on a card, and the words on every fill and every class's
    /// highlighter, at 4.5:1 or better, light and dark.</summary>
    [AvaloniaFact]
    public void Text_on_every_surface_and_fill_reads()
    {
        (string Text, string On)[] pairs =
        [
            ("Ink", "Surface"), ("InkMuted", "Surface"), ("InkSubtle", "Surface"), ("InkMuted", "SurfaceRaised"), ("InkSubtle", "SurfaceRaised"),
            ("Ink", "Canvas"), ("InkMuted", "Canvas"), ("TextFade1", "Surface"), ("TextFade2", "Surface"), ("TextFade3", "Surface"),
            ("OnAccent", "Accent"), ("OnSelected", "Selected"), ("SelectedMuted", "Selected"), ("OnRecording", "Recording"),
            ("OnHighlight", "Highlight"), ("Ink", "Hl"), ("Ink", "Chip"), ("InkMuted", "Chip"),
            ("Ink", "Card"), ("InkMuted", "Card"), ("InkSubtle", "Card"),
            .. Skin.ClassHues.Select(h => ("OnHighlight", "Hl" + h)),
            .. Skin.ClassHues.Select(h => ("Ink", "HlQuiet" + h)),
        ];
        var failures = new List<string>();
        foreach (var kind in Looks)
        {
            var d = Skin.Build(kind);
            foreach (var variant in Variants)
                foreach (var (text, on) in pairs)
                {
                    double ratio = Contrast(Brush(d, text, variant), Brush(d, on, variant));
                    if (ratio < 4.5) failures.Add($"{kind} {variant}: {text} on {on} is {ratio:0.00}:1");
                }
        }
        Assert.Empty(failures);
    }

    /// <summary>Grey, or the brand's warm or violet-grey of a hairline, an ink or a mesh (a few steps apart at most).</summary>
    static bool Neutral(Color c) => Math.Abs(c.R - c.G) <= 8 && Math.Abs(c.G - c.B) <= 16 && Math.Abs(c.R - c.B) <= 18;

    /// <summary>No coloured glow anywhere: every shadow token, in both looks, light and dark, is grey (black, white, a
    /// hairline's grey), so no colour bleeds round a button, a row or a dot.</summary>
    [AvaloniaFact]
    public void Every_shadow_is_grey()
    {
        var coloured = new List<string>();
        foreach (var kind in Looks)
        {
            var d = Skin.Build(kind);
            foreach (var variant in Variants)
                foreach (var (key, type) in Skin.Keys(kind))
                {
                    if (type != typeof(BoxShadows)) continue;
                    Assert.True(d.TryGetResource(key, variant, out var v) && v is BoxShadows, $"{kind} {variant}: no {key}");
                    foreach (var s in (BoxShadows)v!)
                        if (!Neutral(s.Color)) coloured.Add($"{kind} {variant} {key}: {s.Color}");
                }
        }
        Assert.Empty(coloured);
    }

    [GeneratedRegex(@"(?:BoxShadow=""|Property=""BoxShadow""\s+Value="")([^""{]+)""")] private static partial Regex LiteralShadow();

    /// <summary>...and no view writes a coloured shadow of its own either.</summary>
    [Fact]
    public void No_view_draws_a_coloured_shadow()
    {
        var coloured = new List<string>();
        var files = Directory.GetFiles(Path.Combine(Source(), "Views"), "*.axaml").Append(Path.Combine(Source(), "Styles.axaml"));
        foreach (var file in files)
            foreach (Match m in LiteralShadow().Matches(File.ReadAllText(file)))
            {
                string value = m.Groups[1].Value.Trim();
                if (value == "none") continue;
                foreach (var s in BoxShadows.Parse(value))
                    if (!Neutral(s.Color)) coloured.Add($"{Path.GetFileName(file)}: {value}");
            }
        Assert.Empty(coloured);
    }

    static string Source([CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", "src", "StudyStash.App"));

    [GeneratedRegex(@"DynamicResource\s+(\w+)")] private static partial Regex Dynamic();
    [GeneratedRegex(@"GetResourceObservable\(""(\w+)""")] private static partial Regex Observed();
    [GeneratedRegex(@"(?:Fades\.Under\(\w+, |Bind\(\w+, \w+\.\w+Property, |Text\()""(\w+)""")] private static partial Regex Bound();
    [GeneratedRegex(@"\b[Mm]ac \? ""(\w+)"" : ""(\w+)""")] private static partial Regex ByLook();
    /// <summary>A view for one look only: MacLibrary, WinSetup (not WindowHeader, which is for both).</summary>
    [GeneratedRegex(@"^(Mac|Win)[A-Z]")] private static partial Regex OneLook();

    /// <summary>Every token the views, the styles and the controls ask for, and the look it's asked in (null: the
    /// file's own look, or either one for a file both looks share).</summary>
    static IEnumerable<(string File, string Key, SkinKind? Look)> Asks(string src)
    {
        var markup = Directory.GetFiles(Path.Combine(src, "Views"), "*.axaml").Append(Path.Combine(src, "Styles.axaml"));
        foreach (var file in markup)
            foreach (Match m in Dynamic().Matches(File.ReadAllText(file))) yield return (file, m.Groups[1].Value, null);
        var code = Directory.GetFiles(Path.Combine(src, "Views"), "*.cs").Concat(Directory.GetFiles(Path.Combine(src, "Controls"), "*.cs"));
        foreach (var file in code)
        {
            string text = File.ReadAllText(file);
            foreach (Match m in Observed().Matches(text)) yield return (file, m.Groups[1].Value, null);
            foreach (Match m in Bound().Matches(text)) yield return (file, m.Groups[1].Value, null);
            foreach (Match m in ByLook().Matches(text))
            {
                yield return (file, m.Groups[1].Value, SkinKind.Mac);
                yield return (file, m.Groups[2].Value, SkinKind.Win);
            }
        }
    }

    [AvaloniaFact]
    public void Every_token_a_view_asks_for_exists()
    {
        string src = Source();
        Assert.True(File.Exists(Path.Combine(src, "Skin.cs")), $"no app source at {src}");
        var mac = Skin.Build(SkinKind.Mac);
        var win = Skin.Build(SkinKind.Win);
        bool In(ResourceDictionary d, string key) => Variants.All(v => d.TryGetResource(key, v, out _));
        var asks = Asks(src).ToList();
        Assert.Contains(asks, a => a.Key == "Accent");
        Assert.Contains(asks, a => a.Key == "TextFont" && a.Look == SkinKind.Win);
        var missing = new List<string>();
        foreach (var (file, key, look) in asks)
        {
            string name = Path.GetFileName(file);
            SkinKind? own = look ?? (OneLook().IsMatch(name) ? (name.StartsWith("Mac") ? SkinKind.Mac : SkinKind.Win) : null);
            bool there = own switch
            {
                SkinKind.Mac => In(mac, key),
                SkinKind.Win => In(win, key),
                _ => In(mac, key) || In(win, key),
            };
            if (!there) missing.Add($"{name}: {key}{(own is { } k ? $" ({k})" : "")}");
        }
        Assert.Empty(missing.Distinct());
    }

    /// <summary>The status dot and the setup's result line take their colours from the look, by class.</summary>
    [AvaloniaFact]
    public void Status_colours_follow_the_look()
    {
        var app = (App)Application.Current!;
        foreach (var kind in Looks)
        {
            app.UseSkin(kind);
            var d = Skin.Build(kind);
            Color Token(string key) => d.TryGetResource(key, ThemeVariant.Light, out var v) ? ((ISolidColorBrush)v!).Color : default;
            var good = new Avalonia.Controls.Shapes.Ellipse { Classes = { "status" } };
            var warn = new Avalonia.Controls.Shapes.Ellipse { Classes = { "status", "warn" } };
            var worked = new TextBlock { Classes = { "result" } };
            var failed = new TextBlock { Classes = { "result", "bad" } };
            var window = new Window { RequestedThemeVariant = ThemeVariant.Light, Content = new StackPanel { Children = { good, warn, worked, failed } } };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(Token("Good"), ((ISolidColorBrush)good.Fill!).Color);
            Assert.Equal(Token("Warn"), ((ISolidColorBrush)warn.Fill!).Color);
            Assert.Equal(Token("Ok"), ((ISolidColorBrush)worked.Foreground!).Color);
            Assert.Equal(Token("Warn"), ((ISolidColorBrush)failed.Foreground!).Color);
            window.Close();
        }
    }

    /// <summary>An app.json that names a colour theme (any of the twenty there were, or one never heard of) still loads,
    /// and the app shows the Syft look.</summary>
    [AvaloniaFact]
    public void A_saved_colour_theme_loads_and_gets_the_Syft_look()
    {
        string home = Path.Combine(Path.GetTempPath(), "studystash-theme-" + Guid.NewGuid().ToString("N"));
        try
        {
            File.WriteAllText(Path.Combine(Directory.CreateDirectory(home).FullName, "app.json"), "{\"setup_done\": true, \"theme\": \"Plum\"}");
            Assert.Equal("Plum", AppSettings.Load(home).Theme);
            foreach (var name in new[] { "Plum", "Lagoon", "Neon", "", null })
                Assert.Same(ColourThemes.Default, ColourThemes.Find(name));
            Skin.UseTheme(ColourThemes.Find("Plum"));
            Assert.Equal(Syft.Ink, Brush(Skin.Build(SkinKind.Mac), "Accent", ThemeVariant.Light));
            Assert.Equal(new[] { "Syft" }, ColourThemes.All.Select(t => t.Name));
        }
        finally
        {
            if (Directory.Exists(home)) Directory.Delete(home, recursive: true);
        }
    }

    /// <summary>Light and dark repaint what's open (DynamicResources): the main button's ink turns paper, and the fades
    /// follow the page.</summary>
    [AvaloniaFact]
    public void Light_and_dark_swap_live()
    {
        var app = (App)Application.Current!;
        app.UseSkin(SkinKind.Mac);
        var swatch = new Border { Width = 20, Height = 20, [!Border.BackgroundProperty] = new DynamicResourceExtension("Accent") };
        var fade = new Border { Width = 20, Height = 20 };
        Fades.Under(fade, "Surface", 0.7);
        var window = new Window { Width = 100, Height = 100, RequestedThemeVariant = ThemeVariant.Light, Content = new StackPanel { Children = { swatch, fade } } };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(Syft.Ink, ((ISolidColorBrush)swatch.Background!).Color);
            Assert.Equal(Skin.Light.Surface, ((LinearGradientBrush)fade.Background!).GradientStops[1].Color);
            window.RequestedThemeVariant = ThemeVariant.Dark;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(Syft.Paper, ((ISolidColorBrush)swatch.Background!).Color);
            Assert.Equal(Skin.Dark.Surface, ((LinearGradientBrush)fade.Background!).GradientStops[1].Color);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>Settings → Appearance offers light, dark or the system's, and no colour themes.</summary>
    [AvaloniaFact]
    public void Appearance_offers_light_dark_or_system_and_no_colour_themes()
    {
        var app = (App)Application.Current!;
        app.UseSkin(SkinKind.Mac);
        string home = Path.Combine(Path.GetTempPath(), "studystash-appearance-" + Guid.NewGuid().ToString("N"));
        var host = new AppHost(home);
        var model = SettingsModel.Make(host);
        try
        {
            model.Section = "Appearance";
            var view = new SettingsView { DataContext = model };
            var window = new Window { Width = 900, Height = 900, Content = view };
            window.Show();
            try
            {
                Dispatcher.UIThread.RunJobs();
                var shown = view.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible).ToList();
                Assert.DoesNotContain(shown, b => b.DataContext is ThemeSwatch);
                var words = view.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text).ToList();
                Assert.Contains("Match system", words);
                Assert.Contains("Light", words);
                Assert.Contains("Dark", words);
                Assert.DoesNotContain("Colour theme", words);
            }
            finally
            {
                window.Close();
            }
        }
        finally
        {
            model.Dispose();
            host.Dispose();
            if (Directory.Exists(home)) Directory.Delete(home, recursive: true);
        }
    }
}
