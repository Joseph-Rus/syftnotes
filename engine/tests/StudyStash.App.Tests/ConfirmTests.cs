using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using StudyStash.App.Controls;
using StudyStash.App.ViewModels.Kit;
using StudyStash.App.Views;

namespace StudyStash.App.Tests;

/// <summary>A test's answer to "Are you sure?": says yes (or no) and remembers what was asked, so a view model's test
/// needs no window.</summary>
public sealed class FakeConfirm(bool answer = true) : IConfirm
{
    public bool Answer { get; set; } = answer;
    public List<ConfirmAsk> Asked { get; } = [];

    public Task<bool> AskAsync(ConfirmAsk ask)
    {
        Asked.Add(ask);
        return Task.FromResult(Answer);
    }
}

/// <summary>The one "Are you sure?" on a real window: Escape answers no, and a yes goes ahead exactly once.</summary>
public class ConfirmTests
{
    /// <summary>A shown window with a dialog layer round a page, as AppWindows makes them.</summary>
    static Window ShownWindow()
    {
        var window = new Window { Width = 800, Height = 600 };
        window.Content = ConfirmLayer.Wrap(window, new TextBlock { Text = "A page" });
        window.Show();
        return window;
    }

    static async Task<ConfirmDialog> DialogShowingAsync(Window window)
    {
        ConfirmDialog? dialog = null;
        await Wait.UntilAsync(() => (dialog = window.GetVisualDescendants().OfType<ConfirmDialog>().FirstOrDefault()) is { IsEffectivelyVisible: true } && dialog.FindControl<Button>("No")!.IsFocused,
            "the question to show with the safe button focused");
        return dialog!;
    }

    [AvaloniaFact]
    public async Task Escape_answers_no_and_leaves_the_action_undone()
    {
        var window = ShownWindow();
        int done = 0;
        var ask = new ConfirmAsk("Delete this lecture?", "It goes from your library.", "Delete");

        var asking = Confirm.Default.AskAsync(ask);
        var dialog = await DialogShowingAsync(window);
        Assert.Equal("Cancel", dialog.FindControl<Button>("No")!.Content); // the safe button has the keyboard
        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);

        if (await asking) done++;
        Assert.Equal(0, done);
        Assert.False(ConfirmLayer.Pick()!.Host.IsOpen);
        window.Close();
    }

    [AvaloniaFact]
    public async Task Yes_goes_ahead_once_even_when_pressed_twice()
    {
        var window = ShownWindow();
        int done = 0;
        var ask = new ConfirmAsk("Delete this lecture?", "It goes from your library.", "Delete");

        var asking = Confirm.Default.AskAsync(ask);
        var dialog = await DialogShowingAsync(window);
        var yes = dialog.FindControl<Button>("Yes")!;
        Assert.Equal("Delete", yes.Content);
        Assert.Equal("Delete", Avalonia.Automation.AutomationProperties.GetName(yes)); // a screen reader says what it does
        yes.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        yes.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

        if (await asking) done++;
        Assert.Equal(1, done);
        window.Close();
    }
}
