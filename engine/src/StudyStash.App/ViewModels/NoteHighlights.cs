using CommunityToolkit.Mvvm.ComponentModel;
using StudyStash.App.Controls;
using StudyStash.Core.Ai;

namespace StudyStash.App.ViewModels;

/// <summary>
/// The words of a lecture's notes an answer points at: what ties the answer being read (<see cref="AiAskModel"/>) to
/// the notes on the page (<see cref="NoteView"/>, which draws them like a highlighter pen). The notes tell it what
/// they say (<see cref="SetNotes"/>), so a quote the notes don't hold gets no chip; the answer says which quotes are
/// being read (<see cref="Show"/>) and which one the pointer is on (<see cref="Hover"/>), or asks for one to be brought
/// into view (<see cref="Reveal"/>).
/// </summary>
public sealed class NoteHighlights
{
    /// <summary>The Markdown of the notes the page shows; "" before it says.</summary>
    public string Notes { get; private set; } = "";

    /// <summary>The quotes being read: highlighted in the notes. Empty for none.</summary>
    public IReadOnlyList<string> Quotes { get; private set; } = [];

    /// <summary>The one the pointer is on (in its chip), drawn stronger; -1 for none.</summary>
    public int Strong { get; private set; } = -1;

    /// <summary>The quotes shown or the one drawn strong changed.</summary>
    public event Action? Changed;

    /// <summary>The notes the page shows changed: what answers quoted may no longer be there.</summary>
    public event Action? NotesChanged;

    /// <summary>Quote <c>n</c> of the ones shown is to be brought into view, with a flash.</summary>
    public event Action<int>? Revealing;

    public void SetNotes(string markdown)
    {
        if (markdown == Notes) return;
        Notes = markdown;
        NotesChanged?.Invoke();
    }

    /// <summary>These are the quotes of the answer being read now (none: nothing is highlighted).</summary>
    public void Show(IReadOnlyList<string> quotes)
    {
        if (quotes.SequenceEqual(Quotes)) return;
        Quotes = quotes;
        Strong = -1;
        Changed?.Invoke();
    }

    /// <summary>The pointer is on quote <paramref name="index"/> (or, with -1, has left it).</summary>
    public void Hover(int index)
    {
        if (index == Strong) return;
        Strong = index;
        Changed?.Invoke();
    }

    public void Reveal(int index) => Revealing?.Invoke(index);

    /// <summary>Which of <paramref name="quotes"/> the notes hold, in order.</summary>
    public List<string> Found(IEnumerable<string> quotes) => [.. quotes.Where(q => QuoteFinder.InNotes(Notes, q))];
}

/// <summary>One of an answer's quotes as the chip under it shows it: its first few words, its whole in the tip. The
/// pointer on it makes its highlight in the notes stronger; pressing it brings that passage into view.</summary>
public sealed class QuoteChip
{
    readonly NoteHighlights notes;
    readonly IReadOnlyList<string> quotes;
    readonly int index;

    public QuoteChip(NoteHighlights notes, IReadOnlyList<string> quotes, int index)
    {
        (this.notes, this.quotes, this.index) = (notes, quotes, index);
        Quote = quotes[index];
        Full = AskQuotes.Label(Quote) ?? Quote;
        Text = Short(Full);
    }

    public string Quote { get; }

    /// <summary>The quote as a student reads it: a formula in it as plain symbols, not LaTeX (<see cref="AskQuotes.Label"/>).</summary>
    public string Full { get; }
    public string Text { get; }

    /// <summary>Whether a quote earns a chip: not one that's only a formula, whose highlight in the notes is enough.</summary>
    public static bool Readable(string quote) => AskQuotes.Label(quote) is not null;

    /// <summary>The first words of a quote, cut at a word, with an ellipsis when it goes on.</summary>
    public static string Short(string quote, int most = 34)
    {
        if (quote.Length <= most) return quote;
        int cut = quote.LastIndexOf(' ', most);
        return quote[..(cut > most / 2 ? cut : most)].TrimEnd(',', ';', ':', '.') + "…";
    }

    /// <summary>The pointer came onto the chip, or left it: its passage in the notes goes stronger, or back.</summary>
    public void Hover(bool over)
    {
        if (over) Reading();
        notes.Hover(over ? index : -1);
    }

    /// <summary>Pressed: the notes scroll to the passage, which flashes.</summary>
    public void Go()
    {
        Reading();
        notes.Reveal(index);
    }

    /// <summary>A chip of an answer that isn't the one being read (an earlier one in the card) makes it the one.</summary>
    void Reading() => notes.Show(quotes);
}
