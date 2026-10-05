using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using StudyStash.App;
using StudyStash.App.Controls;
using StudyStash.App.ViewModels;
using StudyStash.App.Views;
using StudyStash.Core.Ai;

namespace StudyStash.App.Tests;

/// <summary>
/// Draws the AI settings pane and the library setup's AI step as the design shows them (13, 15): our real controls,
/// framed in a settings window or a setup step that exist only for these pictures. The settings window belongs to
/// WS2 and the setup wizard to WS6 — <see cref="SettingsFrame"/> and <see cref="SetupFrame"/> are picture
/// scaffolding, never app code, so they draw the chrome inline rather than reusing either lane's views.
/// </summary>
public class AiShots
{
    static readonly ThemeVariant[] Themes = [ThemeVariant.Light, ThemeVariant.Dark];

    static AiShots() => Environment.SetEnvironmentVariable("STUDYSTASH_STILL", "1");

    static void Res(AvaloniaObject target, AvaloniaProperty prop, Control anchor, string key) => target.Bind(prop, anchor.GetResourceObservable(key));

    static Icon Glyph(string name, double size, string? colourKey = null)
    {
        var icon = new Icon { Glyph = name, Size = size };
        if (colourKey is not null) Res(icon, Icon.ForegroundProperty, icon, colourKey);
        return icon;
    }

    // ---------------------------------------------------------------------------------------------------------
    // Mac settings window (design's Mac Settings frame): 900×780 r26, an inset glass sidebar with 7 sections.
    // ---------------------------------------------------------------------------------------------------------

    static readonly (string Icon, string Title)[] MacSections =
    [
        ("tune", "General"), ("mic", "Recording"), ("dns", "Library"), ("auto_awesome", "AI engines"),
        ("hub", "AI tool access"), ("school", "Canvas"), ("keyboard", "Shortcuts"),
    ];

    /// <summary>The Mac settings window, its sidebar selected on <paramref name="selected"/>, the pane on the right.</summary>
    public static Control SettingsFrame(SkinKind skin, string selected, Control pane) =>
        skin == SkinKind.Mac ? MacSettingsFrame(selected, pane) : WinSettingsFrame(selected, pane);

    static Border MacSettingsFrame(string selected, Control pane)
    {
        var lights = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Height = 44, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        lights.Children.Add(new Ellipse { Width = 12, Height = 12, Fill = new SolidColorBrush(Color.Parse("#FF5F57")) });
        lights.Children.Add(new Ellipse { Width = 12, Height = 12, Fill = new SolidColorBrush(Color.Parse("#FEBC2E")) });
        lights.Children.Add(new Ellipse { Width = 12, Height = 12, Fill = new SolidColorBrush(Color.Parse("#28C840")) });

        var rows = new StackPanel { Spacing = 2 };
        rows.Children.Add(lights);
        foreach (var (icon, title) in MacSections)
        {
            bool on = title == selected;
            var row = new Border { Height = 34, CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 0) };
            if (on)
            {
                Res(row, Border.BackgroundProperty, row, "Fill2");
                Res(row, Border.BoxShadowProperty, row, "EdgeSoft");
            }
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("16,10,*") };
            grid.Children.Add(Glyph(icon, 16, on ? "AccentText" : "Fg2"));
            var label = new TextBlock { Text = title, FontSize = 13, FontWeight = on ? FontWeight.SemiBold : FontWeight.Normal, VerticalAlignment = VerticalAlignment.Center };
            if (!on) Res(label, TextBlock.ForegroundProperty, label, "Fg2");
            Grid.SetColumn(label, 2);
            grid.Children.Add(label);
            row.Child = grid;
            rows.Children.Add(row);
        }

        var sidebarInner = new Border { CornerRadius = new CornerRadius(18), Padding = new Thickness(10, 0, 10, 14), Child = rows };
        Res(sidebarInner, Border.BackgroundProperty, sidebarInner, "Glass");
        Res(sidebarInner, Border.BoxShadowProperty, sidebarInner, "SideGlassShadow");
        var sidebar = new Border { Width = 220, Padding = new Thickness(8, 8, 0, 8), Child = sidebarInner };

        var grid2 = new Grid { ColumnDefinitions = new ColumnDefinitions("220,*"), Width = 900, Height = 780 };
        grid2.Children.Add(sidebar);
        Grid.SetColumn(pane, 1);
        grid2.Children.Add(pane);

        var window = new Border { Width = 900, Height = 780, CornerRadius = new CornerRadius(26), ClipToBounds = true, Child = grid2 };
        Res(window, Border.BackgroundProperty, window, "Win");
        Res(window, Border.BoxShadowProperty, window, "WindowShadow");
        return window;
    }

    static readonly (string Icon, string Title)[] WinSections = MacSections;

    static Border WinSettingsFrame(string selected, Control pane)
    {
        var titleBar = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*"), Height = 32, Margin = new Thickness(16, 0, 0, 0) };
        var badge = new AppIcon { VerticalAlignment = VerticalAlignment.Center };
        titleBar.Children.Add(badge);
        var titleText = new TextBlock { Text = "Study Stash settings", FontSize = 12, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(titleText, 1);
        titleBar.Children.Add(titleText);

        var nav = new StackPanel { Width = 240, Spacing = 4, Margin = new Thickness(4, 4, 4, 8) };
        foreach (var (icon, title) in WinSections)
        {
            bool on = title == selected;
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("16,14,*"), Height = 40, Margin = new Thickness(4, 0) };
            var cell = new Border { CornerRadius = new CornerRadius(4), Padding = new Thickness(12, 0), Child = row };
            if (on) Res(cell, Border.BackgroundProperty, cell, "Subtle");
            if (on)
            {
                var bar = new Border { Width = 3, Height = 16, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
                Res(bar, Border.BackgroundProperty, bar, "Accent");
                row.Children.Add(bar);
            }
            row.Children.Add(Glyph(icon, 16));
            var label = new TextBlock { Text = title, FontSize = 14, FontWeight = on ? FontWeight.SemiBold : FontWeight.Normal, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(label, 2);
            row.Children.Add(label);
            nav.Children.Add(cell);
        }

        var layer = new Border { BorderThickness = new Thickness(1, 1, 0, 0), CornerRadius = new CornerRadius(8, 0, 0, 0), Child = pane };
        Res(layer, Border.BackgroundProperty, layer, "Layer");
        Res(layer, Border.BorderBrushProperty, layer, "LayerStroke");

        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("240,*") };
        body.Children.Add(nav);
        Grid.SetColumn(layer, 1);
        body.Children.Add(layer);

        var stack = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(titleBar, Dock.Top);
        stack.Children.Add(titleBar);
        stack.Children.Add(body);

        var window = new Border { Width = 900, Height = 860, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), ClipToBounds = true, Child = stack };
        Res(window, Border.BackgroundProperty, window, "Mica");
        Res(window, Border.BorderBrushProperty, window, "FlyStroke");
        Res(window, Border.BoxShadowProperty, window, "ShadowLg");
        return window;
    }

    // ---------------------------------------------------------------------------------------------------------
    // The library setup wizard's AI step (design 15): "This is the library" and "Transcription model" done,
    // "AI engines" current, "AI tool access" still to come. Fixed to this one step: the wizard itself is WS6's.
    // ---------------------------------------------------------------------------------------------------------

    static Control SetupSidebarRow(string title, bool done, bool current, int number, bool optional)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("16,10,*,Auto") };
        if (done) grid.Children.Add(new Icon { Glyph = "check_circle", Filled = true, Size = 18 }.Also(i => Res(i, Icon.ForegroundProperty, i, "AccentText")));
        else
        {
            var circle = new Border { Width = 16, Height = 16, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1.5), VerticalAlignment = VerticalAlignment.Center };
            Res(circle, Border.BorderBrushProperty, circle, current ? "Fg" : "Fg3");
            var num = new TextBlock { Text = number.ToString(), FontSize = 9, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            Res(num, TextBlock.ForegroundProperty, num, current ? "Fg" : "Fg3");
            circle.Child = num;
            grid.Children.Add(circle);
        }
        var label = new TextBlock { Text = title, FontSize = 13, FontWeight = current ? FontWeight.SemiBold : FontWeight.Normal, VerticalAlignment = VerticalAlignment.Center };
        if (!current && !done) Res(label, TextBlock.ForegroundProperty, label, "Fg2");
        Grid.SetColumn(label, 2);
        grid.Children.Add(label);
        if (optional)
        {
            var opt = new TextBlock { Text = "Optional", FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
            Res(opt, TextBlock.ForegroundProperty, opt, "Fg3");
            Grid.SetColumn(opt, 3);
            grid.Children.Add(opt);
        }
        var row = new Border { Height = 34, CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 0), Child = grid };
        if (current)
        {
            Res(row, Border.BackgroundProperty, row, "Fill2");
            Res(row, Border.BoxShadowProperty, row, "EdgeSoft");
        }
        return row;
    }

    /// <summary>The library setup window, its sidebar on the AI engines step, the step's body on the right and a
    /// Back/Continue (or Back/Next) footer below it.</summary>
    public static Control SetupFrame(SkinKind skin, Control pane) => skin == SkinKind.Mac ? MacSetupFrame(pane) : WinSetupFrame(pane);

    static Border MacSetupFrame(Control pane)
    {
        // The design's setup mock shows the window as not frontmost: only the close light is coloured.
        var lights = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Height = 44, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        lights.Children.Add(new Ellipse { Width = 12, Height = 12, Fill = new SolidColorBrush(Color.Parse("#FF5F57")) });
        lights.Children.Add(new Ellipse { Width = 12, Height = 12 }.Also(e => Res(e, Ellipse.FillProperty, e, "Fill2")));
        lights.Children.Add(new Ellipse { Width = 12, Height = 12 }.Also(e => Res(e, Ellipse.FillProperty, e, "Fill2")));

        var rows = new StackPanel { Spacing = 2 };
        rows.Children.Add(lights);
        rows.Children.Add(SetupSidebarRow("This is the library", done: true, current: false, number: 1, optional: false));
        rows.Children.Add(SetupSidebarRow("Transcription model", done: true, current: false, number: 2, optional: false));
        rows.Children.Add(SetupSidebarRow("AI engines", done: false, current: true, number: 3, optional: false));
        rows.Children.Add(SetupSidebarRow("AI tool access", done: false, current: false, number: 4, optional: true));

        var sidebarInner = new Border { CornerRadius = new CornerRadius(18), Padding = new Thickness(10, 0, 10, 14), Child = rows };
        Res(sidebarInner, Border.BackgroundProperty, sidebarInner, "Glass");
        Res(sidebarInner, Border.BoxShadowProperty, sidebarInner, "SideGlassShadow");
        var sidebar = new Border { Width = 220, Padding = new Thickness(8, 8, 0, 8), Child = sidebarInner };

        var caption = new TextBlock { Text = "Library setup · step 3 of 4", FontSize = 12, Margin = new Thickness(0, 0, 0, 6) };
        Res(caption, TextBlock.ForegroundProperty, caption, "Fg2");
        var footer = FooterRow(mac: true, continueWord: "Continue");
        var body = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 44, 0, 20) };
        DockPanel.SetDock(caption, Dock.Top);
        body.Children.Add(caption);
        DockPanel.SetDock(footer, Dock.Bottom);
        body.Children.Add(footer);
        body.Children.Add(pane);

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("220,*"), Width = 900, Height = 640 };
        grid.Children.Add(sidebar);
        Grid.SetColumn(body, 1);
        grid.Children.Add(body);

        var window = new Border { Width = 900, Height = 640, CornerRadius = new CornerRadius(26), ClipToBounds = true, Child = grid };
        Res(window, Border.BackgroundProperty, window, "Win");
        Res(window, Border.BoxShadowProperty, window, "WindowShadow");
        return window;
    }

    static Control FooterRow(bool mac, string continueWord)
    {
        Border Pill(string text, bool primary)
        {
            var t = new TextBlock { Text = text, FontSize = mac ? 13 : 14, VerticalAlignment = VerticalAlignment.Center };
            var pill = new Border { Height = mac ? 30 : 32, CornerRadius = new CornerRadius(mac ? 15 : 4), Padding = new Thickness(14, 0), Child = t };
            if (mac)
            {
                Res(pill, Border.BackgroundProperty, pill, primary ? "Tint" : "Raised");
                Res(pill, Border.BoxShadowProperty, pill, primary ? "EdgeTint" : "RaisedShadow");
                if (primary) { Res(t, TextBlock.ForegroundProperty, t, "OnAccent"); t.FontWeight = FontWeight.Medium; }
            }
            else
            {
                Res(pill, Border.BackgroundProperty, pill, primary ? "Accent" : "Ctrl");
                if (!primary) { pill.BorderThickness = new Thickness(1); Res(pill, Border.BorderBrushProperty, pill, "CtrlBorder"); }
                if (primary) { Res(t, TextBlock.ForegroundProperty, t, "OnAccent"); t.FontWeight = FontWeight.SemiBold; }
            }
            return pill;
        }
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        row.Children.Add(Pill("Back", primary: false));
        row.Children.Add(Pill(continueWord, primary: true));
        if (mac) return new Border { Padding = new Thickness(0, 10, 0, 0), Child = row };
        var strip = new Border { Height = 64, Padding = new Thickness(24, 0), Child = row };
        Res(strip, Border.BackgroundProperty, strip, "Footer");
        strip.BorderThickness = new Thickness(0, 1, 0, 0);
        Res(strip, Border.BorderBrushProperty, strip, "Sep");
        return strip;
    }

    static Border WinSetupFrame(Control pane)
    {
        var titleBar = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*"), Height = 32, Margin = new Thickness(16, 0, 0, 0) };
        var badge = new AppIcon { VerticalAlignment = VerticalAlignment.Center };
        titleBar.Children.Add(badge);
        var titleText = new TextBlock { Text = "Set up your library", FontSize = 12, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(titleText, 1);
        titleBar.Children.Add(titleText);

        var nav = new StackPanel { Width = 220, Spacing = 4, Margin = new Thickness(4, 4, 4, 8) };
        nav.Children.Add(WinSetupSidebarRow("This is the library", done: true, current: false, number: 1, optional: false));
        nav.Children.Add(WinSetupSidebarRow("Transcription model", done: true, current: false, number: 2, optional: false));
        nav.Children.Add(WinSetupSidebarRow("AI engines", done: false, current: true, number: 3, optional: false));
        nav.Children.Add(WinSetupSidebarRow("AI tool access", done: false, current: false, number: 4, optional: true));

        var caption = new TextBlock { Text = "Library setup · step 3 of 4", FontSize = 12, Margin = new Thickness(32, 28, 0, 0) };
        Res(caption, TextBlock.ForegroundProperty, caption, "Fg2");
        var footer = FooterRow(mac: false, continueWord: "Next");
        var layerContent = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(caption, Dock.Top);
        layerContent.Children.Add(caption);
        DockPanel.SetDock(footer, Dock.Bottom);
        layerContent.Children.Add(footer);
        layerContent.Children.Add(pane);
        var layer = new Border { BorderThickness = new Thickness(1, 1, 0, 0), CornerRadius = new CornerRadius(8, 0, 0, 0), Child = layerContent };
        Res(layer, Border.BackgroundProperty, layer, "Layer");
        Res(layer, Border.BorderBrushProperty, layer, "LayerStroke");

        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("220,*") };
        body.Children.Add(nav);
        Grid.SetColumn(layer, 1);
        body.Children.Add(layer);

        var stack = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(titleBar, Dock.Top);
        stack.Children.Add(titleBar);
        stack.Children.Add(body);

        var window = new Border { Width = 900, Height = 680, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), ClipToBounds = true, Child = stack };
        Res(window, Border.BackgroundProperty, window, "Mica");
        Res(window, Border.BorderBrushProperty, window, "FlyStroke");
        Res(window, Border.BoxShadowProperty, window, "ShadowLg");
        return window;
    }

    static Control WinSetupSidebarRow(string title, bool done, bool current, int number, bool optional)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("18,14,*"), Height = 40, Margin = new Thickness(12, 0) };
        if (done) grid.Children.Add(new Icon { Glyph = "check_circle", Filled = true, Size = 18 }.Also(i => Res(i, Icon.ForegroundProperty, i, "AccentText")));
        else
        {
            var circle = new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(9), BorderThickness = new Thickness(1.5), VerticalAlignment = VerticalAlignment.Center };
            Res(circle, Border.BorderBrushProperty, circle, current ? "Fg" : "Fg3");
            var num = new TextBlock { Text = number.ToString(), FontSize = 10, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            Res(num, TextBlock.ForegroundProperty, num, current ? "Fg" : "Fg3");
            circle.Child = num;
            grid.Children.Add(circle);
        }
        var label = new TextBlock { Text = title, FontSize = 14, FontWeight = current ? FontWeight.SemiBold : FontWeight.Normal, VerticalAlignment = VerticalAlignment.Center };
        if (!current && !done) Res(label, TextBlock.ForegroundProperty, label, "Fg2");
        Grid.SetColumn(label, 2);
        grid.Children.Add(label);
        var row = new Border { Height = 40, CornerRadius = new CornerRadius(4), Margin = new Thickness(4, 0), Child = grid };
        if (current)
        {
            Res(row, Border.BackgroundProperty, row, "Subtle");
            var bar = new Border { Width = 3, Height = 16, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
            Res(bar, Border.BackgroundProperty, bar, "Accent");
            grid.Children.Add(bar);
        }
        return row;
    }

    // ---------------------------------------------------------------------------------------------------------
    // The ask bar under a lecture's notes (design 16): a 760×460 page of notes with the "Answer with" menu and the
    // ask bar drawn inline at the design's offsets (popups don't render into a capture), and the recorder's compact
    // chat 56 px to the right, bottom-aligned. Picture scaffolding only: the real notes page is another lane's.
    // ---------------------------------------------------------------------------------------------------------

    const string AskSample = "A recursive function solves a problem by calling itself on a smaller version of it. Each call gets its own frame on the call stack, which holds that call's arguments and local variables. The calls pause in order until a base case returns.";

    static Control AskPage(SkinKind skin, AiAskModel model)
    {
        var text = new TextBlock
        {
            Text = AskSample, FontSize = skin == SkinKind.Mac ? 16 : 15, LineHeight = skin == SkinKind.Mac ? 25.6 : 24,
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(56, 40, 56, 0), HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        if (skin == SkinKind.Mac) Res(text, TextBlock.FontFamilyProperty, text, "SerifFont");
        Res(text, TextBlock.ForegroundProperty, text, "Fg");

        var fade = new Border { Height = 200, VerticalAlignment = VerticalAlignment.Bottom, IsHitTestVisible = false };

        var menu = skin == SkinKind.Mac
            ? (Control)new MacAiEngineMenu { DataContext = model.Menu, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 190, 84) }
            : new WinAiEngineMenu { DataContext = model.Menu, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 200, 84) };

        var bar = skin == SkinKind.Mac
            ? (Control)new MacAiAskBar { DataContext = model, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 24) }
            : new WinAiAskBar { DataContext = model, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 24) };

        var panel = new Panel { Width = 760, Height = 460, Children = { text, fade, menu, bar } };
        var page = new Border { Width = 760, Height = 460, ClipToBounds = true, Child = panel };
        if (skin == SkinKind.Mac)
        {
            Res(page, Border.BackgroundProperty, page, "Win");
            page.CornerRadius = new CornerRadius(26);
            Res(page, Border.BoxShadowProperty, page, "GShadow");
            Fades.Under(fade, "Win", 0.7);
        }
        else
        {
            Res(page, Border.BackgroundProperty, page, "Mica");
            page.CornerRadius = new CornerRadius(8);
            page.BorderThickness = new Thickness(1);
            Res(page, Border.BorderBrushProperty, page, "FlyStroke");
            Res(page, Border.BoxShadowProperty, page, "ShadowLg");
            Fades.Under(fade, "Mica", 0.75);
        }
        return page;
    }

    static Control AskChatPanel(SkinKind skin, AiAskModel model) => skin == SkinKind.Mac
        ? new MacAiAskChat { DataContext = model, VerticalAlignment = VerticalAlignment.Bottom }
        : new WinAiAskChat { DataContext = model, VerticalAlignment = VerticalAlignment.Bottom };

    static Control AskComposition(SkinKind skin) =>
        new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 56, HorizontalAlignment = HorizontalAlignment.Left,
            Children = { AskPage(skin, AiDemo.Ask()), AskChatPanel(skin, AiDemo.Chat()) },
        };

    [AvaloniaFact]
    public void Mac_ai_ask_picker()
    {
        foreach (var t in Themes) Shot.Take("mac-16-ai-ask-picker", SkinKind.Mac, t, () => AskComposition(SkinKind.Mac));
    }

    [AvaloniaFact]
    public void Win_ai_ask_picker()
    {
        foreach (var t in Themes) Shot.Take("win-16-ai-ask-picker", SkinKind.Win, t, () => AskComposition(SkinKind.Win));
    }

    // ---------------------------------------------------------------------------------------------------------
    // An answer being written (design 16's answer card and compact chat, mid-answer): the words so far, the formula
    // it's in the middle of held back, and Stop where Send was. The design has no picture of this moment; it's the
    // same card and chat as the picker's, a few words at a time.
    // ---------------------------------------------------------------------------------------------------------

    const string WritingSoFar = "The base case is the call that stops the recursion: it returns without calling itself again, so each paused call "
        + "on the stack can finish in turn. In a factorial that's when $n = 0$, which returns 1, and then every call multiplies by its own $\\frac{n";

    static AiAskModel Writing(string question, string engine)
    {
        var m = AiDemo.Ask();
        m.Turns.Add(new AiTurn(question, engine) { Answer = StudyStash.Core.Rich.PartialText.Showable(WritingSoFar) });
        m.Busy = true;
        return m;
    }

    static Control WritingPage(SkinKind skin)
    {
        var page = (Border)AskPage(skin, Writing("What's a base case?", "Ollama"));
        var panel = (Panel)page.Child!;
        panel.Children.RemoveAt(2); // no "Answer with" menu open: the answer card in its place, above the bar
        var model = (AiAskModel)((Control)panel.Children[^1]).DataContext!;
        panel.Children.Insert(2, skin == SkinKind.Mac
            ? new MacAiAnswer { DataContext = model, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 84) }
            : new WinAiAnswer { DataContext = model, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 84) });
        return page;
    }

    static Control WritingComposition(SkinKind skin) => Shot.Side(WritingPage(skin), AskChatPanel(skin, Writing("And the stack?", "Ollama")));

    [AvaloniaFact]
    public void Mac_ai_answer_being_written()
    {
        foreach (var t in Themes) Shot.Take("mac-16-ai-answer-being-written", SkinKind.Mac, t, () => WritingComposition(SkinKind.Mac));
    }

    [AvaloniaFact]
    public void Win_ai_answer_being_written()
    {
        foreach (var t in Themes) Shot.Take("win-16-ai-answer-being-written", SkinKind.Win, t, () => WritingComposition(SkinKind.Win));
    }

    // ---------------------------------------------------------------------------------------------------------
    // A lecture's notes with "Rewrite notes" (design 17): each panel is the design's own little "page" — a class
    // dot and meta line, the title, then our real AiNotes control below it. Picture scaffolding only: the real
    // lecture page (title, meta, the class dot) is another lane's.
    // ---------------------------------------------------------------------------------------------------------

    static Border NotesPage(SkinKind skin, double width, double height, Control notes)
    {
        var dot = new Ellipse { Width = 8, Height = 8, Fill = new SolidColorBrush(Oklch.ToColor(0.62, 0.14, 250)), VerticalAlignment = VerticalAlignment.Center };
        var meta = new TextBlock { Text = "CS 101 · Tuesday 23 September · 1 h 12 min", FontSize = 12 };
        Res(meta, TextBlock.ForegroundProperty, meta, "Fg2");
        var metaRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { dot, meta } };

        var title = new TextBlock
        {
            Text = "Recursion and the call stack", FontSize = skin == SkinKind.Mac ? 30 : 28,
            FontWeight = skin == SkinKind.Mac ? FontWeight.Bold : FontWeight.SemiBold, Margin = new Thickness(0, 8, 0, 0),
        };
        Res(title, TextBlock.FontFamilyProperty, title, "DisplayFont");

        var body = new StackPanel { Spacing = 12, Margin = new Thickness(skin == SkinKind.Mac ? 56 : 48, skin == SkinKind.Mac ? 36 : 32, skin == SkinKind.Mac ? 56 : 48, 0), Children = { metaRow, title, notes } };
        var page = new Border { Width = width, Height = height, ClipToBounds = true, Child = body };
        if (skin == SkinKind.Mac)
        {
            Res(page, Border.BackgroundProperty, page, "Win");
            page.CornerRadius = new CornerRadius(26);
            Res(page, Border.BoxShadowProperty, page, "GShadow");
        }
        else
        {
            Res(page, Border.BackgroundProperty, page, "Mica");
            page.CornerRadius = new CornerRadius(8);
            page.BorderThickness = new Thickness(1);
            Res(page, Border.BorderBrushProperty, page, "FlyStroke");
            Res(page, Border.BoxShadowProperty, page, "ShadowLg");
        }
        return page;
    }

    static Control Notes(SkinKind skin, ViewModels.AiNotesModel m) => skin == SkinKind.Mac ? new MacAiNotes { DataContext = m } : new WinAiNotes { DataContext = m };

    /// <summary>The idle panel with "Rewrite notes with" open: the popup never renders into a capture, so it's drawn
    /// inline here at the design's own offset, over the (already dimmed) notes.</summary>
    static Control IdlePage(SkinKind skin, ViewModels.AiNotesModel model)
    {
        var page = NotesPage(skin, 760, 420, Notes(skin, model));
        Control menu = skin == SkinKind.Mac
            ? new MacAiEngineMenu { DataContext = model.Menu, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 196, 56, 0) }
            : new WinAiEngineMenu { DataContext = model.Menu, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 188, 48, 0) };
        return new Panel { Width = 760, Height = 420, Children = { page, menu } };
    }

    static Control RewriteComposition(SkinKind skin)
    {
        var right = new StackPanel { Spacing = 32, Children = { NotesPage(skin, 760, 380, Notes(skin, AiDemo.NotesRewriting())), NotesPage(skin, 760, 380, Notes(skin, AiDemo.NotesReady())) } };
        return new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 40, HorizontalAlignment = HorizontalAlignment.Left,
            Children = { IdlePage(skin, AiDemo.NotesIdle()), right },
        };
    }

    [AvaloniaFact]
    public void Mac_ai_rewrite()
    {
        foreach (var t in Themes) Shot.Take("mac-17-ai-rewrite", SkinKind.Mac, t, () => RewriteComposition(SkinKind.Mac));
    }

    [AvaloniaFact]
    public void Win_ai_rewrite()
    {
        foreach (var t in Themes) Shot.Take("win-17-ai-rewrite", SkinKind.Win, t, () => RewriteComposition(SkinKind.Win));
    }

    // ---------------------------------------------------------------------------------------------------------
    // Every AI problem (design 18): the design's own 1000-wide sheet, its six problems in two columns.
    // ---------------------------------------------------------------------------------------------------------

    static Control ProblemsSheet(SkinKind skin, IEnumerable<ViewModels.AiProblem> problems, double width = 1000)
    {
        Control Card(ViewModels.AiProblem p) => skin == SkinKind.Mac
            ? new MacAiProblem { DataContext = p } : new WinAiProblem { DataContext = p };

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,28,*"), RowSpacing = 20 };
        int row = 0, col = 0;
        foreach (var p in problems)
        {
            while (grid.RowDefinitions.Count <= row) grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var card = Card(p);
            Grid.SetRow(card, row);
            Grid.SetColumn(card, col * 2);
            grid.Children.Add(card);
            if (++col == 2) { col = 0; row++; }
        }

        var sheet = new Border { Width = width, Padding = new Thickness(32), Child = grid };
        if (skin == SkinKind.Mac)
        {
            Res(sheet, Border.BackgroundProperty, sheet, "Win");
            sheet.CornerRadius = new CornerRadius(26);
            Res(sheet, Border.BoxShadowProperty, sheet, "GShadow");
        }
        else
        {
            Res(sheet, Border.BackgroundProperty, sheet, "Mica");
            sheet.CornerRadius = new CornerRadius(8);
            sheet.BorderThickness = new Thickness(1);
            Res(sheet, Border.BorderBrushProperty, sheet, "FlyStroke");
            Res(sheet, Border.BoxShadowProperty, sheet, "ShadowLg");
        }
        return sheet;
    }

    [AvaloniaFact]
    public void Mac_ai_problems()
    {
        foreach (var t in Themes) Shot.Take("mac-18-ai-problems", SkinKind.Mac, t, () => ProblemsSheet(SkinKind.Mac, AiDemo.Problems().Problems));
    }

    [AvaloniaFact]
    public void Win_ai_problems()
    {
        foreach (var t in Themes) Shot.Take("win-18-ai-problems", SkinKind.Win, t, () => ProblemsSheet(SkinKind.Win, AiDemo.Problems().Problems));
    }

    /// <summary>The extras the design's six problems don't show: the library not answering at all, a rewrite that
    /// failed, and the compare view — each with an explicit size, since none of them is the six-card sheet above.</summary>
    static Control ProblemsMoreComposition(SkinKind skin)
    {
        var cards = new StackPanel { Spacing = 20, Width = 460, Children = { }, VerticalAlignment = VerticalAlignment.Top };
        foreach (var p in AiDemo.ProblemsMore().Problems)
            cards.Children.Add(skin == SkinKind.Mac ? new MacAiProblem { DataContext = p } : new WinAiProblem { DataContext = p });

        var comparePage = NotesPage(skin, 760, 460, Notes(skin, AiDemo.NotesComparing()));

        return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 40, HorizontalAlignment = HorizontalAlignment.Left, Children = { cards, comparePage } };
    }

    [AvaloniaFact]
    public void Mac_ai_problems_more()
    {
        foreach (var t in Themes) Shot.Take("mac-18-ai-problems-more", SkinKind.Mac, t, () => ProblemsMoreComposition(SkinKind.Mac), size: new Size(1400, 620));
    }

    [AvaloniaFact]
    public void Win_ai_problems_more()
    {
        foreach (var t in Themes) Shot.Take("win-18-ai-problems-more", SkinKind.Win, t, () => ProblemsMoreComposition(SkinKind.Win), size: new Size(1400, 620));
    }

    // ---------------------------------------------------------------------------------------------------------
    // The shots.
    // ---------------------------------------------------------------------------------------------------------

    [AvaloniaFact]
    public void Mac_ai_engines()
    {
        foreach (var t in Themes)
            Shot.Take("mac-13-ai-engines", SkinKind.Mac, t, () => SettingsFrame(SkinKind.Mac, "AI engines", new MacAiEngines { DataContext = AiDemo.Engines() }));
    }

    [AvaloniaFact]
    public void Win_ai_engines()
    {
        foreach (var t in Themes)
            Shot.Take("win-13-ai-engines", SkinKind.Win, t, () => SettingsFrame(SkinKind.Win, "AI engines", new WinAiEngines { DataContext = AiDemo.Engines() }));
    }

    [AvaloniaFact]
    public void Mac_ai_library_setup()
    {
        foreach (var t in Themes)
            Shot.Take("mac-15-ai-library-setup", SkinKind.Mac, t, () => SetupFrame(SkinKind.Mac, new MacAiSetup { DataContext = AiDemo.Setup() }));
    }

    [AvaloniaFact]
    public void Win_ai_library_setup()
    {
        foreach (var t in Themes)
            Shot.Take("win-15-ai-library-setup", SkinKind.Win, t, () => SetupFrame(SkinKind.Win, new WinAiSetup { DataContext = AiDemo.Setup() }));
    }

    // --- 14: AI tool access --------------------------------------------------------------------------------------

    static AiAccessModel AccessModel(string web = "off")
    {
        // "ready" sets WebOn through the property, which round-trips through the library like the real toggle does
        // (FakeAiLibrary's own Access must be non-null for that: its default, unscripted SetWebAsync then answers
        // with the design's address, already reachable). "off" and "problem" only ever poke plain fields, so they
        // need no library at all.
        var lib = new FakeAiLibrary { Access = new ToolAccessInfo(true, new ReadingScopes(), []) { HasPassword = true } };
        var m = new AiAccessModel(lib);
        m.On = true;
        m.ReadLectures = true;
        m.ReadNotes = true;
        m.ReadCanvas = true;
        m.ReadAudio = false;
        m.HasPassword = true;
        switch (web)
        {
            case "ready":
                m.WebOn = true;
                m.ClaudeConnections.Add(new AiConnectionRow { Id = "sig-1", Name = "Claude", Detail = "claude.ai", UsedWords = "Used 10:40", CanRemove = true, First = true });
                break;
            case "problem":
                m.WebNote = "Your tailnet doesn't allow Funnel yet. Open the page below, allow it for this computer, then turn this on again.";
                m.WebNoteFixUrl = "https://login.tailscale.com/f/funnel?node=abc123";
                break;
        }
        // The AI apps on this computer, one in each state: connected, added but not loaded yet, not connected, not here.
        var now = new DateTime(2026, 10, 4, 14, 0, 0);
        foreach (var (id, name, installed, added, addedAt, started) in new (string, string, bool, bool, DateTime?, DateTime?)[]
        {
            ("claude-desktop", "Claude Desktop", true, true, now.AddHours(-2), now.AddMinutes(-20)),
            ("claude-code", "Claude Code", true, true, now.AddMinutes(-1), null),
            ("codex", "Codex", true, false, null, null),
            ("gemini", "Gemini CLI", false, false, null, null),
        })
        {
            var row = new AiAppRow { Id = id, Name = name, First = m.Apps.Count == 0 };
            row.Show(new Services.AiAppState(id, name, installed, added, false, addedAt, started, ""), now);
            m.Apps.Add(row);
        }
        if (web == "off")
        {
            m.WebNeeds = "Tailscale isn't on the library's computer. Install it from tailscale.com/download and sign in.";
            m.WebNeedsUrl = StudyStash.Core.ClaudeReach.DownloadPage;
        }
        m.Connected.Add(new AiConnectionRow { Id = "1", Name = "Claude Code", Detail = "Signed in from the web", UsedWords = "Used 10:40", CanRemove = true, First = true });
        m.Connected.Add(new AiConnectionRow { Id = "2", Name = "Codex", Detail = "Token", UsedWords = "Used Tue", CanRemove = true });
        return m;
    }

    [AvaloniaFact]
    public void Mac_ai_tool_access()
    {
        foreach (var t in Themes)
            Shot.Take("mac-14-ai-tool-access", SkinKind.Mac, t, () => SettingsFrame(SkinKind.Mac, "AI tool access", new MacAiAccess { DataContext = AccessModel() }));
    }

    [AvaloniaFact]
    public void Win_ai_tool_access()
    {
        foreach (var t in Themes)
            Shot.Take("win-14-ai-tool-access", SkinKind.Win, t, () => SettingsFrame(SkinKind.Win, "AI tool access", new WinAiAccess { DataContext = AccessModel() }));
    }

    // --- 14: the "Claude (desktop and web)" card (connectors task 5): off, on and answering, and a Funnel problem.

    [AvaloniaFact]
    public void Mac_ai_tool_access_claude_off()
    {
        foreach (var t in Themes)
            Shot.Take("mac-14-ai-tool-access-claude-off", SkinKind.Mac, t, () => SettingsFrame(SkinKind.Mac, "AI tool access", new MacAiAccess { DataContext = AccessModel("off") }));
    }

    [AvaloniaFact]
    public void Mac_ai_tool_access_claude_ready()
    {
        foreach (var t in Themes)
            Shot.Take("mac-14-ai-tool-access-claude-ready", SkinKind.Mac, t, () => SettingsFrame(SkinKind.Mac, "AI tool access", new MacAiAccess { DataContext = AccessModel("ready") }));
    }

    [AvaloniaFact]
    public void Mac_ai_tool_access_claude_problem()
    {
        foreach (var t in Themes)
            Shot.Take("mac-14-ai-tool-access-claude-problem", SkinKind.Mac, t, () => SettingsFrame(SkinKind.Mac, "AI tool access", new MacAiAccess { DataContext = AccessModel("problem") }));
    }

    [AvaloniaFact]
    public void Win_ai_tool_access_claude_off()
    {
        foreach (var t in Themes)
            Shot.Take("win-14-ai-tool-access-claude-off", SkinKind.Win, t, () => SettingsFrame(SkinKind.Win, "AI tool access", new WinAiAccess { DataContext = AccessModel("off") }));
    }

    [AvaloniaFact]
    public void Win_ai_tool_access_claude_ready()
    {
        foreach (var t in Themes)
            Shot.Take("win-14-ai-tool-access-claude-ready", SkinKind.Win, t, () => SettingsFrame(SkinKind.Win, "AI tool access", new WinAiAccess { DataContext = AccessModel("ready") }));
    }

    [AvaloniaFact]
    public void Win_ai_tool_access_claude_problem()
    {
        foreach (var t in Themes)
            Shot.Take("win-14-ai-tool-access-claude-problem", SkinKind.Win, t, () => SettingsFrame(SkinKind.Win, "AI tool access", new WinAiAccess { DataContext = AccessModel("problem") }));
    }
}

static class AiShotsExtensions
{
    /// <summary>A small fluent helper so a control literal can be tweaked (bind a resource, set a flag) inline.</summary>
    public static T Also<T>(this T self, Action<T> then)
    {
        then(self);
        return self;
    }
}
