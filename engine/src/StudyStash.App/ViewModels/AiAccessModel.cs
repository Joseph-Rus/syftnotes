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
    /// <summary>terminal for Claude, code for Codex, hub for anything else — the row's icon tile.</summary>
    public string Icon => Name switch { "Claude Code" or "Claude" or "Claude Desktop" => "terminal", "Codex" => "code", _ => "hub" };
    /// <summary>The first row in the list shows no separator above it.</summary>
    public bool First { get; set; }
}

/// <summary>One AI app on this computer (Claude Desktop, Claude Code, Codex, Gemini CLI): what it's called, where it
/// stands, and its own Connect and Disconnect, closed over its id.</summary>
public sealed partial class AiAppRow : ObservableObject
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    /// <summary>What the app is, under its name ("The app, the CLI and the IDE extension").</summary>
    public string About { get; init; } = "";
    public string Icon => Id switch { "codex" => "code", "gemini" => "auto_awesome", "claude-code" => "terminal", _ => "desktop_windows" };
    [ObservableProperty] public partial string Status { get; set; } = "";
    /// <summary>Connected: an app started Study Stash since it was added.</summary>
    [ObservableProperty] public partial bool Ok { get; set; }
    [ObservableProperty] public partial bool CanConnect { get; set; }
    [ObservableProperty] public partial bool CanDisconnect { get; set; }
    /// <summary>"Connect", or "Fix" when the app has another copy of Study Stash set up.</summary>
    [ObservableProperty] public partial string ConnectWords { get; set; } = "Connect";
    public bool First { get; set; }
    public IAsyncRelayCommand? Connect { get; internal set; }
    public IAsyncRelayCommand? Disconnect { get; internal set; }
    /// <summary>Windows shows the status in the one subtitle line.</summary>
    public string WinDetail => Status;
    partial void OnStatusChanged(string value) => OnPropertyChanged(nameof(WinDetail));

    /// <summary>Where an app stands, in words: not here, not connected, added but not loaded yet, connected (and when
    /// it last started Study Stash), or set up for another copy of Study Stash.</summary>
    public void Show(AiAppState s, DateTime now)
    {
        Ok = false;
        CanConnect = s.Installed && (!s.Added || s.OtherCopy);
        CanDisconnect = s.Added;
        ConnectWords = s.OtherCopy ? "Fix" : "Connect";
        if (!s.Installed) Status = "Not on this computer";
        else if (!s.Added) Status = "Not connected";
        else if (s.OtherCopy) Status = "Set up for another copy of Study Stash. Choose Fix to use this one.";
        else if (s.Started is { } started && (s.AddedAt is null || started >= s.AddedAt))
        {
            Ok = true;
            Status = "Connected · " + (started.Date == now.Date ? $"started {started:H:mm}" : now - started < TimeSpan.FromDays(7) ? $"started {started:ddd}" : $"started {started:d MMM}");
        }
        else Status = s.Name switch
        {
            "Claude Desktop" => "Added. Quit and reopen Claude Desktop to load it.",
            "Claude Code" => "Added. Start a new Claude Code session to load it.",
            "Codex" => "Added. Restart Codex to load it.",
            _ => $"Added. Start {s.Name} again to load it.",
        };
    }
}

/// <summary>
/// Settings → AI tool access (design 14): the off switch, what AI apps may read, the AI apps on this computer (each
/// connected with one click, no Tailscale needed), Claude on the web (Tailscale Funnel), and the other connections.
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

    // Claude on the web and phone: whether Claude can reach this library over the internet, through Tailscale Funnel
    // (task 4's ClaudeReach/ReachCheck, read through ToolAccessInfo.Web). WebOn is the switch Settings shows; the
    // library is the truth, so every change round-trips through it before the switch visibly moves.
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
    /// <summary>The master AI-tool-access switch (<see cref="On"/>) is off: Claude can be signed in and the address
    /// can answer, but every tool call is refused. Shown as a quiet note under the card, not another problem.</summary>
    public bool ShowToolsOffNote => !On;
    public bool WebToggleEnabled => WebSupported && HasPassword;

    /// <summary>Claude sign-ins (<c>kind == "signin"</c>): the ones this card lists and can remove. A token made in
    /// Settings, or this computer's own Claude Code/Desktop, stay in <see cref="Connected"/> below instead.</summary>
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

    /// <summary>The "what was written" panel after a change: the app's settings file, the copy of it from before, and
    /// the setup that went in (no secrets: the apps start Study Stash, which reads its own settings).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowWritten))]
    public partial string? WrittenFile { get; set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WrittenBackupWords))]
    public partial string? WrittenBackup { get; set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasWrittenText))]
    public partial string? WrittenText { get; set; }
    public bool ShowWritten => WrittenFile is { Length: > 0 };
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
        foreach (var s in states)
        {
            var row = Apps.FirstOrDefault(a => a.Id == s.Id);
            if (row is null)
            {
                row = new AiAppRow { Id = s.Id, Name = s.Name, About = AppAbout(s.Id), First = Apps.Count == 0 };
                string id = s.Id;
                row.Connect = new AsyncRelayCommand(() => ChangeAppAsync(id, add: true));
                row.Disconnect = new AsyncRelayCommand(() => ChangeAppAsync(id, add: false));
                Apps.Add(row);
            }
            row.Show(s, now());
        }
    }

    static string AppAbout(string id) => id switch
    {
        "claude-desktop" => "The Claude app on this computer",
        "claude-code" => "In the terminal, the Claude app's Code tab, or your editor",
        "codex" => "The Codex app, CLI and IDE extension share one setup",
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
            await LoadApps();
        }
        finally
        {
            Busy = false;
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
