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

    /// <summary>Settings → Phone's link on just this computer: a real library running the phone's link over a folder
    /// in memory (<paramref name="account"/> connected or not, <paramref name="phones"/> boxes already there), and the
    /// app's side of it, which asks that library; and whether this copy has the Google secret.</summary>
    internal sealed class GoogleRig : IAsyncDisposable
    {
        public required LibraryRig Rig { get; init; }
        public required Services.PhoneDrive Drive { get; init; }

        public async ValueTask DisposeAsync()
        {
            Drive.Dispose();
            await Rig.DisposeAsync();
        }
    }

    internal static async Task<GoogleRig> GoogleDrive(bool account, int phones = 0, bool secret = true)
    {
        var folder = new FakeDrive();
        var now = DateTimeOffset.UtcNow;
        string[] names = ["Joey’s iPhone", "Joey’s iPad"];
        for (int i = 0; i < phones; i++)
            folder.Create($"phone-p{i}.json", System.Text.Encoding.UTF8.GetBytes(new JsonObject
            {
                ["v"] = 1, ["phone"] = new JsonObject { ["id"] = $"p{i}", ["name"] = names[i % 2], ["model"] = i % 2 == 0 ? "iPhone" : "iPad" },
                ["open_until"] = (i == 0 ? now.AddSeconds(80) : now.AddHours(-3)).ToString("o"), ["requests"] = new JsonArray(),
            }.ToJsonString()));
        var rig = await LibraryRig.StartAsync(phone: r =>
        {
            if (account) new Core.PhoneDrive.GoogleAccount("sam@example.com", "refresh", DateTimeOffset.UtcNow).Save(r.Home);
            return new Core.PhoneDrive.PhoneLink(r.Home, () => (r.Url, LibraryRig.Password), () => true, secret: () => "x", drive: _ => folder, space: "")
            {
                Times = new Core.PhoneDrive.DriveTimes { OpenBox = TimeSpan.FromMilliseconds(50), ClosedBox = TimeSpan.FromMilliseconds(50), Tick = TimeSpan.FromMilliseconds(20) },
            };
        });
        var drive = new Services.PhoneDrive(rig.Home, () => (rig.Url, LibraryRig.Password), secret: () => secret ? "x" : null, space: "");
        var until = DateTime.UtcNow.AddSeconds(10);
        await drive.RefreshAsync();
        while (drive.Phones.Count < phones && DateTime.UtcNow < until)
        {
            await Task.Delay(20);
            await drive.RefreshAsync();
        }
        Assert.Equal(phones, drive.Phones.Count);
        return new GoogleRig { Rig = rig, Drive = drive };
    }

    [AvaloniaTheory]
    [MemberData(nameof(Looks))]
    public async Task On_just_this_computer_the_phone_connects_through_google(SkinKind skin, bool dark)
    {
        ((App)Application.Current!).UseSkin(skin);
        await using var google = await GoogleDrive(account: true, phones: 2);
        var drive = google.Drive;
        bool whenClosed = true;
        var switched = new List<bool>();
        var background = new PhoneBackgroundSwitch(() => whenClosed, on =>
        {
            switched.Add(whenClosed = on);
            return Task.CompletedTask;
        }, () => null);
        var model = new PhonesModel(() => Library(paired: true)) { Google = new PhoneGoogleModel(drive, background: background) };
        await model.Load();
        Control view = skin == SkinKind.Mac ? new MacPhones { DataContext = model } : new WinPhones { DataContext = model };
        var w = new Window { Width = 760, Height = 1100, RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light, Content = view };
        w.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            bool Shows(string text) => view.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == text && t.IsEffectivelyVisible);
            Button? ButtonSaying(string text) => view.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.Content is TextBlock t && t.Text == text && b.IsEffectivelyVisible);

            Assert.True(Shows("Connected as sam@example.com"));
            var card = view.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "GoogleQrCard");
            Assert.Equal(Colors.White, ((ISolidColorBrush)card.Background!).Color);
            var qr = view.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>().Single(p => p.Data == model.Google!.Qr);
            Assert.Equal(Colors.Black, ((ISolidColorBrush)qr.Fill!).Color);
            Assert.True(qr.IsEffectivelyVisible);
            Assert.Equal("studystash://google?account=sam%40example.com", model.Google!.QrLink);
            Assert.NotNull(ButtonSaying("Disconnect…"));
            // The phones that use the folder, each with Remove; and the one added over Tailscale before, below.
            Assert.True(Shows("Joey’s iPhone") && Shows("Open now"));
            Assert.True(Shows("Joey’s iPad") && Shows("Last open 3 hours ago"));
            Assert.True(Shows("Sam's iPhone") && Shows("Phones added over Tailscale"));
            Assert.Equal(3, view.GetVisualDescendants().OfType<Button>().Count(b => b.Content is TextBlock { Text: "Remove" } && b.IsEffectivelyVisible));
            Assert.Null(ButtonSaying("Add a phone"));

            // Answering with Syft Notes closed: on, in plain words, and the switch turns it off.
            Assert.True(Shows("Answer your phone when Syft Notes is closed"));
            var toggle = view.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.ToggleButton>().Single(t => t.Name == "AnswerWhenClosed");
            Assert.True(toggle.IsEffectivelyVisible);
            Assert.True(toggle.IsChecked);
            toggle.IsChecked = false;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal([false], switched);

            // Disconnect asks first, in words.
            ButtonSaying("Disconnect…")!.Command!.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.True(Shows(model.Google.ConfirmWords));
            Assert.NotNull(ButtonSaying("Disconnect"));
        }
        finally
        {
            w.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(SkinKind.Mac, true)]
    [InlineData(SkinKind.Win, true)]
    [InlineData(SkinKind.Mac, false)]
    [InlineData(SkinKind.Win, false)]
    public async Task Before_connecting_it_offers_google_or_says_this_copy_cant(SkinKind skin, bool secret)
    {
        ((App)Application.Current!).UseSkin(skin);
        await using var google = await GoogleDrive(account: false, secret: secret);
        var drive = google.Drive;
        var model = new PhonesModel(() => Library(paired: false)) { Google = new PhoneGoogleModel(drive) };
        await model.Load();
        Control view = skin == SkinKind.Mac ? new MacPhones { DataContext = model } : new WinPhones { DataContext = model };
        var w = new Window { Width = 760, Height = 700, Content = view };
        w.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var connect = view.GetVisualDescendants().OfType<Button>().Single(b => b.Content is TextBlock { Text: "Connect with Google" });
            Assert.True(connect.IsEffectivelyVisible);
            Assert.Equal(secret, connect.IsEffectivelyEnabled);
            Assert.Equal(!secret, view.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == Core.PhoneDrive.GoogleException.Words(Core.PhoneDrive.GoogleTrouble.NotBuilt) && t.IsEffectivelyVisible));
            Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Your notes go into a private Syft Notes folder in your Google Drive that only Syft Notes can see." && t.IsEffectivelyVisible);
            Assert.DoesNotContain(view.GetVisualDescendants().OfType<Button>(), b => b.Content is TextBlock { Text: "Add a phone" } && b.IsEffectivelyVisible);
        }
        finally
        {
            w.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(SkinKind.Mac)]
    [InlineData(SkinKind.Win)]
    public async Task Other_ways_opens_adding_a_phone_over_tailscale(SkinKind skin)
    {
        ((App)Application.Current!).UseSkin(skin);
        await using var google = await GoogleDrive(account: false);
        var drive = google.Drive;
        var model = new PhonesModel(() => Library(paired: false)) { Google = new PhoneGoogleModel(drive) };
        await model.Load();
        Control view = skin == SkinKind.Mac ? new MacPhones { DataContext = model } : new WinPhones { DataContext = model };
        var w = new Window { Width = 760, Height = 1000, Content = view };
        w.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            Button? Visible(string text) => view.GetVisualDescendants().OfType<Button>()
                .FirstOrDefault(b => b.IsEffectivelyVisible && b.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == text));
            // Closed: Google first, and a quiet Other ways under it that says what it's for.
            Assert.NotNull(Visible("Connect with Google"));
            Assert.Null(Visible("Add a phone"));
            var other = Visible("Other ways")!;
            Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == model.OtherWaysAbout && t.IsEffectivelyVisible);

            other.Command!.Execute(null);
            Dispatcher.UIThread.RunJobs();
            var add = Visible("Add a phone")!;
            Assert.Same(model.AddPhoneCommand, add.Command);
            Assert.True(add.IsEffectivelyEnabled);
            Assert.NotNull(Visible("Connect with Google")); // still first

            // The Tailscale way as it always was: the code and its QR code, black on white.
            await model.AddPhoneCommand.ExecuteAsync(null);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("042 917", view.GetVisualDescendants().OfType<SelectableTextBlock>().Single(t => t.Name == "PhoneCode").Text);
            var card = view.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "QrCard");
            Assert.True(card.IsEffectivelyVisible);
            Assert.Equal(Colors.White, ((ISolidColorBrush)card.Background!).Color);

            // Folded away again, the code goes with it.
            other.Command.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.False(model.Adding);
            Assert.Null(Visible("Add a phone"));
        }
        finally
        {
            w.Close();
        }
    }
}
