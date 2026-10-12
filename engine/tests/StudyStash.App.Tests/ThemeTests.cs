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
using StudyStash.App.Views;

namespace StudyStash.App.Tests;

/// <summary>The look's tokens, and the colour themes that give it its accent: the accent worked out in OKLCH and
/// drawn the way Chrome draws it, every token there for every theme, look and variant, the theme kept in app.json and
/// swapped while the app runs. The surfaces and inks are the design's own (tokens.css), the same whatever the theme.</summary>
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

    /// <summary>What a colour theme still decides: the accent and the status colours. (ThemeExpected's other rows are
    /// surfaces that leant to the theme in the look before; now they're the design's own greys.)</summary>
    static readonly HashSet<string> FromTheme = ["Accent", "AccentText", "Tint", "AccentTint", "IcInfo", "Ok", "Warn", "Good"];

    /// <summary>The theme's accent in the skin is worked out as before: its formulas, as Chrome draws them.</summary>
    [AvaloniaFact]
    public void Every_theme_accent_matches_its_formula()
    {
        var built = new Dictionary<(string, SkinKind), ResourceDictionary>();
        foreach (var row in ThemeExpected.Rows.Where(r => FromTheme.Contains(r.Token)))
        {
            var theme = ColourThemes.Find(row.Theme);
            Assert.Equal(row.Theme, theme.Name);
            var kind = row.Look.StartsWith("mac") ? SkinKind.Mac : SkinKind.Win;
            var variant = row.Look.EndsWith('D') ? ThemeVariant.Dark : ThemeVariant.Light;
            if (!built.TryGetValue((row.Theme, kind), out var d)) built[(row.Theme, kind)] = d = Skin.Build(kind, theme);
            Assert.True(d.TryGetResource(row.Token, variant, out var v), $"{row.Theme} {row.Look}: no {row.Token}");
            Color got = ((ISolidColorBrush)v!).Color;
            string what = $"{row.Theme} {row.Look} {row.Token}: {got}, the design's {row.Rgb} at {row.Alpha}";
            Assert.True(Close(got, Color.Parse(row.Rgb)), what);
            Assert.True(got.A == row.Alpha, what);
        }
    }

    [AvaloniaFact]
    public void Every_theme_builds_every_token()
    {
        foreach (var theme in ColourThemes.All)
            foreach (var kind in Looks)
            {
                var d = Skin.Build(kind, theme);
                foreach (var variant in Variants)
                    foreach (var (key, type) in Skin.Keys(kind))
                    {
                        Assert.True(d.TryGetResource(key, variant, out var v), $"{theme.Name} {kind} {variant}: no {key}");
                        Assert.True(type.IsInstanceOfType(v), $"{theme.Name} {kind} {variant}: {key} is {v?.GetType().Name}, not {type.Name}");
                    }
            }
    }

    /// <summary>Grey, or the design's own blue-grey of a hairline or an ink (their blue runs a few steps over their red).</summary>
    static bool Neutral(Color c) => Math.Abs(c.R - c.G) <= 8 && Math.Abs(c.G - c.B) <= 10 && Math.Abs(c.R - c.B) <= 18;

    /// <summary>The surfaces, inks and reds are the design's own values, the same in both looks and whatever the theme.</summary>
    [AvaloniaFact]
    public void The_designs_tokens_are_its_own_in_both_looks_whatever_the_theme()
    {
        (string Key, string Light, string Dark)[] design =
        [
            ("Canvas", "#E9EBEE", "#0E0F11"), ("Surface", "#FFFFFF", "#191A1D"), ("SurfaceSunken", "#F5F6F7", "#202226"), ("SurfaceToday", "#FFFFFF", "#2A2C31"),
            ("Line", "#DDE0E4", "#2D3035"), ("Ink", "#1D1F23", "#ECEEF1"), ("InkMuted", "#565C66", "#A4A9B1"), ("InkSubtle", "#626973", "#8D939C"),
            ("Record", "#D42A1A", "#FF5A47"), ("OnRecord", "#FFFFFF", "#1A0603"), ("Late", "#B3261E", "#FF8A80"),
            ("HlYellow", "#FFE46B", "#5A4C00"), ("HlTeal", "#A6EBE1", "#134F48"), ("SwatchYellow", "#FFE46B", "#E9CF4A"), ("SwatchLime", "#E2F28C", "#C4DC55"),
        ];
        foreach (var theme in new[] { ColourThemes.Default, ColourThemes.Find("Plum") })
            foreach (var kind in Looks)
            {
                var d = Skin.Build(kind, theme);
                foreach (var (key, light, dark) in design)
                {
                    Assert.True(d.TryGetResource(key, ThemeVariant.Light, out var l), $"{kind}: no {key}");
                    Assert.True(d.TryGetResource(key, ThemeVariant.Dark, out var dk), $"{kind}: no {key} in dark");
                    Assert.Equal(Color.Parse(light), ((ISolidColorBrush)l!).Color);
                    Assert.Equal(Color.Parse(dark), ((ISolidColorBrush)dk!).Color);
                }
                // The names from the look before hold the same colours: text is ink, a window the page's white.
                foreach (var (old, now) in new[] { ("Fg", "Ink"), ("Fg2", "InkMuted"), ("Fg3", "InkSubtle"), ("Sep", "Line"), ("Rec", "Record") })
                    foreach (var variant in Variants)
                    {
                        d.TryGetResource(old, variant, out var was);
                        d.TryGetResource(now, variant, out var isNow);
                        Assert.Equal(((ISolidColorBrush)isNow!).Color, ((ISolidColorBrush)was!).Color);
                    }
            }
        // Each class's highlighter, in the order classes take them; a class's brush is known for which it is.
        Assert.Equal(["Yellow", "Green", "Orange", "Pink", "Blue", "Teal", "Violet", "Lime"], Skin.ClassHues);
        Assert.Equal("Orange", Skin.HueOf(Skin.ClassDot(2)));
        Assert.Equal("Yellow", Skin.HueOf(Skin.ClassDot(8)));
        Assert.Null(Skin.HueOf(Brushes.Gray));
    }

    /// <summary>No coloured glow anywhere: every shadow token, in both looks, light and dark, for every colour theme, is
    /// grey (black, white, a hairline's grey), so the accent never bleeds round a button, a row or a dot. The one ring
    /// with colour in it is the red round a recording.</summary>
    [AvaloniaFact]
    public void Every_shadow_is_grey()
    {
        var coloured = new List<string>();
        foreach (var theme in ColourThemes.All)
            foreach (var kind in Looks)
            {
                var d = Skin.Build(kind, theme);
                foreach (var variant in Variants)
                    foreach (var (key, type) in Skin.Keys(kind))
                    {
                        if (type != typeof(BoxShadows) || key == "RecordRing") continue;
                        Assert.True(d.TryGetResource(key, variant, out var v) && v is BoxShadows, $"{theme.Name} {kind} {variant}: no {key}");
                        foreach (var s in (BoxShadows)v!)
                            if (!Neutral(s.Color)) coloured.Add($"{theme.Name} {kind} {variant} {key}: {s.Color}");
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

    /// <summary>A light accent (Chalkboard, Highlighter, Lilac, Peach, Mint) takes dark ink; so does every accent on dark Windows.</summary>
    [AvaloniaFact]
    public void Text_on_the_accent_reads()
    {
        var ink = Color.Parse("#16171A");
        foreach (var theme in ColourThemes.All)
        {
            Color OnAccent(SkinKind kind, ThemeVariant variant) =>
                Skin.Build(kind, theme).TryGetResource("OnAccent", variant, out var v) ? ((ISolidColorBrush)v!).Color : default;
            var light = theme.IsLight ? ink : Colors.White;
            Assert.Equal(light, OnAccent(SkinKind.Mac, ThemeVariant.Light));
            Assert.Equal(light, OnAccent(SkinKind.Mac, ThemeVariant.Dark));
            Assert.Equal(light, OnAccent(SkinKind.Win, ThemeVariant.Light));
            Assert.Equal(ink, OnAccent(SkinKind.Win, ThemeVariant.Dark));
        }
        Assert.Equal(new[] { "Chalkboard", "Highlighter", "Lilac", "Peach", "Mint" }, ColourThemes.All.Where(t => t.IsLight).Select(t => t.Name));
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
        var mac = Skin.Build(SkinKind.Mac, ColourThemes.Default);
        var win = Skin.Build(SkinKind.Win, ColourThemes.Default);
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

    [Fact]
    public void Unknown_theme_is_Lagoon()
    {
        Assert.Equal("Lagoon", ColourThemes.Default.Name);
        Assert.Same(ColourThemes.Default, ColourThemes.Find("Neon"));
        Assert.Same(ColourThemes.Default, ColourThemes.Find(""));
        Assert.Same(ColourThemes.Default, ColourThemes.Find(null));
        Assert.Equal("Original red", ColourThemes.Find("original red").Name);
        Assert.Equal(new[] { "Lagoon", "Library", "Blueprint", "Marmalade", "Plum", "Chalkboard", "Highlighter", "Terracotta", "Graphite", "Original red",
                "Sky", "Midnight", "Violet", "Lilac", "Bubblegum", "Peach", "Mocha", "Moss", "Clover", "Mint" },
            ColourThemes.All.Select(t => t.Name));
    }

    [Fact]
    public void The_theme_is_kept_in_app_json()
    {
        string home = Path.Combine(Path.GetTempPath(), "studystash-theme-" + Guid.NewGuid().ToString("N"));
        try
        {
            Assert.Equal("Lagoon", AppSettings.Load(home).Theme);
            new AppSettings { Theme = "Plum" }.Save(home);
            Assert.Contains("\"theme\": \"Plum\"", File.ReadAllText(AppSettings.PathIn(home)));
            Assert.Equal("Plum", AppSettings.Load(home).Theme);
            // An app.json from before colour themes: Lagoon.
            File.WriteAllText(AppSettings.PathIn(home), "{\"setup_done\": true}");
            Assert.Equal("Lagoon", AppSettings.Load(home).Theme);
        }
        finally
        {
            if (Directory.Exists(home)) Directory.Delete(home, recursive: true);
        }
    }

    /// <summary>The status dot and the setup's result line take their colours from the look, by class.</summary>
    [AvaloniaFact]
    public void Status_colours_follow_the_look()
    {
        var app = (App)Application.Current!;
        Skin.UseTheme(ColourThemes.Default);
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

    /// <summary>Picking a theme repaints what's open (DynamicResources); light and dark repaint the fades too.</summary>
    [AvaloniaFact]
    public void A_theme_swaps_live()
    {
        var app = (App)Application.Current!;
        app.UseSkin(SkinKind.Mac);
        Skin.UseTheme(ColourThemes.Default);
        var swatch = new Border { Width = 20, Height = 20, [!Border.BackgroundProperty] = new DynamicResourceExtension("Accent") };
        var fade = new Border { Width = 20, Height = 20 };
        Fades.Under(fade, "Surface", 0.7);
        var window = new Window { Width = 100, Height = 100, RequestedThemeVariant = ThemeVariant.Light, Content = new StackPanel { Children = { swatch, fade } } };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var plum = ColourThemes.Find("Plum");
            Assert.Equal(ColourThemes.Default.O(0.58, 0.12), ((ISolidColorBrush)swatch.Background!).Color);
            Assert.Equal(Skin.Light.Surface, ((LinearGradientBrush)fade.Background!).GradientStops[1].Color);

            // The accent follows the theme; the page under the fade is the design's white, whatever the theme.
            Skin.UseTheme(plum);
            Dispatcher.UIThread.RunJobs();
            Assert.Same(plum, Skin.Theme);
            Assert.Equal(plum.O(plum.L, plum.C), ((ISolidColorBrush)swatch.Background!).Color);
            Assert.Equal(Skin.Light.Surface, ((LinearGradientBrush)fade.Background!).GradientStops[1].Color);

            window.RequestedThemeVariant = ThemeVariant.Dark;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(plum.O(Math.Min(plum.L + 0.08, 0.72), plum.C), ((ISolidColorBrush)swatch.Background!).Color);
            Assert.Equal(Skin.Dark.Surface, ((LinearGradientBrush)fade.Background!).GradientStops[1].Color);
        }
        finally
        {
            window.Close();
            Skin.UseTheme(ColourThemes.Default);
        }
    }

    /// <summary>Settings → Appearance: picking a swatch swaps the live accent and saves the choice, no restart.</summary>
    [AvaloniaFact]
    public void Picking_a_theme_in_settings_saves_it_and_repaints()
    {
        var app = (App)Application.Current!;
        app.UseSkin(SkinKind.Mac);
        Skin.UseTheme(ColourThemes.Default);
        string home = Path.Combine(Path.GetTempPath(), "studystash-picktheme-" + Guid.NewGuid().ToString("N"));
        var host = new AppHost(home);
        var model = SettingsModel.Make(host);
        try
        {
            var swatch = new Border { Width = 20, Height = 20, [!Border.BackgroundProperty] = new DynamicResourceExtension("Accent") };
            var window = new Window { Width = 40, Height = 40, RequestedThemeVariant = ThemeVariant.Light, Content = swatch };
            window.Show();
            try
            {
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(ColourThemes.Default.O(0.58, 0.12), ((ISolidColorBrush)swatch.Background!).Color);

                model.PickThemeCommand.Execute("Plum");
                Dispatcher.UIThread.RunJobs();
                var plum = ColourThemes.Find("Plum");
                Assert.Same(plum, Skin.Theme);
                Assert.Equal("Plum", model.ColourTheme);
                Assert.True(model.Themes.Single(t => t.Name == "Plum").Chosen);
                Assert.False(model.Themes.Single(t => t.Name == "Lagoon").Chosen);
                Assert.Equal(plum.O(plum.L, plum.C), ((ISolidColorBrush)swatch.Background!).Color);
                Assert.Contains("\"theme\": \"Plum\"", File.ReadAllText(AppSettings.PathIn(home)));
                Assert.Equal("Plum", AppSettings.Load(home).Theme);
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
            Skin.UseTheme(ColourThemes.Default);
            if (Directory.Exists(home)) Directory.Delete(home, recursive: true);
        }
    }
}
