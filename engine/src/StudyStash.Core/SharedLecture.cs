using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using StudyStash.Core.Rich;

namespace StudyStash.Core;

/// <summary>
/// A lecture one student shared with another as a file (<see cref="SyftFile"/>): its title, class, day, notes and
/// transcript, and nothing that says who sent it. It comes into the library as a lecture like any other, with its notes
/// as they were written (no AI writes them again) and its transcript, so search, Ask and Study read it and the student's
/// own AI can rewrite the notes. Its id is made from what it holds, so the same file brought in twice is one lecture,
/// and no other lecture is ever written over.
/// </summary>
public sealed record SharedLecture(string Title, string ClassName, string Date, string Notes, string Transcript)
{
    public const int MaxTitle = 200, MaxClass = 60;
    /// <summary>Notes rarely pass 100 KB, drawings put back included; a three-hour transcript is about 250 KB.</summary>
    public const int MaxNotesChars = 1024 * 1024, MaxTranscriptChars = 4 * 1024 * 1024;

    /// <summary>Who wrote the notes, as the byline says it: someone else, not this library's AI.</summary>
    public const string Writer = "Shared by a friend";

    /// <summary>"shared-" and the first 16 hex of a hash of what it holds.</summary>
    public string Id => "shared-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
        string.Join('\n', Title, Date, Notes.ReplaceLineEndings("\n"), Transcript.ReplaceLineEndings("\n")))))[..16];

    public static string AlreadyHere(string title) => $"\u201c{title}\u201d is already in your library.";

    /// <summary>What the app sends the library: the lecture, and the class it goes in ("" lets the library sort it).</summary>
    public JsonObject Json(string className) => new()
    {
        ["title"] = Title, ["from_class"] = ClassName, ["date"] = Date, ["notes"] = Notes, ["transcript"] = Transcript, ["class"] = className,
    };

    /// <summary>The lecture the app sent, checked as a file is (<see cref="Checked"/>).</summary>
    public static SharedLecture FromJson(JsonObject o) => Checked(Py.AsString(o["title"]), Py.AsString(o["from_class"]), Py.AsString(o["date"]),
        Py.AsString(o["notes"]), Py.AsString(o["transcript"]));

    /// <summary>A lecture with what it needs (a title, notes or a transcript) and no more than a lecture holds; throws an
    /// <see cref="SyftFileException"/> with words for the student otherwise.</summary>
    internal static SharedLecture Checked(string? title, string? className, string? date, string? notes, string? transcript)
    {
        string t = OneLine(title), n = (notes ?? "").Trim('\n', '\r'), s = (transcript ?? "").Trim('\n', '\r');
        if (t.Length == 0) throw new SyftFileException(SyftFile.NotOne);
        if (n.Length > MaxNotesChars || s.Length > MaxTranscriptChars) throw new SyftFileException(SyftFile.TooBig);
        if (n.Trim().Length == 0 && s.Trim().Length == 0) throw new SyftFileException(SyftFile.Empty);
        string day = (date ?? "").Trim();
        if (!DateTimeOffset.TryParse(day, CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) day = "";
        return new SharedLecture(Py.Head(t, MaxTitle), Py.Head(OneLine(className), MaxClass), day, n, s);
    }

    /// <summary>A title or class as plain words on one line: a line break (which would start a heading in the note file),
    /// a control character or an invisible one (a right-to-left override) becomes a space.</summary>
    static string OneLine(string? text) => new string([.. (text ?? "").Select(c =>
        char.IsControl(c) || CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.Format ? ' ' : c)]).Trim();

    /// <summary>The lecture as the library keeps one. The class the friend had it in is its folder, so the library's
    /// own sorting finds a class of that name (or another name of one); its title stays as it came.</summary>
    public Meeting Meeting() => new(Id)
    {
        Title = Title, Date = Date, Folder = ClassName, Transcript = Transcript,
        Raw = new JsonObject { ["source"] = "shared", [Core.Meeting.TitleBy] = Core.Meeting.Kept },
    };

    /// <summary>Lists it in the library in <paramref name="c"/>'s class, its notes as they came. Null when it's there
    /// already (nothing is changed); otherwise its id.</summary>
    public string? Add(Store store, Classification c)
    {
        if (store.Get(Id) is not null) return null;
        c.LectureTitle = Title;
        store.Save(Meeting(), c, Notes, Writer);
        return Id;
    }
}

/// <summary>A file that isn't a lecture shared from Syft Notes, or that can't be taken as one; its message is for the
/// student.</summary>
public sealed class SyftFileException(string message) : Exception(message);

/// <summary>
/// The .syft file a lecture is shared as: a zip holding <c>manifest.json</c> (<c>format</c>, <c>title</c>, <c>class</c>,
/// <c>date</c>, <c>app</c>: the version of Syft Notes that wrote it), <c>notes.md</c> (the Markdown a download makes:
/// <see cref="NoteExport.Lecture"/>, without its transcript), its pictures as <c>notes.assets/diagram-N.svg</c>, and
/// <c>transcript.txt</c> (one "[mm:ss] words" line per stretch, as the library keeps it). No recording, attachments,
/// ids or names. A file from someone else is read with care: nothing is written to disk from it, only those entries
/// may be in it, each no bigger than a lecture's, and it's read only up to that size whatever the zip says.
/// </summary>
public static partial class SyftFile
{
    public const string Ending = ".syft";
    /// <summary>The kind of file a Mac knows it as (the app's Info.plist declares it).</summary>
    public const string TypeId = "com.syftnotes.lecture";
    /// <summary>The version of the format this writes and reads: a file from a later one says to update.</summary>
    public const int Format = 1;
    public const string Manifest = "manifest.json", NotesEntry = "notes.md", TranscriptEntry = "transcript.txt";

    const long MaxFileBytes = 16L * 1024 * 1024;
    const int MaxEntries = 200, MaxManifestBytes = 64 * 1024, MaxPictureBytes = SafeSvg.MaxBytes;
    const long MaxTotalBytes = 24L * 1024 * 1024;

    public const string NotOne = "Syft Notes can't open that file: it isn't a lecture shared from Syft Notes, or it was damaged on the way. Ask for it to be sent again.";
    public const string Unexpected = "Syft Notes didn't open that file: it has things in it that a shared lecture never has. Ask for it to be shared again from Syft Notes.";
    public const string TooBig = "That file is too big to be a lecture shared from Syft Notes.";
    public const string Newer = "That lecture was shared from a newer Syft Notes. Update Syft Notes, then open it again.";
    public const string Empty = "That file has no notes or transcript in it.";
    public const string Gone = "Syft Notes can't find that file any more. It may have been moved or renamed.";

    [GeneratedRegex(@"\Anotes\.assets/diagram-[1-9][0-9]{0,3}\.svg\z", RegexOptions.CultureInvariant)]
    private static partial Regex PictureEntry();

    public static bool Is(string path) => path.EndsWith(Ending, StringComparison.OrdinalIgnoreCase);

    static bool Expected(string name) => name is Manifest or NotesEntry or TranscriptEntry || PictureEntry().IsMatch(name);

    static long Limit(string name) => name switch
    {
        Manifest => MaxManifestBytes,
        NotesEntry => SharedLecture.MaxNotesChars,
        TranscriptEntry => SharedLecture.MaxTranscriptChars,
        _ => MaxPictureBytes,
    };

    /// <summary>The lecture in the file at <paramref name="path"/>; throws a <see cref="SyftFileException"/> with words
    /// for the student when it isn't one (and an <see cref="IOException"/> or <see cref="UnauthorizedAccessException"/>
    /// when it can't be read).</summary>
    public static SharedLecture Read(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists) throw new SyftFileException(Gone);
        if (info.Length > MaxFileBytes) throw new SyftFileException(TooBig);
        using var stream = info.OpenRead();
        return Read(stream);
    }

    public static SharedLecture Read(Stream stream)
    {
        var texts = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
            if (zip.Entries.Count > MaxEntries) throw new SyftFileException(Unexpected);
            long total = 0;
            foreach (var entry in zip.Entries)
            {
                // Names are only ever compared, never made into paths: "../x" or "C:\x" is simply not one of them.
                if (!Expected(entry.FullName) || texts.ContainsKey(entry.FullName)) throw new SyftFileException(Unexpected);
                byte[] bytes = Bounded(entry, Limit(entry.FullName));
                if ((total += bytes.Length) > MaxTotalBytes) throw new SyftFileException(TooBig);
                texts[entry.FullName] = Strict.GetString(bytes);
            }
        }
        catch (Exception e) when (e is InvalidDataException or DecoderFallbackException)
        {
            throw new SyftFileException(NotOne);
        }
        if (!texts.TryGetValue(Manifest, out string? manifestText) || !texts.TryGetValue(NotesEntry, out string? notes))
            throw new SyftFileException(NotOne);
        int format;
        string? title, className, date;
        try
        {
            var manifest = JsonNode.Parse(manifestText) as JsonObject ?? throw new SyftFileException(NotOne);
            format = manifest["format"] is JsonValue v && v.TryGetValue(out int f) ? f : 0;
            (title, className, date) = (Py.AsString(manifest["title"]), Py.AsString(manifest["class"]), Py.AsString(manifest["date"]));
        }
        // A key said twice ({"format": 1, "format": 1}) is an ArgumentException once it's read.
        catch (Exception e) when (e is System.Text.Json.JsonException or ArgumentException)
        {
            throw new SyftFileException(NotOne);
        }
        if (format > Format) throw new SyftFileException(Newer);
        if (format != Format) throw new SyftFileException(NotOne);
        var pictures = texts.Where(kv => PictureEntry().IsMatch(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value);
        return SharedLecture.Checked(title, className, date,
            NoteExport.Undrawn(notes, pictures, SharedLecture.MaxNotesChars) ?? throw new SyftFileException(TooBig), texts.GetValueOrDefault(TranscriptEntry));
    }

    /// <summary>"Cell Biology - Lecture 4.syft": the name the Save dialog suggests for a lecture.</summary>
    public static string SaveName(string? className, string title) =>
        NoteExport.SaveName(string.Join(" - ", new[] { className, title }.Where(s => !string.IsNullOrWhiteSpace(s) && s != Configs.Unsorted)), Ending);

    /// <summary>Writes <paramref name="lecture"/> (the library's JSON for it) as a .syft file to <paramref name="to"/>:
    /// its notes as a download makes them, their pictures, its transcript, and the manifest. <paramref name="app"/> is
    /// this Syft Notes' version. Nothing else of it goes in: no recording, attachments, ids, names, or what the student
    /// typed themselves. Throws a <see cref="SyftFileException"/> (<see cref="NothingToSend"/>) for a lecture with no notes and no
    /// transcript.</summary>
    public static void Write(Stream to, JsonObject lecture, string app)
    {
        // The AI's notes only: what the student typed in the lecture is theirs, and a download with no notes falls back on it.
        var notesOnly = lecture.DeepClone().AsObject();
        notesOnly["typed"] = "";
        string transcript = Py.AsString(lecture["transcript"]) ?? "";
        if (NoteExport.Shown(notesOnly).Trim().Length == 0 && transcript.Trim().Length == 0) throw new SyftFileException(NothingToSend);
        // A Mermaid chart needs no picture: Syft Notes draws it from its source.
        var export = NoteExport.Lecture(notesOnly, transcript: false, _ => null, NotesEntry);
        string day = DateTimeOffset.TryParse(Py.AsString(lecture["date"]), CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "";
        using var zip = new ZipArchive(to, ZipArchiveMode.Create, leaveOpen: true);
        void Add(string name, string text)
        {
            var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
            // The same lecture makes the same file, and it says nothing of when it was made.
            entry.LastWriteTime = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
            using var w = new StreamWriter(entry.Open(), new UTF8Encoding(false));
            w.Write(text);
        }
        Add(Manifest, PyJson.Dumps(new JsonObject
        {
            ["format"] = Format, ["title"] = Py.AsString(lecture["title"]) ?? "", ["class"] = Py.AsString(lecture["class"]) ?? "",
            ["date"] = day, ["app"] = app,
        }, indent: 2));
        Add(NotesEntry, export.Markdown);
        foreach (var asset in export.Assets) Add(asset.RelativePath, asset.Text);
        if (transcript.Length > 0) Add(TranscriptEntry, transcript);
    }

    public const string NothingToSend = "This lecture has no notes or transcript yet, so there's nothing to send.";

    static readonly UTF8Encoding Strict = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>An entry's bytes, read no further than <paramref name="limit"/> (the size a zip says it has can lie).</summary>
    static byte[] Bounded(ZipArchiveEntry entry, long limit)
    {
        if (entry.Length > limit) throw new SyftFileException(TooBig);
        using var from = entry.Open();
        using var to = new MemoryStream();
        var buffer = new byte[81920];
        for (int n; (n = from.Read(buffer, 0, buffer.Length)) > 0;)
        {
            if (to.Length + n > limit) throw new SyftFileException(TooBig);
            to.Write(buffer, 0, n);
        }
        return to.ToArray();
    }
}
