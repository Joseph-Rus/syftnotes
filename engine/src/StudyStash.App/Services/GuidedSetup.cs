using System.ComponentModel;
using System.Text.Json.Nodes;
using Avalonia.Threading;
using StudyStash.App.ViewModels;
using StudyStash.Audio;
using StudyStash.Core;
using StudyStash.Core.Ai;
using StudyStash.Core.Setup;

namespace StudyStash.App.Services;

/// <summary>
/// Guided setup's driver: what the setup tools read and show (<see cref="ISetupDriver"/>), over the window's own
/// <see cref="SetupModel"/> and the app, and what a card's buttons do (<see cref="IGuidedActions"/>), reusing setup by
/// hand's own steps (<see cref="Setup"/>, <see cref="CanvasConnectModel"/>). Only a press in a card changes the
/// computer; each press ends with a <c>[Study Stash]</c> note so the AI hears what happened. Every call from the
/// tools runs on the window's thread.
/// </summary>
public sealed class GuidedSetup : ISetupDriver, IGuidedActions
{
    readonly GuidedSetupModel g;
    readonly SetupModel m;
    readonly AppHost host;
    bool micSaid, deniedSaid, modelStarted, modelSaid, chromeSaid;

    public GuidedSetup(GuidedSetupModel guided, AppHost host)
    {
        g = guided;
        m = guided.Setup;
        this.host = host;
        g.Actions = this;
        m.PropertyChanged += OnSetupChanged;
        g.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(GuidedSetupModel.OpenCard)) m.MicCheckOpen = g.OpenCard is { IsMicrophone: true, Open: true } && g.Screen == GuidedScreen.Chat;
            if (e.PropertyName == nameof(GuidedSetupModel.Screen) && g.Screen != GuidedScreen.Chat) m.MicCheckOpen = false;
        };
    }

    /// <summary>Makes the Chrome helper's model (the app's, over the library's Canvas; tests their own).</summary>
    public Func<CanvasConnectModel?> MakeCanvas { get; init; } = () => null;
    /// <summary>Sets who writes the notes and answers questions on the library: null when it took it, else why not.</summary>
    public Func<string, Task<string?>>? WriteNotes { get; init; }
    /// <summary>The installer's suggestion (the library download says "library").</summary>
    public AppRole? Installer { get; init; } = Setup.Preset(Apps.RolePreset());

    static Task<T> OnUi<T>(Func<Task<T>> f) => Dispatcher.UIThread.CheckAccess() ? f() : Dispatcher.UIThread.InvokeAsync(f);

    static Task OnUi(Func<Task> f) => Dispatcher.UIThread.CheckAccess() ? f() : Dispatcher.UIThread.InvokeAsync(f);

    string Device => m.DeviceWord;
    bool Chosen => g.RoleChosen || m.Again || m.LibraryOk;

    // --- what the tools read ---------------------------------------------------------------------------------------------

    public Task<SetupStatus> StatusAsync() => OnUi(() => Task.FromResult(Status()));

    SetupStatus Status()
    {
        var items = g.Items();
        return new SetupStatus
        {
            Windows = g.Windows, Again = m.Again,
            Role = !Chosen ? "" : m.IsLaptop ? "laptop" : m.IsLibrary ? "library" : "one",
            Suggests = Installer == AppRole.Library ? "library" : "",
            Items = items, ReadyToFinish = SetupChecklist.ReadyToFinish(items),
            Addresses = m.IsLibrary && m.LibraryOk ? [.. m.Addresses.Select(a => a.Url)] : [],
            NotesWriter = g.NotesWriter switch { "" => "", "none" => "nobody (transcripts only)", "ollama" => "Ollama", var e => AgentCli.Get(e).Brand },
            Busy = m.Connecting ? (m.IsLaptop ? "connecting to the library" : "making the library")
                : m.Finding ? "looking for the library"
                : m.AddingCourses ? "adding the classes"
                : g.OpenCard is { Busy: true } c ? c.Kind.Replace('_', ' ') : null,
        };
    }

    public Task<IReadOnlyList<SetupModelOption>> ModelsAsync() => OnUi(() =>
    {
        if (m.Models.Count == 0 && (m.IsOneComputer || m.IsLaptop)) Setup.ShowModels(m, host);
        return Task.FromResult<IReadOnlyList<SetupModelOption>>(
            [.. m.Models.Select(c => new SetupModelOption(c.Model.Id, c.Name, c.Size, c.Here, c.Recommended, c.Why))]);
    });

    public Task<SetupCourses> CoursesAsync() => OnUi(() =>
    {
        if (m.Canvas is not { } c) return Task.FromResult(new SetupCourses(false, [], "Chrome isn't connected yet: offer_chrome_helper first."));
        if (!c.ChromeConnected) return Task.FromResult(new SetupCourses(false, [], "Chrome isn't connected yet. The Chrome helper card is waiting for it."));
        return Task.FromResult(new SetupCourses(true, [.. c.Found.Select(f => (f.ClassName, f.Code))],
            c.FindingCourses ? "Canvas is still looking for courses." : c.CoursesSay ?? ""));
    });

    // --- what the tools show -----------------------------------------------------------------------------------------------

    public Task AskAsync(string question, IReadOnlyList<string> choices) => OnUi(() =>
    {
        g.ShowQuestion(question, choices);
        return Task.CompletedTask;
    });

    public Task<string?> OfferAsync(SetupCard card) => OnUi(async () =>
    {
        switch (card.Kind)
        {
            case "chrome_helper":
                if (m.Canvas is null && MakeCanvas() is { } made) m.Canvas = made;
                if (m.Canvas is not { } c) return "Canvas can't be connected from setup here. The student can do it later in Settings → Canvas.";
                // The school's address is checked and saved (the library's own setting, as the Canvas step does);
                // the helper itself is only added when the student presses Add to Chrome.
                if (!c.ChromeConnected)
                {
                    c.SchoolField = card.Arg;
                    await c.ContinueCommand.ExecuteAsync(null);
                    if (c.SchoolError is { Length: > 0 } bad) return $"That Canvas address didn't work: {bad.TrimEnd('.')}. Ask the student to check it.";
                }
                chromeSaid = false;
                break;
            case "course_picker" when m.Canvas is { Found.Count: > 0 } canvas:
                m.TakeCourses(canvas.Found);
                break;
            case "microphone_check":
                micSaid = deniedSaid = false;
                break;
        }
        g.ShowCard(card);
        return null;
    });

    public Task<string?> AddClassAsync(string name, string about) => OnUi(async () =>
    {
        if (!m.LibraryOk || host.Remote() is not { } lib) return "Set up the library first (offer_computer_setup): the classes are its.";
        if (m.Classes.Any(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) return $"{name} is already a class.";
        try
        {
            await lib.AddClassAsync(name, about.Length > 0 ? about : null);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or LibraryRefusedException)
        {
            return $"The library didn't take the class: {e.Message.TrimEnd('.')}.";
        }
        m.Classes.Add(new SetupClass { Name = name, About = about, Dot = Skin.ClassDot(Math.Max(0, host.ColorOf(name))) });
        _ = host.CheckLibraryAsync();
        return null;
    });

    public Task<string?> SetNotesWriterAsync(string engine) => OnUi(() => WriterAsync(engine));

    async Task<string?> WriterAsync(string engine)
    {
        if (m.IsLaptop) return "The library writes the notes, on its own computer: that's set there.";
        if (!m.LibraryOk) return "Set up the library first (offer_computer_setup).";
        if (engine is "claude" or "codex" && engine != g.Picked)
            return $"Only {g.Brand} is set up on this {Device}. The student can add {AgentCli.Get(engine).Brand} later in Settings → AI engines.";
        if (engine is "claude" or "codex" && !g.AiReady) return $"{g.Brand} isn't ready yet.";
        string? refused = await (WriteNotes ?? WriteOnLibraryAsync)(engine);
        if (refused is not null) return refused;
        g.NotesWriter = engine;
        m.NotesSummary = engine == "none" ? "Transcripts only for now" : $"Notes by {(engine == "ollama" ? "Ollama" : AgentCli.Get(engine).Brand)}";
        return null;
    }

    /// <summary>The library's AI settings: that engine writes the notes and answers questions ("none": transcripts
    /// only).</summary>
    async Task<string?> WriteOnLibraryAsync(string engine)
    {
        if (host.Remote() is not { } lib) return "The library isn't reachable right now.";
        var cc = host.Client();
        try
        {
            await lib.SettingsAsync(HttpMethod.Post, "", new JsonObject { ["notes"] = new JsonObject { ["write"] = engine != "none", ["sort"] = engine != "none" } });
            if (engine != "none") await new AiRemote(cc.ServerUrl, cc.PoolKey).DefaultsAsync(notes: engine, ask: engine);
            return null;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or LibraryRefusedException or System.Text.Json.JsonException)
        {
            return e is LibraryRefusedException { Message.Length: > 0 } ? e.Message : "The library didn't answer. Try again in a moment.";
        }
    }

    public Task<string?> SkipAsync(string step) => OnUi(() =>
    {
        if (g.Items().All(i => i.Id != step)) return Task.FromResult<string?>($"That isn't a step on this {Device}.");
        g.Skip(step);
        return Task.FromResult<string?>(null);
    });

    public Task OpenManualAsync(string step) => OnUi(() =>
    {
        g.OpenManual(step);
        return Task.CompletedTask;
    });

    // --- what a card's buttons do --------------------------------------------------------------------------------------------

    public async Task PressAsync(CardEntry card, string action)
    {
        card.Problem = null;
        switch (card.Kind, action)
        {
            case ("computer_setup", "setup"):
                await ComputerAsync(card);
                break;
            case ("library_password", "create"):
                await Busy(card, () => m.ConnectCommand.ExecuteAsync(null));
                if (!m.LibraryOk)
                {
                    card.Problem = m.LibraryResult;
                    break;
                }
                await Setup.FillAddressesAsync(m, host);
                Fold(card, $"{m.LibraryName} is ready");
                string wrote = await DefaultWriterAsync();
                g.Note($"{m.LibraryName} is ready on this {Device}", $"The library \"{m.LibraryName}\" is made on this {Device}, with its password.{wrote}");
                break;
            case ("library_connection", "find"):
                await Busy(card, () => m.FindCommand.ExecuteAsync(null));
                break;
            case ("library_connection", "connect"):
                await Busy(card, () => m.ConnectCommand.ExecuteAsync(null));
                if (!m.LibraryOk)
                {
                    card.Problem = m.LibraryResult;
                    break;
                }
                string to = (m.LibraryResult ?? "Connected.").TrimEnd('.');
                Fold(card, to);
                g.Note(to, $"The laptop is connected to its library. ({to}.)");
                break;
            case ("microphone_check", "allow"):
                await m.AllowMicCommand.ExecuteAsync(null);
                break;
            case ("microphone_check", "settings"):
                m.MicSettingsCommand.Execute(null);
                break;
            case ("model_download", "download"):
                Download(card);
                break;
            case ("model_download", "notnow"):
                Fold(card, "Not now");
                g.Note("Not downloading the model for now", "The student said \"Not now\" to the download.", good: false);
                break;
            case ("model_download", _) when action.StartsWith("pick:", StringComparison.Ordinal):
                card.ModelId = action[5..];
                break;
            case ("chrome_helper", "add"):
                if (m.Canvas is not { } c) break;
                await Busy(card, () => c.AddToChromeCommand.ExecuteAsync(null));
                card.Problem = c.ChromeError;
                break;
            case ("chrome_helper", "extensions"):
                m.Canvas?.OpenChromeExtensionsCommand.Execute(null);
                break;
            case ("chrome_helper", "folder"):
                m.Canvas?.ShowFolderCommand.Execute(null);
                break;
            case ("course_picker", "add"):
                await CoursesAsync(card);
                break;
            case ("start_at_login", "on"):
                try
                {
                    host.LoginItems.StartAtLogin(true, host.Home);
                }
                catch (Exception e) when (e is InvalidOperationException or IOException or UnauthorizedAccessException or System.Security.SecurityException)
                {
                    host.Log($"[setup] start at login: {e.Message}");
                    card.Problem = "Study Stash couldn't turn that on. You can try again in Settings → General.";
                    break;
                }
                m.StartAtLogin = true;
                g.StartsAtLogin = true;
                Fold(card, "Starts when you log in");
                g.Note("Start at login is on", "Start at login is on.");
                break;
            case ("start_at_login", "notnow"):
                g.Skip("start_at_login");
                Fold(card, "Not now");
                g.Note("Start at login is off for now", "The student said \"Not now\" to start at login.", good: false);
                break;
            case ("taskbar_tip", "open"):
                m.TaskbarSettingsCommand.Execute(null);
                break;
            case ("taskbar_tip", "done"):
                g.TaskbarDone = true;
                Fold(card, "Done");
                g.Note("Taskbar tip done", "The student has seen the taskbar tip.");
                break;
            case ("finish", "finish"):
                g.FinishCommand.Execute(null);
                break;
        }
    }

    static async Task Busy(CardEntry card, Func<Task> work)
    {
        card.Busy = true;
        try
        {
            await work();
        }
        finally
        {
            card.Busy = false;
        }
    }

    static void Fold(CardEntry card, string outcome)
    {
        card.Outcome = outcome;
        card.Open = false;
    }

    /// <summary>The computer card's Set up: what this computer is for; just this computer also makes its library,
    /// quietly (the welcome's Continue in setup by hand).</summary>
    async Task ComputerAsync(CardEntry card)
    {
        var role = card.Choice switch { "laptop" => AppRole.Laptop, "library" => AppRole.Library, _ => AppRole.Both };
        if (!m.Again) m.SetRole(role);
        g.RoleChosen = true;
        if (m.IsLaptop)
        {
            Fold(card, "This is my laptop");
            g.Note("This is your laptop", "The student chose: this is their laptop, and their library is on another computer. Next, connect to it (offer_library_connection).");
            return;
        }
        if (m.IsLibrary)
        {
            Fold(card, "This is my library");
            g.Note("This is your library", "The student chose: this computer is their library. Next, its name and password (offer_library_password).");
            return;
        }
        if (!m.LibraryOk) await Busy(card, () => m.ConnectCommand.ExecuteAsync(null));
        if (!m.LibraryOk)
        {
            card.Problem = m.LibraryResult ?? "The library didn't start.";
            g.Note("Your library didn't start", $"Making the library on this {Device} didn't work: {card.Problem}", good: false);
            return;
        }
        Fold(card, $"Just this {Device} · your library is ready");
        string wrote = await DefaultWriterAsync();
        g.Note($"Your library is ready on this {Device}", $"The student chose just this {Device}. The library is ready on this {Device}.{wrote}");
    }

    /// <summary>The AI the student picked writes the notes, once the library's here (they can change it later).</summary>
    async Task<string> DefaultWriterAsync()
    {
        if (g.NotesWriter.Length > 0 || !g.AiReady || m.IsLaptop) return "";
        return await WriterAsync(g.Picked) is null ? $" {g.Brand} writes the notes." : "";
    }

    void Download(CardEntry card)
    {
        if (card.Model is not { } choice) return;
        host.Save(s => s.Model = choice.Model.Id);
        foreach (var c in m.Models) c.Chosen = c == choice;
        m.ChosenModel = choice;
        m.ModelName = choice.Name;
        m.ModelSize = choice.Size;
        modelStarted = true;
        modelSaid = false;
        if (!WhisperModels.IsDownloaded(host.Home, choice.Model)) _ = host.DownloadModelAsync(choice.Model);
        Setup.Refresh(m, host);
        Fold(card, m.ModelReady ? $"{choice.Name} is ready" : $"Downloading {choice.Name}");
        g.Note(m.ModelReady ? $"{choice.Name} is ready" : $"Downloading {choice.Name} · it carries on in the background",
            m.ModelReady ? $"{choice.Name} is already downloaded and ready." : $"The student started the download of {choice.Name}, {choice.Size}. It continues in the background.");
        if (m.ModelReady) modelSaid = true;
    }

    async Task CoursesAsync(CardEntry card)
    {
        var ticked = m.Courses.Where(c => c.Ticked).Select(c => c.Name.Trim()).Where(n => n.Length > 0).ToList();
        if (ticked.Count == 0)
        {
            card.Problem = "Tick at least one course, or skip this for now.";
            return;
        }
        await Busy(card, () => Setup.AddCoursesAsync(m, host));
        Setup.ListClasses(m, host);
        foreach (string name in ticked)
            if (m.Classes.All(c => c.Name != name)) m.Classes.Add(new SetupClass { Name = name, Dot = Skin.ClassDot(Math.Max(0, host.ColorOf(name))) });
        Fold(card, $"Added {ticked.Count} class{(ticked.Count == 1 ? "" : "es")}");
        g.Note($"Added {ticked.Count} class{(ticked.Count == 1 ? "" : "es")} from Canvas",
            $"The student added {ticked.Count} class{(ticked.Count == 1 ? "" : "es")} from Canvas: {string.Join(", ", ticked.Take(8))}{(ticked.Count > 8 ? ", …" : "")}.");
    }

    // --- what happens on its own after a press -------------------------------------------------------------------------------

    void OnSetupChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(SetupModel.MicHeard) when m.MicHeard && !micSaid && g.OpenCard is { IsMicrophone: true } mic:
                micSaid = true;
                Fold(mic, "Study Stash hears you");
                m.MicCheckOpen = false;
                g.Note("Microphone allowed · Study Stash hears you", "The microphone is allowed and Study Stash hears the student.");
                break;
            case nameof(SetupModel.MicDenied) or nameof(SetupModel.MicTrouble) when m.ShowMicProblem && !deniedSaid && g.OpenCard is { IsMicrophone: true, Open: true }:
                deniedSaid = true;
                string os = g.Windows ? "Windows" : "macOS";
                g.Note(m.MicTrouble?.Title ?? $"{os} is blocking the microphone",
                    m.MicTrouble is { } t ? $"The microphone has a problem: {t.Title}. The card shows how to fix it." : $"{os} is blocking the microphone. The card shows how to turn it on.",
                    good: false);
                break;
            case nameof(SetupModel.ModelReady) when m.ModelReady && modelStarted && !modelSaid:
                modelSaid = true;
                g.Note($"{m.ModelName} is ready", $"The download finished: {m.ModelName} is ready.");
                break;
            case nameof(SetupModel.Canvas) when m.Canvas is { } c:
                c.PropertyChanged += OnCanvasChanged;
                break;
        }
    }

    void OnCanvasChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not CanvasConnectModel c || chromeSaid) return;
        if (e.PropertyName is not (nameof(CanvasConnectModel.Found) or nameof(CanvasConnectModel.FindingCourses) or nameof(CanvasConnectModel.CoursesSay))) return;
        if (!c.ChromeConnected || c.FindingCourses) return;
        chromeSaid = true;
        if (g.OpenCard is { IsChrome: true } card) Fold(card, "Chrome is connected");
        int n = c.Found.Count;
        if (n > 0) g.Note($"Chrome is connected · Canvas found {n} course{(n == 1 ? "" : "s")}", $"Chrome is connected. Canvas found {n} course{(n == 1 ? "" : "s")}.");
        else g.Note("Chrome is connected", $"Chrome is connected, but Canvas found no courses{(c.CoursesSay is { Length: > 0 } say ? ": " + say.TrimEnd('.') : "")}.", good: false);
        g.Refresh();
    }
}
