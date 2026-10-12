using System.Text;
using System.Text.RegularExpressions;
using StudyStash.Core.Canvas;

namespace StudyStash.App.ViewModels;

/// <summary>
/// A class's name as the app shows it. The library keeps what a school's catalogue calls it ("Theory of Computation
/// and Compilers(CSCI4150.A)"); on screen it's the name alone (<see cref="Display"/>), with the code ("CSCI 4150")
/// only on the class's own page and in tooltips (<see cref="Code"/>), and in tight spots (a class meeting in the
/// week, the recorder) a word or two (<see cref="Short"/>).
/// </summary>
public static partial class ClassNames
{
    /// <summary>The name without the registrar's code on the end: "Theory of Computation and Compilers".</summary>
    public static string Display(string name) => string.IsNullOrWhiteSpace(name) ? name : CourseNames.Title(name);

    /// <summary>The course code in a name, spaced ("CSCI 4150" from "…(CSCI4150.A)"), or "" when it has none or is
    /// nothing but one (a class called "CS 101" needs no code beside it).</summary>
    public static string Code(string name)
    {
        string code = CourseNames.ShortCode("", name ?? "");
        return code.Length == 0 || string.Equals(Display(name ?? ""), code, StringComparison.OrdinalIgnoreCase) ? "" : code;
    }

    [GeneratedRegex(@"^(?:intro(?:duction)?|principles|fundamentals|foundations|elements|topics|survey|studies)\s+(?:to|of|in)\s+", RegexOptions.IgnoreCase)]
    private static partial Regex Opening();

    static readonly HashSet<string> Joining = new(StringComparer.OrdinalIgnoreCase) { "of", "and", "for", "in", "to", "the", "&", "with", "on", "a", "an" };

    /// <summary>
    /// A word or two that still says which class: the name up to its first joining word, two words at most ("Theory"
    /// from "Theory of Computation and Compilers", "Data Mining", "Organic Chemistry" from "Introduction to Organic
    /// Chemistry"). A name that's a code ("CS 101") stays whole.
    /// </summary>
    public static string Short(string name)
    {
        string full = Display(name ?? "");
        string rest = Opening().Replace(full, "");
        var words = rest.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var kept = words.TakeWhile(w => !Joining.Contains(w)).Take(2).ToList();
        return kept.Count == 0 ? full : string.Join(' ', kept);
    }
}

/// <summary>
/// A sentence with maths in it, for a line of plain text (a lecture's summary in a list): the dollar signs go, and
/// simple LaTeX becomes the characters it stands for (<c>$c^T x$</c> is cᵀx, <c>$x_i^2$</c> is xᵢ², <c>\le</c> is ≤).
/// What has no plain form is left readable, never as raw LaTeX with its dollars. The notes themselves typeset maths
/// properly; this is for where there's only one line.
/// </summary>
public static partial class PlainMath
{
    [GeneratedRegex(@"\$\$(.+?)\$\$|\$(.+?)\$|\\\((.+?)\\\)", RegexOptions.Singleline)]
    private static partial Regex Spans();

    [GeneratedRegex(@"\\(?:text|mathrm|mathbf|mathit|operatorname|mathcal|boldsymbol)\s*\{([^{}]*)\}")]
    private static partial Regex Wrapped();

    [GeneratedRegex(@"\\(?:d|t)?frac\s*\{([^{}]*)\}\s*\{([^{}]*)\}")]
    private static partial Regex Fraction();

    [GeneratedRegex(@"\\sqrt\s*\{([^{}]*)\}")]
    private static partial Regex Root();

    [GeneratedRegex(@"\\mathbb\s*\{([A-Z])\}")]
    private static partial Regex Blackboard();

    [GeneratedRegex(@"([\^_])(?:\{([^{}]*)\}|(\\[A-Za-z]+|[A-Za-z0-9+\-=]))")]
    private static partial Regex Script();

    [GeneratedRegex(@"\\([A-Za-z]+)")]
    private static partial Regex Command();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex Spaces();

    [GeneratedRegex(@"([⁰¹²³⁴⁵⁶⁷⁸⁹⁺⁻⁼ⁿᵀⁱᵃˣᵏ]) (?=[A-Za-z0-9(])")]
    private static partial Regex AfterScript();

    static readonly Dictionary<string, string> Symbols = new()
    {
        ["cdot"] = "·", ["times"] = "×", ["div"] = "÷", ["pm"] = "±", ["le"] = "≤", ["leq"] = "≤", ["ge"] = "≥", ["geq"] = "≥",
        ["ne"] = "≠", ["neq"] = "≠", ["approx"] = "≈", ["equiv"] = "≡", ["to"] = "→", ["rightarrow"] = "→", ["leftarrow"] = "←",
        ["Rightarrow"] = "⇒", ["implies"] = "⇒", ["iff"] = "⇔", ["infty"] = "∞", ["in"] = "∈", ["notin"] = "∉", ["subset"] = "⊂",
        ["subseteq"] = "⊆", ["cup"] = "∪", ["cap"] = "∩", ["forall"] = "∀", ["exists"] = "∃", ["sum"] = "∑", ["prod"] = "∏",
        ["int"] = "∫", ["partial"] = "∂", ["nabla"] = "∇", ["ldots"] = "…", ["dots"] = "…", ["cdots"] = "⋯", ["neg"] = "¬",
        ["land"] = "∧", ["lor"] = "∨", ["emptyset"] = "∅", ["circ"] = "∘", ["propto"] = "∝", ["sim"] = "∼",
        ["alpha"] = "α", ["beta"] = "β", ["gamma"] = "γ", ["delta"] = "δ", ["epsilon"] = "ε", ["varepsilon"] = "ε", ["zeta"] = "ζ",
        ["eta"] = "η", ["theta"] = "θ", ["lambda"] = "λ", ["mu"] = "μ", ["nu"] = "ν", ["xi"] = "ξ", ["pi"] = "π", ["rho"] = "ρ",
        ["sigma"] = "σ", ["tau"] = "τ", ["phi"] = "φ", ["varphi"] = "φ", ["chi"] = "χ", ["psi"] = "ψ", ["omega"] = "ω",
        ["Gamma"] = "Γ", ["Delta"] = "Δ", ["Theta"] = "Θ", ["Lambda"] = "Λ", ["Pi"] = "Π", ["Sigma"] = "Σ", ["Phi"] = "Φ", ["Omega"] = "Ω",
        ["log"] = "log", ["ln"] = "ln", ["sin"] = "sin", ["cos"] = "cos", ["tan"] = "tan", ["max"] = "max", ["min"] = "min", ["lim"] = "lim",
    };

    const string SuperFrom = "0123456789+-=nTiaxk", SuperTo = "⁰¹²³⁴⁵⁶⁷⁸⁹⁺⁻⁼ⁿᵀⁱᵃˣᵏ";
    const string SubFrom = "0123456789+-=ijnkxaeotm", SubTo = "₀₁₂₃₄₅₆₇₈₉₊₋₌ᵢⱼₙₖₓₐₑₒₜₘ";

    /// <summary>The sentence with its maths in plain characters.</summary>
    public static string Inline(string text)
    {
        if (string.IsNullOrEmpty(text) || (!text.Contains('$') && !text.Contains(@"\("))) return text;
        return Spans().Replace(text, m => Plain(m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value));
    }

    static string Plain(string tex)
    {
        string t = tex;
        for (int pass = 0; pass < 3; pass++)
        {
            t = Wrapped().Replace(t, "$1");
            t = Fraction().Replace(t, m => $"{Paren(m.Groups[1].Value)}/{Paren(m.Groups[2].Value)}");
            t = Root().Replace(t, m => "√" + Paren(m.Groups[1].Value));
        }
        t = Blackboard().Replace(t, m => m.Groups[1].Value switch { "R" => "ℝ", "N" => "ℕ", "Z" => "ℤ", "Q" => "ℚ", "C" => "ℂ", var c => c });
        t = Script().Replace(t, m =>
        {
            bool up = m.Groups[1].Value == "^";
            string body = m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value;
            if (body.StartsWith('\\')) return (up ? "^" : "_") + body;
            string from = up ? SuperFrom : SubFrom, to = up ? SuperTo : SubTo;
            var made = new StringBuilder();
            foreach (char c in body)
            {
                int at = from.IndexOf(c);
                // No such small letter: say it in line instead ("x^y"), never half and half.
                if (at < 0) return (up ? "^" : "_") + (body.Length > 1 ? $"({body})" : body);
                made.Append(to[at]);
            }
            return made.ToString();
        });
        t = Command().Replace(t, m => Symbols.TryGetValue(m.Groups[1].Value, out string? s) ? s : m.Groups[1].Value);
        t = t.Replace(@"\,", " ").Replace(@"\;", " ").Replace(@"\!", "").Replace(@"\ ", " ").Replace("{", "").Replace("}", "").Replace("\\", "");
        // Maths sets two things side by side with no space ("c^T x" is cᵀx): after a raised letter the space typed
        // between them goes. (After a lowered one it stays: what follows a sum's index is what's summed.)
        t = AfterScript().Replace(t, "$1");
        return Spaces().Replace(t, " ").Trim();
    }

    /// <summary>More than one thing over or under a fraction's bar needs brackets round it.</summary>
    static string Paren(string part) => part.Trim() is var p && (p.Contains(' ') || p.Contains('+') || p.Contains('-')) ? $"({p})" : p;
}
