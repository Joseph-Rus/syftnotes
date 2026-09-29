using System.Net;
using System.Security.Cryptography;
using StudyStash.Audio;

namespace StudyStash.Core.Tests;

/// <summary>Parakeet: how its words become lines, which languages it reads, and its model coming down as a folder.</summary>
public class ParakeetTests
{
    [Fact]
    public void Words_become_lines_at_a_sentence_end_and_at_a_long_pause_but_not_after_an_abbreviation()
    {
        string[] tokens = ["▁The", "▁cell", "▁divides", ".", "▁Dr", ".", "▁Lee", "▁agrees", ".", "▁Then", "▁one", "▁two", "▁three", "▁four"];
        float[] starts = [0, .3f, .6f, .9f, 1.2f, 1.4f, 1.5f, 1.9f, 2.3f, 3, 3.3f, 3.6f, 3.9f, 8];
        // The last word's duration runs on through a quiet stretch (as the model's do): its line still ends soon after it.
        float[] durations = [.3f, .3f, .3f, .1f, .2f, .1f, .4f, .4f, .1f, .3f, .3f, .3f, .3f, 30];

        var lines = ParakeetTranscriber.Lines(tokens, starts, durations, length: 9);

        Assert.Equal(["The cell divides.", "Dr. Lee agrees.", "Then one two three", "four"], lines.Select(l => l.Text));
        Assert.Equal(0, lines[0].Start);
        Assert.Equal(1.2, lines[1].Start, 3);
        Assert.Equal(8, lines[3].Start);
        Assert.Equal(8.4, lines[3].End, 3);
        Assert.All(lines, l => Assert.True(l.End >= l.Start));
    }

    [Fact]
    public void Nothing_heard_is_no_lines()
    {
        Assert.Empty(ParakeetTranscriber.Lines([], null, null, 5));
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("auto", true)]
    [InlineData("en", true)]
    [InlineData("EN-us", true)]
    [InlineData("pt_BR", true)]
    [InlineData("Spanish", true)]
    [InlineData("uk", true)]
    [InlineData("ko", false)]
    [InlineData("japanese", false)]
    [InlineData("zh", false)]
    public void It_reads_the_languages_it_was_taught(string language, bool reads)
    {
        Assert.Equal(reads, ParakeetLanguages.Knows(language));
    }

    [Fact]
    public void The_models_size_is_the_size_of_its_files()
    {
        foreach (var m in new[] { WhisperModels.Parakeet, WhisperModels.Speakers })
        {
            Assert.Equal(m.Bytes, m.Parts!.Sum(p => p.Bytes));
            Assert.All(m.Parts!, p => Assert.Matches("^[0-9a-f]{64}$", p.Sha256));
        }
        Assert.Equal(SpeechEngine.Parakeet, WhisperModels.Parakeet.Engine);
        Assert.Contains(WhisperModels.Parakeet, WhisperModels.All);
        Assert.DoesNotContain(WhisperModels.Speakers, WhisperModels.All); // it isn't a transcription model to pick
    }

    /// <summary>A pretend Hugging Face for a folder of files: each is served by its name, from where a Range asks.</summary>
    sealed class FolderServer(Dictionary<string, byte[]> files) : HttpMessageHandler
    {
        public List<(string Name, long? From)> Asked { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string name = request.RequestUri!.Segments[^1];
            long? from = request.Headers.Range?.Ranges.FirstOrDefault()?.From;
            Asked.Add((name, from));
            var file = files[name];
            if (from is { } f)
            {
                var partial = new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new ByteArrayContent(file, (int)f, file.Length - (int)f) };
                partial.Content.Headers.ContentRange = new System.Net.Http.Headers.ContentRangeHeaderValue(f, file.Length - 1, file.Length);
                return Task.FromResult(partial);
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(file) });
        }
    }

    /// <summary>A small model of three files, and a server for them.</summary>
    static (WhisperModel Model, FolderServer Server, Dictionary<string, byte[]> Files) Folder()
    {
        var files = new Dictionary<string, byte[]>();
        var parts = new List<ModelPart>();
        int seed = 1;
        foreach (var (name, size) in new[] { ("tokens.txt", 1000), ("decoder.onnx", 200_000), ("encoder.weights", 3 << 20) })
        {
            var bytes = new byte[size];
            new Random(seed++).NextBytes(bytes);
            files[name] = bytes;
            parts.Add(new ModelPart(name, size, Convert.ToHexStringLower(SHA256.HashData(bytes))));
        }
        var m = new WhisperModel("test-folder", "Parakeet test", "test-folder", parts.Sum(p => p.Bytes), "", "For tests.")
        {
            Engine = SpeechEngine.Parakeet, Parts = parts,
        };
        return (m, new FolderServer(files), files);
    }

    static readonly Func<string, long?> Roomy = _ => 100L << 30;

    [Fact]
    public async Task A_model_of_several_files_comes_down_into_its_folder_with_one_progress_across_them()
    {
        using var dir = new TempDir();
        var (m, server, files) = Folder();
        var seen = new Seen();

        string path = await ModelDownload.RunAsync(dir.Path, m, seen, default, new HttpClient(server), null, Roomy, "https://models.example/");

        Assert.Equal(WhisperModels.PathFor(dir.Path, m), path);
        Assert.True(WhisperModels.IsDownloaded(dir.Path, m));
        foreach (var (name, bytes) in files) Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(path, name)));
        Assert.Empty(Directory.GetFiles(path, "*.part"));
        Assert.Equal(m.Bytes, seen.All[^1].Done);
        Assert.All(seen.All, p => Assert.Equal(m.Bytes, p.Total));
        // Never backwards from one file to the next.
        for (int i = 1; i < seen.All.Count; i++) Assert.True(seen.All[i].Done >= seen.All[i - 1].Done);

        WhisperModels.Delete(dir.Path, m);
        Assert.False(Directory.Exists(path));
    }

    [Fact]
    public async Task A_stopped_download_of_several_files_asks_only_for_what_is_missing()
    {
        using var dir = new TempDir();
        var (m, server, files) = Folder();
        string folder = WhisperModels.PathFor(dir.Path, m);
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, "tokens.txt"), files["tokens.txt"]); // done
        File.WriteAllBytes(Path.Combine(folder, "encoder.weights.part"), files["encoder.weights"][..1_000_000]); // half way
        long onDisk = WhisperModels.OnDisk(dir.Path, m);
        Assert.Equal(1000 + 1_000_000, onDisk);
        var seen = new Seen();

        await ModelDownload.RunAsync(dir.Path, m, seen, default, new HttpClient(server), null, Roomy, "https://models.example");

        Assert.Equal([("decoder.onnx", (long?)null), ("encoder.weights", 1_000_000L)], server.Asked);
        Assert.Equal(onDisk, seen.All[0].Done); // what's here shows at once
        Assert.True(WhisperModels.IsDownloaded(dir.Path, m));
        Assert.Equal(files["encoder.weights"], File.ReadAllBytes(Path.Combine(folder, "encoder.weights")));
    }

    [Fact]
    public async Task A_disk_without_room_for_all_the_files_says_so_before_the_first_one_starts()
    {
        using var dir = new TempDir();
        var (m, server, _) = Folder();

        var e = await Assert.ThrowsAsync<NotEnoughSpaceException>(() =>
            ModelDownload.RunAsync(dir.Path, m, null, default, new HttpClient(server), null, _ => m.Bytes / 2, "https://models.example"));

        Assert.Contains("Parakeet test", e.Message);
        Assert.Empty(server.Asked);
    }

    /// <summary>Real Parakeet on the recording of speech (Fixtures/speech.wav). Runs when STUDYSTASH_PARAKEET_DIR names a
    /// folder with the model's files; otherwise it passes without running.</summary>
    [Fact]
    public async Task Parakeet_hears_the_words()
    {
        if (Environment.GetEnvironmentVariable("STUDYSTASH_PARAKEET_DIR") is not { Length: > 0 } folder || !Directory.Exists(folder)) return;
        using var parakeet = new ParakeetTranscriber(folder, "en");
        var t = await parakeet.TranscribeAsync(Sound.ReadWav(Path.Combine(AppContext.BaseDirectory, "Fixtures", "speech.wav")), "", "", default);
        string said = string.Join(" ", t.Segments.Select(s => s.Text)).ToLowerInvariant();
        Assert.Contains("midterm", said);
        Assert.Contains("recursion", said);
        Assert.Equal("en", t.Language);
        Assert.All(t.Segments, s => Assert.True(s.End >= s.Start));
    }
}
