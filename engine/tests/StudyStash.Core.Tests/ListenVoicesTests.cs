using System.Net;
using System.Security.Cryptography;
using StudyStash.Audio;
using StudyStash.Core.Study;

namespace StudyStash.Core.Tests;

/// <summary>Listen's downloadable voices (Kokoro): offered by the library with their size and licence, fetched in the
/// background with how far they've got, read with once they're here (the computer's own voices until then), and
/// removed again. The voices and the download are stand-ins here, except in the one test that uses Kokoro itself
/// when its files are on this computer.</summary>
public class ListenVoicesTests
{
    static async Task<VoicesInfo> SettledAsync(StudyLibrary lib)
    {
        for (int i = 0; i < 500; i++)
        {
            if (await lib.Study.VoicesAsync() is { State: not VoicesInfo.Downloading } info) return info;
            await Task.Delay(20);
        }
        throw new TimeoutException("the voices were still downloading");
    }

    [Fact]
    public async Task Downloaded_voices_read_listens_once_theyre_here_and_the_computers_own_until_then()
    {
        var computer = new ListenTests.ToneVoices();
        var kokoro = new ListenTests.ToneVoices();
        var pack = new FakeVoicePack(kokoro) { Let = new TaskCompletionSource() };
        await using var lib = await StudyLibrary.StartAsync(l => (l.Speech, l.VoicePack) = (computer, pack));
        lib.Answers.Add(ListenTests.Script);

        var offered = (await lib.Study.VoicesAsync())!;
        Assert.Equal((VoicesInfo.None, "Kokoro", "Apache-2.0", 1_000_000L, false), (offered.State, offered.Name, offered.License, offered.Bytes, offered.InUse));

        // Not here yet: the computer's own voices read it.
        await lib.Study.MakeListenAsync("v1");
        Assert.Equal(ListenInfo.Ready, (await lib.ListenedAsync("v1")).State);
        Assert.Equal(4, computer.Said.Count);
        Assert.Empty(kokoro.Said);

        // Downloading: how far it's got, and a second press doesn't start another.
        Assert.Equal(VoicesInfo.Downloading, (await lib.Study.DownloadVoicesAsync())!.State);
        VoicesInfo coming;
        for (int i = 0; (coming = (await lib.Study.VoicesAsync())!).Done == 0 && i < 200; i++) await Task.Delay(10);
        Assert.Equal((VoicesInfo.Downloading, 400_000L), (coming.State, coming.Done));
        await lib.Study.DownloadVoicesAsync();
        pack.Let.SetResult();
        var here = await SettledAsync(lib);
        Assert.Equal((VoicesInfo.Ready, 1_000_000L, true), (here.State, here.Done, here.InUse));
        Assert.Equal(1, pack.Downloads);

        // Here: they read the next one.
        await lib.Study.MakeListenAsync("m1");
        Assert.Equal(ListenInfo.Ready, (await lib.ListenedAsync("m1")).State);
        Assert.Equal(4, kokoro.Said.Count);
        Assert.Equal(4, computer.Said.Count);

        // Removed: the computer's own again.
        Assert.Equal(VoicesInfo.None, (await lib.Study.DeleteVoicesAsync())!.State);
        await lib.Study.MakeListenAsync("c1");
        await lib.ListenedAsync("c1");
        Assert.Equal(8, computer.Said.Count);
    }

    [Fact]
    public async Task A_download_that_fails_says_why_and_can_be_tried_again()
    {
        var pack = new FakeVoicePack(new ListenTests.ToneVoices()) { Fail = new HttpRequestException("No connection.") };
        await using var lib = await StudyLibrary.StartAsync(l => (l.Speech, l.VoicePack) = (new ListenTests.ToneVoices(), pack));

        await lib.Study.DownloadVoicesAsync();
        var failed = await SettledAsync(lib);
        Assert.Equal((VoicesInfo.Failed, "No connection."), (failed.State, failed.Error));

        pack.Fail = null;
        await lib.Study.DownloadVoicesAsync();
        Assert.Equal(VoicesInfo.Ready, (await SettledAsync(lib)).State);
    }

    [Fact]
    public async Task Downloaded_voices_speak_on_a_computer_with_none_of_its_own()
    {
        var pack = new FakeVoicePack(new ListenTests.ToneVoices()) { Here = true };
        await using var lib = await StudyLibrary.StartAsync(l => (l.Speech, l.VoicePack) = (new ListenTests.ToneVoices(ready: false), pack));
        Assert.True((await lib.Study.ListenAsync("v1"))!.CanSpeak);
        pack.Here = false;
        Assert.False((await lib.Study.ListenAsync("v1"))!.CanSpeak);
    }

    [Fact]
    public async Task A_library_without_voices_to_download_says_so()
    {
        await using var lib = await StudyLibrary.StartAsync(l => l.Speech = new ListenTests.ToneVoices());
        Assert.Equal(VoicesInfo.Unavailable, (await lib.Study.VoicesAsync())!.State);
        Assert.Equal(409, (await Assert.ThrowsAsync<LibraryRefusedException>(() => lib.Study.DownloadVoicesAsync())).Status);
    }

    /// <summary>A pretend Hugging Face for a model of several files: each file at its own address.</summary>
    sealed class FilesServer(Dictionary<string, byte[]> files) : HttpMessageHandler
    {
        public List<string> Asked { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string path = request.RequestUri!.AbsolutePath.TrimStart('/');
            Asked.Add(path);
            return Task.FromResult(files.TryGetValue(path, out var bytes)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    [Fact]
    public async Task Kokoros_files_come_down_into_their_folders_with_progress_and_go_again()
    {
        using var dir = new TempDir();
        var files = new Dictionary<string, byte[]>();
        var rng = new Random(3);
        foreach (string name in new[] { "tokens.txt", "espeak-ng-data/lang/gmw/en-US", "voices.bin" })
        {
            var bytes = new byte[name == "voices.bin" ? 2 << 20 : 300];
            rng.NextBytes(bytes);
            files[name] = bytes;
        }
        var model = new WhisperModel("kokoro-test", "Kokoro voices", "kokoro-test", files.Values.Sum(b => (long)b.Length), "", "For tests.")
        {
            Source = "https://models.example",
            Parts = [.. files.Select(f => new ModelPart(f.Key, f.Value.Length, Convert.ToHexStringLower(SHA256.HashData(f.Value))))],
        };
        var server = new FilesServer(files);
        var voices = new KokoroVoices(dir.Path, new HttpClient(server), model);
        var seen = new List<long>();

        Assert.False(voices.IsHere);
        await voices.DownloadAsync(new Counted(seen), CancellationToken.None);

        Assert.True(voices.IsHere);
        Assert.Equal(files.Keys, server.Asked);
        string folder = Path.Combine(dir.Path, "models", "kokoro-test");
        Assert.Equal(files["espeak-ng-data/lang/gmw/en-US"], File.ReadAllBytes(Path.Combine(folder, "espeak-ng-data", "lang", "gmw", "en-US")));
        Assert.Equal(model.Bytes, seen[^1]);
        Assert.Equal(seen.Order(), seen);

        voices.Delete();
        Assert.False(voices.IsHere);
        Assert.False(Directory.Exists(folder));
    }

    sealed class Counted(List<long> seen) : IProgress<long>
    {
        public void Report(long value) => seen.Add(value);
    }

    [Fact]
    public void Kokoros_download_is_pinned_hashed_and_small()
    {
        var m = Kokoro.Model;
        Assert.InRange(m.Bytes, 140_000_000, 160_000_000);
        Assert.All(m.Files, f => Assert.Matches("^[0-9a-f]{64}$", f.Sha256));
        Assert.All(m.Files, f => Assert.Matches("/resolve/[0-9a-f]{40}/", m.UrlOf(f, null)));
        Assert.Equal(Kokoro.ModelFile, m.Files[^1].Name); // the big one last
        Assert.Contains(m.Files, f => f.Name == "LICENSE");
        Assert.Equal("Apache-2.0", new KokoroVoices("/nowhere").License);
    }

    /// <summary>Kokoro itself, when its files are on this computer (in STUDYSTASH_KOKORO, or the default home's models
    /// folder): both voices say a line into a 16-bit mono WAV at Listen's rate.</summary>
    [Fact]
    public async Task Kokoro_speaks_a_line_when_its_here()
    {
        string folder = Environment.GetEnvironmentVariable("STUDYSTASH_KOKORO") is { Length: > 0 } env ? env
            : WhisperModels.PathFor(Configs.DefaultHome, Kokoro.Model);
        using var speech = new KokoroSpeech(folder);
        if (!speech.Ready().Ok) return;
        using var dir = new TempDir();
        string a = Path.Combine(dir.Path, "a.wav"), b = Path.Combine(dir.Path, "b.wav");
        await speech.SayAsync("Arteries carry blood away from the heart.", "A", a, CancellationToken.None);
        await speech.SayAsync("And veins bring it back?", "B", b, CancellationToken.None);
        foreach (string wav in new[] { a, b })
        {
            var (channels, rate, bits, _) = Wav.Read(wav);
            Assert.Equal((1, Wav.Rate, 16), (channels, rate, bits));
            Assert.Contains(Wav.Mono(wav), s => Math.Abs((int)s) > 1000);
        }
        Assert.InRange(Wav.Join([a, b], Path.Combine(dir.Path, "all.wav"), 0.35), 2, 10);
    }
}
