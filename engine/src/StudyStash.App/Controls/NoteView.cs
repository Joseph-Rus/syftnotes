using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.VisualTree;
using Markdig;
using Markdig.Extensions.Mathematics;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using StudyStash.App.Controls.Rich;
using StudyStash.App.Services;
using StudyStash.Core;
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

    /// <summary>Set when the notes are laid out for paper (a PDF), this many pixels to a page's height: nothing on
    /// paper scrolls, so every formula and diagram shrinks as far as it must to fit the column, a diagram no taller than
    /// a page; diagrams don't open larger; and a fence left open at the end is saved notes that can't be drawn, not an
    /// answer still arriving. NaN (the default) is the screen.</summary>
    public static readonly StyledProperty<double> PageHeightProperty = AvaloniaProperty.Register<NoteView, double>(nameof(PageHeight), double.NaN);

    /// <summary>Where a run of text links to (a Markdown link's address), for a PDF to make that text clickable.</summary>
    public static readonly AttachedProperty<string?> LinkProperty = AvaloniaProperty.RegisterAttached<NoteView, Run, string?>("Link");

    public static string? GetLink(Run run) => run.GetValue(LinkProperty);

    /// <summary>What a click on one of these notes' links does, when they need their own way (an assignment's
    /// instructions link into its saved files); unset, <see cref="OpenLink"/>.</summary>
    public static readonly StyledProperty<Action<string>?> LinkHandlerProperty = AvaloniaProperty.Register<NoteView, Action<string>?>(nameof(LinkHandler));

    public Action<string>? LinkHandler
    {
        get => GetValue(LinkHandlerProperty);
        set => SetValue(LinkHandlerProperty, value);
    }

    /// <summary>What clicking a link does: a Canvas address opens in the browser the student reads Canvas in, where
    /// they're signed in to it; any other web page in the default browser. A link that isn't a web page or an email
    /// address (a file, a program, another app's own kind of link) does nothing: a note's links were written by an
    /// AI, a teacher or whoever wrote the page it came from. Tests swap it for one that records the address.</summary>
    public static Action<string> OpenLink { get; set; } = url =>
    {
        if (!Dialogs.IsWebLink(url)) return;
        // Not on the page's own thread: finding the browser asks the system, and starting one takes a moment.
        Task.Run(() =>
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Host.EndsWith(".instructure.com", StringComparison.OrdinalIgnoreCase)
                && Browsers.Open(Browsers.ForCanvas(), url) is null) return;
            Dialogs.OpenUrl(url);
        });
    };

    /// <summary>The class a section heading carries, and the one a table's header row carries: a page never ends
    /// just after either, leaving it cut off from what it heads.</summary>
    public const string HeadingClass = "note-heading", TableHeaderClass = "note-table-header";

    /// <summary>A compact diagram never grows past this tall in the column it sits in (a quick answer or a chat
    /// bubble); a click still opens it at full size.</summary>
    public const double CompactDiagramMaxHeight = 260;

    static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().UsePipeTables().UseEmphasisExtras().UseMathematics().UseAutoLinks().Build();

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

    public double PageHeight
    {
        get => GetValue(PageHeightProperty);
        set => SetValue(PageHeightProperty, value);
    }

    bool Print => double.IsFinite(PageHeight);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == CompactProperty || change.Property == BodyFontProperty || change.Property == BodySizeProperty
            || change.Property == BodyLineHeightProperty || change.Property == PageHeightProperty)
        {
            // Every block's type changes (a diagram on paper is another control from one on screen): none can be kept.
            blocks.Clear();
            diagrams.Clear();
            BuildWhenSeen();
        }
        else if (change.Property == MarkdownProperty)
        {
            var hold = HoldPlace();
            BuildWhenSeen();
            hold?.Invoke();
        }
    }

    /// <summary>How long a page that changed keeps the reader's place while what's new in it settles.</summary>
    static readonly TimeSpan Settles = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Notes that change while they're read (their diagrams arrive after them) keep the reader's place: the piece at the
    /// top of the page now stays just where it is on screen, whatever goes in above it, until the new pieces have their
    /// size, or the reader scrolls. Null when there's no place to keep (nothing scrolled, a compact answer, paper), and
    /// the scroll is left alone when that piece itself changed. Its pieces are the same controls, kept by
    /// <see cref="Build"/>, so a diagram's pins and folded groups stay too.
    /// </summary>
    Action? HoldPlace()
    {
        if (Compact || Print || Children.Count == 0 || this.FindAncestorOfType<ScrollViewer>() is not { } scroller || scroller.Offset.Y <= 0) return null;
        Control? anchor = null;
        double top = 0;
        foreach (var child in Children)
            if (child.TranslatePoint(default, scroller) is { } at && at.Y + child.Bounds.Height > 0)
            {
                (anchor, top) = (child, at.Y);
                break;
            }
        if (anchor is null) return null;
        return () =>
        {
            if (!Children.Contains(anchor)) return;
            var until = DateTime.UtcNow + Settles;
            double expected = scroller.Offset.Y;
            void Keep(object? sender, EventArgs e)
            {
                // The reader scrolled meanwhile (or it's had time to settle): their scroll wins.
                if (DateTime.UtcNow > until || Math.Abs(scroller.Offset.Y - expected) > 0.5 || anchor.TranslatePoint(default, scroller) is not { } now)
                {
                    scroller.LayoutUpdated -= Keep;
                    return;
                }
                if (Math.Abs(now.Y - top) <= 0.5) return;
                expected = Math.Max(0, scroller.Offset.Y + now.Y - top);
                scroller.Offset = scroller.Offset.WithY(expected);
            }
            scroller.LayoutUpdated += Keep;
        };
    }

    /// <summary>The notes changed while this view is hidden (the page keeps a view for each way of showing a lecture:
    /// its notes, the two sides of a rewrite's comparison, the plain Markdown): it's built when it shows, not before.
    /// Building is the whole cost of a lecture's notes (every formula typeset, every diagram laid out, every plot's
    /// scene made), and a hidden view built the same notes again for nobody to see.</summary>
    void BuildWhenSeen()
    {
        if (Hidden)
        {
            // What it built before is out of date and nobody sees it: let it go, and build the new when it shows.
            if (!unbuilt && Children.Count > 0)
            {
                Children.Clear();
                blocks.Clear();
                diagrams.Clear();
                previous.Clear();
            }
            unbuilt = true;
            return;
        }
        unbuilt = false;
        Build();
    }

    bool unbuilt;

    /// <summary>In a window, but out of view: hidden itself, or under something hidden, or in a window that's closed to the
    /// menu bar. One that isn't in a window at all (the notes laid out for a PDF, a view a test holds) isn't hidden: it
    /// builds at once.</summary>
    bool Hidden => VisualRoot is not null && seen is { InView: false };

    readonly Seen seen;

    public NoteView() => seen = new Seen(this, inView =>
    {
        if (inView && unbuilt) BuildWhenSeen();
    });

    /// <summary>Whether the notes are waiting to be built (the view is hidden and they changed since it was).</summary>
    internal bool Unbuilt => unbuilt;

    bool Mac => Skin.Current == SkinKind.Mac;

    /// <summary>The reading body's size in the design's own type: 16 on a Mac (New York), 15 on Windows (Segoe UI).
    /// A compact answer uses <see cref="BodySize"/> instead (<see cref="EffectiveBodySize"/>).</summary>
    double DefaultBodySize => Mac ? 16 : 15;

    /// <summary>What a display formula's size follows: the caller's <see cref="BodySize"/> in a compact answer, the
    /// design's reading size in a lecture's notes.</summary>
    double EffectiveBodySize => Compact ? BodySize : DefaultBodySize;

    TextBlock Text(string resourceFont, double size, double lineHeight)
    {
        var t = new SpokenText { FontSize = size, LineHeight = size * lineHeight, TextWrapping = TextWrapping.Wrap };
        t.Bind(TextBlock.FontFamilyProperty, t.GetResourceObservable(resourceFont));
        t.Bind(TextBlock.ForegroundProperty, t.GetResourceObservable("Fg"));
        FollowLinks(t);
        return t;
    }

    /// <summary>A compact answer's paragraph text: the caller's own font, size and line height, selectable (an answer
    /// can be copied a sentence at a time, not just as a whole).</summary>
    TextBlock CompactText()
    {
        var t = new SpokenSelectableText { FontSize = BodySize, LineHeight = BodyLineHeight, TextWrapping = TextWrapping.Wrap };
        t.Bind(TextBlock.FontFamilyProperty, t.GetResourceObservable(BodyFont ?? "TextFont"));
        t.Bind(TextBlock.ForegroundProperty, t.GetResourceObservable("Fg"));
        t.Bind(SelectableTextBlock.SelectionBrushProperty, t.GetResourceObservable("Hl"));
        FollowLinks(t);
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

    /// <summary>The last build's top-level pieces, by the Markdown each came from (and what else shaped it), so an
    /// answer arriving a few words at a time lays out only its newest block again: every block before it, the same
    /// Markdown in the same place, is kept as it was (its formulas already typeset).</summary>
    Dictionary<string, Control> blocks = [];

    void Build()
    {
        Children.Clear();
        Spacing = Compact ? 8 : Mac ? 14 : 12;
        previous = diagrams;
        diagrams = [];
        var kept = blocks;
        blocks = [];
        string markdown = source = Markdown ?? "";
        var doc = Markdig.Markdown.Parse(markdown, Pipeline);
        bool first = true;
        string section = "";
        int skipUntil = -1;
        string? above = null;
        for (int at = 0; at < doc.Count; at++)
        {
            MdBlock block = doc[at];
            if (block.Span.Start < skipUntil) continue;
            // Its Markdown runs to where the next block starts (to the end, for the last one: so it changes whenever
            // anything more arrives), or past that to its </svg> for a drawing written straight into the Markdown.
            int? svgEnd = block is HtmlBlock inline && IsSvg(inline.Lines.ToString()) ? SvgEnd(markdown, inline) : null;
            int end = Math.Max(at + 1 < doc.Count ? doc[at + 1].Span.Start : markdown.Length, svgEnd ?? 0);
            string key = $"{first}\u0001{section}\u0001{at == doc.Count - 1}\u0001{markdown[block.Span.Start..Math.Clamp(end, block.Span.Start, markdown.Length)]}";
            for (int n = 2; blocks.ContainsKey(key); n++) key = $"{n}\u0001{key}";
            // A diagram takes the bold line just above it as its title (the diagram pass writes one there).
            caption = above;
            above = block is ParagraphBlock { Inline: { } line } && BoldLine(line) is { } bold ? bold : null;
            if (kept.Remove(key, out var same))
            {
                // Its diagrams stay its own: none is handed on to a later block drawing the same thing.
                foreach (var (drawn, diagram) in previous.Where(d => d.Value == same || d.Value.GetLogicalAncestors().Contains(same)).ToList())
                {
                    previous.Remove(drawn);
                    diagrams[drawn] = diagram;
                }
                Children.Add(blocks[key] = same);
                if (block is HeadingBlock kept0) section = Plain(kept0.Inline);
                else first = false;
                if (svgEnd is int svgKept) skipUntil = svgKept;
                continue;
            }
            Control? control;
            switch (block)
            {
                case HeadingBlock h:
                    section = Plain(h.Inline);
                    var head = Text("DisplayFont", Mac ? 17 : 20, 1.25);
                    head.FontWeight = FontWeight.SemiBold;
                    head.Classes.Add(HeadingClass);
                    Fill(head, h.Inline);
                    head.Margin = Compact ? default : new Thickness(0, first ? (Mac ? 18 : 16) : (Mac ? 14 : 12), 0, 0);
                    control = head;
                    break;
                case ListBlock list when IsDefinitions(section, list):
                    control = Definitions(list);
                    break;
                case HtmlBlock html when IsSvg(html.Lines.ToString()):
                    if (SvgEnd(markdown, html) is int close)
                    {
                        skipUntil = close;
                        control = Diagram(DiagramKind.Svg, markdown[html.Span.Start..skipUntil]);
                    }
                    else control = BlockControl(block);
                    break;
                default:
                    control = BlockControl(block);
                    break;
            }
            if (control is not null) Children.Add(blocks[key] = control);
            if (block is not HeadingBlock) first = false;
            else if (!first) first = false;
        }
        previous.Clear();
    }

    /// <summary>A drawing written straight into the Markdown ends at its first blank line; it's taken to </svg>
    /// (unless a code fence comes first: then it's only what its own lines hold). Where it ends, or null.</summary>
    static int? SvgEnd(string markdown, HtmlBlock html)
    {
        int from = html.Span.Start, close = markdown.IndexOf("</svg>", from, StringComparison.OrdinalIgnoreCase);
        int fence = markdown.IndexOf("\n```", from, StringComparison.Ordinal);
        return close >= 0 && (fence < 0 || fence > close) ? close + "</svg>".Length : null;
    }

    /// <summary>The Markdown being built.</summary>
    string source = "";

    /// <summary>The bold line just above the block being built (a diagram's title, as the diagram pass writes it).</summary>
    string? caption;

    /// <summary>A paragraph that's one bold phrase and nothing else ("**The cardiac cycle**"): its words; else null.</summary>
    static string? BoldLine(ContainerInline line)
    {
        var parts = line.Where(i => i is not LineBreakInline && !(i is LiteralInline l && l.Content.IsEmptyOrWhitespace())).ToList();
        return parts is [EmphasisInline { DelimiterCount: 2 } bold] && Plain(bold) is { Length: > 0 } words ? words : null;
    }

    /// <summary>Whether a block runs to the end of the notes: an unfinished diagram there is still arriving; one
    /// earlier on never will, and says it can't be drawn.</summary>
    bool AtEnd(LeafBlock block)
    {
        if (Print) return false;
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
                // A formula still being written (an answer arriving in pieces) is typeset once its $$ closes.
                return mb.ClosingFencedCharCount == 0 && AtEnd(mb) ? Pending(FormulaWords) : DisplayMath(mb.Lines.ToString());
            case ParagraphBlock p:
                return Paragraph(p.Inline);
            case ListBlock list:
                return List(list);
            case Table table:
                return TableBlock(table);
            case FencedCodeBlock fence when KindOf(fence) is { } kind:
                // A diagram still being written (an answer arriving in pieces) is drawn once its fence closes.
                return fence.ClosingFencedCharCount == 0 && AtEnd(fence) ? Pending(DrawingWords) : Diagram(kind, fence.Lines.ToString());
            case FencedCodeBlock or CodeBlock:
                return Code(((LeafBlock)block).Lines.ToString());
            case HtmlBlock html when IsSvg(html.Lines.ToString()):
                string svg = html.Lines.ToString();
                return !svg.Contains("</svg>", StringComparison.OrdinalIgnoreCase) && AtEnd(html) ? Pending(DrawingWords) : Diagram(DiagramKind.Svg, svg);
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
                var lifted = new MathDisplay(mv, fitWhole: Print) { Margin = new Thickness(0, 2) };
                segments.Add(Print || Compact ? lifted : PlotFormula.Wrap(lifted, mv.Latex ?? ""));
                current = Body();
                split = true;
                continue;
            }
            current.Inlines!.Add(piece);
        }
        if (current.Inlines!.Count > 0 || !split) segments.Add(current);
        foreach (var segment in segments.OfType<TextBlock>()) SayFormulas(segment);
        if (segments.Count == 1) return LoneFormula(inline) is { } latex && !Print && !Compact && segments[0] is not PlotFormula ? PlotFormula.Wrap(segments[0], latex) : segments[0];
        var stack = new StackPanel { Spacing = 6 };
        foreach (var s in segments) stack.Children.Add(s);
        return stack;
    }

    /// <summary>A paragraph that's one <c>$$…$$</c> formula and nothing else: its LaTeX (it can be asked for as a
    /// plot); else null.</summary>
    static string? LoneFormula(ContainerInline inline)
    {
        var parts = inline.Where(i => i is not LineBreakInline && !(i is LiteralInline l && l.Content.IsEmptyOrWhitespace())).ToList();
        return parts is [MathInline { DelimiterCount: 2 } m] ? m.Content.ToString() : null;
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
        var display = new MathDisplay(mv, fitWhole: Print) { Margin = new Thickness(0, 4) };
        // In a lecture's notes on screen, a formula can be asked for as a plot.
        return Print || Compact ? display : PlotFormula.Wrap(display, latex);
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

    enum DiagramKind { Mermaid, Svg, Plot }

    /// <summary>Which fences are diagrams — the same rule search and the diagram repair read notes by
    /// (<see cref="NoteBlocks.KindOf"/>).</summary>
    static DiagramKind? KindOf(FencedCodeBlock fence) => NoteBlocks.KindOf(fence.Info ?? "", fence.Lines.ToString()) switch
    {
        NoteBlockKind.Mermaid => DiagramKind.Mermaid,
        NoteBlockKind.Svg => DiagramKind.Svg,
        NoteBlockKind.Plot => DiagramKind.Plot,
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
        Control made = kind switch
        {
            DiagramKind.Mermaid => ChartBlock(source),
            DiagramKind.Plot => PlotBlock(source),
            _ => SvgBlock(source),
        };
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
        var view = new DiagramView(opensLarger: !Print, fitWhole: Print) { Source = source, Caption = caption, Margin = new Thickness(0, 6) };
        view.Chart = chart;
        if (Compact) view.MaxHeight = CompactDiagramMaxHeight;
        if (Print) view.MaxHeight = PageHeight;
        return view;
    }

    /// <summary>A ```plot: drawn exactly from its formulas, to play with (<see cref="PlotView"/>); on paper, at its
    /// sliders' starting values. One that can't be read is the calm card, saying which line and why.</summary>
    Control PlotBlock(string source)
    {
        Plot plot;
        try
        {
            plot = Plot.Parse(source);
        }
        catch (PlotException e)
        {
            return new DiagramCard(e.Message, source);
        }
        return new PlotView(plot, source, still: Print) { Caption = caption, Margin = new Thickness(0, 6) };
    }

    Control SvgBlock(string source)
    {
        var drawing = SafeSvg.Clean(source, new SafeSvgOptions { FontFamily = SvgView.Font });
        if (drawing.Problem is { } problem) return new DiagramCard(problem, source);
        using (var hold = SvgPictures.Hold(drawing.Svg!))
            if (hold is null) return new DiagramCard("Study Stash couldn't draw this SVG.", source);
        var view = new SvgView(opensLarger: !Print, fitWhole: Print) { Source = source, Margin = new Thickness(0, 6) };
        if (Compact) view.MaxHeight = CompactDiagramMaxHeight;
        if (Print) view.MaxHeight = PageHeight;
        return view;
    }

    /// <summary>A diagram or formula whose fence hasn't closed yet: a quiet line until the rest arrives.</summary>
    Control Pending(string words)
    {
        var t = Text("TextFont", Mac ? 13 : 14, 1.4);
        t.Text = words;
        t.Bind(TextBlock.ForegroundProperty, t.GetResourceObservable("Fg3"));
        return t;
    }

    public const string DrawingWords = "Drawing the diagram…";
    public const string FormulaWords = "Writing the formula…";

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
            if (row.IsHeader) line.Classes.Add(TableHeaderClass);
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
        SayFormulas(t);
    }

    /// <summary>What a screen reader reads for a paragraph with formulas in it: Avalonia gives each formula's place in a
    /// text as one object-replacement character ("\uFFFC"), so such a paragraph is named with its formulas in words
    /// (<see cref="MathView.Reading"/>) in their places instead.</summary>
    static void SayFormulas(TextBlock t)
    {
        if (t.Inlines is not { Count: > 0 } inlines || !inlines.Any(i => i is InlineUIContainer { Child: MathView })) return;
        var said = new System.Text.StringBuilder();
        foreach (var i in inlines)
        {
            if (i is Run r) said.Append(r.Text);
            else if (i is InlineUIContainer { Child: MathView mv }) said.Append(' ').Append(MathView.Reading(mv.Latex)).Append(' ');
        }
        AutomationProperties.SetName(t, string.Join(' ', said.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries)));
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
                    foreach (var r in Runs(link, skip, weight, style, bodySize, fgKey))
                    {
                        if (!link.IsImage && link.Url is { Length: > 0 } url && r is Run linked)
                        {
                            linked.SetValue(LinkProperty, url);
                            linked.TextDecorations = TextDecorations.Underline;
                            linked.Bind(Run.ForegroundProperty, linked.GetResourceObservable("Accent"));
                        }
                        yield return r;
                    }
                    break;
                case ContainerInline other:
                    foreach (var r in Runs(other, skip, weight, style, bodySize, fgKey)) yield return r;
                    break;
            }
        }
    }

    /// <summary>Makes the links in <paramref name="t"/> work: a hand over one, and a click opens it. A drag that
    /// selects text in a selectable block isn't a click.</summary>
    void FollowLinks(TextBlock t)
    {
        Point? down = null;
        t.PointerMoved += (_, e) => t.Cursor = LinkAt(t, e.GetPosition(t)) is null ? null : new Cursor(StandardCursorType.Hand);
        t.PointerExited += (_, _) => t.Cursor = null;
        t.AddHandler(PointerPressedEvent, (_, e) => down = e.GetPosition(t), Avalonia.Interactivity.RoutingStrategies.Tunnel | Avalonia.Interactivity.RoutingStrategies.Bubble, true);
        t.AddHandler(PointerReleasedEvent, (_, e) =>
        {
            var at = e.GetPosition(t);
            if (e.InitialPressMouseButton != MouseButton.Left || down is not { } d || Math.Abs(d.X - at.X) + Math.Abs(d.Y - at.Y) > 4) return;
            down = null;
            if (LinkAt(t, at) is not { } url) return;
            e.Handled = true;
            (LinkHandler ?? OpenLink)(url);
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel | Avalonia.Interactivity.RoutingStrategies.Bubble, true);
    }

    /// <summary>The address of the link under <paramref name="point"/> in <paramref name="t"/>, or null.</summary>
    static string? LinkAt(TextBlock t, Point point)
    {
        if (t.Inlines is not { Count: > 0 } inlines) return null;
        var hit = t.TextLayout.HitTestPoint(point - new Point(t.Padding.Left, t.Padding.Top));
        if (!hit.IsInside) return null;
        int index = hit.TextPosition, at = 0;
        foreach (var inline in inlines)
        {
            int length = inline is Run r ? r.Text?.Length ?? 0 : 1;
            if (index < at + length) return inline is Run run ? GetLink(run) : null;
            at += length;
        }
        return null;
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
