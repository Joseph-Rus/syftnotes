using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using StudyStash.Core;

namespace StudyStash.App.Controls.Rich;

/// <summary>
/// The controls round an illustration a student explores, in the same buttons, panels and type as a flowchart's
/// (<see cref="DiagramChrome"/>): in a note, a small toolbar in its corner while the pointer or the keyboard is on it
/// (labels off and on, tour the parts, test yourself, the moment of the lecture, open larger), a strip under it while it's
/// toured or tested, a pinned part's card beside it (its name, its line from the lecture, explain it, quiz me, where it
/// was said, zoom in), and the first-time hint; in a window of its own, a toolbar across the top (with zoom) and the
/// strip along the bottom saying what the pointer is on.
/// </summary>
sealed class PartsChrome
{
    readonly PartsExplorer x;
    readonly SvgView view;
    readonly bool windowed;
    bool Mac => Skin.Current == SkinKind.Mac;

    public PartsChrome(PartsExplorer explorer, SvgView view, bool windowed)
    {
        x = explorer;
        this.view = view;
        this.windowed = windowed;
        Tools = windowed ? WindowTools() : NoteTools();
        Strip = BuildStrip();
        Actions = BuildActions();
        Hint = BuildHint();
        Announcer = new TextBlock { Opacity = 0, FontSize = 1, IsHitTestVisible = false, Width = 1, Height = 1, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        AutomationProperties.SetLiveSetting(Announcer, AutomationLiveSetting.Polite);
        x.Changed += Update;
        Update();
    }

    public Control Tools { get; }
    public Control Strip { get; }
    public Control Actions { get; }
    public Control Hint { get; }
    public TextBlock Announcer { get; }

    // --- buttons (the flowchart's own) --------------------------------------------------------------------------------

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

    static void On(Button b, bool on)
    {
        if (on == b.Classes.Contains("on")) return;
        b.Classes.Set("on", on);
        if (on) b.Bind(Avalonia.Controls.Button.BackgroundProperty, b.GetResourceObservable("Hover"));
        else b.ClearValue(Avalonia.Controls.Button.BackgroundProperty);
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

    Border Panel(Control content, double radius)
    {
        var p = new Border { Child = content, Padding = new Thickness(3), CornerRadius = new CornerRadius(Mac ? radius : 6), BorderThickness = new Thickness(1) };
        p.Bind(Border.BackgroundProperty, p.GetResourceObservable("PopupBg"));
        p.Bind(Border.BorderBrushProperty, p.GetResourceObservable("PopupStroke"));
        p.Bind(Border.BoxShadowProperty, p.GetResourceObservable("PopupShadow"));
        return p;
    }

    Control Rule()
    {
        var r = new Border { Width = 1, Height = 16, Margin = new Thickness(3, 0), VerticalAlignment = VerticalAlignment.Center };
        r.Bind(Border.BackgroundProperty, r.GetResourceObservable("Sep"));
        return r;
    }

    TextBlock Text(double size = 12.5, string colour = "Fg", FontWeight? weight = null)
    {
        var t = new TextBlock { FontSize = size, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, FontWeight = weight ?? FontWeight.Normal };
        t.Bind(TextBlock.ForegroundProperty, t.GetResourceObservable(colour));
        return t;
    }

    // --- the toolbar --------------------------------------------------------------------------------------------------

    Button labels = null!, tour = null!, recall = null!, fit = null!, zoomOut = null!, zoomIn = null!, moment = null!, play = null!;

    Control NoteTools()
    {
        labels = Button("subject", null, "Hide the labels (L)", ToggleLabels);
        tour = Button("play_circle", null, "Tour the parts (S)", () => Toggle(DiagramMode.Steps));
        recall = Button("quiz", null, "Test yourself: hide the labels (H)", () => Toggle(DiagramMode.Recall));
        fit = Button(null, "100%", "Back to fitted (0)", () => x.Zoomer.Fit(), quietText: true);
        moment = Button("schedule", null, "Read the transcript where this illustration comes from", x.JumpToMoment);
        var larger = Button("open_in_full", null, OpenLarger.Help, view.OpenLarger);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 1, Children = { fit, labels, tour, recall, moment, larger } };
        var bar = Panel(row, 10);
        bar.HorizontalAlignment = HorizontalAlignment.Right;
        bar.VerticalAlignment = VerticalAlignment.Top;
        bar.Margin = new Thickness(0, 4, 4, 0);
        return bar;
    }

    Control WindowTools()
    {
        zoomOut = Button("remove", null, "Zoom out (−)", () => x.Zoomer.ZoomBy(1 / 1.25));
        fit = Button(null, "100%", "Fit the window (0)", () => x.Zoomer.Fit());
        zoomIn = Button("add", null, "Zoom in (+)", () => x.Zoomer.ZoomBy(1.25));
        var zoomRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 1, Children = { zoomOut, fit, zoomIn } };
        labels = Button("subject", "Hide labels", "Turn the labels off, or on again (L)", ToggleLabels);
        tour = Button("play_circle", "Tour the parts", "Walk through the parts one at a time (S)", () => Toggle(DiagramMode.Steps));
        recall = Button("quiz", "Test yourself", "Hide the labels and name each part (H)", () => Toggle(DiagramMode.Recall));
        moment = Button("schedule", "", "Read the transcript where this illustration comes from", x.JumpToMoment);
        play = Button("play_arrow", null, "Play the lecture from there", x.PlayMoment);
        var row = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 2, LineSpacing = 4, VerticalAlignment = VerticalAlignment.Center };
        foreach (var c in new Control[] { zoomRow, Rule(), labels, tour, recall, moment, play }) row.Children.Add(c);
        var bar = new Border { Child = row, Padding = new Thickness(Mac ? 14 : 12, 8) };
        var rule = new Border { Height = 1, VerticalAlignment = VerticalAlignment.Bottom };
        rule.Bind(Border.BackgroundProperty, rule.GetResourceObservable("Sep"));
        return new Panel { Children = { bar, rule } };
    }

    void Toggle(DiagramMode mode)
    {
        if (x.Mode == mode) x.Explore();
        else if (mode == DiagramMode.Steps) x.StartSteps();
        else x.StartRecall();
        view.Focus();
    }

    void ToggleLabels()
    {
        x.ToggleLabels();
        view.Focus();
    }

    // --- the strip ----------------------------------------------------------------------------------------------------

    TextBlock stripTitle = null!, stripWords = null!;
    Button prev = null!, next = null!, playTour = null!, done = null!, knew = null!, notYet = null!, shuffle = null!, showAll = null!, reset = null!, missed = null!;

    Control BuildStrip()
    {
        prev = Button("chevron_left", null, "Previous part (←)", x.Previous);
        next = Button("chevron_right", null, "Next part (→ or Space)", x.Next);
        playTour = Button("play_arrow", null, "Play the tour (P)", x.TogglePlay);
        knew = Button("check", "Knew it", "I knew it (Y)", () => x.Grade(true));
        notYet = Button(null, "Not yet", "Not yet (N)", () => x.Grade(false));
        missed = Button("refresh", "Practise the missed ones", "Hide just the ones you missed, and try again", x.PractiseMissed);
        shuffle = Button(null, "Hide some", "Hide about half, chosen afresh, the rest left as clues", x.Shuffle);
        showAll = Button(null, "Show all", "Show every label", x.ShowAll);
        reset = Button(null, "Start again", "Hide them all again and clear the score", x.ResetRecall);
        done = Button("close", null, "Done (Esc)", () =>
        {
            x.Explore();
            view.Focus();
        });
        stripTitle = Text(12.5, "Fg", FontWeight.SemiBold);
        stripTitle.TextWrapping = TextWrapping.NoWrap;
        stripTitle.Margin = new Thickness(6, 0, 8, 0);
        stripWords = Text(12.5, "Fg2");
        stripWords.Margin = new Thickness(0, 0, 6, 0);
        AutomationProperties.SetLiveSetting(stripWords, AutomationLiveSetting.Polite);
        var lead = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 1, VerticalAlignment = VerticalAlignment.Center, Children = { prev, next, playTour } };
        var trail = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 1, VerticalAlignment = VerticalAlignment.Center, Children = { knew, notYet, missed, shuffle, showAll, reset } };
        var line = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto,Auto"), RowDefinitions = new RowDefinitions("Auto,Auto"), VerticalAlignment = VerticalAlignment.Center, MinHeight = 28 };
        Grid.SetColumn(stripTitle, 1);
        Grid.SetColumn(stripWords, 2);
        Grid.SetColumn(trail, 3);
        Grid.SetColumn(done, 4);
        foreach (var c in new Control[] { lead, stripTitle, stripWords, trail, done }) line.Children.Add(c);
        line.SizeChanged += (_, e) =>
        {
            trail.Measure(Size.Infinity);
            bool narrow = e.NewSize.Width - trail.DesiredSize.Width - 40 < 260;
            if (narrow == (Grid.GetRow(trail) == 1)) return;
            Grid.SetRow(trail, narrow ? 1 : 0);
            Grid.SetColumn(trail, narrow ? 0 : 3);
            Grid.SetColumnSpan(trail, narrow ? 4 : 1);
            trail.Margin = new Thickness(0, narrow ? 2 : 0, 0, 0);
        };
        var strip = new Border { Child = line, Padding = new Thickness(windowed ? (Mac ? 10 : 8) : 4, 4), CornerRadius = new CornerRadius(windowed ? 0 : Mac ? 10 : 6) };
        if (!windowed)
        {
            strip.Margin = new Thickness(0, 6, 0, 0);
            strip.Bind(Border.BackgroundProperty, strip.GetResourceObservable(Mac ? "Fill" : "Subtle"));
            return strip;
        }
        var rule = new Border { Height = 1, VerticalAlignment = VerticalAlignment.Top };
        rule.Bind(Border.BackgroundProperty, rule.GetResourceObservable("Sep"));
        return new Panel { Children = { strip, rule } };
    }

    // --- a pinned part's card -----------------------------------------------------------------------------------------

    TextBlock cardName = null!, cardNote = null!;
    Button explain = null!, quiz = null!, where = null!, zoomTo = null!;

    Control BuildActions()
    {
        cardName = Text(13, "Fg", FontWeight.SemiBold);
        cardNote = Text(12.5, "Fg2");
        cardNote.MaxWidth = 380;
        var words = new StackPanel { Spacing = 2, Margin = new Thickness(8, 6, 8, 4), Children = { cardName, cardNote } };
        explain = Button("chat", "Explain this", "Ask about this part in the Ask bar", () => Ask(x.ExplainQuestion));
        quiz = Button("quiz", "Quiz me", "Questions on this part, one at a time, in the Ask bar", () => Ask(x.QuizQuestion));
        where = Button("graphic_eq", "Where was this said?", "The transcript where the lecture says this", () =>
        {
            if (x.Pinned is { } id) x.FindSaid(id);
        });
        zoomTo = Button("add", "Zoom in", "Zoom in on this part (Z)", () =>
        {
            if (x.Pinned is { } id) x.ZoomTo(id);
        });
        var row = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 1, LineSpacing = 1, Children = { explain, quiz, where, zoomTo } };
        var bar = Panel(new StackPanel { Children = { words, row } }, 10);
        bar.HorizontalAlignment = HorizontalAlignment.Left;
        bar.VerticalAlignment = VerticalAlignment.Top;
        bar.MaxWidth = 460;
        bar.IsVisible = false;
        return bar;
    }

    void Ask(Func<string, string> question)
    {
        if (x.Pinned is { } id) x.Ask(question(id));
    }

    // --- the first-time hint ------------------------------------------------------------------------------------------

    Control BuildHint()
    {
        var icon = new Icon { Glyph = "info", Size = 14, VerticalAlignment = VerticalAlignment.Center };
        icon.Bind(Icon.ForegroundProperty, icon.GetResourceObservable("AccentText"));
        var words = Text(12, "Fg");
        words.Text = "Point at a part to light it up; click it to learn about it";
        words.TextWrapping = TextWrapping.NoWrap;
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { icon, words } };
        var pill = Panel(row, 12);
        pill.Padding = new Thickness(8, 4, 10, 4);
        pill.HorizontalAlignment = HorizontalAlignment.Left;
        pill.VerticalAlignment = VerticalAlignment.Bottom;
        pill.Margin = new Thickness(6, 0, 0, 6);
        pill.IsHitTestVisible = false;
        pill.IsVisible = false;
        return pill;
    }

    // --- keeping it all up to date ------------------------------------------------------------------------------------

    public bool ToolsWanted => windowed || view.IsPointerOver || view.IsKeyboardFocusWithin || x.Mode != DiagramMode.Explore || x.Zoomer.Zoom > 1.001 || !x.Labels;

    public void Update()
    {
        bool touring = x.Mode == DiagramMode.Steps, recalling = x.Mode == DiagramMode.Recall;
        On(tour, touring);
        On(recall, recalling);
        On(labels, !x.Labels && !recalling);
        labels.IsEnabled = !recalling;
        string labelsTip = x.Labels ? "Hide the labels (L)" : "Show the labels (L)";
        ToolTip.SetTip(labels, labelsTip);
        if (windowed) Words(labels, x.Labels ? "Hide labels" : "Show labels");
        else AutomationProperties.SetName(labels, labelsTip);
        double percent = Math.Round(view.Scale * x.Zoomer.Zoom * 100);
        Words(fit, $"{percent:0}%");
        if (!windowed) fit.IsVisible = x.Zoomer.Zoom > 1.001;
        Tools.IsVisible = ToolsWanted;
        var host = x.Host;
        moment.IsVisible = x.At is not null && host?.Transcript.Count > 0;
        if (x.At is { } at)
        {
            if (windowed) Words(moment, $"From {TimedText.Clock(at)} in the lecture");
            else AutomationProperties.SetName(moment, $"From {TimedText.Clock(at)} in the lecture");
            ToolTip.SetTip(moment, $"Read the transcript at {TimedText.Clock(at)}, where this illustration comes from");
        }
        if (windowed)
        {
            zoomOut.IsEnabled = x.Zoomer.Zoom > x.Zoomer.Min + 1e-3;
            zoomIn.IsEnabled = x.Zoomer.Zoom < x.Zoomer.Max - 1e-3;
            play.IsVisible = x.At is not null && host?.CanPlay == true;
        }

        foreach (var b in new Control[] { prev, next, playTour, knew, notYet, missed, shuffle, showAll, reset }) b.IsVisible = false;
        done.IsVisible = touring || recalling;
        string? note = x.Note;
        if (touring && x.CurrentStep is { } stepId)
        {
            prev.IsVisible = next.IsVisible = playTour.IsVisible = true;
            Glyph(playTour, x.Playing ? "pause" : "play_arrow");
            ToolTip.SetTip(playTour, x.Playing ? "Pause (P)" : "Play the tour (P)");
            stripTitle.Text = $"{x.Step + 1} of {x.Parts.Count}: {x.Name(stepId)}";
            stripWords.Text = x.Part(stepId)?.Note ?? "";
        }
        else if (recalling)
        {
            stripTitle.Text = x.AllChecked ? $"You knew {x.Knew} of {x.HiddenCount}" : x.Checked > 0 ? $"{x.Knew} of {x.HiddenCount} named" : "Test yourself";
            if (x.Asking is { } asking)
            {
                stripWords.Text = $"“{x.Name(asking)}”: did you know it?";
                knew.IsVisible = notYet.IsVisible = true;
            }
            else if (x.AllChecked)
            {
                stripWords.Text = x.Missed > 0 ? $"{x.Missed} to practise." : "Every one. Hide some, or start again.";
                missed.IsVisible = x.Missed > 0;
                shuffle.IsVisible = reset.IsVisible = true;
            }
            else
            {
                stripWords.Text = x.Some ? "Some labels are hidden. Name each part to yourself, then click it to check."
                    : "Name each part to yourself, then click it to check.";
                shuffle.IsVisible = showAll.IsVisible = true;
                reset.IsVisible = x.Checked > 0;
            }
        }
        else if (note is not null)
        {
            stripTitle.Text = "";
            stripWords.Text = note;
        }
        else if (windowed && (x.Pinned ?? x.Hover ?? x.Ring) is { } about)
        {
            stripTitle.Text = x.Name(about);
            stripWords.Text = x.Part(about)?.Note ?? "";
        }
        else
        {
            stripTitle.Text = "";
            stripWords.Text = windowed ? "Point at a part to see what it is, click it to pin it; drag or scroll to move about; pinch or ⌘/Ctrl + scroll to zoom." : "";
        }
        stripTitle.IsVisible = stripTitle.Text?.Length > 0;
        stripTitle.Margin = new Thickness(touring ? 6 : windowed ? 4 : 8, 0, 8, 0);
        stripWords.Margin = new Thickness(stripTitle.IsVisible ? 0 : windowed ? 4 : 8, 0, 6, 0);
        Strip.IsVisible = windowed || touring || recalling || note is not null;

        if (x.Pinned is { } pinned)
        {
            cardName.Text = x.Name(pinned);
            cardNote.Text = x.Part(pinned)?.Note ?? "";
            cardNote.IsVisible = cardNote.Text.Length > 0;
        }
        explain.IsVisible = quiz.IsVisible = host?.CanAsk == true;
        where.IsVisible = host?.Transcript.Count > 0;
        Actions.IsVisible = x.ActionsOpen && x.Pinned is not null && x.Mode == DiagramMode.Explore;

        Hint.IsVisible = x.ShowHint && view.IsPointerOver && x.Mode == DiagramMode.Explore && x.Pinned is null;
        if (x.Said is { } said && Announcer.Text != said) Announcer.Text = said;
    }
}
