using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StudyStash.App.ViewModels;
using StudyStash.App.Views;
using StudyStash.Core.Study;
using StudyStash.Core.Tests;

namespace StudyStash.App.Tests;

/// <summary>Listen on a lecture's page, against a library run here (its AI and its voices stand-ins): Listen makes it,
/// the page says it's being made and looks again until it's ready, and then it plays, pauses where it was, goes back
/// fifteen seconds, stops at the end, and shows its script.</summary>
public class ListenPageTests
{
    const string Script = """
        {"lines":[
          {"who":"A","text":"Today was blood vessels: arteries, capillaries and veins."},
          {"who":"B","text":"So arteries carry blood to the heart?"},
          {"who":"A","text":"Away from it. Veins bring it back, with valves."}
        ]}
        """;

    /// <summary>A second of sound a word.</summary>
    sealed class Voices : ISpeech
    {
        public (bool Ok, string Why) Ready() => (true, "");
        public Task SayAsync(string text, string who, string wav, CancellationToken ct)
        {
            Wav.Write(wav, new short[Wav.Rate * text.Split(' ').Length]);
            return Task.CompletedTask;
        }
    }

    sealed class FakePlayer : IListenPlayer
    {
        public List<(string Wav, double From)> Played { get; } = [];
        public int Stopped { get; private set; }
        public void Play(string wav, double fromSeconds) => Played.Add((wav, fromSeconds));
        public void Stop() => Stopped++;
    }

    [AvaloniaFact]
    public async Task Listen_is_made_then_plays_pauses_and_goes_back()
    {
        await using var lib = await StudyLibrary.StartAsync(l => l.Speech = new Voices());
        lib.Answers.Add(Script);
        using var cache = new TempDir();
        var player = new FakePlayer();
        var listen = new ListenModel(lib.Study, "v1", player, cache.Path) { LookEvery = TimeSpan.FromMilliseconds(30) };

        await listen.LoadAsync();
        Assert.Equal((true, "Listen", false), (listen.CanMake, listen.MakeSaid, listen.Ready));

        await listen.MakeCommand.ExecuteAsync(null);
        await Until(() => listen.Ready);
        Assert.Equal(3, listen.Script.Count);
        Assert.Equal(("Tutor", "Classmate"), (listen.Script[0].Who, listen.Script[1].Who));
        Assert.Equal(24 + 0.7, listen.Seconds, 1); // 8 + 7 + 9 words a second each, and two pauses
        Assert.Equal("00:00 / 00:24", listen.Time);

        await listen.PlayPauseCommand.ExecuteAsync(null);
        Assert.True(listen.Playing);
        var (wav, from) = player.Played.Single();
        Assert.True(File.Exists(wav));
        Assert.Equal(0, from);
        Assert.Equal("Pause", listen.PlaySaid);

        listen.Position = 20; // as if twenty seconds have gone by
        await listen.PlayPauseCommand.ExecuteAsync(null);
        Assert.False(listen.Playing);
        Assert.Equal(1, player.Stopped);
        Assert.Equal("Play on", listen.PlaySaid);

        listen.Position = 20;
        listen.BackCommand.Execute(null);
        Assert.Equal(5, listen.Position);
        await listen.PlayPauseCommand.ExecuteAsync(null);
        Assert.Equal(5, player.Played[^1].From);
        listen.Dispose();
        Assert.False(listen.Playing);
    }

    [AvaloniaFact]
    public async Task A_listen_that_couldnt_be_made_offers_to_try_again()
    {
        await using var lib = await StudyLibrary.StartAsync(l => l.Speech = new Voices());
        lib.Answers.Add("no script");
        var listen = new ListenModel(lib.Study, "v1", new FakePlayer()) { LookEvery = TimeSpan.FromMilliseconds(30) };
        await listen.LoadAsync();
        await listen.MakeCommand.ExecuteAsync(null);
        await Until(() => listen.State == ListenInfo.Failed);
        Assert.Equal(("Try Listen again", true), (listen.MakeSaid, listen.CanMake));
        Assert.StartsWith("It couldn't be made:", listen.Problem);
    }

    [AvaloniaTheory]
    [InlineData(SkinKind.Mac)]
    [InlineData(SkinKind.Win)]
    public async Task The_lectures_page_shows_Listen_and_then_its_player(SkinKind skin)
    {
        await using var lib = await StudyLibrary.StartAsync(l => l.Speech = new Voices());
        lib.Answers.Add(Script);
        var note = new NoteModel { Id = "v1", Title = "Blood vessels", Markdown = "## Vessels" };
        var study = new LectureStudyModel(lib.Study, "v1") { Listen = new ListenModel(lib.Study, "v1", new FakePlayer()) { LookEvery = TimeSpan.FromMilliseconds(30) } };
        await study.LoadAsync();
        await study.Listen.LoadAsync();
        var page = new NotePage { DataContext = new LibraryModel { Note = note, LectureStudy = study } };
        var w = MenuPickTests.Host(page, skin);
        try
        {
            Button Named(string name) => page.GetVisualDescendants().OfType<Button>().Single(b => b.Name == name);
            Assert.True(Named("MakeListen").IsEffectivelyVisible);
            Assert.False(Named("PlayListen").IsEffectivelyVisible);
            await study.Listen.MakeCommand.ExecuteAsync(null);
            await Until(() => study.Listen.Ready);
            Dispatcher.UIThread.RunJobs();
            Assert.True(Named("PlayListen").IsEffectivelyVisible);
            Assert.False(Named("MakeListen").IsEffectivelyVisible);
            Assert.Contains(page.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "00:00 / 00:24" && t.IsEffectivelyVisible);
        }
        finally
        {
            w.Close();
        }
    }

    static async Task Until(Func<bool> done)
    {
        for (int i = 0; i < 500 && !done(); i++)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
            Dispatcher.UIThread.RunJobs();
        }
        Assert.True(done());
    }
}
