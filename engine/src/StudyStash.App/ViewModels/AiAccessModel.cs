using StudyStash.App.Services.Kit;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StudyStash.App.Services;
using StudyStash.Core;
using StudyStash.Core.Ai;

namespace StudyStash.App.ViewModels;

/// <summary>One library grant: an app signed in from the web, or a token made in Settings. The row owns its own
/// Remove, closed over its id. (The AI apps on this computer are on their AI's card, in <see cref="AiEnginesModel"/>.)</summary>
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
    /// <summary>The row's icon: the product's own mark for Claude, ChatGPT, Codex and Gemini, a plain one for anything else.</summary>
    public string Icon => Name switch
    {
        "Claude Code" or "Claude" or "Claude Desktop" => "brand:claude",
        "ChatGPT" => "brand:chatgpt",
        "Codex" => "brand:codex",
        "Gemini" or "Gemini CLI" or "Antigravity" => "brand:gemini",
        _ => "hub",
    };
    /// <summary>The first row in the list shows no separator above it.</summary>
    public bool First { get; set; }
}

/// <summary>
/// Settings → Your AI → Advanced settings, the AI apps' part: the off switch, what AI apps may read and change, Claude
/// and ChatGPT on the web (Tailscale Funnel), the other connections, and the setup to paste into any other AI app. (The
/// AI apps on this computer are connected from their AI's card: <see cref="AiEnginesModel.ConnectAsync"/>.)
/// Reads and drives one library's AI (<see cref="IAiLibrary"/>); the clipboard and the browser are hooks the host sets.
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
    /// <summary>AI apps may save the notes a student asks them to write for a lecture (save_notes). On by default: it
    /// only ever happens when the student asks, and the notes it replaces are kept.</summary>
    [ObservableProperty] public partial bool SaveNotes { get; set; } = true;

    /// <summary>The switch's words, as a tool that's refused names it (<see cref="ToolAccess.SaveToggle"/>).</summary>
    public const string SaveToggle = ToolAccess.SaveToggle;
    public const string SaveToggleSub = "When you ask an AI app to write a lecture's notes, they're saved on its page. Notes they replace are kept, so you can put them back.";

    /// <summary>The one line a student with no paid AI is told here: the Claude app can write their notes.</summary>
    public const string NoPlanLine = "No paid AI? Connect Claude at the top of this page, then ask the free Claude app to “write notes for my last lecture”: they appear on the lecture's page.";
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
    public string WebName => "Syft Notes";
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

    /// <summary>The <c>mcpServers</c> JSON for any other MCP app, to paste in by hand (from <c>ClaudeSetup</c>).</summary>
    public string McpJson { get; set; } = "";

    /// <summary>Puts text on the clipboard (write-only: this model never reads it).</summary>
    public Func<string, Task>? Copy { get; set; }
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
    partial void OnSaveNotesChanged(bool value) => PostReading();

    void PostReading()
    {
        if (!loading) _ = PostAsync(reading: new ReadingScopes(ReadLectures, ReadNotes, ReadCanvas, ReadAudio, SaveNotes));
    }

    [RelayCommand] public void ToggleLectures() => ReadLectures = !ReadLectures;
    [RelayCommand] public void ToggleNotes() => ReadNotes = !ReadNotes;
    [RelayCommand] public void ToggleCanvas() => ReadCanvas = !ReadCanvas;
    [RelayCommand] public void ToggleAudio() => ReadAudio = !ReadAudio;
    [RelayCommand] public void ToggleSaveNotes() => SaveNotes = !SaveNotes;

    [RelayCommand]
    public Task Refresh() => Load();

    /// <summary>Reads the library's tool access once: called when the pane opens.</summary>
    public async Task Load()
    {
        ToolAccessInfo? info;
        try
        {
            info = await library.AccessAsync();
        }
        catch (Exception e) when (LibraryCall.Handled(e, "[ai-access] Load"))
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
            SaveNotes = info.Reading.SaveNotes;
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
            catch (Exception e) when (LibraryCall.Handled(e, "[ai-access] SetWebAsync"))
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
        catch (Exception e) when (LibraryCall.Handled(e, "[ai-access] CheckWebAgain"))
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
                await (Delay?.Invoke(TimeSpan.FromSeconds(5)) ?? Poll.AfterAsync(TimeSpan.FromSeconds(5), ct));
                if (ct.IsCancellationRequested) return;
                ToolAccessInfo? info;
                try
                {
                    info = await library.CheckWebAsync();
                }
                catch (Exception e) when (LibraryCall.Handled(e, "[ai-access] PollAsync"))
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
        catch (Exception e) when (LibraryCall.Handled(e, "[ai-access] PostAsync"))
        {
            Offline = true;
        }
        finally
        {
            Busy = false;
        }
    }
}
