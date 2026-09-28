using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.TextFormatting;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StudyStash.App.Controls;
using StudyStash.App.Controls.Rich;
using StudyStash.App.Views;
using StudyStash.App.Windows;
using StudyStash.Core.Rich;

namespace StudyStash.App.Tests;

/// <summary>
/// The notes' rich content in the Windows look, light and dark, at 100% and 150% display scaling: formulas inline and
/// on their own line, every kind of diagram (a ring, a tree, a chart with a group, a decision, a drawing, and the calm
/// card for one it doesn't draw), a table, code, the words and signs lectures use, and a whole long lecture. CI draws
/// these on a real Windows, with its own fonts, and keeps them (<c>richwin-*.png</c>), so what a student on Windows
/// sees can be looked at beside the Mac's. Each also checks what it drew: every formula typeset, every chart laid
/// out, and the picture the size the scaling asks for.
/// </summary>
public class RichWindowsShots
{
    static readonly ThemeVariant[] Themes = [ThemeVariant.Light, ThemeVariant.Dark];
    static readonly double[] Scales = [1, 1.5];

    static RichWindowsShots() => Environment.SetEnvironmentVariable("STUDYSTASH_STILL", "1");

    static string Fence(string info, string source) => $"```{info}\n{source.TrimEnd()}\n```";

    /// <summary>Words and signs a lecture's notes use, in the reading font: arrows (the Return key's ↩ too), ticks,
    /// Greek, comparisons, degrees, fractions, and sub- and superscripts, none of which may come out as a box.</summary>
    public const string Signs = "→ ← ↔ ⇒ ↩ ✓ ✗ • α β γ δ Δ θ λ μ π σ Σ Ω ≤ ≥ ≠ ≈ ± × ÷ ° ∞ √ ∆ ½ ¼ CO₂ H₂O x² x³ 10⁻³ Na⁺ Cl⁻ 37 °C";

    /// <summary>Carbonic acid, with the subscripts and charges a chemistry chart writes in its boxes.</summary>
    const string Buffer = """
        flowchart LR
          A["CO₂ + H₂O"]:::blue --> B["H₂CO₃"] --> C["H⁺ + HCO₃⁻"]:::amber
          C -.->|kidneys keep HCO₃⁻| A
        """;

    /// <summary>A drawing with the same signs in its own words (Svg.Skia sets them, not Avalonia).</summary>
    const string SignsDrawing = """
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 520 150" font-size="14">
          <title>Forces on a block</title>
          <rect x="30" y="40" width="120" height="70" rx="10" fill="#E8F0FE" stroke="#1A73E8" stroke-width="2"/>
          <text x="90" y="80" text-anchor="middle" fill="#1D1D1F" font-weight="600">m = 2 kg</text>
          <line x1="170" y1="75" x2="300" y2="75" stroke="#1D1D1F" stroke-width="2"/>
          <text x="235" y="65" text-anchor="middle" fill="#6E6E73" font-size="12">F → 9.8 N</text>
          <rect x="320" y="40" width="170" height="70" rx="10" fill="#FCE8E6" stroke="#D93025" stroke-width="2"/>
          <text x="405" y="72" text-anchor="middle" fill="#1D1D1F" font-weight="600">θ ≈ 30° ✓</text>
          <text x="405" y="92" text-anchor="middle" fill="#6E6E73" font-size="12">μ ≤ 0.5 · CO₂ · x²</text>
        </svg>
        """;

    /// <summary>Formulas, the signs, a table and code: everything a note writes in lines of type.</summary>
    public static string TextNotes { get; } = string.Join("\n\n",
        "## Formulas",
        RichDemo.CardiacOutputInline,
        """Inline Greek and scripts sit on the line too: $\alpha + \beta \le \gamma$, $x_1^2 + x_2^2$, and $\Delta G = \Delta H - T\Delta S$.""",
        RichDemo.DoseFormula,
        RichDemo.CasesFormula,
        RichDemo.ChemFormula,
        "## Signs in the words",
        Signs,
        "- A bullet with them: pH ≈ 7.4 → acidosis below 7.35 ✓",
        "## A table",
        """
        | Measure | Normal | Worked out as |
        |---|---|---|
        | Cardiac output | 4–8 L/min | $\text{HR} \times \text{SV}$ |
        | Mean arterial pressure | 70–100 mmHg | $\text{DBP} + \frac{1}{3}(\text{SBP} - \text{DBP})$ |
        | Shock index | below 0.7 | HR ÷ SBP |
        """,
        "## Code",
        Fence("python", """
            def mean_arterial_pressure(sbp, dbp):
                # MAP ≈ DBP + ⅓(SBP − DBP), in mmHg
                return dbp + (sbp - dbp) / 3
            """));

    /// <summary>Every kind of diagram a note draws, each under its heading, and the card for one it doesn't.</summary>
    public static string DiagramNotes { get; } = string.Join("\n\n",
        "## A ring: the cardiac cycle",
        Fence("mermaid", RichDemo.CardiacCycle),
        "## A tree: types of shock",
        Fence("mermaid", RichDemo.TypesOfShock),
        "## A chart with a group: the nursing process",
        Fence("mermaid", RichDemo.NursingProcess),
        "## A decision: pain, assess and act",
        Fence("mermaid", RichDemo.PainReassess),
        "## Subscripts and charges in the boxes",
        Fence("mermaid", Buffer),
        "## A drawing",
        Fence("svg", RichDemo.FourChambers),
        Fence("svg", SignsDrawing),
        "## A sequence diagram, which isn't drawn",
        Fence("mermaid", RichDemo.PainConversation));

    /// <summary>
    /// Draws <paramref name="build"/> in the Windows look on the design's ground at <paramref name="scale"/> (a
    /// display set to 150% draws each point as one and a half pixels), saves it as
    /// <c>richwin-&lt;name&gt;-&lt;100|150&gt;-&lt;light|dark&gt;.png</c>, and hands back the window, still open,
    /// for the caller to check and close.
    /// </summary>
    static Window Take(string name, ThemeVariant variant, double scale, Func<Control> build, Size size, Action<Window>? before = null)
    {
        Skin.UseTheme(ColourThemes.Default);
        ((App)Application.Current!).UseSkin(SkinKind.Win);
        var content = build();
        content.HorizontalAlignment = HorizontalAlignment.Left;
        content.VerticalAlignment = VerticalAlignment.Top;
        var ground = new Border { Padding = new Thickness(64), Child = content };
        ground.Bind(Border.BackgroundProperty, ground.GetResourceObservable("Ground"));
        var window = new Window { Width = size.Width, Height = size.Height, RequestedThemeVariant = variant, Content = ground };
        Look.Apply(window);
        window.SetRenderScaling(scale);
        window.Show();
        DiagramsReady.Wait(window);
        before?.Invoke(window);
        var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("nothing rendered");
        Assert.Equal(new PixelSize((int)Math.Round(size.Width * scale), (int)Math.Round(size.Height * scale)), frame.PixelSize);
        string file = $"richwin-{name}-{scale * 100:0}-{(variant == ThemeVariant.Dark ? "dark" : "light")}.png";
        using (var stream = File.Create(Path.Combine(Shot.Dir, file))) frame.Save(stream, PngBitmapEncoderOptions.Default);
        return window;
    }

    /// <summary>Every formula in the window typeset, and every chart laid out and drawn (none still waiting, none
    /// failed).</summary>
    static void AllDrawn(Window window)
    {
        var formulas = window.GetVisualDescendants().OfType<MathView>().ToList();
        Assert.All(formulas, m => Assert.Null(m.ErrorMessage));
        Assert.All(formulas, m => Assert.True(m.Bounds.Width > 0 && m.Bounds.Height > 0, $"{m.Latex}: {m.Bounds}"));
        var charts = window.GetVisualDescendants().OfType<DiagramView>().ToList();
        Assert.All(charts, d => Assert.NotNull(d.Scene));
        var drawings = window.GetVisualDescendants().OfType<SvgView>().ToList();
        Assert.All(drawings, s => Assert.True(s.Bounds.Width > 0 && s.Drawing?.Svg is not null, s.Title));
    }

    [AvaloniaFact]
    public void Formulas_signs_table_and_code()
    {
        foreach (double scale in Scales)
            foreach (var t in Themes)
            {
                var window = Take("text", t, scale, () => RichShots.NotePage(SkinKind.Win, TextNotes), new Size(876, 1500));
                AllDrawn(window);
                Assert.True(window.GetVisualDescendants().OfType<MathView>().Count() >= 8);
                window.Close();
            }
    }

    [AvaloniaFact]
    public void Every_kind_of_diagram()
    {
        foreach (double scale in Scales)
            foreach (var t in Themes)
            {
                var window = Take("diagrams", t, scale, () => RichShots.NotePage(SkinKind.Win, DiagramNotes), new Size(876, 3600));
                AllDrawn(window);
                Assert.Equal(5, window.GetVisualDescendants().OfType<DiagramView>().Count());
                Assert.Equal(2, window.GetVisualDescendants().OfType<SvgView>().Count());
                Assert.Single(window.GetVisualDescendants().OfType<DiagramCard>());
                window.Close();
            }
    }

    /// <summary>The demo lecture's notes whole: a long note, formulas and diagrams among its sections.</summary>
    [AvaloniaFact]
    public void Long_lecture()
    {
        foreach (double scale in Scales)
            foreach (var t in Themes)
            {
                var window = Take("lecture", t, scale, () => RichShots.NotePage(SkinKind.Win, RichDemo.CardiacLecture), new Size(876, 3300));
                AllDrawn(window);
                window.Close();
            }
    }

    /// <summary>The full Windows app open on the lecture, scrolled to its diagrams.</summary>
    [AvaloniaFact]
    public void Full_app()
    {
        foreach (double scale in Scales)
            foreach (var t in Themes)
            {
                var window = Take("app", t, scale, () => new WinLibrary { DataContext = RichShots.CardiacLibrary(), Width = 1280, Height = 800 },
                    new Size(1280 + 128, 800 + 128), RichShots.ScrollToDiagrams);
                AllDrawn(window);
                window.Close();
            }
    }

    /// <summary>A diagram opened larger, in the window the app opens for it: the decision chart and the four chambers.</summary>
    [AvaloniaFact]
    public void Opened_larger()
    {
        foreach (double scale in Scales)
            foreach (var t in Themes)
            {
                foreach (var (kind, request) in new[]
                {
                    ("chart", new OpenDiagramEventArgs(new Border()) { Title = "Pain: assess, act, reassess", Chart = Flowchart.Parse(RichDemo.PainReassess) }),
                    ("drawing", new OpenDiagramEventArgs(new Border()) { Title = "The four chambers of the heart", Svg = RichDemo.FourChambers }),
                })
                {
                    var (w, h) = DiagramWindow.Size(new Size(560, 420), new Size(1440, 900));
                    var window = Take($"larger-{kind}", t, scale, () => new Border { Width = w, Height = h, Child = DiagramWindow.Content(request) }, new Size(w + 128, h + 128));
                    AllDrawn(window);
                    window.Close();
                }
            }
    }

    /// <summary>
    /// At 125%, 150% and 175% a formula, a chart and a drawing draw that many times as many pixels across as at 100%:
    /// the same size on the screen, drawn sharper, never a small picture stretched. The ink is found in the pictures
    /// themselves.
    /// </summary>
    [AvaloniaFact]
    public void At_125_150_and_175_percent_they_draw_that_much_larger_in_pixels()
    {
        foreach (var (what, build) in new (string, Func<Control>)[]
        {
            ("formula", () => new NoteView { Markdown = RichDemo.DoseFormula, Width = 640 }),
            ("chart", () => new DiagramView { Chart = Flowchart.Parse(RichDemo.PainReassess) }),
            ("drawing", () => new SvgView { Source = RichDemo.FourChambers }),
        })
        {
            var ink = new Dictionary<double, PixelRect>();
            foreach (double scale in new[] { 1, 1.25, 1.5, 1.75 })
            {
                Skin.UseTheme(ColourThemes.Default);
                ((App)Application.Current!).UseSkin(SkinKind.Win);
                var content = build();
                content.HorizontalAlignment = HorizontalAlignment.Left;
                content.VerticalAlignment = VerticalAlignment.Top;
                var window = new Window { Width = 760, Height = 700, RequestedThemeVariant = ThemeVariant.Light, Content = content, Background = Brushes.White };
                Look.Apply(window);
                window.SetRenderScaling(scale);
                window.Show();
                DiagramsReady.Wait(window);
                ink[scale] = Ink(window.CaptureRenderedFrame()!);
                window.Close();
            }
            var small = ink[1];
            Assert.True(small.Width > 40 && small.Height > 20, $"{what}: hardly anything drawn at 100% ({small})");
            foreach (double scale in new[] { 1.25, 1.5, 1.75 })
            {
                Assert.InRange(ink[scale].Width / (double)small.Width, scale - 0.05, scale + 0.05);
                Assert.InRange(ink[scale].Height / (double)small.Height, scale - 0.05, scale + 0.05);
            }
        }
    }

    /// <summary>
    /// The words and signs lectures use, shaped in the Windows look's reading font as the notes shape them: every one
    /// finds a real glyph in some font (no box), and on Windows none is drawn as a colour emoji. The fonts that drew
    /// them are written beside the pictures, for the record.
    /// </summary>
    [AvaloniaFact]
    public void Every_sign_finds_a_glyph_in_the_windows_look()
    {
        Skin.UseTheme(ColourThemes.Default);
        ((App)Application.Current!).UseSkin(SkinKind.Win);
        var family = (FontFamily)Application.Current!.FindResource("TextFont")!;
        var used = new List<string>();
        foreach (var weight in new[] { FontWeight.Normal, FontWeight.SemiBold })
        {
            using var layout = new TextLayout(Signs, new Typeface(family, FontStyle.Normal, weight), 15, Brushes.Black);
            foreach (var run in layout.TextLines.SelectMany(l => l.TextRuns).OfType<ShapedTextRun>())
            {
                string text = run.Text.ToString();
                if (string.IsNullOrWhiteSpace(text)) continue;
                var glyphs = run.GlyphRun.GlyphInfos;
                string face = run.GlyphRun.GlyphTypeface.FamilyName;
                used.Add($"{weight}: {face}: {text}");
                // Only a computer with no fonts at all (a bare Linux container) has nothing to draw them with.
                if (OperatingSystem.IsLinux()) continue;
                Assert.True(glyphs.All(g => g.GlyphIndex != 0), $"'{text}' came out as boxes in {face}");
                if (OperatingSystem.IsWindows()) Assert.NotEqual("Segoe UI Emoji", face);
            }
        }
        File.WriteAllLines(Path.Combine(Shot.Dir, "richwin-signs-fonts.txt"), used);
    }

    /// <summary>The smallest rectangle holding every pixel that isn't the white ground.</summary>
    static PixelRect Ink(Bitmap frame)
    {
        int w = frame.PixelSize.Width, h = frame.PixelSize.Height;
        var pixels = new byte[w * h * 4];
        var pinned = System.Runtime.InteropServices.GCHandle.Alloc(pixels, System.Runtime.InteropServices.GCHandleType.Pinned);
        try
        {
            frame.CopyPixels(new PixelRect(0, 0, w, h), pinned.AddrOfPinnedObject(), pixels.Length, w * 4);
        }
        finally
        {
            pinned.Free();
        }
        int left = w, top = h, right = -1, bottom = -1;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = (y * w + x) * 4;
                if (pixels[i] > 235 && pixels[i + 1] > 235 && pixels[i + 2] > 235) continue;
                left = Math.Min(left, x);
                right = Math.Max(right, x);
                top = Math.Min(top, y);
                bottom = Math.Max(bottom, y);
            }
        return right < 0 ? default : new PixelRect(left, top, right - left + 1, bottom - top + 1);
    }
}
