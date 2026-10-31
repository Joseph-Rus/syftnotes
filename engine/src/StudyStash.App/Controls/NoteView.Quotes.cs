using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StudyStash.App.ViewModels;

namespace StudyStash.App.Controls;

/// <summary>
/// The words an answer quoted from these notes, highlighted in them like a highlighter pen (<see cref="Highlights"/>):
/// found again in the notes' text whenever they're built (so a resize, a new theme, or diagrams arriving don't lose them),
/// stronger for the one the pointer is on in its chip, and brought into view with a flash when its chip is pressed. A
/// quote the text doesn't hold is left out.
/// </summary>
public sealed partial class NoteView
{
    /// <summary>What the lecture's answer being read quotes, to highlight here; set only on the view of the lecture's own
    /// notes (a rewrite's comparison, an answer's own text, set none).</summary>
    public static readonly StyledProperty<NoteHighlights?> HighlightsProperty = AvaloniaProperty.Register<NoteView, NoteHighlights?>(nameof(Highlights));

    public NoteHighlights? Highlights
    {
        get => GetValue(HighlightsProperty);
        set => SetValue(HighlightsProperty, value);
    }

    NoteHighlights? listening;

    /// <summary>The quotes the highlights are of now, so a change of the strong one doesn't look them up again.</summary>
    IReadOnlyList<string>? marked;

    void QuotesChanged(AvaloniaPropertyChangedEventArgs change)
    {
        if (change.Property == HighlightsProperty)
        {
            Listen();
            Highlights?.SetNotes(Markdown ?? "");
            MarkQuotes(true);
        }
        else if (change.Property == MarkdownProperty) Highlights?.SetNotes(Markdown ?? "");
    }

    /// <summary>Hears the highlights while this view is on screen: no more once it's gone, so a page that's left isn't kept
    /// alive by the Ask bar's.</summary>
    void Listen()
    {
        if (listening is { } old)
        {
            old.Changed -= Restyle;
            old.Revealing -= Reveal;
            listening = null;
        }
        if (Highlights is { } now && VisualRoot is not null)
        {
            now.Changed += Restyle;
            now.Revealing += Reveal;
            listening = now;
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Listen();
        MarkQuotes(true);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        flash?.Stop();
        Listen();
    }

    void Restyle() => MarkQuotes(!ReferenceEquals(marked, Highlights?.Quotes));

    static string Words(NoteText t) => t.Inlines is { Count: > 0 } inlines ? inlines.Text ?? "" : t.Text ?? "";

    /// <summary>Quote <c>n</c>'s place now: its piece and the characters in it.</summary>
    List<(NoteText Piece, int Start, int Length, int Quote)> places = [];

    /// <summary>Finds each quote in the notes' pieces (when <paramref name="find"/>; else only the strong one changes) and
    /// has the pieces draw them. The same words first (the first piece that has them), else the near match that's closest.</summary>
    void MarkQuotes(bool find)
    {
        if (Highlights is null && places.Count == 0) return; // (an answer's own text is rebuilt with every word: nothing to do there)
        var pieces = this.GetVisualDescendants().OfType<NoteText>().ToList();
        var quotes = Highlights?.Quotes ?? [];
        if (find)
        {
            marked = Highlights?.Quotes;
            places = [];
            for (int q = 0; q < quotes.Count && pieces.Count > 0; q++)
            {
                (NoteText Piece, QuoteFinder.Match Match)? best = null;
                foreach (var piece in pieces)
                {
                    if (QuoteFinder.Find(Words(piece), quotes[q]) is not { } m) continue;
                    if (best is null || m.Score > best.Value.Match.Score) best = (piece, m);
                    if (m.Score >= 1) break;
                }
                if (best is { } found) places.Add((found.Piece, found.Match.Start, found.Match.Length, q));
            }
        }
        var strong = new HashSet<int>();
        if (Highlights is { Strong: >= 0 } hl) strong.Add(hl.Strong);
        if (flashQuote >= 0 && flashTicks % 2 == 0) strong.Add(flashQuote);
        foreach (var piece in pieces)
            piece.Highlight([.. places.Where(p => p.Piece == piece).Select(p => (p.Start, p.Length, p.Quote))], strong);
    }

    DispatcherTimer? flash;
    int flashQuote = -1, flashTicks;

    /// <summary>Brings quote <paramref name="quote"/> into view, with room round it, and flashes it a couple of times.</summary>
    void Reveal(int quote)
    {
        if (places.FirstOrDefault(p => p.Quote == quote) is not { Piece: { } piece } place) return;
        var box = piece.TextLayout.HitTestTextRange(place.Start, place.Length).FirstOrDefault();
        piece.BringIntoView(new Rect(box.X, box.Y - 90, Math.Max(box.Width, 1), box.Height + 180));
        flash?.Stop();
        (flashQuote, flashTicks) = (quote, 0);
        flash = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(240) };
        flash.Tick += (_, _) =>
        {
            if (++flashTicks >= 5)
            {
                flash.Stop();
                flashQuote = -1;
            }
            MarkQuotes(false);
        };
        MarkQuotes(false);
        flash.Start();
    }
}
