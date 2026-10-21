using System.Globalization;
using System.Text.RegularExpressions;

namespace StudyStash.Core.Ai;

/// <summary>
/// How much of a conversation goes with a follow-up question. Every AI is sent the earlier turns again as text, never
/// asked to pick up its own session: Ask finds fresh passages for each question, answers in JSON, can be asked of
/// another AI each time and falls back to the free AI, none of which a resumed session would follow. So the turns sent
/// are kept short: the last six, each cut to about 4,000 characters, and fewer and shorter for the free AI, whose
/// model reads far less at once.
/// </summary>
public static partial class AskMemory
{
    /// <summary>The most turns that go with a question.</summary>
    public const int Turns = 6;

    /// <summary>The most characters of each turn, its question and answer together.</summary>
    public const int TurnChars = 4000;

    /// <summary>The last <paramref name="turns"/> turns that said something, each cut to <paramref name="chars"/>
    /// characters: the question keeps its start (a quarter of the room, or all it needs), the answer its start in the
    /// rest.</summary>
    public static List<AskTurn> Last(IEnumerable<AskTurn?> history, int turns = Turns, int chars = TurnChars)
    {
        var said = history.OfType<AskTurn>()
            .Select(t => t with { Question = Py.Strip(t.Question ?? ""), Answer = Py.Strip(t.Answer ?? ""), Sources = [.. (t.Sources ?? []).OfType<AskSource>()] })
            .Where(t => t.Question.Length > 0 && t.Answer.Length > 0).ToList();
        return [.. said.Skip(Math.Max(0, said.Count - turns)).Select(t => Cut(t, chars))];
    }

    static AskTurn Cut(AskTurn t, int chars)
    {
        if (t.Question.Length + t.Answer.Length <= chars) return t;
        string question = Head(t.Question, Math.Max(chars / 4, chars - t.Answer.Length));
        return t with { Question = question, Answer = Head(t.Answer, chars - question.Length) };
    }

    static string Head(string s, int n) => s.Length <= n ? s : n <= 1 ? "" : s[..(n - 1)].TrimEnd() + "…";

    /// <summary>What one AI is given of the conversation: Claude, ChatGPT and Gemini the last six turns; the free AI
    /// four, shorter; its small model (on a computer with little memory) the last two, shorter still.</summary>
    public static List<AskTurn> For(string engine, string model, IEnumerable<AskTurn?>? history)
    {
        if (history is null) return [];
        var (turns, chars) = engine != "ollama" ? (Turns, TurnChars) : IsSmall(model) ? (2, 600) : (4, 1500);
        return Last(history, turns, chars);
    }

    /// <summary>A local model small enough that it reads little at once: the small free model, or any under 4 billion
    /// parameters by its name ("qwen3:1.7b", "llama3.2:1b").</summary>
    public static bool IsSmall(string model) =>
        model == Ollama.SmallModel || (Billions().Match(model) is { Success: true } m
            && double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double b) && b < 4);

    [GeneratedRegex(@":(\d+(?:\.\d+)?)b\b", RegexOptions.IgnoreCase)]
    private static partial Regex Billions();
}
