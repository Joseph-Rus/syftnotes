using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StudyStash.App.Controls.Rich;

namespace StudyStash.App.Controls;

/// <summary>
/// Selecting the notes' text with the mouse, as on a web page: press and drag from one paragraph into the next, through
/// bullets, tables and formulas; a double click takes a word (and a drag from it whole words), a triple click its whole
/// paragraph; Shift and a click stretch the selection to there. ⌘C (Ctrl+C on Windows), or Copy in the right-click
/// menu, copies it as plain text: a bullet keeps its "• " (a numbered item its number), a table's cells are a tab apart,
/// a definition's term and meaning a colon, a formula is its LaTeX in $…$ ($$…$$ on a line of its own). A drag past the
/// top or bottom of the page scrolls it along. Each piece of text is a <see cref="NoteText"/>, and this view does the
/// selecting for them all. A click on a link still opens it, and a press on a diagram, a plot or a formula on its own
/// line is still theirs. Touch scrolls, as it did.
/// </summary>
public sealed partial class NoteView
{
    /// <summary>A place in the notes: before character <see cref="Index"/> of a piece of text, or before (0) or after
    /// (1) a formula on its own line.</summary>
    readonly record struct Spot(Control Piece, int Index);

    /// <summary>What can be selected, in reading order, and where each is in this view, as they were when the press
    /// began (scrolling doesn't move them in here; new notes end the selection).</summary>
    List<Control> pieces = [];
    List<Rect> boxes = [];

    /// <summary>Where the selection began and where it ends now (either may come first); no anchor, nothing selected.</summary>
    Spot? anchor;
    Spot focus;

    /// <summary>What a drag selects by: characters, or after a double click whole words, after a triple click whole
    /// pieces; the one the click took runs from <see cref="unitStart"/> to <see cref="unitEnd"/>.</summary>
    enum Unit { Character, Word, Piece }

    Unit unit;
    Spot unitStart, unitEnd;

    /// <summary>Where the left button went down (null while it's up), and whether it's moved far enough since to be a
    /// drag rather than a click: only a drag holds on to the mouse, so a click on a link still reaches the link.</summary>
    Point? pressedAt;
    bool dragging;

    /// <summary>The pieces drawn as selected now.</summary>
    HashSet<Control> lit = [];

    /// <summary>The page a drag scrolls: this view's top left in what it scrolls (which scrolling doesn't change), the
    /// mouse in what shows of it, and how far each tick moves it while the mouse is past its top or bottom.</summary>
    ScrollViewer? scroller;
    Point origin, inScroller;
    double edgeStep;
    DispatcherTimer? edge;

    /// <summary>The mouse within this many pixels of the page's top or bottom edge, or past it, scrolls it.</summary>
    const double EdgeZone = 12;

    void SetUpSelecting()
    {
        // The room between paragraphs starts a selection too, and the notes take the focus a click gives (for ⌘C)
        // without being a Tab stop of their own.
        Background = Brushes.Transparent;
        Focusable = true;
        IsTabStop = false;
        AddHandler(PointerPressedEvent, OnPressed, RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(PointerMovedEvent, OnMoved, RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(PointerReleasedEvent, OnReleased, RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(PointerCaptureLostEvent, (_, _) =>
        {
            if (dragging) EndDrag(null);
        });
        ContextMenu = SelectionMenu();
    }

    ContextMenu SelectionMenu()
    {
        var keys = Application.Current?.PlatformSettings?.HotkeyConfiguration;
        var copy = new MenuItem { Header = "Copy", InputGesture = keys?.Copy.FirstOrDefault() };
        copy.Click += (_, _) => Copy();
        var all = new MenuItem { Header = "Select all", InputGesture = keys?.SelectAll.FirstOrDefault() };
        all.Click += (_, _) => SelectAll();
        var menu = new ContextMenu { WindowManagerAddShadowHint = OperatingSystem.IsMacOS() };
        menu.Items.Add(copy);
        menu.Items.Add(all);
        menu.Opening += (_, _) => copy.IsEnabled = SelectedText.Length > 0;
        return menu;
    }

    /// <summary>The selection as it's copied; empty when nothing is selected.</summary>
    internal string SelectedText
    {
        get
        {
            var text = new StringBuilder();
            bool first = true;
            foreach (var (piece, from, to) in Ranges())
            {
                if (!first) text.Append(piece is NoteText { CopyJoin: { } join } ? join : "\n");
                first = false;
                if (piece is NoteText t)
                {
                    if (from == 0 && t.CopyLead is { } lead) text.Append(lead);
                    text.Append(t.Copied(from, to));
                }
                else if (piece is MathDisplay { Latex: { } latex }) text.Append("$$").Append(latex.Trim()).Append("$$");
            }
            return text.ToString();
        }
    }

    void Copy()
    {
        if (SelectedText is { Length: > 0 } text && TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard) _ = clipboard.SetTextAsync(text);
    }

    /// <summary>Selects all of these notes (⌘A, Ctrl+A on Windows, or Select all in the right-click menu).</summary>
    void SelectAll()
    {
        Collect();
        if (pieces.Count == 0) return;
        (anchor, focus) = (new Spot(pieces[0], 0), new Spot(pieces[^1], LengthOf(pieces[^1])));
        Show();
    }

    /// <summary>Nothing selected: what new notes, or a press on something else in them, leave.</summary>
    void Unselect()
    {
        EndDrag(null);
        anchor = null;
        Show();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || Print || Application.Current?.PlatformSettings?.HotkeyConfiguration is not { } keys) return;
        if (keys.Copy.Any(g => g.Matches(e)) && anchor is not null)
        {
            Copy();
            e.Handled = true;
        }
        else if (keys.SelectAll.Any(g => g.Matches(e)))
        {
            SelectAll();
            e.Handled = true;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        EndDrag(null);
    }

    void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        var press = e.GetCurrentPoint(this);
        if (Print || e.Pointer.Type == PointerType.Touch || !press.Properties.IsLeftButtonPressed) return;
        if (e.Handled || !StartsSelecting(e.Source))
        {
            Unselect();
            return;
        }
        Collect();
        if (SpotAt(press.Position) is not { } spot)
        {
            Unselect();
            return;
        }
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift) && anchor is { } began && pieces.Contains(began.Piece))
        {
            // Shift and a click: the selection stretches from where it began to here.
            unit = Unit.Character;
            (unitStart, unitEnd) = (began, began);
            focus = spot;
        }
        else
        {
            unit = e.ClickCount switch { >= 3 => Unit.Piece, 2 => Unit.Word, _ => Unit.Character };
            (unitStart, unitEnd) = UnitAt(spot);
            (anchor, focus) = (unitStart, unitEnd);
        }
        pressedAt = press.Position;
        dragging = false;
        Show();
        e.Handled = true;
    }

    void OnMoved(object? sender, PointerEventArgs e)
    {
        if (pressedAt is not { } down) return;
        var now = e.GetCurrentPoint(this);
        if (!now.Properties.IsLeftButtonPressed)
        {
            EndDrag(e.Pointer);
            return;
        }
        if (!dragging)
        {
            if (Math.Abs(now.Position.X - down.X) + Math.Abs(now.Position.Y - down.Y) <= 4) return;
            dragging = true;
            e.Pointer.Capture(this);
            Cursor = new Cursor(StandardCursorType.Ibeam);
            FollowScroller();
        }
        if (scroller is not null) inScroller = e.GetPosition(scroller);
        Extend(now.Position);
        ScrollAtEdge();
        e.Handled = true;
    }

    void OnReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (pressedAt is null) return;
        bool dragged = dragging;
        EndDrag(e.Pointer);
        if (dragged) e.Handled = true;
    }

    void EndDrag(IPointer? pointer)
    {
        pressedAt = null;
        if (!dragging) return;
        dragging = false;
        ClearValue(CursorProperty);
        edge?.Stop();
        if (scroller is not null) scroller.ScrollChanged -= OnScrolledWhileDragging;
        scroller = null;
        if (pointer?.Captured == this) pointer.Capture(null);
    }

    /// <summary>Whether a press on <paramref name="source"/> selects: on the notes' text (an inline formula in it too) or
    /// the room around it (between paragraphs, a bullet's dot, a code box's margin); not on a diagram, a plot, a formula
    /// on its own line, or a button.</summary>
    bool StartsSelecting(object? source)
    {
        for (var v = source as Visual; v is not null && v != this; v = v.GetVisualParent())
            if (v is NoteText) return true;
        for (var v = source as Visual; v is not null; v = v.GetVisualParent())
        {
            if (v == this) return true;
            if (v is not (Panel or Border or TextBlock)) return false;
        }
        return false;
    }

    /// <summary>Gathers what can be selected now, in reading order.</summary>
    void Collect()
    {
        pieces = [];
        boxes = [];
        foreach (var piece in this.GetVisualDescendants().OfType<Control>())
            if (piece is NoteText or MathDisplay && piece.IsEffectivelyVisible && piece.TranslatePoint(default, this) is { } at)
            {
                pieces.Add(piece);
                boxes.Add(new Rect(at, piece.Bounds.Size));
            }
    }

    static int LengthOf(Control piece) => piece is NoteText t ? t.Length : 1;

    /// <summary>The place in the notes nearest <paramref name="p"/>: in the piece of text on its line (the nearest one
    /// across, a table's or a definition's), at the line's end when it's past it; between pieces (a paragraph's gap, beside
    /// a diagram), the start of the next; above them all the first, below them all the end of the last.</summary>
    Spot? SpotAt(Point p)
    {
        if (pieces.Count == 0) return null;
        int near = -1;
        double nearBy = double.MaxValue;
        for (int i = 0; i < pieces.Count; i++)
        {
            var box = boxes[i];
            if (p.Y < box.Top || p.Y >= box.Bottom) continue;
            double by = p.X < box.Left ? box.Left - p.X : p.X > box.Right ? p.X - box.Right : 0;
            if (by < nearBy) (near, nearBy) = (i, by);
        }
        if (near >= 0)
            return pieces[near] is NoteText text
                ? new Spot(text, IndexAt(text, p - boxes[near].Position))
                : new Spot(pieces[near], p.Y < boxes[near].Center.Y ? 0 : 1);
        for (int i = 0; i < pieces.Count; i++)
            if (boxes[i].Top > p.Y) return new Spot(pieces[i], 0);
        return new Spot(pieces[^1], LengthOf(pieces[^1]));
    }

    static int IndexAt(NoteText t, Vector within)
    {
        var hit = t.TextLayout.HitTestPoint(new Point(within.X - t.Padding.Left, within.Y - t.Padding.Top));
        return Math.Clamp(hit.TextPosition, 0, t.Length);
    }

    /// <summary>The word, or the piece, a double or triple click at <paramref name="spot"/> takes (just the spot itself
    /// for a single click).</summary>
    (Spot Start, Spot End) UnitAt(Spot spot) => unit switch
    {
        Unit.Word when spot.Piece is NoteText t && WordAround(t.Inlines is { Count: > 0 } inlines ? inlines.Text ?? "" : t.Text ?? "", spot.Index) is var (start, end)
            => (spot with { Index = start }, spot with { Index = end }),
        Unit.Word or Unit.Piece => (spot with { Index = 0 }, spot with { Index = LengthOf(spot.Piece) }),
        _ => (spot, spot),
    };

    /// <summary>The word around character <paramref name="at"/>, an apostrophe inside it included ("don't"); or just
    /// the character there when it's a space or a mark.</summary>
    internal static (int Start, int End) WordAround(string text, int at)
    {
        if (text.Length == 0) return (0, 0);
        bool Letter(int i) => i >= 0 && i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '_');
        bool InWord(int i) => Letter(i) || (i >= 0 && i < text.Length && text[i] is ('\'' or '’') && Letter(i - 1) && Letter(i + 1));
        int c = Math.Clamp(at, 0, text.Length - 1);
        // A click on a word's last letter's right half hits just past it.
        if (!InWord(c) && InWord(c - 1)) c--;
        if (!InWord(c)) return (c, c + 1);
        int s = c, e = c + 1;
        while (InWord(s - 1)) s--;
        while (InWord(e)) e++;
        return (s, e);
    }

    /// <summary>The selection runs from where the press began to <paramref name="p"/>, by what it selects by.</summary>
    void Extend(Point p)
    {
        if (SpotAt(p) is not { } spot) return;
        var (start, end) = UnitAt(spot);
        (anchor, focus) = Compare(start, unitStart) < 0 ? (unitEnd, start) : (unitStart, end);
        Show();
    }

    int Compare(Spot a, Spot b)
    {
        int pa = pieces.IndexOf(a.Piece), pb = pieces.IndexOf(b.Piece);
        return pa != pb ? pa.CompareTo(pb) : a.Index.CompareTo(b.Index);
    }

    /// <summary>Each piece the selection takes some of, and the part of it, in reading order.</summary>
    IEnumerable<(Control Piece, int From, int To)> Ranges()
    {
        if (anchor is not { } a) yield break;
        var (start, end) = Compare(a, focus) <= 0 ? (a, focus) : (focus, a);
        int first = pieces.IndexOf(start.Piece), last = pieces.IndexOf(end.Piece);
        if (first < 0 || last < 0) yield break;
        for (int i = first; i <= last; i++)
        {
            int from = i == first ? start.Index : 0, to = i == last ? end.Index : LengthOf(pieces[i]);
            if (from < to) yield return (pieces[i], from, to);
        }
    }

    /// <summary>Draws the selection: each piece's part of it on the highlighter, and nothing on what's no longer in it.</summary>
    void Show()
    {
        var now = new HashSet<Control>();
        IBrush? highlighter = null;
        foreach (var (piece, from, to) in Ranges())
        {
            now.Add(piece);
            if (piece is NoteText t)
            {
                t.SetCurrentValue(SelectableTextBlock.SelectionStartProperty, from);
                t.SetCurrentValue(SelectableTextBlock.SelectionEndProperty, to);
            }
            else if (piece is MathDisplay d)
                d.Selected = highlighter ??= this.TryFindResource("Hl", ActualThemeVariant, out var hl) ? hl as IBrush : null;
        }
        foreach (var piece in lit)
            if (!now.Contains(piece))
            {
                if (piece is NoteText t)
                {
                    t.SetCurrentValue(SelectableTextBlock.SelectionStartProperty, 0);
                    t.SetCurrentValue(SelectableTextBlock.SelectionEndProperty, 0);
                }
                else if (piece is MathDisplay d) d.Selected = null;
            }
        lit = now;
    }

    /// <summary>A drag began: the page it scrolls in, so the selection follows when it scrolls (a drag past its edge,
    /// the wheel meanwhile).</summary>
    void FollowScroller()
    {
        scroller = this.FindAncestorOfType<ScrollViewer>();
        if (scroller is null || this.TranslatePoint(default, scroller) is not { } at)
        {
            scroller = null;
            return;
        }
        origin = new Point(at.X + scroller.Offset.X, at.Y + scroller.Offset.Y);
        scroller.ScrollChanged += OnScrolledWhileDragging;
    }

    /// <summary>Where the mouse is in this view now, after any scrolling since it last moved.</summary>
    Point MouseHere() => scroller is null ? default : new Point(inScroller.X + scroller.Offset.X - origin.X, inScroller.Y + scroller.Offset.Y - origin.Y);

    void OnScrolledWhileDragging(object? sender, ScrollChangedEventArgs e)
    {
        if (dragging && scroller is not null) Extend(MouseHere());
    }

    /// <summary>With the mouse at or past the page's top or bottom, the page scrolls that way, faster the further past.</summary>
    void ScrollAtEdge()
    {
        if (scroller is null) return;
        double height = scroller.Bounds.Height;
        double past = inScroller.Y < EdgeZone ? inScroller.Y - EdgeZone : inScroller.Y > height - EdgeZone ? inScroller.Y - (height - EdgeZone) : 0;
        if (past == 0)
        {
            edge?.Stop();
            return;
        }
        edgeStep = Math.Sign(past) * Math.Clamp(Math.Abs(past) / 3, 2, 40);
        if (edge is null)
        {
            edge = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            edge.Tick += (_, _) =>
            {
                if (!dragging || scroller is null)
                {
                    edge.Stop();
                    return;
                }
                double y = Math.Clamp(scroller.Offset.Y + edgeStep, 0, Math.Max(0, scroller.Extent.Height - scroller.Viewport.Height));
                if (Math.Abs(y - scroller.Offset.Y) > 0.01) scroller.Offset = scroller.Offset.WithY(y);
            };
        }
        if (!edge.IsEnabled) edge.Start();
    }
}
