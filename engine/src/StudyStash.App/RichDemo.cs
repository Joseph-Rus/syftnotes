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
}
