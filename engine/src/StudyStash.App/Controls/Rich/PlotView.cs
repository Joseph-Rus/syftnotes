using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using StudyStash.Core;
using StudyStash.Core.Rich;

namespace StudyStash.App.Controls.Rich;

/// <summary>
/// A plot in the notes, to play with: drawn exactly from its formulas (<see cref="PlotCanvas"/>), a slider under it
/// for each of its parameters (with a play button that sweeps it back and forth), and the diagrams' own controls
/// round it, in the same buttons and panels: a toolbar in its corner while the pointer or the keyboard is on it (back
/// to where it opened, reset the sliders, predict it, explain it, the moment of the lecture it comes from, open it
/// larger), a pinned reading's actions beside it (explain this, quiz me, where it was said), and a strip under it
/// while the student predicts the curve. On paper it's the still picture at its sliders' starting values, with a line
/// saying what they are.
/// </summary>
public sealed class PlotView : Decorator
{
    /// <summary>What a screen reader hears it does.</summary>
    public const string Help = "Drag a slider to change the plot. Hover to read its values, click to pin one. Plus and minus zoom, 0 goes back, R resets the sliders, P predicts it, Enter opens it larger.";

    readonly PlotCanvas canvas;
    readonly bool windowed;
    readonly Panel stage = new();
    readonly StackPanel sliders = new() { Spacing = 0, Margin = new Thickness(0, 4, 0, 0) };
    readonly List<(PlotParam Param, PlotSlider Slider, TextBlock Value, Button Play)> rows = [];

    public PlotView(Plot plot, string source, bool still = false, bool windowed = false)
    {
        Plot = plot;
        Source = source;
        State = new PlotState(plot);
        Still = still;
        this.windowed = windowed;
        canvas = new PlotCanvas(State, still) { Windowed = windowed };
        HorizontalAlignment = HorizontalAlignment.Stretch;
        if (still)
        {
            var paper = new StackPanel { Spacing = 4, Children = { canvas } };
            if (StillValues() is { } line) paper.Children.Add(line);
            Child = paper;
            AutomationProperties.SetName(this, PlotLayout.Describe(State));
            return;
        }
        Focusable = true;
        FocusAdorner = null;
        AutomationProperties.SetHelpText(this, Help);
        BuildChrome();
        foreach (var p in plot.Params) AddSlider(p);
        stage.Children.Add(canvas);
        stage.Children.Add(Actions);
        stage.Children.Add(Announcer);
        if (windowed)
        {
            stage.Margin = new Thickness(PlotWindow.Pad, PlotWindow.Pad / 2, PlotWindow.Pad, 0);
            sliders.Margin = new Thickness(PlotWindow.Pad, 6, PlotWindow.Pad, 8);
            var dock = new DockPanel();
            DockPanel.SetDock(Tools, Dock.Top);
            DockPanel.SetDock(Strip, Dock.Bottom);
            DockPanel.SetDock(sliders, Dock.Bottom);
            dock.Children.Add(Tools);
            dock.Children.Add(Strip);
            dock.Children.Add(sliders);
            dock.Children.Add(stage);
            stage.SizeChanged += (_, e) => canvas.RoomHeight = e.NewSize.Height;
            Child = dock;
        }
        else
        {
            stage.Children.Add(Tools);
            var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,Auto") };
            grid.Children.Add(stage);
            Grid.SetRow(sliders, 1);
            grid.Children.Add(sliders);
            Grid.SetRow(Strip, 2);
            grid.Children.Add(Strip);
            Child = grid;
        }
        sliders.IsVisible = rows.Count > 0;
        canvas.Changed += Changed;
        canvas.PinChanged += PlaceActions;
        canvas.HandleDrag += dragging => Coarse(dragging);
        canvas.KeyClicked += item =>
        {
            var hidden = new HashSet<PlotItem>(canvas.Look.Hidden);
            if (!hidden.Remove(item)) hidden.Add(item);
            canvas.Look = canvas.Look with { Hidden = hidden };
            Say(hidden.Contains(item) ? $"{item.Name} hidden." : $"{item.Name} shown.");
        };
        canvas.Sketched += Update;
        PointerEntered += (_, _) => Update();
        PointerExited += (_, _) => Update();
        GotFocus += (_, _) => Update();
        LostFocus += (_, _) => Update();
        KeyDown += (_, e) =>
        {
            if (e.Handled) return;
            if (Key(e.Key, e.KeyModifiers)) e.Handled = true;
        };
        DetachedFromVisualTree += (_, _) => StopPlaying();
        // A slider left playing in a window that's closed to the menu bar (hidden, not taken apart) would go on being
        // moved and redrawn sixty times a second for as long as the app runs.
        _ = new Seen(this, inView =>
        {
            if (!inView) StopPlaying();
        });
        AutomationProperties.SetName(this, PlotLayout.Describe(State));
        Update();
    }

    public Plot Plot { get; }
    public PlotState State { get; }
    public bool Still { get; }

    /// <summary>The plot as the note wrote it (it may say the moment of the lecture it comes from).</summary>
    public string Source { get; }

    /// <summary>The note's bold line just above it, its title: the plot doesn't draw its own title again then.</summary>
    public string? Caption
    {
        get => caption;
        set
        {
            caption = value;
            canvas.Look = canvas.Look with { Title = value is null && !windowed };
        }
    }

    string? caption;

    /// <summary>The plot in the note this window's plot was opened from: questions about it go to that note's lecture.</summary>
    public Control? Origin { get; set; }

    /// <summary>What it's called: its own title, else the note's bold line above it.</summary>
    public string Title => Plot.Title ?? caption ?? Plot.Items.FirstOrDefault(PlotLayout.Legendable)?.Name ?? "Plot";

    internal PlotCanvas Canvas => canvas;

    /// <summary>On paper: one quiet line saying where its sliders are, since paper can't move them.</summary>
    Control? StillValues()
    {
        var shown = Plot.Params.Where(p => !p.Name.StartsWith('\u0001')).ToList();
        if (shown.Count == 0) return null;
        var t = new TextBlock
        {
            Text = "Drawn at " + string.Join(", ", shown.Select(p => $"{p.Name} = {Value(p, p.Default)}" + (p.Label is { } l ? $" ({l})" : ""))) + ".",
            FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0),
        };
        t.Bind(TextBlock.ForegroundProperty, t.GetResourceObservable("Fg2"));
        t.Bind(TextBlock.FontFamilyProperty, t.GetResourceObservable("TextFont"));
        return t;
    }

    static string Value(PlotParam p, double v) => p.Step >= 1 && Math.Abs(v - Math.Round(v)) < 1e-9 ? Math.Round(v).ToString(System.Globalization.CultureInfo.InvariantCulture) : PlotNumber.Format(v);

    // --- the sliders ---------------------------------------------------------------------------------------------------

    bool Mac => Skin.Current == SkinKind.Mac;

    void AddSlider(PlotParam p)
    {
        int index = Plot.Params.IndexOf(p);
        var label = new TextBlock
        {
            Text = PlotNumber.Pretty(p.Label ?? p.Name), FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 0, 10, 0),
        };
        label.Bind(TextBlock.ForegroundProperty, label.GetResourceObservable("Fg2"));
        label.Bind(TextBlock.FontFamilyProperty, label.GetResourceObservable("TextFont"));
        var slider = new PlotSlider { Minimum = p.Min, Maximum = p.Max, Step = p.Step, Value = p.Default, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(slider, p.Label ?? p.Name);
        var value = new TextBlock { Text = Value(p, p.Default), FontSize = 12.5, FontWeight = FontWeight.Medium, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right, Margin = new Thickness(8, 0, 4, 0) };
        value.Bind(TextBlock.ForegroundProperty, value.GetResourceObservable("Fg"));
        value.Bind(TextBlock.FontFamilyProperty, value.GetResourceObservable("TextFont"));
        var play = Button("play_arrow", null, $"Play {p.Label ?? p.Name}: sweep it back and forth", () => TogglePlay(index));
        slider.Moved += v =>
        {
            StopPlaying();
            if (State.Set(index, v)) canvas.Refresh();
        };
        slider.DragChanged += Coarse;
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"), MinHeight = 30 };
        row.Children.Add(label);
        Grid.SetColumn(slider, 1);
        row.Children.Add(slider);
        Grid.SetColumn(value, 2);
        row.Children.Add(value);
        Grid.SetColumn(play, 3);
        row.Children.Add(play);
        sliders.Children.Add(row);
        rows.Add((p, slider, value, play));
    }

    /// <summary>Its sliders set to <paramref name="values"/> and <paramref name="hidden"/> hidden (a window opened from
    /// a note shows the plot as the note had it).</summary>
    public void Restore(IReadOnlyList<double> values, IEnumerable<PlotItem> hidden)
    {
        for (int i = 0; i < Plot.Params.Count && i < values.Count; i++) State.Set(i, values[i]);
        canvas.Look = canvas.Look with { Hidden = hidden.ToHashSet() };
        Changed();
    }

    /// <summary>The sliders' labels share one width (the widest, to a point), so the sliders line up.</summary>
    protected override Size MeasureOverride(Size availableSize)
    {
        if (rows.Count > 0)
        {
            double widest = 0;
            foreach (var r in rows)
            {
                var label = ((Grid)r.Slider.Parent!).Children.OfType<TextBlock>().First();
                label.Measure(Size.Infinity);
                widest = Math.Max(widest, label.DesiredSize.Width);
            }
            double cap = double.IsFinite(availableSize.Width) ? Math.Max(90, availableSize.Width * 0.38) : 220;
            foreach (var r in rows) ((Grid)r.Slider.Parent!).ColumnDefinitions[0].Width = new GridLength(Math.Min(widest, cap));
            double valueW = rows.Max(r =>
            {
                var probe = new TextBlock { Text = Value(r.Param, r.Param.Max) + "0", FontSize = 12.5, FontWeight = FontWeight.Medium };
                probe.Measure(Size.Infinity);
                return probe.DesiredSize.Width;
            });
            foreach (var r in rows) r.Value.MinWidth = valueW + 12;
        }
        return base.MeasureOverride(availableSize);
    }

    /// <summary>While something's dragged on a heavy plot (a heat map), it's drawn more coarsely, then finely again.</summary>
    void Coarse(bool dragging)
    {
        if (!Plot.Heavy) return;
        if (dragging) canvas.Look = canvas.Look with { Detail = 0.5 };
        else canvas.Look = canvas.Look with { Detail = 1 };
    }

    // --- playing a slider ----------------------------------------------------------------------------------------------

    DispatcherTimer? timer;
    int playing = -1;
    double phase;
    DateTime lastTick;

    void TogglePlay(int index)
    {
        if (playing == index)
        {
            StopPlaying();
            return;
        }
        StopPlaying();
        playing = index;
        var p = Plot.Params[index];
        // Start from where the slider is, heading towards its farther end.
        double f = (State.Values[index] - p.Min) / (p.Max - p.Min);
        phase = f >= 0.5 ? 2 - f : f;
        lastTick = DateTime.UtcNow;
        Coarse(true);
        timer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, (_, _) => Tick());
        timer.Start();
        Update();
    }

    void Tick()
    {
        if (playing < 0) return;
        var now = DateTime.UtcNow;
        double dt = Math.Min(0.1, (now - lastTick).TotalSeconds);
        lastTick = now;
        // A sweep end to end takes three seconds, there and back.
        phase = (phase + dt / 3) % 2;
        double f = phase <= 1 ? phase : 2 - phase;
        var p = Plot.Params[playing];
        double v = p.Min + f * (p.Max - p.Min);
        if (p.Step > 0) v = p.Snap(v);
        if (State.Set(playing, v)) canvas.Refresh();
    }

    void StopPlaying()
    {
        if (playing < 0) return;
        timer?.Stop();
        timer = null;
        playing = -1;
        Coarse(false);
        Update();
    }

    /// <summary>Whether a slider is being played.</summary>
    public bool Playing => playing >= 0;

    // --- the controls round it -----------------------------------------------------------------------------------------

    static ControlTheme? Surface => Application.Current?.TryFindResource("Surface", out var t) == true ? t as ControlTheme : null;

    Button Button(string? glyph, string? words, string tip, Action click, bool quietText = false)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, VerticalAlignment = VerticalAlignment.Center };
        if (glyph is not null)
        {
            var icon = new Icon { Glyph = glyph, Size = 16, VerticalAlignment = VerticalAlignment.Center };
            icon.Bind(Icon.ForegroundProperty, icon.GetResourceObservable("Fg"));
            row.Children.Add(icon);
        }
        if (words is not null)
        {
            var t = new TextBlock { Text = words, FontSize = 12, FontWeight = FontWeight.Medium, VerticalAlignment = VerticalAlignment.Center };
            t.Bind(TextBlock.ForegroundProperty, t.GetResourceObservable(quietText ? "Fg2" : "Fg"));
            row.Children.Add(t);
        }
        var b = new Button
        {
            Content = row, Height = 28, MinWidth = 28, Padding = new Thickness(words is null ? 6 : 9, 0),
            CornerRadius = new CornerRadius(Mac ? 7 : 4), HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center, Focusable = true,
        };
        if (Surface is { } theme) b.Theme = theme;
        ToolTip.SetTip(b, tip);
        AutomationProperties.SetName(b, words ?? tip);
        b.Click += (_, e) =>
        {
            e.Handled = true;
            click();
        };
        return b;
    }

    static void Words(Button b, string words)
    {
        if (b.Content is StackPanel row && row.Children.OfType<TextBlock>().FirstOrDefault() is { } t) t.Text = words;
        AutomationProperties.SetName(b, words);
    }

    static void Glyph(Button b, string glyph)
    {
        if (b.Content is StackPanel row && row.Children.OfType<Icon>().FirstOrDefault() is { } i) i.Glyph = glyph;
    }

    static void On(Button b, bool on)
    {
        if (on == b.Classes.Contains("on")) return;
        b.Classes.Set("on", on);
        if (on) b.Bind(Avalonia.Controls.Button.BackgroundProperty, b.GetResourceObservable("Hover"));
        else b.ClearValue(Avalonia.Controls.Button.BackgroundProperty);
    }

    Border Panel(Control content, double radius)
    {
        var p = new Border { Child = content, Padding = new Thickness(3), CornerRadius = new CornerRadius(Mac ? radius : 6), BorderThickness = new Thickness(1) };
        p.Bind(Border.BackgroundProperty, p.GetResourceObservable("PopupBg"));
        p.Bind(Border.BorderBrushProperty, p.GetResourceObservable("PopupStroke"));
        p.Bind(Border.BoxShadowProperty, p.GetResourceObservable("PopupShadow"));
        return p;
    }

    TextBlock Words(double size = 12.5, string colour = "Fg", FontWeight? weight = null)
    {
        var t = new TextBlock { FontSize = size, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, FontWeight = weight ?? FontWeight.Normal };
        t.Bind(TextBlock.ForegroundProperty, t.GetResourceObservable(colour));
        return t;
    }

    Control Tools { get; set; } = null!;
    Control Strip { get; set; } = null!;
    Control Actions { get; set; } = null!;
    TextBlock Announcer { get; set; } = null!;
    Button fit = null!, reset = null!, predict = null!, explain = null!, moment = null!, larger = null!, zoomIn = null!, zoomOut = null!, play = null!;
    Button reveal = null!, again = null!, done = null!, explainPin = null!, quizPin = null!, wherePin = null!;
    TextBlock stripWords = null!;

    void BuildChrome()
    {
        fit = Button(null, "100%", windowed ? "Back to where it opened (0)" : "Back to where it opened (0)", canvas.Home, quietText: !windowed);
        reset = Button("refresh", windowed ? "Reset sliders" : null, "Put the sliders back where they started (R)", ResetSliders);
        predict = Button("edit", windowed ? "Predict" : null, "Predict it: sketch the curve yourself, then reveal it (P)", TogglePredict);
        explain = Button("chat", windowed ? "Explain" : null, "Explain this plot in the Ask bar", () => Ask(ExplainQuestion()));
        moment = Button("schedule", windowed ? "" : null, "Read the transcript where this plot comes from", JumpToMoment);
        larger = Button("open_in_full", null, OpenLarger.Help, OpenLargerWindow);
        if (windowed)
        {
            zoomOut = Button("remove", null, "Zoom out (−)", () => canvas.ZoomBy(1 / 1.25));
            zoomIn = Button("add", null, "Zoom in (+)", () => canvas.ZoomBy(1.25));
            play = Button("play_arrow", null, "Play the lecture from there", PlayMoment);
            var row = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 2, LineSpacing = 4, VerticalAlignment = VerticalAlignment.Center };
            foreach (var c in new Control[] { zoomOut, fit, zoomIn, Rule(), reset, predict, explain, moment, play }) row.Children.Add(c);
            var bar = new Border { Child = row, Padding = new Thickness(Mac ? 14 : 12, 8) };
            var rule = new Border { Height = 1, VerticalAlignment = VerticalAlignment.Bottom };
            rule.Bind(Border.BackgroundProperty, rule.GetResourceObservable("Sep"));
            Tools = new Panel { Children = { bar, rule } };
        }
        else
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 1, Children = { fit, reset, predict, explain, moment, larger } };
            var bar = Panel(row, 10);
            bar.HorizontalAlignment = HorizontalAlignment.Right;
            bar.VerticalAlignment = VerticalAlignment.Top;
            bar.Margin = new Thickness(0, -6, -4, 0);
            Tools = bar;
        }

        // The strip: predicting (sketch it, then reveal), or a word for a moment.
        reveal = Button("check", "Reveal", "Show the real curve, and how close you were", Reveal);
        again = Button("refresh", "Try again", "Hide the curve and sketch it again", () =>
        {
            canvas.ClearSketch();
            score = null;
            canvas.Look = canvas.Look with { Predicting = true };
            Update();
        });
        done = Button("close", null, "Done (Esc)", EndPredict);
        stripWords = Words(12.5, "Fg2");
        stripWords.Margin = new Thickness(8, 0, 6, 0);
        AutomationProperties.SetLiveSetting(stripWords, AutomationLiveSetting.Polite);
        var line = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto"), MinHeight = 28 };
        line.Children.Add(stripWords);
        Grid.SetColumn(reveal, 1);
        Grid.SetColumn(again, 2);
        Grid.SetColumn(done, 3);
        line.Children.Add(reveal);
        line.Children.Add(again);
        line.Children.Add(done);
        var strip = new Border { Child = line, Padding = new Thickness(4), CornerRadius = new CornerRadius(windowed ? 0 : Mac ? 10 : 6), Margin = new Thickness(0, 6, 0, 0) };
        strip.Bind(Border.BackgroundProperty, strip.GetResourceObservable(Mac ? "Fill" : "Subtle"));
        Strip = strip;

        // A pinned reading's actions.
        explainPin = Button("chat", "Explain this", "Ask about this point in the Ask bar", () => Ask(ExplainQuestion(pinned: true)));
        quizPin = Button("quiz", "Quiz me", "Questions on this plot, one at a time, in the Ask bar", () => Ask(QuizQuestion()));
        wherePin = Button("graphic_eq", "Where was this said?", "The transcript where the lecture talks about this", FindSaid);
        var actions = Panel(new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 1, LineSpacing = 1, Children = { explainPin, quizPin, wherePin } }, 10);
        actions.HorizontalAlignment = HorizontalAlignment.Left;
        actions.VerticalAlignment = VerticalAlignment.Bottom;
        actions.MaxWidth = 460;
        actions.SizeChanged += (_, _) => Update();
        actions.IsVisible = false;
        Actions = actions;

        Announcer = new TextBlock { Opacity = 0, FontSize = 1, IsHitTestVisible = false, Width = 1, Height = 1, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        AutomationProperties.SetLiveSetting(Announcer, AutomationLiveSetting.Polite);
    }

    Control Rule()
    {
        var r = new Border { Width = 1, Height = 16, Margin = new Thickness(3, 0), VerticalAlignment = VerticalAlignment.Center };
        r.Bind(Border.BackgroundProperty, r.GetResourceObservable("Sep"));
        return r;
    }

    bool CanPredict => Plot.Items.Any(i => i is PlotCurve { Flat: false } or PlotSeries);

    /// <summary>Whether the note's toolbar shows: the pointer or the keyboard is on the plot, or it's in use.</summary>
    bool ToolsWanted => windowed || IsPointerOver || IsKeyboardFocusWithin || State.Zoomed || canvas.Look.Predicting || Playing;

    void Changed()
    {
        for (int i = 0; i < rows.Count; i++)
        {
            var (p, slider, value, _) = rows[i];
            double v = State.Values[Plot.Params.IndexOf(p)];
            slider.Value = v;
            value.Text = Value(p, v);
        }
        Update();
        PlaceActions();
        describe ??= new DispatcherTimer(TimeSpan.FromMilliseconds(400), DispatcherPriority.Background, (_, _) =>
        {
            describe?.Stop();
            AutomationProperties.SetName(this, PlotLayout.Describe(State));
        });
        describe.Stop();
        describe.Start();
    }

    DispatcherTimer? describe;

    void Update()
    {
        if (Tools is null) return;
        var host = Host;
        double percent = Math.Round(State.Home.Width / Math.Max(1e-300, State.View.Width) * 100);
        Words(fit, $"{percent:0}%");
        fit.IsVisible = windowed || State.Zoomed;
        reset.IsVisible = rows.Count > 0 && (windowed ? true : !State.AtDefaults);
        reset.IsEnabled = !State.AtDefaults;
        predict.IsVisible = CanPredict;
        On(predict, canvas.Look.Predicting || score is not null);
        explain.IsVisible = host?.CanAsk == true;
        moment.IsVisible = At is not null && host?.Transcript.Count > 0;
        if (At is { } at)
        {
            if (windowed) Words(moment, $"From {TimedText.Clock(at)} in the lecture");
            ToolTip.SetTip(moment, $"Read the transcript at {TimedText.Clock(at)}, where this plot comes from");
        }
        if (windowed)
        {
            play.IsVisible = At is not null && host?.CanPlay == true;
            zoomOut.IsEnabled = State.Zoomed || State.View.Width < State.Home.Width * 1e4;
        }
        else larger.IsVisible = true;
        Tools.IsVisible = ToolsWanted;
        foreach (var r in rows)
        {
            bool on = playing == Plot.Params.IndexOf(r.Param);
            Glyph(r.Play, on ? "pause" : "play_arrow");
            ToolTip.SetTip(r.Play, on ? "Pause" : $"Play {r.Param.Label ?? r.Param.Name}: sweep it back and forth");
        }

        // The strip.
        bool predicting = canvas.Look.Predicting;
        reveal.IsVisible = predicting;
        reveal.IsEnabled = canvas.Sketch.Count > 1;
        again.IsVisible = score is not null;
        done.IsVisible = predicting || score is not null;
        if (predicting) stripWords.Text = canvas.Sketch.Count > 1 ? "Happy with it? Reveal the real curve." : "Sketch where you think the curve goes: drag across the plot. Then reveal it.";
        else if (score is { } s) stripWords.Text = s;
        else if (note is { } n) stripWords.Text = n;
        Strip.IsVisible = predicting || score is not null || note is not null;

        // A pinned reading's actions.
        explainPin.IsVisible = quizPin.IsVisible = host?.CanAsk == true;
        wherePin.IsVisible = host?.Transcript.Count > 0;
        Actions.IsVisible = canvas.IsPinned && canvas.Pinned is not null && !predicting && (explainPin.IsVisible || wherePin.IsVisible);
        if (Actions.IsVisible && canvas.Pinned is { } pin && canvas.Scene is { } scene)
        {
            // Along the bottom of the plot's area, centred on the pinned reading's x as far as the area allows.
            var origin = canvas.TranslatePoint(default, stage) ?? default;
            double width = Actions.Bounds.Width > 0 ? Actions.Bounds.Width : 370;
            double lo = origin.X + scene.Map.Left, hi = Math.Max(lo, Math.Min(stage.Bounds.Width, origin.X + scene.Map.Right) - width);
            double x = Math.Clamp(origin.X + pin.X - width / 2, lo, hi);
            double below = stage.Bounds.Height > 0 ? Math.Max(0, stage.Bounds.Height - (origin.Y + scene.Map.Bottom) + 8) : 0;
            Actions.Margin = new Thickness(Math.Max(0, x), 0, 0, below);
        }
    }

    void PlaceActions() => Update();

    // --- predict, then reveal ------------------------------------------------------------------------------------------

    string? score;

    void TogglePredict()
    {
        if (canvas.Look.Predicting || score is not null)
        {
            EndPredict();
            return;
        }
        StopPlaying();
        canvas.Unpin();
        canvas.ClearSketch();
        score = null;
        canvas.Look = canvas.Look with { Predicting = true };
        Say("Predict it: sketch where you think the curve goes, then reveal it.");
        Update();
        Focus();
    }

    void Reveal()
    {
        canvas.Look = canvas.Look with { Predicting = false };
        score = Score();
        Say(score);
        Update();
    }

    /// <summary>How close the sketch came: its average distance from the first curve it's under, as a share of the
    /// plot's height.</summary>
    string Score()
    {
        var target = Plot.Items.FirstOrDefault(i => i is PlotCurve { Flat: false } or PlotSeries && !canvas.Look.Hidden.Contains(i));
        if (target is null || canvas.Sketch.Count < 2) return "Here's the real curve.";
        State.NewFrame();
        double sum = 0;
        int n = 0;
        foreach (var (x, y) in canvas.Sketch)
        {
            double truth = target is PlotCurve c ? State.At(c, x) : State.Term((PlotSeries)target, Math.Round(x));
            if (!double.IsFinite(truth)) continue;
            sum += Math.Min(Math.Abs(y - truth), State.View.Height);
            n++;
        }
        if (n == 0) return "Here's the real curve: it isn't where you sketched.";
        double share = sum / n / State.View.Height * 100;
        string name = target.Label?.Text(State.Env) ?? target.Name;
        return share < 4 ? $"Spot on: your sketch was within {share:0}% of {name} on average."
            : share < 12 ? $"Close: your sketch was {share:0}% of the plot's height from {name} on average."
            : $"Your sketch was {share:0}% of the plot's height from {name} on average. Compare the two, then try again.";
    }

    void EndPredict()
    {
        canvas.ClearSketch();
        score = null;
        canvas.Look = canvas.Look with { Predicting = false };
        Update();
    }

    // --- asking about it, and the moment of the lecture ----------------------------------------------------------------

    IDiagramHost? Host => DiagramHost.Find(Origin ?? this);

    /// <summary>The moment the plot comes from ("%% Study Stash diagram, from 12:34"), in seconds.</summary>
    public double? At => DiagramMoment.From(Source);

    static string Quote(string s) => "“" + s + "”";

    string Named => "the plot " + Quote(Title);

    string Formulas => string.Join("; ", Plot.Items.Where(PlotLayout.Legendable).Select(i => i switch
    {
        PlotCurve c => (c.Named is { } n ? $"{n}({c.Variable})" : "y") + " = " + c.Body.Text,
        PlotSeries s => $"{s.Function.Name}({s.Variable}) = {s.Body.Text}",
        PlotHeat h => $"{h.Function.Name}({string.Join(", ", h.Variables)}) = {h.Body.Text}",
        _ => i.Name,
    }).Take(4));

    string Sliders => string.Join(", ", Plot.Params.Where(p => !p.Name.StartsWith('\u0001')).Select(p => $"{p.Name} = {Value(p, State.Value(p))}"));

    /// <summary>"Explain this": the whole plot, or the pinned point's reading.</summary>
    internal string ExplainQuestion(bool pinned = false)
    {
        string with = Sliders.Length > 0 ? $" (with {Sliders})" : "";
        if (pinned && canvas.Pinned is { } p && canvas.ReadAt(p) is { } r)
            return $"Explain what {Named} shows at {r.Heading}{with}: {string.Join(", ", r.Rows.Select(row => $"{row.Name} is {row.Value}"))}. Why is it that value there, as the lecture taught it?";
        string formulas = Formulas;
        return $"Explain {Named}{(formulas.Length > 0 ? $" ({formulas})" : "")}: what its shape shows{(Sliders.Length > 0 ? " and what changes as its sliders move" : "")}, as the lecture taught it.";
    }

    internal string QuizQuestion() =>
        $"Quiz me on {Named}: ask me three short questions about what its shape shows{(Sliders.Length > 0 ? " and what happens when its parameters change" : "")}, one at a time, and wait for my answer before giving yours.";

    void Ask(string question)
    {
        if (Host is not { CanAsk: true } host) return;
        host.Ask(question);
        canvas.Unpin();
        Say("Asked: " + question, transient: true);
        AskedFromWindow?.Invoke();
    }

    /// <summary>A question went to the lecture's Ask bar from a window of its own: the lecture's window comes forward.</summary>
    public Action? AskedFromWindow { get; set; }

    void FindSaid()
    {
        if (Host is not { } host) return;
        string words = Title + " " + string.Join(" ", Plot.Words());
        var found = DiagramMoment.Said(words, host.Transcript, At);
        canvas.Unpin();
        if (found.Count == 0)
        {
            Say("Couldn't find where the lecture says this.", transient: true);
            return;
        }
        host.ShowTranscript(found[0].Line.Start);
        Say($"Said at {TimedText.Clock(found[0].Line.Start)}.", transient: true);
        AskedFromWindow?.Invoke();
    }

    void JumpToMoment()
    {
        if (At is not { } at || Host is not { } host) return;
        host.ShowTranscript(at);
        Say($"The transcript at {TimedText.Clock(at)}, where this plot comes from.", transient: true);
        AskedFromWindow?.Invoke();
    }

    void PlayMoment()
    {
        if (At is not { } at || Host is not { CanPlay: true } host) return;
        host.Play(at);
        Say($"Playing the lecture from {TimedText.Clock(at)}.", transient: true);
    }

    string? note;
    DispatcherTimer? noteTimer;

    void Say(string words, bool transient = false)
    {
        Announcer.Text = words;
        if (!transient) return;
        note = words;
        noteTimer?.Stop();
        noteTimer = new DispatcherTimer(TimeSpan.FromSeconds(5), DispatcherPriority.Normal, (_, _) =>
        {
            noteTimer?.Stop();
            note = null;
            Update();
        });
        noteTimer.Start();
        Update();
    }

    // --- the keyboard, and opening it larger ----------------------------------------------------------------------------

    void ResetSliders()
    {
        StopPlaying();
        if (State.Reset()) canvas.Refresh();
        Say("Sliders reset.");
    }

    bool Key(Key key, KeyModifiers modifiers)
    {
        bool plain = modifiers is KeyModifiers.None or KeyModifiers.Shift;
        bool command = modifiers.HasFlag(KeyModifiers.Control) || modifiers.HasFlag(KeyModifiers.Meta);
        switch (key)
        {
            case Avalonia.Input.Key.OemPlus or Avalonia.Input.Key.Add when plain || command:
                canvas.ZoomBy(1.25);
                return true;
            case Avalonia.Input.Key.OemMinus or Avalonia.Input.Key.Subtract when plain || command:
                canvas.ZoomBy(1 / 1.25);
                return true;
            case Avalonia.Input.Key.D0 or Avalonia.Input.Key.NumPad0 when plain || command:
                canvas.Home();
                return true;
            case Avalonia.Input.Key.R when plain && rows.Count > 0:
                ResetSliders();
                return true;
            case Avalonia.Input.Key.P when plain && CanPredict:
                TogglePredict();
                return true;
            case Avalonia.Input.Key.Escape:
                if (canvas.Look.Predicting || score is not null) EndPredict();
                else if (canvas.IsPinned) canvas.Unpin();
                else if (State.Zoomed) canvas.Home();
                else return false;
                return true;
            case Avalonia.Input.Key.Left or Avalonia.Input.Key.Right when plain && canvas.Scene is { } s:
            {
                // Moves the pinned reading along x (pins one in the middle first).
                var (x, y) = canvas.PinnedAt ?? ((State.View.X0 + State.View.X1) / 2, (State.View.Y0 + State.View.Y1) / 2);
                double step = State.View.Width / (modifiers == KeyModifiers.Shift ? 10 : 100);
                canvas.Pin(x + (key == Avalonia.Input.Key.Right ? step : -step), y);
                if (canvas.ReadAt(s.Map.Screen(canvas.PinnedAt!.Value.X, y)) is { } r) Say($"{r.Heading}: {string.Join(", ", r.Rows.Select(row => $"{row.Name} {row.Value}"))}");
                return true;
            }
            case Avalonia.Input.Key.Enter when modifiers == KeyModifiers.None && !windowed:
                OpenLargerWindow();
                return true;
        }
        return false;
    }

    void OpenLargerWindow()
    {
        if (windowed) return;
        PlotWindow.Open(this, TopLevel.GetTopLevel(this));
    }
}
