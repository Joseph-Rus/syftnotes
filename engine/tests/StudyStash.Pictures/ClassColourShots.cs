using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using StudyStash.App.Services;
using StudyStash.App.Tests;
using StudyStash.App.ViewModels;
using StudyStash.App.Views;
using StudyStash.Core;
using StudyStash.Core.Tests;
using StudyStash.Library;

namespace StudyStash.App.Pictures;

/// <summary>
/// A class keeps its colour: the sidebar and Settings → Classes side by side, before and after BIO 110 (second of
/// four) is removed in Settings (shots/class-colour-&lt;before|after&gt;-&lt;mac|win&gt;-&lt;light|dark&gt;.png). A real
/// library in a throwaway folder, whose config was written before colours were kept; the sidebar's dots come from its
/// overview the way the app's sidebar takes them. Drawn off-screen, never on the student's screen.
/// </summary>
public class ClassColourShots
{
    static readonly TailscaleInfo NoTailnet = new(false, false, "", "", []);

    static async Task TakeAsync(SkinKind skin, ThemeVariant variant)
    {
        ((App)Application.Current!).UseSkin(skin);
        string tail = $"{(skin == SkinKind.Mac ? "mac" : "win")}-{(variant == ThemeVariant.Dark ? "dark" : "light")}";
        using var dir = new TempDir();
        var cfg = new Config(dir["home"], dir["pool"])
        {
            PoolName = "Sam's library", PoolPassword = "pw", OllamaEnabled = false,
            Classes = [new ClassDef("CS 101"), new ClassDef("BIO 110"), new ClassDef("HIST 210"), new ClassDef("MATH 221")],
        };
        // A library from before colours were kept: no class has one written down.
        File.WriteAllLines(Configs.Save(cfg), File.ReadAllLines(cfg.ConfigPath).Where(l => !l.StartsWith("color = ", StringComparison.Ordinal)));
        cfg = Configs.Load(cfg.Home);
        using var store = new Store(cfg.DbPath, cfg.PoolDir);
        int n = 0;
        foreach (var (cls, count) in new[] { ("CS 101", 3), ("HIST 210", 2), ("MATH 221", 1) })
            for (int k = 0; k < count; k++)
                store.Save(new Meeting($"m{++n}") { Title = $"Lecture {n}", Date = $"2026-09-{n:00}", Transcript = "Today." }, new Classification(cls, 0.9, "folder"));
        await using var site = await TestSite.StartAsync(b => LibraryWeb.Build(b, cfg, store, new Pipeline(cfg, store, log: _ => { }), new LibraryWebOptions
        {
            ListModels = _ => Task.FromResult<List<(string, double)>?>([]), Tailscale = () => NoTailnet, Latest = _ => Task.FromResult<Release?>(null),
            RamGb = () => 16, HostName = () => "library",
        }));
        LibrarySettingsModel.Call call = async (method, path, body) =>
        {
            var req = new HttpRequestMessage(method, "/api/v2/settings" + path);
            req.Headers.Authorization = new("Bearer", "pw");
            if (body is not null) req.Content = JsonContent.Create(body);
            var r = await site.Client.SendAsync(req);
            r.EnsureSuccessStatusCode();
            var answer = JsonNode.Parse(await r.Content.ReadAsStringAsync()) as JsonObject;
            // The throwaway folder's long name, shown as a student's would be.
            foreach (var c in (answer?["classes"] as JsonArray ?? []).OfType<JsonObject>())
                c["folder"] = "/Users/sam/Syft Notes/Lecture notes/" + c["name"]!.GetValue<string>();
            return answer;
        };

        using var home = new TempHome();
        new AppSettings { SetupDone = true, Role = AppRole.Laptop }.Save(home.Path);
        using var host = new AppHost(home.Path, log: _ => { });
        using var settings = SettingsModel.Make(host, library: () => call);
        var library = Demo.Library();
        // The sidebar as Shell fills it: each class's dot from the colour the library's overview gives it.
        void Sidebar()
        {
            library.Classes.Clear();
            foreach (var c in new LibraryReader(cfg, store).Overview()["classes"]!.AsArray().OfType<JsonObject>())
                library.Classes.Add(new ClassItem
                {
                    Name = c["name"]!.GetValue<string>(), Dot = Skin.ClassDot(c["color"]!.GetValue<int>()), Count = c["lectures"]!.GetValue<int>(),
                    Selected = c["name"]!.GetValue<string>() == "CS 101",
                });
        }
        Control libraryView = skin == SkinKind.Mac ? new MacLibrary { DataContext = library } : new WinLibrary { DataContext = library };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("1100,900") };
        grid.Children.Add(libraryView);
        var settingsView = new SettingsView { DataContext = settings, DrawChrome = true };
        Grid.SetColumn(settingsView, 1);
        grid.Children.Add(settingsView);
        var window = new Window { Width = 2000, Height = 760, RequestedThemeVariant = variant, Content = grid };
        Look.Apply(window);
        window.Show();

        Sidebar();
        settings.Section = "Classes";
        await Wait.UntilAsync(() => settings.Lib.Classes.Count == 4, "the classes to load");
        Save(window, $"class-colour-before-{tail}");

        await settings.Lib.RemoveClassCommand.ExecuteAsync(settings.Lib.Classes.Single(c => c.Name == "BIO 110"));
        await settings.Lib.Load();
        Assert.Equal(["CS 101", "HIST 210", "MATH 221"], cfg.ClassNames());
        Sidebar();
        Save(window, $"class-colour-after-{tail}");
        window.Close();
    }

    static void Save(Window window, string name)
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("nothing rendered");
        using var file = File.Create(Path.Combine(Shot.Dir, name + ".png"));
        frame.Save(file, PngBitmapEncoderOptions.Default);
    }

    [AvaloniaFact]
    public async Task A_class_keeps_its_colour_when_another_is_removed()
    {
        try
        {
            await TakeAsync(SkinKind.Mac, ThemeVariant.Light);
            await TakeAsync(SkinKind.Mac, ThemeVariant.Dark);
            await TakeAsync(SkinKind.Win, ThemeVariant.Light);
        }
        finally
        {
            ((App)Application.Current!).UseSkin(SkinKind.Mac);
        }
    }
}
