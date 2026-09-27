using StudyStash.App.Platform;

namespace StudyStash.App.Tests;

/// <summary>Full screen on a Mac: the strip the lights come down on loses only its grey and its shadow, never the lights
/// or the toolbar.</summary>
public class MacTitleBarTests
{
    [Theory]
    [InlineData("NSTitlebarBackgroundView", true)]
    [InlineData("NSKVONotifying__NSTitlebarDecorationView", true)]
    [InlineData("_NSTitlebarDecorationView", true)]
    [InlineData("_NSThemeCloseWidget", false)]
    [InlineData("_NSThemeZoomWidget", false)]
    [InlineData("NSToolbarView", false)]
    [InlineData("NSTitlebarView", false)]
    [InlineData("NSVisualEffectView", false)]
    [InlineData("NSView", false)]
    public void Only_the_strips_grey_is_hidden(string className, bool hidden) => Assert.Equal(hidden, MacTitleBar.PaintsTheStrip(className));

    [Theory]
    [InlineData("CALayer", 52, 52, true)]
    [InlineData("CALayer", 60, 52, true)]
    [InlineData("CALayer", 0, 52, false)]
    [InlineData("CUIWindowFrameLayer", 52, 52, false)]
    [InlineData("NSViewBackingLayer", 52, 52, false)]
    [InlineData("CALayer", 52, 0, false)]
    public void Only_the_shadow_under_the_header_is_hidden(string className, double top, double barHeight, bool hidden) =>
        Assert.Equal(hidden, MacTitleBar.IsShadowUnderBar(className, top, barHeight));
}
