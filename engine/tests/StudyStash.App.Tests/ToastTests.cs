using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StudyStash.App.ViewModels;
using StudyStash.App.Views;

namespace StudyStash.App.Tests;

/// <summary>Notifications: plain words (no error codes), and one calm panel in the system's style for every toast.</summary>
public class ToastTests
{
    [Theory]
    [InlineData("The microphone didn't start (error -66680).", "The microphone didn't start.")]
    [InlineData("The microphone didn't open (error -50). Check it's allowed in System Settings.", "The microphone didn't open. Check it's allowed in System Settings.")]
    [InlineData("The microphone didn't open (error -12345). Check it's allowed.", "The microphone didn't open. Check it's allowed.")]
    [InlineData("Access denied (HRESULT 0x80070005)", "Access denied")]
    [InlineData("Couldn't write the file: error 28.", "Couldn't write the file.")]
    [InlineData("It took (12) tries", "It took (12) tries")]
    [InlineData("Couldn't reach it, OSStatus -10863.", "Couldn't reach it.")]
    [InlineData("It failed (0x8000FFFF)", "It failed")]
    [InlineData("Filed in CS 101", "Filed in CS 101")]
    [InlineData("Due Tue 11:59 PM · 18/20", "Due Tue 11:59 PM · 18/20")]
    [InlineData("", "")]
    public void Toasts_say_plain_words(string text, string shown) => Assert.Equal(shown, ToastWords.Plain(text));

    [Fact]
    public void A_toast_with_a_code_is_worth_logging()
    {
        Assert.True(ToastWords.HadCodes("The microphone didn't start (error -66680)."));
        Assert.False(ToastWords.HadCodes("Recording saved"));
        Assert.False(ToastWords.HadCodes(null));
    }

    static Window Host(Control content, SkinKind skin)
    {
        ((App)Application.Current!).UseSkin(skin);
        var w = new Window { Width = 500, Height = 400, RequestedThemeVariant = ThemeVariant.Light, Content = content };
        w.Show();
        Dispatcher.UIThread.RunJobs();
        return w;
    }

    [AvaloniaTheory]
    [InlineData(SkinKind.Mac, 16)]
    [InlineData(SkinKind.Win, 8)]
    public void A_toast_is_one_plain_panel_with_the_app_icon(SkinKind skin, double radius)
    {
        ((App)Application.Current!).UseSkin(skin);
        var view = new ToastView { Title = "Recording saved", Text = "Study Stash is writing it down." };
        var w = Host(view, skin);
        try
        {
            Assert.Equal(356, view.Bounds.Width);
            var card = view.FindControl<Border>("Card")!;
            Assert.Equal(new CornerRadius(radius), card.CornerRadius);
            var bg = Assert.IsAssignableFrom<ISolidColorBrush>(card.Background).Color;
            Assert.Equal(255, bg.A);
            Assert.True(bg.R == bg.G && bg.G == bg.B, $"the panel is {bg}, not a grey");
            foreach (var s in card.BoxShadow)
                Assert.True(s.Color.R == s.Color.G && s.Color.G == s.Color.B, $"shadow {s.Color} is coloured");
            // The app's own icon, not an accent square.
            var icons = view.GetVisualDescendants().OfType<Image>().Where(i => i.IsEffectivelyVisible).ToList();
            Assert.Single(icons);
            Assert.NotNull(icons[0].Source);
            Assert.True(view.TryFindResource("Accent", view.ActualThemeVariant, out var accent));
            Assert.DoesNotContain(view.GetVisualDescendants().OfType<Border>(), b => b.Background is ISolidColorBrush { Color: var c }
                && c == ((ISolidColorBrush)accent!).Color);
            // No buttons unless there's something to do.
            Assert.False(view.FindControl<Grid>("Actions")!.IsVisible);
            var body = view.FindControl<TextBlock>("BodyText")!;
            Assert.Equal(2, body.MaxLines);
            // One × only: Windows' in the header, the Mac's (under the pointer) in the title row.
            Assert.False(view.FindControl<Button>(skin == SkinKind.Mac ? "WinClose" : "MacClose")!.IsEffectivelyVisible);
            Assert.Equal(TextTrimming.CharacterEllipsis, view.FindControl<TextBlock>("TitleText")!.TextTrimming);
        }
        finally
        {
            w.Close();
        }
    }

    [AvaloniaFact]
    public void The_button_and_the_cross_say_what_was_pressed()
    {
        var view = new ToastView { Title = "The model isn't downloaded yet", Text = "Download it in Settings.", ActionLabel = "Settings" };
        var w = Host(view, SkinKind.Mac);
        try
        {
            int acted = 0, dismissed = 0;
            view.Acted += () => acted++;
            view.Dismissed += () => dismissed++;
            Assert.True(view.FindControl<Grid>("Actions")!.IsVisible);
            Assert.False(view.FindControl<Button>("SecondButton")!.IsVisible);
            view.FindControl<Button>("ActionButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            view.FindControl<Button>("MacClose")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(1, acted);
            Assert.Equal(1, dismissed);
            // A Mac banner's × waits for the pointer.
            Assert.False(view.FindControl<Button>("MacClose")!.IsVisible);
        }
        finally
        {
            w.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(SkinKind.Mac, "Later")]
    [InlineData(SkinKind.Win, "Dismiss")]
    public void A_Canvas_notification_is_the_same_panel(SkinKind skin, string later)
    {
        ((App)Application.Current!).UseSkin(skin);
        int opened = 0, dismissed = 0;
        var model = new CanvasToastModel(CanvasShots.ToastGallery()[0].Item)
        {
            When = "now", Expanded = true,
            OnOpen = _ => { opened++; return Task.CompletedTask; },
            OnDismiss = _ => { dismissed++; return Task.CompletedTask; },
        };
        var view = ToastView.For(model);
        var w = Host(view, skin);
        try
        {
            Assert.Equal("New assignment", view.Title);
            Assert.Equal("now", view.When);
            Assert.Equal(later, view.SecondLabel);
            Assert.True(view.FindControl<Grid>("Actions")!.IsVisible);
            view.FindControl<Button>("ActionButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            view.FindControl<Button>("SecondButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            view.FindControl<Button>(skin == SkinKind.Mac ? "MacClose" : "WinClose")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, opened);
            Assert.Equal(2, dismissed);

            model.Expanded = false;
            model.When = "2 min ago";
            Dispatcher.UIThread.RunJobs();
            Assert.False(view.FindControl<Grid>("Actions")!.IsVisible);
            Assert.Equal("2 min ago", view.When);
        }
        finally
        {
            w.Close();
        }
    }

    /// <summary>Menus (the class picker, the library's ••• and Move) are the same plain panel: the look's popup colour
    /// and radius, grey items, nothing tinted.</summary>
    [AvaloniaTheory]
    [InlineData(SkinKind.Mac, 10)]
    [InlineData(SkinKind.Win, 8)]
    public void A_menu_is_a_plain_panel(SkinKind skin, double radius)
    {
        ((App)Application.Current!).UseSkin(skin);
        var menu = SurfaceShots.ClassMenu();
        var w = Host(menu, skin);
        try
        {
            Assert.True(menu.TryFindResource("PopupBg", menu.ActualThemeVariant, out var bg));
            Assert.Equal(((ISolidColorBrush)bg!).Color, ((ISolidColorBrush)menu.Background!).Color);
            Assert.Equal(new CornerRadius(radius), menu.CornerRadius);
            var c = ((ISolidColorBrush)menu.Background!).Color;
            Assert.True(c.A == 255 && c.R == c.G && c.G == c.B, $"the menu is {c}");
        }
        finally
        {
            w.Close();
        }
    }
}
