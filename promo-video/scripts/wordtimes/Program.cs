// Where each word of a voice take starts and ends: whisper.cpp (through Whisper.net) with token-level timestamps, and
// the start of each word from DTW alignment (the model's own alignment heads) when the model is one it knows.
//
//   dotnet run -c Release --project scripts/wordtimes -- <16 kHz mono wav> [model.bin] [--out words.json] [--prompt "…"]
//
// Prints (or writes to --out) [{"word": "Study", "start": 1.02, "end": 1.31, "p": 0.98}, …], seconds into the file;
// p is Whisper's confidence. --no-dtw uses the token timestamps alone; --tokens lists every token's times on stderr.
// The model defaults to ~/.study-stash/models/ggml-large-v3.bin, which is only read. Needs the .NET 10 SDK.
using System.Diagnostics;
using System.Text.Json;
using Whisper.net;

var rest = args.ToList();
string? Option(string flag)
{
    var i = rest.IndexOf(flag);
    if (i < 0 || i + 1 >= rest.Count) return null;
    var value = rest[i + 1];
    rest.RemoveRange(i, 2);
    return value;
}
var outPath = Option("--out");
var prompt = Option("--prompt");
var noDtw = rest.Remove("--no-dtw");
var dumpTokens = rest.Remove("--tokens");  // every token's times, to see how the words were put together
if (rest.Count < 1 || rest[0].StartsWith("-"))
{
    Console.Error.WriteLine("usage: WordTimes <16 kHz mono wav> [model.bin] [--out words.json] [--prompt text] [--no-dtw] [--tokens]");
    return 2;
}
var wav = rest[0];
var model = rest.Count > 1
    ? rest[1]
    : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".study-stash", "models", "ggml-large-v3.bin");

// DTW needs the model's alignment heads: known for every standard Whisper model, picked from the file's name.
var preset = Preset(Path.GetFileName(model).ToLowerInvariant());
var dtw = !noDtw && preset != WhisperAlignmentHeadsPreset.None;
var clock = Stopwatch.StartNew();
using var factory = WhisperFactory.FromPath(model, new WhisperFactoryOptions { UseDtwTimeStamps = dtw, HeadsPreset = preset });
var builder = factory.CreateBuilder()
    .WithLanguage("en")
    .WithThreads(Math.Clamp(Environment.ProcessorCount, 1, 8))
    .WithTokenTimestamps()
    .SplitOnWord();
if (!string.IsNullOrWhiteSpace(prompt)) builder = builder.WithPrompt(prompt);
await using var processor = builder.Build();

// Every text token in order, with its segment, its timestamp-token times (t0, t1) and its DTW time (where the
// alignment reaches it: the better guess at when it starts).
var tokens = new List<(string Text, int Seg, double T0, double T1, double Dtw, float P)>();
float[] samples;
await using (var file = File.OpenRead(wav))
{
    var parser = new Whisper.net.Wave.WaveParser(file);
    await parser.InitializeAsync();
    if (parser.SampleRate != 16000) { Console.Error.WriteLine($"{wav} is {parser.SampleRate} Hz; Whisper needs 16 kHz (ffmpeg -ar 16000 -ac 1)."); return 2; }
    samples = await parser.GetAvgSamplesAsync();
}
// Whisper reads 30 s at a time and can drop a sentence that straddles the end of a window, so a long take is read in
// pieces of at most ~25 s, each cut at the quietest moment it can find (between lines, in a voice take).
var seg = 0;
foreach (var (from, count) in Pieces(samples))
{
    await foreach (var segment in processor.ProcessAsync(samples.AsMemory(from, count)))
    {
        var first = true;
        var offset = from / 16000.0;
        foreach (var t in segment.Tokens ?? [])
        {
            var text = t.Text ?? "";
            if (text.Length == 0 || text.StartsWith("[_") || text.StartsWith("<|")) continue;  // timestamps, BEG, EOT
            // A segment always starts a new word, whether or not its first token carries the leading space.
            if (first && !text.StartsWith(' ')) text = " " + text;
            first = false;
            tokens.Add((text, seg, offset + t.Start / 100.0, offset + t.End / 100.0, offset + t.DtwTimestamp / 100.0, t.Probability));
        }
        seg++;
    }
}
if (dumpTokens)
    foreach (var t in tokens) Console.Error.WriteLine($"{t.Text,-16} seg {t.Seg,3}  t0 {t.T0,6:F2}  t1 {t.T1,6:F2}  dtw {t.Dtw,6:F2}  p {t.P:F2}");

// Words: a token that starts with a space starts one; the rest (word pieces, punctuation) join the word before.
// A word starts at its first token's time. It ends where the next token in its segment starts (the full stop or
// comma after it, or the next word), on the same clock as its start; the last word of a segment, which has no next
// token, lasts as long as its tokens' t0…t1. Without DTW, both ends are the tokens' t0 and t1.
double Start(int i) => dtw && tokens[i].Dtw >= 0 ? tokens[i].Dtw : tokens[i].T0;
var words = new List<Word>();
for (var i = 0; i < tokens.Count;)
{
    var j = i + 1;
    while (j < tokens.Count && !tokens[j].Text.StartsWith(' ')) j++;  // tokens i … j-1 make the word
    var lastLetter = i;
    for (var k = i; k < j; k++) if (tokens[k].Text.Any(char.IsLetterOrDigit)) lastLetter = k;
    var start = Start(i);
    double end;
    if (!dtw) end = tokens[lastLetter].T1;
    else if (lastLetter + 1 < tokens.Count && tokens[lastLetter + 1].Seg == tokens[i].Seg) end = Start(lastLetter + 1);
    else end = start + Math.Max(0.05, tokens[lastLetter].T1 - tokens[i].T0);
    if (j < tokens.Count) end = Math.Min(end, Start(j));
    end = Math.Max(end, start + 0.02);
    var text = string.Concat(tokens.Skip(i).Take(j - i).Select(t => t.Text)).Trim();
    var p = tokens.Skip(i).Take(j - i).Average(t => t.P);
    if (text.Length > 0) words.Add(new Word(text, Math.Round(start, 3), Math.Round(end, 3), MathF.Round(p, 3)));
    i = j;
}

var json = JsonSerializer.Serialize(words);
if (outPath is null) Console.WriteLine(json);
else File.WriteAllText(outPath, json + "\n");
Console.Error.WriteLine($"wordtimes: {words.Count} words in {clock.Elapsed.TotalSeconds:F1} s; starts from {(dtw ? $"DTW ({preset})" : "token timestamps")}; {WhisperFactory.GetRuntimeInfo()?.Trim()}");
return 0;

// Where to cut a take into pieces Whisper reads whole: each piece 12–25 s, ending in the quietest 200 ms it can.
static IEnumerable<(int From, int Count)> Pieces(float[] x)
{
    const int rate = 16000, hop = 160, span = 20;  // 10 ms frames, a 200 ms window
    var frames = x.Length / hop;
    var energy = new double[frames + 1];  // running sum of each frame's energy
    for (var f = 0; f < frames; f++)
    {
        double e = 0;
        for (var k = f * hop; k < (f + 1) * hop; k++) e += x[k] * x[k];
        energy[f + 1] = energy[f] + e;
    }
    var at = 0;
    while (x.Length - at > 27 * rate)
    {
        int lo = (at + 12 * rate) / hop, hi = Math.Min((at + 25 * rate) / hop, frames - 3 * rate / hop) - span;
        var best = lo;
        for (var f = lo; f <= hi; f++)
            if (energy[f + span] - energy[f] < energy[best + span] - energy[best]) best = f;
        var cut = (best + span / 2) * hop;
        yield return (at, cut - at);
        at = cut;
    }
    yield return (at, x.Length - at);
}

static WhisperAlignmentHeadsPreset Preset(string name) => name switch
{
    _ when name.Contains("large-v3-turbo") => WhisperAlignmentHeadsPreset.LargeV3Turbo,
    _ when name.Contains("large-v3") => WhisperAlignmentHeadsPreset.LargeV3,
    _ when name.Contains("large-v2") => WhisperAlignmentHeadsPreset.LargeV2,
    _ when name.Contains("large-v1") || name.Contains("large.") => WhisperAlignmentHeadsPreset.LargeV1,
    _ when name.Contains("medium.en") => WhisperAlignmentHeadsPreset.MediumEn,
    _ when name.Contains("medium") => WhisperAlignmentHeadsPreset.Medium,
    _ when name.Contains("small.en") => WhisperAlignmentHeadsPreset.SmallEn,
    _ when name.Contains("small") => WhisperAlignmentHeadsPreset.Small,
    _ when name.Contains("base.en") => WhisperAlignmentHeadsPreset.BaseEn,
    _ when name.Contains("base") => WhisperAlignmentHeadsPreset.Base,
    _ when name.Contains("tiny.en") => WhisperAlignmentHeadsPreset.TinyEn,
    _ when name.Contains("tiny") => WhisperAlignmentHeadsPreset.Tiny,
    _ => WhisperAlignmentHeadsPreset.None,
};

record Word(string word, double start, double end, float p);
