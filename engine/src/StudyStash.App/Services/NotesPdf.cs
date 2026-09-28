using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Skia.Helpers;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SkiaSharp;
using StudyStash.App.Controls;
using StudyStash.App.Controls.Rich;
using StudyStash.App.ViewModels;
using StudyStash.Core;

namespace StudyStash.App.Services;

/// <summary>The paper a PDF is laid out on: US Letter where the region prints on it, A4 everywhere else.</summary>
public enum Paper
{
    A4,
    Letter,
}

/// <summary>Where each page of a PDF starts and ends in the notes laid out as one long column, in pixels.</summary>
public readonly record struct PageSlice(double Top, double Bottom);

/// <summary>
/// A lecture's notes as a PDF a student can print or hand in: the notes drawn by the app's own notes view (headings,
/// lists, tables, code, typeset formulas and diagrams), as vectors with their fonts embedded and their text selectable,
/// on white paper in the light look and the default colour theme whatever the app is showing. The lecture's class,
/// date and title head the first page; each page has its number at the foot; pages break between blocks, never
/// through a line of text, a formula or a diagram. Runs on the UI thread (it lays out real controls, off screen).
/// </summary>
public static class NotesPdf
{
    /// <summary>Print margins, in points: three quarters of an inch at the sides and top, a little more at the foot
    /// for the page number.</summary>
    const float Side = 54, Top = 54, Bottom = 66, FooterBaseline = 34;

    /// <summary>A pixel of the app's layout on paper: 96 to the inch, as the screen counts them, so the notes' 16 px
    /// reading type prints at 12 pt.</summary>
    const float PointsPerPixel = 72f / 96f;

    /// <summary>A page's size in points.</summary>
    public static SKSize Size(Paper paper) => paper == Paper.Letter ? new SKSize(612, 792) : new SKSize(595, 842);

    /// <summary>How wide the notes' column is on a page, in pixels.</summary>
    public static double ColumnWidth(Paper paper) => (Size(paper).Width - 2 * Side) / PointsPerPixel;

    /// <summary>How much of the notes' column one page holds, in pixels.</summary>
    public static double PageHeight(Paper paper) => (Size(paper).Height - Top - Bottom) / PointsPerPixel;

    /// <summary>Where US Letter is the paper people buy (CLDR's list); everywhere else prints on A4.</summary>
    static readonly HashSet<string> LetterRegions = new(["BZ", "CA", "CL", "CO", "CR", "GT", "MX", "NI", "PA", "PH", "PR", "SV", "US", "VE"],
        StringComparer.OrdinalIgnoreCase);

    /// <summary>The paper for a region (a two-letter country code): Letter where it's the standard, else A4.</summary>
    public static Paper PaperFor(string? region) => region is { Length: 2 } && LetterRegions.Contains(region) ? Paper.Letter : Paper.A4;

    /// <summary>This computer's paper: Windows' own setting for the region, or the Mac's region; A4 when neither can
    /// be read.</summary>
    public static Paper LocalPaper()
    {
        try
        {
            if (OperatingSystem.IsWindows() && WindowsPaper() is { } paper) return paper;
            if (OperatingSystem.IsMacOS() && MacRegion() is { } region) return PaperFor(region);
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or MarshalDirectiveException)
        {
        }
        return PaperFor(RegionOf(Environment.GetEnvironmentVariable("LC_PAPER") ?? Environment.GetEnvironmentVariable("LC_ALL") ?? Environment.GetEnvironmentVariable("LANG")));
    }

    /// <summary>"en_US.UTF-8" → "US".</summary>
    internal static string? RegionOf(string? locale)
    {
        if (locale is not { Length: > 0 }) return null;
        string name = locale.Split('.', '@')[0];
        int cut = name.LastIndexOfAny(['_', '-']);
        return cut >= 0 && name.Length - cut - 1 == 2 ? name[(cut + 1)..] : null;
    }

    /// <summary>
    /// Writes the lecture's notes to <paramref name="output"/> as a PDF on <paramref name="paper"/>, with its
    /// transcript after them when <paramref name="transcript"/> is set, and says how many pages it made.
    /// <paramref name="dot"/> is its class's colour, beside the class's name as the app shows it.
    /// </summary>
    public static async Task<int> WriteAsync(Stream output, JsonObject lecture, bool transcript, Paper paper, Color? dot = null)
    {
        using var pages = await LayOutAsync(lecture, transcript, paper, dot ?? Colors.Gray);
        await pages.SaveAsync(output);
        return pages.Slices.Count;
    }

    /// <summary>The lecture laid out on <paramref name="paper"/> and cut into pages, ready to draw.</summary>
    internal static async Task<PrintedNotes> LayOutAsync(JsonObject lecture, bool transcript, Paper paper, Color dot)
    {
        Dispatcher.UIThread.VerifyAccess();
        double width = ColumnWidth(paper), pageHeight = PageHeight(paper);
        var column = Column(lecture, transcript, dot, width, pageHeight);
        var footer = new Grid { Width = width, ColumnDefinitions = new ColumnDefinitions("*,24,Auto") };
        var footerTitle = FooterText();
        footerTitle.TextTrimming = TextTrimming.CharacterEllipsis;
        footerTitle.Text = Title(lecture);
        var footerPage = FooterText();
        Grid.SetColumn(footerPage, 2);
        footer.Children.Add(footerTitle);
        footer.Children.Add(footerPage);
        // The look's tokens in the light look and the first colour theme: paper is white, whatever the screen shows.
        var host = new StackPanel { Children = { column, footer }, Resources = Skin.Build(Skin.Current, ColourThemes.Default) };
        // Never shown: it's what gives the pieces the app's styles, and the light look.
        var window = new Window { Content = host, RequestedThemeVariant = ThemeVariant.Light, ShowInTaskbar = false, ShowActivated = false };
        try
        {
            await SettleAsync(host, width);
            var slices = Paginate(column, pageHeight);
            // Avalonia sets the canvas's matrix outright as it draws, so the notes are drawn into a picture first (a
            // picture keeps its matrix relative to wherever it's played back), and the picture is put on each page.
            var picture = await PictureAsync(column);
            return new PrintedNotes(window, column, footer, footerPage, picture, slices, Links(column), paper, Title(lecture));
        }
        catch
        {
            Close(window);
            throw;
        }
    }

    static void Close(Window window)
    {
        window.Content = null;
        window.Close();
    }

    /// <summary>A lecture laid out for paper: the notes' column, where each page starts and ends in it, and its links;
    /// it draws a page onto any canvas (a PDF's, or a picture's for a look at it). Closing it lets the controls go.</summary>
    internal sealed class PrintedNotes(Window window, StackPanel column, Grid footer, TextBlock footerPage, SKPicture notes,
        List<PageSlice> slices, List<(Rect Area, string Url)> links, Paper paper, string title) : IDisposable
    {
        public StackPanel Column => column;
        public IReadOnlyList<PageSlice> Slices => slices;
        public IReadOnlyList<(Rect Area, string Url)> Links => links;
        public SKSize PageSize => Size(paper);

        /// <summary>Draws page <paramref name="index"/> (from 0), in points, onto <paramref name="canvas"/>.</summary>
        public async Task DrawPageAsync(SKCanvas canvas, int index)
        {
            var slice = slices[index];
            double width = column.Bounds.Width;
            int saved = canvas.Save();
            canvas.Translate(Side, Top);
            canvas.Scale(PointsPerPixel);
            // A little room either side: a formula's italic or a diagram's hairline may reach just past the column.
            canvas.ClipRect(new SKRect(-8, 0, (float)width + 8, (float)(slice.Bottom - slice.Top)));
            canvas.Translate(0, (float)-slice.Top);
            canvas.DrawPicture(notes);
            foreach (var (area, url) in links)
                if (area.Top >= slice.Top - 0.5 && area.Bottom <= slice.Bottom + 0.5)
                    canvas.DrawUrlAnnotation(new SKRect((float)area.Left, (float)area.Top, (float)area.Right, (float)area.Bottom), url);
            canvas.RestoreToCount(saved);

            footerPage.Text = $"Page {index + 1} of {slices.Count}";
            Layout(footer, width);
            using var foot = await PictureAsync(footer);
            saved = canvas.Save();
            canvas.Translate(Side, PageSize.Height - FooterBaseline - (float)(footer.Bounds.Height * PointsPerPixel));
            canvas.Scale(PointsPerPixel);
            canvas.DrawPicture(foot);
            canvas.RestoreToCount(saved);
        }

        /// <summary>Writes every page to <paramref name="output"/> as a PDF.</summary>
        public async Task SaveAsync(Stream output)
        {
            using var stream = new SKManagedWStream(output, disposeManagedStream: false);
            using var document = SKDocument.CreatePdf(stream, new SKDocumentPdfMetadata
            {
                Title = title, Creator = "Study Stash", Producer = "Study Stash", Creation = DateTime.Now, Modified = DateTime.Now,
            });
            for (int i = 0; i < slices.Count; i++)
            {
                await DrawPageAsync(document.BeginPage(PageSize.Width, PageSize.Height), i);
                document.EndPage();
            }
            document.Close();
        }

        public void Dispose()
        {
            notes.Dispose();
            Close(window);
        }
    }

    /// <summary>The page's column: the class and date, the title, then the notes (and the transcript, asked for) —
    /// the lecture as the app's lecture page shows it, less what only works on a screen.</summary>
    static StackPanel Column(JsonObject lecture, bool transcript, Color dot, double width, double pageHeight)
    {
        bool mac = Skin.Current == SkinKind.Mac;
        var column = new StackPanel { Width = width, Spacing = mac ? 14 : 12 };

        var metaRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        metaRow.Children.Add(new Ellipse { Width = 8, Height = 8, Fill = new SolidColorBrush(dot), VerticalAlignment = VerticalAlignment.Center });
        var meta = Token(new TextBlock { Text = Meta(lecture), FontSize = 12, TextWrapping = TextWrapping.Wrap }, "Fg2");
        metaRow.Children.Add(meta);
        column.Children.Add(metaRow);

        var title = Token(new TextBlock
        {
            Text = Title(lecture), FontSize = mac ? 30 : 28, FontWeight = mac ? FontWeight.Bold : FontWeight.SemiBold,
            LineHeight = mac ? 34.5 : 35, TextWrapping = TextWrapping.Wrap,
        }, "Fg");
        title.Bind(TextBlock.FontFamilyProperty, title.GetResourceObservable("DisplayFont"));
        if (mac) Controls.Typography.SetExtra(title, -0.6);
        column.Children.Add(title);

        string notes = AiWords.DropLeadingSummary(S(lecture["notes"]));
        column.Children.Add(new NoteView { PageHeight = pageHeight, Markdown = notes.Trim().Length == 0 ? "_No notes yet._" : notes });

        string raw = S(lecture["transcript"]);
        if (transcript && raw.Trim().Length > 0)
        {
            var heard = new StackPanel { Spacing = 8 };
            var heading = Token(new TextBlock { Text = "Transcript", FontSize = mac ? 17 : 20, FontWeight = FontWeight.SemiBold, LineHeight = (mac ? 17 : 20) * 1.25 }, "Fg");
            heading.Bind(TextBlock.FontFamilyProperty, heading.GetResourceObservable("DisplayFont"));
            heading.Classes.Add(NoteView.HeadingClass);
            heading.Margin = new Thickness(0, mac ? 14 : 12, 0, 4);
            heard.Children.Add(heading);
            bool timed = TimedText.HasTimes(raw);
            foreach (var line in timed ? TimedText.Parse(raw) : [new Spoken(0, 0, raw)])
            {
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions(timed ? "48,12,*" : "0,0,*") };
                // A plain colon, not the Mac font's raised one between figures (or its figures of one width): that glyph
                // stands for no letter, so a time copied or searched for in the PDF would read "01 05".
                if (timed)
                    row.Children.Add(Token(new TextBlock
                    {
                        Text = TimedText.Clock(line.Start), FontSize = 12, Margin = new Thickness(0, 3, 0, 0), FontFeatures = FontFeatureCollection.Parse("-calt"),
                    }, "Fg2"));
                var said = Token(new TextBlock { Text = line.Text, FontSize = mac ? 16 : 15, LineHeight = (mac ? 16 : 15) * 1.6, TextWrapping = TextWrapping.Wrap }, "Fg");
                said.Bind(TextBlock.FontFamilyProperty, said.GetResourceObservable(mac ? "SerifFont" : "TextFont"));
                Grid.SetColumn(said, 2);
                row.Children.Add(said);
                heard.Children.Add(row);
            }
            column.Children.Add(heard);
        }
        return column;
    }

    static TextBlock FooterText() => Token(new TextBlock { FontSize = 11 }, "Fg3");

    static TextBlock Token(TextBlock t, string foreground)
    {
        t.Bind(TextBlock.ForegroundProperty, t.GetResourceObservable(foreground));
        return t;
    }

    /// <summary>"CS 101 · Tuesday 23 September 2026 · 1 h 12 min": the app's line over a lecture, with the year a
    /// page kept for later needs.</summary>
    static string Meta(JsonObject lecture)
    {
        var date = DateTimeOffset.TryParse(S(lecture["date"]), CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var d) ? d.LocalDateTime : (DateTime?)null;
        return string.Join(" · ", new[]
        {
            S(lecture["class"]), date?.ToString("dddd d MMMM yyyy", CultureInfo.InvariantCulture) ?? "",
            lecture["seconds"] is JsonValue v && v.TryGetValue(out double s) && s > 0 ? TimedText.Length(s) : "",
        }.Where(x => x.Length > 0));
    }

    static string Title(JsonObject lecture) => S(lecture["title"]) is { Length: > 0 } t ? t : "Untitled";

    static string S(JsonNode? n) => n is JsonValue v && v.TryGetValue(out string? s) ? s ?? "" : "";

    /// <summary>A laid-out piece drawn as a picture, at its own top left.</summary>
    static async Task<SKPicture> PictureAsync(Control piece)
    {
        var bounds = new Rect(piece.Bounds.Size);
        using var recorder = new SKPictureRecorder();
        var canvas = recorder.BeginRecording(new SKRect(-16, -16, (float)bounds.Width + 16, (float)bounds.Height + 16));
        await DrawingContextHelper.RenderAsync(canvas, piece, bounds, new Vector(96, 96));
        return recorder.EndRecording();
    }

    /// <summary>Lays the column out, then again as each diagram finishes being laid out in the background (or turns
    /// out it can't be, and becomes its calm card), until nothing is left waiting.</summary>
    static async Task SettleAsync(StackPanel host, double width)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(60);
        for (int round = 0; ; round++)
        {
            Layout(host, width);
            bool waiting = host.GetVisualDescendants().OfType<DiagramView>().Any(d => d.IsLaying);
            if (!waiting && round > 0) return;
            while (SceneCache.Busy && DateTime.UtcNow < deadline) await Task.Delay(20);
            // What the diagrams posted when their layouts finished (measure again, or show the card) runs first.
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            if (DateTime.UtcNow >= deadline || round > 20)
            {
                Layout(host, width);
                return;
            }
        }
    }

    /// <summary>Measures and arranges a piece that isn't on any screen: each control asked again, since nothing
    /// passes a changed size up the tree without a window laying it out.</summary>
    static void Layout(Layoutable root, double width)
    {
        foreach (var l in root.GetVisualDescendants().OfType<Layoutable>()) l.InvalidateMeasure();
        root.InvalidateMeasure();
        root.Measure(new Avalonia.Size(width, double.PositiveInfinity));
        root.Arrange(new Rect(0, 0, width, root.DesiredSize.Height));
    }

    /// <summary>
    /// Where each page starts and ends in the laid-out <paramref name="column"/>, each holding up to
    /// <paramref name="pageHeight"/> pixels of it. A page ends between blocks — the top of a paragraph, a list's item,
    /// a table's row — as far down as fits, but never just after a heading or a table's header row. A block taller
    /// than a page (a long code block or paragraph) goes on from as far down the page as its lines fill. No page ends
    /// through a line of text, a formula or a diagram, unless one is taller than a whole page.
    /// </summary>
    internal static List<PageSlice> Paginate(Control column, double pageHeight)
    {
        var breaks = new List<(double Y, bool Line, bool AfterHeading)>();
        var atoms = new List<(double Top, double Bottom)>();
        Collect(column, 0, breaks, atoms);
        const double Eps = 0.5;
        bool Clear(double y) => !atoms.Any(a => a.Top + Eps < y && y < a.Bottom - Eps);
        var blocks = breaks.Where(b => !b.Line && Clear(b.Y)).ToList();
        var lines = breaks.Where(b => b.Line && Clear(b.Y)).Select(b => b.Y).ToList();

        double total = column.Bounds.Height;
        var slices = new List<PageSlice>();
        double top = 0;
        while (total - top > Eps)
        {
            double limit = top + pageHeight;
            if (total <= limit + Eps)
            {
                slices.Add(new PageSlice(top, total));
                break;
            }
            var fit = blocks.Where(b => b.Y > top + Eps && b.Y <= limit + Eps).ToList();
            double? best = fit.Where(b => !b.AfterHeading).Select(b => (double?)b.Y).Max() ?? fit.Select(b => (double?)b.Y).Max();
            // What starts at the break (a heading and what it heads counting as one) must fit on a page of its own; if
            // it can't, it starts here and goes on down the page.
            double from = best ?? top;
            double next = blocks.Where(b => b.Y > from + Eps && !b.AfterHeading).Select(b => (double?)b.Y).Min() ?? total;
            if (best is null || next - from > pageHeight + Eps)
                if (lines.Where(y => y > from + Eps && y <= limit + Eps).Select(y => (double?)y).Max() is { } line) best = line;
            double end = best ?? limit;
            slices.Add(new PageSlice(top, end));
            top = end;
        }
        if (slices.Count == 0) slices.Add(new PageSlice(0, Math.Max(total, 1)));
        return slices;
    }

    /// <summary>Gathers where a page may end below <paramref name="v"/> (at <paramref name="y"/> down the column):
    /// the top of each piece of a stack (a block, an item, a row) and of each line of text; and what a page may never
    /// end through: each line of text, and each formula and diagram whole.</summary>
    static void Collect(Visual v, double y, List<(double Y, bool Line, bool AfterHeading)> breaks, List<(double Top, double Bottom)> atoms)
    {
        if (v is Control { IsVisible: false }) return;
        switch (v)
        {
            case DiagramView or SvgView or MathDisplay:
                atoms.Add((y, y + v.Bounds.Height));
                return;
            case TextBlock text:
                double lineTop = y + text.Padding.Top;
                bool first = true;
                foreach (var line in text.TextLayout.TextLines)
                {
                    if (!first) breaks.Add((lineTop, true, false));
                    atoms.Add((lineTop, lineTop + line.Height));
                    lineTop += line.Height;
                    first = false;
                }
                return;
            case StackPanel { Orientation: Orientation.Vertical } stack:
                Control? before = null;
                foreach (var child in stack.Children.Where(c => c.IsVisible))
                {
                    bool afterHeading = before is not null && KeepsWithNext(before);
                    breaks.Add((y + child.Bounds.Y, false, afterHeading));
                    before = child;
                }
                break;
        }
        foreach (var child in v.GetVisualChildren())
            Collect(child, y + child.Bounds.Y, breaks, atoms);
    }

    /// <summary>A heading, a table's header row, or a line that leads into what follows it ("Worked step by step:"):
    /// a page doesn't end just after one.</summary>
    static bool KeepsWithNext(Control block) =>
        block.Classes.Contains(NoteView.HeadingClass) || block.Classes.Contains(NoteView.TableHeaderClass)
        || block is TextBlock { Inlines: { Count: > 0 } inlines } && string.Concat(inlines.OfType<Run>().Select(r => r.Text)).TrimEnd().EndsWith(':');

    /// <summary>Every link in the notes: where its words are in the column, and where it goes.</summary>
    static List<(Rect Area, string Url)> Links(Control column)
    {
        var found = new List<(Rect, string)>();
        foreach (var text in column.GetVisualDescendants().OfType<TextBlock>())
        {
            if (text.Inlines is not { Count: > 0 } inlines || !inlines.OfType<Run>().Any(r => NoteView.GetLink(r) is not null)) continue;
            if (text.TranslatePoint(default, column) is not { } origin) continue;
            int at = 0;
            foreach (var inline in inlines)
            {
                int length = inline is Run r ? r.Text?.Length ?? 0 : 1;
                if (inline is Run run && NoteView.GetLink(run) is { } url && IsWebLink(url) && length > 0)
                    foreach (var area in text.TextLayout.HitTestTextRange(at, length))
                        found.Add((area.Translate(new Vector(origin.X + text.Padding.Left, origin.Y + text.Padding.Top)), url));
                at += length;
            }
        }
        return found;
    }

    static bool IsWebLink(string url) => Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme is "http" or "https" or "mailto";

    // --- the region's paper -----------------------------------------------------------------------------------------

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern int GetLocaleInfoEx(string? localeName, uint type, StringBuilder? data, int size);

    /// <summary>Windows' own paper for the region (Control Panel's LOCALE_IPAPERSIZE): 1 is Letter, 5 Legal, 9 A4.</summary>
    static Paper? WindowsPaper()
    {
        const uint LocaleIPaperSize = 0x100A;
        var data = new StringBuilder(8);
        if (GetLocaleInfoEx(null, LocaleIPaperSize, data, data.Capacity) <= 0) return null;
        return data.ToString().Trim() switch
        {
            "1" or "5" => Paper.Letter,
            "" => null,
            _ => Paper.A4,
        };
    }

    const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    [DllImport(CoreFoundation)]
    static extern IntPtr CFLocaleCopyCurrent();

    [DllImport(CoreFoundation)]
    static extern IntPtr CFLocaleGetValue(IntPtr locale, IntPtr key);

    [DllImport(CoreFoundation)]
    [return: MarshalAs(UnmanagedType.U1)]
    static extern bool CFStringGetCString(IntPtr text, byte[] buffer, nint size, uint encoding);

    [DllImport(CoreFoundation)]
    static extern void CFRelease(IntPtr cf);

    /// <summary>The Mac's region (System Settings → General → Language &amp; Region), as its two-letter code.</summary>
    static string? MacRegion()
    {
        IntPtr library = NativeLibrary.Load(CoreFoundation);
        IntPtr key = Marshal.ReadIntPtr(NativeLibrary.GetExport(library, "kCFLocaleCountryCode"));
        IntPtr locale = CFLocaleCopyCurrent();
        if (locale == IntPtr.Zero) return null;
        try
        {
            IntPtr value = CFLocaleGetValue(locale, key);
            var buffer = new byte[16];
            const uint Utf8 = 0x08000100;
            return value != IntPtr.Zero && CFStringGetCString(value, buffer, buffer.Length, Utf8)
                ? Encoding.UTF8.GetString(buffer).TrimEnd('\0')
                : null;
        }
        finally
        {
            CFRelease(locale);
        }
    }
}
