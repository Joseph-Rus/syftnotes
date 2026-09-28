using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;

namespace StudyStash.App.Controls.Rich;

/// <summary>
/// A diagram the app couldn't draw, said calmly in the notes' code box: an info icon beside "Couldn't draw this
/// diagram." and one line saying why (the reader's own words, like "Study Stash draws flowcharts; this is a sequence
/// diagram."), then the diagram's source, so nothing the notes say is lost.
/// </summary>
public sealed class DiagramCard : Border
{
    public const string Heading = "Couldn't draw this diagram.";

    /// <summary>The words' line heights, so the icon can sit on the heading's centre line.</summary>
    static double HeadingSize => Skin.Current == SkinKind.Mac ? 13 : 14;
    static double HeadingLine => Skin.Current == SkinKind.Mac ? 18 : 20;
    const double IconSize = 16;

    public DiagramCard(string reason, string source)
    {
        Reason = reason;
        Source = source.TrimEnd();
        bool mac = Skin.Current == SkinKind.Mac;
        Padding = new Thickness(12, 10);
        CornerRadius = new CornerRadius(mac ? 8 : 4);
        this.Bind(BackgroundProperty, this.GetResourceObservable("Fill2"));

        var icon = new Icon { Glyph = "info", Size = IconSize, Margin = new Thickness(0, (HeadingLine - IconSize) / 2, 0, 0), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top };
        icon.Bind(Icon.ForegroundProperty, icon.GetResourceObservable("Fg2"));

        var heading = Words(Heading, HeadingSize, HeadingLine, "Fg");
        heading.FontWeight = FontWeight.SemiBold;
        var why = Words(reason, 12, 17, "Fg2");
        var code = new TextBlock
        {
            Text = Source, FontFamily = new FontFamily("SF Mono, Menlo, Cascadia Mono, Consolas, monospace"), FontSize = 12, LineHeight = 18,
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0),
        };
        code.Bind(TextBlock.ForegroundProperty, code.GetResourceObservable("Fg2"));

        // One grid, the design's: icon | heading, with the reason and the source under the heading, on its left edge.
        var words = new StackPanel { Spacing = 1, Children = { heading, why, code } };
        Grid.SetColumn(words, 2);
        Child = new Grid { ColumnDefinitions = new ColumnDefinitions($"{IconSize},10,*"), Children = { icon, words } };
        AutomationProperties.SetName(this, $"{Heading} {reason}");
    }

    /// <summary>Why the diagram couldn't be drawn.</summary>
    public string Reason { get; }

    /// <summary>The diagram as it was written.</summary>
    public string Source { get; }

    static TextBlock Words(string text, double size, double lineHeight, string colour)
    {
        var t = new TextBlock { Text = text, FontSize = size, LineHeight = lineHeight, TextWrapping = TextWrapping.Wrap };
        t.Bind(TextBlock.FontFamilyProperty, t.GetResourceObservable("TextFont"));
        t.Bind(TextBlock.ForegroundProperty, t.GetResourceObservable(colour));
        return t;
    }
}
