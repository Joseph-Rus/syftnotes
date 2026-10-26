using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StudyStash.App.Services;
using StudyStash.App.ViewModels;
using StudyStash.App.Views;
using StudyStash.Core;

namespace StudyStash.App.Tests;

/// <summary>Settings → Granola: connecting, the lectures in Granola that Egghead Notes doesn't have, bringing one over
/// or leaving it, and the words of the notification that offers one.</summary>
public class GranolaSettingsTests
{
    static readonly string Summary = string.Join("\n", Enumerable.Repeat("- The cell membrane is a lipid bilayer that lets small nonpolar molecules through.", 8));

    /// <summary>Granola with these lectures in it, each with a summary and no transcript (a free plan).</summary>
    sealed class PretendGranola(params (string Id, string Title, string Date)[] lectures) : ILectureSource
    {
        sealed class Session : IGranolaSession
        {
            public Task<IReadOnlyList<ToolInfo>> ListToolsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ToolInfo>>([]);
            public Task<ToolResult> CallToolAsync(string name, JsonObject args, CancellationToken ct = default) => Task.FromResult(ToolResult.Text(""));
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }

        public Task<IGranolaSession> SessionAsync(CancellationToken ct = default) => Task.FromResult<IGranolaSession>(new Session());

        public Task<List<Meeting>> ListMeetingsAsync(IGranolaSession session, DateOnly? since = null, DateOnly? until = null, int limit = 50,
            int maxPages = 20, CancellationToken ct = default) =>
            Task.FromResult(lectures.Select(l => new Meeting(l.Id) { Title = l.Title, Date = l.Date }).ToList());

        public Task<List<Meeting>> GetMeetingsAsync(IGranolaSession session, IReadOnlyList<string> ids, CancellationToken ct = default) =>
            Task.FromResult(lectures.Where(l => ids.Contains(l.Id)).Select(l => new Meeting(l.Id) { Title = l.Title, Date = l.Date, NotesMarkdown = Summary }).ToList());

        public Task<string> GetTranscriptAsync(IGranolaSession session, string meetingId, CancellationToken ct = default) => Task.FromResult("");
    }

    static readonly (string, string, string)[] TwoLectures =
        [("a", "Cell membranes and transport", "2026-10-05T09:00:00"), ("b", "Osmosis and tonicity", "2026-10-07T09:00:00")];

    /// <summary>Granola's keeper on a computer connected to a library that takes what it's sent (kept in <paramref name="sent"/>).</summary>
    static GranolaImport Import(TempHome home, bool signedIn, List<JsonObject> sent, params (string, string, string)[] lectures)
    {
        if (signedIn) File.WriteAllText(home["granola-tokens.json"], "{\"access_token\": \"t\"}");
        var cc = new ClientConfig(home.Path) { ServerUrl = "http://library:8787", PoolKey = "secret", DisplayName = "Sam" };
        var net = new LaptopHost
        {
            Post = (_, body, _) =>
            {
                sent.Add((JsonObject)JsonNode.Parse(body)!);
                return Task.FromResult(new JsonObject { ["status"] = "queued" });
            },
            Get = (_, _) => Task.FromResult<JsonObject?>(null),
        };
        var granola = new PretendGranola(lectures);
        return new GranolaImport(home["granola-tokens.json"], home["granola-client.json"], new GranolaLedger(home["granola.json"]), () => cc, net, _ => granola);
    }

    /// <summary>Lets what the page started on other threads finish and land back on the page.</summary>
    static async Task Settle(Func<bool> until)
    {
        for (int i = 0; i < 200 && !until(); i++)
        {
            await Task.Delay(10);
            Dispatcher.UIThread.RunJobs();
        }
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public async Task The_page_lists_what_granola_has_and_transfer_sends_it_to_the_library()
    {
        using var home = new TempHome();
        var sent = new List<JsonObject>();
        var import = Import(home, signedIn: true, sent, TwoLectures);
        bool notifies = true;
        using var page = new GranolaSettingsModel(import, () => notifies, on => notifies = on);
        Assert.True(page.SignedIn);
        Assert.True(page.Notify);

        page.LookNowCommand.Execute(null);
        await Settle(() => page.Lectures.Count == 2);
        Assert.Equal(["Osmosis and tonicity", "Cell membranes and transport"], page.Lectures.Select(l => l.Title));
        Assert.Equal("Wed 7 Oct", page.Lectures[0].When);
        Assert.True(page.HasSeveral);

        page.TransferCommand.Execute(page.Lectures[1]);
        await Settle(() => sent.Count == 1 && page.Lectures.Count == 1);
        Assert.Equal(("granola-a", "Cell membranes and transport", Summary), ((string?)sent[0]["id"], (string?)sent[0]["title"], (string?)sent[0]["notes_markdown"]));
        Assert.Equal(["Osmosis and tonicity"], page.Lectures.Select(l => l.Title));
        Assert.False(page.HasSeveral);

        page.LeaveCommand.Execute(page.Lectures[0]);
        await Settle(() => page.Lectures.Count == 0);
        Assert.True(page.NothingWaiting);
        Assert.Single(sent);

        // The switch is the student's, kept where the app keeps its settings.
        page.Notify = false;
        Assert.False(notifies);

        page.DisconnectCommand.Execute(null);
        await Settle(() => !page.SignedIn);
        Assert.True(page.CanConnect);
        Assert.False(File.Exists(home["granola-tokens.json"]));
    }

    [Fact]
    public void The_notification_names_one_lecture_and_counts_several()
    {
        var one = NoticeWords.GranolaHas(["Cell membranes and transport"]);
        Assert.Equal(("Granola has a lecture Egghead Notes doesn't", "Transfer"), (one.Title, one.Action));
        Assert.StartsWith("Cell membranes and transport. Bring it over", one.Text);
        var three = NoticeWords.GranolaHas(["Osmosis", "Membranes", "Enzymes"]);
        Assert.Equal(("Granola has 3 lectures Egghead Notes doesn't", "Osmosis and 2 more.", "Show them"), three);
        Assert.Equal("Osmosis and one more.", NoticeWords.GranolaHas(["Osmosis", "Membranes"]).Text);
        Assert.Equal("A lecture with no title is with your library, which is writing its notes.", NoticeWords.GranolaBrought(" ", null).Text);
    }

    /// <summary>The page in Settings, in both looks: not connected, and connected with two lectures waiting.</summary>
    [AvaloniaFact]
    public async Task Granola_settings_shots()
    {
        foreach (var skin in new[] { SkinKind.Mac, SkinKind.Win })
            foreach (bool signedIn in new[] { false, true })
            {
                using var home = new TempHome();
                new AppSettings { SetupDone = true }.Save(home.Path);
                if (signedIn) File.WriteAllText(home["granola-tokens.json"], "{\"access_token\": \"t\"}");
                var granola = new PretendGranola(TwoLectures);
                using var host = new AppHost(home.Path, log: _ => { }, granola: _ => granola,
                    laptop: new LaptopHost { Get = (_, _) => Task.FromResult<JsonObject?>(null) });
                ((App)Application.Current!).UseSkin(skin);
                using var model = SettingsModel.Make(host);
                model.Section = "Granola";
                if (signedIn) await Settle(() => model.Granola.Lectures.Count == 2);
                string look = skin == SkinKind.Mac ? "mac" : "win";
                foreach (var t in new[] { ThemeVariant.Light, ThemeVariant.Dark })
                {
                    var shot = Shot.Take($"{look}-settings-granola-{(signedIn ? "connected" : "connect")}", skin, t,
                        () => new SettingsView { DataContext = model, DrawChrome = true }, size: new Size(1700, skin == SkinKind.Mac ? 908 : 988));
                    Assert.True(shot.PixelSize.Width > 0);
                }
                // The notification that offers a lecture, and the one for several.
                if (signedIn)
                    foreach (var t in new[] { ThemeVariant.Light, ThemeVariant.Dark })
                        Shot.Take($"{look}-toast-granola", skin, t, () =>
                        {
                            var one = NoticeWords.GranolaHas(["Cell membranes and transport"]);
                            var two = NoticeWords.GranolaHas(["Osmosis and tonicity", "Cell membranes and transport"]);
                            var brought = NoticeWords.GranolaBrought("Cell membranes and transport", null);
                            return new StackPanel
                            {
                                Spacing = 10,
                                Children =
                                {
                                    new ToastView { Title = one.Title, Text = one.Text, ActionLabel = one.Action },
                                    new ToastView { Title = two.Title, Text = two.Text, ActionLabel = two.Action },
                                    new ToastView { Title = brought.Title, Text = brought.Text },
                                },
                            };
                        }, size: new Size(560, 420));
                // The page says what it is and what a free Granola plan gives, whichever state it's in.
                var view = new SettingsView { DataContext = model };
                var window = new Window { Width = 1100, Height = 800, Content = view };
                window.Show();
                Dispatcher.UIThread.RunJobs();
                var texts = view.GetVisualDescendants().OfType<TextBlock>().Where(x => x.IsEffectivelyVisible).Select(x => x.Text ?? "").ToList();
                Assert.Contains(GranolaSettingsModel.About, texts);
                Assert.Contains(GranolaSettingsModel.PlanNote, texts);
                Assert.Contains(signedIn ? "Cell membranes and transport" : "Connect Granola", texts);
                Assert.Equal(signedIn, texts.Contains("Transfer all"));
                window.Close();
            }
        ((App)Application.Current!).UseSkin(SkinKind.Mac);
    }
}
