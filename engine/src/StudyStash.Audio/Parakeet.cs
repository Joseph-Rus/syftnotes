using System.Runtime.InteropServices;
using System.Text;
using SherpaOnnx;
using StudyStash.Core;

namespace StudyStash.Audio;

/// <summary>The languages Parakeet v3 reads: 25 European ones. A lecture in another needs Whisper.</summary>
public static class ParakeetLanguages
{
    static readonly Dictionary<string, string> Known = new()
    {
        ["bg"] = "bulgarian", ["hr"] = "croatian", ["cs"] = "czech", ["da"] = "danish", ["nl"] = "dutch", ["en"] = "english",
        ["et"] = "estonian", ["fi"] = "finnish", ["fr"] = "french", ["de"] = "german", ["el"] = "greek", ["hu"] = "hungarian",
        ["it"] = "italian", ["lv"] = "latvian", ["lt"] = "lithuanian", ["mt"] = "maltese", ["pl"] = "polish", ["pt"] = "portuguese",
        ["ro"] = "romanian", ["sk"] = "slovak", ["sl"] = "slovenian", ["es"] = "spanish", ["sv"] = "swedish", ["ru"] = "russian",
        ["uk"] = "ukrainian",
    };

    /// <summary>Whether a lecture language the student typed ("en", "pt-BR", "Spanish"; "" or "auto" for each lecture's
    /// own) is one Parakeet reads. It finds the language among its 25 itself, so "" is fine.</summary>
    public static bool Knows(string language)
    {
        string l = language.Trim().ToLowerInvariant();
        if (l is "" or "auto") return true;
        return Known.ContainsKey(l.Split('-', '_')[0]) || Known.ContainsValue(l);
    }
}

/// <summary>
/// NVIDIA's Parakeet TDT v3 (through sherpa-onnx) on this computer's processor. It writes punctuation itself, has no
/// habit of inventing words over silence, and finds the language of what it hears among the 25 it knows, so
/// <c>prompt</c> and <c>language</c> aren't used.
/// </summary>
public sealed class ParakeetTranscriber : ITranscriber
{
    public const string Encoder = "encoder.onnx", Decoder = "decoder.onnx", Joiner = "joiner.onnx", Tokens = "tokens.txt";

    /// <summary>A pause this long between two words starts a new line, punctuation or not.</summary>
    const double LongPause = 1.2;
    /// <summary>The most a word is taken to last, from where it begins.</summary>
    const double WordEnds = 0.4;
    /// <summary>A sentence ends a line only after this many words, so "Dr." or "e.g." doesn't make a line of its own.</summary>
    const int FewestWords = 3;

    readonly OfflineRecognizer recognizer;
    readonly string fixedLanguage;

    /// <param name="folder">Where the model's four files are.</param>
    /// <param name="language">A language the lecture is fixed to (reported back as its language), or "" for none.</param>
    public ParakeetTranscriber(string folder, string language = "", int threads = 0)
    {
        var config = new OfflineRecognizerConfig();
        config.ModelConfig.Transducer.Encoder = Readable(Path.Combine(folder, Encoder));
        config.ModelConfig.Transducer.Decoder = Readable(Path.Combine(folder, Decoder));
        config.ModelConfig.Transducer.Joiner = Readable(Path.Combine(folder, Joiner));
        config.ModelConfig.Tokens = Readable(Path.Combine(folder, Tokens));
        config.ModelConfig.ModelType = "nemo_transducer";
        config.ModelConfig.Provider = "cpu";
        config.ModelConfig.NumThreads = threads > 0 ? threads : Math.Clamp(Environment.ProcessorCount / 2, 2, 8);
        recognizer = new OfflineRecognizer(config);
        fixedLanguage = language is "auto" ? "" : language;
    }

    public Task<Transcription> TranscribeAsync(float[] samples, string prompt, string language, CancellationToken stop) =>
        Task.Run(() =>
        {
            stop.ThrowIfCancellationRequested();
            using var stream = recognizer.CreateStream();
            stream.AcceptWaveform(Sound.Rate, samples);
            recognizer.Decode(stream);
            var heard = stream.Result;
            var lines = Lines(heard.Tokens, heard.Timestamps, heard.Durations, samples.Length / (double)Sound.Rate);
            return new Transcription(lines, fixedLanguage.Length > 0 ? fixedLanguage : language);
        }, stop);

    /// <summary>
    /// The words of a piece as lines with their times: a line ends at the end of a sentence (once it has a few words)
    /// and at a long pause. <paramref name="tokens"/> are word pieces (a leading ▁ starts a word), <paramref name="starts"/>
    /// when each begins in the piece and <paramref name="durations"/> how long it lasts (null when the model doesn't say).
    /// </summary>
    internal static List<Spoken> Lines(string[] tokens, float[]? starts, float[]? durations, double length)
    {
        var lines = new List<Spoken>();
        var text = new StringBuilder();
        double from = 0, to = 0;
        int words = 0;
        bool sentenceEnded = false;

        void Flush()
        {
            if (text.ToString().Trim() is { Length: > 0 } line) lines.Add(new Spoken(from, Math.Max(to, from), line));
            text.Clear();
            words = 0;
            sentenceEnded = false;
        }

        for (int i = 0; i < tokens.Length; i++)
        {
            string piece = tokens[i].Replace('▁', ' ');
            bool startsWord = piece.StartsWith(' ');
            double start = starts is not null && i < starts.Length ? starts[i] : 0;
            // A duration runs on through the quiet after the word (until the next one), so a word ends soon after it begins.
            double end = start + (durations is not null && i < durations.Length && durations[i] > 0 ? Math.Min(durations[i], WordEnds) : WordEnds);
            if (startsWord && text.Length > 0 && (sentenceEnded || start - to >= LongPause)) Flush();
            if (text.Length == 0) from = start;
            text.Append(piece);
            to = Math.Min(end, length);
            if (startsWord) words++;
            string ended = piece.TrimEnd();
            sentenceEnded = words >= FewestWords && ended.Length > 0 && ended[^1] is '.' or '?' or '!';
        }
        Flush();
        return lines;
    }

    /// <summary>sherpa-onnx takes file names as ANSI text on Windows, so a folder with a name the code page can't
    /// spell (a user called Zoë on some computers) would not open: its short name, all plain letters, does.</summary>
    static string Readable(string path)
    {
        if (!OperatingSystem.IsWindows() || path.All(c => c < 128)) return path;
        var buffer = new StringBuilder(520);
        if (GetShortPathName(path, buffer, buffer.Capacity) > 0 && buffer.ToString().All(c => c < 128)) return buffer.ToString();
        throw new IOException($"Study Stash can't open the model in {path}: the folder's name has letters Windows can't pass on. Move Study Stash's folder to one with a plain name.");
    }

    [DllImport("kernel32", CharSet = CharSet.Unicode, EntryPoint = "GetShortPathNameW")]
    static extern int GetShortPathName(string path, StringBuilder shortPath, int length);

    public void Dispose() => recognizer.Dispose();
}
