using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using StudyStash.App.Platform;
using StudyStash.Audio;
using StudyStash.Core;

namespace StudyStash.App.Services;

/// <summary>What this computer does: record and send lectures (Laptop), also run the library (Both), or only run the
/// library, with no recording (Library).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AppRole>))]
public enum AppRole
{
    Laptop,
    Both,
    Library,
}

/// <summary>Settings → Appearance's mode: follow the computer's own light/dark setting, or always show one.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AppAppearance>))]
public enum AppAppearance
{
    System,
    Light,
    Dark,
}

/// <summary>The app's own settings (app.json beside client.toml): what's been set up, and how to record.</summary>
public sealed class AppSettings
{
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    public bool SetupDone { get; set; }
    /// <summary>The Whisper model's id; empty picks the one for this computer.</summary>
    public string Model { get; set; } = "";
    /// <summary>The lighter model Study Stash once suggested for this computer (an install on a heavier one hears it
    /// once, never again); empty until then.</summary>
    public string ModelSuggested { get; set; } = "";
    /// <summary>"" finds each lecture's language; "en" and so on fixes it.</summary>
    public string Language { get; set; } = "";
    /// <summary>Windows: record what the computer plays too (a lecture on Zoom).</summary>
    public bool ComputerAudio { get; set; }
    /// <summary>Tell the voices in a lecture apart once it's written down, and say who's speaking in the transcript.</summary>
    public bool Speakers { get; set; }
    /// <summary>Filed lectures' audio is deleted after this many days (the notes and transcript stay). 0 keeps it.</summary>
    public int KeepAudioDays { get; set; } = 30;
    /// <summary>"Download as Markdown…" includes the transcript too (Settings' words: "Include transcripts").</summary>
    public bool DownloadTranscripts { get; set; }
    /// <summary>What this computer is for.</summary>
    public AppRole Role { get; set; } = AppRole.Laptop;
    /// <summary>This computer is the library too (Both or Library). Read-only: set <see cref="Role"/> instead. Kept
    /// for the places that only ask "is it here", such as Settings' binding.</summary>
    [JsonIgnore]
    public bool LibraryHere => Role != AppRole.Laptop;
    public bool Shortcuts { get; set; } = true;
    /// <summary>Shortcuts changed from their defaults, by <see cref="KeyAction"/> name: {"Record": "Control+Shift+R"}.</summary>
    public Dictionary<string, string> Keys { get; set; } = [];
    public double? RecorderX { get; set; }
    public double? RecorderY { get; set; }
    /// <summary>Where the library window was left, and its size (or that it was zoomed), so it opens there again.</summary>
    public WindowPlace? LibraryWindow { get; set; }
    /// <summary>The colour theme's name (Settings → Appearance): "Lagoon", "Plum"…</summary>
    public string Theme { get; set; } = "Lagoon";
    /// <summary>Light, dark, or match the computer (Settings → Appearance). Settings files saved before this
    /// existed have none, so they come back as <see cref="AppAppearance.System"/>: nothing changes for them.</summary>
    public AppAppearance Appearance { get; set; } = AppAppearance.System;
    /// <summary>How setup was done: "claude" or "codex" (guided, with that AI), "ollama" (by hand, the free model on
    /// this computer), "manual" (by hand), or "" not yet.</summary>
    public string SetupAi { get; set; } = "";
    /// <summary>Guided setup's chat, so a window closed part-way picks it up again: the AI's CLI and its session.</summary>
    public SetupChatSaved? SetupChat { get; set; }
    /// <summary>The version of Study Stash that last ran here: a newer one starting (it updated itself) says so, once.</summary>
    public string LastVersion { get; set; } = "";
    /// <summary>When the student said "Maybe later" to the library window's ask for a tip (it asks once more a month
    /// on); null until they do.</summary>
    public DateTimeOffset? SupportAskLater { get; set; }
    /// <summary>The ask for a tip never shows again: they tipped, said don't ask again, or put it off twice.</summary>
    public bool SupportAskDone { get; set; }

    public static string PathIn(string home) => System.IO.Path.Combine(home, "app.json");

    public static AppSettings Load(string home)
    {
        try
        {
            string text = File.ReadAllText(PathIn(home));
            var settings = JsonSerializer.Deserialize<AppSettings>(text, Json) ?? new AppSettings();
            // Before roles existed, "library_here": true meant Both; a file with no "role" yet still means that.
            if (JsonNode.Parse(text) is JsonObject raw && raw["role"] is null && raw["library_here"]?.GetValue<bool>() == true)
                settings.Role = AppRole.Both;
            return settings;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return new AppSettings();
        }
    }

    public void Save(string home)
    {
        Directory.CreateDirectory(home);
        string path = PathIn(home), tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, Json));
        File.Move(tmp, path, overwrite: true);
    }
}

/// <summary>Guided setup's conversation: which CLI ("claude" or "codex") and its own session id, to resume.</summary>
public sealed record SetupChatSaved(string Provider, string Session);

/// <summary>A window's spot (in the screen's pixels, as Avalonia gives a window's position), its size in the
/// window's own units, and whether it was zoomed to fill the display.</summary>
public sealed record WindowPlace(int X, int Y, double Width, double Height, bool Zoomed = false);

/// <summary>What pressing Record came to: the lecture now recording, or why the microphone didn't start.</summary>
public sealed record RecordStart(Lecture? Lecture, MicTrouble? Trouble);

/// <summary>How the library answered lately.</summary>
public enum LibraryState
{
    NotSetUp,
    /// <summary>This computer's own library is starting: not "Can't reach your library" while it does.</summary>
    Starting,
    Connected,
    Unreachable,
    WrongPassword,
}

/// <summary>
/// Everything the app runs, apart from its windows: the recorder, Whisper and the sender on their threads, the
/// library's classes (asked for every 20 seconds, which also says whether it's reachable), the model and its
/// download. The windows read it and are told when it changes.
/// </summary>
public sealed partial class AppHost : IDisposable, IProblemSource
{
    readonly CancellationTokenSource stop = new();
    readonly Action<string> log;
    readonly Func<IAudioSource>? pretendMic;
    readonly IMicPermissions mics;
    readonly List<Task> running = [];
    Timer? watchdog;
    int checking;
    KeepAwake? awake;
    readonly ModelSetting models;
    /// <summary>What this computer has, asked once on the thread pool as the app starts.</summary>
    readonly Task<HardwareProfile> hardware;
    readonly HttpClient? http;
    readonly Func<ISpeakerLabeler>? voices;
    readonly Func<LibraryService>? localLibrary;
    // The model download: one at a time, its stop button, and a nudge that ends a wait to try again.
    readonly Lock downloadLock = new();
    CancellationTokenSource? download;
    TaskCompletionSource? retryNow;
    Task downloadTask = Task.CompletedTask;
    volatile bool disposed;

    public string Home { get; }
    public AppSettings Settings { get; private set; }
    public LectureStore Lectures { get; }
    public Recorder Recorder { get; }
    public TranscriptionWorker Whisper { get; }
    public LectureSender Sender { get; }

    public LibraryState Library { get; private set; } = LibraryState.NotSetUp;
    /// <summary>This computer's own library, for Both/Library roles: started in <see cref="Start"/>, stopped in
    /// <see cref="Dispose"/>. Null on a plain laptop.</summary>
    public LibraryService? LocalLibrary { get; private set; }
    /// <summary>The library is from before /api/v2 (the Python engine): it files lectures, but can't be browsed, searched
    /// or asked from the app until it's updated.</summary>
    public bool OlderLibrary { get; private set; }
    /// <summary>The library's name, classes (in order: their colors) and the rest of /api/v2/library.</summary>
    public JsonObject? Overview { get; private set; }
    public DownloadProgress? Downloading { get; private set; }
    /// <summary>The model <see cref="Downloading"/> is about.</summary>
    public WhisperModel? DownloadingModel { get; private set; }
    /// <summary>Why the model isn't downloading ("Not enough space for …", "The download stopped (no internet?)…").</summary>
    public string? DownloadProblem { get; private set; }
    /// <summary>Why Whisper isn't writing lectures down ("Whisper couldn't start: …"); null while it works.</summary>
    public string? WhisperProblem => Whisper.Problem;
    /// <summary>Why the recording paused by itself (the microphone, the disk); null while all is well.</summary>
    public string? RecorderProblem => Recorder.LastProblem;

    // IProblemSource: Problems.For reads AppHost through these, so a test can hand it a fake instead.
    AppRole IProblemSource.Role => Settings.Role;
    LibraryServiceState? IProblemSource.LocalLibraryState => LocalLibrary?.State;
    string? IProblemSource.LocalLibraryFailure => LocalLibrary?.Failure;

    /// <summary>Anything the windows show changed (called on a worker thread).</summary>
    public event Action? Changed;
    /// <summary>A lecture was filed: its title and class, for a notification.</summary>
    public event Action<Lecture>? Filed;
    /// <summary>Whisper wrote down more of the lecture being recorded.</summary>
    public event Action<Lecture, IReadOnlyList<Spoken>>? Heard;
    /// <summary>Something went wrong that the person should hear about now: a title and what to know.</summary>
    public event Action<string, string>? Problem;

    /// <summary><see cref="Problem"/>'s title when the lecture paused by itself (the microphone, the disk): the
    /// notification waits for the student and offers Resume.</summary>
    public const string RecordingPaused = "Recording paused";

    /// <summary>Starting the app at login (the real login items unless a test gives its own).</summary>
    public ILoginItems LoginItems { get; }

    /// <summary>
    /// The app's engine room for a settings folder. <paramref name="microphone"/> stands in for the microphone; without
    /// one, STUDYSTASH_MIC_FILE (a WAV) does, and only with neither is the real microphone ever opened.
    /// <paramref name="models"/> is the model the environment asks for (by default, this process's: see
    /// <see cref="ModelSetting"/>); <paramref name="http"/> downloads it (a test's pretend server).
    /// <paramref name="localLibrary"/> makes this computer's own library (a test's, on a spare port with no real child).
    /// <paramref name="hardware"/> says what this computer has, which picks its model (a test's pretend computer).
    /// <paramref name="voices"/> tells a lecture's voices apart (a test's pretend one).
    /// </summary>
    public AppHost(string home, Func<IAudioSource>? microphone = null, Func<ITranscriber>? whisper = null, LaptopHost? laptop = null,
        Action<string>? log = null, ILoginItems? loginItems = null, ModelSetting? models = null, HttpClient? http = null,
        Func<LibraryService>? localLibrary = null, IMicPermissions? micPermissions = null, IHardwareProbe? hardware = null,
        Func<ISpeakerLabeler>? voices = null)
    {
        this.voices = voices;
        var probe = hardware ?? HardwareProbe.System;
        this.hardware = Task.Run(probe.Probe);
        this.log = log ?? (s => Console.WriteLine(s));
        _ = this.hardware.ContinueWith(t =>
        {
            var advice = WhisperModels.Advise(t.Result);
            this.log($"[model] this computer: {t.Result.Describe()}; the model for it: {advice.Model.Name}");
        }, TaskContinuationOptions.OnlyOnRanToCompletion);
        Home = home;
        pretendMic = microphone ?? MicFromEnvironment();
        mics = micPermissions ?? (pretendMic is not null ? MicPermissions.Pretend : MicPermissions.System);
        this.models = models ?? ModelSetting.FromEnvironment();
        this.http = http;
        this.localLibrary = localLibrary;
        LoginItems = loginItems ?? Platform.LoginItems.System;
        Directory.CreateDirectory(home);
        Settings = AppSettings.Load(home);
        KeepModelInUse();
        Lectures = new LectureStore(home);
        Recorder.Recover(Lectures, this.log);
        Recorder = new Recorder(Lectures, OpenMic, log: this.log);
        Whisper = new TranscriptionWorker(Lectures, whisper ?? LoadWhisper, () => Recorder.Current, this.log)
        {
            EngineName = () => Model.Engine == SpeechEngine.Parakeet ? "Parakeet" : "Whisper",
            LabelVoices = () => Settings.Speakers && SpeakerModelReady,
            LoadVoices = () => voices?.Invoke() ?? new SherpaSpeakerLabeler(WhisperModels.PathFor(Home, WhisperModels.Speakers)),
        };
        var net = laptop ?? new LaptopHost();
        laptopHost = net;
        Sender = new LectureSender(Lectures, Client, net, this.log);
        Recorder.Changed += () => Changed?.Invoke();
        Recorder.Problem += why =>
        {
            Problem?.Invoke(RecordingPaused, why);
            Changed?.Invoke();
        };
        Whisper.ProblemChanged += () => Changed?.Invoke();
        Whisper.Heard += (l, lines) =>
        {
            Heard?.Invoke(l, lines);
            Changed?.Invoke();
        };
        Whisper.Finished += _ =>
        {
            Sender.Wake();
            Changed?.Invoke();
        };
        Sender.Changed += l =>
        {
            if (l.State == LectureState.Filed) Filed?.Invoke(l);
            Changed?.Invoke();
        };
    }

    /// <summary>Write a line in the app's log.</summary>
    public void Log(string line) => log(line);

    readonly Lock clientLock = new();
    ClientConfig? client;
    DateTime clientReadAt;

    /// <summary>client.toml, read once and kept until the file's own timestamp moves on (someone else wrote it, or
    /// <see cref="SaveClient"/> did): every tick doesn't need its own trip to disk.</summary>
    public ClientConfig Client()
    {
        string path = Path.Combine(Home, "client.toml");
        lock (clientLock)
        {
            DateTime mtime = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
            if (client is null || mtime != clientReadAt)
            {
                client = Configs.LoadClient(Home);
                clientReadAt = mtime;
            }
            return client;
        }
    }

    /// <summary>Write client.toml and keep it as what <see cref="Client"/> hands back, so the app's own save is never
    /// immediately re-read as if someone else had changed it.</summary>
    public void SaveClient(ClientConfig cc)
    {
        Configs.SaveClient(cc);
        lock (clientLock)
        {
            client = cc;
            clientReadAt = File.Exists(cc.ConfigPath) ? File.GetLastWriteTimeUtc(cc.ConfigPath) : DateTime.MinValue;
        }
    }

    // --- the microphone -------------------------------------------------------------------------------------------
    // The only way the app reaches the microphone: with a pretend one (a test, the self-test, STUDYSTASH_MIC_FILE) the
    // real one is never opened, asked for, or even asked about.

    /// <summary>STUDYSTASH_MIC_FILE: a WAV file played, round again, as the microphone; STUDYSTASH_MIC_SPEED (1 to
    /// 8) plays it that many times faster than real time.</summary>
    public static Func<IAudioSource>? MicFromEnvironment()
    {
        if (Environment.GetEnvironmentVariable("STUDYSTASH_MIC_FILE") is not { Length: > 0 } wav || !File.Exists(wav)) return null;
        int speed = MicSpeed(Environment.GetEnvironmentVariable("STUDYSTASH_MIC_SPEED"));
        return () => new FileMicrophone(wav, speed);
    }

    /// <summary>STUDYSTASH_MIC_SPEED's value as a speed: 1 (real time) to 8; anything else is 1.</summary>
    public static int MicSpeed(string? value) => int.TryParse(value, out int n) && n >= 1 ? Math.Min(n, 8) : 1;

    /// <summary>A pretend microphone stands in for the real one.</summary>
    public bool PretendMic => pretendMic is not null;

    /// <summary>Whether Study Stash may use the microphone (a pretend one always may).</summary>
    public MicAccess MicAccess() => mics.Access();

    /// <summary>Have the system ask the student (a Mac asks once, with its own prompt) and wait for the answer.
    /// Nothing is asked for a pretend microphone.</summary>
    public Task<MicAccess> AskMicAsync() => mics.AskAsync();

    /// <summary>The microphone to record from (and on Windows, if asked, what the computer plays too).</summary>
    public IAudioSource OpenMic() => pretendMic is { } pretend ? pretend() : Microphones.Open(Settings.ComputerAudio);

    /// <summary>Where the student turns the microphone on for Study Stash.</summary>
    public string MicSettingsUrl => Microphones.SettingsUrl;

    /// <summary>This computer can record what it plays, too (Windows).</summary>
    public bool CanRecordComputerAudio => Microphones.CanRecordComputerAudio;

    /// <summary>The library over its API, once one is set up.</summary>
    public RemoteLibrary? Remote()
    {
        var cc = Client();
        return cc.ServerUrl.Length > 0 ? new RemoteLibrary(cc.ServerUrl, cc.PoolKey) : null;
    }

    /// <summary>The transcription model: the one the environment names, else the one picked in setup or Settings, else
    /// the one for this computer (<see cref="Advice"/>).</summary>
    public WhisperModel Model => models.Model ?? WhisperModels.Find(Settings.Model) ?? Advice.Model;

    /// <summary>What this computer has. Asked once, on the thread pool, as the app starts; the first to want it before
    /// then waits the moment it takes.</summary>
    public HardwareProfile Hardware => hardware.GetAwaiter().GetResult();

    /// <summary>The model that keeps up with a lecture on this computer, and why.</summary>
    public ModelAdvice Advice => WhisperModels.Advise(Hardware);

    /// <summary>
    /// An install from before models were picked for the computer saved no model: it used large-v3 (or the compact
    /// turbo with little memory). That one is kept, so nothing changes under the student or downloads again by itself;
    /// <see cref="ModelSuggestionAsync"/> may suggest a lighter one, once. Kept only when a model (or part of one) is
    /// here: with nothing downloaded yet, the one for this computer is the one it gets.
    /// </summary>
    void KeepModelInUse()
    {
        if (!Settings.SetupDone || Settings.Model.Length > 0 || Settings.Role == AppRole.Library || ModelFromEnvironment) return;
        var had = WhisperModels.All.FirstOrDefault(m => WhisperModels.IsDownloaded(Home, m))
                  ?? WhisperModels.All.FirstOrDefault(m => WhisperModels.OnDisk(Home, m) > 0);
        if (had is null) return;
        Settings.Model = had.Id;
        try
        {
            Settings.Save(Home);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            log($"[model] couldn't save the model in use: {e.Message}");
        }
    }

    /// <summary>
    /// A lighter model to suggest, once: the student records with a model heavier than this computer keeps up with
    /// (large-v3 on a PC with no graphics card Whisper can use), and hasn't heard about it before. A bigger model than
    /// the compact one a computer starts on, but one it handles (large-v3 on Apple silicon), is the student's call: no
    /// word about it. Null otherwise, and always when the environment names the model. Waits for the hardware probe,
    /// never on the UI thread.
    /// </summary>
    public async Task<ModelAdvice?> ModelSuggestionAsync()
    {
        var hw = await hardware.ConfigureAwait(false);
        if (ModelFromEnvironment || !Settings.SetupDone || Settings.Role == AppRole.Library) return null;
        var heaviest = WhisperModels.Heaviest(hw);
        return WhisperModels.Heavier(Model, heaviest.Model) && Settings.ModelSuggested != heaviest.Model.Id ? heaviest : null;
    }

    /// <summary>The suggestion was made (or the student picked a model knowing the one for this computer): not again.</summary>
    public void ModelSuggestionMade(WhisperModel suggested) => Save(s => s.ModelSuggested = suggested.Id);

    /// <summary>Whisper waits for 20 to 30 seconds of speech and then takes its time over it, so a transcript a minute
    /// behind is normal. Three minutes behind is clearly more than that: the model can't keep up with this lecture.</summary>
    public const double BehindAfterSeconds = 180;

    /// <summary>What to tell the student when the lecture being recorded is being written down clearly slower than it's
    /// said (a title and what to know), or null while it keeps up, pauses, or waits for something else (the model's
    /// download, a Whisper that couldn't start).</summary>
    public (string Title, string Text)? FallingBehind()
    {
        if (Recorder.Current is not { State: LectureState.Recording } live || !ModelReady || WhisperProblem is not null) return null;
        return BehindWords(Recorder.Elapsed, live.TranscribedSeconds, Model, Advice);
    }

    /// <summary><see cref="FallingBehind"/>'s words for <paramref name="recorded"/> seconds recorded and
    /// <paramref name="written"/> written down with <paramref name="inUse"/>: a lighter model to switch to (the one for
    /// this computer when it's lighter, else the next lighter Whisper one), unless it's already the lightest. Parakeet
    /// is never the suggestion: it's a bigger download, and nothing says it keeps up where Whisper doesn't.</summary>
    public static (string Title, string Text)? BehindWords(double recorded, double written, WhisperModel inUse, ModelAdvice advice)
    {
        if (recorded - written < BehindAfterSeconds) return null;
        var lighter = WhisperModels.Heavier(inUse, advice.Model) ? advice.Model
            : WhisperModels.All.SkipWhile(m => m.Id != inUse.Id).Skip(1).FirstOrDefault(m => m.Id != WhisperModels.Tiny.Id && m.Engine == SpeechEngine.Whisper);
        string text = $"{inUse.Name} is slower than the lecture on this computer. Nothing is lost: it catches up after class.";
        // The notification's Settings button opens Settings → Recording, so the words needn't say where.
        if (lighter is not null) text += $" {lighter.Name} would keep up.";
        return ("The transcript is falling behind", text);
    }

    /// <summary>The environment names the model (tests, the self-test): the student's pick and the advice stand aside.</summary>
    public bool ModelFromEnvironment => models.Model is not null || models.File is not null;

    /// <summary>A model file the environment gives to use as it is (nothing downloads); null normally.</summary>
    public string? ModelFile => models.File;

    public bool ModelReady => ModelFile is not null || WhisperModels.IsDownloaded(Home, Model);

    /// <summary>What tells voices apart is on this computer.</summary>
    public bool SpeakerModelReady => WhisperModels.IsDownloaded(Home, WhisperModels.Speakers);

    /// <summary>
    /// Download what tells voices apart when the student wants it and it isn't here, once the transcription model is
    /// (there's nothing to label before it) and while no other download runs (a new one would stop it). Called when the
    /// setting is turned on, at start, and when another download ends.
    /// </summary>
    public void EnsureSpeakerModel()
    {
        if (!Settings.Speakers || Settings.Role == AppRole.Library || !ModelReady || SpeakerModelReady) return;
        lock (downloadLock)
            if (download is not null || disposed) return;
        _ = DownloadModelAsync(WhisperModels.Speakers);
    }

    ITranscriber LoadWhisper()
    {
        if (!ModelReady) throw new InvalidOperationException("The transcription model isn't downloaded yet.");
        if (ModelFile is null && Model.Engine == SpeechEngine.Parakeet)
        {
            if (!ParakeetLanguages.Knows(Settings.Language))
                throw new InvalidOperationException($"Parakeet doesn't read \"{Settings.Language}\": pick a Whisper model in Settings → Recording, or leave the language empty.");
            return new ParakeetTranscriber(WhisperModels.PathFor(Home, Model), Settings.Language);
        }
        return new WhisperTranscriber(ModelFile ?? WhisperModels.PathFor(Home, Model), Settings.Language);
    }

    public void Start()
    {
        running.Add(Task.Run(() => Whisper.RunAsync(stop.Token)));
        running.Add(Task.Run(() => Sender.RunAsync(stop.Token)));
        running.Add(Task.Run(WatchLibrary));
        StartCalendars();
        running.Add(Task.Run(() => Lectures.PruneAudio(Settings.KeepAudioDays, DateTimeOffset.Now)));
        watchdog = new Timer(_ => CheckRecorder(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        // A download that quitting (or a closed laptop) cut short picks up where it stopped. A library-only
        // computer never records, so it never needs the model.
        if (Settings.SetupDone && Settings.Role != AppRole.Library && !ModelReady) _ = DownloadModelAsync();
        else EnsureSpeakerModel();
        if (Settings.Role != AppRole.Laptop && Settings.SetupDone) _ = RefreshLocalLibraryAsync();
    }

    /// <summary>Take this as this computer's own library: the one <see cref="LocalLibrary"/> shows from now on, stopped
    /// in <see cref="Dispose"/>. Setup calls this the moment it starts one (before the app has even reached
    /// <see cref="Start"/> for a first-time "this computer" library), so that library is never left running past quit.
    /// Does nothing if one is already in charge.</summary>
    internal void UseLocalLibrary(LibraryService svc)
    {
        if (LocalLibrary is not null) return;
        LocalLibrary = svc;
        svc.Changed += () => Changed?.Invoke();
    }

    /// <summary>Start this computer's own library (Settings' Start button, after Stop or a problem): make one if there
    /// isn't one yet, otherwise just ask the one we have to try again (a no-op while it's already up).</summary>
    public Task RefreshLocalLibraryAsync()
    {
        if (LocalLibrary is null)
        {
            var svc = localLibrary?.Invoke() ?? new LibraryService(Home, Configs.Load(Home));
            UseLocalLibrary(svc);
        }
        return LocalLibrary!.StartAsync();
    }

    /// <summary>Every second, on the thread pool: the recorder looks at its microphone and the disk. One look at a
    /// time (reopening a microphone can take a moment).</summary>
    void CheckRecorder()
    {
        if (Interlocked.Exchange(ref checking, 1) == 1) return;
        try
        {
            Recorder.Check(DateTime.UtcNow);
        }
        catch (Exception e)
        {
            log($"[recorder] {e.Message}");
        }
        finally
        {
            Volatile.Write(ref checking, 0);
        }
    }

    async Task WatchLibrary()
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                await CheckLibraryAsync();
                if (Library != LibraryState.Connected && LocalLibrary is { State: LibraryServiceState.Elsewhere } local)
                {
                    await local.TakeOverIfGoneAsync();
                    if (local.State == LibraryServiceState.Running) await CheckLibraryAsync();
                }
            }
            catch (Exception e)
            {
                log($"[library] {e.Message}");
            }
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(Library == LibraryState.Connected ? 20 : 10), stop.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>Only one check talks to the library at a time: Setup, Settings' Connect and the background watch can
    /// all ask for one at once, and there's no sense in two racing.</summary>
    readonly SemaphoreSlim libraryCheck = new(1, 1);
    /// <summary>A dropped Tailscale link shouldn't leave the dropdown saying "connected" for minutes: the library's
    /// own HTTP client waits far longer than that, so a check gives up on its own after this.</summary>
    static readonly TimeSpan LibraryCheckTimeout = TimeSpan.FromSeconds(8);
    /// <summary>Ask the library how it is: whether it's reachable, its classes, and the lectures it deleted for good.</summary>
    public async Task CheckLibraryAsync()
    {
        await libraryCheck.WaitAsync();
        try
        {
            var before = Library;
            if (LocalLibrary is { State: LibraryServiceState.Starting })
            {
                // Our own library is coming up: "Can't reach it" would be alarming and wrong.
                Library = LibraryState.Starting;
                Changed?.Invoke();
                return;
            }
            if (Remote() is not { } lib)
            {
                Library = LibraryState.NotSetUp;
            }
            else
            {
                try
                {
                    try
                    {
                        Overview = await lib.OverviewAsync().WaitAsync(LibraryCheckTimeout);
                        OlderLibrary = false;
                    }
                    catch (LibraryRefusedException e) when (e.Status == 404)
                    {
                        // The Python engine's library: its classes from /api/health, in its order.
                        var cc = Client();
                        var health = await LibraryApi.CheckServerAsync(cc.ServerUrl, cc.PoolKey).WaitAsync(LibraryCheckTimeout);
                        int i = 0;
                        Overview = new JsonObject
                        {
                            ["name"] = health["pool_name"]?.DeepClone(),
                            ["classes"] = new JsonArray((health["classes"] as JsonArray ?? []).Select(n => (JsonNode?)new JsonObject { ["name"] = n?.DeepClone(), ["lectures"] = 0, ["color"] = i++ }).ToArray()),
                            ["unsorted"] = 0,
                            ["ask"] = false,
                        };
                        OlderLibrary = true;
                    }
                    Library = LibraryState.Connected;
                    // A class renamed in the library keeps its old name among its other names: lectures recorded here
                    // under the old name follow it, before anything deleted for good drops out.
                    var names = (Overview["classes"] as JsonArray ?? []).Select(c => c?["name"]?.GetValue<string>() ?? "").ToList();
                    foreach (var c in (Overview["classes"] as JsonArray ?? []).OfType<JsonObject>())
                        foreach (var alias in (c["aliases"] as JsonArray ?? []).Select(a => a is JsonValue v && v.TryGetValue(out string? t) ? t : null).OfType<string>())
                            if (c["name"] is JsonValue nv && nv.TryGetValue(out string? name) && name is not null && !names.Contains(alias)
                                && Lectures.All().Any(l => l.ClassName == alias || l.FiledClass == alias))
                                FollowRename(alias, name);
                    DropGone(Overview["gone"] as JsonArray);
                }
                catch (InvalidOperationException e) when (e.Message == "wrong password")
                {
                    Library = LibraryState.WrongPassword;
                }
                catch (InvalidOperationException)
                {
                    Library = LibraryState.Unreachable;
                }
                catch (LibraryRefusedException e) when (e.Status is 401 or 403)
                {
                    Library = LibraryState.WrongPassword;
                }
                catch (Exception e) when (e is HttpRequestException or TaskCanceledException or TimeoutException or LibraryRefusedException or JsonException)
                {
                    Library = LibraryState.Unreachable;
                }
            }
            // Lectures that waited for it go now: a wake alone would leave them waiting out their last try's wait (10 minutes).
            if (before != Library && Library == LibraryState.Connected) Sender.RetryNow();
            Changed?.Invoke();
        }
        finally
        {
            libraryCheck.Release();
        }
    }

    /// <summary>Lectures the library has deleted for good (their time in its trash is up): this laptop drops its own
    /// recording of them, the audio and what Whisper wrote, once the library had filed them.</summary>
    void DropGone(JsonArray? gone)
    {
        foreach (var n in gone ?? [])
        {
            if (n is not JsonValue v || !v.TryGetValue(out string? id) || Lectures.Get(id) is not { State: LectureState.Filed }) continue;
            Lectures.Delete(id);
            log($"[app] dropped the recording of {id}: it was deleted from the library");
        }
    }

    /// <summary>The library's classes, in its order: (name, color index, lectures).</summary>
    public List<(string Name, int Color, int Lectures)> Classes() =>
        (Overview?["classes"] as JsonArray ?? []).OfType<JsonObject>()
            .Select(c => (c["name"]?.GetValue<string>() ?? "", c["color"]?.GetValue<int>() ?? 0, c["lectures"]?.GetValue<int>() ?? 0)).ToList();

    public int ColorOf(string className) => Classes().FirstOrDefault(c => c.Name == className) is { Name.Length: > 0 } c ? c.Color : -1;

    /// <summary>"Library connected · Model ready", or what needs doing: pure, so a test needn't drive a real library
    /// or download to check the words.</summary>
    public static string StatusText(LibraryState library, bool localLibraryRunning, bool modelReady, DownloadProgress? downloading, bool records = true)
    {
        string lib = localLibraryRunning
            ? $"Library running on this {(OperatingSystem.IsMacOS() ? "Mac" : "PC")}"
            : library switch
        {
            LibraryState.Connected => "Library connected",
            LibraryState.Starting => "Starting your library…",
            LibraryState.Unreachable => "Can't reach your library",
            LibraryState.WrongPassword => "Library password changed",
            _ => "No library yet",
        };
        // A library-only computer never records, so its transcription model isn't worth a word.
        if (!records) return lib;
        string model = modelReady ? "Model ready" : downloading is { } d ? $"Model {Math.Round(d.Fraction * 100)}%" : "No transcription model";
        return $"{lib} · {model}";
    }

    /// <summary>"Library connected · Model ready", or what needs doing, and whether all is well.</summary>
    public (string Text, bool Good) Status()
    {
        bool records = Settings.Role != AppRole.Library;
        return (StatusText(Library, Settings.Role != AppRole.Laptop && LocalLibrary?.State == LibraryServiceState.Running, ModelReady, Downloading, records),
            Library == LibraryState.Connected && (ModelReady || !records));
    }

    /// <summary>Change the settings and write them to app.json. A full disk (or a folder it can't write) is said, not
    /// thrown: the change still holds until the app quits.</summary>
    public void Save(Action<AppSettings> change)
    {
        change(Settings);
        try
        {
            Settings.Save(Home);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            log($"[app] couldn't save the settings: {e.Message}");
            Problem?.Invoke("Your settings couldn't be saved", "The change lasts until Study Stash quits. Check this computer has free space.");
        }
        Changed?.Invoke();
    }

    /// <summary>A class was renamed in the library: the lectures recorded here under the old name follow it, so they
    /// file where it went.</summary>
    public void FollowRename(string from, string to)
    {
        foreach (var l in Lectures.All().Where(l => l.ClassName == from || l.FiledClass == from))
            Lectures.Update(l.Id, x =>
            {
                if (x.ClassName == from) x.ClassName = to;
                if (x.FiledClass == from) x.FiledClass = to;
            });
        log($"[app] {from} is now {to} in the library: the lectures here follow it");
    }

    // --- recording ------------------------------------------------------------------------------------------------

    /// <summary>
    /// Record's whole start: ask for the microphone if the system hasn't asked yet (and wait for the answer), then
    /// start. A microphone that isn't allowed, isn't there or won't start comes back as <see cref="MicTrouble"/>
    /// instead of a lecture; the model or the disk not being ready still throws, as <see cref="StartRecording"/> does.
    /// </summary>
    public async Task<RecordStart> RecordAsync(string className)
    {
        var access = MicAccess();
        if (access == Audio.MicAccess.NotAsked)
        {
            log("[app] asking for the microphone");
            access = await AskMicAsync();
            log($"[app] the microphone: {access}");
        }
        if (access is Audio.MicAccess.Denied or Audio.MicAccess.Restricted or Audio.MicAccess.NotAsked)
            return new RecordStart(null, MicTrouble.Denied());
        try
        {
            return new RecordStart(StartRecording(className), null);
        }
        catch (MicrophoneException e)
        {
            log($"[app] the microphone didn't start: {e.Message}");
            return new RecordStart(null, e.Trouble);
        }
    }

    public Lecture StartRecording(string className)
    {
        if (!ModelReady) throw new InvalidOperationException("Download the transcription model first (Settings → Recording).");
        var calendar = CalendarStart(className);
        var l = WithEvent(Recorder.Start(calendar.ClassName, Client().DisplayName), calendar);
        awake ??= new KeepAwake("Recording a lecture");
        Whisper.Wake();
        return l;
    }

    public void Pause() => Recorder.Pause();

    public void Resume() => Recorder.Resume();

    /// <summary>Try a lecture that failed again: back to Whisper, or on to the library.</summary>
    public void Retry(string id)
    {
        if (Whisper.Retry(id) is { State: LectureState.Sending }) Sender.Wake();
        Changed?.Invoke();
    }

    public Lecture? StopRecording()
    {
        var l = Recorder.Stop();
        awake?.Dispose();
        awake = null;
        Whisper.Wake();
        return l;
    }

    // --- the model --------------------------------------------------------------------------------------------------

    /// <summary>What the setup and Settings say while a dropped connection waits to be tried again.</summary>
    public const string DownloadStopped = "The download stopped (no internet?). It picks up where it left off.";

    /// <summary>How long a download that stopped (no internet) waits before trying again: 30 seconds, a minute, two,
    /// then every five minutes while the app runs.</summary>
    public static TimeSpan RetryAfter(int failures) => TimeSpan.FromSeconds(failures switch
    {
        0 => 30,
        1 => 60,
        2 => 120,
        _ => 300,
    });

    /// <summary>
    /// Download a model (the one for this computer unless another is given), or pick up where its download stopped.
    /// Another model while one downloads: that one stops (its .part stays) and this one starts. The same one while it
    /// waits to try again: it tries now (setup's Try again, Settings' Download). The task ends when the download does.
    /// </summary>
    public Task DownloadModelAsync(WhisperModel? model = null)
    {
        // A model file given to use as it is (the self-test's): nothing to download unless another is picked.
        if (model is null && ModelFile is not null) return Task.CompletedTask;
        model ??= Model;
        if (WhisperModels.IsDownloaded(Home, model)) return Task.CompletedTask;
        lock (downloadLock)
        {
            if (disposed) return Task.CompletedTask;
            if (download is not null && DownloadingModel?.Id == model.Id)
            {
                retryNow?.TrySetResult();
                return downloadTask;
            }
            download?.Cancel();
            var cts = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
            download = cts;
            DownloadingModel = model;
            DownloadProblem = null;
            Downloading = new DownloadProgress(WhisperModels.OnDisk(Home, model), model.Bytes, 0);
            // On the thread pool: picking up a download first reads the gigabytes already here.
            downloadTask = Task.Run(() => DownloadAsync(model, cts));
        }
        Changed?.Invoke();
        return downloadTask;
    }

    /// <summary>Stop the model download (another model, already here, was picked). What came stays for next time.</summary>
    public void StopDownload()
    {
        lock (downloadLock) download?.Cancel();
    }

    /// <summary>Throw the model away and download it again (Whisper couldn't start with it: it may be damaged).</summary>
    public Task RedownloadModel()
    {
        var model = Model;
        if (ModelFile is not null)
        {
            // The environment's own file isn't the app's to delete.
            log($"[model] {ModelFile} is given to use as it is: nothing to download again");
            return Task.CompletedTask;
        }
        lock (downloadLock)
        {
            if (download is not null && DownloadingModel?.Id == model.Id)
            {
                retryNow?.TrySetResult();
                return downloadTask;
            }
        }
        string path = WhisperModels.PathFor(Home, model);
        try
        {
            WhisperModels.Delete(Home, model);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            log($"[model] couldn't remove {path}: {e.Message}");
            DownloadProblem = $"The old {model.Name} couldn't be removed: {e.Message}";
            Changed?.Invoke();
            return Task.CompletedTask;
        }
        log($"[model] downloading {model.Name} again");
        return DownloadModelAsync(model);
    }

    /// <summary>
    /// Remove a model this computer no longer uses (Settings asks the student first), and whatever came of its
    /// download. Never the one in use or the one downloading. Returns why it couldn't, or null when it's gone.
    /// </summary>
    public string? RemoveModel(WhisperModel model)
    {
        if (model.Id == Model.Id) return $"{model.Name} is the one in use.";
        lock (downloadLock)
            if (download is not null && DownloadingModel?.Id == model.Id) return $"{model.Name} is downloading.";
        string path = WhisperModels.PathFor(Home, model);
        try
        {
            WhisperModels.Delete(Home, model);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            log($"[model] couldn't remove {path}: {e.Message}");
            return $"{model.Name} couldn't be removed: {e.Message}";
        }
        log($"[model] removed {model.Name}");
        Changed?.Invoke();
        return null;
    }

    async Task DownloadAsync(WhisperModel model, CancellationTokenSource cts)
    {
        int failures = 0;
        bool damagedBefore = false;
        try
        {
            while (true)
            {
                try
                {
                    await ModelDownload.RunAsync(Home, model, Progress(cts), cts.Token, http, models.UrlFor(model), mirror: models.Mirror);
                    Say(cts, null);
                    log($"[model] {model.Name} downloaded");
                    return;
                }
                catch (NotEnoughSpaceException e)
                {
                    // Only the student can make room: no trying again until they ask.
                    log($"[model] {e.Message}");
                    Say(cts, e.Message);
                    return;
                }
                catch (InvalidDataException e) when (!damagedBefore)
                {
                    damagedBefore = true;
                    log($"[model] {e.Message} Downloading it again.");
                }
                catch (InvalidDataException e)
                {
                    log($"[model] {e.Message}");
                    Say(cts, e.Message);
                    return;
                }
                catch (Exception e) when (e is HttpRequestException or IOException || (e is TaskCanceledException && !cts.IsCancellationRequested))
                {
                    var wait = RetryAfter(failures++);
                    log($"[model] {e.Message}: trying again in {wait.TotalSeconds:0} s");
                    await WaitToRetry(wait, cts);
                }
            }
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
        }
        catch (Exception e)
        {
            // Anything else (a folder it can't write): said, and tried again when the student asks.
            log($"[model] {e}");
            Say(cts, $"The download stopped: {e.Message}");
        }
        finally
        {
            lock (downloadLock)
            {
                if (download == cts)
                {
                    download = null;
                    retryNow = null;
                    Downloading = null;
                    DownloadingModel = null;
                }
                cts.Dispose();
            }
            Changed?.Invoke();
            Whisper.Wake();
            // The voice model follows the transcription model; its own end asks for nothing more (a full disk would loop).
            if (model.Id != WhisperModels.Speakers.Id) EnsureSpeakerModel();
        }
    }

    /// <summary>Progress straight to the windows, while this is still the download the app wants. Once bytes arrive
    /// again, a problem from an earlier try is over.</summary>
    IProgress<DownloadProgress> Progress(CancellationTokenSource cts)
    {
        long? from = null;
        return new Reporter(p =>
        {
            lock (downloadLock)
            {
                if (download != cts) return;
                from ??= p.Done;
                Downloading = p;
                if (p.Done > from) DownloadProblem = null;
            }
            Changed?.Invoke();
        });
    }

    /// <summary>Say why this download stopped (or null: it's fine), unless another has taken its place.</summary>
    void Say(CancellationTokenSource cts, string? problem)
    {
        lock (downloadLock)
        {
            if (download != cts) return;
            DownloadProblem = problem;
        }
        Changed?.Invoke();
    }

    /// <summary>Say the download stopped, then wait to try again: until the wait's over, or the student says now.</summary>
    async Task WaitToRetry(TimeSpan wait, CancellationTokenSource cts)
    {
        var now = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        // Together, so a Try again the moment the words show is never missed.
        lock (downloadLock)
        {
            if (download == cts)
            {
                retryNow = now;
                DownloadProblem = DownloadStopped;
            }
        }
        Changed?.Invoke();
        using (var waiting = CancellationTokenSource.CreateLinkedTokenSource(cts.Token))
        {
            await Task.WhenAny(Task.Delay(wait, waiting.Token), now.Task);
            await waiting.CancelAsync();
        }
        cts.Token.ThrowIfCancellationRequested();
        lock (downloadLock)
            if (retryNow == now) retryNow = null;
    }

    sealed class Reporter(Action<DownloadProgress> report) : IProgress<DownloadProgress>
    {
        public void Report(DownloadProgress value) => report(value);
    }

    /// <summary>Stop: the lecture being recorded is saved, and Whisper, the sender and the library check get up to 3
    /// seconds to finish what they're doing.</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        watchdog?.Dispose();
        try
        {
            if (Recorder.Current is not null) StopRecording();
        }
        catch (Exception e) when (e is IOException or InvalidDataException or InvalidOperationException or UnauthorizedAccessException or AggregateException)
        {
            log($"[app] couldn't finish the recording: {e.Message}");
        }
        stop.Cancel();
        Task downloading;
        lock (downloadLock)
        {
            download?.Cancel();
            downloading = downloadTask;
        }
        awake?.Dispose();
        awake = null;
        if (LocalLibrary is { } lib)
        {
            // A clean stop (SIGTERM, then a moment to shut its database down) if it manages one in time; otherwise
            // Dispose's hard kill so quitting is never held up by a library that won't go. On the thread pool: awaits
            // inside StopAsync must not need this (UI) thread's own message loop to go on.
            try { Task.Run(lib.StopAsync).Wait(TimeSpan.FromSeconds(3)); }
            catch (AggregateException e) { log($"[library] {e.InnerException?.Message}"); }
            lib.Dispose();
        }
        try
        {
            if (!Task.WaitAll([.. running, downloading], TimeSpan.FromSeconds(3))) log("[app] still busy after 3 seconds: quitting anyway");
        }
        catch (AggregateException e)
        {
            log($"[app] {e.InnerException?.Message}");
        }
    }
}
