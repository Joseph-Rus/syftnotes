using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StudyStash.App.Platform;
using StudyStash.Core;
using StudyStash.Core.Rich;

namespace StudyStash.App.Controls.Rich;

/// <summary>What an illustration shows over its picture while it's explored: the part lit (and how far the rest has
/// dimmed), whether the labels show, and, while testing yourself, which labels are hidden and which shown again.</summary>
sealed record PartsLook(string? Lit, double Dim, bool Labels, bool Recall, IReadOnlySet<string> Blanks, IReadOnlySet<string> Shown, string? Ring);

/// <summary>
/// An illustration a student can explore, on screen only (paper gets the still picture): the pointer over a part lights
/// it (with its label, even with labels off) while the rest dims, and a click pins it with its name, its line from the
/// lecture and its actions (explain it, quiz me, where it was said, zoom in); the labels turn off and on; a tour walks
/// the parts in the order their labels are read; Test yourself hides every label to be remembered, revealed by a click
/// and scored; and it zooms and pans like a flowchart. Which part is where comes from a map of the picture drawn part by
/// part (<see cref="PartMap"/>). The chrome round it (<see cref="PartsChrome"/>) shows and drives this.
/// </summary>
sealed class PartsExplorer
{
    readonly SvgView view;
    readonly SvgCanvas canvas;
    readonly Control room;

    public PartsExplorer(SvgView view, SvgCanvas canvas, Control room, bool windowed)
    {
        this.view = view;
        this.canvas = canvas;
        this.room = room;
        Windowed = windowed;
        Zoomer = new Zoomer(canvas, room) { Max = windowed ? 6 : 4 };
        Zoomer.Changed += () => Changed?.Invoke();
        Wire();
    }

    public bool Windowed { get; }

    public Zoomer Zoomer { get; }

    /// <summary>Whenever anything the chrome shows changes.</summary>
    public event Action? Changed;

    // --- the drawing ------------------------------------------------------------------------------------------------

    SafeSvgResult? drawing;
    IReadOnlyList<SvgPart> parts = [];
    Dictionary<string, Callout> callouts = [];
    PartMap? map;
    int loads;

    /// <summary>The drawing's parts, in the order a student reads their labels (down the left, then down the right),
    /// those without a label after them.</summary>
    public IReadOnlyList<SvgPart> Parts => parts;

    /// <summary>The cleaned drawing, in its written colours.</summary>
    public SafeSvgResult? Drawing => drawing;

    /// <summary>Where each part's label's words are (for the blanks while testing yourself).</summary>
    public IReadOnlyDictionary<string, Callout> Callouts => callouts;

    /// <summary>The map of which part is where, once it's been made (a moment after the drawing arrives).</summary>
    public PartMap? Map => map;

    public void Load(SafeSvgResult cleaned)
    {
        Reset();
        drawing = cleaned;
        var said = Illustration.Callouts(cleaned.Svg!).GroupBy(c => c.Id).ToDictionary(g => g.Key, g => g.First());
        callouts = said;
        double middle = cleaned.Width / 2;
        parts = [.. cleaned.Parts.OrderBy(p => said.TryGetValue(p.Id, out var c) ? (c.Words.CenterX < middle ? 0 : 1) : 2)
            .ThenBy(p => said.TryGetValue(p.Id, out var c) ? c.Words.Y : 0)];
        map = null;
        int load = ++loads;
        string svg = cleaned.Svg!;
        var ids = cleaned.Parts.Select(p => p.Id).ToList();
        var ui = Dispatcher.UIThread;
        Task.Run(() => PartMap.Make(svg, ids, cleaned.Width, cleaned.Height)).ContinueWith(t => ui.Post(() =>
        {
            if (load != loads || t.Status != TaskStatus.RanToCompletion) return;
            map = t.Result;
            Changed?.Invoke();
            Paint();
        }), TaskScheduler.Default);
        Paint();
    }

    /// <summary>A part by its id.</summary>
    public SvgPart? Part(string id) => parts.FirstOrDefault(p => p.Id == id);

    /// <summary>A part's name.</summary>
    public string Name(string id) => Part(id)?.Name ?? id;

    /// <summary>One line about a part, for the strip and screen readers: its name and its line from the lecture.</summary>
    public string About(string id) => Part(id) is { } p ? p.Note.Length > 0 ? $"{p.Name}: {p.Note}" : p.Name : id;

    // --- what's lit ---------------------------------------------------------------------------------------------------

    string? hover, pinned, ring;
    double dim, dimTo;
    IDisposable? dimming;
    string? lit;

    public string? Pinned => pinned;

    public string? Hover => hover;

    public string? Ring => ring;

    /// <summary>The pinned part's card is showing.</summary>
    public bool ActionsOpen { get; private set; }

    /// <summary>Whether the labels show (the student can turn them off; testing yourself hides them).</summary>
    public bool Labels { get; private set; } = true;

    string? FocusNow() => mode switch
    {
        DiagramMode.Steps => CurrentStep,
        DiagramMode.Recall => asking ?? hover ?? (view.KeyboardFocused ? ring : null),
        _ => pinned ?? hover ?? (view.KeyboardFocused ? ring : null),
    };

    /// <summary>Works out what's lit now and draws it, easing the dimming in (or out).</summary>
    void Refresh()
    {
        var focus = FocusNow();
        if (focus is not null) lit = focus;
        double to = focus is null ? 0 : 1;
        if (Math.Abs(dimTo - to) > 1e-3)
        {
            dimTo = to;
            dimming?.Dispose();
            double from = dim;
            dimming = Motion.Animate(canvas, TimeSpan.FromMilliseconds(to > 0 ? 140 : 200), t =>
            {
                dim = from + (to - from) * t;
                Paint();
            }, () =>
            {
                dimming = null;
                if (dimTo < 0.5) lit = null;
                Paint();
            });
        }
        else Paint();
        Changed?.Invoke();
    }

    void Paint()
    {
        bool recall = mode == DiagramMode.Recall;
        var blanks = recall ? hidden.Where(h => !revealed.Contains(h)).ToHashSet() : [];
        var shownAgain = recall ? revealed.ToHashSet() : [];
        bool still = lit is null && Labels && !recall && !(view.KeyboardFocused && ring is not null);
        // Nothing over it: the still picture, exactly as a note that's never been touched draws it.
        canvas.Look = still ? null : new PartsLook(lit, dim, Labels, recall, blanks, shownAgain, view.KeyboardFocused ? ring : null);
    }

    public void ToggleLabels()
    {
        Labels = !Labels;
        Say(Labels ? "Labels shown." : "Labels hidden. Point at a part to see its name.");
        Refresh();
    }

    // --- the pointer --------------------------------------------------------------------------------------------------

    Point? pressedAt;
    string? pressedOn;
    bool pressedPaper;
    int presses;
    Point lastDrag;
    bool dragging;
    double pinchScale = 1;

    /// <summary>The part under a point of the canvas, if any.</summary>
    public string? PartAt(Point onCanvas)
    {
        if (map is null || drawing is null || canvas.Bounds.Width <= 0) return null;
        double k = canvas.Bounds.Width / drawing.Width;
        return map.At(onCanvas.X / k, onCanvas.Y / k, PartMap.Slop / Math.Max(0.5, k * Zoomer.Zoom));
    }

    void Wire()
    {
        room.PointerMoved += (_, e) =>
        {
            var p = e.GetPosition(canvas);
            if (pressedAt is { } start && e.GetCurrentPoint(view).Properties.IsLeftButtonPressed)
            {
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
            HoverAt(PartAt(p));
        };
        room.PointerExited += (_, _) =>
        {
            if (!dragging) HoverAt(null);
        };
        room.PointerPressed += (_, e) =>
        {
            var point = e.GetCurrentPoint(view);
            if (point.Properties.IsRightButtonPressed)
            {
                if (PartAt(e.GetPosition(canvas)) is { } id && mode == DiagramMode.Explore)
                {
                    Pin(id, actions: true);
                    e.Handled = true;
                }
                return;
            }
            if (!point.Properties.IsLeftButtonPressed) return;
            if (e.Source is Visual v && v.FindAncestorOfType<Avalonia.Controls.Primitives.ScrollBar>(includeSelf: true) is not null) return;
            pressedAt = lastDrag = point.Position;
            presses = e.ClickCount;
            pressedOn = PartAt(e.GetPosition(canvas));
            pressedPaper = new Rect(canvas.Bounds.Size).Contains(e.GetPosition(canvas));
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
            room.Cursor = null;
            if (!click || e.InitialPressMouseButton != MouseButton.Left) return;
            e.Handled = true;
            if (presses >= 2 && pressedOn is { } twice)
            {
                ZoomTo(twice);
                return;
            }
            Click(pressedOn, pressedPaper);
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
                if (!view.IsKeyboardFocusWithin) view.Focus(NavigationMethod.Pointer);
                e.Handled = true;
            }
            else if (Windowed || (CanPan && e.Delta.X != 0 && Math.Abs(e.Delta.X) > Math.Abs(e.Delta.Y)))
            {
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

    Point RoomPoint(PointerEventArgs e) => Windowed ? e.GetPosition(room) : Zoomer.ToRoom(e.GetPosition(canvas));

    /// <summary>The picture is bigger than its room (zoomed in, or too wide), so a drag moves it.</summary>
    public bool CanPan => canvas.Bounds.Width * Zoomer.Zoom > (Windowed ? room.Bounds.Width : canvas.Bounds.Width) + 1
        || canvas.Bounds.Height * Zoomer.Zoom > (Windowed ? room.Bounds.Height : canvas.Bounds.Height) + 1;

    void HoverAt(string? id)
    {
        if (id == hover) return;
        hover = id;
        room.Cursor = id is null ? null : new Cursor(StandardCursorType.Hand);
        if (id is not null && mode == DiagramMode.Explore && pinned is null) Say(About(id));
        Refresh();
    }

    /// <summary>A click on the picture: on a part, or on the paper round them (<paramref name="paper"/>).</summary>
    public void Click(string? id, bool paper = true)
    {
        HintSeen();
        switch (mode)
        {
            case DiagramMode.Steps:
                if (id is not null && StepOf(id) is int k) GoTo(k);
                return;
            case DiagramMode.Recall:
                if (id is not null && hidden.Contains(id) && !revealed.Contains(id)) Reveal(id);
                return;
        }
        if (id is not null)
        {
            if (pinned == id) Unpin();
            else Pin(id, actions: true);
        }
        else if (pinned is not null) Unpin();
        else if (!Windowed && paper) view.OpenLarger();
    }

    /// <summary>Pins a part: it stays lit, and its card (its name, its line, its actions) shows beside it.</summary>
    public void Pin(string id, bool actions)
    {
        pinned = id;
        ring = id;
        ActionsOpen = actions;
        Say(About(id));
        Refresh();
    }

    public void Unpin()
    {
        pinned = null;
        ActionsOpen = false;
        Refresh();
    }

    /// <summary>Zooms in on a part.</summary>
    public void ZoomTo(string id)
    {
        if (map?.Of(id) is not { } b || drawing is null || canvas.Bounds.Width <= 0) return;
        double k = canvas.Bounds.Width / drawing.Width;
        var around = b.Inflate(Windowed ? 40 : 20);
        Zoomer.Show(new Rect(around.X * k, around.Y * k, around.W * k, around.H * k), most: Math.Max(1.8, 2.2 / Math.Max(0.1, k)));
    }

    /// <summary>Where a part is on the canvas (its box, in the canvas's own pixels), once the map is made.</summary>
    public Rect? Where(string id)
    {
        if (map?.Of(id) is not { } b || drawing is null || canvas.Bounds.Width <= 0) return null;
        double k = canvas.Bounds.Width / drawing.Width;
        return new Rect(b.X * k, b.Y * k, b.W * k, b.H * k);
    }

    // --- the keyboard -------------------------------------------------------------------------------------------------

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
            case Avalonia.Input.Key.Right or Avalonia.Input.Key.Down: return Walk(1);
            case Avalonia.Input.Key.Left or Avalonia.Input.Key.Up: return Walk(-1);
            case Avalonia.Input.Key.Enter or Avalonia.Input.Key.Space when mode == DiagramMode.Explore:
                if (ring is { } r)
                {
                    Pin(r, actions: true);
                    return true;
                }
                if (!Windowed)
                {
                    view.OpenLarger();
                    return true;
                }
                return Walk(0);
            case Avalonia.Input.Key.Escape:
                if (ActionsOpen)
                {
                    ActionsOpen = false;
                    Changed?.Invoke();
                    return true;
                }
                if (pinned is not null)
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
            case Avalonia.Input.Key.L:
                ToggleLabels();
                return true;
            case Avalonia.Input.Key.S:
                StartSteps();
                return true;
            case Avalonia.Input.Key.H:
                StartRecall();
                return true;
            case Avalonia.Input.Key.Z when (pinned ?? ring) is { } z:
                ZoomTo(z);
                return true;
        }
        return false;
    }

    /// <summary>Moves the keyboard's ring to the next part (or the one before), in the order their labels are read.</summary>
    bool Walk(int by)
    {
        if (parts.Count == 0) return false;
        int at = ring is null ? -1 : parts.ToList().FindIndex(p => p.Id == ring);
        int to = at < 0 ? (by < 0 ? parts.Count - 1 : 0) : ((at + by) % parts.Count + parts.Count) % parts.Count;
        ring = parts[to].Id;
        hover = null;
        if (pinned is not null && pinned != ring)
        {
            pinned = null;
            ActionsOpen = false;
        }
        Say(About(ring));
        Refresh();
        return true;
    }

    /// <summary>The keyboard came to the drawing or left it: its ring shows only while it's there.</summary>
    public void FocusChanged() => Refresh();

    // --- the tour -----------------------------------------------------------------------------------------------------

    DiagramMode mode;
    int step;
    IDisposable? playing;

    public DiagramMode Mode => mode;

    public int Step => step;

    public string? CurrentStep => mode == DiagramMode.Steps && step >= 0 && step < parts.Count ? parts[step].Id : null;

    public bool Playing => playing is not null;

    int? StepOf(string id) => parts.ToList().FindIndex(p => p.Id == id) is int k and >= 0 ? k : null;

    public void StartSteps()
    {
        if (parts.Count == 0) return;
        HintSeen();
        StopRecall();
        mode = DiagramMode.Steps;
        pinned = null;
        ActionsOpen = false;
        step = 0;
        ring = parts[0].Id;
        SayStep();
        Refresh();
    }

    public void Next()
    {
        if (parts.Count > 0) GoTo(step + 1 >= parts.Count ? 0 : step + 1);
    }

    public void Previous()
    {
        if (parts.Count > 0) GoTo(step - 1 < 0 ? parts.Count - 1 : step - 1);
    }

    public void GoTo(int k)
    {
        if (parts.Count == 0) return;
        step = Math.Clamp(k, 0, parts.Count - 1);
        ring = parts[step].Id;
        SayStep();
        Refresh();
    }

    /// <summary>Tours the parts on its own, one every few seconds (time to read each one's line), stopping at the last.</summary>
    public void TogglePlay()
    {
        if (playing is not null)
        {
            StopPlaying();
            return;
        }
        if (mode != DiagramMode.Steps) StartSteps();
        if (step >= parts.Count - 1) GoTo(0);
        var timer = new DispatcherTimer(TimeSpan.FromSeconds(Motion.Reduced ? 4 : 3.2), DispatcherPriority.Normal, (_, _) =>
        {
            if (step >= parts.Count - 1)
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

    void SayStep()
    {
        if (CurrentStep is { } id) Say($"Part {step + 1} of {parts.Count}: {About(id)}");
    }

    /// <summary>Back to exploring: no tour, nothing hidden.</summary>
    public void Explore()
    {
        StopPlaying();
        StopRecall();
        mode = DiagramMode.Explore;
        Refresh();
    }

    // --- testing yourself ---------------------------------------------------------------------------------------------

    readonly HashSet<string> hidden = [];
    readonly HashSet<string> revealed = [];
    readonly Dictionary<string, bool> marks = [];
    string? asking;
    bool some;

    public int HiddenCount => hidden.Count;
    public int Checked => revealed.Count;
    public int Knew => marks.Count(m => m.Value);
    public int Missed => marks.Count(m => !m.Value);
    public string? Asking => asking;
    public bool AllChecked => hidden.Count > 0 && revealed.IsSupersetOf(hidden);
    public bool Some => some;

    /// <summary>What's worth recalling: every part with a label.</summary>
    IEnumerable<string> Recallable() => parts.Where(p => callouts.ContainsKey(p.Id)).Select(p => p.Id);

    public void StartRecall()
    {
        var all = Recallable().ToList();
        if (all.Count == 0) return;
        HintSeen();
        StopPlaying();
        mode = DiagramMode.Recall;
        pinned = null;
        ActionsOpen = false;
        some = false;
        Hide(all);
        Say("Labels hidden. Name each part to yourself, then click it to check.");
        Refresh();
    }

    void Hide(IEnumerable<string> ids)
    {
        hidden.Clear();
        hidden.UnionWith(ids);
        revealed.Clear();
        marks.Clear();
        asking = null;
    }

    public void Shuffle()
    {
        var all = Recallable().ToList();
        if (all.Count == 0) return;
        Hide(all.OrderBy(_ => Random.Shared.Next()).Take(Math.Max(1, (all.Count + 1) / 2)));
        some = true;
        Refresh();
    }

    public void Reveal(string id)
    {
        revealed.Add(id);
        asking = id;
        ring = id;
        Say($"{Name(id)}. Did you know it? Y for yes, N for not yet.");
        Refresh();
    }

    public void Grade(bool knew)
    {
        if (asking is null) return;
        marks[asking] = knew;
        asking = null;
        if (AllChecked) Say($"You knew {Knew} of {HiddenCount}.");
        Refresh();
    }

    public void ShowAll()
    {
        revealed.UnionWith(hidden);
        asking = null;
        Refresh();
    }

    public void ResetRecall()
    {
        Hide(hidden.ToList());
        Refresh();
    }

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
        pinned = hover = ring = lit = null;
        ActionsOpen = false;
        step = 0;
        dim = dimTo = 0;
    }

    // --- asking about a part, and the moment of the lecture -------------------------------------------------------------

    public IDiagramHost? Host => DiagramHost.Find(view.Origin ?? view);

    /// <summary>The moment the drawing comes from ("Study Stash diagram, from 12:34"), in seconds.</summary>
    public double? At => DiagramMoment.From(view.Source);

    public bool CanAsk => Host?.CanAsk == true;

    static string Quote(string s) => "“" + s + "”";

    string Figure => view.Drawing?.Title is { Length: > 0 } t ? "the illustration " + Quote(t) : "this lecture's illustration";

    /// <summary>The question "Explain this" asks about a part: its name, what the drawing says of it, the figure.</summary>
    public string ExplainQuestion(string id) =>
        $"Explain the {Quote(Name(id))} in {Figure}, as the lecture taught it: what it is, where it sits and what it does."
        + (Part(id)?.Note is { Length: > 0 } note ? $" The figure says: {note}" : "");

    /// <summary>The question "Quiz me" asks: a few questions on the part, answers after.</summary>
    public string QuizQuestion(string id) =>
        $"Quiz me on the {Quote(Name(id))} in {Figure}: ask me three short questions from this lecture about it, one at a time, and wait for my answer before giving yours.";

    public void Ask(string question)
    {
        if (Host is not { CanAsk: true } host) return;
        host.Ask(question);
        ActionsOpen = false;
        Say("Asked: " + question);
        Changed?.Invoke();
        AskedFromWindow?.Invoke();
    }

    public Action? AskedFromWindow { get; set; }

    /// <summary>"Where was this said?": the lecture's transcript at the line that says the part's name best.</summary>
    public void FindSaid(string id)
    {
        if (Host is not { } host) return;
        var found = DiagramMoment.Said(Name(id), host.Transcript, At);
        ActionsOpen = false;
        if (found.Count == 0)
        {
            Say($"Couldn't find {Quote(Name(id))} in the transcript.", transient: true);
            return;
        }
        host.ShowTranscript(found[0].Line.Start);
        string others = found.Count > 1 ? " Also at " + string.Join(", ", found.Skip(1).Select(f => TimedText.Clock(f.Line.Start))) + "." : "";
        Say($"{Quote(Name(id))}: said at {TimedText.Clock(found[0].Line.Start)}.{others}", transient: true);
        AskedFromWindow?.Invoke();
    }

    public void JumpToMoment()
    {
        if (At is not { } at || Host is not { } host) return;
        host.ShowTranscript(at);
        Say($"The transcript at {TimedText.Clock(at)}, where this illustration comes from.", transient: true);
        AskedFromWindow?.Invoke();
    }

    public void PlayMoment()
    {
        if (At is not { } at || Host is not { CanPlay: true } host) return;
        host.Play(at);
        Say($"Playing the lecture from {TimedText.Clock(at)}.", transient: true);
    }

    // --- words for the strip and screen readers -------------------------------------------------------------------------

    public string? Said { get; private set; }

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

    // --- the first-time hint (one for every diagram and drawing) --------------------------------------------------------

    public bool ShowHint => !hintDone && !DiagramExplorer.HintWasSeen();

    bool hintDone;

    public void HintSeen()
    {
        if (hintDone) return;
        hintDone = true;
        if (!DiagramExplorer.HintWasSeen()) DiagramExplorer.RememberHint();
        Changed?.Invoke();
    }
}
