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

    /// <summary>How deep <see cref="Prepare"/> will follow nested commands before it gives up rewriting further (a
    /// safety net against adversarial input, never a real formula).</summary>
    const int MaxRewriteDepth = 40;

    /// <summary>
    /// LaTeX made ready for CSharpMath: commands it doesn't know, spelled the way every engine writes them, become
    /// ones it does — <c>\dfrac</c> to <c>\frac</c>, <c>\ce{H2O}</c> to a chemistry formula, units spelled out, and
    /// so on (the full list is in the notes-rich plan). Unknown commands and unbalanced braces pass through
    /// unchanged; this never throws.
    /// </summary>
    public static string Prepare(string latex) => string.IsNullOrEmpty(latex) ? latex ?? "" : RewriteLatex(latex, 0);

    static string RewriteLatex(string s, int depth)
    {
        if (depth > MaxRewriteDepth) return s;
        var sb = new StringBuilder(s.Length + 8);
        int i = 0;
        while (i < s.Length)
        {
            char c = s[i];
            if (c != '\\') { sb.Append(c); i++; continue; }
            if (i + 1 >= s.Length) { sb.Append(c); break; }
            int start = i + 1, j = start;
            if (char.IsLetter(s[j])) while (j < s.Length && char.IsLetter(s[j])) j++;
            else j++;
            string name = s[start..j];
            i = j;
            if (name == "operatorname" && i < s.Length && s[i] == '*') { name = "operatorname*"; i++; }
            switch (name)
            {
                case "dfrac" or "tfrac" or "cfrac":
                    sb.Append("\\frac");
                    break;
                case "boldsymbol":
                    sb.Append("\\bm");
                    break;
                case "operatorname*":
                    sb.Append("\\operatorname");
                    break;
                case "lvert" or "rvert":
                    sb.Append('|');
                    break;
                case "lVert" or "rVert":
                    sb.Append("\\|");
                    break;
                case "not" when i < s.Length && s[i] == '=':
                    i++;
                    sb.Append("\\neq");
                    break;
                case "textbf" or "textit":
                {
                    string arg = Argument(s, ref i);
                    string prepared = RewriteLatex(arg, depth + 1);
                    if (arg.Contains(' ')) sb.Append("\\text{").Append(prepared).Append('}');
                    else sb.Append(name == "textbf" ? "\\mathbf{" : "\\mathit{").Append(prepared).Append('}');
                    break;
                }
                case "boxed" or "cancel" or "bcancel" or "xcancel":
                    sb.Append(RewriteLatex(Argument(s, ref i), depth + 1));
                    break;
                case "phantom":
                    Argument(s, ref i); // dropped: it only ever reserved space
                    break;
                case "overset" or "stackrel":
                {
                    string a = Argument(s, ref i), b = Argument(s, ref i);
                    sb.Append('{').Append(RewriteLatex(b, depth + 1)).Append("}^{").Append(RewriteLatex(a, depth + 1)).Append('}');
                    break;
                }
                case "underset":
                {
                    string a = Argument(s, ref i), b = Argument(s, ref i);
                    sb.Append('{').Append(RewriteLatex(b, depth + 1)).Append("}_{").Append(RewriteLatex(a, depth + 1)).Append('}');
                    break;
                }
                case "xrightarrow" or "xleftarrow":
                {
                    string? below = OptionalBracket(s, ref i);
                    string above = Argument(s, ref i);
                    string arrow = name == "xrightarrow" ? "\\longrightarrow" : "\\longleftarrow";
                    string result = arrow + "^{" + RewriteLatex(above, depth + 1) + "}";
                    if (below is not null) result = "\\underset{" + RewriteLatex(below, depth + 1) + "}{" + result + "}";
                    sb.Append(result);
                    break;
                }
                case "overbrace":
                {
                    string a = Argument(s, ref i);
                    int save = i;
                    SkipSpaces(s, ref i);
                    string? b = null;
                    if (i < s.Length && s[i] == '^') { i++; b = Argument(s, ref i); }
                    else i = save;
                    sb.Append("\\overline{").Append(RewriteLatex(a, depth + 1)).Append('}');
                    if (b is not null) sb.Append("^{").Append(RewriteLatex(b, depth + 1)).Append('}');
                    break;
                }
                case "tag" or "label":
                    Argument(s, ref i); // dropped
                    break;
                case "nonumber" or "notag" or "hline" or "displaybreak":
                    break; // dropped, no argument
                case "SI" or "qty":
                {
                    string v = Argument(s, ref i), u = Argument(s, ref i);
                    sb.Append(RewriteLatex(v, depth + 1)).Append("\\ \\text{").Append(Unit(u)).Append('}');
                    break;
                }
                case "si" or "unit":
                    sb.Append("\\text{").Append(Unit(Argument(s, ref i))).Append('}');
                    break;
                case "ket":
                    sb.Append('|').Append(RewriteLatex(Argument(s, ref i), depth + 1)).Append("\\rangle");
                    break;
                case "bra":
                    sb.Append("\\langle ").Append(RewriteLatex(Argument(s, ref i), depth + 1)).Append('|');
                    break;
                case "ce":
                    sb.Append("\\mathrm{").Append(Chem(Argument(s, ref i))).Append('}');
                    break;
                case "begin":
                    RewriteEnvironment(sb, s, ref i, depth);
                    break;
                default:
                    sb.Append('\\').Append(name);
                    break;
            }
        }
        return sb.ToString();
    }

    /// <summary>An optional <c>[…]</c> argument (balanced on brackets), or null when there isn't one; never throws
    /// on an unbalanced one.</summary>
    static string? OptionalBracket(string s, ref int i)
    {
        int save = i;
        SkipSpaces(s, ref i);
        if (i >= s.Length || s[i] != '[') { i = save; return null; }
        int depth = 0, start = i + 1;
        for (; i < s.Length; i++)
        {
            if (s[i] == '\\') { i++; continue; }
            if (s[i] == '[') depth++;
            else if (s[i] == ']' && --depth == 0) { string r = s[start..i]; i++; return r; }
        }
        return s[start..];
    }

    static void SkipSpaces(string s, ref int i)
    {
        while (i < s.Length && s[i] == ' ') i++;
    }

    /// <summary>
    /// <c>\begin{name}…\end{name}</c>, renamed to what CSharpMath knows (align/eqnarray → aligned, gather*/multline →
    /// gather, equation(*) unwrapped to its body, array's column spec dropped as matrix), its body rewritten too.
    /// Nested environments of any name are skipped correctly when hunting for the matching <c>\end</c>; an unmatched
    /// one takes the rest of the string as its body rather than throwing.
    /// </summary>
    static void RewriteEnvironment(StringBuilder sb, string s, ref int i, int depth)
    {
        string envName = Argument(s, ref i);
        if (envName == "array")
        {
            int save = i;
            SkipSpaces(s, ref i);
            if (i < s.Length && s[i] == '{') Argument(s, ref i); // the column spec: dropped, matrix needs none
            else i = save;
        }
        int nest = 1, p = i, bodyEnd = s.Length, afterEnd = s.Length;
        bool matched = false;
        while (p < s.Length)
        {
            int b = s.IndexOf("\\begin", p, StringComparison.Ordinal);
            int e = s.IndexOf("\\end", p, StringComparison.Ordinal);
            if (e < 0) break; // unbalanced: the body runs to the end, never throws
            if (b >= 0 && b < e) { nest++; p = b + 6; continue; }
            nest--;
            p = e + 4;
            if (nest == 0) { bodyEnd = e; afterEnd = p; matched = true; break; }
        }
        string body = s[i..bodyEnd];
        i = afterEnd;
        if (matched)
        {
            SkipSpaces(s, ref i);
            if (i < s.Length && s[i] == '{') Argument(s, ref i); // \end{name}: its own name argument, discarded
        }

        string inner = RewriteLatex(body, depth + 1);
        string baseName = envName.TrimEnd('*');
        string newName = baseName switch
        {
            "align" or "eqnarray" => "aligned",
            "gather" when envName == "gather*" => "gather",
            "multline" => "gather",
            "equation" => "",
            "array" => "matrix",
            _ => envName,
        };
        if (newName.Length == 0) sb.Append(inner);
        else
        {
            sb.Append("\\begin{").Append(newName).Append('}').Append(inner);
            if (matched) sb.Append("\\end{").Append(newName).Append('}');
        }
    }

    /// <summary>Unit macros (siunitx-style) spelled out as text: prefixes and units concatenate with nothing between
    /// them (<c>\milli\gram</c> reads "mg"); anything not recognised keeps its bare name.</summary>
    static readonly Dictionary<string, string> UnitWords = new(StringComparer.Ordinal)
    {
        ["milli"] = "m", ["micro"] = "µ", ["centi"] = "c", ["kilo"] = "k", ["deci"] = "d", ["nano"] = "n", ["mega"] = "M",
        ["gram"] = "g", ["metre"] = "m", ["meter"] = "m", ["litre"] = "L", ["liter"] = "L", ["mole"] = "mol",
        ["second"] = "s", ["minute"] = "min", ["hour"] = "h", ["kelvin"] = "K", ["ampere"] = "A", ["celsius"] = "°C",
        ["fahrenheit"] = "°F", ["percent"] = "%", ["per"] = "/", ["square"] = "sq ", ["cubic"] = "cu ",
    };

    static string Unit(string s)
    {
        var sb = new StringBuilder(s.Length);
        int i = 0;
        while (i < s.Length)
        {
            if (s[i] == '\\')
            {
                int start = i + 1, j = start;
                while (j < s.Length && char.IsLetter(s[j])) j++;
                string name = s[start..j];
                i = j;
                sb.Append(UnitWords.TryGetValue(name, out string? w) ? w : name);
            }
            else { sb.Append(s[i]); i++; }
        }
        return sb.ToString();
    }

    /// <summary>
    /// A small mhchem-lite for <c>\ce{…}</c>: a digit right after an element or a closing bracket becomes a
    /// subscript, a bare <c>+</c>/<c>-</c> right after one becomes a superscript charge, <c>-></c> becomes an arrow
    /// and <c>&lt;=></c>/<c>&lt;-></c> become equilibrium arrows. Anything already inside braces (an explicit
    /// <c>^{2+}</c>) is left exactly as written.
    /// </summary>
    static string Chem(string content)
    {
        var sb = new StringBuilder(content.Length + 8);
        int i = 0, braces = 0;
        while (i < content.Length)
        {
            char c = content[i];
            if (c == '{') { braces++; sb.Append(c); i++; continue; }
            if (c == '}') { braces--; sb.Append(c); i++; continue; }
            if (braces == 0)
            {
                if (c == '-' && i + 1 < content.Length && content[i + 1] == '>') { sb.Append("\\rightarrow "); i += 2; while (i < content.Length && content[i] == ' ') i++; continue; }
                if (c == '<' && i + 2 < content.Length && content[i + 1] == '=' && content[i + 2] == '>') { sb.Append("\\rightleftharpoons "); i += 3; while (i < content.Length && content[i] == ' ') i++; continue; }
                if (c == '<' && i + 2 < content.Length && content[i + 1] == '-' && content[i + 2] == '>') { sb.Append("\\rightleftharpoons "); i += 3; while (i < content.Length && content[i] == ' ') i++; continue; }
                if (char.IsAsciiDigit(c) && sb.Length > 0 && (char.IsLetter(sb[^1]) || sb[^1] is ')' or ']'))
                {
                    int start = i;
                    while (i < content.Length && char.IsAsciiDigit(content[i])) i++;
                    sb.Append("_{").Append(content, start, i - start).Append('}');
                    continue;
                }
                if (c is '+' or '-' && sb.Length > 0 && char.IsLetterOrDigit(sb[^1]))
                {
                    int start = i;
                    while (i < content.Length && content[i] is '+' or '-') i++;
                    sb.Append("^{").Append(content, start, i - start).Append('}');
                    continue;
                }
            }
            sb.Append(c);
            i++;
        }
        return sb.ToString();
    }

    /// <summary>
    /// <c>\(…\)</c> and <c>\[…\]</c> as <c>$…$</c> and <c>$$…$$</c> (what Markdig's maths extension reads), left
    /// alone inside a fenced code block or an inline code span.
    /// </summary>
    public static string DollarDelimiters(string markdown)
    {
        var sb = new StringBuilder(markdown.Length);
        int i = 0;
        bool inFence = false;
        char fenceChar = '`';
        int fenceLen = 0;
        while (i < markdown.Length)
        {
            if (i == 0 || markdown[i - 1] == '\n')
            {
                int j = i;
                while (j < markdown.Length && markdown[j] == ' ') j++;
                if (j < markdown.Length && (markdown[j] == '`' || markdown[j] == '~'))
                {
                    char fc = markdown[j];
                    int k = j;
                    while (k < markdown.Length && markdown[k] == fc) k++;
                    int count = k - j;
                    if (count >= 3 && (!inFence || (fc == fenceChar && count >= fenceLen)))
                    {
                        if (!inFence) { inFence = true; fenceChar = fc; fenceLen = count; }
                        else inFence = false;
                        int nl = markdown.IndexOf('\n', i);
                        int end = nl < 0 ? markdown.Length : nl + 1;
                        sb.Append(markdown, i, end - i);
                        i = end;
                        continue;
                    }
                }
            }
            if (inFence)
            {
                int nl = markdown.IndexOf('\n', i);
                int end = nl < 0 ? markdown.Length : nl + 1;
                sb.Append(markdown, i, end - i);
                i = end;
                continue;
            }
            if (markdown[i] == '`')
            {
                int j = i;
                while (j < markdown.Length && markdown[j] == '`') j++;
                string ticks = markdown[i..j];
                int close = markdown.IndexOf(ticks, j, StringComparison.Ordinal);
                int end = close < 0 ? j : close + ticks.Length;
                sb.Append(markdown, i, end - i);
                i = end;
                continue;
            }
            if (i + 1 < markdown.Length && markdown[i] == '\\')
            {
                char n = markdown[i + 1];
                if (n is '(' or ')') { sb.Append('$'); i += 2; continue; }
                if (n is '[' or ']') { sb.Append("$$"); i += 2; continue; }
            }
            sb.Append(markdown[i]);
            i++;
        }
        return sb.ToString();
    }

    /// <summary>Commands a JSON decoder swallowed the backslash-letter of, inside <c>$…$</c> only: a control
    /// character JSON turns <c>\f \t \b \r</c> into, put back as the two characters they came from. A real newline
    /// stays a newline unless it precedes one of the few command names LaTeX still needs (<c>\nu</c>, <c>\neq</c>…) —
    /// so sentences that just happen to sit either side of a formula are untouched.</summary>
    public static string RepairJsonEscapes(string text)
    {
        if (string.IsNullOrEmpty(text) || text.IndexOf('$') < 0) return text;
        var sb = new StringBuilder(text.Length + 8);
        int i = 0;
        while (i < text.Length)
        {
            if (text[i] == '$')
            {
                int close = text.IndexOf('$', i + 1);
                if (close < 0) { sb.Append(text, i, text.Length - i); break; }
                sb.Append('$').Append(RepairSpan(text[(i + 1)..close])).Append('$');
                i = close + 1;
                continue;
            }
            sb.Append(text[i]);
            i++;
        }
        return sb.ToString();
    }

    static readonly HashSet<string> NewlineWords = new(StringComparer.Ordinal)
        { "nu", "nabla", "neq", "ne", "neg", "not", "ni", "notin", "newline" };

    static string RepairSpan(string s)
    {
        var sb = new StringBuilder(s.Length + 4);
        int i = 0;
        while (i < s.Length)
        {
            char c = s[i];
            switch (c)
            {
                case '\f': sb.Append("\\f"); i++; break;
                case '\t': sb.Append("\\t"); i++; break;
                case '\b': sb.Append("\\b"); i++; break;
                case '\r': sb.Append("\\r"); i++; break;
                case '\n':
                {
                    // JSON's \n escape ate the backslash and the command's own leading "n" (\nu, \neq, \nabla…);
                    // what survives is the rest of the name, so the word to check for is "n" + that.
                    int j = i + 1;
                    while (j < s.Length && char.IsLetter(s[j])) j++;
                    if (NewlineWords.Contains("n" + s[(i + 1)..j])) { sb.Append("\\n"); i++; }
                    else { sb.Append(c); i++; }
                    break;
                }
                default:
                    sb.Append(c);
                    i++;
                    break;
            }
        }
        return sb.ToString();
    }

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
