using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using StudyStash.App.Controls;
using StudyStash.App.Controls.Rich;

namespace StudyStash.App.Pictures;

/// <summary>Formulas next to each other, as the notes draw them inline: k-means++, C++ and the neighbours that must not
/// change (ion charges, a command inside \text). Mac and Windows look, light and dark, into STUDYSTASH_SHOTS as
/// math-text-<STUDYSTASH_NAME>-<mac|win>-<light|dark>.png; only with STUDYSTASH_FILM=1.</summary>
public class MathTextShots
{
    static readonly string[] Formulas =
    [
        @"$k\text{-means}++$", @"$\text{k-means++}$", @"$\text{O}(n\log n)$", @"$a-b$", @"$x^{2}+1$", @"$\text{well-known}+a-b$", @"$(n++)$",
        @"$Ca^{2+} + Fe^{3+}$", @"$x_{n+}$", @"$\text{C++ and Java}$", @"$\text{see \textbf{bold} here}$",
    ];

    [AvaloniaTheory]
    [InlineData(SkinKind.Mac, false)]
    [InlineData(SkinKind.Mac, true)]
    [InlineData(SkinKind.Win, false)]
    [InlineData(SkinKind.Win, true)]
    public void Formulas_side_by_side(SkinKind skin, bool dark)
    {
        if (Environment.GetEnvironmentVariable("STUDYSTASH_FILM") != "1") return;
        Skin.UseTheme(ColourThemes.Default);
        ((App)Application.Current!).UseSkin(skin);
        var variant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        var fg = dark ? Brushes.White : Brushes.Black;
        var rows = new StackPanel { Spacing = 14, Margin = new Thickness(24) };
        foreach (string f in Formulas)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 24 };
            row.Children.Add(new TextBlock { Text = f, Width = 230, Foreground = fg, FontSize = 13, VerticalAlignment = VerticalAlignment.Center, FontFamily = new FontFamily("Menlo, Consolas, monospace") });
            row.Children.Add(new MathView { Latex = f.Trim('$'), Size = 28, Foreground = fg });
            rows.Children.Add(row);
        }
        var window = new Window { Width = 660, Height = 640, RequestedThemeVariant = variant, Background = dark ? Brushes.Black : Brushes.White, Content = rows };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("nothing rendered");
        string name = Environment.GetEnvironmentVariable("STUDYSTASH_NAME") ?? "after";
        using var file = File.Create(Path.Combine(Shot.Dir, $"math-text-{name}-{(skin == SkinKind.Mac ? "mac" : "win")}-{(dark ? "dark" : "light")}.png"));
        frame.Save(file, PngBitmapEncoderOptions.Default);
        window.Close();
    }
}
