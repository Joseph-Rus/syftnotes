namespace StudyStash.Core;

/// <summary>
/// The kinds of rich notes a student can switch on and off (Settings → AI engines → Rich notes): <see cref="Diagrams"/>
/// (flowcharts, state and sequence diagrams, timelines, mind maps), <see cref="Plots"/> (a curve or distribution drawn
/// from the lecturer's formula) and <see cref="Drawings"/> (labelled figures of what the lecture describes: SVG drawings
/// and illustrations). The designer never asks for a kind that's off, and drops one it's given anyway.
/// </summary>
[Flags]
public enum RichKinds
{
    None = 0,
    Diagrams = 1,
    Plots = 2,
    Drawings = 4,
    All = Diagrams | Plots | Drawings,
}
