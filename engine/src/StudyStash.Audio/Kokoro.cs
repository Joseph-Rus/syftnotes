using SherpaOnnx;
using StudyStash.Core.Study;

namespace StudyStash.Audio;

/// <summary>
/// Kokoro (hexgrad's Kokoro-82M v1.0, Apache-2.0) through sherpa-onnx: Listen's downloadable voices, far more natural
/// than say, Windows' speech or espeak. sherpa-onnx's 8-bit conversion, with only what English needs: the model,
/// its voices, the US English lexicon and seven of espeak-ng's files for words the lexicon hasn't got, about 149 MB.
/// Chosen over Piper, whose voices sound flatter, whose maintained code is now GPL and whose voices each have their
/// own licence (some only for research). On an M3 Max it reads about twice as fast as it talks, on four threads; the
/// full-precision model is twice as quick but 325 MB.
/// </summary>
public static class Kokoro
{
    /// <summary>Hugging Face's copy at a fixed revision, so the files are always the ones hashed here.</summary>
    const string Source = "https://huggingface.co/csukuangfj/kokoro-int8-multi-lang-v1_0/resolve/57adb37fc0e8d4bd430cfc1b8f8c2294945b9af2";

    public const string ModelFile = "model.int8.onnx", VoicesFile = "voices.bin", TokensFile = "tokens.txt", LexiconFile = "lexicon-us-en.txt",
        DataDir = "espeak-ng-data";

    /// <summary>The script's two voices: A, who explains, is af_heart (American, a woman's); B, the classmate, is
    /// am_michael (American, a man's). Their numbers in the model's own list of speakers.</summary>
    public const int Tutor = 3, Classmate = 16;

    static readonly ModelPart[] Files =
    [
        // The small files first, so a wrong address or a full disk shows before the model.
        new(TokensFile, 687, "6ebb6bb288f20f3ae8d004d3c2ca27697da27c037d75e81a60e2a6a663f95425"),
        new("LICENSE", 11358, "cfc7749b96f63bd31c3c42b5c471bf756814053e847c10f3eb003417bc523d30"),
        new($"{DataDir}/phontab", 55796, "886f3fa402cb0ba73d483aa8ad000af47a6b7cc06293c75a97913fba68a530f6"),
        new($"{DataDir}/phonindex", 39074, "3ca7b8fa3b42624e4b0f152707e7a39245fce569aa99ea47c055d9e622fcf0c4"),
        new($"{DataDir}/phondata", 550424, "4e0288957874029a8c3c9f41a8f517ad4bf18127046decbdd4b9d1d6807ce3a3"),
        new($"{DataDir}/intonations", 2040, "3f8af65fd3eda9759a10f021d61361c120871f463515229c925995c7f90918cc"),
        new($"{DataDir}/en_dict", 166944, "71bd330ba8a2e3e8076e631508208ef49449d6147c17b7bd2b4b1e1468292e35"),
        new($"{DataDir}/lang/gmw/en", 140, "4605d5330801de3641c6e366d15f129ea1f5ffbce8722642aba01ace07ab9c83"),
        new($"{DataDir}/lang/gmw/en-US", 257, "41534c2a22df5dd4f1052ff9e1a33a3ea7bff5a26b5c02bdad5ba8ddb7524704"),
        new(LexiconFile, 5956885, "7daaab53a181be9885b853a8582bf1838186317e5dadacbcef9c426d6fa0da14"),
        new(VoicesFile, 28200960, "1c5a5b983d3d50d8586d437a51f3faa2da7919ce76a013c081e65671a3447c29"),
        new(ModelFile, 114202993, "75635ea3b85a32d00094dcdd61a258aba87c160f7c264c304a7b3ae9d734195e"),
    ];

    /// <summary>The download, as a model of several files in the models folder (not a transcription model, so it isn't
    /// in <see cref="WhisperModels.All"/>).</summary>
    public static readonly WhisperModel Model = new("kokoro", "Kokoro voices", "kokoro-82m-v1.0-int8", Files.Sum(f => f.Bytes), "",
        "Natural voices for Listen, made on your library's computer by an open-source model. Nothing you listen to leaves it.")
    {
        Source = Source,
        Parts = Files,
    };
}

/// <summary>Kokoro's voices for Listen: downloaded into the library's models folder, spoken with once they're here.</summary>
/// <param name="model">The files to fetch: <see cref="Kokoro.Model"/>, or a stand-in in tests.</param>
public sealed class KokoroVoices(string home, HttpClient? http = null, WhisperModel? model = null) : IVoicePack
{
    readonly WhisperModel model = model ?? Kokoro.Model;
    KokoroSpeech? speech;

    public string Name => "Kokoro";
    public string License => "Apache-2.0";
    public string About => model.About;
    public long Bytes => model.Bytes;
    public bool IsHere => WhisperModels.IsDownloaded(home, model);
    public long OnDisk => WhisperModels.OnDisk(home, model);
    public ISpeech Speech => speech ??= new KokoroSpeech(WhisperModels.PathFor(home, model));

    public Task DownloadAsync(IProgress<long>? progress, CancellationToken ct) =>
        ModelDownload.RunAsync(home, model, progress is null ? null : new Done(progress), ct, http);

    public void Delete()
    {
        speech?.Unload();
        WhisperModels.Delete(home, model);
        string folder = WhisperModels.PathFor(home, model);
        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
    }

    sealed class Done(IProgress<long> to) : IProgress<DownloadProgress>
    {
        public void Report(DownloadProgress value) => to.Report(value.Done);
    }
}

/// <summary>Kokoro speaking a line: loaded on the first line (about a second), kept while a listen is read, and let go
/// two minutes after the last, so the library doesn't hold its few hundred megabytes all day. One line at a time.</summary>
public sealed class KokoroSpeech(string folder) : ISpeech, IDisposable
{
    readonly object gate = new();
    OfflineTts? tts;
    Timer? idle;

    static readonly TimeSpan KeepFor = TimeSpan.FromMinutes(2);

    public (bool Ok, string Why) Ready() => File.Exists(Path.Combine(folder, Kokoro.ModelFile)) && File.Exists(Path.Combine(folder, Kokoro.VoicesFile))
        ? (true, "")
        : (false, "Kokoro's voices aren't all here: download them again in Settings.");

    public Task SayAsync(string text, string who, string wav, CancellationToken ct) => Task.Run(() =>
    {
        ct.ThrowIfCancellationRequested();
        float[] samples;
        int rate;
        lock (gate)
        {
            tts ??= Load();
            var audio = tts.Generate(text, 1.0f, who == "B" ? Kokoro.Classmate : Kokoro.Tutor);
            samples = audio.Samples;
            rate = audio.SampleRate;
            idle ??= new Timer(_ => Unload());
            idle.Change(KeepFor, Timeout.InfiniteTimeSpan);
        }
        if (samples.Length == 0) throw new InvalidOperationException("Kokoro said nothing for a line of the script.");
        Wav.Write(wav, Wav.At([.. samples.Select(s => s * short.MaxValue)], rate));
    }, ct);

    OfflineTts Load()
    {
        var config = new OfflineTtsConfig();
        var k = config.Model.Kokoro;
        k.Model = NativePath.Readable(Path.Combine(folder, Kokoro.ModelFile));
        k.Voices = NativePath.Readable(Path.Combine(folder, Kokoro.VoicesFile));
        k.Tokens = NativePath.Readable(Path.Combine(folder, Kokoro.TokensFile));
        k.Lexicon = NativePath.Readable(Path.Combine(folder, Kokoro.LexiconFile));
        k.DataDir = NativePath.Readable(Path.Combine(folder, Kokoro.DataDir));
        k.Lang = "en-us";
        config.Model.Kokoro = k;
        config.Model.NumThreads = Math.Clamp(Environment.ProcessorCount / 2, 1, 4);
        config.Model.Provider = "cpu";
        return new OfflineTts(config);
    }

    /// <summary>Lets the model go (after a while idle, or before its files are removed).</summary>
    public void Unload()
    {
        lock (gate)
        {
            tts?.Dispose();
            tts = null;
        }
    }

    public void Dispose()
    {
        idle?.Dispose();
        Unload();
    }
}
