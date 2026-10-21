using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StudyStash.App.ViewModels;

namespace StudyStash.App.Controls;

/// <summary>
/// Where a student types their own notes while a lecture records, in Markdown that reads as the notes will, the way
/// Notion, Bear or Obsidian show it. A line that starts "# ", "## " or "### " is a heading the moment the space is
/// typed; "- " (or "* ") a bullet, "1. " a numbered line, "[ ] " or "- [ ] " a checkbox that ticks, "> " a quote; Return
/// carries a list on (and on an empty item ends it), Tab and Shift-Tab move an item in and out, Backspace at a line's
/// start makes it plain text again. In the words, **bold**, *italic*, `code`, ~~struck~~, [links](address) and $maths$
/// show formatted as they're typed, their marks hidden once the caret leaves the line (<see cref="MarkdownBox"/>), and
/// ⌘B and ⌘I make what's selected bold or italic. A ``` line starts and ends a block of code. Underneath it's plain
/// Markdown, a line of text per line shown, with every mark kept (<see cref="Text"/>): that's what goes with the
/// recording, and what the notes are written round. Each line is a field of its own, so the arrows, Return, Backspace
/// at a line's start and Delete at its end move between them as they would in one piece of text.
/// </summary>
public sealed partial class TypedEditor : UserControl
{
    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<TypedEditor, string>(nameof(Text), "", defaultBindingMode: BindingMode.TwoWay);
    public static readonly StyledProperty<string> PlaceholderProperty = AvaloniaProperty.Register<TypedEditor, string>(nameof(Placeholder), "");

    enum Kind { Plain, Heading, Bullet, Numbered, Task, Quote }

    /// <summary>One line: what it is, how it starts in the text, exactly ("## ", "  - ", "3. ", "- [x] ", "> "; nothing
    /// for a plain line), the field its words are typed in, and what's drawn before them.</summary>
    sealed class Line
    {
        public Kind Kind;
        public string Lead = "";
        public required MarkdownBox Box;
        public required Grid Row;
        /// <summary>A bullet's dot, or a numbered line's number.</summary>
        public required TextBlock Marker;
        public required ToggleButton Tick;
        /// <summary>A quote's bar.</summary>
        public required Border Bar;
        /// <summary>A rule's line ("---"), drawn while the caret's elsewhere.</summary>
        public required Border Rule;
        /// <summary>How many lists it's inside, and (a numbered line) the number it shows.</summary>
        public int Depth;
        public long Number;
        /// <summary>It's inside a ``` block, or one of the block's ``` lines.</summary>
        public bool InCode, Fence;
        /// <summary>What the field held just before it became a heading, a bullet or a checkbox, until the keystroke that
        /// made it one is done: the field puts that text back once more as it finishes (Avalonia's text field hands
        /// what it typed to its presenter after its own text has changed), and it's taken off again.</summary>
        public string? Typed;
    }

    readonly StackPanel rows = new();
    readonly List<Line> lines = [];
    /// <summary>The lines are being changed from here (the text is being put together from them, or they're being
    /// made from the text): one doesn't set off the other.</summary>
    bool busy;

    [GeneratedRegex(@"^(#{1,6})[ \t]+")]
    private static partial Regex HeadingLead();

    [GeneratedRegex(@"^( *)[-*+][ \t]+\[[ xX]\](?:[ \t]+|$)")]
    private static partial Regex TaskLead();

    [GeneratedRegex(@"^( *)[-*+][ \t]+")]
    private static partial Regex BulletLead();

    [GeneratedRegex(@"^( *)(\d{1,9})([.)])[ \t]+")]
    private static partial Regex NumberLead();

    [GeneratedRegex(@"^>[ \t]+")]
    private static partial Regex QuoteLead();

    /// <summary>A checkbox typed at the start of a line or a bullet: "[ ] ", "[] " or "[x] ".</summary>
    [GeneratedRegex(@"^\[([ xX]?)\][ \t]+")]
    private static partial Regex Box();

    [GeneratedRegex(@"^\s*(?:```|~~~)")]
    private static partial Regex FenceLine();

    [GeneratedRegex(@"^\s*(?:-{3,}|\*{3,}|_{3,})\s*$")]
    private static partial Regex RuleLine();

    public TypedEditor()
    {
        ClipToBounds = false;
        Focusable = false;
        Content = rows;
        Build("");
    }

    /// <summary>The notes as text: a line per line, every heading's "#", bullet's "-" and mark kept as typed.</summary>
    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>What an empty editor says in its first line.</summary>
    public string Placeholder
    {
        get => GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    /// <summary>The lines' own fields, top to bottom.</summary>
    public IReadOnlyList<TextBox> Fields => [.. lines.Select(l => l.Box)];

    /// <summary>The checkboxes' ticks, by line (null for a line that isn't a checkbox).</summary>
    internal IReadOnlyList<ToggleButton?> Ticks => [.. lines.Select(l => l.Kind == Kind.Task ? l.Tick : null)];

    /// <summary>What each line shows before its words (a bullet's dot, a number; "" for none), for a test.</summary>
    internal IReadOnlyList<string> Markers => [.. lines.Select(l => l.Marker.IsVisible ? l.Marker.Text ?? "" : "")];

    /// <summary>The caret goes to the end of what's typed.</summary>
    public void FocusEnd()
    {
        if (lines.Count > 0) Focus(lines[^1], int.MaxValue);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty && !busy && Compose() != Normal(Text)) Build(Text);
        else if (change.Property == PlaceholderProperty) ShowPlaceholder();
    }

    static string Normal(string? text) => (text ?? "").ReplaceLineEndings("\n");

    /// <summary>What a line of text is: its kind, its start (as it's written) and its words. <paramref name="typing"/>:
    /// it's being typed, so a checkbox can start without its bullet ("[ ] ").</summary>
    static (Kind Kind, string Lead, string Words) Parse(string line, bool typing = false)
    {
        Match m;
        if ((m = HeadingLead().Match(line)).Success) return (Kind.Heading, m.Value, line[m.Length..]);
        if ((m = TaskLead().Match(line)).Success) return (Kind.Task, m.Value, line[m.Length..]);
        if ((m = BulletLead().Match(line)).Success) return (Kind.Bullet, m.Value, line[m.Length..]);
        if ((m = NumberLead().Match(line)).Success) return (Kind.Numbered, m.Value, line[m.Length..]);
        if ((m = QuoteLead().Match(line)).Success) return (Kind.Quote, m.Value, line[m.Length..]);
        if (typing && (m = Box().Match(line)).Success) return (Kind.Task, m.Groups[1].Value is "x" or "X" ? "- [x] " : "- [ ] ", line[m.Length..]);
        return (Kind.Plain, "", line);
    }

    static int Indent(string lead) => lead.Length - lead.TrimStart(' ').Length;

    static bool IsList(Kind kind) => kind is Kind.Bullet or Kind.Numbered or Kind.Task;

    string Compose() => string.Join("\n", lines.Select(l => l.Lead + (l.Box.Text ?? "")));

    /// <summary>The lines, made again from text (it was set from outside: a recording's page opening; or several
    /// lines were pasted). A block of code is code, its lines plain whatever they start with.</summary>
    void Build(string? text)
    {
        busy = true;
        try
        {
            rows.Children.Clear();
            lines.Clear();
            bool code = false;
            foreach (string line in Normal(text).Split('\n'))
            {
                bool fence = FenceLine().IsMatch(line);
                var (kind, lead, words) = code || fence ? (Kind.Plain, "", line) : Parse(line);
                Add(lines.Count, kind, lead, words);
                if (fence) code = !code;
            }
        }
        finally
        {
            busy = false;
        }
        Restyle();
    }

    /// <summary>What's typed changed: the text follows the lines.</summary>
    void Changed()
    {
        Restyle();
        busy = true;
        try
        {
            SetCurrentValue(TextProperty, Compose());
        }
        finally
        {
            busy = false;
        }
    }

    Line Add(int at, Kind kind, string lead, string words)
    {
        var box = new MarkdownBox { Text = words, AcceptsReturn = false, TextWrapping = TextWrapping.Wrap, VerticalContentAlignment = VerticalAlignment.Top, HideMarks = true };
        box.Classes.Add("bare");
        Avalonia.Automation.AutomationProperties.SetName(box, "Your notes");
        var marker = new TextBlock { VerticalAlignment = VerticalAlignment.Top, TextAlignment = TextAlignment.Left, MinWidth = 20 };
        marker.Bind(TextBlock.ForegroundProperty, marker.GetResourceObservable("InkSubtle"));
        var tick = new ToggleButton { Template = TickTemplate, Focusable = false, IsTabStop = false, Cursor = new Cursor(StandardCursorType.Hand), VerticalAlignment = VerticalAlignment.Top };
        Avalonia.Automation.AutomationProperties.SetName(tick, "Done");
        var bar = new Border { Width = 3, CornerRadius = new CornerRadius(1.5), Margin = new Thickness(1, 3, 12, 3) };
        bar.Bind(Border.BackgroundProperty, bar.GetResourceObservable("InkSubtle"));
        var rule = new Border { Height = 1, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
        rule.Bind(Border.BackgroundProperty, rule.GetResourceObservable("Line"));
        var front = new Panel { Children = { marker, tick, bar } };
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        Grid.SetColumn(box, 1);
        Grid.SetColumn(rule, 1);
        row.Children.Add(front);
        row.Children.Add(rule);
        row.Children.Add(box);
        var line = new Line { Kind = kind, Lead = lead, Box = box, Row = row, Marker = marker, Tick = tick, Bar = bar, Rule = rule };
        box.AddHandler(KeyDownEvent, (_, e) => OnKey(line, e), RoutingStrategies.Tunnel);
        box.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty && !busy)
            {
                if (line.Typed is { } before && box.Text == before) Again(line);
                else Typed(line);
            }
            else if (e.Property == IsFocusedProperty) ShowRule(line);
        };
        tick.IsCheckedChanged += (_, _) =>
        {
            if (busy || line.Kind != Kind.Task) return;
            // Ticked (or not): "[ ]" becomes "[x]" in the text, or back.
            int open = line.Lead.IndexOf('[');
            line.Lead = line.Lead[..(open + 1)] + (tick.IsChecked == true ? "x" : " ") + line.Lead[(open + 2)..];
            Changed();
        };
        lines.Insert(at, line);
        rows.Children.Insert(at, row);
        return line;
    }

    /// <summary>A checkbox's box: a rounded square, filled in ink with a tick when it's ticked.</summary>
    static readonly FuncControlTemplate<ToggleButton> TickTemplate = new((button, _) =>
    {
        var check = new Avalonia.Controls.Shapes.Path { Data = Geometry.Parse("M3.6,8.2 L6.6,11.1 L12.4,4.9"), StrokeThickness = 1.8, StrokeLineCap = PenLineCap.Round, StrokeJoin = PenLineJoin.Round };
        check.Bind(Shape.StrokeProperty, check.GetResourceObservable("Canvas"));
        var square = new Border { Width = 16, Height = 16, CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1.5), Child = check, Background = Brushes.Transparent };
        void Show()
        {
            bool on = button.IsChecked == true;
            check.IsVisible = on;
            square.Bind(Border.BorderBrushProperty, square.GetResourceObservable(on ? "Ink" : "InkSubtle"));
            if (on) square.Bind(Border.BackgroundProperty, square.GetResourceObservable("Ink"));
            else square.Background = Brushes.Transparent;
        }
        button.IsCheckedChanged += (_, _) => Show();
        Show();
        return square;
    });

    void Remove(Line line)
    {
        lines.Remove(line);
        rows.Children.Remove(line.Row);
    }

    /// <summary>Each line looks like what it is: a heading heavier and set apart (the more #s, the smaller), a list's
    /// item behind its dot, number or checkbox and set in by how deep it is, a quote behind its bar, code in mono on a
    /// tinted ground. Lines in a block of code are code, whatever they start with.</summary>
    void Restyle()
    {
        bool code = false;
        // The list items open above this line, by how far each is indented: where a line's dot and number come from.
        var open = new List<Line>();
        for (int i = 0; i < lines.Count; i++)
        {
            var l = lines[i];
            string words = l.Box.Text ?? "";
            l.Fence = l.Kind == Kind.Plain && FenceLine().IsMatch(words);
            l.InCode = code && !l.Fence;
            if (l.Fence) code = !code;
            if (l.InCode && l.Kind != Kind.Plain) Plain(l, keepLead: true);

            if (IsList(l.Kind))
            {
                int indent = Indent(l.Lead);
                while (open.Count > 0 && Indent(open[^1].Lead) > indent) open.RemoveAt(open.Count - 1);
                var sibling = open.Count > 0 && Indent(open[^1].Lead) == indent ? open[^1] : null;
                if (sibling is not null) open.RemoveAt(open.Count - 1);
                l.Depth = open.Count;
                l.Number = l.Kind != Kind.Numbered ? 0 : sibling is { Kind: Kind.Numbered } ? sibling.Number + 1 : Start(l.Lead);
                open.Add(l);
            }
            else
            {
                l.Depth = 0;
                // (A blank line between a list's items doesn't end it, as in Markdown.)
                if (l.Kind != Kind.Plain || words.Trim().Length > 0) open.Clear();
            }

            bool heading = l.Kind == Kind.Heading, list = IsList(l.Kind), task = l.Kind == Kind.Task, quote = l.Kind == Kind.Quote;
            bool inCode = l.InCode || l.Fence;
            int level = heading ? l.Lead.TrimEnd().Length : 0;
            double size = level switch { 1 => 17, 2 => 16, >= 3 => 15, _ => 16 };
            double height = inCode ? 22 : heading ? (level >= 3 ? 22 : 24) : 26.4;
            l.Box.FontSize = l.Marker.FontSize = size;
            l.Box.FontWeight = heading ? FontWeight.ExtraBold : FontWeight.SemiBold;
            l.Box.LineHeight = l.Marker.LineHeight = height;
            l.Box.Code = inCode;
            bool done = task && l.Lead.Contains("[x]", StringComparison.OrdinalIgnoreCase);
            l.Box.Muted = done || l.Fence;
            l.Box.Struck = done;
            l.Box.Padding = inCode ? new Thickness(10, 0) : default;
            if (inCode) l.Row.Bind(Panel.BackgroundProperty, l.Row.GetResourceObservable("Fill2"));
            else l.Row.Background = null;
            // (A heading under an empty line the student left has its room already.)
            bool spaced = i > 0 && lines[i - 1] is { Kind: Kind.Plain } above && string.IsNullOrEmpty(above.Box.Text);
            l.Row.Margin = new Thickness(0, heading && i > 0 && !spaced ? 18 : list || quote ? 3 : 0, 0, heading ? 4 : list || quote ? 3 : 0);

            // What's before the words: a bullet's dot (deeper ones hollow, then square), a number, a checkbox, a bar.
            double inset = l.Depth * 24;
            l.Marker.IsVisible = l.Kind is Kind.Bullet or Kind.Numbered;
            l.Marker.Text = l.Kind switch
            {
                Kind.Bullet => (l.Depth % 3) switch { 0 => "•", 1 => "◦", _ => "▪" },
                Kind.Numbered => $"{l.Number}{l.Lead.TrimEnd()[^1]}",
                _ => "",
            };
            l.Marker.Margin = new Thickness(inset, 0, l.Marker.Text.Length > 1 ? 6 : 0, 0);
            l.Tick.IsVisible = task;
            l.Tick.Margin = new Thickness(inset + 1, (height - 16) / 2, 9, 0);
            busy = true;
            try
            {
                l.Tick.IsChecked = done;
            }
            finally
            {
                busy = false;
            }
            l.Bar.IsVisible = quote;
            ShowRule(l);
        }
        ShowPlaceholder();
    }

    /// <summary>The number a numbered line's own text gives it ("3. " is 3).</summary>
    static long Start(string lead) => NumberLead().Match(lead) is { Success: true } m && long.TryParse(m.Groups[2].Value, out long n) ? n : 1;

    /// <summary>A line that's only "---" is a rule: a line across the page while the caret's in another line.</summary>
    void ShowRule(Line l)
    {
        bool rule = l.Kind == Kind.Plain && !l.InCode && RuleLine().IsMatch(l.Box.Text ?? "");
        bool drawn = rule && !l.Box.IsFocused;
        l.Rule.IsVisible = drawn;
        l.Box.Opacity = drawn ? 0 : 1;
        if (rule) l.Box.Muted = true;
    }

    void ShowPlaceholder()
    {
        for (int i = 0; i < lines.Count; i++)
            lines[i].Box.PlaceholderText = i == 0 && lines.Count == 1 && lines[0].Kind == Kind.Plain ? Placeholder : null;
    }

    /// <summary>A line made plain text again: its words kept, its heading's or bullet's start gone (or, inside a block
    /// of code, put back among its words, as it was typed).</summary>
    void Plain(Line line, bool keepLead = false)
    {
        int caret = line.Box.CaretIndex;
        if (keepLead)
        {
            busy = true;
            try
            {
                line.Box.Text = line.Lead + line.Box.Text;
            }
            finally
            {
                busy = false;
            }
            if (line.Box.IsFocused) line.Box.CaretIndex = caret + line.Lead.Length;
        }
        line.Kind = Kind.Plain;
        line.Lead = "";
    }

    /// <summary>A plain line that now starts "# ", "- ", "1. ", "[ ] " or "> " becomes a heading, a bullet, a numbered
    /// line, a checkbox or a quote, there and then; a bullet that now starts "[ ] " becomes a checkbox.</summary>
    void Typed(Line line)
    {
        string text = line.Box.Text ?? "";
        bool code = line.InCode || lines.TakeWhile(l => l != line).Count(l => l.Kind == Kind.Plain && FenceLine().IsMatch(l.Box.Text ?? "")) % 2 == 1;
        (Kind Kind, string Lead, string Words)? now = null;
        if (code) { }
        else if (line.Kind == Kind.Plain && Parse(text, typing: true) is { Kind: not Kind.Plain } plainNow) now = plainNow;
        else if (line.Kind == Kind.Bullet && Box().Match(text) is { Success: true } box)
            now = (Kind.Task, line.Lead + (box.Groups[1].Value is "x" or "X" ? "[x] " : "[ ] "), text[box.Length..]);
        if (now is { } becomes)
        {
            int caret = line.Box.CaretIndex, gone = text.Length - becomes.Words.Length;
            busy = true;
            try
            {
                line.Kind = becomes.Kind;
                line.Lead = becomes.Lead;
                line.Box.Text = becomes.Words;
            }
            finally
            {
                busy = false;
            }
            line.Box.CaretIndex = Math.Clamp(caret - gone, 0, becomes.Words.Length);
            line.Typed = text;
            Dispatcher.UIThread.Post(() => line.Typed = null, DispatcherPriority.Input);
        }
        Changed();
    }

    /// <summary>The keystroke that made a line a heading, a bullet or a checkbox put back what the field held before
    /// it was one: its start comes off its words again.</summary>
    void Again(Line line)
    {
        string text = line.Box.Text ?? "";
        int caret = line.Box.CaretIndex;
        string words = text.StartsWith(line.Lead, StringComparison.Ordinal) ? text[line.Lead.Length..]
            : Box().Match(text) is { Success: true } box ? text[box.Length..] : text;
        busy = true;
        try
        {
            line.Box.Text = words;
        }
        finally
        {
            busy = false;
        }
        int at = Math.Clamp(caret - (text.Length - words.Length), 0, words.Length);
        line.Box.CaretIndex = at;
        Dispatcher.UIThread.Post(() =>
        {
            // (The field moves its caret on past what was typed as the keystroke finishes.)
            if (line.Box.CaretIndex > (line.Box.Text ?? "").Length || line.Box.Text != words) return;
            line.Box.SelectionStart = line.Box.SelectionEnd = line.Box.CaretIndex = Math.Min(words.Length, Math.Max(at, line.Box.CaretIndex));
        }, DispatcherPriority.Input);
    }

    void Focus(Line line, int caret)
    {
        void Place()
        {
            line.Box.Focus();
            int at = Math.Clamp(caret, 0, (line.Box.Text ?? "").Length);
            line.Box.SelectionStart = line.Box.SelectionEnd = at;
            line.Box.CaretIndex = at;
        }
        // A line just made isn't on screen until the next layout: its field takes the caret then.
        if (line.Box.IsAttachedToVisualTree() && line.Box.IsMeasureValid) Place();
        else Dispatcher.UIThread.Post(Place, DispatcherPriority.Loaded);
    }

    /// <summary>Which of the field's own lines (as it wraps) the caret is on, and how many there are.</summary>
    static (int Line, int Count) Where(TextBox box)
    {
        if (box.GetVisualDescendants().OfType<TextPresenter>().FirstOrDefault()?.TextLayout is not { } layout) return (0, 1);
        return (layout.GetLineIndexFromCharacterIndex(Math.Clamp(box.CaretIndex, 0, (box.Text ?? "").Length), false), layout.TextLines.Count);
    }

    /// <summary>The start the line after <paramref name="line"/> takes when Return is pressed in it: a list's next item
    /// (the next number, an empty checkbox), a quote's next line; nothing after a heading or a plain line.</summary>
    static (Kind Kind, string Lead) After(Line line) => line.Kind switch
    {
        Kind.Bullet or Kind.Task => (line.Kind, TypedNotes.Bullet(line.Lead) ?? line.Lead),
        Kind.Numbered => (Kind.Numbered, $"{new string(' ', Indent(line.Lead))}{line.Number + 1}{line.Lead.TrimEnd()[^1]} "),
        Kind.Quote => (Kind.Quote, line.Lead),
        _ => (Kind.Plain, ""),
    };

    /// <summary>
    /// Tab: a list's item moves in, under the item above it at its level (it can't go deeper than one under that);
    /// Shift-Tab: out to the level of the item it's under. A numbered line moved in starts its own list at 1.
    /// </summary>
    void Nest(Line line, bool outward)
    {
        int at = lines.IndexOf(line), indent = Indent(line.Lead), to = outward ? 0 : indent;
        Line? sibling = null;
        for (int i = at - 1; i >= 0; i--)
        {
            var above = lines[i];
            if (above.Kind == Kind.Plain && string.IsNullOrWhiteSpace(above.Box.Text)) continue;
            if (!IsList(above.Kind)) break;
            int its = Indent(above.Lead);
            if (outward && its < indent)
            {
                to = its;
                break;
            }
            if (!outward && its <= indent)
            {
                if (its == indent) to = its + ListWidth(above.Lead);
                break;
            }
        }
        if (to == indent) return;
        // The item it now follows at its new level, if any: a number carries on from it.
        for (int i = at - 1; i >= 0 && IsList(lines[i].Kind); i--)
        {
            int its = Indent(lines[i].Lead);
            if (its == to) sibling = lines[i];
            if (its <= to) break;
        }
        string lead = line.Lead.TrimStart(' ');
        if (line.Kind == Kind.Numbered && NumberLead().Match(lead) is { Success: true } m)
            lead = (sibling is { Kind: Kind.Numbered } ? sibling.Number + 1 : outward ? Start(lead) : 1) + lead[m.Groups[2].Length..];
        line.Lead = new string(' ', to) + lead;
        Changed();
    }

    /// <summary>How far an item's words are in from where its marker starts ("- " 2, "1. " 3): where an item under it
    /// has to start, in Markdown, to be under it.</summary>
    static int ListWidth(string lead)
    {
        string bare = lead.TrimStart(' ');
        int box = bare.IndexOf('[');
        return box > 0 ? box : bare.Length;
    }

    void OnKey(Line line, KeyEventArgs e)
    {
        int at = lines.IndexOf(line);
        if (at < 0) return;
        var box = line.Box;
        string text = box.Text ?? "";
        int from = Math.Clamp(Math.Min(box.SelectionStart, box.SelectionEnd), 0, text.Length), to = Math.Clamp(Math.Max(box.SelectionStart, box.SelectionEnd), 0, text.Length);
        bool selecting = from != to;
        int caret = selecting ? from : Math.Clamp(box.CaretIndex, 0, text.Length);
        bool command = e.KeyModifiers.HasFlag(KeyModifiers.Meta) || e.KeyModifiers.HasFlag(KeyModifiers.Control);
        switch (e.Key)
        {
            case Key.Enter when command && line.Kind == Kind.Task:
                // ⌘Return ticks a checkbox (or unticks it).
                e.Handled = true;
                line.Tick.IsChecked = line.Tick.IsChecked != true;
                break;
            case Key.Enter when !command:
                e.Handled = true;
                if (selecting) text = text.Remove(from, to - from);
                if (line.Kind != Kind.Plain && text.Length == 0)
                {
                    // Return on an empty item of a list inside another moves it out a level; on any other empty
                    // bullet, checkbox, quote or heading, it ends it: the line is plain again.
                    if (IsList(line.Kind) && Indent(line.Lead) > 0) Nest(line, outward: true);
                    else
                    {
                        Plain(line);
                        Changed();
                    }
                    return;
                }
                busy = true;
                Line next;
                try
                {
                    box.Text = text[..caret];
                    var (kind, lead) = line.InCode ? (Kind.Plain, "") : After(line);
                    next = Add(at + 1, kind, lead, text[caret..]);
                }
                finally
                {
                    busy = false;
                }
                Changed();
                Focus(next, 0);
                break;
            case Key.Tab when IsList(line.Kind) && e.KeyModifiers is KeyModifiers.None or KeyModifiers.Shift:
                e.Handled = true;
                Nest(line, outward: e.KeyModifiers == KeyModifiers.Shift);
                Focus(line, caret);
                break;
            case Key.Back when caret == 0 && !selecting:
                if (line.Kind != Kind.Plain)
                {
                    // At the start of a heading, a bullet or a checkbox: it's a plain line again, its words kept.
                    e.Handled = true;
                    Plain(line);
                    Changed();
                }
                else if (at > 0)
                {
                    e.Handled = true;
                    var above = lines[at - 1];
                    int join = (above.Box.Text ?? "").Length;
                    busy = true;
                    try
                    {
                        above.Box.Text += text;
                        Remove(line);
                    }
                    finally
                    {
                        busy = false;
                    }
                    Changed();
                    Focus(above, join);
                }
                break;
            case Key.Delete when caret == text.Length && !selecting && at < lines.Count - 1:
                e.Handled = true;
                var below = lines[at + 1];
                busy = true;
                try
                {
                    box.Text = text + (below.Box.Text ?? "");
                    Remove(below);
                }
                finally
                {
                    busy = false;
                }
                Changed();
                Focus(line, caret);
                break;
            case Key.Up when at > 0 && !e.KeyModifiers.HasFlag(KeyModifiers.Shift) && Where(box).Line == 0:
                e.Handled = true;
                Focus(lines[at - 1], Math.Max(0, (lines[at - 1].Box.Text ?? "").Length - Math.Max(0, text.Length - caret)));
                break;
            case Key.Down when at < lines.Count - 1 && !e.KeyModifiers.HasFlag(KeyModifiers.Shift) && Where(box) is var (on, count) && on >= count - 1:
                e.Handled = true;
                Focus(lines[at + 1], caret);
                break;
            case Key.Left when caret == 0 && !selecting && at > 0 && e.KeyModifiers == KeyModifiers.None:
                e.Handled = true;
                Focus(lines[at - 1], int.MaxValue);
                break;
            case Key.Right when caret == text.Length && !selecting && at < lines.Count - 1 && e.KeyModifiers == KeyModifiers.None:
                e.Handled = true;
                Focus(lines[at + 1], 0);
                break;
            case Key.V when command && !e.KeyModifiers.HasFlag(KeyModifiers.Shift):
                // Several lines pasted land as several lines (a field of one line would run them together).
                e.Handled = true;
                _ = PasteAsync(line, from, to);
                break;
        }
    }

    async Task PasteAsync(Line line, int from, int to)
    {
        string? pasted;
        try
        {
            pasted = TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard ? await clipboard.TryGetTextAsync() : null;
        }
        catch (Exception e) when (e is InvalidOperationException or TimeoutException or System.Runtime.InteropServices.COMException)
        {
            return;
        }
        if (!string.IsNullOrEmpty(pasted)) Insert(line, from, to, pasted);
    }

    /// <summary>Puts text in at the caret (over what's selected). Its first line joins the line the caret is in; each
    /// line after is a line of its own, a heading, a bullet, a checkbox or a quote when it's written as one: pasted
    /// Markdown stays Markdown.</summary>
    public void Insert(string text)
    {
        var line = lines.FirstOrDefault(l => l.Box.IsFocused) ?? lines[^1];
        string has = line.Box.Text ?? "";
        int from = Math.Clamp(Math.Min(line.Box.SelectionStart, line.Box.SelectionEnd), 0, has.Length), to = Math.Clamp(Math.Max(line.Box.SelectionStart, line.Box.SelectionEnd), 0, has.Length);
        Insert(line, from, to, text);
    }

    void Insert(Line line, int from, int to, string pasted)
    {
        int at = lines.IndexOf(line);
        if (at < 0) return;
        string text = line.Box.Text ?? "";
        from = Math.Clamp(from, 0, text.Length);
        to = Math.Clamp(to, from, text.Length);
        string[] parts = Normal(pasted).Split('\n');
        string before = text[..from], after = text[to..];
        if (parts.Length == 1)
        {
            busy = true;
            try
            {
                line.Box.Text = before + parts[0] + after;
            }
            finally
            {
                busy = false;
            }
            line.Box.CaretIndex = before.Length + parts[0].Length;
            // (What joined the line may have made it a heading, a bullet or a checkbox.)
            int words = (line.Box.Text ?? "").Length;
            Typed(line);
            Focus(line, (line.Box.Text ?? "").Length - (words - before.Length - parts[0].Length));
            return;
        }
        // Several lines: the notes are laid out again from their text with the pasted lines in it, so each is what
        // it's written as (and a block of code pasted is code).
        var all = lines.Select(l => l.Lead + (l.Box.Text ?? "")).ToList();
        all.RemoveAt(at);
        all.InsertRange(at, [line.Lead + before + parts[0], .. parts[1..^1], parts[^1] + after]);
        Build(string.Join("\n", all));
        var last = lines[at + parts.Length - 1];
        Changed();
        Focus(last, (last.Box.Text ?? "").Length - after.Length);
    }
}
