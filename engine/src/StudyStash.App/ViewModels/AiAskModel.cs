using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StudyStash.Core;
using StudyStash.Core.Ai;
using StudyStash.Core.Rich;

namespace StudyStash.App.ViewModels;

/// <summary>One row of the "Answer with" / "Rewrite notes with" menu (<see cref="EngineMenuModel"/>): a name and
/// subtitle, whether it's checked (the library's default, or the notes' writer), whether it's the one currently
/// picked (tinted), and whether picking it is allowed at all.</summary>
public sealed partial class EngineMenuItem(string id, string name, string subtitle, bool @checked, bool enabled) : ObservableObject
{
    public string Id { get; } = id;
    public string Name { get; } = name;
    /// <summary>The engine's own mark (Claude's, ChatGPT's), beside its name.</summary>
    public string Icon => AiWords.EngineIcon(Id);
    public string Subtitle { get; } = subtitle;
    public bool Checked { get; } = @checked;
    public bool Enabled { get; } = enabled;
    [ObservableProperty] public partial bool Selected { get; set; }
    public IRelayCommand Command { get; internal set; } = null!;
}

/// <summary>One thing a menu does besides picking an engine ("Copy for your AI", "Paste notes", "Put back earlier
/// notes"): its icon, its words, a quieter line under them, and what it does.</summary>
public sealed class MenuAction(string glyph, string name, string subtitle, IRelayCommand command)
{
    public string Glyph { get; } = glyph;
    public string Name { get; } = name;
    public string Subtitle { get; } = subtitle;
    public IRelayCommand Command { get; } = command;
}

/// <summary>A menu of engines, shared by the Ask bar's "Answer with" and the notes' "Rewrite notes with": a header,
/// the rows, then anything else it does (<see cref="Actions"/>), and an optional footer link. The view is one control
/// (<c>{Mac,Win}AiEngineMenu</c>) over this.</summary>
public sealed partial class EngineMenuModel : ObservableObject
{
    public EngineMenuModel()
    {
        Items.CollectionChanged += (_, _) => Changed();
        Actions.CollectionChanged += (_, _) => Changed();
    }

    void Changed()
    {
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(HasActions));
        OnPropertyChanged(nameof(ShowActionsLine));
        OnPropertyChanged(nameof(ShowFooter));
    }

    public string Header { get; init; } = "";
    public ObservableCollection<EngineMenuItem> Items { get; } = [];
    /// <summary>The header and the engines show only when there are engines to pick (a student with no AI on the
    /// library's computer has only the actions).</summary>
    public bool HasItems => Items.Count > 0;
    public ObservableCollection<MenuAction> Actions { get; } = [];
    public bool HasActions => Actions.Count > 0;
    /// <summary>A line between the engines and the actions, when there are both.</summary>
    public bool ShowActionsLine => HasItems && HasActions;
    public string FooterText { get; init; } = "";
    public IRelayCommand? FooterCommand { get; init; }
    /// <summary>The footer is about the engines: it goes with them.</summary>
    public bool ShowFooter => HasItems && FooterText.Length > 0;
    /// <summary>260 for "Answer with", 280 for "Rewrite notes with" (the design's Mac widths).</summary>
    public double Width { get; init; } = 260;
}

/// <summary>A piece of an answer's byline as it's shown: its words ("Claude · from ", " and "), or one of the moments
/// it names ("01:52"), a link of its own that opens the transcript there.</summary>
public sealed class BylinePart(string text, Action? open = null, bool moment = false)
{
    public string Text { get; } = text;
    /// <summary>One of the moments the answer drew on: it reads as a link.</summary>
    public bool IsMoment { get; } = moment;
    /// <summary>What pressing it does; null for the words between.</summary>
    public IRelayCommand? Open { get; } = open is null ? null : new RelayCommand(open);
    public bool CanOpen => Open is not null;
    /// <summary>What a moment does, under the pointer.</summary>
    public string? Tip => IsMoment ? AiWords.MomentTip(Text) : null;
    /// <summary>What a screen reader says for it.</summary>
    public string Said => Tip ?? Text;
}

/// <summary>One question and its answer in an ask thread: what was asked, of which engine, and once it comes back,
/// the answer, its byline, a fell-back note, or why it failed. A turn never edits itself back to thinking.</summary>
public sealed partial class AiTurn : ObservableObject
{
    public string Question { get; }
    /// <summary>The engine's name at the moment this was asked (the thinking line names it, even if the answer
    /// ends up crediting a different one after a fallback).</summary>
    public string AskedName { get; }
    [ObservableProperty] public partial string? Answer { get; set; }
    [ObservableProperty] public partial string? Byline { get; set; }
    /// <summary>The byline as it's shown: all words, until the ask bar that can open its sources gives each moment
    /// it names a link of its own (<see cref="AiWords.AskBylineParts"/>).</summary>
    [ObservableProperty] public partial IReadOnlyList<BylinePart> BylineParts { get; set; } = [];
    [ObservableProperty] public partial string? FellBackNote { get; set; }
    /// <summary>Under the first answer from a library too old to keep chats: this conversation isn't kept.</summary>
    [ObservableProperty] public partial string? OlderLibraryNote { get; set; }
    [ObservableProperty] public partial string? Failed { get; set; }
    public List<AskSource> Sources { get; set; } = [];
    /// <summary>The words of the lecture's notes the answer was built on, as the AI quoted them (<see cref="AskQuotes"/>):
    /// the ones the notes hold are <see cref="Shown"/>, each a chip under the answer.</summary>
    public List<string> Quotes { get; set; } = [];
    /// <summary>The quotes the notes on the page hold: the highlights this answer asks for, and its chips.</summary>
    public List<string> Shown { get; private set; } = [];
    [ObservableProperty] public partial IReadOnlyList<QuoteChip> Chips { get; set; } = [];
    public bool HasChips => Chips.Count > 0;
    partial void OnChipsChanged(IReadOnlyList<QuoteChip> value) => OnPropertyChanged(nameof(HasChips));
    /// <summary>The pointer is over this answer: if it quotes the notes, they're what the page highlights.</summary>
    internal Action<AiTurn>? OnReading { get; set; }
    public void Reading() => OnReading?.Invoke(this);

    /// <summary>The quotes the notes hold now (<paramref name="found"/>), made into chips.</summary>
    internal void Rematch(NoteHighlights notes, List<string> found)
    {
        Shown = found;
        // A chip for each quote that reads as words; one that's only a formula keeps its highlight in the notes, and its place in the list.
        Chips = [.. found.Select((q, i) => (q, i)).Where(x => QuoteChip.Readable(x.q)).Select(x => new QuoteChip(notes, found, x.i))];
    }
    /// <summary>The engine that answered (the one asked, until the answer says otherwise): whether the chat above has
    /// been to an AI before goes by it.</summary>
    public string Engine { get; set; } = "";
    public bool IsThinking => Answer is null && Failed is null;
    public string ThinkingWords => AiWords.Thinking(AskedName);

    public AiTurn(string question, string askedName)
    {
        Question = question;
        AskedName = askedName;
    }

    partial void OnAnswerChanged(string? value) => OnPropertyChanged(nameof(IsThinking));
    partial void OnFailedChanged(string? value) => OnPropertyChanged(nameof(IsThinking));
    partial void OnBylineChanged(string? value) => BylineParts = string.IsNullOrEmpty(value) ? [] : [new BylinePart(value)];
}

/// <summary>
/// The ask bar under a lecture's notes, the recorder's compact chat, and the quick panel's ask (design 16): an
/// engine picked per question (starting at the library's default and lasting for this model), a scope, and the
/// thread of turns, with an answer's byline naming who answered and when in the lecture. Reads and asks one
/// library's AI (<see cref="IAiLibrary"/>); the view is just this.
/// </summary>
public sealed partial class AiAskModel : ObservableObject
{
    readonly IAiLibrary library;

    public AiAskModel(IAiLibrary library)
    {
        this.library = library;
        Highlights.NotesChanged += NotesChanged;
        foreach (var c in ScopeChoices) c.Pick = new RelayCommand(() => Scope = c.Id);
        EngineChoice.Mark(ScopeChoices, Scope);
    }

    /// <summary>An ask bar with no lecture under it (Home, a class's home): only the scopes that make sense there.</summary>
    public void OnlyScopes(params string[] ids)
    {
        ScopeChoices.RemoveAll(c => !ids.Contains(c.Id));
        Scope = ids[0];
        EngineChoice.Mark(ScopeChoices, Scope);
    }

    /// <summary>Set by the host: the lecture or class this ask bar is under (whichever the current scope needs).</summary>
    public string? LectureId { get; set; }
    public string? ClassName { get; set; }
    /// <summary>The recorder's chat asks with the transcript so far: the host sets this (and <see cref="LiveTitle"/>),
    /// with the recording's own id and folder above, so the scope says how far beyond what's been said to look (not
    /// at all, for "This lecture"). Everywhere else it's null.</summary>
    public Func<string?>? Live { get; set; }
    public string LiveTitle { get; set; } = "This lecture, now";

    [ObservableProperty] public partial string Question { get; set; } = "";
    /// <summary>The engine picked for the question being typed gets ready once it's begun (a local model loads), at
    /// most once a minute; the library does the rest.</summary>
    DateTime warmedAt = DateTime.MinValue;
    [ObservableProperty] public partial string Scope { get; set; } = "lecture";
    [ObservableProperty] public partial string Engine { get; set; } = "";
    [ObservableProperty] public partial bool MenuOpen { get; set; }
    [ObservableProperty] public partial bool Busy { get; set; }
    [ObservableProperty] public partial bool OlderLibrary { get; set; }
    [ObservableProperty] public partial bool Offline { get; set; }

    public ObservableCollection<AiTurn> Turns { get; } = [];
    public EngineMenuModel Menu { get; } = new() { Header = "Answer with", FooterText = "Change defaults in Settings" };
    public List<EngineChoice> ScopeChoices { get; } =
    [
        new("lecture", "This lecture"), new("class", "This folder"), new("all", "All notes"),
    ];

    string noun = Nouns.Lecture;
    string? folder;

    /// <summary>What the bar is under, in the page's own words: a lecture in a class, a meeting in a club, a recording
    /// that's in no folder ("This meeting", "Ask about this meeting"), and its folder by name ("Machine Learning",
    /// "Ask about Machine Learning").</summary>
    public void NameScopes(string noun, string? folder)
    {
        this.noun = noun;
        this.folder = string.IsNullOrEmpty(folder) ? null : folder;
        for (int i = 0; i < ScopeChoices.Count; i++)
        {
            var c = new EngineChoice(ScopeChoices[i].Id, ScopeWords(ScopeChoices[i].Id));
            c.Pick = new RelayCommand(() => Scope = c.Id);
            ScopeChoices[i] = c;
        }
        EngineChoice.Mark(ScopeChoices, Scope);
        OnPropertyChanged(nameof(ScopeName));
        OnPropertyChanged(nameof(Placeholder));
    }

    string ScopeWords(string scope) => scope switch
    {
        "lecture" => $"This {noun}",
        "class" => folder ?? "This folder",
        _ => "All notes",
    };

    /// <summary>The engine's name on the chip. One that isn't in the menu (no AI set up yet, so the default is an
    /// Ollama nobody installed) still goes by its name, "Ollama", not its id.</summary>
    public string EngineName => Menu.Items.FirstOrDefault(i => i.Id == Engine)?.Name ?? Engines.Name(Engine);
    /// <summary>The engine on the ask bar: its own mark (Claude's, ChatGPT's), and what the button under it says.</summary>
    public string EngineIcon => AiWords.EngineIcon(Engine);
    public string EngineTip => $"Answer with {EngineName}";
    /// <summary>Send shows once there's a question; until then the keys that put the caret in the bar do.</summary>
    public bool CanSend => !Busy && Question.Trim().Length > 0;
    public bool ShowKeys => !Busy && Question.Trim().Length == 0;
    public string ScopeName => ScopeChoices.FirstOrDefault(c => c.Id == Scope)?.Name ?? Scope;
    public string Placeholder => Live is not null ? AiWords.AskLive : AiWords.AskPlaceholder(Scope, noun, folder);
    public AiTurn? Latest => Turns.Count > 0 ? Turns[^1] : null;
    /// <summary>The latest answer shows until the student closes it; the next question opens it again.</summary>
    public bool HasLatest => Latest is not null && !AnswerClosed;
    [ObservableProperty] public partial bool AnswerClosed { get; set; }

    /// <summary>The conversation's panel is open beside the page: an answer to show, or a new chat started in the panel
    /// and waiting for its first question (New chat keeps the panel where it is, empty).</summary>
    public bool PanelOpen => !AnswerClosed && (Latest is not null || waiting);
    /// <summary>New chat was pressed with the panel open: it stays open for the first question.</summary>
    bool waiting;

    partial void OnAnswerClosedChanged(bool value) => ThreadShown();

    /// <summary>The words of the notes the answer being read points at: the notes on the page highlight them
    /// (<see cref="NoteView"/>). The same object is the notes' (the page links them).</summary>
    public NoteHighlights Highlights { get; } = new();

    /// <summary>The answer the pointer is over, if it quotes the notes; null: the latest.</summary>
    AiTurn? reading;

    /// <summary>Highlights the quotes of the answer being read: the latest (while its card is open), or the one the pointer
    /// went over. None once the card is closed, or on a new chat.</summary>
    void ShowReading()
    {
        var turn = reading is not null && Turns.Contains(reading) ? reading : Latest;
        Highlights.Show(HasLatest && turn is not null ? turn.Shown : []);
    }

    /// <summary>A turn added to the thread: it hears when the pointer is over it, and finds its quotes in the notes (again,
    /// when the notes change).</summary>
    void Quoting(AiTurn turn, IEnumerable<string>? quotes)
    {
        turn.Quotes = [.. quotes ?? []];
        turn.OnReading = t =>
        {
            if (t.Shown.Count == 0 || ReferenceEquals(reading, t)) return;
            reading = t;
            ShowReading();
        };
        if (turn.Quotes.Count > 0) turn.Rematch(Highlights, Highlights.Found(turn.Quotes));
    }

    void NotesChanged()
    {
        foreach (var t in Turns)
            if (t.Quotes.Count > 0) t.Rematch(Highlights, Highlights.Found(t.Quotes));
        ShowReading();
    }

    /// <summary>The panel's close button (and Esc in it): the notes get their room back, the bar goes back to the foot of
    /// the page, and an answer still being written stops. The conversation is kept: the next question carries it on,
    /// and it's in the Chats list.</summary>
    [RelayCommand]
    void CloseAnswer()
    {
        Stop();
        waiting = false;
        AnswerClosed = true;
        ThreadShown();
    }

    /// <summary>New chat: the next question starts a conversation of its own, the AI remembering nothing before it. The
    /// one before stays in the Chats list. An answer still being written stops. An open panel stays open, empty, with the
    /// caret in the bar for the first question.</summary>
    [RelayCommand(CanExecute = nameof(CanStartOver))]
    void NewChat() => StartOver(show: true);

    void StartOver(bool show)
    {
        waiting = PanelOpen;
        Stop();
        Turns.Clear();
        reading = null;
        ChatId = null;
        foreach (var row in Chats) row.Current = false;
        AnswerClosed = false;
        OnPropertyChanged(nameof(Latest));
        ThreadShown();
        if (show) Started?.Invoke();
    }

    /// <summary>The student asked: a turn was added at the end of the thread. The views bring the thread back to its end,
    /// even where the student had scrolled up to read something earlier, so the question and its answer show.</summary>
    public event Action? Asked;

    public bool CanStartOver => Turns.Count > 0;

    /// <summary>A new chat was started: the view puts the caret in the field for its first question.</summary>
    public Action? Started { get; set; }

    /// <summary>Whether this bar has said its library is too old to keep chats (it says so once).</summary>
    bool toldOlder;

    /// <summary>The recorder's chat was closed with a conversation in it: "Show the chat" brings it back as it was.</summary>
    public bool CanShowChat => Turns.Count > 0 && AnswerClosed;

    /// <summary>The recorder's chat's close button (and Esc): the transcript gets its room back and an answer still
    /// being written stops. The conversation is kept for this lecture: the next question, or "Show the chat", shows it
    /// again (a new recording starts a new one).</summary>
    [RelayCommand(CanExecute = nameof(HasLatest))]
    void CloseChat()
    {
        Stop();
        waiting = false;
        AnswerClosed = true;
    }

    /// <summary>"Show the chat": the conversation closed a moment ago, back as it was.</summary>
    [RelayCommand]
    void ShowChat() => AnswerClosed = false;

    /// <summary>Whether the thread shows changed (a question asked, the chat closed or shown again).</summary>
    void ThreadShown()
    {
        ShowReading();
        OnPropertyChanged(nameof(HasLatest));
        OnPropertyChanged(nameof(PanelOpen));
        OnPropertyChanged(nameof(CanShowChat));
        OnPropertyChanged(nameof(CanStartOver));
        CloseChatCommand.NotifyCanExecuteChanged();
        NewChatCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Opens Settings → AI engines (the "Change defaults" footer link).</summary>
    public Action? OpenSettings { get; set; }
    /// <summary>One of an answer's sources, pressed in its byline: the transcript opens at its moment.</summary>
    public Action<AskSource>? OnSource { get; set; }
    /// <summary>Closes the "Answer with" popup once an engine is picked; the view wires this to the real flyout.</summary>
    public Action? CloseMenu { get; set; }

    partial void OnQuestionChanged(string value)
    {
        OnPropertyChanged(nameof(CanSend));
        OnPropertyChanged(nameof(ShowKeys));
        RecipesFollowQuestion();
        if (value.Trim().Length == 0 || DateTime.UtcNow - warmedAt < TimeSpan.FromMinutes(1)) return;
        warmedAt = DateTime.UtcNow;
        _ = library.WarmAsync(Engine.Length > 0 ? Engine : null);
    }

    partial void OnEngineChanged(string value)
    {
        warmedAt = DateTime.MinValue; // another engine may have something of its own to get ready
        foreach (var i in Menu.Items) i.Selected = i.Id == value;
        EngineNamed();
    }

    partial void OnBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanSend));
        OnPropertyChanged(nameof(ShowKeys));
    }

    void EngineNamed()
    {
        OnPropertyChanged(nameof(EngineName));
        OnPropertyChanged(nameof(EngineIcon));
        OnPropertyChanged(nameof(EngineTip));
    }

    partial void OnScopeChanged(string value)
    {
        EngineChoice.Mark(ScopeChoices, value);
        OnPropertyChanged(nameof(ScopeName));
        OnPropertyChanged(nameof(Placeholder));
    }

    [RelayCommand]
    void ToggleMenu() => MenuOpen = !MenuOpen;

    [RelayCommand]
    public Task Refresh() => Load();

    /// <summary>Reads the library's AI once and builds the "Answer with" menu: called when the ask bar opens.</summary>
    public async Task Load()
    {
        AiOverview? overview;
        try
        {
            overview = await library.EnginesAsync();
        }
        catch
        {
            Offline = true;
            return;
        }
        Apply(overview);
        await LoadRecipesAsync();
        // A lecture's page opens on its latest conversation (in the card, closed until it's asked in or opened).
        await LoadChatsAsync(openLatest: Place?.Scope == "lecture");
    }

    void Apply(AiOverview? overview)
    {
        if (overview is null)
        {
            OlderLibrary = true;
            Offline = false;
            return;
        }
        OlderLibrary = false;
        Offline = false;

        // The default first (checked), then the rest in the library's own order; an engine nobody has installed
        // never reaches this menu.
        var ordered = overview.Engines.Where(e => e.Id == overview.Ask)
            .Concat(overview.Engines.Where(e => e.Id != overview.Ask))
            .Where(e => e.State != "not_installed");

        Menu.Items.Clear();
        foreach (var e in ordered)
        {
            bool isDefault = e.Id == overview.Ask;
            var item = new EngineMenuItem(e.Id, e.Name, AiWords.AskEngineSubtitle(e.Id, e.State, isDefault), isDefault, AiWords.EngineUsable(e.State));
            item.Command = new RelayCommand(() => { Engine = item.Id; CloseMenu?.Invoke(); }, () => item.Enabled);
            Menu.Items.Add(item);
        }
        if (Engine.Length == 0 || Menu.Items.All(i => i.Id != Engine)) Engine = overview.Ask;
        foreach (var i in Menu.Items) i.Selected = i.Id == Engine;
        EngineNamed();
    }

    /// <summary>The question being answered right now, to stop it.</summary>
    CancellationTokenSource? asking;

    /// <summary>
    /// Asks, and shows the answer as it's written: the turn says it's thinking at once, then its answer grows (at
    /// <see cref="Paced"/>'s steady pace) until the whole reply, with its byline and sources, takes over. Stopped
    /// or cut off partway, what came stays, with a line saying why it ends there.
    /// </summary>
    [RelayCommand]
    async Task Ask()
    {
        string q = Question.Trim();
        if (q.Length == 0 || Busy) return;
        Question = "";
        // The conversation so far goes with the question: the library reads it from the kept chat; where nothing is
        // kept, the turns that were answered go with it (the last few, each cut short).
        var answered = Turns.Where(t => !string.IsNullOrWhiteSpace(t.Answer)).TakeLast(AskMemory.Turns).ToList();
        var earlier = Place is null ? AskMemory.Last(answered.Select(t => new AskTurn(t.Question, t.Answer!) { Sources = t.Sources })) : [];
        string first = Turns.Count > 0 ? Turns[0].Question : q;
        var turn = new AiTurn(q, EngineName) { Engine = Engine };
        Turns.Add(turn);
        AnswerClosed = false;
        OnPropertyChanged(nameof(Latest));
        ThreadShown();
        Asked?.Invoke();
        Busy = true;
        using var stop = asking = new CancellationTokenSource();
        var paced = new Paced(text => turn.Answer = PartialText.Showable(text));
        string? inChat = null;
        string? Partial() => paced.End() is { } text ? PartialText.Ended(text) : null;
        try
        {
            string? live = Live?.Invoke();
            var request = new AskRequest(q)
            {
                Engine = Engine,
                Live = live,
                LiveTitle = live is not null ? LiveTitle : null,
                // While it records the scope still says how far to look: this recording alone, its folder, everything.
                Lecture = Scope == "lecture" ? LectureId : null,
                Class = Scope == "class" ? ClassName : null,
                History = earlier.Count > 0 ? earlier : null,
                Chat = Place is null ? null : ChatId ?? "new",
                Place = Place,
            };
            AskReply? reply = await library.AskAsync(request, paced.Show, id => inChat = id, stop.Token);
            paced.End();
            if (reply is null)
            {
                turn.Failed = AiWords.OlderLibraryWords;
                OlderLibrary = true;
                return;
            }
            turn.Answer = reply.Answer;
            turn.Sources = reply.Sources;
            turn.Engine = reply.Engine;
            Quoting(turn, reply.Quotes);
            reading = null;
            ShowReading();
            // The chat above went to an AI that hadn't had it: Claude or ChatGPT, after the free AI kept it on the computer.
            bool readChat = reply.Remembered is int n && n > 0 && SentElsewhere(reply.Engine, answered.TakeLast(n));
            turn.Byline = AiWords.AskByline(reply.EngineName, reply.Sources, readChat);
            turn.BylineParts = AiWords.AskBylineParts(reply.EngineName, reply.Sources, OnSource, readChat);
            if (reply.FellBack) turn.FellBackNote = AiWords.FellBackNote(reply.EngineName, reply.Why);
            if (reply.Chat is { } kept)
            {
                Kept(kept, first);
                inChat = null;
            }
            else if (Place is not null && !toldOlder)
            {
                // A library too old to keep chats: said once, so a chat that isn't there later isn't a mystery.
                toldOlder = true;
                turn.OlderLibraryNote = AiWords.NoHistoryOnOlderLibrary;
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            turn.Answer = Partial() ?? "";
            turn.Byline = AiWords.Stopped;
        }
        catch (LibraryRefusedException ex)
        {
            if (Partial() is { } partial) turn.Answer = partial;
            turn.Failed = ex.Message;
        }
        catch
        {
            string? partial = Partial();
            if (partial is not null) turn.Answer = partial;
            turn.Failed = partial is null ? AiWords.NotAnswering : AiWords.CutOff;
            Offline = partial is null;
        }
        finally
        {
            // Stopped or cut off with some of an answer: the library kept it in the chat it said it's in, and the next
            // question carries that chat on (a new one's first turn too, so it isn't split in two).
            if (inChat is { } said && !string.IsNullOrWhiteSpace(turn.Answer)) Kept(said, first);
            asking = null;
            Busy = false;
            OnPropertyChanged(nameof(Latest));
        }
    }

    /// <summary>Stops the answer being written (the ask bar's stop button, or the answer closed, or another
    /// lecture opened): what's been written so far stays.</summary>
    [RelayCommand]
    public void Stop() => asking?.Cancel();
}
