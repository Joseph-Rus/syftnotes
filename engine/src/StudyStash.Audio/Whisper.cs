using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using StudyStash.Core;
using Whisper.net;

namespace StudyStash.Audio;

/// <summary>A Whisper model Study Stash can download (whisper.cpp's files on Hugging Face).</summary>
public sealed record WhisperModel(string Id, string Name, string File, long Bytes, string Sha256, string About)
{
    public string Url => $"https://huggingface.co/ggerganov/whisper.cpp/resolve/main/{File}";

    /// <summary>Where it downloads from: Hugging Face, or <c>{mirror}/{file}</c> when a mirror is given (the
    /// self-test's own).</summary>
    public string UrlFrom(string? mirror) => mirror is { Length: > 0 } m ? $"{m.TrimEnd('/')}/{File}" : Url;

    /// <summary>"3.1 GB", "550 MB".</summary>
    public string Size => SizeOf(Bytes);

    /// <summary>An amount of bytes the way Study Stash says it: "1.9 GB", "550 MB".</summary>
    public static string SizeOf(long bytes) => bytes >= 1_000_000_000 ? $"{bytes / 1e9:0.0} GB" : $"{bytes / 1e6:0} MB";
}

/// <summary>The model that suits a computer, and why, in a line the student reads.</summary>
public sealed record ModelAdvice(WhisperModel Model, string Why);

public static class WhisperModels
{
    public static readonly WhisperModel LargeV3 = new("large-v3", "Whisper large-v3", "ggml-large-v3.bin", 3095033483,
        "64d182b440b98d5203c4f9bd541544d84c605196c4f7b845dfa11fb23594d1e2", "The most accurate. Best on a Mac with Apple silicon or a PC with a graphics card.");
    public static readonly WhisperModel LargeV3Turbo = new("large-v3-turbo", "Whisper large-v3 turbo", "ggml-large-v3-turbo.bin", 1624555275,
        "1fc70f774d38eb169993ac391eea357ef47c88757ef72ee5943879b7e8e2bc69", "Nearly as accurate as large-v3 and several times faster.");
    public static readonly WhisperModel LargeV3TurboSmall = new("large-v3-turbo-q5", "Whisper large-v3 turbo (compact)", "ggml-large-v3-turbo-q5_0.bin", 574041195,
        "394221709cd5ad1f40c46e6031ca61bce88931e6e088c188294c6d5a55ffa7e2", "large-v3 turbo in a third of the space, for computers without a graphics card.");
    public static readonly WhisperModel Small = new("small", "Whisper small", "ggml-small.bin", 487601967,
        "1be3a9b2063867b937e64e2ec7483364a79917e157fa98c5d94b5c1fffea987b", "Quick on any processor. Misses more names and terms than the large ones.");
    public static readonly WhisperModel Base = new("base", "Whisper base", "ggml-base.bin", 147951465,
        "60ed5bc3dd14eea856493d334349b405782ddcaf0028d4b5df4088345fba2efe", "The lightest that still follows a lecture, for an older computer or one with little memory.");
    /// <summary>For tests: small and fast, not good enough for lectures.</summary>
    public static readonly WhisperModel Tiny = new("tiny", "Whisper tiny", "ggml-tiny.bin", 77691713,
        "be07e048e1e599ad46341c8d2a135645097a538221678b7acdd1b1919c6e1b21", "For trying things out.");

    /// <summary>Every model, heaviest (and most accurate) first.</summary>
    public static readonly IReadOnlyList<WhisperModel> All = [LargeV3, LargeV3Turbo, LargeV3TurboSmall, Small, Base, Tiny];

    public static WhisperModel? Find(string id) => All.FirstOrDefault(m => m.Id == id);

    /// <summary><paramref name="a"/> asks more of the computer than <paramref name="b"/>.</summary>
    public static bool Heavier(WhisperModel a, WhisperModel b)
    {
        int ai = IndexOf(a), bi = IndexOf(b);
        return ai >= 0 && bi >= 0 && ai < bi;
    }

    static int IndexOf(WhisperModel m)
    {
        for (int i = 0; i < All.Count; i++)
            if (All[i].Id == m.Id) return i;
        return -1;
    }

    /// <summary>
    /// The model that keeps up with a lecture on this computer, and one plain line why.
    /// <para>
    /// A lecture is written down live, a 30-second piece at a time, so the model has to finish each piece well inside
    /// 30 seconds or the transcript falls further behind all lecture. What decides that:
    /// </para>
    /// <list type="bullet">
    /// <item>Apple silicon runs Whisper on its graphics (Metal) with memory the chip shares: large-v3 keeps up on every
    /// one, as it always has here. Only a Mac with under 7 GB (none are sold) gets the compact model, to leave room.</item>
    /// <item>A PC with a real graphics card runs Whisper on the card through Vulkan, and what fits is the card's own
    /// memory. large-v3 needs about 4 GB of it with room to work, so it wants an 8 GB card (the usual size after 6;
    /// cards say a little under, hence 7.5). large-v3 turbo (about 2 GB) fits a 4 GB card and is nearly as accurate;
    /// the compact turbo (about 0.8 GB) fits the rest down to 2 GB. Under 2 GB it's graphics built into the processor
    /// and Whisper uses the processor.</item>
    /// <item>On the processor alone, the encoder (the same size in every large-v3 model) is the slow part. With 8 or
    /// more threads and fast maths (AVX2, or any ARM chip's NEON) the compact turbo (q5) does a piece in well under
    /// 30 seconds on 4 of them; it also needs about 1.5 GB of memory, so 8 GB of memory in all. Fewer threads,
    /// no AVX2 or less memory: Whisper small, whose encoder does a fraction of the work. Under 4 threads or under
    /// 4 GB of memory: Whisper base.</item>
    /// </list>
    /// Memory the computer won't say (null) counts as enough: the student can always pick a lighter one.
    /// </summary>
    public static ModelAdvice Advise(HardwareProfile hw)
    {
        string device = hw.DeviceWord;
        double? ram = hw.RamGb;
        if (hw.AppleSilicon)
            return ram is < 7
                ? new(LargeV3TurboSmall, $"This Mac has {ram:0} GB of memory, so the compact model leaves room for everything else.")
                : new(LargeV3, "This Mac's Apple silicon runs the most accurate model and keeps up with a lecture.");
        if (hw.WhisperCard is { MemoryGb: >= 2 } card)
        {
            if (card.MemoryGb >= 7.5 && ram is not < 8)
                return new(LargeV3, $"This PC's graphics card ({card.Name}) runs the most accurate model and keeps up with a lecture.");
            if (card.MemoryGb >= 3.5)
                return new(LargeV3Turbo, $"This PC's graphics card ({card.Name}) has room for large-v3 turbo, which keeps up with a lecture.");
            return new(LargeV3TurboSmall, $"This PC's graphics card ({card.Name}) has little memory of its own, so the compact model keeps up with a lecture.");
        }
        if (hw.Cores >= 8 && hw.FastMath && ram is not < 8)
            return new(LargeV3TurboSmall, $"This {device} has no graphics card Whisper can use, so the compact model keeps up with a lecture.");
        if (hw.Cores >= 4 && ram is not < 4)
            return new(Small, $"This {device} has no graphics card Whisper can use and a modest processor, so Whisper small keeps up with a lecture.");
        return new(Base, ram is < 4
            ? $"This {device} has little memory, so Whisper base keeps up with a lecture and leaves room for everything else."
            : $"This {device}'s processor would fall behind a lecture with anything bigger, so Whisper base keeps up.");
    }

    public static string Dir(string home) => Path.Combine(home, "models");

    public static string PathFor(string home, WhisperModel m) => Path.Combine(Dir(home), m.File);

    public static bool IsDownloaded(string home, WhisperModel m)
    {
        var f = new FileInfo(PathFor(home, m));
        return f.Exists && f.Length == m.Bytes;
    }
}

/// <summary>How far a download has got: bytes so far, of how many, and how fast (bytes a second, lately).</summary>
public sealed record DownloadProgress(long Done, long Total, double BytesPerSecond)
{
    public double Fraction => Total <= 0 ? 0 : Math.Clamp(Done / (double)Total, 0, 1);

    /// <summary>"1.9 GB of 3.1 GB".</summary>
    public string Amount => $"{WhisperModel.SizeOf(Done)} of {WhisperModel.SizeOf(Total)}";

    /// <summary>"About 4 minutes left", once there's a rate to go on.</summary>
    public string? Left()
    {
        if (BytesPerSecond <= 0 || Done >= Total) return null;
        double s = (Total - Done) / BytesPerSecond;
        return s < 60 ? "Less than a minute left" : s < 90 ? "About a minute left" : $"About {Math.Round(s / 60)} minutes left";
    }
}

/// <summary>The disk hasn't room for a model. Only the student can fix that, so nothing tries again by itself.</summary>
public sealed class NotEnoughSpaceException(WhisperModel model, long needed)
    : IOException($"Not enough space for {model.Name}: it needs {WhisperModel.SizeOf(needed)} free.");

/// <summary>
/// Downloads a model into the models folder: to a .part file that a later try picks up from (a laptop closes
/// mid-download, the connection drops), checked against its SHA-256 before it's used.
/// </summary>
public static class ModelDownload
{
    static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };

    /// <summary>Room kept free beyond the model, so a download never fills the disk a recording needs.</summary>
    public const long Spare = 200_000_000;

    /// <summary>
    /// Download <paramref name="m"/> (from <paramref name="url"/>, or Hugging Face), picking up from its .part file.
    /// Progress is reported at once and then about once a second. Throws <see cref="NotEnoughSpaceException"/> when the
    /// disk can't hold it, <see cref="InvalidDataException"/> (and forgets the .part) when what arrived isn't the model,
    /// and HttpRequestException or IOException when the connection fails (the .part stays for next time).
    /// </summary>
    public static async Task<string> RunAsync(string home, WhisperModel m, IProgress<DownloadProgress>? progress = null,
        CancellationToken stop = default, HttpClient? http = null, string? url = null, Func<string, long?>? freeBytes = null)
    {
        string path = WhisperModels.PathFor(home, m), part = path + ".part", dir = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);
        if (WhisperModels.IsDownloaded(home, m)) return path;
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long have = System.IO.File.Exists(part) ? new FileInfo(part).Length : 0;
        if (have > m.Bytes) have = 0;
        if (have > 0) await HashAsync(part, sha, stop);
        progress?.Report(new DownloadProgress(have, m.Bytes, 0));
        if (have == m.Bytes)
        {
            // All of it came before, and the app stopped before moving it into place: nothing to ask for.
            if (Convert.ToHexStringLower(sha.GetHashAndReset()) == m.Sha256)
            {
                System.IO.File.Move(part, path, overwrite: true);
                return path;
            }
            System.IO.File.Delete(part);
            have = 0;
        }
        long needed = m.Bytes - have + Spare;
        if ((freeBytes ?? Disk.FreeBytes)(dir) is { } free && free < needed) throw new NotEnoughSpaceException(m, needed);

        var response = await SendAsync(http ?? Http, url ?? m.Url, have, stop);
        if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable && have > 0)
        {
            // The server won't send from where the .part stops (its file changed?): start over, once.
            response.Dispose();
            System.IO.File.Delete(part);
            have = 0;
            sha.GetHashAndReset();
            progress?.Report(new DownloadProgress(0, m.Bytes, 0));
            response = await SendAsync(http ?? Http, url ?? m.Url, 0, stop);
        }
        using (response)
        {
            if (have > 0 && response.StatusCode != HttpStatusCode.PartialContent)
            {
                // The server sent the whole file again: start over.
                have = 0;
                sha.GetHashAndReset();
                progress?.Report(new DownloadProgress(0, m.Bytes, 0));
            }
            response.EnsureSuccessStatusCode();
            try
            {
                await using var body = await response.Content.ReadAsStreamAsync(stop);
                await using var file = new FileStream(part, have > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20);
                var buf = new byte[1 << 20];
                long done = have, windowStart = have;
                var clock = System.Diagnostics.Stopwatch.StartNew();
                double rate = 0;
                int n;
                while ((n = await body.ReadAsync(buf, stop)) > 0)
                {
                    await file.WriteAsync(buf.AsMemory(0, n), stop);
                    sha.AppendData(buf, 0, n);
                    done += n;
                    if (clock.Elapsed.TotalSeconds >= 1)
                    {
                        double now = (done - windowStart) / clock.Elapsed.TotalSeconds;
                        rate = rate <= 0 ? now : rate * 0.7 + now * 0.3;
                        windowStart = done;
                        clock.Restart();
                        progress?.Report(new DownloadProgress(done, m.Bytes, rate));
                    }
                }
                progress?.Report(new DownloadProgress(done, m.Bytes, rate));
            }
            catch (IOException e) when (Disk.IsFull(e))
            {
                long got = System.IO.File.Exists(part) ? new FileInfo(part).Length : 0;
                throw new NotEnoughSpaceException(m, m.Bytes - got + Spare);
            }
        }
        long length = new FileInfo(part).Length;
        // The connection closed early without saying so: what came is kept, and the next try asks for the rest.
        if (length < m.Bytes) throw new IOException($"The {m.Name} download stopped before the end.");
        if (length != m.Bytes || Convert.ToHexStringLower(sha.GetHashAndReset()) != m.Sha256)
        {
            System.IO.File.Delete(part);
            throw new InvalidDataException($"The {m.Name} download came out damaged; try again.");
        }
        System.IO.File.Move(part, path, overwrite: true);
        return path;
    }

    static Task<HttpResponseMessage> SendAsync(HttpClient http, string url, long from, CancellationToken stop)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (from > 0) request.Headers.Range = new RangeHeaderValue(from, null);
        return http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, stop);
    }

    static async Task HashAsync(string path, IncrementalHash sha, CancellationToken stop)
    {
        await using var existing = System.IO.File.OpenRead(path);
        var buf = new byte[1 << 20];
        int n;
        while ((n = await existing.ReadAsync(buf, stop)) > 0) sha.AppendData(buf, 0, n);
    }
}

/// <summary>Whisper (whisper.cpp) on this computer: Metal on Apple silicon, Vulkan on a Windows graphics card, the
/// processor elsewhere.</summary>
public sealed class WhisperTranscriber : ITranscriber
{
    readonly WhisperFactory factory;
    readonly string fixedLanguage;

    /// <param name="language">"" or "auto" finds each lecture's language; "en" and so on fixes it.</param>
    public WhisperTranscriber(string modelPath, string language = "")
    {
        factory = WhisperFactory.FromPath(modelPath);
        fixedLanguage = language is "auto" ? "" : language;
    }

    /// <summary>Which of whisper.cpp's builds loaded (it says whether the GPU is in use).</summary>
    public static string Runtime => WhisperFactory.GetRuntimeInfo() ?? "";

    public async Task<Transcription> TranscribeAsync(float[] samples, string prompt, string language, CancellationToken stop)
    {
        string lang = fixedLanguage.Length > 0 ? fixedLanguage : language.Length > 0 ? language : "auto";
        var builder = factory.CreateBuilder()
            .WithLanguage(lang)
            .WithThreads(Math.Clamp(Environment.ProcessorCount / 2, 1, 8))
            .WithNoSpeechThreshold(0.6f);
        if (prompt.Length > 0) builder = builder.WithPrompt(prompt);
        await using var processor = builder.Build();
        var segments = new List<Spoken>();
        string found = "";
        await foreach (var s in processor.ProcessAsync(samples, stop))
        {
            segments.Add(new Spoken(s.Start.TotalSeconds, s.End.TotalSeconds, s.Text));
            if (found.Length == 0 && !string.IsNullOrEmpty(s.Language)) found = s.Language;
        }
        return new Transcription(segments, lang == "auto" ? found : lang);
    }

    public void Dispose() => factory.Dispose();
}
