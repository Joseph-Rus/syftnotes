using System.Globalization;
using System.Text;

namespace StudyStash.Core.Rich;

/// <summary>Why a plot can't be drawn, in plain English with the line it's on (what a repair is sent back with).</summary>
public sealed class PlotException(string message) : Exception(message);

/// <summary>What a piece of a plot's line is: a number, a name, an operator, a "quoted" label, or a LaTeX command
/// (<c>\frac</c>).</summary>
public enum PlotTokenKind { Number, Name, Op, Text, Command }

/// <summary>One piece of a plot's line, and where it sits in the line.</summary>
public readonly record struct PlotToken(PlotTokenKind Kind, string Text, double Value, int Start, int End)
{
    public bool Is(string op) => Kind == PlotTokenKind.Op && Text == op;
    public bool IsWord(string word) => Kind == PlotTokenKind.Name && Text == word;
}

/// <summary>
/// Reads a plot's line into pieces, in the notation lecturers write: <c>2x</c>, <c>x²</c>, <c>e^{-x^2/2}</c>,
/// <c>σ</c> or <c>sigma</c>, <c>−</c> and <c>×</c>, <c>≤</c>, a little LaTeX (<c>\frac{a}{b}</c>, <c>\sqrt{x}</c>,
/// <c>\cdot</c>, <c>\sigma</c>), subscripts (<c>x_1</c>, <c>x_{max}</c>), and "quoted" labels. Nothing it reads
/// can run: it only ever makes numbers, names and operators.
/// </summary>
public static class PlotLexer
{
    /// <summary>Greek letters as they're spelled, each read as the letter itself (so <c>sigma</c> and <c>σ</c> are
    /// one name).</summary>
    public static readonly IReadOnlyDictionary<string, string> Greek = new Dictionary<string, string>
    {
        ["alpha"] = "α", ["beta"] = "β", ["gamma"] = "γ", ["delta"] = "δ", ["epsilon"] = "ε", ["varepsilon"] = "ε", ["zeta"] = "ζ",
        ["eta"] = "η", ["theta"] = "θ", ["vartheta"] = "θ", ["kappa"] = "κ", ["lambda"] = "λ", ["mu"] = "μ", ["nu"] = "ν",
        ["xi"] = "ξ", ["rho"] = "ρ", ["sigma"] = "σ", ["tau"] = "τ", ["phi"] = "φ", ["varphi"] = "φ", ["chi"] = "χ",
        ["psi"] = "ψ", ["omega"] = "ω", ["Delta"] = "Δ", ["Gamma"] = "Γ", ["Lambda"] = "Λ", ["Omega"] = "Ω", ["Theta"] = "Θ",
        ["Psi"] = "Ψ",
    };

    /// <summary>The longest line a plot may have.</summary>
    public const int MaxLine = 2000;

    public static List<PlotToken> Lex(string line)
    {
        if (line.Length > MaxLine) throw new PlotException($"a line longer than {MaxLine} characters");
        var tokens = new List<PlotToken>();
        int i = 0;
        while (i < line.Length)
        {
            char c = line[i];
            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }
            int start = i;
            if (c is '"' or '“' or '”')
            {
                int close = line.IndexOfAny(['"', '”', '“'], i + 1);
                if (close < 0) throw new PlotException("a label whose quotes are never closed");
                tokens.Add(new PlotToken(PlotTokenKind.Text, line[(i + 1)..close], 0, start, close + 1));
                i = close + 1;
                continue;
            }
            if (char.IsAsciiDigit(c) || (c == '.' && i + 1 < line.Length && char.IsAsciiDigit(line[i + 1])))
            {
                while (i < line.Length && char.IsAsciiDigit(line[i])) i++;
                if (i < line.Length && line[i] == '.' && !(i + 1 < line.Length && line[i + 1] == '.'))
                {
                    i++;
                    while (i < line.Length && char.IsAsciiDigit(line[i])) i++;
                }
                // 1e-3 is a thousandth; 2e^x and 2e are 2 times e.
                if (i < line.Length && line[i] is 'e' or 'E')
                {
                    int k = i + 1;
                    if (k < line.Length && line[k] is '+' or '-') k++;
                    if (k < line.Length && char.IsAsciiDigit(line[k]))
                    {
                        i = k;
                        while (i < line.Length && char.IsAsciiDigit(line[i])) i++;
                    }
                }
                string number = line[start..i];
                if (!double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                    throw new PlotException($"“{number}” isn't a number");
                tokens.Add(new PlotToken(PlotTokenKind.Number, number, value, start, i));
                continue;
            }
            if (char.IsLetter(c))
            {
                if (c == 'π')
                {
                    tokens.Add(new PlotToken(PlotTokenKind.Name, "pi", 0, start, ++i));
                    continue;
                }
                while (i < line.Length && (char.IsLetterOrDigit(line[i]) && line[i] != 'π')) i++;
                string name = line[start..i];
                // A subscript: x_1, w_0, x_{max}, log_2, log_{10}.
                while (i < line.Length && line[i] == '_')
                {
                    int k = i + 1;
                    if (k < line.Length && line[k] == '{')
                    {
                        int close = line.IndexOf('}', k);
                        if (close < 0) throw new PlotException("a subscript whose { is never closed");
                        string sub = line[(k + 1)..close].Trim();
                        if (sub.Length == 0 || !sub.All(char.IsLetterOrDigit)) throw new PlotException($"“{name}_{{{sub}}}” isn't a name");
                        name += "_" + sub;
                        i = close + 1;
                    }
                    else if (k < line.Length && char.IsLetterOrDigit(line[k]))
                    {
                        int e = k;
                        while (e < line.Length && char.IsLetterOrDigit(line[e])) e++;
                        name += "_" + line[k..e];
                        i = e;
                    }
                    else break;
                }
                tokens.Add(new PlotToken(PlotTokenKind.Name, Normal(name), 0, start, i));
                continue;
            }
            if (c == '\\')
            {
                int k = i + 1;
                while (k < line.Length && char.IsAsciiLetter(line[k])) k++;
                string command = line[(i + 1)..k];
                if (command.Length == 0)
                {
                    // \, \; \! \  are spacing; \{ \} are braces.
                    char next = k < line.Length ? line[k] : ' ';
                    if (next is '{' or '}') tokens.Add(new PlotToken(PlotTokenKind.Op, next.ToString(), 0, start, k + 1));
                    else if (next is not (',' or ';' or '!' or ' ' or ':' or '|')) throw new PlotException($"“\\{next}” isn't something a plot can read");
                    i = k + 1;
                    continue;
                }
                i = k;
                switch (command)
                {
                    case "left" or "right" or "big" or "Big" or "bigg" or "Bigg" or "bigl" or "bigr" or "Bigl" or "Bigr" or "quad" or "qquad" or "displaystyle" or "mathrm" or "mathit" or "text":
                        continue;
                    case "cdot" or "times" or "ast":
                        tokens.Add(new PlotToken(PlotTokenKind.Op, "*", 0, start, i));
                        continue;
                    case "div":
                        tokens.Add(new PlotToken(PlotTokenKind.Op, "/", 0, start, i));
                        continue;
                    case "le" or "leq":
                        tokens.Add(new PlotToken(PlotTokenKind.Op, "<=", 0, start, i));
                        continue;
                    case "ge" or "geq":
                        tokens.Add(new PlotToken(PlotTokenKind.Op, ">=", 0, start, i));
                        continue;
                    case "ne" or "neq":
                        tokens.Add(new PlotToken(PlotTokenKind.Op, "!=", 0, start, i));
                        continue;
                    case "lt":
                        tokens.Add(new PlotToken(PlotTokenKind.Op, "<", 0, start, i));
                        continue;
                    case "gt":
                        tokens.Add(new PlotToken(PlotTokenKind.Op, ">", 0, start, i));
                        continue;
                    case "frac" or "dfrac" or "tfrac" or "sqrt":
                        tokens.Add(new PlotToken(PlotTokenKind.Command, command.EndsWith("frac", StringComparison.Ordinal) ? "frac" : "sqrt", 0, start, i));
                        continue;
                    case "pi":
                        tokens.Add(new PlotToken(PlotTokenKind.Name, "pi", 0, start, i));
                        continue;
                    case "exp" or "ln" or "log" or "sin" or "cos" or "tan" or "sinh" or "cosh" or "tanh" or "arcsin" or "arccos" or "arctan"
                        or "min" or "max" or "sec" or "csc" or "cot":
                        tokens.Add(new PlotToken(PlotTokenKind.Name, Normal(command), 0, start, i));
                        continue;
                    case "operatorname":
                        continue; // \operatorname{relu}: the braces' word reads as a name
                    default:
                        if (Greek.TryGetValue(command, out string? letter))
                        {
                            tokens.Add(new PlotToken(PlotTokenKind.Name, letter, 0, start, i));
                            continue;
                        }
                        throw new PlotException($"“\\{command}” isn't something a plot can read");
                }
            }
            // Two-character operators first.
            if (i + 1 < line.Length)
            {
                string two = line.Substring(i, 2);
                if (two is "<=" or ">=" or "==" or "!=" or "..")
                {
                    tokens.Add(new PlotToken(PlotTokenKind.Op, two, 0, start, i + 2));
                    i += 2;
                    continue;
                }
            }
            string? op = c switch
            {
                '+' or '-' or '*' or '/' or '^' or '(' or ')' or '[' or ']' or '{' or '}' or ',' or '!' or '\'' or '|' or '<' or '>' or '=' or ':' or '%' => c.ToString(),
                '−' or '–' => "-",
                '×' or '·' or '⋅' or '∙' => "*",
                '÷' => "/",
                '≤' => "<=",
                '≥' => ">=",
                '≠' => "!=",
                '′' => "'",
                _ => null,
            };
            if (op is not null)
            {
                tokens.Add(new PlotToken(PlotTokenKind.Op, op, 0, start, i + 1));
                i++;
                continue;
            }
            if (c is '²' or '³')
            {
                tokens.Add(new PlotToken(PlotTokenKind.Op, "^", 0, start, i + 1));
                tokens.Add(new PlotToken(PlotTokenKind.Number, c == '²' ? "2" : "3", c == '²' ? 2 : 3, start, i + 1));
                i++;
                continue;
            }
            if (c == '√')
            {
                tokens.Add(new PlotToken(PlotTokenKind.Name, "sqrt", 0, start, i + 1));
                i++;
                continue;
            }
            throw new PlotException($"“{c}” isn't something a plot can read");
        }
        return tokens;
    }

    /// <summary>A name as the plot keeps it: a Greek letter spelled out becomes the letter (σ for sigma, σ_1 for
    /// sigma_1); log_2 and log_10 become log2 and log10.</summary>
    static string Normal(string name)
    {
        if (name is "log_2") return "log2";
        if (name is "log_10") return "log10";
        if (name is "log_e") return "ln";
        int bar = name.IndexOf('_');
        string head = bar < 0 ? name : name[..bar];
        return Greek.TryGetValue(head, out string? letter) ? letter + (bar < 0 ? "" : name[bar..]) : name;
    }
}

/// <summary>
/// What an expression is evaluated in: the value of each variable and slider (by slot), and how much work one
/// evaluation may still do. A plot evaluates on one thread at a time; each thread has its own.
/// </summary>
public sealed class PlotEnv
{
    public PlotEnv(int slots) => Slots = new double[Math.Max(1, slots)];

    public double[] Slots;

    /// <summary>The steps one evaluation may still take (a sum's terms, a call to another curve): past it, NaN.</summary>
    public long Budget;

    /// <summary>Changes whenever a slider does, so a sequence worked out for the old values is worked out again.</summary>
    public int Generation;

    /// <summary>The steps taken so far, over every evaluation.</summary>
    public long Spent;

    /// <summary>The steps a frame of the plot may still take, over all its evaluations: once they're spent, anything
    /// that calls another curve or sums a range comes out undefined at once, so no plot can make a frame slow.</summary>
    public long FrameLeft = long.MaxValue;

    /// <summary>A new frame, with <paramref name="steps"/> to spend.</summary>
    public void NewFrame(long steps = FrameSteps) => FrameLeft = steps;

    /// <summary>What one frame (or one look at the plot, when it opens) may spend.</summary>
    public const long FrameSteps = 4_000_000;

    /// <summary>The most steps one evaluation takes before it gives up (and is NaN).</summary>
    public const long MaxSteps = 50_000;
}

/// <summary>A name an expression may call: a curve written <c>f(x) = …</c> (or a sequence, <c>a(n) = …</c>), with
/// its arguments' slots and its body.</summary>
public sealed class PlotFunction(string name, int[] args)
{
    public string Name { get; } = name;
    public int[] Args { get; } = args;
    public int Arity => Args.Length;
    internal PlotNode? Body { get; set; }

    /// <summary>A sequence (bars, stems, dots or steps): defined at whole numbers from <see cref="From"/> to
    /// <see cref="To"/>, each term able to use the ones before it (<c>a(n-1)</c>); worked out in order, once per
    /// slider setting.</summary>
    public bool Sequence { get; init; }
    internal PlotExpression? From { get; set; }
    internal PlotExpression? To { get; set; }

    /// <summary>The longest a sequence runs.</summary>
    public const int MaxTerms = 10_000;

    double[]? terms;
    long termsFrom;
    int generation = -1;
    bool working;

    /// <summary>The sequence's term at <paramref name="n"/>: NaN off its range, or for a term not yet worked out
    /// (a term can only use the ones before it).</summary>
    internal double Term(PlotEnv env, double n)
    {
        if (!double.IsFinite(n)) return double.NaN;
        long k = (long)Math.Round(n);
        if (Math.Abs(n - k) > 1e-9) return double.NaN;
        if (working)
        {
            // Inside its own working out: only an earlier term.
            if (terms is null || k < termsFrom || k - termsFrom >= filled) return double.NaN;
            return terms[k - termsFrom];
        }
        if (generation != env.Generation || terms is null) Work(env);
        return terms is null || k < termsFrom || k - termsFrom >= terms.Length ? double.NaN : terms[k - termsFrom];
    }

    long filled;

    void Work(PlotEnv env)
    {
        generation = env.Generation;
        terms = null;
        long budget = env.Budget;
        double a = From!.Root.Eval(env), b = To!.Root.Eval(env);
        if (!double.IsFinite(a) || !double.IsFinite(b)) return;
        long first = (long)Math.Ceiling(a - 1e-9), last = (long)Math.Floor(b + 1e-9);
        if (last < first) return;
        last = Math.Min(last, first + MaxTerms - 1);
        terms = new double[last - first + 1];
        termsFrom = first;
        filled = 0;
        int slot = Args[0];
        double saved = env.Slots[slot];
        working = true;
        // The whole table shares one budget (a hostile sequence can't take a budget per term).
        long start = Math.Min(PlotEnv.MaxSteps * 10, env.FrameLeft);
        env.Budget = start;
        try
        {
            for (long k = first; k <= last; k++)
            {
                env.Slots[slot] = k;
                terms[k - first] = env.Budget < 0 ? double.NaN : Body!.Eval(env);
                env.Budget--;
                filled++;
            }
        }
        finally
        {
            working = false;
            env.Slots[slot] = saved;
            long used = start - Math.Max(0, env.Budget);
            env.Spent += used;
            env.FrameLeft -= used;
            env.Budget = budget;
        }
    }

    /// <summary>Forgets the worked-out terms (a new evaluation context).</summary>
    internal void Reset() => generation = -1;
}

/// <summary>An expression, read and checked: evaluate it with <see cref="Eval"/>. <see cref="Text"/> is how it was
/// written, kept as written (the lecturer's notation) when the plot is written out again.</summary>
public sealed class PlotExpression
{
    internal PlotExpression(string text, PlotNode root, int nodes)
    {
        Text = text;
        Root = root;
        Nodes = nodes;
    }

    public string Text { get; }
    internal PlotNode Root { get; }

    /// <summary>How big it is (how much work one evaluation is, roughly).</summary>
    public int Nodes { get; }

    /// <summary>Its value with the slots as <paramref name="env"/> has them; NaN where it's undefined (or where it
    /// would take too long to say).</summary>
    public double Eval(PlotEnv env)
    {
        long start = Math.Min(PlotEnv.MaxSteps, env.FrameLeft);
        env.Budget = start;
        double v = Root.Eval(env);
        long used = 1 + start - Math.Max(0, env.Budget);
        env.Spent += used;
        env.FrameLeft -= used;
        return v;
    }

    /// <summary>Whether it's a plain number (no names, no calls), and if so which.</summary>
    public double? Constant => Root is ConstNode c ? c.Value : null;

    /// <summary>The slot it reads when it's just one name (a slider), else null.</summary>
    public int? SlotOnly => Root is SlotNode s ? s.Slot : null;

    /// <summary>The slots it reads, through the curves it calls too.</summary>
    public IReadOnlySet<int> Reads()
    {
        var slots = new HashSet<int>();
        var seen = new HashSet<PlotFunction>();
        Root.Collect(slots, seen);
        return slots;
    }

    /// <summary>The curves it calls (directly).</summary>
    public IReadOnlySet<PlotFunction> Calls()
    {
        var calls = new HashSet<PlotFunction>();
        Root.Calls(calls);
        return calls;
    }
}

/// <summary>The names an expression in a plot may use: each variable and slider's slot, and the curves that can be
/// called.</summary>
public sealed class PlotScope
{
    readonly Dictionary<string, int> slots = new(StringComparer.Ordinal);
    readonly Dictionary<string, int> parameters = new(StringComparer.Ordinal);
    readonly Dictionary<string, PlotFunction> functions = new(StringComparer.Ordinal);

    /// <summary>How many slots an evaluation needs (every variable, slider and counter).</summary>
    public int SlotCount => slots.Count;

    /// <summary>The sliders, by name: every expression in the plot can use them.</summary>
    public IReadOnlyDictionary<string, int> Params => parameters;

    public IReadOnlyDictionary<string, PlotFunction> Functions => functions;

    /// <summary>The slot kept under <paramref name="key"/>, made if it's new.</summary>
    public int Slot(string key)
    {
        if (!slots.TryGetValue(key, out int s)) slots[key] = s = slots.Count;
        return s;
    }

    /// <summary>A slider's slot (a variable of its own, seen by every expression).</summary>
    public int AddParam(string name) => parameters[name] = Slot("param " + name);

    /// <summary>A variable's slot (x, t, n…): one per name, seen only by the expressions that name it.</summary>
    public int Variable(string name) => Slot("var " + name);

    public void Add(PlotFunction f) => functions[f.Name] = f;

    /// <summary>A name the plot can't use for a slider or a curve of its own (a constant, a built-in function, or a
    /// word its lines are made of).</summary>
    public static bool Reserved(string name) => name is "pi" or "e" or "and" or "or" or "not" || PlotBuiltins.Has(name) || PlotSyntax.StopWords.Contains(name);
}

/// <summary>The built-in functions: each one's name, how many arguments it takes, and what it does.</summary>
public static class PlotBuiltins
{
    static readonly Dictionary<string, Func<double, double>> One = new(StringComparer.Ordinal)
    {
        ["sin"] = Math.Sin, ["cos"] = Math.Cos, ["tan"] = Math.Tan,
        ["sec"] = x => 1 / Math.Cos(x), ["csc"] = x => 1 / Math.Sin(x), ["cot"] = x => 1 / Math.Tan(x),
        ["asin"] = Math.Asin, ["acos"] = Math.Acos, ["atan"] = Math.Atan, ["arcsin"] = Math.Asin, ["arccos"] = Math.Acos, ["arctan"] = Math.Atan,
        ["sinh"] = Math.Sinh, ["cosh"] = Math.Cosh, ["tanh"] = Math.Tanh,
        ["exp"] = Math.Exp, ["ln"] = Math.Log, ["log"] = Math.Log, ["log2"] = Math.Log2, ["log10"] = Math.Log10, ["lg"] = Math.Log2,
        ["sqrt"] = Math.Sqrt, ["cbrt"] = Math.Cbrt, ["abs"] = Math.Abs, ["floor"] = Math.Floor, ["ceil"] = Math.Ceiling,
        ["round"] = x => Math.Round(x, MidpointRounding.AwayFromZero), ["sign"] = x => double.IsNaN(x) ? double.NaN : Math.Sign(x),
        ["sgn"] = x => double.IsNaN(x) ? double.NaN : Math.Sign(x),
        ["gamma"] = PlotMath.Gamma, ["lgamma"] = PlotMath.LogGamma, ["erf"] = PlotMath.Erf, ["Phi"] = PlotMath.NormalCdf,
        ["Φ"] = PlotMath.NormalCdf, ["fact"] = x => PlotMath.Gamma(x + 1),
    };

    static readonly Dictionary<string, Func<double, double, double>> Two = new(StringComparer.Ordinal)
    {
        ["pow"] = PlotMath.Pow, ["mod"] = PlotMath.Mod, ["atan2"] = Math.Atan2, ["choose"] = PlotMath.Choose, ["binom"] = PlotMath.Choose,
        ["nCr"] = PlotMath.Choose, ["logb"] = (b, x) => Math.Log(x) / Math.Log(b),
    };

    /// <summary>Functions of any number of arguments (at least one).</summary>
    static readonly HashSet<string> Many = ["min", "max"];

    /// <summary>The forms that aren't plain functions: if(test, then, else), sum and prod over a whole-number range,
    /// clamp(x, low, high).</summary>
    static readonly HashSet<string> Special = ["if", "sum", "prod", "clamp"];

    public static bool Has(string name) => One.ContainsKey(name) || Two.ContainsKey(name) || Many.Contains(name) || Special.Contains(name);

    internal static Func<double, double>? Unary(string name) => One.GetValueOrDefault(name);
    internal static Func<double, double, double>? Binary(string name) => Two.GetValueOrDefault(name);
    internal static bool IsMany(string name) => Many.Contains(name);
    internal static bool IsSpecial(string name) => Special.Contains(name);

    /// <summary>Every built-in name, for the brief.</summary>
    public static IEnumerable<string> Names => One.Keys.Concat(Two.Keys).Concat(Many).Concat(Special);
}

/// <summary>The maths the built-in functions need that .NET doesn't have: the gamma function, erf, n choose k.</summary>
public static class PlotMath
{
    static readonly double[] Lanczos =
    [
        0.99999999999980993, 676.5203681218851, -1259.1392167224028, 771.32342877765313, -176.61502916214059,
        12.507343278686905, -0.13857109526572012, 9.9843695780195716e-6, 1.5056327351493116e-7,
    ];

    public static double Gamma(double x)
    {
        if (double.IsNaN(x)) return double.NaN;
        if (x == Math.Floor(x) && x <= 0) return double.NaN;
        if (x == Math.Floor(x) && x <= 171)
        {
            double f = 1;
            for (int k = 2; k < (int)x; k++) f *= k;
            return f;
        }
        if (x < 0.5) return Math.PI / (Math.Sin(Math.PI * x) * Gamma(1 - x));
        if (x > 171.7) return double.PositiveInfinity;
        x -= 1;
        double a = Lanczos[0], t = x + 7.5;
        for (int i = 1; i < 9; i++) a += Lanczos[i] / (x + i);
        return Math.Sqrt(2 * Math.PI) * Math.Pow(t, x + 0.5) * Math.Exp(-t) * a;
    }

    public static double LogGamma(double x)
    {
        if (double.IsNaN(x) || x <= 0 && x == Math.Floor(x)) return double.NaN;
        if (x < 0.5) return Math.Log(Math.PI / Math.Abs(Math.Sin(Math.PI * x))) - LogGamma(1 - x);
        x -= 1;
        double a = Lanczos[0], t = x + 7.5;
        for (int i = 1; i < 9; i++) a += Lanczos[i] / (x + i);
        return 0.5 * Math.Log(2 * Math.PI) + (x + 0.5) * Math.Log(t) - t + Math.Log(a);
    }

    /// <summary>erf to about 1e-7 (Abramowitz and Stegun 7.1.26 is too coarse for a shaded probability; this is
    /// W. J. Cody's rational form by way of a continued series, good to plotting accuracy).</summary>
    public static double Erf(double x)
    {
        if (double.IsNaN(x)) return double.NaN;
        if (Math.Abs(x) > 6) return Math.Sign(x);
        // A series near 0, a continued fraction further out.
        double ax = Math.Abs(x);
        double r;
        if (ax < 2.5)
        {
            double sum = ax, term = ax, x2 = ax * ax;
            for (int n = 1; n < 60; n++)
            {
                term *= -x2 / n;
                double add = term / (2 * n + 1);
                sum += add;
                if (Math.Abs(add) < 1e-17 * Math.Abs(sum)) break;
            }
            r = 2 / Math.Sqrt(Math.PI) * sum;
        }
        else
        {
            // erfc by Lentz's continued fraction.
            double x2 = ax * ax, f = ax, c = ax, d = 0;
            for (int n = 1; n < 80; n++)
            {
                double an = n / 2.0;
                d = ax + an * d;
                d = d == 0 ? 1e-300 : 1 / d;
                c = ax + an / c;
                if (c == 0) c = 1e-300;
                double delta = c * d;
                f *= delta;
                if (Math.Abs(delta - 1) < 1e-16) break;
            }
            r = 1 - Math.Exp(-x2) / (f * Math.Sqrt(Math.PI));
        }
        return x < 0 ? -r : r;
    }

    public static double NormalCdf(double x) => 0.5 * (1 + Erf(x / Math.Sqrt(2)));

    public static double Pow(double a, double b)
    {
        // A negative number to an odd root's power (x^(1/3)) stays real, as a lecturer means it.
        if (a < 0 && !double.IsInteger(b) && double.IsFinite(b))
        {
            double inv = 1 / b;
            if (Math.Abs(inv - Math.Round(inv)) < 1e-9 && ((long)Math.Round(inv)) % 2 != 0) return -Math.Pow(-a, b);
        }
        return Math.Pow(a, b);
    }

    public static double Mod(double a, double b) => b == 0 ? double.NaN : a - b * Math.Floor(a / b);

    public static double Choose(double n, double k)
    {
        if (double.IsNaN(n) || double.IsNaN(k)) return double.NaN;
        if (double.IsInteger(n) && double.IsInteger(k))
        {
            if (k < 0 || n < 0 || k > n) return 0;
            if (n <= 1000)
            {
                double r = 1;
                k = Math.Min(k, n - k);
                for (int i = 1; i <= (int)k; i++) r = r * (n - k + i) / i;
                return Math.Round(r) is var rounded && Math.Abs(rounded - r) < 1e-6 * Math.Max(1, r) ? rounded : r;
            }
        }
        return Math.Exp(LogGamma(n + 1) - LogGamma(k + 1) - LogGamma(n - k + 1));
    }
}

// --- the tree an expression is read into ------------------------------------------------------------------------------

/// <summary>A piece of a read expression. Every piece is pure: it only reads the slots and does arithmetic.</summary>
internal abstract class PlotNode
{
    public abstract double Eval(PlotEnv env);
    public virtual void Collect(HashSet<int> slots, HashSet<PlotFunction> seen)
    {
        foreach (var c in Children) c.Collect(slots, seen);
    }
    public virtual void Calls(HashSet<PlotFunction> calls)
    {
        foreach (var c in Children) c.Calls(calls);
    }
    protected virtual IEnumerable<PlotNode> Children => [];
}

sealed class ConstNode(double value) : PlotNode
{
    public double Value { get; } = value;
    public override double Eval(PlotEnv env) => Value;
}

sealed class SlotNode(int slot) : PlotNode
{
    public int Slot { get; } = slot;
    public override double Eval(PlotEnv env) => env.Slots[Slot];
    public override void Collect(HashSet<int> slots, HashSet<PlotFunction> seen) => slots.Add(Slot);
}

sealed class UnaryNode(Func<double, double> f, PlotNode a) : PlotNode
{
    public override double Eval(PlotEnv env) => f(a.Eval(env));
    protected override IEnumerable<PlotNode> Children => [a];
}

sealed class BinaryNode(char op, PlotNode a, PlotNode b) : PlotNode
{
    public override double Eval(PlotEnv env)
    {
        double x = a.Eval(env), y = b.Eval(env);
        return op switch
        {
            '+' => x + y,
            '-' => x - y,
            '*' => x * y,
            '/' => x / y,
            '^' => PlotMath.Pow(x, y),
            _ => double.NaN,
        };
    }
    protected override IEnumerable<PlotNode> Children => [a, b];
}

sealed class NegNode(PlotNode a) : PlotNode
{
    public override double Eval(PlotEnv env) => -a.Eval(env);
    protected override IEnumerable<PlotNode> Children => [a];
}

sealed class CallNode(Func<double, double, double> f, PlotNode a, PlotNode b) : PlotNode
{
    public override double Eval(PlotEnv env) => f(a.Eval(env), b.Eval(env));
    protected override IEnumerable<PlotNode> Children => [a, b];
}

sealed class MinMaxNode(bool max, PlotNode[] args) : PlotNode
{
    public override double Eval(PlotEnv env)
    {
        double r = args[0].Eval(env);
        for (int i = 1; i < args.Length; i++)
        {
            double v = args[i].Eval(env);
            if (double.IsNaN(v) || double.IsNaN(r)) return double.NaN;
            r = max ? Math.Max(r, v) : Math.Min(r, v);
        }
        return r;
    }
    protected override IEnumerable<PlotNode> Children => args;
}

sealed class CompareNode(string[] ops, PlotNode[] terms) : PlotNode
{
    public override double Eval(PlotEnv env)
    {
        double left = terms[0].Eval(env);
        for (int i = 0; i < ops.Length; i++)
        {
            double right = terms[i + 1].Eval(env);
            if (double.IsNaN(left) || double.IsNaN(right)) return double.NaN;
            bool ok = ops[i] switch
            {
                "<" => left < right,
                "<=" => left <= right,
                ">" => left > right,
                ">=" => left >= right,
                "==" or "=" => Math.Abs(left - right) <= 1e-12 * Math.Max(1, Math.Max(Math.Abs(left), Math.Abs(right))),
                _ => Math.Abs(left - right) > 1e-12 * Math.Max(1, Math.Max(Math.Abs(left), Math.Abs(right))),
            };
            if (!ok) return 0;
            left = right;
        }
        return 1;
    }
    protected override IEnumerable<PlotNode> Children => terms;
}

sealed class LogicNode(bool and, PlotNode a, PlotNode b) : PlotNode
{
    public override double Eval(PlotEnv env)
    {
        double x = a.Eval(env);
        if (double.IsNaN(x)) return double.NaN;
        bool l = x != 0;
        if (and && !l) return 0;
        if (!and && l) return 1;
        double y = b.Eval(env);
        return double.IsNaN(y) ? double.NaN : y != 0 ? 1 : 0;
    }
    protected override IEnumerable<PlotNode> Children => [a, b];
}

sealed class NotNode(PlotNode a) : PlotNode
{
    public override double Eval(PlotEnv env)
    {
        double x = a.Eval(env);
        return double.IsNaN(x) ? double.NaN : x == 0 ? 1 : 0;
    }
    protected override IEnumerable<PlotNode> Children => [a];
}

sealed class IfNode(PlotNode test, PlotNode then, PlotNode otherwise) : PlotNode
{
    public override double Eval(PlotEnv env)
    {
        double t = test.Eval(env);
        if (double.IsNaN(t)) return double.NaN;
        return t != 0 ? then.Eval(env) : otherwise.Eval(env);
    }
    protected override IEnumerable<PlotNode> Children => [test, then, otherwise];
}

sealed class FactorialNode(PlotNode a) : PlotNode
{
    public override double Eval(PlotEnv env) => PlotMath.Gamma(a.Eval(env) + 1);
    protected override IEnumerable<PlotNode> Children => [a];
}

/// <summary>sum(k, a, b, body) and prod(k, a, b, body): over the whole numbers from a to b, at most
/// <see cref="MaxTerms"/> of them, each counted against the evaluation's budget.</summary>
sealed class LoopNode(bool product, int slot, PlotNode from, PlotNode to, PlotNode body) : PlotNode
{
    public const int MaxTerms = 100_000;

    public override double Eval(PlotEnv env)
    {
        double a = from.Eval(env), b = to.Eval(env);
        if (!double.IsFinite(a) || !double.IsFinite(b)) return double.NaN;
        double first = Math.Ceiling(a - 1e-9), last = Math.Floor(b + 1e-9);
        if (last - first + 1 > MaxTerms) return double.NaN;
        double saved = env.Slots[slot], r = product ? 1 : 0;
        try
        {
            for (double k = first; k <= last; k++)
            {
                if (--env.Budget < 0) return double.NaN;
                env.Slots[slot] = k;
                double v = body.Eval(env);
                r = product ? r * v : r + v;
            }
        }
        finally
        {
            env.Slots[slot] = saved;
        }
        return r;
    }
    protected override IEnumerable<PlotNode> Children => [from, to, body];
}

/// <summary>A call to one of the plot's own curves (or a sequence's term, or a curve's derivative).</summary>
sealed class UserCallNode(PlotFunction f, PlotNode[] args, int derivative) : PlotNode
{
    public override double Eval(PlotEnv env)
    {
        if (--env.Budget < 0) return double.NaN;
        if (f.Sequence) return f.Term(env, args[0].Eval(env));
        if (f.Body is not { } body) return double.NaN;
        if (derivative == 0)
        {
            if (args.Length == 1) return At(env, body, args[0].Eval(env));
            var values = new double[args.Length];
            for (int i = 0; i < args.Length; i++) values[i] = args[i].Eval(env);
            var saved = new double[args.Length];
            for (int i = 0; i < args.Length; i++) saved[i] = env.Slots[f.Args[i]];
            try
            {
                for (int i = 0; i < args.Length; i++) env.Slots[f.Args[i]] = values[i];
                return body.Eval(env);
            }
            finally
            {
                for (int i = 0; i < args.Length; i++) env.Slots[f.Args[i]] = saved[i];
            }
        }
        double x = args[0].Eval(env);
        if (!double.IsFinite(x)) return double.NaN;
        if (derivative == 1)
        {
            double h = 1e-5 * Math.Max(1, Math.Abs(x));
            return (At(env, body, x + h) - At(env, body, x - h)) / (2 * h);
        }
        double h2 = 1e-3 * Math.Max(1, Math.Abs(x));
        return (At(env, body, x + h2) - 2 * At(env, body, x) + At(env, body, x - h2)) / (h2 * h2);
    }

    double At(PlotEnv env, PlotNode body, double x)
    {
        int slot = f.Args[0];
        double saved = env.Slots[slot];
        env.Slots[slot] = x;
        try
        {
            return body.Eval(env);
        }
        finally
        {
            env.Slots[slot] = saved;
        }
    }

    public override void Collect(HashSet<int> slots, HashSet<PlotFunction> seen)
    {
        base.Collect(slots, seen);
        if (!seen.Add(f)) return;
        // What the curve itself reads, but not its own argument (that's set by the call).
        var inner = new HashSet<int>();
        f.Body?.Collect(inner, seen);
        f.From?.Root.Collect(inner, seen);
        f.To?.Root.Collect(inner, seen);
        foreach (int a in f.Args) inner.Remove(a);
        slots.UnionWith(inner);
    }

    public override void Calls(HashSet<PlotFunction> calls)
    {
        base.Calls(calls);
        calls.Add(f);
    }

    protected override IEnumerable<PlotNode> Children => args;
}

// --- reading an expression ---------------------------------------------------------------------------------------------

/// <summary>
/// Reads an expression from a line's pieces into a <see cref="PlotExpression"/>: the usual precedence (comparisons,
/// then + and −, then × ÷ and multiplying by writing side by side, then a leading minus, then powers, which go
/// right to left), function calls with or without brackets (<c>sin x</c>, <c>sin 2x</c>, <c>sin^2 x</c>),
/// <c>|x|</c>, <c>n!</c>, <c>\frac{a}{b}</c>, <c>f'(x)</c> for one of the plot's own curves. A name it doesn't know is
/// read as names it does, side by side (<c>kx</c> is k times x), or it says which. Too long or too deeply nested
/// and it says so rather than reading on.
/// </summary>
public sealed class PlotExprReader
{
    public const int MaxTokens = 400, MaxDepth = 40, MaxNodes = 800;

    readonly List<PlotToken> t;
    readonly string line;
    readonly PlotScope scope;
    int pos, end, depth, nodes, absDepth;
    readonly Dictionary<string, int> bound = new(StringComparer.Ordinal);
    readonly IReadOnlyDictionary<string, int> locals;

    PlotExprReader(string line, List<PlotToken> tokens, int from, int to, PlotScope scope, IReadOnlyDictionary<string, int>? locals)
    {
        this.line = line;
        t = tokens.GetRange(from, to - from);
        pos = 0;
        end = t.Count;
        this.scope = scope;
        this.locals = locals ?? new Dictionary<string, int>();
    }

    /// <summary>The expression in <paramref name="tokens"/>[<paramref name="from"/>..<paramref name="to"/>) of
    /// <paramref name="line"/>, read with the sliders and curves <paramref name="scope"/> knows and its own variables
    /// (<paramref name="locals"/>: x for a curve, t for a parametric one, n for a sequence…).</summary>
    public static PlotExpression Read(string line, List<PlotToken> tokens, int from, int to, PlotScope scope, IReadOnlyDictionary<string, int>? locals = null)
    {
        if (to <= from) throw new PlotException("an expression is missing");
        if (to - from > MaxTokens) throw new PlotException($"an expression longer than {MaxTokens} pieces");
        var r = new PlotExprReader(line, tokens, from, to, scope, locals);
        var root = r.Expr();
        if (r.pos < r.end) throw new PlotException($"didn't expect “{r.t[r.pos].Text}” in {Quote(line, tokens, from, to)}");
        return new PlotExpression(Text(line, tokens, from, to), Fold(root, scope), r.nodes);
    }

    /// <summary>Reads a whole string as one expression.</summary>
    public static PlotExpression Read(string text, PlotScope scope, IReadOnlyDictionary<string, int>? locals = null)
    {
        var tokens = PlotLexer.Lex(text);
        return Read(text, tokens, 0, tokens.Count, scope, locals);
    }

    /// <summary>The expression as written, its spaces made single.</summary>
    public static string Text(string line, List<PlotToken> tokens, int from, int to) =>
        string.Join(' ', line[tokens[from].Start..tokens[to - 1].End].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    static string Quote(string line, List<PlotToken> tokens, int from, int to) => "“" + Text(line, tokens, from, to) + "”";

    string Here => Quote(line, t, 0, end);

    PlotException Error(string what) => new($"{what} in {Here}");

    PlotToken? Peek => pos < end ? t[pos] : null;

    bool At(string op) => pos < end && t[pos].Is(op);

    bool AtWord(string w) => pos < end && t[pos].IsWord(w);

    T Node<T>(T n) where T : PlotNode
    {
        if (++nodes > MaxNodes) throw Error("an expression too big to plot");
        return n;
    }

    void Enter()
    {
        if (++depth > MaxDepth) throw Error("brackets nested too deeply");
    }

    void Expect(string op)
    {
        if (!At(op)) throw Error(pos < end ? $"expected “{op}” but found “{t[pos].Text}”" : $"a missing “{op}”");
        pos++;
    }

    PlotNode Expr()
    {
        Enter();
        try
        {
            return Or();
        }
        finally
        {
            depth--;
        }
    }

    PlotNode Or()
    {
        var a = And();
        while (AtWord("or"))
        {
            pos++;
            a = Node(new LogicNode(false, a, And()));
        }
        return a;
    }

    PlotNode And()
    {
        var a = Not();
        while (AtWord("and"))
        {
            pos++;
            a = Node(new LogicNode(true, a, Not()));
        }
        return a;
    }

    PlotNode Not()
    {
        if (AtWord("not"))
        {
            pos++;
            return Node(new NotNode(Not()));
        }
        return Compare();
    }

    static readonly HashSet<string> Comparisons = ["<", "<=", ">", ">=", "==", "!="];

    PlotNode Compare()
    {
        var first = Add();
        if (!(Peek is { Kind: PlotTokenKind.Op } p && Comparisons.Contains(p.Text))) return first;
        var ops = new List<string>();
        var terms = new List<PlotNode> { first };
        while (Peek is { Kind: PlotTokenKind.Op } q && Comparisons.Contains(q.Text))
        {
            pos++;
            ops.Add(q.Text);
            terms.Add(Add());
        }
        return Node(new CompareNode([.. ops], [.. terms]));
    }

    PlotNode Add()
    {
        var a = Mul();
        while (At("+") || At("-"))
        {
            char op = t[pos++].Text[0];
            a = Node(new BinaryNode(op, a, Mul()));
        }
        return a;
    }

    PlotNode Mul()
    {
        var a = Unary();
        while (true)
        {
            if (At("*") || At("/"))
            {
                char op = t[pos++].Text[0];
                a = Node(new BinaryNode(op, a, Unary()));
            }
            else if (StartsFactor()) a = Node(new BinaryNode('*', a, Power()));
            else return a;
        }
    }

    /// <summary>Whether what's next starts a factor written straight after another (2x, 2(x+1), x sin x): a number,
    /// a name, an opening bracket, an opening |, \frac or \sqrt.</summary>
    bool StartsFactor()
    {
        if (Peek is not { } p) return false;
        return p.Kind switch
        {
            PlotTokenKind.Number => true,
            PlotTokenKind.Name => !PlotSyntax.StopWords.Contains(p.Text) && p.Text is not ("and" or "or" or "not"),
            PlotTokenKind.Command => true,
            PlotTokenKind.Op => p.Text is "(" or "{" or "[" || (p.Text == "|" && absDepth == 0),
            _ => false,
        };
    }

    PlotNode Unary()
    {
        if (At("-"))
        {
            pos++;
            Enter();
            try
            {
                return Node(new NegNode(Unary()));
            }
            finally
            {
                depth--;
            }
        }
        if (At("+"))
        {
            pos++;
            return Unary();
        }
        return Power();
    }

    PlotNode Power()
    {
        var b = Postfix(Primary());
        if (!At("^")) return b;
        pos++;
        Enter();
        try
        {
            return Node(new BinaryNode('^', b, Exponent()));
        }
        finally
        {
            depth--;
        }
    }

    /// <summary>A power's exponent: a leading minus, then a power (so 2^-x and 2^3^2 read as they're meant).</summary>
    PlotNode Exponent()
    {
        if (At("-"))
        {
            pos++;
            return Node(new NegNode(Exponent()));
        }
        if (At("+"))
        {
            pos++;
            return Exponent();
        }
        return Power();
    }

    PlotNode Postfix(PlotNode a)
    {
        while (At("!"))
        {
            pos++;
            a = Node(new FactorialNode(a));
        }
        return a;
    }

    PlotNode Primary()
    {
        if (Peek is not { } p) throw Error("something missing at the end");
        Enter();
        try
        {
            switch (p.Kind)
            {
                case PlotTokenKind.Number:
                    pos++;
                    return Node(new ConstNode(p.Value));
                case PlotTokenKind.Op when p.Text is "(" or "{" or "[":
                {
                    pos++;
                    var inner = Expr();
                    Expect(p.Text == "(" ? ")" : p.Text == "{" ? "}" : "]");
                    return inner;
                }
                case PlotTokenKind.Op when p.Text == "|":
                {
                    pos++;
                    absDepth++;
                    var inner = Expr();
                    absDepth--;
                    Expect("|");
                    return Node(new UnaryNode(Math.Abs, inner));
                }
                case PlotTokenKind.Command when p.Text == "frac":
                {
                    pos++;
                    var top = Group();
                    var bottom = Group();
                    return Node(new BinaryNode('/', top, bottom));
                }
                case PlotTokenKind.Command when p.Text == "sqrt":
                {
                    pos++;
                    PlotNode? index = null;
                    if (At("["))
                    {
                        pos++;
                        index = Expr();
                        Expect("]");
                    }
                    var under = Group();
                    return index is null ? Node(new UnaryNode(Math.Sqrt, under))
                        : Node(new BinaryNode('^', under, Node(new BinaryNode('/', Node(new ConstNode(1)), index))));
                }
                case PlotTokenKind.Name:
                    return Name();
                default:
                    throw Error(p.Kind == PlotTokenKind.Text ? $"a label “{p.Text}” where a number was expected" : $"didn't expect “{p.Text}”");
            }
        }
        finally
        {
            depth--;
        }
    }

    /// <summary>A LaTeX argument: {…}, or a single piece (\frac12).</summary>
    PlotNode Group()
    {
        if (At("{"))
        {
            pos++;
            var inner = Expr();
            Expect("}");
            return inner;
        }
        if (Peek is { Kind: PlotTokenKind.Number } n && n.Text.All(char.IsAsciiDigit))
        {
            // \frac12: one digit is one argument, as in LaTeX.
            if (n.Text.Length == 1) pos++;
            else t[pos] = n with { Text = n.Text[1..], Value = double.Parse(n.Text[1..], System.Globalization.CultureInfo.InvariantCulture) };
            return Node(new ConstNode(n.Text[0] - '0'));
        }
        return Primary();
    }

    PlotNode Name()
    {
        var p = t[pos];
        string name = p.Text;
        if (PlotSyntax.StopWords.Contains(name)) throw Error($"“{name}” where a number was expected");
        bool call = pos + 1 < end && t[pos + 1].Is("(");
        if (bound.TryGetValue(name, out int loop))
        {
            pos++;
            return Node(new SlotNode(loop));
        }
        if ((locals.TryGetValue(name, out int slot) || scope.Params.TryGetValue(name, out slot)) && !(PlotBuiltins.Has(name) && call))
        {
            pos++;
            return Node(new SlotNode(slot));
        }
        // Γ(x), and γ(x) where γ isn't a slider, is the gamma function.
        if (name is "Γ" or "γ" && call)
        {
            pos++;
            return Builtin("gamma");
        }
        if (scope.Functions.TryGetValue(name, out var f))
        {
            pos++;
            return UserCall(f);
        }
        switch (name)
        {
            case "pi":
                pos++;
                return Node(new ConstNode(Math.PI));
            case "e":
                pos++;
                return Node(new ConstNode(Math.E));
        }
        if (PlotBuiltins.Has(name))
        {
            pos++;
            return Builtin(name);
        }
        // A run of names written together (kx, 2πx is already split): read as those names side by side.
        if (Split(name) is { } parts)
        {
            var pieces = parts.Select((s, i) => new PlotToken(PlotTokenKind.Name, s, 0, p.Start, p.End)).ToList();
            t.RemoveAt(pos);
            t.InsertRange(pos, pieces);
            end += pieces.Count - 1;
            return Name();
        }
        throw Error($"“{name}”, a name the plot doesn't have (a slider needs a param line)");
    }

    /// <summary>A name made of known names written together (kx, ax, σx, sinx): the fewest pieces, longest first;
    /// null when it can't be.</summary>
    List<string>? Split(string name)
    {
        if (name.Contains('_') || name.Length > 24) return null;
        bool Known(string s) => bound.ContainsKey(s) || locals.ContainsKey(s) || scope.Params.ContainsKey(s) || scope.Functions.ContainsKey(s) || s is "pi" or "e" || PlotBuiltins.Has(s);
        var best = new List<string>?[name.Length + 1];
        best[0] = [];
        for (int i = 0; i < name.Length; i++)
        {
            if (best[i] is not { } so) continue;
            for (int j = name.Length; j > i; j--)
            {
                string piece = name[i..j];
                string norm = PlotLexer.Greek.TryGetValue(piece, out var g) ? g : piece;
                if (!Known(norm)) continue;
                if (best[j] is null || best[j]!.Count > so.Count + 1) best[j] = [.. so, norm];
            }
        }
        return best[name.Length] is { Count: > 1 } parts ? parts : null;
    }

    PlotNode UserCall(PlotFunction f)
    {
        int primes = 0;
        while (At("'"))
        {
            pos++;
            primes++;
        }
        if (primes > 2) throw Error($"more than two primes on {f.Name}");
        if (!At("(")) throw Error($"{f.Name} without its argument (write {f.Name}(x))");
        var args = Arguments();
        if (args.Count != f.Arity) throw Error($"{f.Name} given {args.Count} argument{(args.Count == 1 ? "" : "s")}, but it takes {f.Arity}");
        if (primes > 0 && (f.Arity != 1 || f.Sequence)) throw Error($"a derivative of {f.Name}, which only a curve of one variable has");
        return Node(new UserCallNode(f, [.. args], primes));
    }

    List<PlotNode> Arguments()
    {
        Expect("(");
        var args = new List<PlotNode>();
        if (At(")"))
        {
            pos++;
            return args;
        }
        while (true)
        {
            args.Add(Expr());
            if (At(","))
            {
                pos++;
                continue;
            }
            Expect(")");
            return args;
        }
    }

    PlotNode Builtin(string name)
    {
        if (PlotBuiltins.IsSpecial(name)) return Special(name);
        // sin^2 x: the function's value, squared.
        PlotNode? power = null;
        if (At("^"))
        {
            pos++;
            power = Exponent();
            if (power is NegNode { } && PlotBuiltins.Unary(name) is not null) throw Error($"{name}^-1 (write a{name}(x) for the inverse)");
        }
        PlotNode call;
        if (At("("))
        {
            var args = Arguments();
            call = Apply(name, args);
        }
        else
        {
            if (PlotBuiltins.Unary(name) is null) throw Error($"{name} without its brackets");
            call = Apply(name, [ImplicitArgument()]);
        }
        return power is null ? call : Node(new BinaryNode('^', call, power));
    }

    /// <summary>The argument of a function written without brackets: what's written side by side after it (sin 2x is
    /// sin(2x)), up to the next operator or the next function (sin x cos x is sin(x) times cos(x)).</summary>
    PlotNode ImplicitArgument()
    {
        if (Peek is null) throw Error("a function with nothing after it");
        PlotNode a;
        if (At("-"))
        {
            pos++;
            a = Node(new NegNode(Power()));
        }
        else a = Power();
        while (StartsFactor() && !(Peek is { Kind: PlotTokenKind.Name } n && (PlotBuiltins.Has(n.Text) || scope.Functions.ContainsKey(n.Text))))
            a = Node(new BinaryNode('*', a, Power()));
        return a;
    }

    PlotNode Apply(string name, List<PlotNode> args)
    {
        if (PlotBuiltins.IsMany(name))
        {
            if (args.Count == 0) throw Error($"{name}() with nothing to compare");
            return args.Count == 1 ? args[0] : Node(new MinMaxNode(name == "max", [.. args]));
        }
        if (PlotBuiltins.Unary(name) is { } one)
        {
            if (args.Count != 1) throw Error($"{name} given {args.Count} arguments, but it takes one");
            return Node(new UnaryNode(one, args[0]));
        }
        if (PlotBuiltins.Binary(name) is { } two)
        {
            if (args.Count != 2) throw Error($"{name} given {args.Count} argument{(args.Count == 1 ? "" : "s")}, but it takes two");
            return Node(new CallNode(two, args[0], args[1]));
        }
        throw Error($"“{name}”, which isn't a function");
    }

    PlotNode Special(string name)
    {
        if (!At("(")) throw Error($"{name} without its brackets");
        if (name is "sum" or "prod")
        {
            pos++;
            if (Peek is not { Kind: PlotTokenKind.Name } v || scope.Functions.ContainsKey(v.Text) || v.Text is "pi" or "e")
                throw Error($"{name} needs its counter first: {name}(k, 1, n, …)");
            pos++;
            if (At("=")) pos++; // sum(k = 1, n, …) reads the same
            else Expect(",");
            var from = Expr();
            Expect(",");
            var to = Expr();
            Expect(",");
            int slot = scope.Slot("\u0001" + v.Text + bound.Count);
            bool had = bound.TryGetValue(v.Text, out int before);
            bound[v.Text] = slot;
            var body = Expr();
            if (had) bound[v.Text] = before;
            else bound.Remove(v.Text);
            Expect(")");
            return Node(new LoopNode(name == "prod", slot, from, to, body));
        }
        var args = Arguments();
        if (name == "if")
        {
            if (args.Count != 3) throw Error("if needs three parts: if(test, value when true, value when false)");
            return Node(new IfNode(args[0], args[1], args[2]));
        }
        if (args.Count != 3) throw Error("clamp needs three parts: clamp(x, low, high)");
        return Node(new MinMaxNode(false, [Node(new MinMaxNode(true, [args[0], args[1]])), args[2]]));
    }

    /// <summary>An expression that's only numbers (2π, 1/2) worked out once, so it isn't worked out at every point.</summary>
    static PlotNode Fold(PlotNode n, PlotScope scope)
    {
        if (n is ConstNode) return n;
        var slots = new HashSet<int>();
        n.Collect(slots, []);
        var calls = new HashSet<PlotFunction>();
        n.Calls(calls);
        if (slots.Count > 0 || calls.Count > 0) return n;
        var env = new PlotEnv(scope.SlotCount) { Budget = PlotEnv.MaxSteps };
        return new ConstNode(n.Eval(env));
    }
}

/// <summary>The words a plot's lines are made of, which can't be names in it.</summary>
public static class PlotSyntax
{
    /// <summary>Colours a curve can be drawn in, in the order curves get them.</summary>
    public static readonly string[] Colours = ["blue", "orange", "green", "purple", "pink"];

    /// <summary>Words that end an expression on a plot's line (and so can't be the names of sliders or curves).</summary>
    public static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "from", "to", "for", "at", "rate", "steps", "step", "blue", "orange", "green", "purple", "pink", "red", "grey", "gray",
        "dashed", "dotted", "morph", "ticks",
    };
}
