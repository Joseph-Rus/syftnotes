using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StudyStash.App.Services;
using StudyStash.Core;
using StudyStash.Core.Ai;

namespace StudyStash.App.ViewModels;

/// <summary>One library grant: an app signed in from the web, or a token made in Settings. The row owns its own
/// Remove, closed over its id. (The AI apps on this computer are <see cref="AiAppRow"/>s.)</summary>
public sealed class AiConnectionRow
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    /// <summary>"Signed in from the web" · "Token" · "This computer".</summary>
    public string Detail { get; init; } = "";
    public string UsedWords { get; init; } = "";
    public bool HasUsedWords => UsedWords.Length > 0;
    /// <summary>Windows keeps the used-time in the one subtitle line, Mac on the row's own right side.</summary>
    public string WinDetail => HasUsedWords ? $"{Detail} · {UsedWords}" : Detail;
    public bool CanRemove { get; init; }
    public IAsyncRelayCommand? Remove { get; internal set; }
    /// <summary>terminal for Claude, code for ChatGPT and Codex, hub for anything else — the row's icon tile.</summary>
    public string Icon => Name switch { "Claude Code" or "Claude" or "Claude Desktop" => "terminal", "Codex" or "ChatGPT" => "code", _ => "hub" };
    /// <summary>The first row in the list shows no separator above it.</summary>
    public bool First { get; set; }
}

/// <summary>One AI app on this computer (Claude Desktop, Claude Code, ChatGPT, Gemini CLI): what it's called, where it
/// stands, and its own Connect and Disconnect, closed over its id.</summary>
public sealed partial class AiAppRow : ObservableObject
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    /// <summary>What the app is, in a few words ("The ChatGPT app, and Codex in the terminal or your editor"): the
    /// row's tooltip.</summary>
    public string About { get; init; } = "";
    public string Icon => Id switch { "codex" => "code", "gemini" => "auto_awesome", "claude-code" => "terminal", _ => "desktop_windows" };
    [ObservableProperty] public partial string Status { get; set; } = "";
    /// <summary>Connected: an app started Study Stash since it was added.</summary>
    [ObservableProperty] public partial bool Ok { get; set; }
    [ObservableProperty] public partial bool CanConnect { get; set; }
    [ObservableProperty] public partial bool CanDisconnect { get; set; }
    /// <summary>"Connect", "Fix" when the app has another copy of Study Stash set up, or "Update" when it has this
    /// one the way it was pasted in by hand.</summary>
    [ObservableProperty] public partial string ConnectWords { get; set; } = "Connect";
    /// <summary>Added, but the app hasn't loaded it yet, and it's a desktop app Study Stash can quit and open again
    /// (a Mac): the row offers to, since closing a Mac app's window doesn't quit it.</summary>
    [ObservableProperty] public partial bool CanReopen { get; set; }
    /// <summary>The app as its own menu bar names it ("Claude", "ChatGPT"): what the words about it say.</summary>
    public string AppName => ClaudeSetup.DesktopApp(Id)?.Name ?? Name;
    public string ReopenWords => $"Reopen {AppName}";
    /// <summary>While Reopen is quitting and opening the app.</summary>
    public bool Reopening { get; set; }
    /// <summary>Reopen opened the app since it was added: the row says what's left, if anything.</summary>
    public bool Reopened { get; set; }
    [ObservableProperty] public partial bool First { get; set; }
    public IAsyncRelayCommand? Connect { get; internal set; }
    public IAsyncRelayCommand? Disconnect { get; internal set; }
    public IAsyncRelayCommand? Reopen { get; internal set; }
    /// <summary>Windows shows the status in the one subtitle line.</summary>
    public string WinDetail => Status;
    partial void OnStatusChanged(string value) => OnPropertyChanged(nameof(WinDetail));

    /// <summary>Where an app stands, in words: not here, not connected, added but not loaded yet, connected (and when
    /// it last started Study Stash), set up for another copy of Study Stash, or set up by hand.</summary>
    public void Show(AiAppState s, DateTime now)
    {
        Ok = false;
        CanConnect = s.Installed && (!s.Added || s.OtherCopy || s.Outdated);
        CanDisconnect = s.Added;
        ConnectWords = s.OtherCopy ? "Fix" : s.Outdated ? "Update" : "Connect";
        bool waiting = false;
        if (!s.Installed) Status = "Not on this computer";
        else if (!s.Added) Status = "Not connected yet";
        else if (s.OtherCopy) Status = "Set up for another copy of Study Stash. Choose Fix to use this one.";
        else if (s.Outdated) Status = "Set up by hand, so Study Stash can't tell when it's connected. Choose Update.";
        else if (s.Started is { } started && (s.AddedAt is null || started >= s.AddedAt))
        {
            Ok = true;
            Reopened = false;
            Status = "Connected · " + (started.Date == now.Date ? $"started {started:H:mm}" : now - started < TimeSpan.FromDays(7) ? $"started {started:ddd}" : $"started {started:d MMM}");
        }
        else
        {
            waiting = true;
            Status = Reopening ? $"Reopening {AppName}…" : Reopened ? OpenedWords : WaitingWords(s.CanReopen);
        }
        CanReopen = waiting && s.CanReopen && !Reopening;
    }

    /// <summary>The one step left after Connect: the app reads its settings when it starts. ChatGPT starts Study
    /// Stash only once a chat in Codex begins, so its row says that too.</summary>
    string WaitingWords(bool canReopen) => Id switch
    {
        "claude-desktop" => canReopen ? "Almost done. Reopen Claude so it loads Study Stash." : "Almost done. Quit Claude completely, then open it again.",
        "codex" => canReopen ? "Almost done. Reopen ChatGPT, then start a chat in Codex." : "Almost done. Quit ChatGPT and open it again, then start a chat in Codex.",
        "claude-code" => "Almost done. Start a new Claude Code session.",
        _ => $"Almost done. Start {Name} again.",
    };

    string OpenedWords => Id == "codex"
        ? "ChatGPT is open. Start a chat in Codex and this turns to Connected."
        : $"{AppName} is opening. This turns to Connected in a moment.";
}

/// <summary>
/// Settings → AI tool access (design 14): the off switch, what AI apps may read, the AI apps on this computer (each
/// connected with one click, no Tailscale needed), Claude and ChatGPT on the web (Tailscale Funnel), and the other
/// connections.
/// Reads and drives one library's AI (<see cref="IAiLibrary"/>); what it can't do through that (reading and changing
/// the AI apps' own settings on this computer) goes through hooks the host sets, so this model never touches a file
/// or a process itself.
/// </summary>
public sealed partial class AiAccessModel : ObservableObject
{
    readonly IAiLibrary library;
    readonly Func<DateTime> now;
    bool loading;

    public AiAccessModel(IAiLibrary library, Func<DateTime>? now = null)
    {
        this.library = library;
        this.now = now ?? (() => DateTime.Now);
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowToolsOffNote))]
    public partial bool On { get; set; } = true;
    [ObservableProperty] public partial bool ReadLectures { get; set; } = true;
    [ObservableProperty] public partial bool ReadNotes { get; set; } = true;
    [ObservableProperty] public partial bool ReadCanvas { get; set; } = true;
    [ObservableProperty] public partial bool ReadAudio { get; set; }
    [ObservableProperty] public partial bool Busy { get; set; }
    [ObservableProperty] public partial string? Say { get; set; }
    [ObservableProperty] public partial bool OlderLibrary { get; set; }
    [ObservableProperty] public partial bool Offline { get; set; }
    [ObservableProperty] public partial string? PublicUrl { get; set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WebToggleEnabled))]
    public partial bool HasPassword { get; set; }

    // Claude and ChatGPT on the web and phone: whether they can reach this library over the internet, through Tailscale
    // Funnel (task 4's ClaudeReach/ReachCheck, read through ToolAccessInfo.Web). WebOn is the switch Settings shows;
    // the library is the truth, so every change round-trips through it before the switch visibly moves.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowWebReady), nameof(ShowWebNeeds), nameof(ShowWebNeedsAction))]
    public partial bool WebOn { get; set; }
    [ObservableProperty] public partial bool WebBusy { get; set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WebStatusWords))]
    public partial bool WebChecking { get; set; }
    public string WebName => "Study Stash";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowWebReady))]
    public partial string? WebUrl { get; set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WebStatusWords))]
    public partial string? WebWords { get; set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WebStatusOk), nameof(WebStatusWarn))]
    public partial bool? WebReachable { get; set; }
    /// <summary>Null from a library too old to put itself on the internet (<see cref="ToolAccessInfo.Web"/> is null).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowWebReady), nameof(WebToggleEnabled))]
    public partial bool WebSupported { get; set; } = true;
    /// <summary>The words to show in the card's problem/info row (Funnel's own problem, no password yet, or an
    /// older library), or null when there's nothing to say. Set directly at each transition, not computed from
    /// several flags at once, so a test can assert it without reconstructing the priority rules by hand.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowWebNote), nameof(ShowWebReady), nameof(ShowWebNeeds), nameof(ShowWebNeedsAction))]
    public partial string? WebNote { get; set; }
    /// <summary>The problem's own fix page, when it has one ("Open" + "Copy link"); null for "no password" and
    /// "older library", which have no page to send the student to.</summary>
    [ObservableProperty] public partial string? WebNoteFixUrl { get; set; }
    /// <summary>While Claude's address is off: what this computer needs first (Tailscale), from the library, and the
    /// page that gets it. A computer on its own without Tailscale sees this, not a switch that fails.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowWebNeeds), nameof(ShowWebNeedsAction))]
    public partial string? WebNeeds { get; set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowWebNeedsAction))]
    public partial string? WebNeedsUrl { get; set; }
    public bool ShowWebNeeds => WebNeeds is { Length: > 0 } && !WebOn && !ShowWebNote;
    public bool ShowWebNeedsAction => ShowWebNeeds && WebNeedsUrl is { Length: > 0 };

    public bool ShowWebReady => WebSupported && WebOn && WebNote is null && WebUrl is { Length: > 0 };
    public bool ShowWebNote => WebNote is { Length: > 0 };
    public bool ShowWebNoteAction => WebNoteFixUrl is { Length: > 0 };
    /// <summary>The status row's dot and words: "Checking…" while a poll is running, else the last check's own words.</summary>
    public string? WebStatusWords => WebChecking ? "Checking…" : WebWords;
    public bool WebStatusOk => WebReachable == true;
    public bool WebStatusWarn => WebReachable == false;
    /// <summary>The master AI-tool-access switch (<see cref="On"/>) is off: an app can be signed in and the address
    /// can answer, but every tool call is refused. Shown as a quiet note under the card, not another problem.</summary>
    public bool ShowToolsOffNote => !On;
    public bool WebToggleEnabled => WebSupported && HasPassword;

    /// <summary>Sign-ins from the web (Claude, ChatGPT: <c>kind == "signin"</c>): the ones this card lists and can
    /// remove. A token made in Settings stays in <see cref="Connected"/> below instead.</summary>
    public ObservableCollection<AiConnectionRow> ClaudeConnections { get; } = [];
    public bool HasClaudeConnections => ClaudeConnections.Count > 0;

    public ObservableCollection<AiConnectionRow> Connected { get; } = [];
    public bool HasConnections => Connected.Count > 0;

    /// <summary>The AI apps on this computer, each with its status and its own Connect / Disconnect.</summary>
    public ObservableCollection<AiAppRow> Apps { get; } = [];

    /// <summary>The <c>mcpServers</c> JSON for any other MCP app, to paste in by hand (from <c>ClaudeSetup</c>).</summary>
    public string McpJson { get; set; } = "";

    /// <summary>Puts text on the clipboard (write-only: this model never reads it).</summary>
    public Func<string, Task>? Copy { get; set; }
    /// <summary>Reads every AI app's settings on this computer (read only), asked fresh each <see cref="Load"/>.</summary>
    public Func<IReadOnlyList<AiAppState>>? ReadApps { get; set; }
    /// <summary>Adds Study Stash to one app's settings, or takes it out; answers what happened.</summary>
    public Func<string, AiAppChange>? ConnectApp { get; set; }
    public Func<string, AiAppChange>? DisconnectApp { get; set; }
    /// <summary>Quits one app and opens it again (a Mac); answers whether it did, with the words to show if not.</summary>
    public Func<string, Task<AiAppChange>>? ReopenApp { get; set; }

    /// <summary>The apps Study Stash looked for and didn't find, in one quiet line under the ones it did.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowMissingApps))]
    public partial string? MissingApps { get; set; }
    /// <summary>None of them is on this computer: the group says where to get one instead of a list of absences.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowMissingApps))]
    public partial bool NoApps { get; set; }
    public bool ShowMissingApps => !NoApps && MissingApps is { Length: > 0 };

    public const string ChatGptDownload = "https://openai.com/chatgpt/download/";
    public const string ClaudeDownload = "https://claude.com/download";

    [RelayCommand] public void GetChatGpt() => OpenUrl?.Invoke(ChatGptDownload);
    [RelayCommand] public void GetClaude() => OpenUrl?.Invoke(ClaudeDownload);

    /// <summary>Once an app is connected: a first question to ask it, so the student sees it work.</summary>
    public const string TryQuestion = "What did my last lecture cover?";
    public bool ShowTry => Apps.Any(a => a.Ok);
    public string TryLead => Apps.FirstOrDefault(a => a.Ok) switch
    {
        { Id: "codex" } => "Try it. In ChatGPT, start a chat in Codex and ask:",
        { } row => $"Try it. In {row.AppName}, ask:",
        null => "",
    };

    [RelayCommand]
    public async Task CopyTry()
    {
        if (Copy is null) return;
        await Copy(TryQuestion);
        Say = "Copied. Paste it into the app.";
    }

    /// <summary>The "what was written" panel after a change: the app's settings file, the copy of it from before, and
    /// the setup that went in (no secrets: the apps start Study Stash, which reads its own settings).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowWritten), nameof(HasWritten))]
    public partial string? WrittenFile { get; set; }
    /// <summary>The panel is folded away behind "Show what changed": it's there for whoever wants to see it, and
    /// opens by itself only when a change couldn't be made and the setup has to be pasted in by hand.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowWritten), nameof(DetailsWords))]
    public partial bool DetailsOpen { get; set; }
    public bool HasWritten => WrittenFile is { Length: > 0 };
    public string DetailsWords => DetailsOpen ? "Hide what changed" : "Show what changed";
    [RelayCommand] public void ToggleDetails() => DetailsOpen = !DetailsOpen;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WrittenBackupWords))]
    public partial string? WrittenBackup { get; set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasWrittenText))]
    public partial string? WrittenText { get; set; }
    public bool ShowWritten => HasWritten && DetailsOpen;
    public bool HasWrittenText => WrittenText is { Length: > 0 };
    public string WrittenBackupWords => WrittenBackup is { Length: > 0 } b ? "As it was before: " + b : "It's a new file: there was nothing to keep.";
    public bool HasSay => Say is { Length: > 0 };
    partial void OnSayChanged(string? value) => OnPropertyChanged(nameof(HasSay));

    /// <summary>Revokes one library grant (DELETE /api/v2/claude/connections/{id}). Whether it worked.</summary>
    public Func<string, Task<bool>>? RevokeConnection { get; set; }
    /// <summary>Opens a problem's fix page in the system browser (<c>Dialogs.OpenUrl</c> in <c>Settings.MakeAccess</c>).</summary>
    public Action<string>? OpenUrl { get; set; }
    /// <summary>How the poll between checks waits, so a test can skip the real 5 seconds. Defaults to a real wait.</summary>
    public Func<TimeSpan, Task>? Delay { get; set; }

    CancellationTokenSource? pollCts;

    partial void OnOnChanged(bool value)
    {
        if (!loading) _ = PostAsync(on: value);
    }

    partial void OnWebOnChanged(bool value)
    {
        if (!loading) _ = SetWebAsync(value);
    }

    partial void OnReadLecturesChanged(bool value) => PostReading();
    partial void OnReadNotesChanged(bool value) => PostReading();
    partial void OnReadCanvasChanged(bool value) => PostReading();
    partial void OnReadAudioChanged(bool value) => PostReading();

    void PostReading()
    {
        if (!loading) _ = PostAsync(reading: new ReadingScopes(ReadLectures, ReadNotes, ReadCanvas, ReadAudio));
    }

    [RelayCommand] public void ToggleLectures() => ReadLectures = !ReadLectures;
    [RelayCommand] public void ToggleNotes() => ReadNotes = !ReadNotes;
    [RelayCommand] public void ToggleCanvas() => ReadCanvas = !ReadCanvas;
    [RelayCommand] public void ToggleAudio() => ReadAudio = !ReadAudio;

    [RelayCommand]
    public Task Refresh() => Load();

    /// <summary>Reads the library's tool access, and this computer's own Claude Code / Claude Desktop, once: called
    /// when the pane opens.</summary>
    public async Task Load()
    {
        await LoadApps();
        ToolAccessInfo? info;
        try
        {
            info = await library.AccessAsync();
        }
        catch
        {
            Offline = true;
            return;
        }
        Apply(info);
    }

    void Apply(ToolAccessInfo? info)
    {
        loading = true;
        try
        {
            if (info is null)
            {
                OlderLibrary = true;
                Offline = false;
                return;
            }
            OlderLibrary = false;
            Offline = false;
            On = info.On;
            ReadLectures = info.Reading.Lectures;
            ReadNotes = info.Reading.Notes;
            ReadCanvas = info.Reading.Canvas;
            ReadAudio = info.Reading.Audio;
            PublicUrl = info.PublicUrl;
            HasPassword = info.HasPassword;
            ApplyWeb(info.Web);

            List<AiConnectionRow> rows = [];
            List<AiConnectionRow> claudeRows = [];
            foreach (var c in info.Connections)
            {
                var row = new AiConnectionRow
                {
                    Id = c.Id,
                    Name = c.Kind == "signin" ? (c.Name.Length > 0 ? c.Name : "Claude") : c.Name,
                    Detail = c.Kind == "signin" ? (c.ClientHost.Length > 0 ? c.ClientHost : "claude.ai") : "Token",
                    UsedWords = c.LastUsed is double lu ? AiWords.UsedWords(Epoch(lu), now()) : "",
                    CanRemove = true,
                };
                row.Remove = new AsyncRelayCommand(() => RemoveAsync(row.Id));
                (c.Kind == "signin" ? claudeRows : rows).Add(row);
            }
            if (rows.Count > 0) rows[0].First = true;
            if (claudeRows.Count > 0) claudeRows[0].First = true;
            Connected.Clear();
            foreach (var row in rows) Connected.Add(row);
            ClaudeConnections.Clear();
            foreach (var row in claudeRows) ClaudeConnections.Add(row);
            OnPropertyChanged(nameof(HasConnections));
            OnPropertyChanged(nameof(HasClaudeConnections));
        }
        finally
        {
            loading = false;
        }
    }

    /// <summary>Reads Claude's reach into the switch, address and problem/checking state. A null <paramref name="web"/>
    /// (an older library) leaves the switch off with the "update the library" note; otherwise the "no password" note
    /// wins over whatever Funnel problem the library sent, since nothing can work until there's a password regardless.</summary>
    void ApplyWeb(WebReach? web)
    {
        WebSupported = web is not null;
        if (web is null)
        {
            WebOn = false;
            WebUrl = null;
            WebWords = null;
            WebReachable = null;
            WebNote = "Update the library to turn this on.";
            WebNoteFixUrl = null;
            WebNeeds = WebNeedsUrl = null;
            return;
        }
        WebOn = web.On;
        WebNeeds = web.Needs;
        WebNeedsUrl = web.NeedsUrl;
        WebUrl = web.McpUrl;
        WebWords = web.Words;
        WebReachable = web.Reachable;
        WebNote = web.Problem;
        WebNoteFixUrl = web.FixUrl;
        if (!HasPassword)
        {
            WebNote = "Set a library password first (Settings → Library).";
            WebNoteFixUrl = null;
        }
    }

    static DateTime Epoch(double seconds) => DateTimeOffset.FromUnixTimeMilliseconds((long)(seconds * 1000)).LocalDateTime;

    [RelayCommand]
    public async Task CopyOther()
    {
        if (Copy is null) return;
        await Copy(McpJson);
        Say = "Copied. Paste it into the tool's settings.";
    }

    /// <summary>Reads every app's settings again and shows where each stands (off the UI thread: Claude Code's
    /// settings file can be large).</summary>
    async Task LoadApps()
    {
        if (ReadApps is null) return;
        IReadOnlyList<AiAppState> states;
        try
        {
            states = await Task.Run(ReadApps);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return;
        }
        // A row for each app this computer has, in the order Settings lists them; the rest are named in one line.
        int at = 0;
        foreach (var s in states)
        {
            var row = Apps.FirstOrDefault(a => a.Id == s.Id);
            if (!s.Installed)
            {
                if (row is not null) Apps.Remove(row);
                continue;
            }
            if (row is null)
            {
                row = new AiAppRow { Id = s.Id, Name = s.Name, About = AppAbout(s.Id) };
                string id = s.Id;
                row.Connect = new AsyncRelayCommand(() => ChangeAppAsync(id, add: true));
                row.Disconnect = new AsyncRelayCommand(() => ChangeAppAsync(id, add: false));
                row.Reopen = new AsyncRelayCommand(() => ReopenAppAsync(id));
                Apps.Insert(Math.Min(at, Apps.Count), row);
            }
            row.Show(s, now());
            at = Apps.IndexOf(row) + 1;
        }
        for (int i = 0; i < Apps.Count; i++) Apps[i].First = i == 0;
        NoApps = Apps.Count == 0;
        var missing = states.Where(s => !s.Installed).Select(s => s.Name).ToList();
        MissingApps = missing.Count > 0 ? $"Not found on this computer: {string.Join(", ", missing)}." : null;
        OnPropertyChanged(nameof(ShowTry));
        OnPropertyChanged(nameof(TryLead));
    }

    /// <summary>The row's Reopen: quits the app and opens it again, then keeps looking for it to load Study Stash.</summary>
    async Task ReopenAppAsync(string id)
    {
        if (ReopenApp is null || Apps.FirstOrDefault(a => a.Id == id) is not { } row) return;
        row.Reopening = true;
        await LoadApps();
        AiAppChange done;
        try
        {
            done = await ReopenApp(id);
        }
        finally
        {
            row.Reopening = false;
        }
        row.Reopened = done.Ok;
        Say = done.Ok ? null : done.Say;
        await LoadApps();
        StopWatchingApps();
        if (done.Ok) WatchApp(id);
    }

    static string AppAbout(string id) => id switch
    {
        "claude-desktop" => "The Claude app on this computer",
        "claude-code" => "In the terminal, the Claude app's Code tab, or your editor",
        "codex" => "The ChatGPT app, and Codex in the terminal or your editor",
        "gemini" => "Google's Gemini in the terminal",
        _ => "",
    };

    async Task ChangeAppAsync(string id, bool add)
    {
        if ((add ? ConnectApp : DisconnectApp) is not { } change) return;
        Busy = true;
        try
        {
            var done = await Task.Run(() => change(id));
            Say = done.Say;
            WrittenFile = done.File;
            WrittenBackup = done.Backup;
            WrittenText = done.Written;
            // Folded away unless the setup has to be pasted in by hand, which is what the words then point at.
            DetailsOpen = !done.Ok && done.Written is { Length: > 0 };
            if (Apps.FirstOrDefault(a => a.Id == id) is { } changed) changed.Reopened = false;
            await LoadApps();
            StopWatchingApps();
            if (add && done.Ok) WatchApp(id);
        }
        finally
        {
            Busy = false;
        }
    }

    CancellationTokenSource? appWatch;

    void StopWatchingApps()
    {
        appWatch?.Cancel();
        appWatch = null;
    }

    /// <summary>After Connect or Reopen, the app still has to load Study Stash (ChatGPT, once a chat in Codex starts).
    /// Looks again every few seconds for five minutes, so the row turns to Connected by itself the moment it does,
    /// with Settings still open.</summary>
    void WatchApp(string id)
    {
        var cts = new CancellationTokenSource();
        appWatch = cts;
        _ = WatchAsync();

        async Task WatchAsync()
        {
            try
            {
                for (var i = 0; i < 100 && !cts.IsCancellationRequested; i++)
                {
                    await (Delay?.Invoke(TimeSpan.FromSeconds(3)) ?? Task.Delay(TimeSpan.FromSeconds(3), cts.Token));
                    if (cts.IsCancellationRequested) return;
                    await LoadApps();
                    if (Apps.FirstOrDefault(a => a.Id == id) is not { Ok: false, CanDisconnect: true }) return;
                }
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    [RelayCommand]
    public async Task CopyWritten()
    {
        if (Copy is null || WrittenText is not { Length: > 0 } text) return;
        await Copy(text);
        Say = "Copied.";
    }

    /// <summary>Turns Claude's address on or off through the library (task 4's <c>/api/v2/ai/access/web</c>), then,
    /// once it's on and not yet answering from the internet, checks again every few seconds for a while. A refusal
    /// (no password) or a 404 (an older library) puts the switch straight back to off.</summary>
    async Task SetWebAsync(bool on)
    {
        StopPolling();
        WebBusy = true;
        try
        {
            ToolAccessInfo? info;
            try
            {
                info = await library.SetWebAsync(on);
            }
            catch (LibraryRefusedException ex)
            {
                loading = true;
                try
                {
                    WebOn = false;
                    HasPassword = false;
                    WebNote = ex.Message;
                    WebNoteFixUrl = null;
                }
                finally
                {
                    loading = false;
                }
                return;
            }
            catch
            {
                Offline = true;
                return;
            }
            if (info is null)
            {
                loading = true;
                try
                {
                    WebSupported = false;
                    WebOn = false;
                }
                finally
                {
                    loading = false;
                }
                return;
            }
            Apply(info);
            if (WebOn && WebNote is null && WebReachable != true) StartPolling();
        }
        finally
        {
            WebBusy = false;
        }
    }

    /// <summary>The "Check again" button: one more look from the internet, outside the automatic poll.</summary>
    [RelayCommand]
    public async Task CheckWebAgain()
    {
        StopPolling();
        WebBusy = true;
        try
        {
            var info = await library.CheckWebAsync();
            if (info is not null) Apply(info);
        }
        catch
        {
            Offline = true;
        }
        finally
        {
            WebBusy = false;
        }
    }

    [RelayCommand]
    public async Task CopyWebName()
    {
        if (Copy is null) return;
        await Copy(WebName);
        Say = "Copied.";
    }

    [RelayCommand]
    public async Task CopyWebUrl()
    {
        if (Copy is null || WebUrl is not { Length: > 0 } url) return;
        await Copy(url);
        Say = "Copied.";
    }

    [RelayCommand]
    public async Task CopyWebFix()
    {
        if (Copy is null || WebNoteFixUrl is not { Length: > 0 } url) return;
        await Copy(url);
        Say = "Copied.";
    }

    [RelayCommand]
    public void OpenWebNeeds()
    {
        if (WebNeedsUrl is { Length: > 0 } url) OpenUrl?.Invoke(url);
    }

    [RelayCommand]
    public void OpenWebFix()
    {
        if (WebNoteFixUrl is { Length: > 0 } url) OpenUrl?.Invoke(url);
    }

    void StopPolling()
    {
        pollCts?.Cancel();
        pollCts = null;
        WebChecking = false;
    }

    void StartPolling()
    {
        var cts = new CancellationTokenSource();
        pollCts = cts;
        WebChecking = true;
        _ = PollAsync(cts.Token);
    }

    /// <summary>Checks again every 5 seconds, up to 12 times, while Claude's address is on but not yet answering from
    /// the internet. Stops the moment it answers, a problem shows up, or the switch goes off — otherwise gives up
    /// after 12 tries and leaves "Check again" for the student.</summary>
    async Task PollAsync(CancellationToken ct)
    {
        try
        {
            for (var i = 0; i < 12 && !ct.IsCancellationRequested; i++)
            {
                await (Delay?.Invoke(TimeSpan.FromSeconds(5)) ?? Task.Delay(TimeSpan.FromSeconds(5), ct));
                if (ct.IsCancellationRequested) return;
                ToolAccessInfo? info;
                try
                {
                    info = await library.CheckWebAsync();
                }
                catch
                {
                    return;
                }
                if (ct.IsCancellationRequested) return;
                if (info is not null) Apply(info);
                if (WebReachable == true || WebNote is not null || !WebOn) return;
            }
        }
        finally
        {
            if (pollCts is { } mine && mine.Token == ct)
            {
                pollCts = null;
                WebChecking = false;
            }
        }
    }

    async Task RemoveAsync(string id)
    {
        if (RevokeConnection is null) return;
        Busy = true;
        try
        {
            if (await RevokeConnection(id)) await Load();
        }
        finally
        {
            Busy = false;
        }
    }

    async Task PostAsync(bool? on = null, ReadingScopes? reading = null)
    {
        Busy = true;
        try
        {
            var info = await library.SetAccessAsync(on, reading);
            Apply(info);
        }
        catch (LibraryRefusedException ex)
        {
            Say = ex.Message;
        }
        catch
        {
            Offline = true;
        }
        finally
        {
            Busy = false;
        }
    }
}
