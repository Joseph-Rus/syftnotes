using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using StudyStash.Core;
using Whisper.net;

namespace StudyStash.Audio;

/// <summary>Which program reads a model's files.</summary>
public enum SpeechEngine { Whisper, Parakeet }

/// <summary>One file of a model that comes as several: its name in the model's folder, its size and its SHA-256, and
/// where it downloads from when that isn't the model's <see cref="WhisperModel.Source"/>.</summary>
public sealed record ModelPart(string Name, long Bytes, string Sha256, string? From = null);

/// <summary>
/// A speech-to-text model Study Stash can download: a Whisper model (whisper.cpp's files on Hugging Face) or
/// Parakeet, which is a folder of several files (the name is from the first model there was).
/// </summary>
public sealed record WhisperModel(string Id, string Name, string File, long Bytes, string Sha256, string About)
{
    public SpeechEngine Engine { get; init; }

    /// <summary>Where the files are: Hugging Face's address up to the file's name.</summary>
    public string Source { get; init; } = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main";

    /// <summary>The files, when the model is a folder called <see cref="File"/> of several; null for one file.</summary>
    public IReadOnlyList<ModelPart>? Parts { get; init; }

    /// <summary>Every file the model is made of.</summary>
    public IReadOnlyList<ModelPart> Files => Parts ?? [new ModelPart(File, Bytes, Sha256)];

    public string Url => UrlOf(Files[0], null);

    /// <summary>Where it downloads from: Hugging Face, or <c>{mirror}/{file}</c> when a mirror is given (the
    /// self-test's own).</summary>
    public string UrlFrom(string? mirror) => UrlOf(Files[0], mirror);

    /// <summary>Where one of its files downloads from.</summary>
    public string UrlOf(ModelPart part, string? mirror) =>
        mirror is { Length: > 0 } m ? $"{m.TrimEnd('/')}/{part.Name}" : part.From ?? $"{Source}/{part.Name}";

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
        "64d182b440b98d5203c4f9bd541544d84c605196c4f7b845dfa11fb23594d1e2", "The most accurate, and the heaviest: it wants a Mac with Apple silicon or a PC with a big graphics card.");
    public static readonly WhisperModel LargeV3Turbo = new("large-v3-turbo", "Whisper large-v3 turbo", "ggml-large-v3-turbo.bin", 1624555275,
        "1fc70f774d38eb169993ac391eea357ef47c88757ef72ee5943879b7e8e2bc69", "More accurate than the compact one, and heavier: it wants a Mac with Apple silicon or a PC with a graphics card.");
    public static readonly WhisperModel LargeV3TurboSmall = new("large-v3-turbo-q5", "Whisper large-v3 turbo (compact)", "ggml-large-v3-turbo-q5_0.bin", 574041195,
        "394221709cd5ad1f40c46e6031ca61bce88931e6e088c188294c6d5a55ffa7e2", "large-v3 turbo in a third of the space. Keeps up with a lecture on most computers and leaves room for everything else.");
    /// <summary>NVIDIA's Parakeet TDT v3 at full precision (its 8-bit file is a fifth the size and about as accurate as
    /// Whisper base on a lecture from a laptop's microphone, so it isn't offered). It was measured against large-v3
    /// on two recorded lectures: 17% and 7% of words different, where the compact turbo differs on 13% and 6% and
    /// Whisper small on 20% and 9%.</summary>
    public static readonly WhisperModel Parakeet = new("parakeet-v3", "NVIDIA Parakeet v3", "parakeet-tdt-0.6b-v3", 2549800429, "",
        "Nearly as accurate as the compact turbo, and quick on the processor alone: it doesn't need a graphics card. Writes whole sentences and never makes up words over silence. Reads English and 24 other European languages.")
    {
        Engine = SpeechEngine.Parakeet,
        Source = "https://huggingface.co/csukuangfj/sherpa-onnx-nemo-parakeet-tdt-0.6b-v3/resolve/main",
        // The small files first, so a wrong address or a full disk shows before the gigabytes.
        Parts =
        [
            new("tokens.txt", 93939, "d58544679ea4bc6ac563d1f545eb7d474bd6cfa467f0a6e2c1dc1c7d37e3c35d"),
            new("joiner.onnx", 25286330, "b9b0bcf88ac571902e69a6536223ed2d94885e981b85045410f1403d53121a63"),
            new("decoder.onnx", 47233743, "d593cdb0e571f5a457ec2219af9968cbf6b0e8198e8f7839b40a8754593bf68c"),
            new("encoder.onnx", 41766257, "3eed7ce424bf8339ad09233533c687e2dbd07e74ccf5027b5e7344019ea373b0"),
            new("encoder.weights", 2435420160, "3af3f51af5f2d01dbbf5af47d42c7962a2c205f11004254bb4f2b979862f39a8"),
        ],
    };
    /// <summary>What tells the voices in a lecture apart (not a transcription model, so it isn't in <see cref="All"/>):
    /// pyannote's segmentation (which stretches are one voice; MIT) and NVIDIA's TitaNet small (which voices are the
    /// same person; CC BY 4.0), both from sherpa-onnx's conversions. Tried on two lectures with the voices set alike at
    /// 0.9: a lecturer and the students who asked came out as separate voices.</summary>
    public static readonly WhisperModel Speakers = new("speakers", "Speaker voices", "speaker-voices", 5992913 + 40257283, "",
        "Tells the voices in a lecture apart, so a student's question isn't taken for the lecturer's words.")
    {
        Parts =
        [
            new("segmentation.onnx", 5992913, "220ad67ca923bef2fa91f2390c786097bf305bceb5e261d4af67b38e938e1079",
                "https://huggingface.co/csukuangfj/sherpa-onnx-pyannote-segmentation-3-0/resolve/main/model.onnx"),
            new("embedding.onnx", 40257283, "ad4a1802485d8b34c722d2a9d04249662f2ece5d28a7a039063ca22f515a789e",
                "https://huggingface.co/csukuangfj/speaker-embedding-models/resolve/main/nemo_en_titanet_small.onnx"),
        ],
    };
    public static readonly WhisperModel Small = new("small", "Whisper small", "ggml-small.bin", 487601967,
        "1be3a9b2063867b937e64e2ec7483364a79917e157fa98c5d94b5c1fffea987b", "Quick on any processor. Misses more names and terms than the large ones.");
    public static readonly WhisperModel Base = new("base", "Whisper base", "ggml-base.bin", 147951465,
        "60ed5bc3dd14eea856493d334349b405782ddcaf0028d4b5df4088345fba2efe", "The lightest that still follows a lecture, for an older computer or one with little memory.");
    /// <summary>For tests: small and fast, not good enough for lectures.</summary>
    public static readonly WhisperModel Tiny = new("tiny", "Whisper tiny", "ggml-tiny.bin", 77691713,
        "be07e048e1e599ad46341c8d2a135645097a538221678b7acdd1b1919c6e1b21", "For trying things out.");

    /// <summary>Every model, heaviest first (Parakeet asks less of the processor than the compact turbo, but more
    /// memory; it sits below it, where a computer that keeps up with the compact turbo keeps up with it).</summary>
    public static readonly IReadOnlyList<WhisperModel> All = [LargeV3, LargeV3Turbo, LargeV3TurboSmall, Parakeet, Small, Base, Tiny];

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
    /// The model a computer starts on, and one plain line why: the compact large-v3 turbo, on every computer that keeps
    /// up with it (Apple silicon and PCs with a strong graphics card too), because it's light on the computer and
    /// leaves room for everything else; a computer too weak for it starts on the lighter one it can run
    /// (<see cref="Heaviest"/>). The bigger models stay a choice in setup and Settings → Recording. A computer with no
    /// graphics card Whisper can use, and the processor and memory for it, starts on Parakeet instead, when
    /// <paramref name="parakeet"/> (it doesn't read the lecture's language, or it won't run here: false).
    /// </summary>
    public static ModelAdvice Advise(HardwareProfile hw, bool parakeet = true)
    {
        // On the processor alone Parakeet is what to start on: it was made for it (see Heaviest), nearly as accurate as
        // the compact turbo, and never makes up words over silence.
        if (parakeet && ProcessorOnly(hw) && FitsParakeet(hw))
            return new(Parakeet, $"This {hw.DeviceWord} has no graphics card Whisper can use, so Parakeet, made for the processor, keeps up with a lecture.");
        var heaviest = Heaviest(hw, parakeet);
        if (!Heavier(heaviest.Model, LargeV3TurboSmall)) return heaviest;
        return new(LargeV3TurboSmall, hw.AppleSilicon
            ? "The compact model keeps up with a lecture and leaves this Mac room for everything else. Its Apple silicon can run a bigger one too."
            : $"The compact model keeps up with a lecture and leaves this PC room for everything else. Its graphics card ({hw.WhisperCard?.Name}) can run a bigger one too.");
    }

    /// <summary>
    /// The heaviest (most accurate) model that keeps up with a lecture on this computer, and one plain line why.
    /// <para>
    /// A lecture is written down live, a 30-second piece at a time, so the model has to finish each piece well inside
    /// 30 seconds or the transcript falls further behind all lecture. What decides that:
    /// </para>
    /// <list type="bullet">
    /// <item>Apple silicon runs Whisper on its graphics (Metal) with memory the chip shares: large-v3 keeps up on every
    /// one. Only a Mac with under 7 GB (none are sold) is kept to the compact model, to leave room.</item>
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
    /// Memory the computer won't say (null) counts as enough: the student can always pick a lighter one. A model
    /// heavier than this one is worth a word (<c>ModelSuggestionAsync</c>); one up to this heavy is the student's call.
    /// </summary>
    public static ModelAdvice Heaviest(HardwareProfile hw, bool parakeet = true)
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
        // Parakeet does a piece of a lecture on the processor in a small part of the time Whisper does (measured at
        // 0.04 of the lecture's length on 5 threads, 0.09 on 2), so it takes the place of Whisper small here; it holds
        // about 3 GB of memory.
        if (parakeet && FitsParakeet(hw))
            return new(Parakeet, $"This {device} has no graphics card Whisper can use, and Parakeet, made for the processor, keeps up with a lecture.");
        if (hw.Cores >= 4 && ram is not < 4)
            return new(Small, $"This {device} has no graphics card Whisper can use and a modest processor, so Whisper small keeps up with a lecture.");
        return new(Base, ram is < 4
            ? $"This {device} has little memory, so Whisper base keeps up with a lecture and leaves room for everything else."
            : $"This {device}'s processor would fall behind a lecture with anything bigger, so Whisper base keeps up.");
    }

    /// <summary>No Apple silicon, no graphics card Whisper can use: Whisper would run on the processor.</summary>
    static bool ProcessorOnly(HardwareProfile hw) => !hw.AppleSilicon && hw.WhisperCard is not { MemoryGb: >= 2 };

    /// <summary>Parakeet on the processor wants 4 cores with fast maths (AVX2, or an ARM chip's NEON) and 8 GB of memory
    /// (it holds about 3); memory the computer won't say counts as enough.</summary>
    static bool FitsParakeet(HardwareProfile hw) => hw.Cores >= 4 && hw.FastMath && hw.RamGb is not < 8;

    public static string Dir(string home) => Path.Combine(home, "models");

    /// <summary>The model's file, or its folder when it's several files.</summary>
    public static string PathFor(string home, WhisperModel m) => Path.Combine(Dir(home), m.File);

    /// <summary>Where one of the model's files is.</summary>
    public static string PathOf(string home, WhisperModel m, ModelPart part) =>
        m.Parts is null ? PathFor(home, m) : Path.Combine(PathFor(home, m), part.Name);

    public static bool IsDownloaded(string home, WhisperModel m) =>
        m.Files.All(p => new FileInfo(PathOf(home, m, p)) is { Exists: true } f && f.Length == p.Bytes);

    /// <summary>How much of the model is on this computer: the files that are all here, and what came of the others'
    /// downloads (their .part files).</summary>
    public static long OnDisk(string home, WhisperModel m) => m.Files.Sum(p => OnDisk(home, m, p));

    /// <summary>How much of one of the model's files is here: all of it, or what a stopped download left.</summary>
    public static long OnDisk(string home, WhisperModel m, ModelPart part)
    {
        string path = PathOf(home, m, part);
        if (new FileInfo(path) is { Exists: true } f && f.Length == part.Bytes) return part.Bytes;
        return new FileInfo(path + ".part") is { Exists: true } stopped ? Math.Min(stopped.Length, part.Bytes) : 0;
    }

    /// <summary>Delete the model and whatever came of its download. Throws IOException or UnauthorizedAccessException
    /// when a file can't be removed.</summary>
    public static void Delete(string home, WhisperModel m)
    {
        foreach (var p in m.Files)
        {
            string path = PathOf(home, m, p);
            System.IO.File.Delete(path);
            System.IO.File.Delete(path + ".part");
        }
        if (m.Parts is not null && Directory.Exists(PathFor(home, m)) && !Directory.EnumerateFileSystemEntries(PathFor(home, m)).Any())
            Directory.Delete(PathFor(home, m));
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
    /// Download <paramref name="m"/> (from <paramref name="url"/>, or Hugging Face; a model of several files comes from
    /// Hugging Face, or <c>{mirror}/{file}</c>), picking up from the .part file of the one it stopped in. Progress is
    /// reported at once and then about once a second, over all its files. Throws <see cref="NotEnoughSpaceException"/>
    /// when the disk can't hold it, <see cref="InvalidDataException"/> (and forgets the .part) when what arrived isn't
    /// the model, and HttpRequestException or IOException when the connection fails (the .part stays for next time).
    /// </summary>
    public static async Task<string> RunAsync(string home, WhisperModel m, IProgress<DownloadProgress>? progress = null,
        CancellationToken stop = default, HttpClient? http = null, string? url = null, Func<string, long?>? freeBytes = null,
        string? mirror = null)
    {
        string path = WhisperModels.PathFor(home, m);
        Directory.CreateDirectory(m.Parts is null ? Path.GetDirectoryName(path)! : path);
        if (WhisperModels.IsDownloaded(home, m)) return path;
        if (m.Parts is not null)
        {
            // A folder of gigabytes: room for all of it is asked before the first file starts.
            long needed = m.Bytes - WhisperModels.OnDisk(home, m) + Spare;
            if ((freeBytes ?? Disk.FreeBytes)(path) is { } free && free < needed) throw new NotEnoughSpaceException(m, needed);
        }
        // What's done is the files before this one, this one so far, and what the files after it already have (a
        // stopped download's .part files), so the count only ever goes up.
        var files = m.Files;
        long[] here = [.. files.Select(p => WhisperModels.OnDisk(home, m, p))];
        long before = 0;
        for (int i = 0; i < files.Count; i++)
        {
            await FileAsync(WhisperModels.PathOf(home, m, files[i]), files[i], m, before + here.Skip(i + 1).Sum(), progress, stop, http,
                m.Parts is null ? url ?? m.UrlOf(files[i], mirror) : m.UrlOf(files[i], mirror), freeBytes);
            before += files[i].Bytes;
        }
        return path;
    }

    /// <summary>One file of a model, <paramref name="before"/> bytes of the rest of the model already being here.</summary>
    static async Task FileAsync(string path, ModelPart file, WhisperModel m, long before, IProgress<DownloadProgress>? progress,
        CancellationToken stop, HttpClient? http, string url, Func<string, long?>? freeBytes)
    {
        string part = path + ".part", dir = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);
        if (System.IO.File.Exists(path) && new FileInfo(path).Length == file.Bytes) return;
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long have = System.IO.File.Exists(part) ? new FileInfo(part).Length : 0;
        if (have > file.Bytes) have = 0;
        if (have > 0) await HashAsync(part, sha, stop);
        progress?.Report(new DownloadProgress(before + have, m.Bytes, 0));
        if (have == file.Bytes)
        {
            // All of it came before, and the app stopped before moving it into place: nothing to ask for.
            if (Convert.ToHexStringLower(sha.GetHashAndReset()) == file.Sha256)
            {
                System.IO.File.Move(part, path, overwrite: true);
                return;
            }
            System.IO.File.Delete(part);
            have = 0;
        }
        long needed = file.Bytes - have + Spare;
        if ((freeBytes ?? Disk.FreeBytes)(dir) is { } free && free < needed) throw new NotEnoughSpaceException(m, needed);

        var response = await SendAsync(http ?? Http, url, have, stop);
        if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable && have > 0)
        {
            // The server won't send from where the .part stops (its file changed?): start over, once.
            response.Dispose();
            System.IO.File.Delete(part);
            have = 0;
            sha.GetHashAndReset();
            progress?.Report(new DownloadProgress(before, m.Bytes, 0));
            response = await SendAsync(http ?? Http, url, 0, stop);
        }
        using (response)
        {
            if (have > 0 && response.StatusCode != HttpStatusCode.PartialContent)
            {
                // The server sent the whole file again: start over.
                have = 0;
                sha.GetHashAndReset();
                progress?.Report(new DownloadProgress(before, m.Bytes, 0));
            }
            response.EnsureSuccessStatusCode();
            try
            {
                await using var body = await response.Content.ReadAsStreamAsync(stop);
                await using var stream = new FileStream(part, have > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20);
                var buf = new byte[1 << 20];
                long done = have, windowStart = have;
                var clock = System.Diagnostics.Stopwatch.StartNew();
                double rate = 0;
                int n;
                while ((n = await body.ReadAsync(buf, stop)) > 0)
                {
                    await stream.WriteAsync(buf.AsMemory(0, n), stop);
                    sha.AppendData(buf, 0, n);
                    done += n;
                    if (clock.Elapsed.TotalSeconds >= 1)
                    {
                        double now = (done - windowStart) / clock.Elapsed.TotalSeconds;
                        rate = rate <= 0 ? now : rate * 0.7 + now * 0.3;
                        windowStart = done;
                        clock.Restart();
                        progress?.Report(new DownloadProgress(before + done, m.Bytes, rate));
                    }
                }
                progress?.Report(new DownloadProgress(before + done, m.Bytes, rate));
            }
            catch (IOException e) when (Disk.IsFull(e))
            {
                long got = System.IO.File.Exists(part) ? new FileInfo(part).Length : 0;
                throw new NotEnoughSpaceException(m, file.Bytes - got + Spare);
            }
        }
        long length = new FileInfo(part).Length;
        // The connection closed early without saying so: what came is kept, and the next try asks for the rest.
        if (length < file.Bytes) throw new IOException($"The {m.Name} download stopped before the end.");
        if (length != file.Bytes || Convert.ToHexStringLower(sha.GetHashAndReset()) != file.Sha256)
        {
            System.IO.File.Delete(part);
            throw new InvalidDataException($"The {m.Name} download came out damaged; try again.");
        }
        System.IO.File.Move(part, path, overwrite: true);
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
    readonly bool gpu;

    /// <param name="language">"" or "auto" finds each lecture's language; "en" and so on fixes it.</param>
    /// <param name="gpu">False keeps it on the processor even where there's a GPU (to measure a computer without one).</param>
    public WhisperTranscriber(string modelPath, string language = "", bool gpu = true)
    {
        factory = WhisperFactory.FromPath(modelPath, new WhisperFactoryOptions { UseGpu = gpu });
        this.gpu = gpu;
        fixedLanguage = language is "auto" ? "" : language;
    }

    /// <summary>Which of whisper.cpp's builds loaded (it says whether the GPU is in use).</summary>
    public static string Runtime => WhisperFactory.GetRuntimeInfo() ?? "";

    /// <summary>For the log: what this model runs on (Metal, Vulkan or the processor), and whisper.cpp's own line about
    /// the build it loaded ("MTL" in it is Metal).</summary>
    public string Backend
    {
        get
        {
            var loaded = Whisper.net.LibraryLoader.RuntimeOptions.LoadedLibrary;
            string info = Runtime.Trim().TrimEnd('|').Trim();
            string on = !gpu ? "the processor" : info.Contains("MTL", StringComparison.Ordinal) ? "Metal"
                : loaded is Whisper.net.LibraryLoader.RuntimeLibrary.Vulkan ? "Vulkan" : "the processor";
            return $"{on} (whisper.cpp's {loaded?.ToString() ?? "unknown"} build: {info})";
        }
    }

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
