using SherpaOnnx;
using StudyStash.Core;

namespace StudyStash.Audio;

/// <summary>
/// Tells the voices in a recording apart with sherpa-onnx: pyannote's segmentation finds where someone speaks, and
/// TitaNet's voice prints are grouped into people. It's given the whole recording at once and works on the processor,
/// about 6% of the recording's length on an Apple silicon Mac (a lecture of an hour in about four minutes).
/// </summary>
public sealed class SherpaSpeakerLabeler : ISpeakerLabeler
{
    public const string Segmentation = "segmentation.onnx", Embedding = "embedding.onnx";

    /// <summary>How different two voice prints may be and still be one person. Lower splits one person into several,
    /// higher joins different people: 0.9 gave a lecturer and each student who asked on two lectures.</summary>
    const float SamePerson = 0.9f;

    readonly OfflineSpeakerDiarization diarizer;
    readonly SpeakerEmbeddingExtractor prints;

    /// <summary>A voice's print is the average of its longest stretches, up to this many, each this long at least.</summary>
    const int PrintStretches = 6;
    const double ShortestStretch = 1.5;

    /// <param name="folder">Where the model's two files are.</param>
    public SherpaSpeakerLabeler(string folder, int threads = 0)
    {
        var config = new OfflineSpeakerDiarizationConfig();
        int n = threads > 0 ? threads : Math.Clamp(Environment.ProcessorCount / 2, 2, 4);
        config.Segmentation.Pyannote.Model = NativePath.Readable(Path.Combine(folder, Segmentation));
        config.Segmentation.NumThreads = n;
        config.Embedding.Model = NativePath.Readable(Path.Combine(folder, Embedding));
        config.Embedding.NumThreads = n;
        config.Clustering.Threshold = SamePerson;
        diarizer = new OfflineSpeakerDiarization(config);
        var printing = new SpeakerEmbeddingExtractorConfig { Model = config.Embedding.Model, NumThreads = n };
        prints = new SpeakerEmbeddingExtractor(printing);
        if (diarizer.SampleRate != Sound.Rate) throw new InvalidOperationException($"The voice model wants {diarizer.SampleRate} Hz sound, not {Sound.Rate}.");
    }

    public Task<IReadOnlyList<VoiceTurn>> ListenAsync(float[] samples, CancellationToken stop) =>
        Task.Run<IReadOnlyList<VoiceTurn>>(() =>
        {
            stop.ThrowIfCancellationRequested();
            return [.. diarizer.Process(samples).Select(t => new VoiceTurn(t.Start, t.End, t.Speaker))];
        }, stop);

    /// <summary>Each voice's print: the average of the prints of its longest stretches (a few seconds of one voice
    /// each), so a cough or a word across someone else's doesn't steer it.</summary>
    public Task<Dictionary<int, float[]>> PrintsAsync(float[] samples, IReadOnlyList<VoiceTurn> turns, CancellationToken stop) =>
        Task.Run(() =>
        {
            var result = new Dictionary<int, float[]>();
            foreach (var voice in turns.GroupBy(t => t.Voice))
            {
                float[]? sum = null;
                int count = 0;
                foreach (var t in voice.Where(t => t.End - t.Start >= ShortestStretch).OrderByDescending(t => t.End - t.Start).Take(PrintStretches))
                {
                    stop.ThrowIfCancellationRequested();
                    int from = Math.Clamp((int)(t.Start * Sound.Rate), 0, samples.Length), to = Math.Clamp((int)(t.End * Sound.Rate), from, samples.Length);
                    if (to - from < ShortestStretch * Sound.Rate) continue;
                    using var stream = prints.CreateStream();
                    stream.AcceptWaveform(Sound.Rate, samples[from..to]);
                    stream.InputFinished();
                    if (!prints.IsReady(stream)) continue;
                    float[] print = prints.Compute(stream);
                    double norm = Math.Sqrt(print.Sum(x => (double)x * x));
                    if (norm == 0) continue;
                    sum ??= new float[print.Length];
                    for (int i = 0; i < print.Length; i++) sum[i] += (float)(print[i] / norm);
                    count++;
                }
                if (sum is not null) result[voice.Key] = [.. sum.Select(x => x / count)];
            }
            return result;
        }, stop);

    public void Dispose()
    {
        diarizer.Dispose();
        prints.Dispose();
    }
}
