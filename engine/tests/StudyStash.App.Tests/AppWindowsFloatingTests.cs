using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using StudyStash.App.Windows;

namespace StudyStash.App.Tests;

/// <summary>The menu-bar panel, the recorder and its card, and the quick search are made from a spec: each keeps the
/// flags it had when they were built by hand (the recorder must stay over full-screen slides, its card must never take
/// the keyboard).</summary>
public class AppWindowsFloatingTests
{
    [AvaloniaFact]
    public void A_floating_spec_keeps_its_flags_and_a_plain_one_has_none()
    {
        var card = AppWindows.MakeFloating(new WindowSpec("c", "Syft Notes recorder card", new Border()) { OverFullScreen = true, ShowActivated = false, ClickThrough = true });
        Assert.True(card.OverFullScreen);
        Assert.True(card.ClickThrough);
        Assert.False(card.ShowActivated);
        Assert.False(card.CloseOnDeactivate);
        Assert.Equal("Syft Notes recorder card", card.Title);

        var panel = AppWindows.MakeFloating(new WindowSpec("p", "Syft Notes", new Border()) { CloseOnDeactivate = true, KeepClear = true });
        Assert.True(panel.CloseOnDeactivate);
        Assert.True(panel.KeepClear);
        Assert.True(panel.ShowActivated);
        Assert.False(panel.OverFullScreen);
        Assert.False(AppWindows.AnyInDock);
    }
}
