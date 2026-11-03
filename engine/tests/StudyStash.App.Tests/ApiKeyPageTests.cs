using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StudyStash.App.ViewModels;
using StudyStash.App.Views;

namespace StudyStash.App.Tests;

/// <summary>Settings → AI engines' API keys: what the page says of the last thing done on it shows on it.</summary>
public class ApiKeyPageTests
{
    /// <summary>What the page says of the last thing done on it ("The key was saved, but Claude said: …") shows on the
    /// page, in both looks. Neither view showed it, so a key its maker refused looked saved and fine.</summary>
    [AvaloniaFact]
    public void What_the_page_says_shows_on_it_in_both_looks()
    {
        try
        {
            foreach (var skin in new[] { SkinKind.Mac, SkinKind.Win })
            {
                ((App)Application.Current!).UseSkin(skin);
                var m = Model();
                Control view = skin == SkinKind.Mac ? new MacAiEngines { DataContext = m } : new WinAiEngines { DataContext = m };
                var w = new Window { Width = 700, Height = 1800, RequestedThemeVariant = ThemeVariant.Light, Content = view };
                w.Show();
                Dispatcher.UIThread.RunJobs();
                Assert.DoesNotContain(view.GetVisualDescendants().OfType<TextBlock>(), t => t.IsEffectivelyVisible && (t.Text ?? "").StartsWith("The key was saved", StringComparison.Ordinal));
                m.Say = "The key was saved, but Claude said: invalid x-api-key";
                Dispatcher.UIThread.RunJobs();
                Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(), t => t.IsEffectivelyVisible && t.Text == m.Say);
                w.Close();
            }
        }
        finally
        {
            ((App)Application.Current!).UseSkin(SkinKind.Mac);
        }
    }

    static AiEnginesModel Model()
    {
        var m = AiDemo.Engines();
        m.Keys[2].Hint = "…a1b2";
        return m;
    }
}
