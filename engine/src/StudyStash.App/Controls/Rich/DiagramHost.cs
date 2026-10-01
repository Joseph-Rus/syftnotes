using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using StudyStash.Core;

namespace StudyStash.App.Controls.Rich;

/// <summary>
/// What the page a diagram sits on can do with it: put a question about one of its boxes to the lecture's Ask bar, and
/// show the lecture at a moment (its transcript there, or its recording from there). The library's lecture page is one;
/// a diagram on a page that isn't (a quick answer, the recorder's chat) offers none of this rather than something that
/// goes nowhere.
/// </summary>
public interface IDiagramHost
{
    /// <summary>Whether a question can be asked here now.</summary>
    bool CanAsk { get; }

    /// <summary>Asks <paramref name="question"/> in the lecture's Ask bar, the answer showing above it.</summary>
    void Ask(string question);

    /// <summary>The lecture's transcript as timed lines (none when it has no times).</summary>
    IReadOnlyList<Spoken> Transcript { get; }

    /// <summary>Shows the transcript at <paramref name="seconds"/>, that line marked.</summary>
    void ShowTranscript(double seconds);

    /// <summary>Whether the recording is on this computer, to play from a moment.</summary>
    bool CanPlay { get; }

    /// <summary>Plays the recording from a moment (a couple of seconds before, so the sentence is heard whole).</summary>
    void Play(double seconds);
}

/// <summary>Where a diagram finds its <see cref="IDiagramHost"/>: set on any control around it (the lecture page sets it
/// on its notes).</summary>
public static class DiagramHost
{
    public static readonly AttachedProperty<IDiagramHost?> HostProperty =
        AvaloniaProperty.RegisterAttached<Control, IDiagramHost?>("Host", typeof(DiagramHost));

    public static IDiagramHost? GetHost(Control control) => control.GetValue(HostProperty);

    public static void SetHost(Control control, IDiagramHost? value) => control.SetValue(HostProperty, value);

    /// <summary>The host around <paramref name="control"/>, nearest first (along the page it's shown on, or the
    /// controls it was built into); null when it's on no lecture's page.</summary>
    public static IDiagramHost? Find(Control? control)
    {
        if (control is null) return null;
        foreach (var v in control.GetSelfAndVisualAncestors().OfType<Control>())
            if (GetHost(v) is { } host) return host;
        foreach (var l in control.GetSelfAndLogicalAncestors().OfType<Control>())
            if (GetHost(l) is { } host) return host;
        return null;
    }
}
