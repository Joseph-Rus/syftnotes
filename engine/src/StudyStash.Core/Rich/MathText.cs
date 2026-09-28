using System.Text;

namespace StudyStash.Core.Rich;

/// <summary>LaTeX maths as the notes write it ($…$), made ready for the places that show it.</summary>
public static class MathText
{
    /// <summary>Commands that stand for one symbol.</summary>
    static readonly Dictionary<string, string> Symbols = new(StringComparer.Ordinal)
    {
        ["times"] = "×", ["cdot"] = "·", ["div"] = "÷", ["pm"] = "±", ["mp"] = "∓", ["le"] = "≤", ["leq"] = "≤",
        ["ge"] = "≥", ["geq"] = "≥", ["ne"] = "≠", ["neq"] = "≠", ["approx"] = "≈", ["sim"] = "∼", ["equiv"] = "≡",
        ["propto"] = "∝", ["to"] = "→", ["rightarrow"] = "→", ["leftarrow"] = "←", ["Rightarrow"] = "⇒",
        ["Leftarrow"] = "⇐", ["leftrightarrow"] = "↔", ["Leftrightarrow"] = "⇔", ["iff"] = "⇔", ["implies"] = "⇒",
        ["uparrow"] = "↑", ["downarrow"] = "↓", ["rightleftharpoons"] = "⇌", ["longrightarrow"] = "⟶",
        ["infty"] = "∞", ["partial"] = "∂", ["nabla"] = "∇", ["sum"] = "Σ", ["prod"] = "Π", ["int"] = "∫",
        ["oint"] = "∮", ["sqrt"] = "√", ["degree"] = "°", ["circ"] = "°", ["angle"] = "∠", ["perp"] = "⊥",
        ["parallel"] = "∥", ["in"] = "∈", ["notin"] = "∉", ["subset"] = "⊂", ["cup"] = "∪", ["cap"] = "∩",
        ["forall"] = "∀", ["exists"] = "∃", ["neg"] = "¬", ["land"] = "∧", ["lor"] = "∨", ["ldots"] = "…",
        ["cdots"] = "⋯", ["dots"] = "…", ["prime"] = "′", ["hbar"] = "ℏ", ["ell"] = "ℓ", ["%"] = "%",
        ["alpha"] = "α", ["beta"] = "β", ["gamma"] = "γ", ["delta"] = "δ", ["epsilon"] = "ε", ["varepsilon"] = "ε",
        ["zeta"] = "ζ", ["eta"] = "η", ["theta"] = "θ", ["vartheta"] = "ϑ", ["iota"] = "ι", ["kappa"] = "κ",
        ["lambda"] = "λ", ["mu"] = "μ", ["nu"] = "ν", ["xi"] = "ξ", ["pi"] = "π", ["rho"] = "ρ", ["sigma"] = "σ",
        ["tau"] = "τ", ["upsilon"] = "υ", ["phi"] = "φ", ["varphi"] = "φ", ["chi"] = "χ", ["psi"] = "ψ",
        ["omega"] = "ω", ["Gamma"] = "Γ", ["Delta"] = "Δ", ["Theta"] = "Θ", ["Lambda"] = "Λ", ["Xi"] = "Ξ",
        ["Pi"] = "Π", ["Sigma"] = "Σ", ["Phi"] = "Φ", ["Psi"] = "Ψ", ["Omega"] = "Ω",
        [","] = " ", [";"] = " ", [":"] = " ", [" "] = " ", ["quad"] = " ", ["qquad"] = "  ", ["!"] = "",
        ["{"] = "{", ["}"] = "}", ["$"] = "$", ["_"] = "_", ["#"] = "#", ["&"] = "&", ["|"] = "‖", ["\\"] = " ",
    };

    /// <summary>Commands that only change how their argument looks: plain text keeps the argument.</summary>
    static readonly HashSet<string> Wrappers = new(StringComparer.Ordinal)
    {
        "text", "mathrm", "mathbf", "mathit", "mathsf", "mathtt", "mathcal", "mathbb", "boldsymbol", "bm", "textbf",
        "textit", "operatorname", "mathnormal", "vec", "hat", "bar", "dot", "ddot", "tilde", "overline", "underline",
        "boxed", "ce", "mbox", "textrm", "displaystyle", "textstyle", "left", "right", "big", "Big", "bigl", "bigr",
        "Bigl", "Bigr",
    };

    const string Sup = "⁰¹²³⁴⁵⁶⁷⁸⁹", Sub = "₀₁₂₃₄₅₆₇₈₉";

    /// <summary>
    /// LaTeX (without its dollar signs) as plain text for a place that can't typeset it, such as a diagram's box:
    /// symbol commands become their symbols, digits after ^ and _ become superscripts and subscripts,
    /// <c>\frac{a}{b}</c> becomes a/b, and the braces and styling commands go. Never throws.
    /// </summary>
    public static string Plain(string latex)
    {
        var sb = new StringBuilder(latex.Length);
        Append(sb, latex, 0);
        return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    static void Append(StringBuilder sb, string s, int depth)
    {
        if (depth > 20) { sb.Append(s.Replace("{", "").Replace("}", "").Replace("\\", "")); return; }
        int i = 0;
        while (i < s.Length)
        {
            char c = s[i];
            if (c == '\\' && i + 1 == s.Length) break;
            if (c == '\\')
            {
                int start = ++i;
                if (char.IsLetter(s[i])) while (i < s.Length && char.IsLetter(s[i])) i++;
                else i++;
                string name = s[start..i];
                if (name == "frac" || name == "dfrac" || name == "tfrac")
                {
                    string a = Argument(s, ref i), b = Argument(s, ref i);
                    Append(sb, Simple(a) ? a : "(" + a + ")", depth + 1);
                    sb.Append('/');
                    Append(sb, Simple(b) ? b : "(" + b + ")", depth + 1);
                }
                else if (name == "sqrt")
                {
                    sb.Append('√');
                    string a = Argument(s, ref i);
                    Append(sb, Simple(a) ? a : "(" + a + ")", depth + 1);
                }
                else if (Symbols.TryGetValue(name, out string? symbol))
                {
                    sb.Append(symbol);
                    // \Delta G reads ΔG, but \times \text{SV} reads × SV.
                    if (symbol.Length == 1 && char.IsLetter(symbol[0]) && i + 1 < s.Length && s[i] == ' ' && char.IsLetter(s[i + 1])) i++;
                }
                else if (!Wrappers.Contains(name)) sb.Append(name);
                continue;
            }
            if ((c == '^' || c == '_') && i + 1 < s.Length)
            {
                i++;
                string arg = Argument(s, ref i);
                string table = c == '^' ? Sup : Sub;
                if (arg.Length > 0 && arg.All(char.IsAsciiDigit)) foreach (char d in arg) sb.Append(table[d - '0']);
                else if (c == '^' && arg is "\\circ" or "o") sb.Append('°');
                else if (c == '^' && arg is "+" or "-" or "2+" or "3+" or "2-") sb.Append(arg.Replace("2", "²").Replace("3", "³").Replace('+', '⁺').Replace('-', '⁻'));
                else { sb.Append(c); Append(sb, arg, depth + 1); }
                continue;
            }
            if (c == '{' || c == '}') { i++; continue; }
            if (c == '~') { sb.Append(' '); i++; continue; }
            sb.Append(c);
            i++;
        }
    }

    static bool Simple(string s) => s.Length <= 1 || s.All(char.IsLetterOrDigit) || s.StartsWith('\\') && s.Skip(1).All(char.IsLetter);

    /// <summary>The next argument: a {group} (braces balanced; to the end if they never close) or one character.</summary>
    static string Argument(string s, ref int i)
    {
        while (i < s.Length && s[i] == ' ') i++;
        if (i >= s.Length) return "";
        if (s[i] == '{')
        {
            int depth = 0, start = i + 1;
            for (; i < s.Length; i++)
            {
                if (s[i] == '\\') { i++; continue; }
                if (s[i] == '{') depth++;
                else if (s[i] == '}' && --depth == 0) return s[start..i++];
            }
            return s[start..];
        }
        if (s[i] == '\\')
        {
            int start = i++;
            while (i < s.Length && char.IsLetter(s[i])) i++;
            if (i == start + 1 && i < s.Length) i++;
            return s[start..i];
        }
        return s[i++].ToString();
    }
}
