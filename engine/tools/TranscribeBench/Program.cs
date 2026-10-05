// TranscribeBench: what writing a lecture down costs on this computer, live or after class.
//
// It records a slice of a real lecture through Study Stash's own Recorder (the WAV played as the microphone, in real
// time unless --speed says otherwise) while Study Stash's own TranscriptionWorker writes it down with a real model,
// as the app does: live (as it records, Settings → Recording's "as you record") or after class (nothing while it
// records, then all of it once it stops). Nothing is written under ~/.study-stash: the recording and the model are
// only read, and the lecture is kept in a temporary folder of its own.
//
//   dotnet build -c Release engine/tools/TranscribeBench
//   dotnet engine/tools/TranscribeBench/bin/Release/net10.0/TranscribeBench.dll <lecture.wav> \
//       --model large-v3-turbo-q5|large-v3|parakeet-v3|small|… --mode live|after|whole \
//       [--from 600] [--length 720] [--speed 1] [--models ~/.study-stash/models] [--out bench-results] [--cpu] [--live-words]
//   dotnet …/TranscribeBench.dll compare <reference.txt> <other.txt>…   (words different, as a share of the reference's)
//
// For each phase (while it records; after it stops, until all of it is written down) it writes, in <out>/<label>.json:
//   wall time; CPU time (user + system); the CPU energy macOS bills to the process (proc_pid_rusage's ri_energy_nj:
//   the processor's only, an estimate from its own power model); GPU time (this process's Metal time, from ioreg's
//   AppUsage, Apple silicon only); peak memory (phys_footprint, which counts Metal's buffers, so a model on the GPU
//   too); wakeups; and, over a phase of a few minutes or more, what the whole computer drew (the battery gauge's
//   AccumulatedSystemLoad: the screen and every other app too, so only comparable between runs on a quiet computer).
//   None of it needs sudo. The transcript goes to <label>.txt, the worker's log (each piece and how
//   long the model took over it) to <label>.log.
// --mode whole sends the whole slice to the model in one go instead (no worker, no recorder): an experiment for after
// class, where nothing has to keep up. --cpu keeps Whisper off the GPU, as on a computer without one (a Mac's
// processor is quicker than most such PCs', so read it as the best such a PC would do).
// --live-words (live mode) also runs the recorder's live words (Cactus Whistle hearing the newest sound every second,
// as the app does), so the CPU and energy while it records include them; first_words_s is when the recorder first had
// words to show (seconds after recording started) and first_words_late_s how long after they were said, for the
// transcript's own pieces and, with --live-words, for the live words.
// What isn't measured: the GPU's energy (only its time), the display, and the microphone itself (a file stands in).

using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using StudyStash.Audio;
using StudyStash.Core;

if (args.Length > 0 && args[0] == "compare") return Compare.Run(args[1..]);
if (args.Length > 1 && args[0] == "table") return Table.Run(args[1]);
return await Bench.RunAsync(args);

/// <summary>Every run in a folder as one Markdown table: during the lecture, the wait after it, and all of it.</summary>
static class Table
{
    public static int Run(string dir)
    {
        Console.WriteLine("| model | mode | lecture | during: CPU s | during: CPU J | during: GPU s | during: memory | during: whole Mac W | wait after stop | after: CPU J | after: GPU s | peak memory | all: CPU J | all: GPU s | words |");
        Console.WriteLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|");
        foreach (string f in Directory.EnumerateFiles(dir, "*.json").Order(StringComparer.Ordinal))
        {
            var r = JsonNode.Parse(File.ReadAllText(f))!.AsObject();
            JsonNode? during = r["recording_phase"], after = r["after_stop"], all = r["total"] ?? r["after_stop"];
            string D(JsonNode? n, string key, string unit = "") => n?[key] is { } v ? $"{v}{unit}" : "–";
            Console.WriteLine($"| {r["model"]} | {r["mode"]} | {TimedText.Clock((double)r["length_s"]!)} | {D(during, "cpu_s")} | {D(during, "cpu_energy_j")} | {D(during, "gpu_s")} | {D(during, "peak_footprint_mb", " MB")} | {D(during, "system_power_w")} | {D(after, "wall_s", " s")} | {D(after, "cpu_energy_j")} | {D(after, "gpu_s")} | {D(all, "peak_footprint_mb", " MB")} | {D(all, "cpu_energy_j")} | {D(all, "gpu_s")} | {r["words"]} |");
        }
        return 0;
    }
}

sealed record Options(string Wav, string Model, string Mode, double From, double Length, int Speed, string Models, string Out, string Label, bool Cpu, bool LiveWords)
{
    public static Options Parse(string[] args)
    {
        string? wav = null;
        var named = new Dictionary<string, string>();
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] is "--cpu" or "--live-words") named[args[i][2..]] = "1";
            else if (args[i].StartsWith("--", StringComparison.Ordinal) && i + 1 < args.Length) named[args[i][2..]] = args[++i];
            else wav = args[i];
        }
        if (wav is null) throw new ArgumentException("Give a lecture's WAV (16 kHz mono, as Study Stash records).");
        string model = named.GetValueOrDefault("model", WhisperModels.LargeV3TurboSmall.Id);
        string mode = named.GetValueOrDefault("mode", "live");
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return new Options(wav, model, mode,
            double.Parse(named.GetValueOrDefault("from", "600"), CultureInfo.InvariantCulture),
            double.Parse(named.GetValueOrDefault("length", "720"), CultureInfo.InvariantCulture),
            int.Parse(named.GetValueOrDefault("speed", "1"), CultureInfo.InvariantCulture),
            named.GetValueOrDefault("models", Path.Combine(home, ".study-stash", "models")),
            named.GetValueOrDefault("out", "bench-results"),
            named.GetValueOrDefault("label", $"{model}-{mode}{(named.ContainsKey("cpu") ? "-cpu" : "")}{(named.ContainsKey("live-words") ? "-livewords" : "")}"),
            named.ContainsKey("cpu"), named.ContainsKey("live-words"));
    }
}

/// <summary>What this process has used so far: CPU, the energy macOS bills it, GPU time, memory, wakeups.</summary>
sealed record Reading(double Wall, double CpuSeconds, double EnergyJoules, double GpuSeconds, long Footprint, long Wakeups,
    double SystemLoadSum, double SystemLoadCount)
{
    public JsonObject Since(Reading start, long peakFootprint)
    {
        double wall = Wall - start.Wall, cpu = CpuSeconds - start.CpuSeconds, energy = EnergyJoules - start.EnergyJoules, gpu = GpuSeconds - start.GpuSeconds;
        return new JsonObject
        {
            ["wall_s"] = Math.Round(wall, 1),
            ["cpu_s"] = Math.Round(cpu, 1),
            ["cpu_cores_busy"] = Math.Round(wall > 0 ? cpu / wall : 0, 3),
            ["cpu_energy_j"] = Math.Round(energy, 1),
            ["cpu_power_w"] = Math.Round(wall > 0 ? energy / wall : 0, 3),
            ["gpu_s"] = Math.Round(gpu, 1),
            ["gpu_busy"] = Math.Round(wall > 0 ? gpu / wall : 0, 3),
            ["wakeups_per_s"] = Math.Round(wall > 0 ? (Wakeups - start.Wakeups) / wall : 0, 1),
            ["peak_footprint_mb"] = peakFootprint / (1 << 20),
            // The whole computer's draw (screen and every other app too), which the battery's gauge adds up about once a
            // minute: only worth reading over a few minutes.
            ["system_power_w"] = SystemLoadCount - start.SystemLoadCount >= 120
                ? Math.Round((SystemLoadSum - start.SystemLoadSum) / (SystemLoadCount - start.SystemLoadCount) / 1000, 2) : null,
        };
    }
}

static class Meter
{
    static readonly Stopwatch Clock = Stopwatch.StartNew();
    static readonly int Pid = Environment.ProcessId;

    [DllImport("/usr/lib/libSystem.dylib")]
    static extern int proc_pid_rusage(int pid, int flavor, byte[] buffer);

    /// <summary>rusage_info_v6 (sys/resource.h): 16 bytes of UUID, then 56 counters.</summary>
    static ulong[] Rusage()
    {
        var buf = new byte[16 + 56 * 8];
        if (proc_pid_rusage(Pid, 6, buf) != 0) return new ulong[56];
        var r = new ulong[56];
        for (int i = 0; i < 56; i++) r[i] = BitConverter.ToUInt64(buf, 16 + i * 8);
        return r;
    }

    const int IdleWakeups = 2, InterruptWakeups = 3, PhysFootprint = 7, LifetimeMax = 28, EnergyNj = 40;

    public static long Footprint() => (long)Rusage()[PhysFootprint];
    public static long LifetimeMaxFootprint() => (long)Rusage()[LifetimeMax];

    public static Reading Read()
    {
        var r = Rusage();
        using var me = Process.GetCurrentProcess();
        var (load, count) = SystemLoad();
        return new Reading(Clock.Elapsed.TotalSeconds, me.TotalProcessorTime.TotalSeconds, r[EnergyNj] / 1e9, GpuSeconds(),
            (long)r[PhysFootprint], (long)(r[IdleWakeups] + r[InterruptWakeups]), load, count);
    }

    /// <summary>The battery gauge's running sum of what the whole computer draws (mW, about one sample a second, added
    /// up about once a minute), and how many samples: a Mac laptop's, plugged in or not; zeros elsewhere.</summary>
    static (double Sum, double Count) SystemLoad()
    {
        string text = Run("ioreg", "-r -c AppleSmartBattery -w 0");
        var sum = Regex.Match(text, "\"AccumulatedSystemLoad\"=(\\d+)");
        var count = Regex.Match(text, "\"SystemLoadAccumulatorCount\"=(\\d+)");
        return sum.Success && count.Success
            ? (double.Parse(sum.Groups[1].Value, CultureInfo.InvariantCulture), double.Parse(count.Groups[1].Value, CultureInfo.InvariantCulture))
            : (0, 0);
    }

    static string Run(string file, string arguments)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo(file, arguments) { RedirectStandardOutput = true })!;
            string text = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            return text;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return "";
        }
    }

    /// <summary>This process's Metal time so far, as Activity Monitor's GPU Time reads it (Apple silicon's GPU).</summary>
    static double GpuSeconds()
    {
        long ns = 0;
        foreach (string block in Run("ioreg", "-r -c AGXDeviceUserClient -l -w 0").Split("+-o "))
        {
            if (!block.Contains($"\"IOUserClientCreator\" = \"pid {Pid},", StringComparison.Ordinal)) continue;
            foreach (Match m in Regex.Matches(block, "\"accumulatedGPUTime\"=(\\d+)")) ns += long.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        }
        return ns / 1e9;
    }
}

static class Bench
{
    public static async Task<int> RunAsync(string[] args)
    {
        var o = Options.Parse(args);
        Directory.CreateDirectory(o.Out);
        var model = WhisperModels.Find(o.Model) ?? throw new ArgumentException($"No model called {o.Model}");
        string modelPath = model.Bundled ? WhisperModels.PathFor("", model) : Path.Combine(o.Models, model.File);
        string work = Directory.CreateTempSubdirectory("transcribe-bench-").FullName;
        using var logFile = new StreamWriter(Path.Combine(o.Out, o.Label + ".log")) { AutoFlush = true };
        var started = Stopwatch.StartNew();
        void Log(string line)
        {
            string stamped = $"{started.Elapsed.TotalSeconds,7:0.0} {line}";
            lock (logFile) logFile.WriteLine(stamped);
            Console.WriteLine(stamped);
        }
        string backend = "";
        ITranscriber Load()
        {
            ITranscriber t = model.Engine switch
            {
                SpeechEngine.Parakeet => new ParakeetTranscriber(modelPath),
                SpeechEngine.Whistle => new WhistleTranscriber(modelPath),
                _ => new WhisperTranscriber(modelPath, gpu: !o.Cpu),
            };
            backend = t switch { ParakeetTranscriber p => p.Backend, WhistleTranscriber w => w.Backend, _ => ((WhisperTranscriber)t).Backend };
            Log($"[bench] {model.Name} loaded: {backend}");
            return t;
        }
        try
        {
            string slice = Path.Combine(work, "slice.wav");
            var samples = Sound.ReadWav(o.Wav, (long)(o.From * Sound.Rate), (long)(o.Length * Sound.Rate));
            using (var w = new WavWriter(slice)) w.Write(samples);
            double length = samples.Length / (double)Sound.Rate;
            Log($"[bench] {o.Label}: {TimedText.Clock(length)} of {Path.GetFileName(o.Wav)} from {TimedText.Clock(o.From)}, {model.Name}, {o.Mode}, played at {o.Speed}×");

            var result = new JsonObject
            {
                ["label"] = o.Label, ["model"] = model.Id, ["mode"] = o.Mode + (o.Cpu ? ", CPU only" : ""), ["speed"] = o.Speed,
                ["recording"] = Path.GetFileName(o.Wav), ["from_s"] = o.From, ["length_s"] = Math.Round(length, 1),
                ["machine"] = $"{RuntimeInformation.OSDescription}, {RuntimeInformation.ProcessArchitecture}, {Environment.ProcessorCount} threads",
            };
            string transcript;
            if (o.Mode == "whole")
            {
                var before = Meter.Read();
                using var t = Load();
                var heard = await t.TranscribeAsync(samples, "", "", default);
                var after = Meter.Read();
                result["after_stop"] = after.Since(before, Meter.LifetimeMaxFootprint());
                transcript = TimedText.Format([.. heard.Segments.Select(s => new Spoken(s.Start, s.End, TimedText.Tidy(s.Text))).Where(s => s.Text.Length > 0)]);
            }
            else
            {
                var store = new LectureStore(Path.Combine(work, "home"));
                using var recorder = new Recorder(store, () => new FileMicrophone(slice, o.Speed), log: Log);
                var worker = new TranscriptionWorker(store, Load, () => recorder.Current, Log);
                using var stop = new CancellationTokenSource();
                var running = Task.Run(() => worker.RunAsync(stop.Token));
                // When the recorder first had words to show, and how long after they were said.
                var clock = Stopwatch.StartNew();
                (double At, double Late)? firstPiece = null, firstLive = null;
                worker.Heard += (_, lines) => firstPiece ??= (clock.Elapsed.TotalSeconds, clock.Elapsed.TotalSeconds * o.Speed - lines[0].Start);
                LiveCaptioner? captions = null;
                Thread? live = null;
                if (o.LiveWords)
                {
                    var hearer = new WhistleTranscriber(WhisperModels.PathFor("", WhisperModels.Whistle));
                    captions = new LiveCaptioner(() => recorder.Current, recorder.Recent, _ => (hearer, "en"), Log) { Every = TimeSpan.FromSeconds(1.0 / o.Speed) };
                    captions.Changed += l =>
                    {
                        if (firstLive is null && captions.Words(l.Id) is { Count: > 0 } w)
                            firstLive = (clock.Elapsed.TotalSeconds, clock.Elapsed.TotalSeconds * o.Speed - w[0].Start);
                    };
                    live = new Thread(() => captions.Run(stop.Token)) { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = "live words" };
                }

                var start = Meter.Read();
                long peak = 0;
                clock.Restart();
                var lecture = recorder.Start("Bench", afterClass: o.Mode == "after");
                live?.Start();
                worker.Wake();
                while (recorder.Elapsed < length - 0.05)
                {
                    await Task.Delay(250);
                    peak = Math.Max(peak, Meter.Footprint());
                }
                var stopped = Meter.Read();
                result["recording_phase"] = stopped.Since(start, Math.Max(peak, stopped.Footprint));
                result["first_words_s"] = firstPiece is { } fp ? Math.Round(fp.At, 1) : null;
                result["first_words_late_s"] = firstPiece is { } fp2 ? Math.Round(fp2.Late, 1) : null;
                result["first_live_words_s"] = firstLive is { } fl ? Math.Round(fl.At, 1) : null;
                result["first_live_words_late_s"] = firstLive is { } fl2 ? Math.Round(fl2.Late, 1) : null;
                var l = recorder.Stop() ?? throw new InvalidOperationException("The recording wasn't kept.");
                worker.Wake();
                peak = 0;
                while (store.Get(l.Id) is { State: LectureState.Transcribing })
                {
                    await Task.Delay(100);
                    peak = Math.Max(peak, Meter.Footprint());
                }
                var done = Meter.Read();
                result["after_stop"] = done.Since(stopped, Math.Max(peak, done.Footprint));
                result["total"] = done.Since(start, Meter.LifetimeMaxFootprint());
                await stop.CancelAsync();
                await running;
                var written = store.Get(l.Id)!;
                result["state"] = written.State.ToString();
                result["error"] = written.Error;
                transcript = written.Transcript();
            }
            result["backend"] = backend;
            result["words"] = Compare.Words(transcript).Count;
            File.WriteAllText(Path.Combine(o.Out, o.Label + ".txt"), transcript);
            File.WriteAllText(Path.Combine(o.Out, o.Label + ".json"), result.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine(result.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }
        finally
        {
            Directory.Delete(work, recursive: true);
        }
    }
}

/// <summary>How different two transcripts' words are: the edits (a word changed, missed or added) from the reference
/// to the other, as a share of the reference's words. Times, speakers, case and punctuation don't count.</summary>
static class Compare
{
    public static int Run(string[] files)
    {
        var reference = Words(File.ReadAllText(files[0]));
        Console.WriteLine($"{Path.GetFileName(files[0])}: {reference.Count} words (the reference)");
        foreach (string f in files[1..])
        {
            var other = Words(File.ReadAllText(f));
            Console.WriteLine($"{Path.GetFileName(f)}: {other.Count} words, {Distance(reference, other) / (double)Math.Max(1, reference.Count):0.0%} different");
        }
        return 0;
    }

    public static List<string> Words(string transcript)
    {
        var words = new List<string>();
        foreach (string raw in transcript.Split('\n'))
        {
            string line = Regex.Replace(raw, @"^\s*\[[\d:]+\]\s*", "");
            line = Regex.Replace(line, @"^Speaker \d+:\s*", "");
            foreach (string w in Regex.Split(line.ToLowerInvariant(), @"[^\p{L}\p{N}']+"))
                if (w.Trim('\'') is { Length: > 0 } word) words.Add(word);
        }
        return words;
    }

    static int Distance(List<string> a, List<string> b)
    {
        var prev = new int[b.Count + 1];
        var cur = new int[b.Count + 1];
        for (int j = 0; j <= b.Count; j++) prev[j] = j;
        for (int i = 1; i <= a.Count; i++)
        {
            cur[0] = i;
            for (int j = 1; j <= b.Count; j++)
                cur[j] = Math.Min(Math.Min(prev[j] + 1, cur[j - 1] + 1), prev[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            (prev, cur) = (cur, prev);
        }
        return prev[b.Count];
    }
}
