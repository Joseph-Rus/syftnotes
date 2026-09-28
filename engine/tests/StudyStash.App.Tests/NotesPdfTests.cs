using System.Text;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using SkiaSharp;
using StudyStash.App.Controls;
using StudyStash.App.Controls.Rich;
using StudyStash.App.Services;
using StudyStash.Core;
using StudyStash.Core.Rich;
using StudyStash.Core.Tests;

namespace StudyStash.App.Tests;

/// <summary>A lecture's notes saved as a PDF: drawn by the app's own notes view, as vectors with their fonts inside
/// and their words findable, on the region's paper, light on white whatever the app shows, and cut into pages
/// between blocks — read back from the PDF itself, not from the code that wrote it.</summary>
public class NotesPdfTests
{
    static JsonObject Lecture(string notes, string title = "The cardiac cycle", string? transcript = null) => new()
    {
        ["title"] = title, ["class"] = "BIO 110", ["date"] = "2026-09-23T10:00:00", ["seconds"] = 3120.0, ["notes"] = notes,
        ["transcript"] = transcript,
    };

    static void Look(SkinKind skin)
    {
        Skin.UseTheme(ColourThemes.Default);
        ((App)Application.Current!).UseSkin(skin);
        Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
    }

    static async Task<PdfProbe> PdfAsync(JsonObject lecture, Paper paper = Paper.Letter, bool transcript = false, string? keep = null)
    {
        using var output = new MemoryStream();
        int pages = await NotesPdf.WriteAsync(output, lecture, transcript, paper, Colors.Teal);
        var pdf = new PdfProbe(output.ToArray());
        Assert.Equal(pages, pdf.PageCount);
        if (keep is not null && Environment.GetEnvironmentVariable("STUDYSTASH_SHOTS") is { Length: > 0 } dir)
        {
            Directory.CreateDirectory(dir);
            await File.WriteAllBytesAsync(Path.Combine(dir, keep + ".pdf"), output.ToArray());
        }
        return pdf;
    }

    [AvaloniaTheory]
    [InlineData(SkinKind.Mac)]
    [InlineData(SkinKind.Win)]
    public async Task The_rich_lecture_is_a_pdf_with_its_words_in_it_and_its_formulas_and_diagrams_drawn_as_vectors(SkinKind skin)
    {
        Look(skin);
        var pdf = await PdfAsync(Lecture(RichDemo.CardiacLecture), keep: $"cardiac-{(skin == SkinKind.Mac ? "mac" : "win")}");

        Assert.True(pdf.IsPdf);
        Assert.InRange(pdf.PageCount, 2, 8);
        Assert.True(pdf.PageHas(0, "BIO 110 · Wednesday 23 September 2026 · 52 min"));
        Assert.True(pdf.PageHas(0, "The cardiac cycle"));
        Assert.Contains("Stroke volume is the difference between how full the ventricle gets", pdf.AllText.Replace('\n', ' '));
        Assert.True(pdf.PageHas(pdf.PageCount - 1, "Questions to review"));
        // A diagram's own words (a flowchart's boxes, an SVG's labels) are text in the PDF too, not pixels.
        Assert.Equal(1, pdf.Count("Isovolumetric relaxation"));
        Assert.True(pdf.Count("Right ventricle") >= 1);
        // Every formula and diagram is drawn: none fell back to its source, and none is a picture.
        Assert.DoesNotContain("\\frac", pdf.AllText);
        Assert.DoesNotContain("flowchart", pdf.AllText);
        Assert.False(pdf.HasImages);
        Assert.Empty(pdf.FontsNotEmbedded);
        for (int i = 0; i < pdf.PageCount; i++) Assert.True(pdf.PageHas(i, $"Page {i + 1} of {pdf.PageCount}"));
    }

    [AvaloniaFact]
    public async Task A_long_note_runs_to_as_many_pages_as_it_needs_each_word_once()
    {
        Look(SkinKind.Mac);
        // Six drawings each taller than half a page: no two share one, so there are exactly six pages on any computer.
        var notes = new StringBuilder();
        for (int i = 1; i <= 6; i++)
            notes.Append($"Figure {i} shows chamber {i}:\n\n```svg\n<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 400 560\"><title>Figure {i}</title>")
                .Append($"<rect x=\"20\" y=\"20\" width=\"360\" height=\"520\" fill=\"none\" stroke=\"#333\"/><text x=\"200\" y=\"280\" text-anchor=\"middle\" font-size=\"24\">Chamber {i}</text></svg>\n```\n\n");
        var pdf = await PdfAsync(Lecture(notes.ToString()), keep: "six-figures");

        Assert.Equal(6, pdf.PageCount);
        for (int i = 0; i < 6; i++)
        {
            Assert.True(pdf.PageHas(i, $"Chamber {i + 1}"), $"page {i + 1} should hold figure {i + 1}");
            // The line leading into it comes with it, rather than being left at the foot of the page before.
            Assert.True(pdf.PageHas(i, $"Figure {i + 1} shows chamber {i + 1}:"), $"page {i + 1} should hold figure {i + 1}'s line");
            Assert.True(pdf.PageHas(i, $"Page {i + 1} of 6"));
        }
        Assert.False(pdf.HasImages);

        // Plenty of paragraphs: every one printed once, none twice on two pages.
        var text = new StringBuilder();
        for (int i = 1; i <= 90; i++) text.Append($"Paragraph {i:000}. The ventricle fills, the valve closes and the pressure rises until it opens.\n\n");
        var paragraphs = await PdfAsync(Lecture(text.ToString()));
        Assert.InRange(paragraphs.PageCount, 4, 12);
        for (int i = 1; i <= 90; i++) Assert.Equal(1, paragraphs.Count($"Paragraph {i:000}."));
    }

    /// <summary>Where each page ends, checked against the laid-out notes: never through a line of text, a formula or a
    /// diagram, never just after a heading or a line leading into what follows, and each page full enough.</summary>
    [AvaloniaTheory]
    [InlineData(SkinKind.Mac)]
    [InlineData(SkinKind.Win)]
    public async Task Pages_break_between_blocks_never_through_a_line_a_formula_or_a_diagram(SkinKind skin)
    {
        Look(skin);
        using var laid = await NotesPdf.LayOutAsync(Lecture(RichDemo.CardiacLecture + "\n\n" + RichDemo.CardiacLecture), false, Paper.A4, Colors.Teal);
        var column = laid.Column;
        double pageHeight = NotesPdf.PageHeight(Paper.A4);
        Assert.True(laid.Slices.Count >= 3);
        Assert.Equal(0, laid.Slices[0].Top);
        Assert.Equal(column.Bounds.Height, laid.Slices[^1].Bottom, 1);
        foreach (var (a, b) in laid.Slices.Zip(laid.Slices.Skip(1))) Assert.Equal(a.Bottom, b.Top);
        foreach (var s in laid.Slices) Assert.InRange(s.Bottom - s.Top, 1, pageHeight + 0.5);

        var cuts = laid.Slices.Take(laid.Slices.Count - 1).Select(s => s.Bottom).ToList();
        double Top(Visual v) => v.TranslatePoint(default, column)!.Value.Y;
        foreach (var whole in column.GetVisualDescendants().Where(v => v is DiagramView or SvgView or MathDisplay).Cast<Control>())
            foreach (double cut in cuts)
                Assert.False(Top(whole) + 0.5 < cut && cut < Top(whole) + whole.Bounds.Height - 0.5, $"a page ends through a {whole.GetType().Name}");
        foreach (var text in column.GetVisualDescendants().OfType<TextBlock>())
        {
            double y = Top(text) + text.Padding.Top;
            foreach (var line in text.TextLayout.TextLines)
            {
                foreach (double cut in cuts) Assert.False(y + 0.5 < cut && cut < y + line.Height - 0.5, "a page ends through a line of text");
                y += line.Height;
            }
        }
        var headings = column.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Classes.Contains(NoteView.HeadingClass)).ToList();
        foreach (double cut in cuts) // no heading left at a page's foot, cut off from what it heads
            Assert.DoesNotContain(headings, h => cut >= Top(h) + h.Bounds.Height - 0.5 && cut - (Top(h) + h.Bounds.Height) < 30);
    }

    [AvaloniaFact]
    public async Task A_code_block_or_table_longer_than_a_page_goes_on_between_its_lines_and_rows()
    {
        Look(SkinKind.Mac);
        var code = new StringBuilder("## Listing\n\n```python\n");
        for (int i = 1; i <= 140; i++) code.Append($"step_{i:000} = beat({i})  # one line of the listing\n");
        code.Append("```\n");
        using (var laid = await NotesPdf.LayOutAsync(Lecture(code.ToString()), false, Paper.Letter, Colors.Teal))
        {
            Assert.True(laid.Slices.Count >= 3);
            // It starts on the first page, under the title, rather than leaving that page empty.
            Assert.True(laid.Slices[0].Bottom > NotesPdf.PageHeight(Paper.Letter) * 0.9);
            var listing = laid.Column.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text?.StartsWith("step_001", StringComparison.Ordinal) == true);
            double y = listing.TranslatePoint(default, laid.Column)!.Value.Y + listing.Padding.Top;
            var lineTops = new List<double>();
            foreach (var line in listing.TextLayout.TextLines)
            {
                lineTops.Add(y);
                y += line.Height;
            }
            foreach (var cut in laid.Slices.Take(laid.Slices.Count - 1).Select(s => s.Bottom))
                Assert.Contains(lineTops, t => Math.Abs(t - cut) < 0.01);
        }
        var pdf = await PdfAsync(Lecture(code.ToString()), keep: "long-code");
        for (int i = 1; i <= 140; i++) Assert.Equal(1, pdf.Count($"step_{i:000}="));

        var table = new StringBuilder("## Drug doses\n\n| Drug | Dose | Route |\n|---|---|---|\n");
        for (int i = 1; i <= 70; i++) table.Append($"| Drug {i:00} | {i * 5} mg | oral |\n");
        using (var laid = await NotesPdf.LayOutAsync(Lecture(table.ToString()), false, Paper.Letter, Colors.Teal))
        {
            Assert.True(laid.Slices.Count >= 2);
            var rows = laid.Column.GetVisualDescendants().OfType<Border>().Where(b => b.Child is Grid && b.Parent is StackPanel).ToList();
            var rowTops = rows.Select(r => r.TranslatePoint(default, laid.Column)!.Value.Y).ToList();
            foreach (var cut in laid.Slices.Take(laid.Slices.Count - 1).Select(s => s.Bottom))
                Assert.Contains(rowTops, t => Math.Abs(t - cut) < 0.01);
        }
        var rowsPdf = await PdfAsync(Lecture(table.ToString()), keep: "long-table");
        for (int i = 1; i <= 70; i++) Assert.Equal(1, rowsPdf.Count($"Drug {i:00}"));
    }

    [Fact]
    public void Letter_where_the_region_prints_on_it_and_a4_everywhere_else()
    {
        Assert.Equal(Paper.Letter, NotesPdf.PaperFor("US"));
        Assert.Equal(Paper.Letter, NotesPdf.PaperFor("ca"));
        Assert.Equal(Paper.Letter, NotesPdf.PaperFor("MX"));
        Assert.Equal(Paper.A4, NotesPdf.PaperFor("GB"));
        Assert.Equal(Paper.A4, NotesPdf.PaperFor("DE"));
        Assert.Equal(Paper.A4, NotesPdf.PaperFor("AU"));
        Assert.Equal(Paper.A4, NotesPdf.PaperFor(null));
        Assert.Equal(Paper.A4, NotesPdf.PaperFor(""));
        Assert.Equal("US", NotesPdf.RegionOf("en_US.UTF-8"));
        Assert.Equal("GB", NotesPdf.RegionOf("en-GB"));
        Assert.Null(NotesPdf.RegionOf("C"));
        Assert.Null(NotesPdf.RegionOf(null));
        // This computer's own answer comes back without a fuss, whichever it is.
        Assert.True(Enum.IsDefined(NotesPdf.LocalPaper()));
    }

    [AvaloniaFact]
    public async Task The_pages_are_the_papers_size()
    {
        Look(SkinKind.Mac);
        var letter = await PdfAsync(Lecture("## Summary\nThe heart has four chambers."), Paper.Letter);
        Assert.All(letter.PageSizes, s => Assert.Equal((612.0, 792.0), s));
        var a4 = await PdfAsync(Lecture("## Summary\nThe heart has four chambers."), Paper.A4);
        Assert.All(a4.PageSizes, s =>
        {
            Assert.Equal(595, s.Width, 0.01); // 210 × 297 mm, to the nearest point
            Assert.Equal(842, s.Height, 0.01);
        });
    }

    /// <summary>The app in dark mode with another colour theme: the paper still gets the light look's ink and the
    /// first theme's accent, drawn onto white.</summary>
    [AvaloniaFact]
    public async Task Paper_is_light_whatever_the_app_is_showing()
    {
        Skin.UseTheme(ColourThemes.All[4]);
        ((App)Application.Current!).UseSkin(SkinKind.Mac);
        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        try
        {
            using var laid = await NotesPdf.LayOutAsync(Lecture(RichDemo.CardiacLecture), false, Paper.Letter, Colors.Teal);
            var light = Skin.Build(SkinKind.Mac, ColourThemes.Default);
            var lightTokens = (ResourceDictionary)light.ThemeDictionaries[ThemeVariant.Light];
            Color Token(string key) => ((ISolidColorBrush)lightTokens[key]!).Color;
            var body = laid.Column.GetVisualDescendants().OfType<NoteView>().Single().GetVisualDescendants().OfType<TextBlock>().First();
            Assert.Equal(Token("Fg"), ((ISolidColorBrush)body.Foreground!).Color);
            var diagram = laid.Column.GetVisualDescendants().OfType<DiagramCanvas>().First();
            Assert.Equal(Token("Accent"), ((ISolidColorBrush)diagram.Accent!).Color);

            // And drawn (twice the size, to look at), each page is white paper with dark words on it.
            for (int page = 0; page < laid.Slices.Count; page++)
            {
                using var bitmap = new SKBitmap((int)laid.PageSize.Width * 2, (int)laid.PageSize.Height * 2);
                using (var canvas = new SKCanvas(bitmap))
                {
                    canvas.Clear(SKColors.White);
                    canvas.Scale(2);
                    await laid.DrawPageAsync(canvas, page);
                }
                var pixels = bitmap.Pixels;
                Assert.True(pixels.Count(p => p.Red < 80 && p.Green < 80 && p.Blue < 80) > 500);
                Assert.True(pixels.Count(p => p == SKColors.White) > pixels.Length * 0.6);
                if (Environment.GetEnvironmentVariable("STUDYSTASH_SHOTS") is { Length: > 0 } dir)
                {
                    Directory.CreateDirectory(dir);
                    using var png = File.Create(Path.Combine(dir, $"pdf-page-{page + 1}-from-a-dark-app.png"));
                    bitmap.Encode(png, SKEncodedImageFormat.Png, 100);
                }
            }
        }
        finally
        {
            Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
            Skin.UseTheme(ColourThemes.Default);
        }
    }

    /// <summary>Some fonts (SF Pro, Inter) raise a colon between figures with a glyph that stands for no letter; on
    /// paper the plain colon is kept, so "3:1" and "10:30" copy and search as written, in either look.</summary>
    [AvaloniaTheory]
    [InlineData(SkinKind.Mac)]
    [InlineData(SkinKind.Win)]
    public async Task A_colon_between_figures_reads_back_as_a_colon(SkinKind skin)
    {
        Look(skin);
        var pdf = await PdfAsync(Lecture("## Ratios\nThe inspiration to expiration ratio is 1:2, and rounds start at 10:30.\n\n| Ratio | Time |\n|---|---|\n| 3:1 | 07:45 |\n"));
        Assert.Equal(1, pdf.Count("ratio is 1:2, and rounds start at 10:30."));
        Assert.Equal(1, pdf.Count("3:1"));
        Assert.Equal(1, pdf.Count("07:45"));
    }

    [AvaloniaFact]
    public async Task A_link_in_the_notes_can_be_clicked_in_the_pdf()
    {
        Look(SkinKind.Mac);
        var pdf = await PdfAsync(Lecture("## Further reading\nThe [heart sounds guide](https://example.org/heart-sounds) has recordings of each valve."));
        Assert.Contains("https://example.org/heart-sounds", pdf.Links);
        Assert.True(pdf.PageHas(0, "The heart sounds guide has recordings of each valve."));
    }

    [AvaloniaFact]
    public async Task The_transcript_follows_the_notes_only_when_asked_for()
    {
        Look(SkinKind.Mac);
        var lecture = Lecture("## Summary\nThe heart has four chambers.", transcript: "[00:00] Good morning, everyone.\n[01:05] Today it's the cardiac cycle.");
        var without = await PdfAsync(lecture);
        Assert.Equal(0, without.Count("Good morning"));
        var with = await PdfAsync(lecture, transcript: true);
        Assert.Equal(1, with.Count("Good morning, everyone."));
        Assert.Equal(1, with.Count("01:05"));
        Assert.Equal(1, with.Count("Transcript"));
    }

    [AvaloniaFact]
    public async Task A_lecture_with_no_notes_yet_says_so()
    {
        Look(SkinKind.Mac);
        var pdf = await PdfAsync(Lecture(""));
        Assert.Equal(1, pdf.PageCount);
        Assert.True(pdf.PageHas(0, "No notes yet."));
    }

    // --- the download ------------------------------------------------------------------------------------------------

    [Fact]
    public void The_suggested_name_ends_in_pdf_and_windows_keeps_it_exactly()
    {
        Assert.Equal("The cardiac cycle.pdf", NoteExport.SaveName("The cardiac cycle", ".pdf"));
        Assert.Equal("CON_.pdf", NoteExport.SaveName("CON", ".pdf"));
        Assert.Equal("Chem.pdf", NoteExport.SaveName("Chem.", ".pdf"));
        Assert.Equal("Lab week 3.pdf", NoteExport.SaveName("Lab: week 3?", ".pdf"));
        Assert.Equal("untitled.pdf", NoteExport.SaveName("", ".pdf"));
        Assert.Equal("The cardiac cycle.md", NoteExport.SaveName("The cardiac cycle"));
    }

    [AvaloniaFact]
    public async Task Downloading_the_pdf_saves_it_from_the_library_over_whatever_was_there()
    {
        Look(SkinKind.Win);
        await using var rig = await LibraryRig.StartAsync();
        rig.AddLecture(new Meeting("l1") { Title = "The cardiac cycle", Date = "2026-09-22" }, "BIO 110",
            "## Details and examples\nCardiac output is $\\text{CO} = \\text{HR} \\times \\text{SV}$.\n\n```mermaid\n" + Summarize.MermaidExample + "\n```\n");
        var lib = new RemoteLibrary(rig.Url, LibraryRig.Password);
        using var dir = new TempDir();
        string path = dir[NoteExport.SaveName("The cardiac cycle", ".pdf")];
        await File.WriteAllBytesAsync(path, new byte[3_000_000], TestContext.Current.CancellationToken); // a bigger file there already

        await NotesDownload.LecturePdfAsync(() => Task.FromResult<Func<Task<Stream>>?>(() => Task.FromResult<Stream>(File.Open(path, FileMode.OpenOrCreate))),
            lib, "l1", transcript: false, Paper.A4, Colors.Teal);

        var pdf = new PdfProbe(await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
        Assert.True(pdf.IsPdf);
        Assert.EndsWith("%%EOF", Encoding.Latin1.GetString(pdf.Bytes).TrimEnd()); // nothing of the old file after it
        Assert.True(pdf.PageHas(0, "The cardiac cycle"));
        Assert.True(pdf.PageHas(0, "Cardiac output is"));
        Assert.False(pdf.HasImages);

        // Cancelling the Save dialog saves nothing and asks the library for nothing.
        await NotesDownload.LecturePdfAsync(() => Task.FromResult<Func<Task<Stream>>?>(null), lib, "l1", false, Paper.A4, Colors.Teal);
    }
}
