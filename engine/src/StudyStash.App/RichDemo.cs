namespace StudyStash.App;

/// <summary>
/// Made-up notes that show off what the notes can draw (the pictures and the tests use them): nursing and computing
/// flowcharts of every kind the app lays out — cycles on a ring, a decision chart, a process with a group, and trees.
/// </summary>
public static class RichDemo
{
    public const string CardiacCycle = """
        flowchart LR
          A([Atrial systole]):::accent --> B[Isovolumetric contraction] --> C[Ventricular ejection]
          C --> D[Isovolumetric relaxation] --> E[Ventricular filling] --> A
        """;

    public const string BloodFlow = """
        flowchart LR
          RA["Right atrium"]:::blue -->|tricuspid valve| RV["Right ventricle"]:::blue -->|pulmonary valve| Lungs(("Lungs"))
          Lungs --> LA["Left atrium"]:::red -->|mitral valve| LV["Left ventricle"]:::red -->|aortic valve| Body(("Body"))
          Body --> RA
        """;

    public const string PainReassess = """
        flowchart TD
          A[Assess pain on a 0 to 10 scale] --> B{Score above 4?}
          B -->|yes| C[Give the prescribed analgesic]
          B -->|no| D[Reposition and use non-drug comfort measures]
          C --> E([Reassess in 30 to 60 minutes])
          D --> E
          E -.->|still in pain| A
        """;

    public const string NursingProcess = """
        flowchart TD
          subgraph assess [Assessment]
            A[Collect subjective and objective data] --> V[Validate and cluster the cues]
          end
          V --> D[Nursing diagnosis]:::accent
          D --> P[Plan goals and outcomes] --> I[Implement interventions] --> E{Outcomes met?}
          E -->|yes| R([Resolve the care plan])
          E -.->|no, reassess| A
        """;

    public const string TypesOfShock = """
        flowchart TD
          S[Shock]:::accent --> H[Hypovolaemic]
          S --> C[Cardiogenic]
          S --> D[Distributive]
          S --> O[Obstructive]
          H --> H1[Haemorrhage]
          H --> H2[Burns]
          D --> D1[Septic]
          D --> D2[Anaphylactic]
          D --> D3[Neurogenic]
          O --> O1[Tension pneumothorax]
        """;

    public const string SearchTree = """
        flowchart TD
          R((8)) --> L((3))
          R --> Q((10))
          L --> L1((1))
          L --> L2((6))
          Q --> Q2((14))
          L2 --> L3((4))
          L2 --> L4((7))
        """;

    /// <summary>Every sample, with the heading and the sentence that go with it in a note.</summary>
    public static IReadOnlyList<(string Title, string Words, string Source)> Diagrams { get; } =
    [
        ("The cardiac cycle", "Each beat runs through five phases, starting when the atria contract.", CardiacCycle),
        ("Blood flow through the heart", "Oxygen-poor blood (blue) goes through the right heart to the lungs; oxygen-rich blood (red) through the left heart to the body.", BloodFlow),
        ("Pain: assess, act, reassess", "Treat by the score, then always reassess, and go round again while the pain stays.", PainReassess),
        ("The nursing process", "Assessment feeds the diagnosis and the plan; evaluation sends you back to assess when the outcomes aren't met.", NursingProcess),
        ("Types of shock", "Four families, grouped by what fails: the volume, the pump, the vessels, or the way out of the heart.", TypesOfShock),
        ("A binary search tree", "Smaller keys go left and larger keys go right, so 4 sits under 6, under 3.", SearchTree),
    ];

    /// <summary>The four chambers of the heart, drawn as an AI writes an SVG: the writers' palette (blue for the
    /// oxygen-poor right side, red for the left), arrows with a marker, the valves and the septum labelled.</summary>
    public const string FourChambers = """
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 640 380" font-size="14">
          <title>The four chambers of the heart</title>
          <defs><marker id="arr" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="7" markerHeight="7" orient="auto-start-reverse"><path d="M0,0 L10,5 L0,10 z" fill="#1D1D1F"/></marker></defs>
          <rect x="150" y="60" width="160" height="110" rx="14" fill="#E8F0FE" stroke="#1A73E8" stroke-width="2"/>
          <text x="230" y="112" text-anchor="middle" fill="#1D1D1F" font-weight="600">Right atrium</text>
          <text x="230" y="132" text-anchor="middle" fill="#6E6E73" font-size="12">from the body</text>
          <rect x="330" y="60" width="160" height="110" rx="14" fill="#FCE8E6" stroke="#D93025" stroke-width="2"/>
          <text x="410" y="112" text-anchor="middle" fill="#1D1D1F" font-weight="600">Left atrium</text>
          <text x="410" y="132" text-anchor="middle" fill="#6E6E73" font-size="12">from the lungs</text>
          <rect x="150" y="200" width="160" height="130" rx="14" fill="#E8F0FE" stroke="#1A73E8" stroke-width="2"/>
          <text x="230" y="262" text-anchor="middle" fill="#1D1D1F" font-weight="600">Right ventricle</text>
          <text x="230" y="282" text-anchor="middle" fill="#6E6E73" font-size="12">to the lungs</text>
          <rect x="330" y="200" width="160" height="130" rx="14" fill="#FCE8E6" stroke="#D93025" stroke-width="2"/>
          <text x="410" y="262" text-anchor="middle" fill="#1D1D1F" font-weight="600">Left ventricle</text>
          <text x="410" y="282" text-anchor="middle" fill="#6E6E73" font-size="12">to the body</text>
          <line x1="230" y1="170" x2="230" y2="198" stroke="#1D1D1F" stroke-width="2" marker-end="url(#arr)"/>
          <line x1="410" y1="170" x2="410" y2="198" stroke="#1D1D1F" stroke-width="2" marker-end="url(#arr)"/>
          <text x="140" y="190" text-anchor="end" fill="#6E6E73" font-size="12">tricuspid valve</text>
          <text x="500" y="190" fill="#6E6E73" font-size="12">mitral valve</text>
          <line x1="320" y1="40" x2="320" y2="350" stroke="#C7C7CC" stroke-width="1.5" stroke-dasharray="5 4"/>
          <text x="320" y="30" text-anchor="middle" fill="#6E6E73" font-size="12">septum</text>
        </svg>
        """;

    /// <summary>A diagram Study Stash doesn't draw (a sequence diagram), for the calm card that shows its source.</summary>
    public const string PainConversation = """
        sequenceDiagram
          Nurse->>Patient: How bad is the pain, 0 to 10?
          Patient-->>Nurse: About a 7
          Nurse->>Patient: I'll bring your analgesic and check back in 30 minutes
        """;

    /// <summary>A flowchart with a box left open, for the card that says which line.</summary>
    public const string BrokenChart = """
        flowchart TD
          A[Check the order] --> B[Give the dose
          B --> C[Document it]
        """;

    static string Fence(string info, string source) => $"```{info}\n{source.TrimEnd()}\n```";

    /// <summary>The part of a nursing lecture's notes where its diagrams are: a ring, a drawing, a decision chart,
    /// each with the sentence that says the same in words.</summary>
    public static string DiagramNotes { get; } = string.Join("\n\n",
        "## Details and examples",
        "Each beat runs through five phases, starting when the atria contract.",
        Fence("mermaid", CardiacCycle),
        "The atria contract, the ventricles tense and then eject, relax, and fill again, and the cycle repeats.",
        Fence("svg", FourChambers),
        "Oxygen-poor blood fills the right side and goes to the lungs; oxygen-rich blood fills the left side and goes to the body.",
        Fence("mermaid", PainReassess),
        "Treat by the score, then always reassess, and go round again while the pain stays.");

    /// <summary>What a note shows when a diagram can't be drawn: a sequence diagram, and a chart with a box left open.</summary>
    public static string FallbackNotes { get; } = string.Join("\n\n",
        "## Talking about pain",
        "The conversation, as the lecturer drew it on the board:",
        Fence("mermaid", PainConversation),
        "And the order check, which lost a bracket on the way:",
        Fence("mermaid", BrokenChart));

    /// <summary>Cardiac output and mean arterial pressure, inline in a sentence.</summary>
    public const string CardiacOutputInline =
        """Cardiac output is $\text{CO} = \text{HR} \times \text{SV}$, typically 4 to 8 litres a minute; mean arterial pressure, $\text{MAP} = \text{DBP} + \frac{1}{3}(\text{SBP} - \text{DBP})$, should stay above 65 mmHg.""";

    /// <summary>A dose calculation, worked step by step, the way the AI prompt's own example does.</summary>
    public const string DoseFormula = """
        $$
        \text{Volume} = \frac{\text{Desired}}{\text{Have}} \times \text{Quantity} = \frac{500\ \text{mg}}{250\ \text{mg}} \times 5\ \text{mL} = 10\ \text{mL}
        $$
        """;

    /// <summary>The MAP formula again, this time as an <c>aligned</c> block worked through line by line.</summary>
    public const string AlignedFormula = """
        $$
        \begin{aligned}
        \text{MAP} &= \text{DBP} + \frac{1}{3}(\text{SBP} - \text{DBP}) \\
        &= 80 + \frac{1}{3}(120 - 80) \\
        &= 93.3\ \text{mmHg}
        \end{aligned}
        $$
        """;

    /// <summary>A <c>cases</c> block: the shock index read differently depending on its value.</summary>
    public const string CasesFormula = """
        $$
        \text{Shock index} =
        \begin{cases}
        \text{normal} & \text{if} < 0.7 \\
        \text{early shock} & \text{if } 0.7 \text{ to } 1.0 \\
        \text{critical} & \text{if} > 1.0
        \end{cases}
        $$
        """;

    /// <summary>A chemistry formula, through <c>\ce{…}</c>: carbon dioxide and water forming carbonic acid, both ways.</summary>
    public const string ChemFormula = """
        $$
        \ce{CO2 + H2O <=> H2CO3}
        $$
        """;

    /// <summary>The part of a nursing lecture's notes where its formulas are.</summary>
    public static string FormulaNotes { get; } = string.Join("\n\n",
        "## Key points",
        CardiacOutputInline,
        "## Details and examples",
        "A dose calculation, worked step by step:",
        DoseFormula,
        "The same formula again, this time worked through line by line:",
        AlignedFormula,
        "A shock index reads differently depending on its value:",
        CasesFormula,
        "Carbon dioxide and water form carbonic acid in the blood, and the reaction runs both ways:",
        ChemFormula);

    /// <summary>An inline formula that lost a brace, and the same mistake on its own line: CSharpMath can't typeset
    /// either, so both show their source instead.</summary>
    public const string BrokenInlineFormula = """An unclosed fraction, $\frac{a}{$, breaks CSharpMath, so its source shows instead.""";

    public const string BrokenDisplayFormula = """
        $$
        \frac{a}{
        $$
        """;

    /// <summary>What a note shows when a formula can't be typeset: its source, inline and on its own line.</summary>
    public static string FormulaFallbackNotes { get; } = string.Join("\n\n",
        "## A formula that won't typeset",
        BrokenInlineFormula,
        "And on its own line:",
        BrokenDisplayFormula);
}
