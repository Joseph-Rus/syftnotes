using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using StudyStash.App.Views;

namespace StudyStash.App.Tests;

/// <summary>A picture of an Ask answer that holds a display formula followed by a full stop, with the chips of its quotes (one
/// holding formula source, one that is only a formula), Mac look, light. Drawn off screen into STUDYSTASH_SHOTS; only with
/// STUDYSTASH_FILM=1. The answer is a stand-in's: no AI is asked.</summary>
public class AskMathShots
{
    const string Formula = @"\text{Attention}(Q,K,V)=\text{softmax}\left(\frac{QK^T}{\sqrt{d_k}}\right)V";

    static readonly string Notes = "## Summary\nHow self-attention lets every token look at every other token.\n\n### Scores\n"
        + "- Each token's query is compared with every other token's key using a dot product. Those scores go through a softmax and are used to weight the values: $" + Formula + "$\n"
        + "- The scaling term is $\\frac{QK^T}{\\sqrt{d_k}}$ and nothing else.\n- Attention on its own ignores word order, so a position signal is added to each token's embedding.";

    static readonly string Answer = "In self-attention, every token gets a query, a key and a value, and each token's query is compared with every other token's key using a dot product. Those scores go through a softmax and are used to weight the values: $$" + Formula + "$$. Attention on its own ignores word order, so a position signal is added to each token's embedding.";

    static readonly string[] Quotes =
    [
        Formula,
        "Those scores go through a softmax and are used to weight the values",
        "Attention on its own ignores word order, so a position signal is added to each token's embedding.",
        "$\\frac{QK^T}{\\sqrt{d_k}}$",
    ];

    [AvaloniaFact]
    public async Task An_answer_with_a_formula_and_its_chips()
    {
        if (Environment.GetEnvironmentVariable("STUDYSTASH_FILM") != "1") return;
        var model = await AskHighlightsShots.Page(Notes, Answer, Quotes, "How does self-attention work?");
        AskHighlightsShots.Take(new MacLibrary { DataContext = model }, SkinKind.Mac, ThemeVariant.Light, Environment.GetEnvironmentVariable("STUDYSTASH_NAME") ?? "mac-light-answer-formula");
    }
}
