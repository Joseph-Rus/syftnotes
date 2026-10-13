using System.Text.RegularExpressions;

namespace StudyStash.Core;

/// <summary>One stretch of one voice in a recording: when it starts and ends (seconds), and which voice, a number that
/// is the same for the same voice through the recording.</summary>
public sealed record VoiceTurn(double Start, double End, int Voice);

/// <summary>
/// The voices of one stretch of a lecture, told apart while the rest of it still records: who spoke when (seconds
/// into the lecture's sound), and a voice print for each voice (a direction in the voice model's space; null for one
/// too short to have one). The pieces are joined into one lecture's voices once it stops (<see cref="Speakers.Join"/>).
/// </summary>
public sealed record VoicePiece(double Start, double End, List<VoiceTurn> Turns, Dictionary<int, float[]> Prints);

/// <summary>Tells the voices in a whole recording apart, on this computer.</summary>
public interface ISpeakerLabeler : IDisposable
{
    /// <summary>16 kHz mono samples of the whole recording to who spoke when.</summary>
    Task<IReadOnlyList<VoiceTurn>> ListenAsync(float[] samples, CancellationToken stop);

    /// <summary>A voice print for each voice in <paramref name="turns"/>, from its own stretches of
    /// <paramref name="samples"/>: what lets one voice be found again in the next piece of the lecture. None by default
    /// (each piece's voices then stay apart).</summary>
    Task<Dictionary<int, float[]>> PrintsAsync(float[] samples, IReadOnlyList<VoiceTurn> turns, CancellationToken stop) =>
        Task.FromResult(new Dictionary<int, float[]>());
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

    /// <summary>Two voices in different pieces of a lecture are one person when their prints point this nearly the same
    /// way (cosine similarity, from -1 to 1). Measured on a 68-minute lecture's first two ten-minute pieces: the lecturer
    /// with themself 0.96 (their second voice 0.92); different people 0.05 to 0.60, even voices one piece told apart.</summary>
    public const double SamePrint = 0.75;

    /// <summary>
    /// The voices of a lecture's pieces as one lecture's: each piece's voices are numbered apart from every other's, then
    /// the two groups most alike are joined while their prints (on average) are at least <see cref="SamePrint"/> alike,
    /// never two voices from the same piece (the piece already told them apart). A voice without a print stays as it is.
    /// </summary>
    public static List<VoiceTurn> Join(IReadOnlyList<VoicePiece> pieces)
    {
        var groups = new List<(HashSet<int> Pieces, List<float[]> Prints, int Id)>();
        var idOf = new Dictionary<(int Piece, int Voice), int>();
        for (int p = 0; p < pieces.Count; p++)
            foreach (int v in pieces[p].Turns.Select(t => t.Voice).Distinct())
            {
                int id = groups.Count + 1;
                idOf[(p, v)] = id;
                groups.Add(([p], pieces[p].Prints.TryGetValue(v, out var print) && print.Length > 0 ? [print] : [], id));
            }
        while (true)
        {
            int bestA = -1, bestB = -1;
            double best = SamePrint;
            for (int a = 0; a < groups.Count; a++)
                for (int b = a + 1; b < groups.Count; b++)
                {
                    if (groups[a].Prints.Count == 0 || groups[b].Prints.Count == 0 || groups[a].Pieces.Overlaps(groups[b].Pieces)) continue;
                    double alike = groups[a].Prints.SelectMany(x => groups[b].Prints, Cosine).Average();
                    if (alike >= best) (best, bestA, bestB) = (alike, a, b);
                }
            if (bestA < 0) break;
            var (into, from) = (groups[bestA], groups[bestB]);
            into.Pieces.UnionWith(from.Pieces);
            into.Prints.AddRange(from.Prints);
            foreach (var key in idOf.Where(kv => kv.Value == from.Id).Select(kv => kv.Key).ToList()) idOf[key] = into.Id;
            groups.RemoveAt(bestB);
        }
        return [.. pieces.SelectMany((piece, p) => piece.Turns.Select(t => t with { Voice = idOf[(p, t.Voice)] })).OrderBy(t => t.Start)];
    }

    /// <summary>How nearly two prints point the same way, from -1 to 1.</summary>
    public static double Cosine(float[] a, float[] b)
    {
        double dot = 0, na = 0, nb = 0;
        for (int i = 0; i < Math.Min(a.Length, b.Length); i++)
        {
            dot += a[i] * b[i];
            na += a[i] * a[i];
            nb += b[i] * b[i];
        }
        return na == 0 || nb == 0 ? 0 : dot / Math.Sqrt(na * nb);
    }

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
