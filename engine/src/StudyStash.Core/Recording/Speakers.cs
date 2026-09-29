using System.Text.RegularExpressions;

namespace StudyStash.Core;

/// <summary>One stretch of one voice in a recording: when it starts and ends (seconds), and which voice, a number that
/// is the same for the same voice through the recording.</summary>
public sealed record VoiceTurn(double Start, double End, int Voice);

/// <summary>Tells the voices in a whole recording apart, on this computer.</summary>
public interface ISpeakerLabeler : IDisposable
{
    /// <summary>16 kHz mono samples of the whole recording to who spoke when.</summary>
    Task<IReadOnlyList<VoiceTurn>> ListenAsync(float[] samples, CancellationToken stop);
}

/// <summary>Who said each line of a lecture, from who spoke when.</summary>
public static partial class Speakers
{
    /// <summary>A voice that speaks less than this in all (seconds) is a cough or a noise, not a person: its lines
    /// take the voice around them.</summary>
    public const double LeastTalk = 6;

    /// <summary>A voice with at least this share of all the talk is the lecturer's. One lecturer often comes out as two
    /// or three voices (the room, the distance from the microphone and the way they face change), and in a lecture
    /// nobody else speaks that much, so every voice this big is Speaker 1. Measured on two recorded lectures: the
    /// lecturer's second voice held 9% and 16% of the talk, the students' 1% to 5% each.</summary>
    public const double LecturerShare = 0.05;

    /// <summary>What the note-writing model is told when a transcript has speakers in it.</summary>
    public const string NotesHint = "(Lines that begin \"Speaker 2:\" and so on mark where a voice other than the lecturer's seems to speak, "
        + "usually a student asking or answering; Speaker 1 is the lecturer. The labels can be wrong, so use them only as a hint to keep "
        + "what the lecturer taught apart from what a student said, and leave them out of the notes.)";

    [GeneratedRegex(@"(?m)^(?:\[[\d:]+\] )?Speaker \d+: ")]
    private static partial Regex Label();

    /// <summary>A transcript that says who's speaking (with its times, as the library keeps it, or without).</summary>
    public static bool HasLabels(string transcript) => Label().IsMatch(transcript);

    /// <summary>
    /// <paramref name="lines"/> with the voice that said each: the one that overlaps it most. Every voice with at least
    /// <see cref="LecturerShare"/> of the talk is Speaker 1, the lecturer; the smaller ones that spoke for at least
    /// <see cref="LeastTalk"/> seconds are Speaker 2, 3… by how much each spoke; a voice with less than that is a
    /// noise, and a line with no voice takes the one before it. A lecture with nobody but the lecturer comes back
    /// with no speakers numbered: there's nothing to tell apart.
    /// </summary>
    public static List<Spoken> Label(IReadOnlyList<Spoken> lines, IReadOnlyList<VoiceTurn> turns)
    {
        var voice = new int[lines.Count];
        for (int i = 0; i < lines.Count; i++)
        {
            var overlap = new Dictionary<int, double>();
            foreach (var t in turns)
            {
                double o = Math.Min(t.End, lines[i].End) - Math.Max(t.Start, lines[i].Start);
                if (o > 0) overlap[t.Voice] = overlap.GetValueOrDefault(t.Voice) + o;
            }
            voice[i] = overlap.Count == 0 ? -1 : overlap.MaxBy(kv => kv.Value).Key;
        }

        var talk = new Dictionary<int, double>();
        for (int i = 0; i < lines.Count; i++)
            if (voice[i] >= 0) talk[voice[i]] = talk.GetValueOrDefault(voice[i]) + Math.Max(0.5, lines[i].End - lines[i].Start);
        double total = talk.Values.Sum();
        var ranked = talk.OrderByDescending(kv => kv.Value).ToList();
        // The lecturer: the voice that talks most, and any other as big as a lecturer's second voice can be.
        var number = new Dictionary<int, int>();
        foreach (var (v, seconds) in ranked)
            if (number.Count == 0 || seconds >= LecturerShare * total) number[v] = 1;
        int next = 2;
        foreach (var (v, seconds) in ranked)
            if (!number.ContainsKey(v) && seconds >= LeastTalk) number[v] = next++;
        if (next == 2) return [.. lines.Select(l => l with { Speaker = 0 })];

        var numbers = new int[lines.Count];
        for (int i = 0; i < lines.Count; i++) numbers[i] = voice[i] >= 0 && number.TryGetValue(voice[i], out int n) ? n : 0;
        // A line with no voice of its own (a quiet stretch, a noise) goes on with the one before it; the first ones, with the next.
        for (int i = 1; i < numbers.Length; i++)
            if (numbers[i] == 0) numbers[i] = numbers[i - 1];
        for (int i = numbers.Length - 2; i >= 0; i--)
            if (numbers[i] == 0) numbers[i] = numbers[i + 1];
        return [.. lines.Select((l, i) => l with { Speaker = numbers[i] })];
    }
}
