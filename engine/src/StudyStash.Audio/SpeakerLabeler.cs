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
        if (diarizer.SampleRate != Sound.Rate) throw new InvalidOperationException($"The voice model wants {diarizer.SampleRate} Hz sound, not {Sound.Rate}.");
    }

    public Task<IReadOnlyList<VoiceTurn>> ListenAsync(float[] samples, CancellationToken stop) =>
        Task.Run<IReadOnlyList<VoiceTurn>>(() =>
        {
            stop.ThrowIfCancellationRequested();
            return [.. diarizer.Process(samples).Select(t => new VoiceTurn(t.Start, t.End, t.Speaker))];
        }, stop);

    public void Dispose() => diarizer.Dispose();
}
