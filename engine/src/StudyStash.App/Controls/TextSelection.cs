using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StudyStash.App.Controls.Rich;

namespace StudyStash.App.Controls;

/// <summary>
/// Selecting text with the mouse, as on a web page, wherever the app shows words worth copying: a lecture's notes
/// (<see cref="NoteView"/>), its transcript, a chat's questions and answers, an assignment's or an announcement's text,
/// an error to paste when asking for help. Press and drag from one piece of text into the next; a double click takes a
/// word (and a drag from it whole words), a triple click its whole piece; Shift and a click stretch the selection to
/// there. ⌘C (Ctrl+C on Windows), or Copy in the right-click menu, copies it as plain text: a piece's
/// <see cref="NoteText.CopyLead"/> in front of it (a bullet's "• ", a transcript line's time), a table's cells a tab
/// apart, a formula as its LaTeX in $…$. ⌘A selects all of it. A drag past the top or bottom of the page scrolls it
/// along.
/// <para>
/// Each piece of text is a <see cref="NoteText"/> (or a <see cref="PageText"/>, the same for a page's own words), and
/// the control this selection belongs to does the selecting for all of them inside it, so one drag runs on from one
/// into the next. Mark a part of a page with <c>c:TextSelection.Area="True"</c> to make its pieces one selection; a
/// <see cref="PageText"/> outside any is a selection of its own. Where one such part is inside another (an answer's
/// notes in a chat), the outer one selects for both. One window has one selection: starting another ends it.
/// </para>
/// A press that starts within <see cref="Settle"/> pixels of where it ends is a click, not a drag, so a link in the
/// text still opens; a press on a button, a diagram, a plot or a formula on its own line is still theirs. Touch
/// scrolls, as it did.
/// </summary>
public sealed class TextSelection
{
    /// <summary>Makes a part of a page one selection: a drag in it runs from any of its pieces of text into the next.</summary>
    public static readonly AttachedProperty<bool> AreaProperty = AvaloniaProperty.RegisterAttached<TextSelection, Control, bool>("Area");

    public static bool GetArea(Control control) => control.GetValue(AreaProperty);

    public static void SetArea(Control control, bool value) => control.SetValue(AreaProperty, value);

    /// <summary>The selection a control does (a <see cref="NoteView"/>, an <see cref="AreaProperty"/>, a lone
    /// <see cref="PageText"/>).</summary>
    static readonly AttachedProperty<TextSelection?> OfProperty = AvaloniaProperty.RegisterAttached<TextSelection, Control, TextSelection?>("Of");

    static TextSelection() => AreaProperty.Changed.AddClassHandler<Control>((control, e) =>
    {
        if (e.NewValue is true && control.GetValue(OfProperty) is null) _ = new TextSelection(control);
    });

    /// <summary>The selection <paramref name="control"/> does itself, if it does one.</summary>
    internal static TextSelection? Of(Control control) => control.GetValue(OfProperty);

    /// <summary>Whether something above <paramref name="control"/> does a selection it's part of.</summary>
    internal static bool InOne(Control control)
    {
        for (var v = control.GetVisualParent(); v is not null; v = v.GetVisualParent())
            if (v is Control c && Of(c) is { } s && !s.off()) return true;
        return false;
    }

    /// <summary>The mouse moves this far (across and down together) from where it was pressed before it's a drag.</summary>
    public const double Settle = 4;

    /// <summary>The mouse within this many pixels of the page's top or bottom edge, or past it, scrolls it.</summary>
    const double EdgeZone = 12;

    readonly Control host;
    readonly Func<bool> off;

    /// <summary>A place in the text: before character <see cref="Index"/> of a piece of text, or before (0) or after
    /// (1) a formula on its own line.</summary>
    readonly record struct Spot(Control Piece, int Index);

    /// <summary>What can be selected, in reading order, and where each is in the host, as they were when the press
    /// began (scrolling doesn't move them in here; new text ends the selection).</summary>
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

    /// <summary>The page a drag scrolls: the host's top left in what it scrolls (which scrolling doesn't change), the
    /// mouse in what shows of it, and how far each tick moves it while the mouse is past its top or bottom.</summary>
    ScrollViewer? scroller;
    Point origin, inScroller;
    double edgeStep;
    DispatcherTimer? edge;

    /// <summary>Every selection there is, so starting one ends any other in the same window.</summary>
    static readonly List<WeakReference<TextSelection>> All = [];

    /// <summary>Makes <paramref name="host"/> select the text in it; <paramref name="off"/> says when it doesn't
    /// (notes laid out for paper).</summary>
    internal TextSelection(Control host, Func<bool>? off = null)
    {
        this.host = host;
        this.off = off ?? (() => false);
        host.SetValue(OfProperty, this);
        lock (All)
        {
            All.RemoveAll(w => !w.TryGetTarget(out _));
            All.Add(new WeakReference<TextSelection>(this));
        }
        // The room between pieces starts a selection too (unless the host is drawn on already, or it's given no
        // background, Background="{x:Null}", to leave that room to what's under it: the recorder's, which moves it),
        // and the host takes the focus a click gives (for ⌘C) without being a Tab stop of its own.
        var background = host switch
        {
            Panel => Panel.BackgroundProperty,
            Border => Border.BackgroundProperty,
            TemplatedControl => TemplatedControl.BackgroundProperty,
            TextBlock => TextBlock.BackgroundProperty,
            _ => null,
        };
        if (background is not null && !host.IsSet(background) && host.GetValue(background) is null) host.SetCurrentValue(background, Brushes.Transparent);
        host.Focusable = true;
        host.IsTabStop = false;
        host.AddHandler(InputElement.PointerPressedEvent, OnPressed, RoutingStrategies.Bubble, handledEventsToo: true);
        host.AddHandler(InputElement.PointerMovedEvent, OnMoved, RoutingStrategies.Bubble, handledEventsToo: true);
        host.AddHandler(InputElement.PointerReleasedEvent, OnReleased, RoutingStrategies.Bubble, handledEventsToo: true);
        host.AddHandler(InputElement.PointerCaptureLostEvent, (_, _) =>
        {
            if (dragging) EndDrag(null);
        });
        host.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Bubble);
        // A click into a field (to type the next question) ends it, as a web page's does: the field's own selection
        // is the one now.
        host.AddHandler(InputElement.LostFocusEvent, (_, _) => Dispatcher.UIThread.Post(() =>
        {
            if (anchor is not null && TopLevel.GetTopLevel(host)?.FocusManager?.GetFocusedElement() is TextBox) Unselect();
        }), RoutingStrategies.Bubble);
        host.DetachedFromVisualTree += (_, _) => EndDrag(null);
        if (host.ContextMenu is null && host.ContextFlyout is null) host.ContextMenu = Menu();
    }

    /// <summary>The selection that selects for this one's text: this, or the outermost one it's inside.</summary>
    TextSelection Owner
    {
        get
        {
            var owner = this;
            for (var v = host.GetVisualParent(); v is not null; v = v.GetVisualParent())
                if (v is Control c && Of(c) is { } outer && !outer.off()) owner = outer;
            return owner;
        }
    }

    ContextMenu Menu()
    {
        var keys = Application.Current?.PlatformSettings?.HotkeyConfiguration;
        var copy = new MenuItem { Header = "Copy", InputGesture = keys?.Copy.FirstOrDefault() };
        copy.Click += (_, _) => Owner.Copy();
        var all = new MenuItem { Header = "Select all", InputGesture = keys?.SelectAll.FirstOrDefault() };
        all.Click += (_, _) => Owner.SelectAll();
        var menu = new ContextMenu { WindowManagerAddShadowHint = OperatingSystem.IsMacOS() };
        menu.Items.Add(copy);
        menu.Items.Add(all);
        menu.Opening += (_, _) => copy.IsEnabled = Owner.SelectedText.Length > 0;
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

    internal void Copy()
    {
        if (SelectedText is { Length: > 0 } text && TopLevel.GetTopLevel(host)?.Clipboard is { } clipboard) _ = clipboard.SetTextAsync(text);
    }

    /// <summary>Selects all of the text here (⌘A, Ctrl+A on Windows, or Select all in the right-click menu).</summary>
    internal void SelectAll()
    {
        Collect();
        if (pieces.Count == 0) return;
        EndOthers();
        (anchor, focus) = (new Spot(pieces[0], 0), new Spot(pieces[^1], LengthOf(pieces[^1])));
        Show();
    }

    /// <summary>Draws the selection again (a piece that lost the focus cleared its own).</summary>
    internal void Redraw()
    {
        foreach (var piece in lit)
            if (piece is NoteText t)
            {
                t.SetCurrentValue(SelectableTextBlock.SelectionStartProperty, 0);
                t.SetCurrentValue(SelectableTextBlock.SelectionEndProperty, 0);
            }
        lit = [];
        Show();
    }

    /// <summary>Nothing selected: what new text, or a press on something else here, leaves.</summary>
    internal void Unselect()
    {
        EndDrag(null);
        anchor = null;
        Show();
    }

    /// <summary>The text in <paramref name="part"/> (a <see cref="NoteView"/>) is about to be built again: a selection
    /// that takes any of it ends, as what it took is going.</summary>
    internal static void Rebuilding(Control part)
    {
        var self = Of(part);
        self?.Unselect();
        var owner = self?.Owner;
        if (owner is null || owner == self || owner.anchor is null) return;
        foreach (var piece in owner.lit)
            if (piece.GetVisualAncestors().Contains(part))
            {
                owner.Unselect();
                return;
            }
    }

    /// <summary>Starting a selection here ends any other in the same window.</summary>
    void EndOthers()
    {
        var window = TopLevel.GetTopLevel(host);
        List<TextSelection> others = [];
        lock (All)
            foreach (var w in All)
                if (w.TryGetTarget(out var other) && other != this && other.anchor is not null) others.Add(other);
        foreach (var other in others)
            if (TopLevel.GetTopLevel(other.host) == window) other.Unselect();
    }

    void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled || off() || Owner != this || Application.Current?.PlatformSettings?.HotkeyConfiguration is not { } keys) return;
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

    void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        var press = e.GetCurrentPoint(host);
        if (off() || e.Pointer.Type == PointerType.Touch || !press.Properties.IsLeftButtonPressed || Owner != this) return;
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
        EndOthers();
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
        var now = e.GetCurrentPoint(host);
        if (!now.Properties.IsLeftButtonPressed)
        {
            EndDrag(e.Pointer);
            return;
        }
        if (!dragging)
        {
            if (Math.Abs(now.Position.X - down.X) + Math.Abs(now.Position.Y - down.Y) <= Settle) return;
            dragging = true;
            e.Pointer.Capture(host);
            host.Cursor = new Cursor(StandardCursorType.Ibeam);
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
        host.ClearValue(InputElement.CursorProperty);
        edge?.Stop();
        if (scroller is not null) scroller.ScrollChanged -= OnScrolledWhileDragging;
        scroller = null;
        if (pointer?.Captured == host) pointer.Capture(null);
    }

    /// <summary>Whether a press on <paramref name="source"/> selects: on a piece of text (an inline formula in it too)
    /// or the room around it (between paragraphs or messages, a bullet's dot, a box's margin, a label beside it); not on
    /// a button, a field, a diagram, a plot or a formula on its own line, or anything else that takes a press.</summary>
    bool StartsSelecting(object? source)
    {
        for (var v = source as Visual; v is not null && v != host; v = v.GetVisualParent())
            if (v is NoteText) return true;
        for (var v = source as Visual; v is not null; v = v.GetVisualParent())
        {
            if (v == host) return true;
            if (!Around(v)) return false;
        }
        return false;
    }

    /// <summary>What text is laid out in, which a press passes through to the selection: a panel, a box, a line of
    /// text, a list's own frame, a page that scrolls (not its scroll bar), a view of one look or the other.</summary>
    static bool Around(Visual v) => v is Panel or Border or TextBlock or BySkin or Glass or UserControl or ItemsPresenter or ContentPresenter
        or ScrollViewer or (ItemsControl and not SelectingItemsControl);

    /// <summary>Gathers what can be selected now, in reading order.</summary>
    void Collect()
    {
        pieces = [];
        boxes = [];
        foreach (var piece in host.GetSelfAndVisualDescendants().OfType<Control>())
            if (piece is NoteText or MathDisplay && piece.IsEffectivelyVisible && piece.TranslatePoint(default, host) is { } at)
            {
                pieces.Add(piece);
                boxes.Add(new Rect(at, piece.Bounds.Size));
            }
    }

    static int LengthOf(Control piece) => piece is NoteText t ? t.Length : 1;

    /// <summary>The place in the text nearest <paramref name="p"/>: in the piece of text on its line (the nearest one
    /// across, a table's or a definition's), at the line's end when it's past it; between pieces (a paragraph's gap,
    /// beside a diagram), the start of the next; above them all the first, below them all the end of the last.</summary>
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
                d.Selected = highlighter ??= host.TryFindResource("Hl", host.ActualThemeVariant, out var hl) ? hl as IBrush : null;
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
        scroller = host.FindAncestorOfType<ScrollViewer>();
        if (scroller is null || host.TranslatePoint(default, scroller) is not { } at)
        {
            scroller = null;
            return;
        }
        origin = new Point(at.X + scroller.Offset.X, at.Y + scroller.Offset.Y);
        scroller.ScrollChanged += OnScrolledWhileDragging;
    }

    /// <summary>Where the mouse is in the host now, after any scrolling since it last moved.</summary>
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
