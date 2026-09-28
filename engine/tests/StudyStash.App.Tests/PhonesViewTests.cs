using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StudyStash.App.ViewModels;
using StudyStash.App.Views;

namespace StudyStash.App.Tests;

/// <summary>Settings → Phone drawn in both looks and both themes: the QR code black on white and the full size of its
/// card, the code, and a Remove on each phone.</summary>
public class PhonesViewTests
{
    static LibrarySettingsModel.Call Library(bool paired) => (method, path, _) => Task.FromResult<JsonObject?>((method.Method, path) switch
    {
        ("GET", "") => new JsonObject
        {
            ["devices"] = paired
                ? new JsonArray(new JsonObject { ["id"] = "a", ["name"] = "Sam's iPhone", ["added"] = "2026-09-20T10:00:00Z", ["lastSeen"] = "2026-09-27T10:00:00Z" })
                : new JsonArray(),
        },
        ("POST", "/code") => new JsonObject { ["code"] = "042917", ["url"] = "https://mini.tail1234.ts.net:8443/app/", ["expires"] = DateTimeOffset.UtcNow.AddMinutes(10).ToString("o") },
        _ => null,
    });

    public static IEnumerable<object[]> Looks() =>
        from skin in new[] { SkinKind.Mac, SkinKind.Win }
        from dark in new[] { false, true }
        select new object[] { skin, dark };

    [AvaloniaTheory]
    [MemberData(nameof(Looks))]
    public async Task The_code_and_its_qr_code_show_black_on_white_in_either_look(SkinKind skin, bool dark)
    {
        ((App)Application.Current!).UseSkin(skin);
        var model = new PhonesModel(() => Library(paired: true));
        await model.Load();
        await model.AddPhoneCommand.ExecuteAsync(null);
        Control view = skin == SkinKind.Mac ? new MacPhones { DataContext = model } : new WinPhones { DataContext = model };
        var w = new Window { Width = 760, Height = 900, RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light, Content = view };
        w.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var card = view.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "QrCard");
            Assert.Equal(Colors.White, ((ISolidColorBrush)card.Background!).Color);
            var qr = view.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>().Single(p => p.Data == model.Qr);
            Assert.Equal(Colors.Black, ((ISolidColorBrush)qr.Fill!).Color);
            Assert.True(qr.IsEffectivelyVisible);
            // The squares fill the card (the margin is in the drawing): scaled up, not a thumbnail in a corner.
            var far = qr.TranslatePoint(new Point(model.QrSize, model.QrSize), card)!.Value;
            Assert.Equal(card.Bounds.Width - card.BorderThickness.Left - card.BorderThickness.Right, far.X, 1.5);
            Assert.Equal("042 917", view.GetVisualDescendants().OfType<SelectableTextBlock>().Single(t => t.Name == "PhoneCode").Text);
            Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Sam's iPhone");
            Assert.Contains(view.GetVisualDescendants().OfType<Button>(), b => b.Content is TextBlock { Text: "Remove" } && b.IsEffectivelyVisible);
        }
        finally
        {
            w.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(SkinKind.Mac)]
    [InlineData(SkinKind.Win)]
    public async Task With_no_phone_yet_it_offers_to_add_one(SkinKind skin)
    {
        ((App)Application.Current!).UseSkin(skin);
        var model = new PhonesModel(() => Library(paired: false));
        await model.Load();
        Control view = skin == SkinKind.Mac ? new MacPhones { DataContext = model } : new WinPhones { DataContext = model };
        var w = new Window { Width = 760, Height = 700, Content = view };
        w.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var add = view.GetVisualDescendants().OfType<Button>().Single(b => b.Content is TextBlock { Text: "Add a phone" });
            Assert.True(add.IsEffectivelyVisible);
            Assert.Same(model.AddPhoneCommand, add.Command);
            Assert.DoesNotContain(view.GetVisualDescendants().OfType<Button>(), b => b.Content is TextBlock { Text: "Remove" } && b.IsEffectivelyVisible);
        }
        finally
        {
            w.Close();
        }
    }
}
