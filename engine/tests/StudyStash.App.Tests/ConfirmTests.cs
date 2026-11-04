using Avalonia.Threading;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using StudyStash.App.Controls;
using StudyStash.App.Services.Kit;
using StudyStash.App.ViewModels.Kit;
using StudyStash.App.Views;
using StudyStash.App.Windows;

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
    public async Task A_question_over_a_window_behind_another_brings_it_to_the_front()
    {
        var behind = ShownWindow();
        var front = ShownWindow();
        front.Activate();
        Assert.False(behind.IsActive);

        var asking = ConfirmLayer.For(behind)!.AskAsync(new ConfirmAsk("Delete this lecture?", "", "Delete"));
        var dialog = await DialogShowingAsync(behind);
        Dispatcher.UIThread.RunJobs(); // the activation the question asked for is posted; let it land before looking

        Assert.True(behind.IsActive); // where the student can see the question
        behind.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Assert.False(await asking);
        Assert.NotNull(dialog);
        front.Close();
        behind.Close();
    }

    /// <summary>Quit while recording asks over the library window even with Settings in front: the library is raised
    /// as the menu's Open does (AppWindows.Raise), the question waits until the shared Confirm would pick it (both can
    /// count as active for a moment), and is asked there.</summary>
    [AvaloniaFact]
    public async Task The_quit_question_sits_on_the_library_window_with_another_in_front()
    {
        var library = ShownWindow();
        var settings = ShownWindow();
        settings.Activate();
        Dispatcher.UIThread.RunJobs();
        Assert.True(settings.IsActive);

        AppWindows.Raise(library);
        Assert.True(await Poll.UntilAsync(() => ConfirmLayer.Pick() == ConfirmLayer.For(library), TimeSpan.FromMilliseconds(20), TimeSpan.FromSeconds(5)));
        var asking = Confirm.Default.AskAsync(QuitWhileRecording.Question);
        await DialogShowingAsync(library);

        Assert.True(ConfirmLayer.For(library)!.Host.IsOpen);
        Assert.False(ConfirmLayer.For(settings)!.Host.IsOpen);
        library.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Assert.False(await asking);
        settings.Close();
        library.Close();
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
