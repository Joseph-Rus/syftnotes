using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace StudyStash.Core;

/// <summary>What bringing in one folder's notes does (or did): lectures that come in (or came), ones that are here
/// already, ones that differ between the two folders (both kept: the other folder's comes in beside this one's as
/// "title (from …)"), ones the student deleted here (they stay deleted), classes added, and attached files copied.</summary>
public sealed record BringInCount(string From, int New, int Here, int Both, int Deleted, int Classes, int Files)
{
    public JsonObject Json() => new()
    {
        ["from"] = From, ["new"] = New, ["here"] = Here, ["both"] = Both, ["deleted"] = Deleted, ["classes"] = Classes, ["files"] = Files,
    };

    public static BringInCount FromJson(JsonObject o) => new(
        Py.AsString(o["from"]) ?? "", Int(o["new"]), Int(o["here"]), Int(o["both"]), Int(o["deleted"]), Int(o["classes"]), Int(o["files"]));

    static int Int(JsonNode? n) => n is JsonValue v && v.TryGetValue(out int i) ? i : 0;
}

/// <summary>
/// Bringing the notes from another library folder on this computer into this library's own (when a student has two:
/// <see cref="LibraryFolders"/>). Modelled on Lightroom's "Import from Another Catalog" and <see cref="LibraryMove"/>:
/// the library's folder is the one main library, and the other folder is only read, never changed, moved or deleted.
/// So it's a copy, and doing it again brings nothing twice.
/// <para>What lives where decides how. A lecture is listed from state.db (beside config.toml, not in the folder), whose
/// row says where its note file is; the folder holds the note files (<c>&lt;Class&gt;/&lt;day&gt; &lt;title&gt;.md</c>,
/// each with the lecture's id in its front matter) and the class's attached files (<c>&lt;Class&gt;/Attachments</c>).
/// Recordings and chats are beside config.toml, so a folder never has them. So:</para>
/// <list type="bullet">
/// <item>A note file in this library's own folder that state.db doesn't list (a folder last used before state.db was
/// made afresh) is listed where it is.</item>
/// <item>A lecture state.db lists whose file is in the other folder (the folder the library used before) gets a copy of
/// the file in this folder, and the row points at the copy.</item>
/// <item>A note file in the other folder is matched by its id. Not listed here: it's copied in and listed in its class
/// (a class this library hasn't got is added; "Thermodynamics" in both folders is one class). Listed, and the same:
/// nothing to do. Listed, and different: the lecture here stays as it is, and the other folder's version comes in beside
/// it as a lecture of its own, "title (from OneDrive – Personal)", the way a sync service keeps a conflicted copy.
/// Deleted here (in the trash, or gone this month): it stays deleted.</item>
/// <item>An attached file a listed lecture is missing here is copied from the other folder.</item>
/// </list>
/// </summary>
public static class LibraryBringIn
{
    public const string Route = "/api/v2/folders";
    public const string BringInRoute = "/api/v2/folders/bring-in";

    /// <summary>What bringing in <paramref name="from"/> would do, changing nothing: for a library whose folder is
    /// <paramref name="into"/> (this library's, or one the student is about to use).</summary>
    public static BringInCount Plan(Store store, Config cfg, string into, string from, string userHome) =>
        new Run(store, cfg, into, from, userHome, apply: false).Go();

    /// <summary>Brings in <paramref name="from"/>'s notes (see the class), into this library's folder.</summary>
    public static BringInCount Apply(Store store, Config cfg, string from, string userHome) =>
        new Run(store, cfg, store.PoolDir, from, userHome, apply: true).Go();

    /// <summary>Lists the note files in this library's own folder that state.db doesn't (a folder picked that the library
    /// used before its state.db was made afresh), where they are. How many.</summary>
    public static int ListOwn(Store store, Config cfg)
    {
        int listed = 0;
        var gone = Deleted(store);
        foreach (var file in NoteFiles(store.PoolDir))
            if (Read(file) is { } note && store.Get(note.Id) is null && !gone.Contains(note.Id))
            {
                Enlist(store, cfg, note, note.Text, note.Path, apply: true);
                listed++;
            }
        return listed;
    }

    sealed class Run(Store store, Config cfg, string into, string from, string userHome, bool apply)
    {
        int fresh, here, both, deleted, classes, files;
        readonly HashSet<string> gone = Deleted(store);
        /// <summary>A dry run's memory of what it would have listed: id → its note file's text.</summary>
        readonly Dictionary<string, string> planned = [];
        /// <summary>The other folder's note files already copied in for the lectures listed from there.</summary>
        readonly HashSet<string> copied = new(LibraryFolders.Comparer);

        public BringInCount Go()
        {
            if (Under(into, from) || Under(from, into) || !Directory.Exists(from)) return Count();
            // This folder's own unlisted lectures first, so one in both folders is found here.
            foreach (var file in NoteFiles(into))
                if (Read(file) is { } note && store.Get(note.Id) is null && !gone.Contains(note.Id))
                {
                    if (apply) classes += Enlist(store, cfg, note, note.Text, note.Path, apply: true);
                    else planned.TryAdd(note.Id, note.Text);
                }
            // Lectures listed here whose file is still in the other folder.
            foreach (var row in store.ListNotes())
            {
                if (string.IsNullOrEmpty(row.MdPath) || !Under(row.MdPath, from)) continue;
                if (!File.Exists(row.MdPath)) continue;
                string text = Text(row.MdPath);
                string rel = Path.GetRelativePath(from, row.MdPath);
                if (apply) store.Refile(row.Id, CopyIn(text, Path.Combine(into, rel)));
                fresh++;
                planned[row.Id] = text;
                copied.Add(Path.GetFullPath(row.MdPath));
            }
            // The other folder's own lectures.
            foreach (var file in NoteFiles(from))
            {
                if (Read(file) is not { } note) continue;
                if (gone.Contains(note.Id))
                {
                    deleted++;
                    continue;
                }
                if (copied.Contains(Path.GetFullPath(note.Path))) continue; // counted above
                var row = store.Get(note.Id);
                string? mine = planned.TryGetValue(note.Id, out string? p) ? p
                    : row?.MdPath is { Length: > 0 } path && File.Exists(path) ? Text(path) : null;
                if (row is not null && mine is null && string.IsNullOrEmpty(row.MdPath))
                {
                    here++; // still being written here: its notes come from this library
                    continue;
                }
                if (mine is null)
                {
                    if (row is null) classes += Enlist(store, cfg, note, note.Text, null, apply);
                    else if (apply) store.Refile(row.Id, CopyIn(note.Text, Path.Combine(into, Path.GetRelativePath(from, note.Path))));
                    fresh++;
                    planned[note.Id] = note.Text;
                    continue;
                }
                if (Same(mine, note.Text))
                {
                    here++;
                    continue;
                }
                // Different in each folder: this one's stays, the other's comes in beside it.
                string copyId = $"{note.Id}-{Hash(note.Text)}";
                if (store.Get(copyId) is not null || planned.ContainsKey(copyId) || gone.Contains(copyId))
                {
                    here++;
                    continue;
                }
                var copy = note.Beside(copyId, Short(LibraryFolders.Where(from, userHome)));
                classes += Enlist(store, cfg, copy, copy.Text, null, apply);
                planned[copyId] = copy.Text;
                both++;
            }
            files = Attachments();
            return Count();
        }

        BringInCount Count() => new(from, fresh, here, both, deleted, classes, files);

        /// <summary>Attached files a lecture here is missing, copied from the same place in the other folder.</summary>
        int Attachments()
        {
            int n = 0;
            foreach (var a in store.ListAttachments())
            {
                string mine = store.AttachmentPath(a);
                if (!Under(mine, store.PoolDir)) continue;
                string target = Path.Combine(into, Path.GetRelativePath(store.PoolDir, mine));
                string other = Path.Combine(from, Path.GetRelativePath(store.PoolDir, mine));
                if (File.Exists(target) || !File.Exists(other)) continue;
                if (apply)
                {
                    Directory.CreateDirectory(Py.Parent(target));
                    File.Copy(other, target, overwrite: false);
                }
                n++;
            }
            return n;
        }

        /// <summary>A note file's copy in this folder at <paramref name="want"/>, or beside it when that's a different
        /// file: where it is.</summary>
        static string CopyIn(string text, string want)
        {
            string path = want;
            for (int i = 2; File.Exists(path); i++)
            {
                if (Same(Text(path), text)) return path;
                path = Path.Combine(Py.Parent(want), $"{Path.GetFileNameWithoutExtension(want)} ({i}).md");
            }
            Directory.CreateDirectory(Py.Parent(path));
            File.WriteAllText(path, text, new UTF8Encoding(false));
            return path;
        }
    }

    /// <summary>Lists <paramref name="note"/> in its class (added to the library when it hasn't got it) with
    /// <paramref name="text"/> as its file: at <paramref name="at"/>, or where its class, day and title put it in this
    /// library's folder (a free name when that's taken). How many classes it added (0 or 1).</summary>
    static int Enlist(Store store, Config cfg, NoteFile note, string text, string? at, bool apply)
    {
        string cls = note.Class;
        int added = 0;
        if (!cls.Equals(Configs.Unsorted, StringComparison.OrdinalIgnoreCase))
        {
            var known = cfg.Classes.FirstOrDefault(c => c.Name.Equals(cls, StringComparison.OrdinalIgnoreCase)
                || c.Aliases.Contains(cls, StringComparer.OrdinalIgnoreCase));
            if (known is not null) cls = known.Name;
            else if (cls.Length is > 0 and <= 60)
            {
                added = 1;
                if (apply)
                {
                    cfg.Classes.Add(new ClassDef(cls, [], ""));
                    Configs.Save(cfg);
                }
            }
        }
        else cls = Configs.Unsorted;
        if (!apply) return added;
        var m = note.Meeting();
        if (at is null)
        {
            string name = $"{Notes.DatePrefix(m.Date)} {Notes.Slugify(m.Title)}";
            string dir = store.ClassDir(cls);
            at = Path.Combine(dir, name + ".md");
            for (int i = 2; File.Exists(at); i++) at = Path.Combine(dir, $"{name} ({i}).md");
        }
        store.Save(m, new Classification(cls, note.Confidence, note.By, note.LectureTitle, note.Topics), note.Summary, note.SummaryBy,
            at: at, keepText: text);
        return added;
    }

    /// <summary>The lectures deleted here: in the trash, or gone for good this month.</summary>
    static HashSet<string> Deleted(Store store) => [.. store.Trashed(), .. store.Gone()];

    /// <summary>Every lecture's note file in a library folder: <c>&lt;Class&gt;/*.md</c>, the Inbox's captured notes and
    /// hidden folders left out (as <see cref="LibraryFolders"/> counts them).</summary>
    static List<string> NoteFiles(string root)
    {
        var found = new List<string>();
        var options = new EnumerationOptions { IgnoreInaccessible = true };
        try
        {
            if (!Directory.Exists(root)) return found;
            foreach (var cls in new DirectoryInfo(root).EnumerateDirectories("*", options).OrderBy(d => d.Name, StringComparer.Ordinal))
            {
                if (cls.Name.StartsWith('.') || cls.Name.Equals("Inbox", StringComparison.OrdinalIgnoreCase)) continue;
                if (cls.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
                found.AddRange(cls.EnumerateFiles("*.md", options).Select(f => f.FullName).Order(StringComparer.Ordinal));
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Gone, or turned away part-way: what was found stands.
        }
        return found;
    }

    /// <summary>A note file as the library writes it, or null for one with no lecture id in it (a file the student
    /// made by hand) or that can't be read.</summary>
    static NoteFile? Read(string path)
    {
        string text;
        try
        {
            text = Text(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
        var front = Front(text.Replace("\r\n", "\n"));
        if (!front.TryGetValue("id", out var idNode) || Py.AsString(idNode) is not { Length: > 0 and <= 200 } id) return null;
        string folderName = Path.GetFileName(Py.Parent(path));
        return new NoteFile(path, text, id, front, Py.AsString(front.GetValueOrDefault("class")) is { Length: > 0 } c ? c : folderName);
    }

    /// <summary>The front matter's <c>key: value</c> lines (<see cref="Notes.Render"/> writes each value as JSON; one
    /// that isn't is kept as text).</summary>
    static Dictionary<string, JsonNode?> Front(string text)
    {
        var front = new Dictionary<string, JsonNode?>();
        if (!text.StartsWith("---\n", StringComparison.Ordinal)) return front;
        foreach (string line in text[4..].Split('\n'))
        {
            if (line == "---") break;
            int colon = line.IndexOf(':');
            if (colon <= 0) continue;
            string key = line[..colon].Trim(), value = line[(colon + 1)..].Trim();
            JsonNode? node;
            try
            {
                node = value.Length > 0 && value[0] is '"' or '[' or '{' ? JsonNode.Parse(value) : JsonValue.Create(value);
            }
            catch (System.Text.Json.JsonException)
            {
                node = JsonValue.Create(value);
            }
            front[key] = node;
        }
        return front;
    }

    /// <param name="Text">The file exactly as it is (a Windows one keeps its "\r\n"): what a copy is written with.</param>
    sealed record NoteFile(string Path, string Text, string Id, Dictionary<string, JsonNode?> Fields, string Class)
    {
        /// <summary>The text with every line ending a "\n", as the library reads a note file.</summary>
        string Plain => Text.Replace("\r\n", "\n");
        string S(string key) => Py.AsString(Fields.GetValueOrDefault(key)) ?? "";
        public string Title => S("title");
        public string LectureTitle => S("lecture_title");
        public List<string> Topics => (Fields.GetValueOrDefault("topics") as JsonArray ?? []).Select(Py.AsString).OfType<string>().ToList();
        public string Summary => Notes.SummaryInFile(Plain) ?? "";
        public string SummaryBy => Summary.Length > 0 ? S("summary_by") : "";

        /// <summary>"human (1.00)": who sorted it, and how sure.</summary>
        public string By => S("classified_by").Split(' ')[0] is { Length: > 0 } by ? by : "none";
        public double Confidence
        {
            get
            {
                string s = S("classified_by");
                int open = s.IndexOf('(');
                return open >= 0 && double.TryParse(s[(open + 1)..].TrimEnd(')'), NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : 0;
            }
        }

        /// <summary>The lecture, as far as its note file tells it: the rest of it (its recording, typed notes from the
        /// laptop) was never in the folder.</summary>
        public Meeting Meeting()
        {
            var m = new Meeting(Id)
            {
                Title = Title.Length > 0 ? Title : System.IO.Path.GetFileNameWithoutExtension(Path),
                Date = S("date"), Owner = S("source"), Folder = S("folder"),
                Attendees = (Fields.GetValueOrDefault("attendees") as JsonArray ?? []).Select(Py.AsString).OfType<string>().ToList(),
                NotesMarkdown = Notes.Section(Plain, "Notes"),
                PrivateNotes = Notes.Section(Plain, "Private notes"),
                Transcript = Notes.Section(Plain, "Transcript"),
            };
            if (LectureTitle.Length > 0 && LectureTitle != m.Title) m.Raw[Core.Meeting.TitleBy] = Core.Meeting.Kept;
            return m;
        }

        /// <summary>This lecture as a lecture of its own beside the one listed here: its own id, and "(from …)" after its
        /// titles, in its file too.</summary>
        public NoteFile Beside(string id, string from)
        {
            string title = $"{Title} (from {from})", lecture = $"{(LectureTitle.Length > 0 ? LectureTitle : Title)} (from {from})";
            // Line by line in the file as it is, each line keeping its own ending.
            var lines = Text.Split('\n');
            bool inFront = lines.Length > 0 && lines[0].TrimEnd('\r') == "---";
            string heading = $"# {(LectureTitle.Length > 0 ? LectureTitle : Title)}";
            for (int i = 1; i < lines.Length; i++)
            {
                string line = lines[i].TrimEnd('\r'), end = lines[i].EndsWith('\r') ? "\r" : "";
                if (inFront && line == "---")
                {
                    inFront = false;
                    continue;
                }
                if (inFront)
                {
                    if (line.StartsWith("id: ", StringComparison.Ordinal)) lines[i] = $"id: {PyJson.Dumps(id)}{end}";
                    else if (line.StartsWith("title: ", StringComparison.Ordinal)) lines[i] = $"title: {PyJson.Dumps(title)}{end}";
                    else if (line.StartsWith("lecture_title: ", StringComparison.Ordinal)) lines[i] = $"lecture_title: {PyJson.Dumps(lecture)}{end}";
                }
                else if (line == heading)
                {
                    lines[i] = $"# {lecture}{end}";
                    break;
                }
            }
            var fields = new Dictionary<string, JsonNode?>(Fields) { ["id"] = id, ["title"] = title, ["lecture_title"] = lecture };
            return new NoteFile(Path, string.Join("\n", lines), id, fields, Class);
        }
    }

    /// <summary>A note file's text exactly as it is (compared with <see cref="Same"/>, which ignores line endings).</summary>
    static string Text(string path) => File.ReadAllText(path, new UTF8Encoding(false));

    static bool Same(string a, string b) => a.Replace("\r\n", "\n").TrimEnd() == b.Replace("\r\n", "\n").TrimEnd();

    static string Hash(string text) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.Replace("\r\n", "\n").TrimEnd())))[..8];

    /// <summary>"OneDrive – Personal › Documents" → "OneDrive – Personal": the first part, for a title.</summary>
    static string Short(string where) => where.Split(" › ")[0];

    /// <summary>Whether <paramref name="path"/> is inside <paramref name="dir"/> (or is it).</summary>
    static bool Under(string path, string dir)
    {
        string p = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string d = Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var cmp = OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        return p.Equals(d, cmp) || p.StartsWith(d + Path.DirectorySeparatorChar, cmp);
    }
}
