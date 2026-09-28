using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using Markdig;
using Markdig.Extensions.Mathematics;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using StudyStash.App.Controls.Rich;
using StudyStash.Core.Rich;
using MdBlock = Markdig.Syntax.Block;
using MdInline = Markdig.Syntax.Inlines.Inline;
using Inline = Avalonia.Controls.Documents.Inline;

namespace StudyStash.App.Controls;

/// <summary>
/// A lecture's notes, from their Markdown, in the design's type: section headings (SF Pro Display 17, or Segoe UI
/// Display 20), reading text (New York 16/1.6 on a Mac, Segoe UI 15/1.6 on Windows), bullets and numbered questions
/// with their markers in the tertiary color, and the Definitions section as two columns with a rule above each term.
/// Diagrams are drawn (a ```mermaid flowchart, a ```svg drawing), in a bullet too; one that can't be drawn shows its
/// source calmly instead.
/// </summary>
public sealed partial class NoteView : StackPanel
{
    public static readonly StyledProperty<string?> MarkdownProperty = AvaloniaProperty.Register<NoteView, string?>(nameof(Markdown));

    /// <summary>A quick answer or a chat bubble, not a lecture's notes: the caller's own font, size and line height
    /// (<see cref="BodyFont"/>, <see cref="BodySize"/>, <see cref="BodyLineHeight"/>) rather than the design's reading
    /// type; no top margin before a heading; tighter spacing; text selectable; a diagram capped at 260 px tall.</summary>
    public static readonly StyledProperty<bool> CompactProperty = AvaloniaProperty.Register<NoteView, bool>(nameof(Compact));
    /// <summary>The resource key of a compact answer's font (e.g. "SerifFont" for the quick panel); the ask chat's
    /// plain answer leaves this unset and reads the same "TextFont" every <see cref="TextBlock"/> defaults to.</summary>
    public static readonly StyledProperty<string?> BodyFontProperty = AvaloniaProperty.Register<NoteView, string?>(nameof(BodyFont));
    public static readonly StyledProperty<double> BodySizeProperty = AvaloniaProperty.Register<NoteView, double>(nameof(BodySize), 15);
    /// <summary>The line's own height in pixels (not a multiplier of the size, unlike the design's reading type).</summary>
    public static readonly StyledProperty<double> BodyLineHeightProperty = AvaloniaProperty.Register<NoteView, double>(nameof(BodyLineHeight), 22);

    /// <summary>A compact diagram never grows past this tall in the column it sits in (a quick answer or a chat
    /// bubble); a click still opens it at full size.</summary>
    public const double CompactDiagramMaxHeight = 260;

    static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().UsePipeTables().UseEmphasisExtras().UseMathematics().Build();

    /// <summary>Inline maths reads a touch smaller than the body (Latin Modern reads larger than the body fonts at
    /// equal size); a display formula, larger and centred. Past 2 000 characters, a formula goes straight to its
    /// fallback rather than asking CSharpMath to lay out something that size.</summary>
    const double InlineMathFactor = 0.94, DisplayMathFactor = 1.15;
    const int MaxFormulaChars = 2000;

    public string? Markdown
    {
        get => GetValue(MarkdownProperty);
        set => SetValue(MarkdownProperty, value);
    }

    public bool Compact
    {
        get => GetValue(CompactProperty);
        set => SetValue(CompactProperty, value);
    }

    public string? BodyFont
    {
        get => GetValue(BodyFontProperty);
        set => SetValue(BodyFontProperty, value);
    }

    public double BodySize
    {
        get => GetValue(BodySizeProperty);
        set => SetValue(BodySizeProperty, value);
    }

    public double BodyLineHeight
    {
        get => GetValue(BodyLineHeightProperty);
        set => SetValue(BodyLineHeightProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == MarkdownProperty || change.Property == CompactProperty || change.Property == BodyFontProperty
            || change.Property == BodySizeProperty || change.Property == BodyLineHeightProperty) Build();
    }

    bool Mac => Skin.Current == SkinKind.Mac;

    /// <summary>The reading body's size in the design's own type: 16 on a Mac (New York), 15 on Windows (Segoe UI).
    /// A compact answer uses <see cref="BodySize"/> instead (<see cref="EffectiveBodySize"/>).</summary>
    double DefaultBodySize => Mac ? 16 : 15;

    /// <summary>What a display formula's size follows: the caller's <see cref="BodySize"/> in a compact answer, the
    /// design's reading size in a lecture's notes.</summary>
    double EffectiveBodySize => Compact ? BodySize : DefaultBodySize;

    TextBlock Text(string resourceFont, double size, double lineHeight)
    {
        var t = new TextBlock { FontSize = size, LineHeight = size * lineHeight, TextWrapping = TextWrapping.Wrap };
        t.Bind(TextBlock.FontFamilyProperty, t.GetResourceObservable(resourceFont));
        t.Bind(TextBlock.ForegroundProperty, t.GetResourceObservable("Fg"));
        return t;
    }

    /// <summary>A compact answer's paragraph text: the caller's own font, size and line height, selectable (an answer
    /// can be copied a sentence at a time, not just as a whole).</summary>
    TextBlock CompactText()
    {
        var t = new SelectableTextBlock { FontSize = BodySize, LineHeight = BodyLineHeight, TextWrapping = TextWrapping.Wrap };
        t.Bind(TextBlock.FontFamilyProperty, t.GetResourceObservable(BodyFont ?? "TextFont"));
        t.Bind(TextBlock.ForegroundProperty, t.GetResourceObservable("Fg"));
        t.Bind(SelectableTextBlock.SelectionBrushProperty, t.GetResourceObservable("Hl"));
        return t;
    }

    TextBlock Body() => Compact ? CompactText() : Text(Mac ? "SerifFont" : "TextFont", DefaultBodySize, 1.6);

    TextBlock Secondary(double size)
    {
        var t = Text("TextFont", size, 1.5);
        t.Bind(TextBlock.ForegroundProperty, t.GetResourceObservable("Fg2"));
        return t;
    }

    TextBlock Marker(string text)
    {
        var t = Compact ? CompactText() : Text(Mac ? "SerifFont" : "TextFont", DefaultBodySize, 1.6);
        t.Text = text;
        t.TextWrapping = TextWrapping.NoWrap;
        t.Bind(TextBlock.ForegroundProperty, t.GetResourceObservable("Fg3"));
        return t;
    }

    /// <summary>The diagrams of the last build, by what they draw, so the same notes set again (an answer arriving
    /// in pieces, a lecture reopened) keep their drawings instead of laying them out again.</summary>
    Dictionary<string, Control> diagrams = [];
    Dictionary<string, Control> previous = [];

    void Build()
    {
        Children.Clear();
        Spacing = Compact ? 8 : Mac ? 14 : 12;
        previous = diagrams;
        diagrams = [];
        string markdown = source = Markdown ?? "";
        var doc = Markdig.Markdown.Parse(markdown, Pipeline);
        bool first = true;
        string section = "";
        int skipUntil = -1;
        foreach (MdBlock block in doc)
        {
            if (block.Span.Start < skipUntil) continue;
            Control? control;
            switch (block)
            {
                case HeadingBlock h:
                    section = Plain(h.Inline);
                    var head = Text("DisplayFont", Mac ? 17 : 20, 1.25);
                    head.FontWeight = FontWeight.SemiBold;
                    Fill(head, h.Inline);
                    head.Margin = Compact ? default : new Thickness(0, first ? (Mac ? 18 : 16) : (Mac ? 14 : 12), 0, 0);
                    control = head;
                    break;
                case ListBlock list when IsDefinitions(section, list):
                    control = Definitions(list);
                    break;
                case HtmlBlock html when IsSvg(html.Lines.ToString()):
                    // A drawing written straight into the Markdown ends at its first blank line; take it to </svg>
                    // (unless a code fence comes first: then it's only what its own lines hold).
                    int from = html.Span.Start, close = markdown.IndexOf("</svg>", from, StringComparison.OrdinalIgnoreCase);
                    int fence = markdown.IndexOf("\n```", from, StringComparison.Ordinal);
                    if (close >= 0 && (fence < 0 || fence > close))
                    {
                        skipUntil = close + "</svg>".Length;
                        control = Diagram(DiagramKind.Svg, markdown[from..skipUntil]);
                    }
                    else control = BlockControl(block);
                    break;
                default:
                    control = BlockControl(block);
                    break;
            }
            if (control is not null) Children.Add(control);
            if (block is not HeadingBlock) first = false;
            else if (!first) first = false;
        }
        previous.Clear();
    }

    /// <summary>The Markdown being built.</summary>
    string source = "";

    /// <summary>Whether a block runs to the end of the notes: an unfinished diagram there is still arriving; one
    /// earlier on never will, and says it can't be drawn.</summary>
    bool AtEnd(LeafBlock block)
    {
        // An unclosed fence's span covers only its opening line; its last line says where it really ends.
        int end = block.Span.End;
        if (block.Lines.Count > 0 && block.Lines.Lines[block.Lines.Count - 1] is var last) end = System.Math.Max(end, last.Position + last.Slice.Length - 1);
        return end >= source.TrimEnd().Length - 1;
    }

    /// <summary>A block of the notes as the page shows it (or null for one it doesn't): the same at the top level and
    /// inside a bullet.</summary>
    Control? BlockControl(MdBlock block)
    {
        switch (block)
        {
            // MathBlock is itself a FencedCodeBlock ($$ on its own lines reads as one): it must come first.
            case MathBlock mb:
                return DisplayMath(mb.Lines.ToString());
            case ParagraphBlock p:
                return Paragraph(p.Inline);
            case ListBlock list:
                return List(list);
            case Table table:
                return TableBlock(table);
            case FencedCodeBlock fence when KindOf(fence) is { } kind:
                // A diagram still being written (an answer arriving in pieces) is drawn once its fence closes.
                return fence.ClosingFencedCharCount == 0 && AtEnd(fence) ? Pending() : Diagram(kind, fence.Lines.ToString());
            case FencedCodeBlock or CodeBlock:
                return Code(((LeafBlock)block).Lines.ToString());
            case HtmlBlock html when IsSvg(html.Lines.ToString()):
                string svg = html.Lines.ToString();
                return !svg.Contains("</svg>", StringComparison.OrdinalIgnoreCase) && AtEnd(html) ? Pending() : Diagram(DiagramKind.Svg, svg);
            case QuoteBlock q:
                var quote = Body();
                quote.Text = string.Join(" ", q.Descendants<ParagraphBlock>().Select(x => Plain(x.Inline)));
                quote.Margin = new Thickness(14, 0, 0, 0);
                quote.Bind(TextBlock.ForegroundProperty, quote.GetResourceObservable("Fg2"));
                return quote;
            case ThematicBreakBlock:
                var rule = new Border { Height = 1, Margin = new Thickness(0, 6) };
                rule.Bind(Border.BackgroundProperty, rule.GetResourceObservable("Sep"));
                return rule;
            default:
                return null;
        }
    }

    /// <summary>
    /// A paragraph, its formulas typeset: ordinarily one <see cref="TextBlock"/>, unless it holds an inline formula
    /// tall enough to crush the lines around it (a matrix, <c>cases</c>) — that one is lifted onto its own centred
    /// line instead, splitting the paragraph into a small stack of pieces around it.
    /// </summary>
    Control Paragraph(ContainerInline? inline)
    {
        if (inline is null) return Body();
        var segments = new List<Control>();
        var current = Body();
        bool split = false;
        foreach (var piece in Runs(inline, null, null, null, current.FontSize, null))
        {
            if (piece is InlineUIContainer { Child: MathView { Display: false } mv } && mv.DesiredSize.Height > current.FontSize * 2)
            {
                if (current.Inlines!.Count > 0) segments.Add(current);
                // The same size and style a display formula gets, just still sitting in the paragraph's flow.
                mv.Display = true;
                mv.Size = current.FontSize * DisplayMathFactor;
                mv.InvalidateMeasure();
                var lifted = new MathDisplay(mv) { Margin = new Thickness(0, 2) };
                segments.Add(lifted);
                current = Body();
                split = true;
                continue;
            }
            current.Inlines!.Add(piece);
        }
        if (current.Inlines!.Count > 0 || !split) segments.Add(current);
        if (segments.Count == 1) return segments[0];
        var stack = new StackPanel { Spacing = 6 };
        foreach (var s in segments) stack.Children.Add(s);
        return stack;
    }

    /// <summary>A <c>$$…$$</c> formula on its own lines: centred, larger than the body, scaled down to fit the
    /// column when it's wider (never below <see cref="MathDisplay.MinScale"/>), then scrollable sideways past that.
    /// One CSharpMath can't typeset shows its source instead, calmly.</summary>
    Control DisplayMath(string source)
    {
        string latex = source.Trim();
        if (latex.Length == 0 || latex.Length > MaxFormulaChars) return DisplayFallback(latex);
        var mv = new MathView { Latex = latex, Display = true, Size = EffectiveBodySize * DisplayMathFactor };
        mv.Bind(MathView.ForegroundProperty, mv.GetResourceObservable("Fg"));
        mv.Measure(Size.Infinity);
        if (mv.ErrorMessage is not null) return DisplayFallback(latex);
        return new MathDisplay(mv) { Margin = new Thickness(0, 4) };
    }

    /// <summary>The code-box look, for a display formula that couldn't be typeset: one quiet line saying so, then
    /// the source in mono — the same shape as <see cref="DiagramCard"/>'s, one Border and no nested background.</summary>
    Control DisplayFallback(string source)
    {
        var note = Text("TextFont", Mac ? 12 : 13, 1.4);
        note.Text = "Couldn't typeset this formula.";
        note.Bind(TextBlock.ForegroundProperty, note.GetResourceObservable("Fg3"));
        var code = new TextBlock
        {
            Text = source.TrimEnd(), FontFamily = new FontFamily("SF Mono, Menlo, Cascadia Mono, Consolas, monospace"), FontSize = 13, LineHeight = 19.5,
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0),
        };
        code.Bind(TextBlock.ForegroundProperty, code.GetResourceObservable("Fg"));
        var card = new Border { Padding = new Thickness(12, 10), CornerRadius = new CornerRadius(Mac ? 8 : 4), Child = new StackPanel { Children = { note, code } } };
        card.Bind(Border.BackgroundProperty, card.GetResourceObservable("Fill2"));
        return card;
    }

    enum DiagramKind { Mermaid, Svg }

    /// <summary>Which fences are diagrams — the same rule search and the diagram repair read notes by
    /// (<see cref="NoteBlocks.KindOf"/>).</summary>
    static DiagramKind? KindOf(FencedCodeBlock fence) => NoteBlocks.KindOf(fence.Info ?? "", fence.Lines.ToString()) switch
    {
        NoteBlockKind.Mermaid => DiagramKind.Mermaid,
        NoteBlockKind.Svg => DiagramKind.Svg,
        _ => null,
    };

    static bool IsSvg(string text) => NoteBlocks.IsSvg(text);

    /// <summary>A diagram, drawn: centred in the column with a little room above and below and no frame; or, when it
    /// can't be drawn, the calm card that says why and shows its source. Never an exception.</summary>
    Control Diagram(DiagramKind kind, string source)
    {
        string key = $"{kind}\u0001{source}";
        for (int n = 2; diagrams.ContainsKey(key); n++) key = $"{kind}\u0001{n}\u0001{source}";
        if (previous.Remove(key, out var kept))
        {
            Detach(kept);
            return diagrams[key] = kept;
        }
        Control made = kind == DiagramKind.Mermaid ? ChartBlock(source) : SvgBlock(source);
        if (made is not DiagramCard) diagrams[key] = made;
        return made;
    }

    Control ChartBlock(string source)
    {
        Flowchart chart;
        try
        {
            chart = Flowchart.Parse(source);
        }
        catch (MermaidException e)
        {
            return new DiagramCard(e.Message, source);
        }
        // Laid out in the background from now, in the look's font, while the rest of the note is built; the view keeps
        // its space until then, and becomes the calm card if it can't be laid out.
        var font = this.FindResource("TextFont") as FontFamily ?? Application.Current?.FindResource("TextFont") as FontFamily ?? FontFamily.Default;
        SceneCache.Find(chart, chart.ToSource(), font, null);
        var view = new DiagramView { Chart = chart, Source = source, Margin = new Thickness(0, 6) };
        if (Compact) view.MaxHeight = CompactDiagramMaxHeight;
        return view;
    }

    Control SvgBlock(string source)
    {
        var drawing = SafeSvg.Clean(source, new SafeSvgOptions { FontFamily = SvgView.Font });
        if (drawing.Problem is { } problem) return new DiagramCard(problem, source);
        using (var hold = SvgPictures.Hold(drawing.Svg!))
            if (hold is null) return new DiagramCard("Study Stash couldn't draw this SVG.", source);
        var view = new SvgView { Source = source, Margin = new Thickness(0, 6) };
        if (Compact) view.MaxHeight = CompactDiagramMaxHeight;
        return view;
    }

    /// <summary>A diagram whose fence hasn't closed yet: a quiet line until the rest arrives.</summary>
    Control Pending()
    {
        var t = Text("TextFont", Mac ? 13 : 14, 1.4);
        t.Text = DrawingWords;
        t.Bind(TextBlock.ForegroundProperty, t.GetResourceObservable("Fg3"));
        return t;
    }

    public const string DrawingWords = "Drawing the diagram…";

    /// <summary>Takes a kept diagram out of the page it was on, so it can go on the new one.</summary>
    static void Detach(Control control)
    {
        switch (control.Parent)
        {
            case Panel panel:
                panel.Children.Remove(control);
                break;
            case Decorator decorator:
                decorator.Child = null;
                break;
            case ContentControl content:
                content.Content = null;
                break;
        }
    }

    [GeneratedRegex(@"^\s*\*\*[^*]+\*\*\s*[:—–-]")]
    private static partial Regex TermFirst();

    /// <summary>The Definitions section, or any list whose every item starts "**term**: …".</summary>
    static bool IsDefinitions(string section, ListBlock list) =>
        !list.IsOrdered && list.Count > 0 && (section.Equals("Definitions", StringComparison.OrdinalIgnoreCase)
            || list.OfType<ListItemBlock>().All(i => i.FirstOrDefault() is ParagraphBlock p && TermFirst().IsMatch(Source(p))))
        && list.OfType<ListItemBlock>().All(i => i.FirstOrDefault() is ParagraphBlock p && p.Inline?.FirstChild is EmphasisInline { DelimiterCount: 2 });

    static string Source(ParagraphBlock p) => p.Inline is null ? "" : string.Concat(p.Inline.Descendants<LiteralInline>().Select(l => l.Content.ToString()));

    Control Definitions(ListBlock list)
    {
        var rows = new StackPanel();
        foreach (var item in list.OfType<ListItemBlock>())
        {
            if (item.FirstOrDefault() is not ParagraphBlock p || p.Inline?.FirstChild is not EmphasisInline term) continue;
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("120,16,*"), Margin = new Thickness(0) };
            var cell = new Border { BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 10), Child = row };
            cell.Bind(Border.BorderBrushProperty, cell.GetResourceObservable("Sep"));
            var name = Text("TextFont", 14, 1.5);
            name.FontWeight = FontWeight.SemiBold;
            name.Text = Plain(term);
            var meaning = Secondary(14);
            Fill(meaning, p.Inline, skip: term, trimLead: true, fgKey: "Fg2");
            Grid.SetColumn(meaning, 2);
            row.Children.Add(name);
            row.Children.Add(meaning);
            rows.Children.Add(cell);
        }
        return rows;
    }

    Control List(ListBlock list)
    {
        var items = new StackPanel { Spacing = 6 };
        int n = int.TryParse(list.OrderedStart, out int start) ? start : 1;
        foreach (var item in list.OfType<ListItemBlock>())
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,12,*") };
            row.Children.Add(Marker(list.IsOrdered ? $"{n++}" : "•"));
            var content = new StackPanel { Spacing = 6 };
            foreach (var inner in item)
                if (BlockControl(inner) is { } c) content.Children.Add(c);
            Grid.SetColumn(content, 2);
            row.Children.Add(content);
            items.Children.Add(row);
        }
        return items;
    }

    /// <summary>
    /// A table, in the Definitions' calm style: its header in semibold, a hairline above each row, no boxes. Columns
    /// share the width by how much their longest cell says (so a short "Normal" column doesn't take a third of it), each
    /// wraps its words, keeps the alignment the Markdown gave it, and typesets the formulas in its cells.
    /// </summary>
    Control TableBlock(Table table)
    {
        var rows = table.OfType<TableRow>().ToList();
        int columns = rows.Count == 0 ? 0 : rows.Max(r => r.Count);
        if (columns == 0) return new StackPanel();
        var weights = new double[columns];
        foreach (var row in rows)
            for (int c = 0; c < row.Count; c++)
                weights[c] = Math.Max(weights[c], Math.Clamp(CellText(row[c]).Length + 4, 10, 40));
        string widths = string.Join(",", weights.Select(w => $"{w.ToString(System.Globalization.CultureInfo.InvariantCulture)}*"));
        double size = Compact ? BodySize : 14;
        var stack = new StackPanel();
        foreach (var row in rows)
        {
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions(widths) };
            for (int c = 0; c < row.Count; c++)
            {
                var cell = Text("TextFont", size, 1.5);
                cell.Margin = new Thickness(0, 0, c < columns - 1 ? 16 : 0, 0);
                if (row.IsHeader) cell.FontWeight = FontWeight.SemiBold;
                cell.TextAlignment = c < table.ColumnDefinitions.Count ? table.ColumnDefinitions[c].Alignment switch
                {
                    TableColumnAlign.Center => TextAlignment.Center,
                    TableColumnAlign.Right => TextAlignment.Right,
                    _ => TextAlignment.Left,
                } : TextAlignment.Left;
                if (row[c] is TableCell { Count: > 0 } tc && tc[0] is ParagraphBlock p) Fill(cell, p.Inline);
                Grid.SetColumn(cell, c);
                grid.Children.Add(cell);
            }
            var line = new Border { BorderThickness = new Thickness(0, row.IsHeader ? 0 : 1, 0, 0), Padding = new Thickness(0, row.IsHeader ? 0 : 8, 0, 8), Child = grid };
            line.Bind(Border.BorderBrushProperty, line.GetResourceObservable("Sep"));
            stack.Children.Add(line);
        }
        return stack;
    }

    static string CellText(MdBlock cell) => string.Concat(cell.Descendants<ParagraphBlock>().Select(p => Plain(p.Inline)))
        + string.Concat(cell.Descendants<ParagraphBlock>().SelectMany(p => p.Inline?.Descendants<MathInline>() ?? []).Select(m => m.Content.ToString()));

    Control Code(string text)
    {
        var t = new TextBlock { Text = text.TrimEnd(), FontFamily = new FontFamily("SF Mono, Menlo, Cascadia Mono, Consolas, monospace"), FontSize = 13, LineHeight = 19.5, TextWrapping = TextWrapping.Wrap };
        t.Bind(TextBlock.ForegroundProperty, t.GetResourceObservable("Fg"));
        var box = new Border { Padding = new Thickness(12, 10), CornerRadius = new CornerRadius(Mac ? 8 : 4), Child = t };
        box.Bind(Border.BackgroundProperty, box.GetResourceObservable("Fill2"));
        return box;
    }

    static string Plain(ContainerInline? inline) => inline is null ? "" : string.Concat(inline.Descendants<LiteralInline>().Select(l => l.Content.ToString()))
        + string.Concat(inline.Descendants<CodeInline>().Select(c => c.Content));

    /// <summary>A paragraph's text, with bold, italics, code and math kept. <paramref name="fgKey"/> is the resource
    /// an inline formula's colour follows — null keeps <see cref="MathView"/>'s own default (Fg).</summary>
    static void Fill(TextBlock t, ContainerInline? inline, MdInline? skip = null, bool trimLead = false, string? fgKey = null)
    {
        t.Inlines ??= [];
        if (inline is null) return;
        bool lead = trimLead;
        foreach (var piece in Runs(inline, skip, null, null, t.FontSize, fgKey))
        {
            if (lead)
            {
                if (piece is Run run)
                {
                    string trimmed = run.Text?.TrimStart(' ', ':', '—', '–', '-') ?? "";
                    if (trimmed.Length == 0) continue;
                    run.Text = char.ToUpperInvariant(trimmed[0]) + trimmed[1..];
                }
                lead = false;
            }
            t.Inlines.Add(piece);
        }
    }

    /// <summary>The text as runs (or an inline formula's <see cref="InlineUIContainer"/>); weight and style are set
    /// only where the Markdown changes them, so a heading's runs stay as heavy as the heading.</summary>
    static IEnumerable<Inline> Runs(ContainerInline container, MdInline? skip, FontWeight? weight, FontStyle? style, double bodySize, string? fgKey)
    {
        for (var i = container.FirstChild; i is not null; i = i.NextSibling)
        {
            if (ReferenceEquals(i, skip)) continue;
            switch (i)
            {
                case MathInline m:
                    yield return MathInlineOf(m.Content.ToString(), bodySize, fgKey);
                    break;
                case LiteralInline l:
                    var run = new Run(l.Content.ToString());
                    if (weight is { } w) run.FontWeight = w;
                    if (style is { } st) run.FontStyle = st;
                    yield return run;
                    break;
                case EmphasisInline e:
                    foreach (var r in Runs(e, skip, e.DelimiterCount >= 2 ? FontWeight.SemiBold : weight, e.DelimiterCount == 1 ? FontStyle.Italic : style, bodySize, fgKey)) yield return r;
                    break;
                case CodeInline c:
                    yield return new Run(c.Content) { FontFamily = new FontFamily("SF Mono, Menlo, Cascadia Mono, Consolas, monospace"), FontSize = 14 };
                    break;
                case LineBreakInline:
                    yield return new Run(" ");
                    break;
                case LinkInline link:
                    foreach (var r in Runs(link, skip, weight, style, bodySize, fgKey)) yield return r;
                    break;
                case ContainerInline other:
                    foreach (var r in Runs(other, skip, weight, style, bodySize, fgKey)) yield return r;
                    break;
            }
        }
    }

    /// <summary>An inline <c>$…$</c> formula, typeset on the text's baseline; one CSharpMath can't typeset, or one
    /// too long to try, falls back to its plain source in mono (Fg2) rather than an empty gap.</summary>
    static Inline MathInlineOf(string latex, double bodySize, string? fgKey)
    {
        latex = latex.Trim();
        if (latex.Length == 0 || latex.Length > MaxFormulaChars) return MonoFallback("$" + latex + "$");
        var mv = new MathView { Latex = latex, Display = false, Size = bodySize * InlineMathFactor };
        mv.Bind(MathView.ForegroundProperty, mv.GetResourceObservable(fgKey ?? "Fg"));
        mv.Measure(Size.Infinity);
        if (mv.ErrorMessage is not null) return MonoFallback("$" + latex + "$");
        return new InlineUIContainer(mv);
    }

    static Run MonoFallback(string text)
    {
        var r = new Run(text) { FontFamily = new FontFamily("SF Mono, Menlo, Cascadia Mono, Consolas, monospace") };
        r.Bind(Run.ForegroundProperty, r.GetResourceObservable("Fg2"));
        return r;
    }
}
