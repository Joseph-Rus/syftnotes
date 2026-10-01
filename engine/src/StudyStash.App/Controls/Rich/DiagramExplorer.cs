using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StudyStash.App.Platform;
using StudyStash.Core;
using StudyStash.Core.Rich;

namespace StudyStash.App.Controls.Rich;

/// <summary>What a diagram is doing: being explored (the pointer lights a box and its arrows), stepped through, or
/// hiding its words for the student to recall.</summary>
enum DiagramMode { Explore, Steps, Recall }

/// <summary>
/// A flowchart a student can explore, on screen only (paper always gets the still picture): the pointer over a box
/// lights it, its arrows and the boxes they join while the rest dims, and a click pins that; arrow keys walk from box to
/// box along the arrows; its groups fold into an overview and open one at a time; Steps walks it in reading order a box
/// at a time; Recall hides its words to be remembered and checked, keeping a small score; and a pinned box can be
/// asked about, or found in the lecture. The chrome round it (<see cref="DiagramChrome"/>) shows and drives this.
/// </summary>
sealed class DiagramExplorer
{
    readonly DiagramView view;
    readonly DiagramCanvas canvas;
    readonly Control room;

    public DiagramExplorer(DiagramView view, DiagramCanvas canvas, Control room, bool windowed)
    {
        this.view = view;
        this.canvas = canvas;
        this.room = room;
        Windowed = windowed;
        Zoomer = new Zoomer(canvas, room) { Max = windowed ? 6 : 4 };
        Zoomer.Changed += Changed0;
        Zoomer.Rested += () => canvas.Zoom = Zoomer.Zoom;
        canvas.SceneChanged += OnScene;
        Wire();
    }

    /// <summary>In a window of its own (wheel pans, the whole window to zoom in), not a note.</summary>
    public bool Windowed { get; }

    public Zoomer Zoomer { get; }

    /// <summary>Whenever anything the chrome shows changes.</summary>
    public event Action? Changed;

    void Changed0() => Changed?.Invoke();

    // --- the chart, and its groups folded or open -------------------------------------------------------------------

    Flowchart? written;
    FoldedChart? shown;
    readonly HashSet<string> folded = [];

    public Flowchart? Written => written;

    /// <summary>The chart as drawn now: some groups folded, perhaps.</summary>
    public FoldedChart? Shown => shown;

    public DiagramScene? Scene => canvas.Scene;

    /// <summary>The chart to explore (the one the note wrote), its groups folded as <paramref name="fold"/> says, or
    /// (for a big chart made of groups) all folded into its overview.</summary>
    public void Load(Flowchart? chart, IEnumerable<string>? fold = null)
    {
        written = chart;
        folded.Clear();
        if (chart is not null) folded.UnionWith(fold ?? (DiagramFold.StartsFolded(chart) ? DiagramFold.All(chart) : []));
        Reset();
        Refold(morph: false);
    }

    public IReadOnlySet<string> Folded => folded;

    public bool HasGroups => written?.Groups.Count > 0;

    /// <summary>Some group shows folded.</summary>
    public bool AnyFolded => shown?.Inside.Count > 0;

    /// <summary>Every group shows open.</summary>
    public bool AllOpen => !AnyFolded;

    /// <summary>Folds an open group, or opens a folded one (and the groups inside it stay as they were).</summary>
    public void ToggleGroup(string id)
    {
        if (written is null) return;
        if (shown?.IsFolded(id) == true || folded.Contains(id)) folded.Remove(id);
        else folded.Add(id);
        Refold(morph: true);
    }

    /// <summary>The overview: every group folded.</summary>
    public void FoldAll()
    {
        if (written is null) return;
        folded.UnionWith(DiagramFold.All(written));
        Refold(morph: true);
    }

    /// <summary>Every group open: the whole chart as written.</summary>
    public void OpenAll()
    {
        folded.Clear();
        Refold(morph: true);
    }

    void Refold(bool morph)
    {
        var before = shown;
        shown = written is null ? null : DiagramFold.Fold(written, folded);
        plan = null;
        // A box folded away takes the keyboard's ring to the group that now holds it; nothing stays pinned across a fold.
        pinned = null;
        pinnedEdge = -1;
        ActionsOpen = false;
        if (ring is not null && shown?.Chart.Node(ring) is null) ring = shown?.Shown.GetValueOrDefault(ring);
        hover = DiagramTarget.Nothing;
        canvas.Morph(morph && before is not null && !Motion.Reduced ? before : null, shown);
        canvas.Chart = shown?.Chart;
        if (mode == DiagramMode.Steps) step = Math.Min(step, Steps.Count - 1);
        Refresh();
    }

    void OnScene()
    {
        // A new layout (the chart folded or opened, turned to fit, or first laid out): what's lit is found again on it.
        if (pinned is not null && Scene?.Nodes.Any(n => n.Id == pinned) != true) pinned = null;
        Refresh();
    }

    // --- what's lit ---------------------------------------------------------------------------------------------------

    DiagramTarget hover = DiagramTarget.Nothing;
    string? pinned;
    int pinnedEdge = -1;
    string? ring;
    DiagramFocus lit = DiagramFocus.None;
    double dim;
    IDisposable? dimming;

    /// <summary>The box clicked (its arrows lit until another click), if any.</summary>
    public string? Pinned => pinned;

    /// <summary>The box the keyboard is on, if any.</summary>
    public string? Ring => ring;

    public DiagramTarget Hover => hover;

    /// <summary>A box's words on one line (a folded group's title).</summary>
    public string Label(string id)
    {
        if (shown?.Chart.Node(id) is not { } n) return id;
        return shown.IsFolded(id) ? written?.Groups.FirstOrDefault(g => g.Id == id)?.Title ?? n.Label : n.Label;
    }

    /// <summary>What a box joins: the boxes with arrows to it (and their words) and those it points to.</summary>
    public (IReadOnlyList<(string Label, string? Via)> From, IReadOnlyList<(string Label, string? Via)> To) Joins(string id)
    {
        var from = new List<(string, string?)>();
        var to = new List<(string, string?)>();
        if (shown is null) return (from, to);
        foreach (var e in shown.Chart.Edges)
        {
            if (e.From == e.To) continue;
            if (e.To == id && from.All(f => f.Item1 != Label(e.From))) from.Add((Label(e.From), e.Label?.Replace('\n', ' ')));
            if (e.From == id && to.All(f => f.Item1 != Label(e.To))) to.Add((Label(e.To), e.Label?.Replace('\n', ' ')));
        }
        return (from, to);
    }

    /// <summary>One line about a box, for the caption and screen readers: its words, where its arrows come from and go.</summary>
    public string About(string id)
    {
        var (from, to) = Joins(id);
        static string Said((string Label, string? Via) j) => j.Via is { Length: > 0 } v ? $"{j.Label} ({v})" : j.Label;
        var parts = new List<string> { Label(id) };
        if (shown?.IsFolded(id) == true) parts.Add(DiagramFold.Count(shown.Inside[id]));
        if (from.Count > 0) parts.Add("from " + string.Join(", ", from.Select(Said)));
        if (to.Count > 0) parts.Add("to " + string.Join(", ", to.Select(Said)));
        return string.Join(" · ", parts);
    }

    DiagramFocus FocusNow()
    {
        if (Scene is not { } scene) return DiagramFocus.None;
        if (mode == DiagramMode.Steps) return StepFocus(scene);
        if (mode == DiagramMode.Recall) return DiagramFocus.None;
        if (pinned is not null) return DiagramHit.Around(scene, DiagramTarget.Node(pinned));
        if (pinnedEdge >= 0) return DiagramHit.Around(scene, new DiagramTarget(DiagramPart.Edge, null, pinnedEdge));
        if (hover.IsNode || hover.IsEdge) return DiagramHit.Around(scene, hover);
        if (hover.Part == DiagramPart.GroupTitle && hover.Id is { } g && written is not null)
            return DiagramHit.Around(scene, hover, id => DiagramFold.Members(written, id).Select(m => shown?.Shown.GetValueOrDefault(m, m) ?? m).Distinct());
        if (ring is not null) return DiagramHit.Around(scene, DiagramTarget.Node(ring));
        return DiagramFocus.None;
    }

    /// <summary>Works out what's lit now and draws it, easing the dimming in (or out, the last lit boxes staying clear
    /// as the rest comes back).</summary>
    void Refresh()
    {
        var focus = FocusNow();
        bool want = !focus.IsEmpty;
        if (want) lit = focus;
        double to = want ? 1 : 0;
        if (Math.Abs(dimTo - to) > 1e-3)
        {
            dimTo = to;
            dimming?.Dispose();
            double from = dim;
            dimming = Motion.Animate(TimeSpan.FromMilliseconds(want ? 140 : 200), t =>
            {
                dim = from + (to - from) * t;
                Paint();
            }, () =>
            {
                dimming = null;
                if (dimTo < 0.5) lit = DiagramFocus.None;
                Paint();
            });
        }
        else Paint();
        Changed?.Invoke();
    }

    double dimTo;

    void Paint()
    {
        HashSet<int>? accented = null;
        string? picked = null;
        if (mode == DiagramMode.Steps && Scene is not null && CurrentStep is { } s)
        {
            accented = [.. s.Arrives];
            if (step == Steps.Count - 1) accented.UnionWith(s.Returns);
            picked = s.Node;
        }
        else if (mode == DiagramMode.Explore && pinned is not null)
        {
            accented = [.. lit.Edges];
            picked = pinned;
        }
        else if (mode == DiagramMode.Explore && pinnedEdge >= 0) accented = [pinnedEdge];
        else if (mode == DiagramMode.Recall) picked = asking;
        var look = new DiagramLook
        {
            Lit = lit.IsEmpty ? null : lit.Nodes,
            LitEdges = lit.IsEmpty ? null : lit.Edges,
            Dim = dim,
            Accented = accented,
            Picked = picked,
            Ring = view.KeyboardFocused && mode != DiagramMode.Steps ? ring : null,
            Hidden = mode == DiagramMode.Recall ? hidden.Where(h => !revealed.Contains(h)).ToHashSet() : null,
            Marks = mode == DiagramMode.Recall ? marks : null,
            Folded = shown?.Inside.Keys.ToHashSet(),
            GroupHover = hover.Part == DiagramPart.GroupTitle ? hover.Id : null,
        };
        // Nothing over it: the still picture, exactly as a note that's never been touched draws it.
        canvas.Look = look.IsEmpty ? null : look;
    }

    // --- the pointer --------------------------------------------------------------------------------------------------

    Point? pressedAt;
    DiagramTarget pressedOn = DiagramTarget.Nothing;
    int presses;
    Point lastDrag;
    bool dragging;
    double pinchScale = 1;

    /// <summary>A point of the canvas (as the pointer gives it) in the scene's own units.</summary>
    Pt ToScene(Point p) => new(p.X / Math.Max(1e-6, canvas.Scale), p.Y / Math.Max(1e-6, canvas.Scale));

    /// <summary>What's under the pointer, in the scene.</summary>
    public DiagramTarget TargetAt(Point onCanvas) => Scene is { } scene
        ? DiagramHit.At(scene, ToScene(onCanvas), DiagramHit.LineSlop / Math.Max(0.5, Zoomer.Zoom))
        : DiagramTarget.Nothing;

    void Wire()
    {
        room.PointerMoved += (_, e) =>
        {
            var p = e.GetPosition(canvas);
            if (pressedAt is { } start && e.GetCurrentPoint(view).Properties.IsLeftButtonPressed)
            {
                // Measured against the view, which doesn't move: the picture itself moves under the pointer as it pans.
                var r = e.GetPosition(view);
                if (!dragging && CanPan && Distance(r, start) > 4) dragging = true;
                if (dragging)
                {
                    room.Cursor = new Cursor(StandardCursorType.SizeAll);
                    Zoomer.PanBy(r - lastDrag);
                    lastDrag = r;
                    e.Handled = true;
                    return;
                }
            }
            HoverAt(TargetAt(p));
        };
        room.PointerExited += (_, _) =>
        {
            if (dragging) return;
            HoverAt(DiagramTarget.Nothing);
        };
        room.PointerPressed += (_, e) =>
        {
            var point = e.GetCurrentPoint(view);
            if (point.Properties.IsRightButtonPressed)
            {
                if (TargetAt(e.GetPosition(canvas)) is { IsNode: true, Id: { } id } && mode == DiagramMode.Explore)
                {
                    Pin(id, actions: true);
                    e.Handled = true;
                }
                return;
            }
            if (!point.Properties.IsLeftButtonPressed) return;
            // The scroll bar of a wide diagram scrolls it; it isn't a click on the picture.
            if (e.Source is Visual v && v.FindAncestorOfType<Avalonia.Controls.Primitives.ScrollBar>(includeSelf: true) is not null) return;
            pressedAt = lastDrag = point.Position;
            presses = e.ClickCount;
            pressedOn = TargetAt(e.GetPosition(canvas));
            dragging = false;
            e.Pointer.Capture(room);
            view.Focus(NavigationMethod.Pointer);
        };
        room.PointerReleased += (_, e) =>
        {
            if (pressedAt is null) return;
            bool click = !dragging;
            pressedAt = null;
            dragging = false;
            e.Pointer.Capture(null);
            room.Cursor = view.Cursor;
            if (!click || e.InitialPressMouseButton != MouseButton.Left) return;
            e.Handled = true;
            // What was pressed is what's clicked (the page may have moved under the pointer since).
            if (presses >= 2 && pressedOn is { IsNode: true, Id: { } twice })
            {
                ZoomTo(twice);
                return;
            }
            Click(pressedOn);
        };
        room.PointerCaptureLost += (_, _) =>
        {
            pressedAt = null;
            dragging = false;
        };
        room.PointerWheelChanged += (_, e) =>
        {
            bool zoomKey = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
            if (zoomKey)
            {
                Zoomer.ZoomBy(Math.Pow(1.18, e.Delta.Y + e.Delta.X), RoomPoint(e), glide: false);
                // Its keys (0 to fit again) work on it from here on.
                if (!view.IsKeyboardFocusWithin) view.Focus(NavigationMethod.Pointer);
                e.Handled = true;
            }
            else if (Windowed || (CanPan && e.Delta.X != 0 && Math.Abs(e.Delta.X) > Math.Abs(e.Delta.Y)))
            {
                // A window of its own pans with the wheel (or two fingers); a note scrolls on, unless the student is
                // swiping sideways across a diagram they've zoomed into.
                Zoomer.PanBy(new Vector(e.Delta.X, e.Delta.Y) * 40);
                e.Handled = true;
            }
        };
        room.PointerTouchPadGestureMagnify += (_, e) =>
        {
            Zoomer.ZoomBy(1 + e.Delta.X, RoomPoint(e), glide: false);
            e.Handled = true;
        };
        room.GestureRecognizers.Add(new PinchGestureRecognizer());
        room.Pinch += (_, e) =>
        {
            Zoomer.ZoomBy(e.Scale / pinchScale, Windowed ? e.ScaleOrigin : Zoomer.ToRoom(e.ScaleOrigin), glide: false);
            pinchScale = e.Scale;
            e.Handled = true;
        };
        room.PinchEnded += (_, _) => pinchScale = 1;
    }

    static double Distance(Point a, Point b) => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);

    /// <summary>Where the pointer is in the picture's room: in a note the room is the picture's own place on the page,
    /// so it's the pointer on the picture, moved and scaled as the picture is drawn now.</summary>
    Point RoomPoint(PointerEventArgs e) => Windowed ? e.GetPosition(room) : Zoomer.ToRoom(e.GetPosition(canvas));

    /// <summary>The picture is bigger than its room (zoomed in, or too wide), so a drag moves it.</summary>
    public bool CanPan => canvas.Bounds.Width * Zoomer.Zoom > (Windowed ? room.Bounds.Width : canvas.Bounds.Width) + 1
        || canvas.Bounds.Height * Zoomer.Zoom > (Windowed ? room.Bounds.Height : canvas.Bounds.Height) + 1;

    void HoverAt(DiagramTarget target)
    {
        if (target == hover) return;
        hover = target;
        room.Cursor = target.IsNothing || target.Part == DiagramPart.Group
            ? (Windowed ? null : view.Cursor)
            : new Cursor(StandardCursorType.Hand);
        if (mode == DiagramMode.Explore && pinned is null && pinnedEdge < 0) Refresh();
        else Paint();
    }

    /// <summary>A click on the picture.</summary>
    public void Click(DiagramTarget target)
    {
        HintSeen();
        switch (mode)
        {
            case DiagramMode.Steps:
                if (target is { IsNode: true, Id: { } id } && Steps.ToList().FindIndex(s => s.Node == id) is int k and >= 0) GoTo(k);
                return;
            case DiagramMode.Recall:
                if (target is { IsNode: true, Id: { } hiddenId } && hidden.Contains(hiddenId) && !revealed.Contains(hiddenId)) Reveal(hiddenId);
                else if (target is { Part: DiagramPart.GroupTitle, Id: { } g }) ToggleGroup(g);
                return;
        }
        switch (target.Part)
        {
            case DiagramPart.Node when target.Id is { } node:
                if (shown?.IsFolded(node) == true) ToggleGroup(node);
                else if (pinned == node) Unpin();
                else Pin(node, actions: true);
                break;
            case DiagramPart.Edge or DiagramPart.EdgeLabel:
                pinned = null;
                pinnedEdge = pinnedEdge == target.Edge ? -1 : target.Edge;
                Refresh();
                break;
            case DiagramPart.GroupTitle when target.Id is { } group:
                ToggleGroup(group);
                break;
            default:
                if (pinned is not null || pinnedEdge >= 0) Unpin();
                else if (!Windowed) view.OpenLarger();
                break;
        }
    }

    /// <summary>Pins a box: it and its arrows stay lit, and its actions show beside it.</summary>
    public void Pin(string id, bool actions)
    {
        pinned = id;
        pinnedEdge = -1;
        ring = id;
        ActionsOpen = actions;
        Say(About(id));
        Refresh();
    }

    public void Unpin()
    {
        pinned = null;
        pinnedEdge = -1;
        ActionsOpen = false;
        Refresh();
    }

    /// <summary>The pinned box's actions are showing.</summary>
    public bool ActionsOpen { get; private set; }

    /// <summary>Zooms in on a box (and the boxes round it).</summary>
    public void ZoomTo(string id)
    {
        if (Scene?.Nodes.FirstOrDefault(n => n.Id == id) is not { } n) return;
        double s = canvas.Scale;
        var b = n.Box.Inflate(Windowed ? 60 : 30);
        Zoomer.Show(new Rect(b.X * s, b.Y * s, b.W * s, b.H * s), most: Math.Max(1.6, 1.6 / Math.Max(0.1, s)));
    }

    // --- the keyboard -------------------------------------------------------------------------------------------------

    /// <summary>A key pressed while the diagram has the keyboard; true when it was the diagram's.</summary>
    public bool Key(Key key, KeyModifiers modifiers)
    {
        bool plain = modifiers == KeyModifiers.None || modifiers == KeyModifiers.Shift;
        bool command = modifiers.HasFlag(KeyModifiers.Control) || modifiers.HasFlag(KeyModifiers.Meta);
        switch (key)
        {
            case Avalonia.Input.Key.OemPlus or Avalonia.Input.Key.Add when plain || command:
                Zoomer.ZoomBy(1.25);
                return true;
            case Avalonia.Input.Key.OemMinus or Avalonia.Input.Key.Subtract when plain || command:
                Zoomer.ZoomBy(1 / 1.25);
                return true;
            case Avalonia.Input.Key.D0 or Avalonia.Input.Key.NumPad0 when plain || command:
                Zoomer.Fit();
                return true;
        }
        if (!plain) return false;
        if (mode == DiagramMode.Steps)
            switch (key)
            {
                case Avalonia.Input.Key.Right or Avalonia.Input.Key.Down or Avalonia.Input.Key.Space or Avalonia.Input.Key.Enter:
                    Next();
                    return true;
                case Avalonia.Input.Key.Left or Avalonia.Input.Key.Up:
                    Previous();
                    return true;
                case Avalonia.Input.Key.P:
                    TogglePlay();
                    return true;
                case Avalonia.Input.Key.Home:
                    GoTo(0);
                    return true;
                case Avalonia.Input.Key.End:
                    GoTo(Steps.Count - 1);
                    return true;
                case Avalonia.Input.Key.Escape or Avalonia.Input.Key.S:
                    Explore();
                    return true;
            }
        if (mode == DiagramMode.Recall)
            switch (key)
            {
                case Avalonia.Input.Key.Y when asking is not null:
                    Grade(true);
                    return true;
                case Avalonia.Input.Key.N when asking is not null:
                    Grade(false);
                    return true;
                case Avalonia.Input.Key.Enter or Avalonia.Input.Key.Space when ring is not null && hidden.Contains(ring) && !revealed.Contains(ring):
                    Reveal(ring);
                    return true;
                case Avalonia.Input.Key.Escape or Avalonia.Input.Key.H:
                    Explore();
                    return true;
            }
        switch (key)
        {
            case Avalonia.Input.Key.Left: return Walk(-1, 0);
            case Avalonia.Input.Key.Right: return Walk(1, 0);
            case Avalonia.Input.Key.Up: return Walk(0, -1);
            case Avalonia.Input.Key.Down: return Walk(0, 1);
            case Avalonia.Input.Key.Enter or Avalonia.Input.Key.Space when mode == DiagramMode.Explore:
                if (ring is { } r)
                {
                    if (shown?.IsFolded(r) == true) ToggleGroup(r);
                    else Pin(r, actions: true);
                    return true;
                }
                if (!Windowed)
                {
                    view.OpenLarger();
                    return true;
                }
                return Walk(0, 0);
            case Avalonia.Input.Key.Escape:
                if (ActionsOpen)
                {
                    ActionsOpen = false;
                    Changed?.Invoke();
                    return true;
                }
                if (pinned is not null || pinnedEdge >= 0)
                {
                    Unpin();
                    return true;
                }
                if (ring is not null)
                {
                    ring = null;
                    Refresh();
                    return true;
                }
                if (Zoomer.Zoom > 1.001)
                {
                    Zoomer.Fit();
                    return true;
                }
                return false;
            case Avalonia.Input.Key.S:
                StartSteps();
                return true;
            case Avalonia.Input.Key.H:
                StartRecall();
                return true;
            case Avalonia.Input.Key.O when HasGroups:
                if (AnyFolded) OpenAll();
                else FoldAll();
                return true;
            case Avalonia.Input.Key.Z when ring is { } z:
                ZoomTo(z);
                return true;
        }
        return false;
    }

    /// <summary>Moves the keyboard's ring along the arrows, the way the key points (to where the walk starts, if it's
    /// on no box yet).</summary>
    bool Walk(double dx, double dy)
    {
        if (Scene is not { } scene || scene.Nodes.Count == 0) return false;
        string? to = ring is null || scene.Nodes.All(n => n.Id != ring)
            ? Steps.FirstOrDefault()?.Node ?? scene.Nodes[0].Id
            : dx == 0 && dy == 0 ? ring : DiagramHit.Toward(scene, ring, dx, dy);
        if (to is null) return true;
        ring = to;
        // The keyboard leads now: what it's on lights up, not what the pointer was last over.
        hover = DiagramTarget.Nothing;
        if (pinned is not null && pinned != to)
        {
            pinned = null;
            ActionsOpen = false;
        }
        Say(About(to));
        Refresh();
        return true;
    }

    /// <summary>The keyboard came to the diagram or left it: its ring shows only while it's there.</summary>
    public void FocusChanged() => Paint();

    // --- stepping through ---------------------------------------------------------------------------------------------

    DiagramMode mode;
    IReadOnlyList<DiagramStep>? plan;
    int step;
    IDisposable? playing;

    public DiagramMode Mode => mode;

    public IReadOnlyList<DiagramStep> Steps => plan ??= shown is null ? [] : DiagramSteps.Order(shown.Chart);

    public int Step => step;

    public DiagramStep? CurrentStep => mode == DiagramMode.Steps && step >= 0 && step < Steps.Count ? Steps[step] : null;

    public bool Playing => playing is not null;

    /// <summary>"Step 3 of 8", and the box with how the walk came to it.</summary>
    public string StepCaption
    {
        get
        {
            if (CurrentStep is not { } s) return "";
            string words = Label(s.Node);
            if (s.Via is { Length: > 0 } via) words = $"{via} → {words}";
            if (step == Steps.Count - 1 && s.Returns.Count > 0 && shown is not null)
            {
                var back = shown.Chart.Edges[s.Returns[0]];
                words += $", then back to {Label(back.To)}" + (back.Label is { Length: > 0 } l ? $" ({l.Replace('\n', ' ')})" : "");
            }
            return words;
        }
    }

    public void StartSteps()
    {
        HintSeen();
        StopRecall();
        mode = DiagramMode.Steps;
        pinned = null;
        pinnedEdge = -1;
        ActionsOpen = false;
        step = 0;
        if (Steps.Count > 0) ring = Steps[0].Node;
        SayStep();
        Refresh();
    }

    public void Next()
    {
        if (Steps.Count == 0) return;
        GoTo(step + 1 >= Steps.Count ? 0 : step + 1);
    }

    public void Previous()
    {
        if (Steps.Count == 0) return;
        GoTo(step - 1 < 0 ? Steps.Count - 1 : step - 1);
    }

    public void GoTo(int k)
    {
        if (Steps.Count == 0) return;
        step = Math.Clamp(k, 0, Steps.Count - 1);
        ring = Steps[step].Node;
        SayStep();
        Refresh();
    }

    /// <summary>Plays the steps on their own, one every couple of seconds, stopping at the last.</summary>
    public void TogglePlay()
    {
        if (playing is not null)
        {
            StopPlaying();
            return;
        }
        if (mode != DiagramMode.Steps) StartSteps();
        if (step >= Steps.Count - 1) GoTo(0);
        var timer = new DispatcherTimer(TimeSpan.FromSeconds(Motion.Reduced ? 3 : 2.2), DispatcherPriority.Normal, (s, _) =>
        {
            if (step >= Steps.Count - 1)
            {
                StopPlaying();
                return;
            }
            Next();
        });
        timer.Start();
        playing = new Stopper(timer);
        Changed?.Invoke();
    }

    void StopPlaying()
    {
        playing?.Dispose();
        playing = null;
        Changed?.Invoke();
    }

    sealed class Stopper(DispatcherTimer timer) : IDisposable
    {
        public void Dispose() => timer.Stop();
    }

    DiagramFocus StepFocus(DiagramScene scene)
    {
        // The walk so far stays lit (each box taken and the arrows between them); what's still to come dims.
        var nodes = new HashSet<string>();
        for (int i = 0; i <= step && i < Steps.Count; i++) nodes.Add(Steps[i].Node);
        var edges = new HashSet<int>();
        for (int i = 0; i < scene.Edges.Count; i++)
            if (nodes.Contains(scene.Edges[i].From) && nodes.Contains(scene.Edges[i].To)) edges.Add(i);
        if (CurrentStep is { } s && step == Steps.Count - 1) edges.UnionWith(s.Returns);
        return new DiagramFocus(nodes, edges);
    }

    void SayStep()
    {
        if (CurrentStep is { } s) Say($"Step {step + 1} of {Steps.Count}: {StepCaption}");
    }

    /// <summary>Back to exploring: no steps, nothing hidden.</summary>
    public void Explore()
    {
        StopPlaying();
        StopRecall();
        mode = DiagramMode.Explore;
        Refresh();
    }

    // --- recall -------------------------------------------------------------------------------------------------------

    readonly HashSet<string> hidden = [];
    readonly HashSet<string> revealed = [];
    readonly Dictionary<string, bool> marks = [];
    string? asking;
    bool some;

    /// <summary>How many boxes are hidden this round, how many have been checked, and how many the student knew.</summary>
    public int HiddenCount => hidden.Count;

    public int Checked => revealed.Count;

    public int Knew => marks.Count(m => m.Value);

    public int Missed => marks.Count(m => !m.Value);

    /// <summary>The box just revealed, waiting to hear whether the student knew it.</summary>
    public string? Asking => asking;

    /// <summary>Every hidden box has been revealed.</summary>
    public bool AllChecked => hidden.Count > 0 && revealed.IsSupersetOf(hidden);

    /// <summary>Only some boxes are hidden (shuffled), the rest left as cues.</summary>
    public bool Some => some;

    public void StartRecall()
    {
        HintSeen();
        StopPlaying();
        mode = DiagramMode.Recall;
        pinned = null;
        pinnedEdge = -1;
        ActionsOpen = false;
        some = false;
        Hide(RecallBoxes());
        Say("Words hidden. Say each box to yourself, then click it to check.");
        Refresh();
    }

    /// <summary>The boxes worth recalling: every box with words (not a folded group's count).</summary>
    IEnumerable<string> RecallBoxes() => shown?.Chart.Nodes.Where(n => n.Label.Trim().Length > 0 && !shown.IsFolded(n.Id)).Select(n => n.Id) ?? [];

    void Hide(IEnumerable<string> boxes)
    {
        hidden.Clear();
        hidden.UnionWith(boxes);
        revealed.Clear();
        marks.Clear();
        asking = null;
    }

    /// <summary>A new mix: about half the boxes hidden, chosen afresh, the rest left as cues.</summary>
    public void Shuffle()
    {
        var boxes = RecallBoxes().ToList();
        if (boxes.Count == 0) return;
        int take = Math.Max(1, (boxes.Count + 1) / 2);
        Hide(boxes.OrderBy(_ => Random.Shared.Next()).Take(take));
        some = true;
        Refresh();
    }

    public void Reveal(string id)
    {
        revealed.Add(id);
        asking = id;
        ring = id;
        Say($"{Label(id)}. Did you know it? Y for yes, N for not yet.");
        Refresh();
    }

    /// <summary>The student says whether they knew the box just revealed.</summary>
    public void Grade(bool knew)
    {
        if (asking is null) return;
        marks[asking] = knew;
        asking = null;
        if (AllChecked) Say($"You knew {Knew} of {HiddenCount}.");
        Refresh();
    }

    /// <summary>Every box shown, without marking any.</summary>
    public void ShowAll()
    {
        revealed.UnionWith(hidden);
        asking = null;
        Refresh();
    }

    /// <summary>The same boxes hidden again, the score cleared.</summary>
    public void ResetRecall()
    {
        Hide(hidden.ToList());
        Refresh();
    }

    /// <summary>Only the boxes the student missed, hidden again to try once more.</summary>
    public void PractiseMissed()
    {
        var missed = marks.Where(m => !m.Value).Select(m => m.Key).ToList();
        if (missed.Count == 0) return;
        Hide(missed);
        some = true;
        Refresh();
    }

    void StopRecall()
    {
        hidden.Clear();
        revealed.Clear();
        marks.Clear();
        asking = null;
    }

    void Reset()
    {
        StopPlaying();
        StopRecall();
        mode = DiagramMode.Explore;
        pinned = null;
        pinnedEdge = -1;
        ring = null;
        ActionsOpen = false;
        hover = DiagramTarget.Nothing;
        plan = null;
        step = 0;
    }

    // --- asking about a box, and the moment of the lecture ------------------------------------------------------------

    /// <summary>The page's host, if it has one (the lecture's page).</summary>
    public IDiagramHost? Host => DiagramHost.Find(view.Origin ?? view);

    /// <summary>The moment the diagram comes from ("%% Study Stash diagram, from 12:34"), in seconds.</summary>
    public double? At => DiagramMoment.From(view.Source);

    public bool CanAsk => Host?.CanAsk == true;

    public bool CanFind => Host?.Transcript.Count > 0;

    /// <summary>The question "Explain this" asks about a box: its words, what it joins, and the diagram it's in.</summary>
    public string ExplainQuestion(string id)
    {
        var (from, to) = Joins(id);
        string context = from.Count + to.Count == 0 ? ""
            : " In the diagram it" + (from.Count > 0 ? " comes from " + string.Join(", ", from.Select(f => Quote(f.Label) + (f.Via is { } v ? $" ({v})" : ""))) : "")
              + (from.Count > 0 && to.Count > 0 ? " and" : "")
              + (to.Count > 0 ? " leads to " + string.Join(", ", to.Select(t => Quote(t.Label) + (t.Via is { } v ? $" ({v})" : ""))) : "") + ".";
        return $"Explain {Quote(Label(id))} from {Diagram}, as the lecture taught it.{context}";
    }

    /// <summary>The question "Quiz me on this" asks: a few questions on the box and how it connects, answers after.</summary>
    public string QuizQuestion(string id)
    {
        var (from, to) = Joins(id);
        var near = from.Concat(to).Select(j => Quote(j.Label)).Distinct().ToList();
        return $"Quiz me on {Quote(Label(id))} from {Diagram}"
            + (near.Count > 0 ? $", and how it connects to {string.Join(", ", near)}" : "")
            + ": ask me three short questions from this lecture, one at a time, and wait for my answer before giving yours.";
    }

    static string Quote(string s) => "“" + s + "”";

    /// <summary>The diagram, as a question names it: by its title, where it has one.</summary>
    string Diagram => view.Named is { } title ? "the diagram " + Quote(title) : "this lecture's diagram";

    public void Ask(string question)
    {
        if (Host is not { CanAsk: true } host) return;
        host.Ask(question);
        ActionsOpen = false;
        Say("Asked: " + question);
        Changed?.Invoke();
        AskedFromWindow?.Invoke();
    }

    /// <summary>A question went to the lecture's Ask bar from a window of its own: the lecture's window comes forward.</summary>
    public Action? AskedFromWindow { get; set; }

    /// <summary>"Where was this said?": the lecture's transcript at the line that says the box's words best.</summary>
    public void FindSaid(string id)
    {
        if (Host is not { } host) return;
        var found = DiagramMoment.Said(Label(id), host.Transcript, At);
        ActionsOpen = false;
        if (found.Count == 0)
        {
            Say($"Couldn't find {Quote(Label(id))} in the transcript.", transient: true);
            return;
        }
        host.ShowTranscript(found[0].Line.Start);
        string others = found.Count > 1 ? " Also at " + string.Join(", ", found.Skip(1).Select(f => TimedText.Clock(f.Line.Start))) + "." : "";
        Say($"{Quote(Label(id))}: said at {TimedText.Clock(found[0].Line.Start)}.{others}", transient: true);
        AskedFromWindow?.Invoke();
    }

    public void JumpToMoment()
    {
        if (At is not { } at || Host is not { } host) return;
        host.ShowTranscript(at);
        Say($"The transcript at {TimedText.Clock(at)}, where this diagram comes from.", transient: true);
        AskedFromWindow?.Invoke();
    }

    public void PlayMoment()
    {
        if (At is not { } at || Host is not { CanPlay: true } host) return;
        host.Play(at);
        Say($"Playing the lecture from {TimedText.Clock(at)}.", transient: true);
    }

    // --- words for the caption and screen readers ---------------------------------------------------------------------

    /// <summary>The latest thing said about the diagram (a box the keyboard came to, a step, a result).</summary>
    public string? Said { get; private set; }

    /// <summary>A line said for a moment (a result), gone after a few seconds.</summary>
    public string? Note { get; private set; }

    DispatcherTimer? noteTimer;

    void Say(string words, bool transient = false)
    {
        Said = words;
        if (!transient) return;
        Note = words;
        noteTimer?.Stop();
        noteTimer = new DispatcherTimer(TimeSpan.FromSeconds(5), DispatcherPriority.Normal, (_, _) =>
        {
            noteTimer?.Stop();
            Note = null;
            Changed?.Invoke();
        });
        noteTimer.Start();
        Changed?.Invoke();
    }

    // --- the first-time hint ------------------------------------------------------------------------------------------

    /// <summary>Whether the student has been shown how to explore a diagram (asked of the app's settings).</summary>
    public static Func<bool> HintWasSeen { get; set; } = () => hintSeen;

    /// <summary>Remembers that they have.</summary>
    public static Action RememberHint { get; set; } = () => hintSeen = true;

    static bool hintSeen;

    public bool ShowHint => !hintDone && !HintWasSeen();

    bool hintDone;

    public void HintSeen()
    {
        if (hintDone) return;
        hintDone = true;
        if (!HintWasSeen()) RememberHint();
        Changed?.Invoke();
    }
}
