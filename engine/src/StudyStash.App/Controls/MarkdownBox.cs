using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Threading;
using Avalonia.Utilities;
using Markdig;
using Markdig.Extensions.Mathematics;
using Markdig.Extensions.TaskLists;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using MdInline = Markdig.Syntax.Inlines.Inline;

namespace StudyStash.App.Controls;

/// <summary>How a character of Markdown is drawn while it's typed.</summary>
[Flags]
enum MdLook : ushort
{
    None = 0,
    Bold = 1,
    Italic = 2,
    Code = 4,
    Strike = 8,
    Link = 16,
    Maths = 32,
    /// <summary>One of Markdown's own marks (the ** round bold, a link's brackets and address): dimmed, or hidden
    /// when the caret is in another line.</summary>
    Mark = 64,
}

/// <summary>Where Markdown's formatting falls in a piece of text, character by character, as the notes will read it
/// (Markdig, the same reader the notes' page uses, so what looks bold while it's typed is bold on the page).</summary>
static class MarkdownLook
{
    static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseEmphasisExtras().UseMathematics().UseAutoLinks().UseTaskLists().UsePreciseSourceLocation().Build();

    /// <summary>How each character of <paramref name="text"/> is drawn; null when none of it is formatted.
    /// <paramref name="blocks"/>: the text is whole notes, so a heading's line is heavy and a code block mono too.</summary>
    public static MdLook[]? Of(string text, bool blocks)
    {
        if (text.Length == 0) return null;
        var looks = new MdLook[text.Length];
        bool any = false;
        void Set(int from, int to, MdLook look)
        {
            from = Math.Max(0, from);
            to = Math.Min(text.Length - 1, to);
            for (int i = from; i <= to; i++) looks[i] |= look;
            any |= from <= to && look != MdLook.None;
        }
        void Clear(int from, int to, MdLook look)
        {
            for (int i = Math.Max(0, from); i <= Math.Min(text.Length - 1, to); i++) looks[i] &= ~look;
        }
        void Walk(ContainerInline container)
        {
            for (var i = container.FirstChild; i is not null; i = i.NextSibling)
            {
                var (start, end) = (i.Span.Start, i.Span.End);
                if (start < 0 || end < start) continue;
                switch (i)
                {
                    case EmphasisInline e:
                        int n = e.DelimiterCount;
                        var look = e.DelimiterChar switch
                        {
                            '*' or '_' => n >= 2 ? MdLook.Bold : MdLook.Italic,
                            '~' when n >= 2 => MdLook.Strike,
                            _ => MdLook.None,
                        };
                        Set(start + n, end - n, look);
                        Set(start, start + n - 1, MdLook.Mark);
                        Set(end - n + 1, end, MdLook.Mark);
                        Walk(e);
                        break;
                    case CodeInline code:
                        Set(start, end, MdLook.Code);
                        Set(start, start + code.DelimiterCount - 1, MdLook.Mark);
                        Set(end - code.DelimiterCount + 1, end, MdLook.Mark);
                        break;
                    case MathInline maths:
                        Set(start, end, MdLook.Maths);
                        Set(start, start + maths.DelimiterCount - 1, MdLook.Mark);
                        Set(end - maths.DelimiterCount + 1, end, MdLook.Mark);
                        break;
                    case LinkInline { IsImage: false } link when link.IsAutoLink:
                        Set(start, end, MdLook.Link);
                        break;
                    case LinkInline { IsImage: false } link:
                        // "[" and "](address)" are marks; what's between the brackets is the link.
                        Set(start, end, MdLook.Mark);
                        if (link.FirstChild is { } first && link.LastChild is { } last && first.Span.Start >= 0)
                        {
                            Clear(first.Span.Start, last.Span.End, MdLook.Mark);
                            Set(first.Span.Start, last.Span.End, MdLook.Link);
                        }
                        Walk(link);
                        break;
                    case AutolinkInline:
                        Set(start, end, MdLook.Link);
                        Set(start, start, MdLook.Mark);
                        Set(end, end, MdLook.Mark);
                        break;
                    case TaskList:
                        Set(start, end, MdLook.Mark);
                        break;
                    case ContainerInline other:
                        Walk(other);
                        break;
                }
            }
        }
        var doc = Markdig.Markdown.Parse(text, Pipeline);
        foreach (var block in doc.Descendants<Block>())
        {
            switch (block)
            {
                case HeadingBlock h when blocks:
                    Set(h.Span.Start, h.Span.End, MdLook.Bold);
                    int words = h.Inline?.FirstChild?.Span.Start is int s and >= 0 ? s : h.Span.End + 1;
                    Set(h.Span.Start, words - 1, MdLook.Mark);
                    break;
                case FencedCodeBlock fence when blocks:
                    // Its fences are marks; what's between them is code, read as it's written.
                    Set(fence.Span.Start, fence.Span.End, MdLook.Code | MdLook.Mark);
                    foreach (var line in fence.Lines.Lines.Take(fence.Lines.Count))
                        if (line.Slice.Length > 0) Clear(line.Slice.Start, line.Slice.End, MdLook.Mark);
                    continue;
                case CodeBlock code when blocks:
                    Set(code.Span.Start, code.Span.End, MdLook.Code);
                    continue;
                case ThematicBreakBlock rule when blocks:
                    Set(rule.Span.Start, rule.Span.End, MdLook.Mark);
                    break;
                case ListItemBlock item when blocks:
                    // The bullet or number in front of the item's words.
                    int content = item.Count > 0 && item[0].Span.Start > item.Span.Start ? item[0].Span.Start : item.Span.Start + 1;
                    Set(item.Span.Start, content - 1, MdLook.Mark);
                    break;
            }
            if (block is LeafBlock { Inline: { } inline }) Walk(inline);
        }
        return any ? looks : null;
    }
}

/// <summary>The text of a <see cref="MarkdownBox"/>, laid out with its Markdown's formatting: the field's own caret,
/// selection and hit-testing work on the very layout that's drawn, so the caret sits where the bold letters are.</summary>
sealed class MarkdownPresenter : TextPresenter
{
    protected override Type StyleKeyOverride => typeof(TextPresenter);

    public MarkdownBox? Box { get; init; }

    /// <summary>The text is laid out again (its formatting or its marks' showing changed).</summary>
    public void Refresh()
    {
        InvalidateTextLayout();
        InvalidateMeasure();
        InvalidateVisual();
    }

    protected override TextLayout CreateTextLayout()
    {
        var plain = base.CreateTextLayout();
        string text = Text ?? "";
        // While a word is composed (an input method's preedit) or a password is hidden, the field's own layout.
        if (Box is not { } box || text.Length == 0 || !string.IsNullOrEmpty(PreeditText) || (PasswordChar != default && !RevealPassword)) return plain;
        var typeface = new Typeface(FontFamily, FontStyle, FontWeight, FontStretch);
        if (box.RunStyles(text, typeface, FontSize, Foreground, FontFeatures) is not { } styles) return plain;
        var layout = new TextLayout(text, typeface, FontSize, Foreground, TextAlignment, TextWrapping, null, null, FlowDirection,
            plain.MaxWidth, plain.MaxHeight, LineHeight, LetterSpacing, 0, FontFeatures, styles);
        plain.Dispose();
        return layout;
    }
}

/// <summary>
/// A text field that shows the Markdown typed in it formatted, as Notion, Bear or Obsidian do: **bold** is bold,
/// *italic* italic, `code` mono, ~~struck~~ struck through, a [link](address) a link, $maths$ set apart, and the marks
/// themselves dimmed (or, with <see cref="HideMarks"/>, hidden while the caret is in another field). What's typed stays
/// Markdown, exactly as typed. ⌘B and ⌘I (Ctrl on Windows) put bold or italic marks round what's selected, or take them
/// off. With <see cref="WholeNotes"/>, it's a lecture's whole notes: headings are heavy and code blocks mono too,
/// Return carries a list on (and on an empty item ends it), and Tab and Shift-Tab move a list's item in and out.
/// </summary>
public class MarkdownBox : TextBox
{
    protected override Type StyleKeyOverride => typeof(TextBox);

    public static readonly StyledProperty<bool> HideMarksProperty = AvaloniaProperty.Register<MarkdownBox, bool>(nameof(HideMarks));
    public static readonly StyledProperty<bool> WholeNotesProperty = AvaloniaProperty.Register<MarkdownBox, bool>(nameof(WholeNotes));

    /// <summary>Markdown's marks are hidden while the caret is in some other field (they show, dimmed, in the one
    /// being typed in): a line of notes reads as it will once it's written.</summary>
    public bool HideMarks
    {
        get => GetValue(HideMarksProperty);
        set => SetValue(HideMarksProperty, value);
    }

    /// <summary>The field holds whole notes, not one line of them.</summary>
    public bool WholeNotes
    {
        get => GetValue(WholeNotesProperty);
        set => SetValue(WholeNotesProperty, value);
    }

    /// <summary>The whole line is code (it's inside a ``` block): mono, nothing in it formatted.</summary>
    internal bool Code
    {
        get => code;
        set => Set(ref code, value);
    }

    /// <summary>The whole line is drawn faint (a ticked checkbox's words, a code block's fence).</summary>
    internal bool Muted
    {
        get => muted;
        set => Set(ref muted, value);
    }

    /// <summary>The whole line is struck through (a ticked checkbox's words).</summary>
    internal bool Struck
    {
        get => struck;
        set => Set(ref struck, value);
    }

    void Set(ref bool field, bool value)
    {
        if (field == value) return;
        field = value;
        presenter?.Refresh();
    }

    bool code, muted, struck, marksShown = true;
    MarkdownPresenter? presenter;

    public const string MonoFont = "SF Mono, Menlo, Cascadia Mono, Consolas, monospace";
    static readonly FontFamily Mono = new(MonoFont);

    public MarkdownBox()
    {
        Template = FieldTemplate;
        ActualThemeVariantChanged += (_, _) => presenter?.Refresh();
    }

    /// <summary>The field's look: the theme's text field reduced to what the app's bare fields show (no frame of its
    /// own), round a <see cref="MarkdownPresenter"/> in place of the plain one.</summary>
    static readonly FuncControlTemplate<TextBox> FieldTemplate = new((box, scope) =>
    {
        var presenter = new MarkdownPresenter { Name = "PART_TextPresenter", Box = box as MarkdownBox };
        presenter.Bind(TextPresenter.TextProperty, new TemplateBinding(TextProperty) { Mode = BindingMode.TwoWay });
        foreach (var (mine, its) in new (AvaloniaProperty, AvaloniaProperty)[]
        {
            (TextPresenter.CaretIndexProperty, CaretIndexProperty), (TextPresenter.SelectionStartProperty, SelectionStartProperty),
            (TextPresenter.SelectionEndProperty, SelectionEndProperty), (TextPresenter.TextAlignmentProperty, TextAlignmentProperty),
            (TextPresenter.TextWrappingProperty, TextWrappingProperty), (TextPresenter.LineHeightProperty, LineHeightProperty),
            (TextPresenter.LetterSpacingProperty, LetterSpacingProperty), (TextPresenter.PasswordCharProperty, PasswordCharProperty),
            (TextPresenter.RevealPasswordProperty, RevealPasswordProperty), (TextPresenter.SelectionBrushProperty, SelectionBrushProperty),
            (TextPresenter.SelectionForegroundBrushProperty, SelectionForegroundBrushProperty), (TextPresenter.CaretBrushProperty, CaretBrushProperty),
            (TextPresenter.CaretBlinkIntervalProperty, CaretBlinkIntervalProperty),
            (HorizontalAlignmentProperty, HorizontalContentAlignmentProperty), (VerticalAlignmentProperty, VerticalContentAlignmentProperty),
        })
            presenter.Bind(mine, new TemplateBinding(its));
        presenter.RegisterInNameScope(scope);
        var placeholder = new TextBlock { Name = "PART_Placeholder", IsHitTestVisible = false };
        foreach (var (mine, its) in new (AvaloniaProperty, AvaloniaProperty)[]
        {
            (TextBlock.TextProperty, PlaceholderTextProperty), (TextBlock.ForegroundProperty, PlaceholderForegroundProperty),
            (TextBlock.TextAlignmentProperty, TextAlignmentProperty), (TextBlock.TextWrappingProperty, TextWrappingProperty),
            (TextBlock.LineHeightProperty, LineHeightProperty),
            (HorizontalAlignmentProperty, HorizontalContentAlignmentProperty), (VerticalAlignmentProperty, VerticalContentAlignmentProperty),
        })
            placeholder.Bind(mine, new TemplateBinding(its));
        placeholder.Bind(IsVisibleProperty, new TemplateBinding(TextProperty) { Converter = StringConverters.IsNullOrEmpty });
        placeholder.RegisterInNameScope(scope);
        var scroller = new ScrollViewer { Name = "PART_ScrollViewer", Content = new Panel { Children = { placeholder, presenter } } };
        scroller.Bind(ScrollViewer.HorizontalScrollBarVisibilityProperty, new TemplateBinding(ScrollViewer.HorizontalScrollBarVisibilityProperty));
        scroller.Bind(ScrollViewer.VerticalScrollBarVisibilityProperty, new TemplateBinding(ScrollViewer.VerticalScrollBarVisibilityProperty));
        scroller.Bind(MarginProperty, new TemplateBinding(PaddingProperty));
        scroller.RegisterInNameScope(scope);
        var frame = new Border { Name = "PART_BorderElement" };
        foreach (var (mine, its) in new (AvaloniaProperty, AvaloniaProperty)[]
        {
            (Border.BackgroundProperty, BackgroundProperty), (Border.BorderBrushProperty, BorderBrushProperty),
            (Border.BorderThicknessProperty, BorderThicknessProperty), (Border.CornerRadiusProperty, CornerRadiusProperty),
        })
            frame.Bind(mine, new TemplateBinding(its));
        frame.RegisterInNameScope(scope);
        var inside = new Border { Child = scroller };
        inside.Bind(MarginProperty, new TemplateBinding(BorderThicknessProperty));
        return new Panel { Children = { frame, inside } };
    });

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        presenter = e.NameScope.Find<MarkdownPresenter>("PART_TextPresenter");
        base.OnApplyTemplate(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsFocusedProperty || change.Property == HideMarksProperty)
        {
            // After the click that brought the caret here has found its letter in the text as it was drawn (marks
            // hidden), not in the wider text with them showing.
            Dispatcher.UIThread.Post(() =>
            {
                bool shown = !HideMarks || IsFocused;
                if (shown == marksShown) return;
                marksShown = shown;
                presenter?.Refresh();
            }, DispatcherPriority.Input);
        }
        else if (change.Property == WholeNotesProperty) presenter?.Refresh();
    }

    /// <summary>Whether the marks show now (for a test).</summary>
    internal bool MarksShown => marksShown;

    /// <summary>How each run of the text is drawn, over the field's own font: null when it's all plain.</summary>
    internal List<ValueSpan<TextRunProperties>>? RunStyles(string text, Typeface typeface, double size, IBrush? foreground, FontFeatureCollection? features)
    {
        var looks = code ? null : MarkdownLook.Of(text, WholeNotes);
        if (looks is null && !code && !muted && !struck) return null;
        IBrush? Brush(string key) => this.FindResource(ActualThemeVariant, key) as IBrush;
        IBrush? subtle = Brush("InkSubtle") ?? foreground, accent = Brush("Accent") ?? foreground, fill = Brush("Fill2");
        var styles = new List<ValueSpan<TextRunProperties>>();
        int at = 0;
        while (at < text.Length)
        {
            var look = looks?[at] ?? MdLook.None;
            int end = at + 1;
            while (end < text.Length && (looks?[end] ?? MdLook.None) == look) end++;
            if (look != MdLook.None || code || muted || struck)
            {
                var family = typeface.FontFamily;
                var style = typeface.Style;
                var weight = typeface.Weight;
                double em = size;
                IBrush? brush = muted ? subtle : foreground, background = null;
                TextDecorationCollection? lines = struck ? TextDecorations.Strikethrough : null;
                if (code || look.HasFlag(MdLook.Code))
                {
                    family = Mono;
                    em = size * 0.88;
                    if (!code && !look.HasFlag(MdLook.Mark)) background = fill;
                }
                else if (look.HasFlag(MdLook.Maths))
                {
                    family = this.FindResource("SerifFont") as FontFamily ?? family;
                    style = FontStyle.Italic;
                }
                if (look.HasFlag(MdLook.Bold)) weight = Heavier(weight);
                if (look.HasFlag(MdLook.Italic)) style = FontStyle.Italic;
                if (look.HasFlag(MdLook.Strike)) lines = TextDecorations.Strikethrough;
                if (look.HasFlag(MdLook.Link))
                {
                    brush = accent;
                    lines = TextDecorations.Underline;
                }
                if (look.HasFlag(MdLook.Mark))
                {
                    lines = null;
                    background = null;
                    brush = subtle;
                    if (!marksShown)
                    {
                        // Out of sight and taking no room, but still there to be typed through.
                        brush = Brushes.Transparent;
                        em = 0.1;
                    }
                }
                styles.Add(new ValueSpan<TextRunProperties>(at, end - at,
                    new GenericTextRunProperties(new Typeface(family, style, weight, typeface.Stretch), em, lines, brush, background, BaselineAlignment.Baseline, null, features)));
            }
            at = end;
        }
        return styles.Count == 0 ? null : styles;
    }

    /// <summary>Bold within text of this weight: a step heavier, however heavy it is already.</summary>
    static FontWeight Heavier(FontWeight weight) => (int)weight < 600 ? FontWeight.Bold : (int)weight < 800 ? FontWeight.ExtraBold : FontWeight.Black;

    protected override void OnKeyDown(KeyEventArgs e)
    {
        bool command = e.KeyModifiers == (OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control);
        if (command && e.Key is Key.B or Key.I && !IsReadOnly)
        {
            e.Handled = true;
            Toggle(e.Key == Key.B ? 2 : 1);
            return;
        }
        if (WholeNotes && AcceptsReturn && !IsReadOnly)
        {
            if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None && ListReturn())
            {
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Tab && e.KeyModifiers is KeyModifiers.None or KeyModifiers.Shift && ListIndent(e.KeyModifiers == KeyModifiers.Shift))
            {
                e.Handled = true;
                return;
            }
        }
        base.OnKeyDown(e);
    }

    /// <summary>
    /// ⌘B (<paramref name="stars"/> 2) or ⌘I (1): what's selected gets bold or italic marks round it, or loses them
    /// when it has them. With nothing selected, the word the caret is in is made bold or italic (or plain again); out
    /// of a word, the marks go in at the caret with the caret between them, so what's typed next is bold or italic,
    /// and the key again takes them away.
    /// </summary>
    public void Toggle(int stars)
    {
        string text = Text ?? "";
        int from = Math.Clamp(Math.Min(SelectionStart, SelectionEnd), 0, text.Length), to = Math.Clamp(Math.Max(SelectionStart, SelectionEnd), 0, text.Length);
        string mark = new('*', stars);
        if (from == to)
        {
            if (from >= stars && to + stars <= text.Length && text[(from - stars)..from] == mark && text[to..(to + stars)] == mark)
            {
                // The marks just put in, nothing typed between them: they go.
                Replace(from - stars, to + stars, "");
                Select(from - stars, from - stars);
                return;
            }
            while (from > 0 && Wordy(text[from - 1])) from--;
            while (to < text.Length && Wordy(text[to])) to++;
        }
        // The spaces at a selection's ends (a double-clicked word takes its space) stay outside the marks, and a
        // selection that took in its marks ("**bold**", all of it) is the words inside them.
        while (from < to && text[from] == ' ') from++;
        while (to > from && text[to - 1] == ' ') to--;
        int lead = 0, trail = 0;
        while (from + lead < to && text[from + lead] == '*') lead++;
        while (to - trail - 1 >= from + lead && text[to - trail - 1] == '*') trail++;
        if (Math.Min(lead, trail) is int inner and > 0 && from + inner < to - inner)
        {
            from += inner;
            to -= inner;
        }
        if (from < to && Marked(text, from, to) is { } has && (stars == 2 ? has >= 2 : has is 1 or 3))
        {
            // Already bold (or italic): its marks come off.
            Replace(from - stars, to + stars, text[from..to]);
            Select(from - stars, to - stars);
        }
        else
        {
            Replace(from, to, mark + text[from..to] + mark);
            Select(from + stars, to + stars);
        }
    }

    /// <summary>A character a word is made of, for ⌘B on the word the caret is in.</summary>
    static bool Wordy(char c) => char.IsLetterOrDigit(c) || c is '\'' or '’' or '_';

    /// <summary>Puts <paramref name="with"/> in place of [from, to), as typing it would (so ⌘Z takes it back).</summary>
    void Replace(int from, int to, string with)
    {
        SelectionStart = from;
        SelectionEnd = to;
        CaretIndex = to;
        SelectionStart = from;
        SelectedText = with;
    }

    void Select(int from, int to)
    {
        SelectionStart = from;
        SelectionEnd = to;
        CaretIndex = to;
        SelectionStart = from;
    }

    /// <summary>How many *s stand on both sides of [from, to) (the fewer of the two), or null when there are none.</summary>
    static int? Marked(string text, int from, int to)
    {
        int before = 0, after = 0;
        while (from - before - 1 >= 0 && text[from - before - 1] == '*') before++;
        while (to + after < text.Length && text[to + after] == '*') after++;
        int n = Math.Min(before, after);
        return n == 0 ? null : n;
    }

    /// <summary>Where the line that <paramref name="at"/> is in starts and ends (before its line break).</summary>
    static (int Start, int End) LineAt(string text, int at)
    {
        at = Math.Clamp(at, 0, text.Length);
        int start = at == 0 ? 0 : text.LastIndexOf('\n', at - 1) + 1;
        int end = text.IndexOf('\n', at);
        if (end < 0) end = text.Length;
        if (end > start && text[end - 1] == '\r') end--;
        return (start, end);
    }

    static int Indent(string line) => line.Length - line.TrimStart(' ').Length;

    /// <summary>Return in whole notes, in a list's item: the next item (what's after the caret goes into it), or,
    /// on an empty item, the list ends there (or, in a list inside another, the item moves out a level).</summary>
    bool ListReturn()
    {
        string text = Text ?? "";
        if (SelectionStart != SelectionEnd) return false;
        int caret = Math.Clamp(CaretIndex, 0, text.Length);
        var (start, end) = LineAt(text, caret);
        string line = text[start..end];
        if (ListLine.Lead(line) is not { } lead || caret - start < lead.Length) return false;
        if (line[lead.Length..].Trim().Length == 0)
        {
            if (Indent(line) > 0) return ListIndent(outward: true);
            Replace(start, start + lead.Length, "");
            Select(start, start);
            return true;
        }
        string newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        string next = newline + ListLine.Next(lead);
        Replace(caret, caret, next);
        Select(caret + next.Length, caret + next.Length);
        return true;
    }

    /// <summary>Tab in a list's item in whole notes: it moves in under the item above at its level; Shift-Tab: out to
    /// the level of the item it's under.</summary>
    bool ListIndent(bool outward)
    {
        string text = Text ?? "";
        int caret = Math.Clamp(CaretIndex, 0, text.Length);
        var (start, end) = LineAt(text, caret);
        string line = text[start..end];
        if (ListLine.Lead(line) is null) return false;
        int indent = Indent(line), to = outward ? 0 : indent;
        for (int s = start; s > 0;)
        {
            var (ps, pe) = LineAt(text, s - 1);
            s = ps;
            string above = text[ps..pe];
            if (above.Trim().Length == 0) continue;
            if (ListLine.Lead(above) is not { } lead) break;
            int its = Indent(above);
            if (outward && its < indent)
            {
                to = its;
                break;
            }
            if (!outward && its <= indent)
            {
                if (its == indent) to = its + ListLine.Width(lead);
                break;
            }
        }
        if (to == indent) return true;
        Replace(start, start + indent, new string(' ', to));
        int moved = Math.Max(start + to, caret + to - indent);
        Select(moved, moved);
        return true;
    }
}

/// <summary>A line of a list in Markdown: its marker, and what the next item starts with.</summary>
static partial class ListLine
{
    [System.Text.RegularExpressions.GeneratedRegex(@"^( *)(?:([-*+])( +)(\[[ xX]\] +)?|(\d{1,9})([.)])( +))")]
    private static partial System.Text.RegularExpressions.Regex Item();

    /// <summary>What a list's item starts with ("- ", "  1. ", "- [x] "), or null when the line isn't one.</summary>
    public static string? Lead(string line) => Item().Match(line) is { Success: true } m ? m.Value : null;

    /// <summary>The next item's start after this one's: the same bullet, the next number, an empty checkbox.</summary>
    public static string Next(string lead)
    {
        var m = Item().Match(lead);
        if (!m.Success) return lead;
        if (m.Groups[2].Success) return m.Groups[1].Value + m.Groups[2].Value + " " + (m.Groups[4].Success ? "[ ] " : "");
        return m.Groups[1].Value + (long.Parse(m.Groups[5].Value, System.Globalization.CultureInfo.InvariantCulture) + 1) + m.Groups[6].Value + " ";
    }

    /// <summary>How far an item's words are in from its marker ("- " 2, "1. " 3): where an item under it starts.</summary>
    public static int Width(string lead)
    {
        var m = Item().Match(lead);
        if (!m.Success) return 2;
        return m.Groups[2].Success ? 1 + m.Groups[3].Length : m.Groups[5].Length + 1 + m.Groups[7].Length;
    }
}
