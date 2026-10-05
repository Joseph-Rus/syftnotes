using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using StudyStash.Core;

namespace StudyStash.Audio;

/// <summary>The languages Cactus Whistle reads: seven European ones. A lecture in another needs Whisper.</summary>
public static class WhistleLanguages
{
    static readonly Dictionary<string, string> Known = new()
    {
        ["en"] = "english", ["de"] = "german", ["fr"] = "french", ["es"] = "spanish", ["it"] = "italian", ["nl"] = "dutch", ["pl"] = "polish",
    };

    /// <summary>Whether a lecture language the student typed ("en", "en-GB", "Spanish"; "" or "auto" for each lecture's
    /// own) is one Whistle reads.</summary>
    public static bool Knows(string language) => Code(language) is not null;

    /// <summary>The code Whistle takes for a language ("" to find it), or null for one it doesn't read.</summary>
    public static string? Code(string language)
    {
        string l = language.Trim().ToLowerInvariant();
        if (l is "" or "auto") return "";
        string code = l.Split('-', '_')[0];
        if (Known.ContainsKey(code)) return code;
        return Known.FirstOrDefault(k => k.Value == l).Key;
    }
}

/// <summary>
/// Cactus Whistle: a 17 MB speech model on the processor, through Cactus Compute's needle engine (both Apache-2.0,
/// carried in the app: see Needle.targets). It hears 30 seconds at most at a time, in a small part of a second, gives
/// each word its time, and hears nothing in silence rather than making words up. Quick enough to hear the newest sound
/// of a lecture every second (<see cref="IWordHearer"/>, the recorder's live words), and to write a whole lecture down.
/// <para>The engine holds one speech model for the whole process and isn't safe on two threads at once: every pass
/// takes <see cref="Gate"/>, and the model, once loaded, stays (it has no way to let go of one; 17 MB).</para>
/// </summary>
public sealed class WhistleTranscriber : ITranscriber, IWordHearer
{
    public const string File = "whistle.cact";

    static readonly Lock Gate = new();
    static string? loadedFrom;
    static byte[]? output;

    readonly string fixedLanguage;

    /// <summary>The engine and the build of it this computer runs: "needle 3.1.0 osx-x64".</summary>
    public static string EngineBuild => $"needle {Needle.Version} {Needle.Rid ?? "none"}";

    /// <summary>For the log: what it runs on.</summary>
    public string Backend => $"Cactus needle {Needle.Version} on the processor ({Needle.Rid})";

    /// <param name="modelPath">whistle.cact (the app's own: <see cref="BundledModel"/>).</param>
    /// <param name="language">A language the lecture is fixed to, or "" for none (each lecture's own).</param>
    public WhistleTranscriber(string modelPath, string language = "")
    {
        fixedLanguage = WhistleLanguages.Code(language) ?? throw new InvalidOperationException(
            $"Cactus Whistle doesn't read \"{language}\": pick a Whisper model in Settings → Recording, or leave the language empty.");
        lock (Gate) Load(modelPath);
    }

    /// <summary>The model the app carries (models/whistle.cact beside it).</summary>
    public static string BundledModel => Path.Combine(AppContext.BaseDirectory, "models", File);

    /// <summary>The engine is here for this computer and the model beside the app: Whistle can run.</summary>
    public static bool Available => Needle.LibraryPath() is not null && System.IO.File.Exists(BundledModel);

    static void Load(string modelPath)
    {
        string full = Path.GetFullPath(modelPath);
        if (loadedFrom == full) return;
        if (loadedFrom is not null) throw new InvalidOperationException($"Cactus Whistle has {loadedFrom} loaded already.");
        Needle.Bind();
        Needle.LoadModel(System.IO.File.ReadAllBytes(full));
        loadedFrom = full;
    }

    public Task<Transcription> TranscribeAsync(float[] samples, string prompt, string language, CancellationToken stop) =>
        Task.Run(() =>
        {
            string lang = fixedLanguage.Length > 0 ? fixedLanguage : WhistleLanguages.Code(language) ?? "";
            var words = new List<TimedWord>();
            string found = "";
            // A pass takes 30 seconds at most: longer sound is cut at its quietest moments, as the transcript's pieces are.
            int at = 0;
            while (at < samples.Length)
            {
                stop.ThrowIfCancellationRequested();
                int take = Segmenter.CutLength(samples.AsSpan(at), final: true, minSeconds: 20, maxSeconds: CaptionStitcher.MostSeconds);
                var piece = samples.AsSpan(at, take).ToArray();
                var (heard, spoke) = Pass(piece, lang, "");
                double offset = at / (double)Sound.Rate;
                words.AddRange(heard.Select(w => w with { Start = w.Start + offset, End = w.End + offset }));
                if (found.Length == 0) found = spoke;
                at += take;
            }
            return new Transcription(WordLines.Lines(words), fixedLanguage.Length > 0 ? fixedLanguage : lang.Length > 0 ? lang : found);
        }, stop);

    public HeardWords Hear(float[] samples, string language, string keywords)
    {
        var (words, heard) = Pass(samples, WhistleLanguages.Code(language) ?? fixedLanguage, keywords);
        return new HeardWords(words, heard);
    }

    /// <summary>One pass of at most 30 seconds: its words, and the language it heard ("" for silence).</summary>
    static (List<TimedWord> Words, string Language) Pass(float[] samples, string language, string keywords)
    {
        if (samples.Length == 0) return ([], "");
        string json;
        lock (Gate)
        {
            output ??= new byte[1 << 20];
            json = Needle.Transcribe(samples, language, keywords, output);
        }
        return Read(json);
    }

    /// <summary>The engine's answer: {"text", "language", "words": [{"word", "start", "end", "probability"}]}.</summary>
    internal static (List<TimedWord> Words, string Language) Read(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        string language = root.TryGetProperty("language", out var l) && l.ValueKind == JsonValueKind.String ? l.GetString() ?? "" : "";
        var words = new List<TimedWord>();
        if (root.TryGetProperty("words", out var list) && list.ValueKind == JsonValueKind.Array)
            foreach (var w in list.EnumerateArray())
            {
                string text = w.TryGetProperty("word", out var t) ? t.GetString() ?? "" : "";
                if (text.Trim().Length == 0) continue;
                double start = w.TryGetProperty("start", out var s) ? s.GetDouble() : 0;
                double end = w.TryGetProperty("end", out var e) ? e.GetDouble() : start;
                double p = w.TryGetProperty("probability", out var pr) ? pr.GetDouble() : 1;
                words.Add(new TimedWord(start, Math.Max(start, end), text.Trim(), p));
            }
        return (words, language);
    }

    /// <summary>Nothing to let go of: the engine keeps its one model for the process.</summary>
    public void Dispose() { }
}

/// <summary>The needle engine's C API (needle.h), loaded from needle/&lt;rid&gt;/ beside the app.</summary>
static unsafe class Needle
{
    public const string Version = "3.1.0";

    static nint library;
    static delegate* unmanaged[Cdecl]<byte*, ulong, int> load;
    static delegate* unmanaged[Cdecl]<float*, int, byte*, byte*, int, byte*, int, int> transcribe;
    static delegate* unmanaged[Cdecl]<byte*> lastError;
    // The model's bytes, which the engine reads in place for as long as the process runs: never moved, never freed.
    static void* model;

    /// <summary>A model is loaded (the engine holds one speech model for the process).</summary>
    public static bool Loaded => model is not null;

    /// <summary>This computer's runtime as the engine's folders name it; null where there's no engine for it.</summary>
    public static string? Rid =>
        (OperatingSystem.IsMacOS() ? "osx-" : OperatingSystem.IsWindows() ? "win-" : null) is { } os
        && RuntimeInformation.ProcessArchitecture switch { Architecture.Arm64 => "arm64", Architecture.X64 => "x64", _ => null } is { } arch
            ? os + arch : null;

    static string FileName => OperatingSystem.IsWindows() ? "libneedle3.dll" : "libneedle3.dylib";

    /// <summary>Where this computer's engine is, or null when the app has none for it.</summary>
    public static string? LibraryPath()
    {
        if (Rid is not { } rid) return null;
        foreach (string dir in new[] { Path.Combine(AppContext.BaseDirectory, "needle", rid), Path.Combine(AppContext.BaseDirectory, "needle") })
            if (System.IO.File.Exists(Path.Combine(dir, FileName))) return Path.Combine(dir, FileName);
        return null;
    }

    /// <summary>Load the engine (once). Throws DllNotFoundException when there's none for this computer.</summary>
    public static void Bind()
    {
        if (library != 0) return;
        string path = LibraryPath() ?? throw new DllNotFoundException($"Study Stash has no Cactus Whistle engine for {RuntimeInformation.OSDescription} on {RuntimeInformation.ProcessArchitecture}.");
        // needle's README says its binary counts usage unless this is 0. The engine library imports nothing that reaches
        // the network (its usage counts are the Python package's, not used here), but it costs nothing to say no.
        Environment.SetEnvironmentVariable("NEEDLE_TELEMETRY", "0");
        nint lib = NativeLibrary.Load(path);
        load = (delegate* unmanaged[Cdecl]<byte*, ulong, int>)NativeLibrary.GetExport(lib, "needle_load");
        transcribe = (delegate* unmanaged[Cdecl]<float*, int, byte*, byte*, int, byte*, int, int>)NativeLibrary.GetExport(lib, "needle_transcribe");
        lastError = (delegate* unmanaged[Cdecl]<byte*>)NativeLibrary.GetExport(lib, "needle_last_error");
        library = lib;
    }

    public static void LoadModel(byte[] cact)
    {
        void* bytes = NativeMemory.AlignedAlloc((nuint)cact.Length, 64);
        cact.AsSpan().CopyTo(new Span<byte>(bytes, cact.Length));
        int r = load((byte*)bytes, (ulong)cact.Length);
        if (r < 0)
        {
            NativeMemory.AlignedFree(bytes);
            throw new InvalidOperationException($"Cactus Whistle's model didn't load: {Error()}");
        }
        model = bytes;
    }

    /// <summary>One pass (at most 30 s of 16 kHz mono) to the engine's JSON, with each word's times.</summary>
    public static string Transcribe(float[] samples, string language, string keywords, byte[] output)
    {
        byte[]? lang = language.Length > 0 ? Encoding.UTF8.GetBytes(language + "\0") : null;
        byte[]? words = keywords.Length > 0 ? Encoding.UTF8.GetBytes(keywords + "\0") : null;
        int r;
        fixed (float* pcm = samples)
        fixed (byte* l = lang)
        fixed (byte* k = words)
        fixed (byte* o = output)
            r = transcribe(pcm, samples.Length, l, k, 1, o, output.Length);
        if (r < 0) throw new InvalidOperationException($"Cactus Whistle couldn't hear it: {Error()}");
        int n = Array.IndexOf(output, (byte)0);
        return Encoding.UTF8.GetString(output, 0, n < 0 ? output.Length : n);
    }

    static string Error()
    {
        byte* e = lastError();
        return e is null ? "no reason given" : Marshal.PtrToStringUTF8((nint)e) ?? "no reason given";
    }
}
