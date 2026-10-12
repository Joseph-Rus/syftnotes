using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
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
/// Where a student types their own notes while a lecture records. It reads as the notes will: a line that starts
/// "# " is a heading the moment the space is typed, "- " a bullet (Return carries a list on, and on an empty bullet
/// ends it), anything else a plain line. Underneath it's plain text, a line of text per line shown, with the "# "
/// and "- " kept (<see cref="Text"/>): that's what goes with the recording, and what the notes are written round.
/// Each line is a field of its own, so the arrows, Return, Backspace at a line's start and Delete at its end move
/// between them as they would in one piece of text.
/// </summary>
public sealed partial class TypedEditor : UserControl
{
    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<TypedEditor, string>(nameof(Text), "", defaultBindingMode: BindingMode.TwoWay);
    public static readonly StyledProperty<string> PlaceholderProperty = AvaloniaProperty.Register<TypedEditor, string>(nameof(Placeholder), "");

    enum Kind { Plain, Heading, Bullet }

    /// <summary>One line: what it is, how it starts in the text ("# ", "- ", "2. "; nothing for a plain line), the
    /// field its words are typed in, and the dot (or number) drawn before a bullet's.</summary>
    sealed class Line
    {
        public Kind Kind;
        public string Lead = "";
        public required TextBox Box;
        public required Grid Row;
        public required TextBlock Marker;
    }

    readonly StackPanel rows = new();
    readonly List<Line> lines = [];
    /// <summary>The lines are being changed from here (the text is being put together from them, or they're being
    /// made from the text): one doesn't set off the other.</summary>
    bool busy;

    [GeneratedRegex(@"^(#{1,6})\s+(.*)$")]
    private static partial Regex HeadingLine();

    [GeneratedRegex(@"^(\s*(?:[-*+]|\d+[.)]))\s+(.*)$")]
    private static partial Regex BulletLine();

    public TypedEditor()
    {
        ClipToBounds = false;
        Focusable = false;
        Content = rows;
        Build("");
    }

    /// <summary>The notes as text: a line per line, with each heading's "# " and each bullet's "- ".</summary>
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

    static (Kind Kind, string Lead, string Words) Parse(string line)
    {
        if (HeadingLine().Match(line) is { Success: true } h) return (Kind.Heading, h.Groups[1].Value + " ", h.Groups[2].Value);
        if (BulletLine().Match(line) is { Success: true } b) return (Kind.Bullet, b.Groups[1].Value + " ", b.Groups[2].Value);
        return (Kind.Plain, "", line);
    }

    string Compose() => string.Join("\n", lines.Select(l => l.Lead + (l.Box.Text ?? "")));

    /// <summary>The lines, made again from text (it was set from outside: a recording's page opening).</summary>
    void Build(string? text)
    {
        busy = true;
        try
        {
            rows.Children.Clear();
            lines.Clear();
            foreach (string line in Normal(text).Split('\n'))
            {
                var (kind, lead, words) = Parse(line);
                Add(lines.Count, kind, lead, words);
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
        var box = new TextBox { Text = words, AcceptsReturn = false, TextWrapping = TextWrapping.Wrap, VerticalContentAlignment = VerticalAlignment.Top };
        box.Classes.Add("bare");
        Avalonia.Automation.AutomationProperties.SetName(box, "Your notes");
        var marker = new TextBlock { VerticalAlignment = VerticalAlignment.Top, TextAlignment = TextAlignment.Left };
        marker.Bind(TextBlock.ForegroundProperty, marker.GetResourceObservable("InkSubtle"));
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        Grid.SetColumn(box, 1);
        row.Children.Add(marker);
        row.Children.Add(box);
        var line = new Line { Kind = kind, Lead = lead, Box = box, Row = row, Marker = marker };
        box.AddHandler(KeyDownEvent, (_, e) => OnKey(line, e), RoutingStrategies.Tunnel);
        box.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty && !busy) Typed(line);
        };
        lines.Insert(at, line);
        rows.Children.Insert(at, row);
        return line;
    }

    void Remove(Line line)
    {
        lines.Remove(line);
        rows.Children.Remove(line.Row);
    }

    /// <summary>Each line looks like what it is: a heading heavier and set apart, a bullet behind its dot.</summary>
    void Restyle()
    {
        for (int i = 0; i < lines.Count; i++)
        {
            var l = lines[i];
            bool heading = l.Kind == Kind.Heading, bullet = l.Kind == Kind.Bullet;
            l.Box.FontSize = l.Marker.FontSize = heading ? 17 : 16;
            l.Box.FontWeight = heading ? FontWeight.ExtraBold : FontWeight.SemiBold;
            l.Box.LineHeight = l.Marker.LineHeight = heading ? 24 : 26.4;
            // (A heading under an empty line the student left has its room already.)
            bool spaced = i > 0 && lines[i - 1] is { Kind: Kind.Plain } above && string.IsNullOrEmpty(above.Box.Text);
            l.Row.Margin = new Thickness(0, heading && i > 0 && !spaced ? 18 : bullet ? 3 : 0, 0, heading ? 4 : bullet ? 3 : 0);
            // A bullet's dot (a numbered one keeps its number), set in by how far the line was indented.
            string lead = l.Lead.TrimEnd();
            l.Marker.IsVisible = bullet;
            l.Marker.Text = !bullet ? "" : lead.TrimStart() is "-" or "*" or "+" ? "•" : lead.TrimStart();
            l.Marker.MinWidth = 20;
            l.Marker.Margin = new Thickness((lead.Length - lead.TrimStart().Length) * 8, 0, bullet && l.Marker.Text.Length > 1 ? 6 : 0, 0);
        }
        ShowPlaceholder();
    }

    void ShowPlaceholder()
    {
        for (int i = 0; i < lines.Count; i++)
            lines[i].Box.PlaceholderText = i == 0 && lines.Count == 1 && lines[0].Kind == Kind.Plain ? Placeholder : null;
    }

    /// <summary>A plain line that now starts "# " or "- " becomes a heading or a bullet, there and then.</summary>
    void Typed(Line line)
    {
        if (line.Kind == Kind.Plain && Parse(line.Box.Text ?? "") is { Kind: not Kind.Plain } now)
        {
            int caret = line.Box.CaretIndex;
            busy = true;
            try
            {
                line.Kind = now.Kind;
                line.Lead = now.Lead;
                line.Box.Text = now.Words;
            }
            finally
            {
                busy = false;
            }
            line.Box.CaretIndex = Math.Clamp(caret - ((line.Box.Text ?? "").Length < caret ? caret - (line.Box.Text ?? "").Length : 0), 0, (line.Box.Text ?? "").Length);
        }
        Changed();
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
            case Key.Enter when !command:
                e.Handled = true;
                if (selecting) text = text.Remove(from, to - from);
                // Return on an empty bullet (or an empty heading) ends it: the line is plain again.
                if (line.Kind != Kind.Plain && text.Length == 0)
                {
                    line.Kind = Kind.Plain;
                    line.Lead = "";
                    Changed();
                    return;
                }
                busy = true;
                Line next;
                try
                {
                    box.Text = text[..caret];
                    // A list carries on (a numbered one with the next number); after a heading comes a plain line.
                    bool list = line.Kind == Kind.Bullet;
                    next = Add(at + 1, list ? Kind.Bullet : Kind.Plain, list ? TypedNotes.Bullet(line.Lead + "x") ?? line.Lead : "", text[caret..]);
                }
                finally
                {
                    busy = false;
                }
                Changed();
                Focus(next, 0);
                break;
            case Key.Back when caret == 0 && !selecting:
                if (line.Kind != Kind.Plain)
                {
                    // At the start of a heading or a bullet: it's a plain line again, its words kept.
                    e.Handled = true;
                    line.Kind = Kind.Plain;
                    line.Lead = "";
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
    /// line after is a line of its own, a heading or a bullet when it's written as one.</summary>
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
        Line last = line;
        int caret;
        busy = true;
        try
        {
            if (parts.Length == 1)
            {
                line.Box.Text = before + parts[0] + after;
                caret = before.Length + parts[0].Length;
            }
            else
            {
                line.Box.Text = before + parts[0];
                for (int i = 1; i < parts.Length; i++)
                {
                    var (kind, lead, words) = Parse(parts[i]);
                    last = Add(at + i, kind, lead, i == parts.Length - 1 ? words + after : words);
                    if (i == parts.Length - 1) caret = words.Length;
                }
                caret = (last.Box.Text ?? "").Length - after.Length;
            }
        }
        finally
        {
            busy = false;
        }
        // (What joined the first line may have made it a heading or a bullet.)
        Typed(line);
        Focus(last, caret);
    }
}
