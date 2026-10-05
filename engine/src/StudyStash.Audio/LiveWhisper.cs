using System.Runtime.InteropServices;
using StudyStash.Core;
using Whisper.net;

namespace StudyStash.Audio;

/// <summary>What hears the recorder's live words on a computer: Cactus Whistle where its engine has the processor's
/// own fast code (ARM: Apple silicon, Windows on ARM), a small Whisper elsewhere (x64: Intel Macs, most PCs), where
/// Whistle's engine has no AVX code and took 1.2–1.5× the sound it heard on CI's runners.</summary>
public enum LiveEngine { None, Whistle, SmallWhisper }

public static class LiveEngines
{
    /// <summary>The live words' engine for a processor.</summary>
    public static LiveEngine EngineFor(Architecture processor) => processor switch
    {
        Architecture.Arm64 => LiveEngine.Whistle,
        Architecture.X64 => LiveEngine.SmallWhisper,
        _ => LiveEngine.None,
    };

    /// <summary>This computer's live words' engine, when this copy of the app carries it; None otherwise.</summary>
    public static LiveEngine Here => EngineFor(RuntimeInformation.ProcessArchitecture) switch
    {
        LiveEngine.Whistle when WhistleTranscriber.Available => LiveEngine.Whistle,
        LiveEngine.SmallWhisper when LiveWhisperHearer.Available => LiveEngine.SmallWhisper,
        _ => LiveEngine.None,
    };

    /// <summary>Whether the live words read a lecture language the student typed ("" or "auto": each lecture's own):
    /// Whistle reads seven, the small Whisper all of Whisper's.</summary>
    public static bool Reads(LiveEngine engine, string language) =>
        engine == LiveEngine.SmallWhisper || (engine == LiveEngine.Whistle && WhistleLanguages.Knows(language));
}

/// <summary>
/// The live words on an x64 computer: Whisper tiny (multilingual, 8-bit, 43.5 MB, carried in the x64 builds) through
/// Whisper.net, which has AVX code for the processor and uses a PC's graphics card through Vulkan as the transcript's
/// Whisper does. Its own model and processors, never the transcript's, so neither waits for the other, on two threads
/// so the transcript's Whisper keeps the rest. A short pass doesn't pay for Whisper's 30 seconds: the encoder is told
/// how much sound there is (whisper.cpp's audio context, in 5-second steps): hearing the whole 30 seconds every pass
/// cost three times as much and heard no better. Measured on two 12-minute stretches of recorded lectures, heard as
/// the recorder hears them (a pass a second, 3 seconds of context): 58% and 48% of words different from large-v3, where
/// Whistle's live words had 34% and 29%; the 5-bit tiny was 4 points worse at the same cost, and base (5-bit, 60 MB)
/// 5 points better at 1.5 times the cost (engine/tools/TranscribeBench livewords).
/// </summary>
public sealed class LiveWhisperHearer : IWordHearer, IDisposable
{
    public const string File = "live-whisper-tiny-q8_0.bin";
    /// <summary>Encoder frames per second of sound (Whisper's 1500 for 30 s).</summary>
    const int FramesPerSecond = 50;
    const double Step = 5;
    /// <summary>More tokens a second than any lecturer says (about four).</summary>
    const int TokensPerSecond = 7;

    readonly WhisperFactory factory;
    readonly int threads;
    readonly Dictionary<(string Language, int Seconds), WhisperProcessor> processors = [];
    readonly Lock gate = new();

    /// <summary>The model the x64 builds carry (models/ beside the app).</summary>
    public static string BundledModel => Path.Combine(AppContext.BaseDirectory, "models", File);

    public static bool Available => System.IO.File.Exists(BundledModel);

    /// <param name="threads">0: two, or one on a computer with fewer than four.</param>
    public LiveWhisperHearer(string modelPath, int threads = 0, bool gpu = true)
    {
        factory = WhisperFactory.FromPath(modelPath, new WhisperFactoryOptions { UseGpu = gpu });
        this.threads = threads > 0 ? threads : Environment.ProcessorCount >= 4 ? 2 : 1;
    }

    public HeardWords Hear(float[] samples, string language, string keywords)
    {
        if (samples.Length == 0) return new([], "");
        double seconds = samples.Length / (double)Sound.Rate;
        // The encoder hears the sound rounded up to 5 seconds, so a few processors serve every pass.
        int bucket = (int)Math.Min(30, Math.Ceiling(seconds / Step) * Step);
        string lang = language.Length > 0 ? language : "auto";
        lock (gate)
        {
            if (!processors.TryGetValue((lang, bucket), out var p))
            {
                // One go at each pass, never Whisper's retries at higher temperatures, and no more words than a
                // lecturer says in that time: a pass that would run away repeating itself stops instead.
                var builder = factory.CreateBuilder().WithLanguage(lang).WithThreads(threads).WithNoSpeechThreshold(0.6f)
                    .WithTokenTimestamps().WithNoContext()
                    .WithTemperature(0).WithTemperatureInc(0).WithMaxTokensPerSegment(TokensPerSecond * bucket + 8);
                if (bucket < 30) builder = builder.WithAudioContextSize(bucket * FramesPerSecond);
                processors[(lang, bucket)] = p = builder.Build();
            }
            var words = new List<TimedWord>();
            string found = "";
            foreach (var segment in p.ProcessAsync(samples).ToBlockingEnumerable())
            {
                if (found.Length == 0 && !string.IsNullOrEmpty(segment.Language)) found = segment.Language;
                Words(segment, words, seconds);
            }
            return new(words, lang == "auto" ? found : lang);
        }
    }

    /// <summary>A segment's tokens as words: a token starting with a space starts a word; Whisper's own markers
    /// ("[_BEG_]", "[_TT_150]", "&lt;|endoftext|&gt;") aren't words. Token times are hundredths of a second.</summary>
    static void Words(SegmentData segment, List<TimedWord> words, double length)
    {
        string text = "";
        double start = 0, end = 0, probability = 1;
        void Flush()
        {
            string word = text.Trim();
            if (word.Length > 0 && TimedText.Tidy(word).Length > 0) words.Add(new TimedWord(start, Math.Max(start, Math.Min(end, length)), word, probability));
            text = "";
        }
        foreach (var t in segment.Tokens)
        {
            string piece = t.Text ?? "";
            if (piece.StartsWith("[_", StringComparison.Ordinal) || piece.StartsWith("<|", StringComparison.Ordinal)) continue;
            if (piece.StartsWith(' ') && text.Length > 0) Flush();
            if (text.Length == 0)
            {
                start = t.Start / 100.0;
                probability = 1;
            }
            text += piece;
            end = t.End / 100.0;
            probability = Math.Min(probability, t.Probability);
        }
        Flush();
    }

    public void Dispose()
    {
        lock (gate)
        {
            foreach (var p in processors.Values) p.Dispose();
            processors.Clear();
        }
        factory.Dispose();
    }
}
