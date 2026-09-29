using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StudyStash.App.Services;
using StudyStash.App.Views;

namespace StudyStash.App.Tests;

/// <summary>"Buy us more Claude usage": Settings → About's line and button, opening the team's Ko-fi page (caught
/// here, never handed to a real browser).</summary>
public class SupportTests
{
    const string KoFi = "https://ko-fi.com/studystashteam";

    static List<string> Texts(Control view) =>
        [.. view.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text ?? "")];

    [AvaloniaFact]
    public void About_says_Study_Stash_is_free_and_its_button_opens_the_Ko_fi_page()
    {
        using var home = new TempHome();
        using var host = new AppHost(home.Path, log: _ => { });
        using var model = SettingsModel.Make(host);
        var opened = new List<string>();
        model.OpenUrl = opened.Add;
        model.Section = "General";
        var view = new SettingsView { DataContext = model };
        var window = new Window { Width = 900, Height = 900, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var texts = Texts(view);
        Assert.Contains("Study Stash is free and open source, built with Claude.", texts);
        Assert.Contains("Buy us more Claude usage", texts);
        var button = view.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "BuyClaudeUsage");
        Assert.True(button.IsEffectivelyVisible);
        button.Command!.Execute(null);
        Assert.Equal([KoFi], opened);
        window.Close();
    }
}
