using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace StudyStash.Core.Study;

/// <summary>One line of a listening script: who says it ("A", the one who explains; "B", the one who asks) and what.</summary>
public sealed record ScriptLine(string Who, string Text);

/// <summary>A lecture to listen to, as the library has it: whether it's being made, ready or failed, its script, how
/// long it is, and when it was made.</summary>
public sealed record ListenInfo(string Lecture, string State)
{
    public const string None = "none", Making = "making", Ready = "ready", Failed = "failed";
    public List<ScriptLine> Script { get; init; } = [];
    public double Seconds { get; init; }
    public string Error { get; init; } = "";
    public DateTimeOffset? Made { get; init; }
    /// <summary>Whether this computer can speak at all (no voices on a Linux without espeak).</summary>
    public bool CanSpeak { get; init; } = true;
}

/// <summary>A computer's own voices, turning a line into a WAV file (16-bit, mono, <see cref="Wav.Rate"/> Hz).</summary>
public interface ISpeech
{
    /// <summary>Whether there are voices to speak with, and why not.</summary>
    (bool Ok, string Why) Ready();
    /// <summary>Says <paramref name="text"/> in the voice for <paramref name="who"/> ("A" or "B") into <paramref name="wav"/>.</summary>
    Task SayAsync(string text, string who, string wav, CancellationToken ct);
}

/// <summary>
/// Listen: a lecture as a short conversation to listen to (on a walk, on the bus), two voices going over what was
/// taught, the way NotebookLM's audio overviews do. The library's AI writes the script from the notes and what the
/// lecturer flagged for the exam; the computer's own voices read it (say on a Mac, Windows' speech, espeak on Linux),
/// so nothing about it leaves the computer but the script's request to the AI. Kept in the library's listen folder,
/// a WAV and its script for each lecture.
/// </summary>
public sealed partial class Listen(string home, ISpeech? speech = null)
{
    public ISpeech Speech { get; } = speech ?? Voices.ForThisComputer();
    string Dir => Path.Combine(home, "listen");
    public string AudioPath(string lecture) => Path.Combine(Dir, Safe(lecture) + ".wav");
    string InfoPath(string lecture) => Path.Combine(Dir, Safe(lecture) + ".json");
    static string Safe(string id) => Regex.Replace(id, @"[^A-Za-z0-9_-]", "_");

    readonly Dictionary<string, Task> making = [];

    public ListenInfo Info(string lecture)
    {
        bool canSpeak = Speech.Ready().Ok;
        lock (making)
            if (making.TryGetValue(lecture, out var job) && !job.IsCompleted) return new ListenInfo(lecture, ListenInfo.Making) { CanSpeak = canSpeak };
        try
        {
            if (SharedFile.Read(InfoPath(lecture)) is { } text && JsonSerializer.Deserialize<ListenInfo>(text, Review.Options) is { } info)
                return info with { CanSpeak = canSpeak, State = info.State == ListenInfo.Ready && !File.Exists(AudioPath(lecture)) ? ListenInfo.None : info.State };
        }
        catch (JsonException)
        {
        }
        return new ListenInfo(lecture, ListenInfo.None) { CanSpeak = canSpeak };
    }

    void Keep(ListenInfo info)
    {
        Directory.CreateDirectory(Dir);
        SharedFile.Write(InfoPath(info.Lecture), JsonSerializer.Serialize(info with { CanSpeak = true }, Review.Options) + "\n");
    }

    /// <summary>Starts making a lecture's listen in the background (the script, then the voices); one at a time a
    /// lecture. What it comes to is kept for <see cref="Info"/>.</summary>
    public ListenInfo Start(StudyLecture lecture, StudyMaterial.Planner plan, DateTimeOffset now, Action<string>? log = null)
    {
        lock (making)
        {
            if (making.TryGetValue(lecture.Id, out var running) && !running.IsCompleted) return new ListenInfo(lecture.Id, ListenInfo.Making);
            making[lecture.Id] = Task.Run(async () =>
            {
                try
                {
                    await MakeAsync(lecture, plan, now, CancellationToken.None);
                }
                catch (Exception e) when (e is InvalidOperationException or InvalidDataException or HttpRequestException or TimeoutException
                                               or JsonException or IOException or System.ComponentModel.Win32Exception)
                {
                    log?.Invoke($"[listen] {lecture.Id}: {e.Message}");
                    Keep(new ListenInfo(lecture.Id, ListenInfo.Failed) { Error = e.Message, Made = now });
                }
            });
        }
        return new ListenInfo(lecture.Id, ListenInfo.Making);
    }

    /// <summary>Waits for a lecture's listen being made, if one is (tests, and the CLI).</summary>
    public Task Finished(string lecture)
    {
        lock (making) return making.TryGetValue(lecture, out var job) ? job : Task.CompletedTask;
    }

    /// <summary>The script, then each line spoken and the lines joined with a breath between them.</summary>
    public async Task<ListenInfo> MakeAsync(StudyLecture lecture, StudyMaterial.Planner plan, DateTimeOffset now, CancellationToken ct)
    {
        var (ok, why) = Speech.Ready();
        if (!ok) throw new InvalidOperationException(why);
        var script = ReadScript(await plan(ScriptPrompt(lecture), ScriptSchema));
        if (script.Count == 0) throw new InvalidDataException("the AI's script had no lines");
        Directory.CreateDirectory(Dir);
        string parts = Path.Combine(Dir, ".parts-" + Safe(lecture.Id) + "-" + Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(parts);
        try
        {
            var files = new List<string>();
            for (int i = 0; i < script.Count; i++)
            {
                string wav = Path.Combine(parts, $"{i:000}.wav");
                await Speech.SayAsync(script[i].Text, script[i].Who, wav, ct);
                files.Add(wav);
            }
            string audio = AudioPath(lecture.Id), fresh = audio + ".new";
            double seconds = Wav.Join(files, fresh, pauseSeconds: 0.35);
            File.Move(fresh, audio, overwrite: true);
            var info = new ListenInfo(lecture.Id, ListenInfo.Ready) { Script = script, Seconds = seconds, Made = now };
            Keep(info);
            return info;
        }
        finally
        {
            try
            {
                Directory.Delete(parts, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    public bool Delete(string lecture)
    {
        bool any = false;
        foreach (string f in new[] { AudioPath(lecture), InfoPath(lecture) })
            if (File.Exists(f))
            {
                File.Delete(f);
                any = true;
            }
        return any;
    }

    public static readonly JsonObject ScriptSchema = JsonNode.Parse("""
        {"type":"object","properties":{"lines":{"type":"array","items":{"type":"object","properties":{
          "who":{"type":"string","enum":["A","B"]},"text":{"type":"string"}},"required":["who","text"]}}},"required":["lines"]}
        """)!.AsObject();

    /// <summary>What the AI is asked for a listening script.</summary>
    public static string ScriptPrompt(StudyLecture l) =>
        "Write a script for a short audio overview of this lecture, about five minutes read aloud (650 to 850 words), for a " +
        "student to listen to on a walk. Two people talk it through: A knows the material and explains it; B is a sharp " +
        "classmate who asks the questions a student would, sums up in their own words, and now and then gets something " +
        "slightly wrong for A to put right. Start with what the lecture was about in a sentence, go through its main ideas " +
        "in the order taught with an example for each, spend longest on what the lecturer said would be on the exam, and " +
        "end with the three things to remember. It's heard, not read: short sentences, no lists, no headings, no symbols " +
        "or formulas written out in symbols (say them in words), no \"as you can see\". Each line is one turn, two to four " +
        "sentences at most.\n\n" + l.ForAi(20000);

    /// <summary>The script from the AI's answer: its lines with words, each A or B (anything else is A).</summary>
    public static List<ScriptLine> ReadScript(string json)
    {
        var lines = new List<ScriptLine>();
        foreach (var line in (JsonNode.Parse(json)?["lines"] as JsonArray ?? []).OfType<JsonObject>())
        {
            string text = Spoken(StudyMaterial.Text(line["text"]));
            if (text.Length == 0) continue;
            lines.Add(new ScriptLine(StudyMaterial.Text(line["who"]).ToUpperInvariant() == "B" ? "B" : "A", text));
        }
        return lines;
    }

    /// <summary>A line as it's said: no Markdown marks, no stage directions in brackets.</summary>
    public static string Spoken(string text)
    {
        text = Regex.Replace(text, @"\[[^\]]*\]|\*[^*]+\*(?=\s|$)", m => m.Value.StartsWith('[') ? "" : m.Value.Trim('*'));
        text = Regex.Replace(text, @"[*_#`>]+", "");
        return Regex.Replace(text, @"\s+", " ").Trim();
    }
}

/// <summary>WAV files as Listen writes them: 16-bit PCM, mono, one rate; read, joined and measured.</summary>
public static class Wav
{
    public const int Rate = 22050;

    /// <summary>The samples and format of a PCM WAV: (channels, rate, bits, data).</summary>
    public static (int Channels, int Rate, int Bits, byte[] Data) Read(string path)
    {
        using var r = new BinaryReader(File.OpenRead(path));
        if (Encoding.ASCII.GetString(r.ReadBytes(4)) != "RIFF") throw new InvalidDataException($"{Path.GetFileName(path)} isn't a WAV file");
        r.ReadInt32();
        if (Encoding.ASCII.GetString(r.ReadBytes(4)) != "WAVE") throw new InvalidDataException($"{Path.GetFileName(path)} isn't a WAV file");
        int channels = 0, rate = 0, bits = 0;
        byte[]? data = null;
        while (r.BaseStream.Position + 8 <= r.BaseStream.Length)
        {
            string id = Encoding.ASCII.GetString(r.ReadBytes(4));
            int size = r.ReadInt32();
            long next = r.BaseStream.Position + size + (size & 1);
            if (id == "fmt ")
            {
                int format = r.ReadInt16();
                channels = r.ReadInt16();
                rate = r.ReadInt32();
                r.ReadInt32();
                r.ReadInt16();
                bits = r.ReadInt16();
                if (format is not 1 and not -2) throw new InvalidDataException($"{Path.GetFileName(path)} isn't PCM");
            }
            else if (id == "data")
            {
                // Some writers leave the size at its maximum while they stream; take what's there.
                int take = (int)Math.Min(size < 0 ? int.MaxValue : size, r.BaseStream.Length - r.BaseStream.Position);
                data = r.ReadBytes(take);
            }
            if (next > r.BaseStream.Length) break;
            r.BaseStream.Position = next;
        }
        if (data is null || channels == 0) throw new InvalidDataException($"{Path.GetFileName(path)} has no sound");
        return (channels, rate, bits, data);
    }

    /// <summary>Writes 16-bit mono PCM at <paramref name="rate"/>.</summary>
    public static void Write(string path, ReadOnlySpan<short> samples, int rate = Rate)
    {
        using var w = new BinaryWriter(File.Create(path));
        int bytes = samples.Length * 2;
        w.Write("RIFF"u8);
        w.Write(36 + bytes);
        w.Write("WAVEfmt "u8);
        w.Write(16);
        w.Write((short)1);
        w.Write((short)1);
        w.Write(rate);
        w.Write(rate * 2);
        w.Write((short)2);
        w.Write((short)16);
        w.Write("data"u8);
        w.Write(bytes);
        foreach (short s in samples) w.Write(s);
    }

    /// <summary>16-bit mono samples at <see cref="Rate"/> from any 8/16-bit PCM WAV: channels mixed down, and the rate
    /// changed by straight-line steps between samples (good enough for a voice).</summary>
    public static short[] Mono(string path)
    {
        var (channels, rate, bits, data) = Read(path);
        int width = bits / 8;
        if (width is not 1 and not 2) throw new InvalidDataException($"{Path.GetFileName(path)} is {bits}-bit, not 8 or 16");
        int frames = data.Length / (width * channels);
        var mono = new float[frames];
        for (int f = 0; f < frames; f++)
        {
            float sum = 0;
            for (int c = 0; c < channels; c++)
            {
                int at = (f * channels + c) * width;
                sum += width == 2 ? BitConverter.ToInt16(data, at) : (data[at] - 128) * 256;
            }
            mono[f] = sum / channels;
        }
        if (rate == Rate) return [.. mono.Select(v => (short)Math.Clamp(v, short.MinValue, short.MaxValue))];
        int outFrames = (int)((long)frames * Rate / rate);
        var result = new short[outFrames];
        for (int i = 0; i < outFrames; i++)
        {
            double src = (double)i * rate / Rate;
            int a = (int)src;
            int b = Math.Min(a + 1, frames - 1);
            double t = src - a;
            result[i] = (short)Math.Clamp(mono[Math.Min(a, frames - 1)] * (1 - t) + mono[b] * t, short.MinValue, short.MaxValue);
        }
        return result;
    }

    /// <summary>Joins WAVs into one, with <paramref name="pauseSeconds"/> of quiet between them. How long it is, in seconds.</summary>
    public static double Join(IReadOnlyList<string> parts, string to, double pauseSeconds)
    {
        var all = new List<short>();
        var pause = new short[(int)(Rate * pauseSeconds)];
        for (int i = 0; i < parts.Count; i++)
        {
            if (i > 0) all.AddRange(pause);
            all.AddRange(Mono(parts[i]));
        }
        Write(to, all.ToArray());
        return (double)all.Count / Rate;
    }
}

/// <summary>The voices this computer has: say on a Mac, Windows' own speech (System.Speech, through Windows PowerShell),
/// espeak-ng or espeak on Linux.</summary>
public static class Voices
{
    public static ISpeech ForThisComputer() =>
        OperatingSystem.IsMacOS() ? new MacSay() : OperatingSystem.IsWindows() ? new WindowsSpeech() : new Espeak();

    static async Task<(int Code, string Out)> RunAsync(string file, IEnumerable<string> args, CancellationToken ct, string? input = null)
    {
        var psi = new ProcessStartInfo(file)
        {
            RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = input is not null, UseShellExecute = false,
            CreateNoWindow = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (string a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi) ?? throw new InvalidOperationException($"{file} didn't start");
        if (input is not null)
        {
            await p.StandardInput.WriteAsync(input);
            p.StandardInput.Close();
        }
        var stdout = p.StandardOutput.ReadToEndAsync(ct);
        var stderr = p.StandardError.ReadToEndAsync(ct);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));
        try
        {
            await p.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            try
            {
                p.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
            }
            throw new TimeoutException($"{Path.GetFileName(file)} took too long to speak");
        }
        return (p.ExitCode, (await stdout) + (await stderr));
    }

    static string? OnPath(string name)
    {
        foreach (string dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string path = Path.Combine(dir, name);
            if (File.Exists(path)) return path;
        }
        return null;
    }

    /// <summary>A Mac's <c>say</c>: two of its English voices if it has them (Samantha and Daniel, or the like), else
    /// its own voice at two speeds.</summary>
    public sealed class MacSay : ISpeech
    {
        string[]? voices;

        public (bool Ok, string Why) Ready() => File.Exists("/usr/bin/say") ? (true, "") : (false, "This Mac has no say command to speak with.");

        async Task<string[]> VoicesAsync(CancellationToken ct)
        {
            if (voices is not null) return voices;
            var have = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var (_, list) = await RunAsync("/usr/bin/say", ["-v", "?"], ct);
                // "Samantha (English (US))  en_US  # Hello…": the name, without what's in brackets.
                foreach (Match m in Regex.Matches(list, @"^(\S+(?: \S+)*?)\s+en[_-]\w+\s+#", RegexOptions.Multiline))
                    have.Add(Regex.Replace(m.Groups[1].Value, @"\s*\(.*$", "").Trim());
            }
            catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or TimeoutException)
            {
            }
            string? Pick(params string[] names) => names.FirstOrDefault(have.Contains);
            return voices = [Pick("Samantha", "Ava", "Allison", "Karen", "Moira", "Tessa") ?? "", Pick("Daniel", "Alex", "Tom", "Fred", "Oliver", "Evan") ?? ""];
        }

        public async Task SayAsync(string text, string who, string wav, CancellationToken ct)
        {
            var v = await VoicesAsync(ct);
            string voice = who == "B" ? v[1] : v[0];
            var args = new List<string>();
            if (voice.Length > 0) args.AddRange(["-v", voice]);
            else if (who == "B") args.AddRange(["-r", "200"]);
            args.AddRange(["-o", wav, $"--data-format=LEI16@{Wav.Rate}", "-f", "-"]);
            var (code, said) = await RunAsync("/usr/bin/say", args, ct, input: text);
            if (code != 0 || !File.Exists(wav)) throw new InvalidOperationException($"say couldn't speak: {said.Trim()}");
        }
    }

    /// <summary>Windows' own speech (SAPI, through System.Speech in Windows PowerShell): its first two voices.</summary>
    public sealed class WindowsSpeech : ISpeech
    {
        static string PowerShell => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");

        public (bool Ok, string Why) Ready() => File.Exists(PowerShell) ? (true, "") : (false, "Windows PowerShell isn't on this PC, so it can't speak.");

        public async Task SayAsync(string text, string who, string wav, CancellationToken ct)
        {
            string textFile = wav + ".txt";
            await File.WriteAllTextAsync(textFile, text, new UTF8Encoding(false), ct);
            try
            {
                string script =
                    "$ErrorActionPreference='Stop'; Add-Type -AssemblyName System.Speech; " +
                    "$s = New-Object System.Speech.Synthesis.SpeechSynthesizer; " +
                    "$v = @($s.GetInstalledVoices() | Where-Object { $_.Enabled -and $_.VoiceInfo.Culture.Name -like 'en*' }); " +
                    "if ($v.Count -eq 0) { $v = @($s.GetInstalledVoices() | Where-Object { $_.Enabled }) }; " +
                    $"$i = {(who == "B" ? 1 : 0)}; if ($v.Count -gt $i) {{ $s.SelectVoice($v[$i].VoiceInfo.Name) }} elseif ($i -eq 1) {{ $s.Rate = 2 }}; " +
                    $"$f = New-Object System.Speech.AudioFormat.SpeechAudioFormatInfo({Wav.Rate}, [System.Speech.AudioFormat.AudioBitsPerSample]::Sixteen, [System.Speech.AudioFormat.AudioChannel]::Mono); " +
                    "$s.SetOutputToWaveFile($env:SYFT_WAV, $f); $s.Speak([IO.File]::ReadAllText($env:SYFT_TEXT)); $s.Dispose()";
                var psi = new ProcessStartInfo(PowerShell) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
                foreach (string a in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", script }) psi.ArgumentList.Add(a);
                psi.Environment["SYFT_WAV"] = wav;
                psi.Environment["SYFT_TEXT"] = textFile;
                using var p = Process.Start(psi) ?? throw new InvalidOperationException("Windows PowerShell didn't start");
                var err = p.StandardError.ReadToEndAsync(ct);
                await p.StandardOutput.ReadToEndAsync(ct);
                await p.WaitForExitAsync(ct);
                if (p.ExitCode != 0 || !File.Exists(wav)) throw new InvalidOperationException($"Windows couldn't speak: {(await err).Trim()}");
            }
            finally
            {
                File.Delete(textFile);
            }
        }
    }

    /// <summary>espeak-ng (or espeak) on Linux: a voice for each, at two pitches.</summary>
    public sealed class Espeak : ISpeech
    {
        string? Binary => OnPath("espeak-ng") ?? OnPath("espeak");

        public (bool Ok, string Why) Ready() => Binary is not null ? (true, "")
            : (false, "This computer has no voice to speak with: install espeak-ng (sudo apt install espeak-ng), then try again.");

        public async Task SayAsync(string text, string who, string wav, CancellationToken ct)
        {
            string bin = Binary ?? throw new InvalidOperationException(Ready().Why);
            var (code, said) = await RunAsync(bin, ["-v", who == "B" ? "en-us+m3" : "en-us+f3", "-s", "165", "-w", wav, "--stdin"], ct, input: text);
            if (code != 0 || !File.Exists(wav)) throw new InvalidOperationException($"espeak couldn't speak: {said.Trim()}");
        }
    }
}
