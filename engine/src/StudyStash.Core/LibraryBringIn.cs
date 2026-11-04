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
/// Deleted here (in the trash, or deleted for good: <see cref="Store.DeletedIds"/>): it stays deleted.</item>
/// <item>An attached file a listed lecture is missing here is copied from the other folder; the other folder's attached
/// files this one has no copy of come in with their class.</item>
/// <item>Every file is written whole or not at all (a hidden file renamed into place), so a crash part-way leaves no
/// half a lecture to be taken for the real one next time.</item>
/// </list>
/// </summary>
public static class LibraryBringIn
{
    /// <summary>GET: the bring-in job running or last run (<see cref="BringInJob"/>); POST: start one.</summary>
    public const string Route = "/api/v2/folders/job";

    /// <summary>What bringing in <paramref name="from"/> would do, changing nothing: for a library whose folder is
    /// <paramref name="into"/> (this library's, or one the student is about to use).</summary>
    public static BringInCount Plan(Store store, Config cfg, string into, string from, string userHome, Action<int, int>? progress = null) =>
        new Run(store, cfg, into, from, userHome, Mode.Plan, progress).Go();

    /// <summary>Brings in <paramref name="from"/>'s notes (see the class), into this library's folder.</summary>
    public static BringInCount Apply(Store store, Config cfg, string from, string userHome, Action<int, int>? progress = null) =>
        new Run(store, cfg, store.PoolDir, from, userHome, Mode.Apply, progress).Go();

    /// <summary>Only the lectures this library lists from <paramref name="from"/> (the folder it used before the
    /// student picked this one): their note files and attached files copied into this folder, and their rows pointed at
    /// the copies, so nothing the library lists is still read or written in the other folder. The other folder's own
    /// lectures stay there.</summary>
    public static BringInCount Refile(Store store, Config cfg, string from, string userHome, Action<int, int>? progress = null) =>
        new Run(store, cfg, store.PoolDir, from, userHome, Mode.Refile, progress).Go();

    /// <summary>Lists the note files in this library's own folder that state.db doesn't (a folder picked that the library
    /// used before its state.db was made afresh), where they are. How many.</summary>
    public static int ListOwn(Store store, Config cfg)
    {
        int listed = 0;
        var gone = store.DeletedIds();
        foreach (var file in NoteFiles(store.PoolDir))
            if (Read(file) is { } note && store.Get(note.Id) is null && !gone.Contains(note.Id))
            {
                Enlist(store, cfg, note, note.Text, note.Path, apply: true);
                listed++;
            }
        return listed;
    }

    enum Mode { Plan, Apply, Refile }

    sealed class Run(Store store, Config cfg, string into, string from, string userHome, Mode mode, Action<int, int>? progress)
    {
        readonly bool apply = mode != Mode.Plan;
        int fresh, here, both, deleted, files;
        /// <summary>The classes this library hasn't got that come in (each once, however many lectures bring it).</summary>
        readonly HashSet<string> newClasses = new(StringComparer.OrdinalIgnoreCase);
        int done, total;
        readonly HashSet<string> gone = store.DeletedIds();
        /// <summary>A dry run's memory of what it would have listed: id → its note file's text.</summary>
        readonly Dictionary<string, string> planned = [];
        /// <summary>The other folder's note files already copied in for the lectures listed from there.</summary>
        readonly HashSet<string> copied = new(LibraryFolders.Comparer);
        /// <summary>The other folder's attached files already copied in for attachments listed here.</summary>
        readonly HashSet<string> copiedFiles = new(LibraryFolders.Comparer);
        readonly Inside inside = new(from);

        void Step() => progress?.Invoke(++done, total);

        public BringInCount Go()
        {
            if (Under(into, from) || Under(from, into)) return Count();
            // A folder gone since still has its rows pointed here (their files went with it), so none is left pointing there.
            var own = mode == Mode.Refile ? [] : NoteFiles(into);
            var rows = store.ListNotes().Where(r => !string.IsNullOrEmpty(r.MdPath) && inside.Rel(r.MdPath) is not null).ToList();
            var theirs = mode == Mode.Refile ? [] : NoteFiles(from);
            var attached = mode == Mode.Refile ? [] : AttachedFiles(from);
            var listedAttachments = store.ListAttachments();
            total = own.Count + rows.Count + theirs.Count + listedAttachments.Count + attached.Count;
            progress?.Invoke(0, total);
            // This folder's own unlisted lectures first, so one in both folders is found here.
            foreach (var file in own)
            {
                if (Read(file) is { } note && store.Get(note.Id) is null && !gone.Contains(note.Id))
                {
                    if (apply) Enlist(store, cfg, note, note.Text, note.Path, apply: true, newClasses);
                    else planned.TryAdd(note.Id, note.Text);
                }
                Step();
            }
            // Lectures listed here whose file is still in the other folder.
            foreach (var row in rows)
            {
                Step();
                string rel = inside.Rel(row.MdPath!)!;
                if (!File.Exists(row.MdPath))
                {
                    // Its file is gone from there: the row looks for it here from now on (the lecture is in its row), at a
                    // path no other lecture's file is at.
                    if (apply) store.Refile(row.Id, FreeFor(Path.Combine(into, rel), row.Id));
                    continue;
                }
                string text = Text(row.MdPath!);
                if (apply) store.Refile(row.Id, CopyIn(text, Path.Combine(into, rel)));
                fresh++;
                planned[row.Id] = text;
                copied.Add(Path.GetFullPath(Path.Combine(from, rel)));
            }
            // The other folder's own lectures.
            foreach (var file in theirs)
            {
                Step();
                Theirs(file);
            }
            files = ListedAttachments(listedAttachments) + OtherAttachments(attached);
            // What the student wrote by hand in the other folder (captures waiting in the Inbox, notes filed into a class's
            // Notes): the folder in use is where they're read, so they come too. The Canvas mirror is fetched again.
            var loose = LooseFiles(from);
            total += loose.Count;
            foreach (string rel in loose)
            {
                Step();
                if (CopyLoose(store, rel, Path.Combine(from, rel), Path.Combine(into, rel), apply)) files++;
            }
            // A lecture in the trash that was filed in the other folder comes back, if restored, into this one.
            if (apply) store.RepointTrash(path => inside.Rel(path) is { } rel ? Path.Combine(into, rel) : null);
            return Count();
        }

        void Theirs(string file)
        {
            if (Read(file) is not { } note) return;
            if (gone.Contains(note.Id))
            {
                deleted++;
                return;
            }
            if (copied.Contains(Path.GetFullPath(note.Path))) return; // counted above
            var row = store.Get(note.Id);
            string? mine = planned.TryGetValue(note.Id, out string? p) ? p
                : row?.MdPath is { Length: > 0 } path && File.Exists(path) ? Text(path) : null;
            if (row is not null && mine is null && string.IsNullOrEmpty(row.MdPath))
            {
                here++; // still being written here: its notes come from this library
                return;
            }
            if (mine is null)
            {
                if (row is null) Enlist(store, cfg, note, note.Text, null, apply, newClasses);
                else if (apply) store.Refile(row.Id, CopyIn(note.Text, Path.Combine(into, Path.GetRelativePath(from, note.Path))));
                fresh++;
                planned[note.Id] = note.Text;
                return;
            }
            if (Same(mine, note.Text))
            {
                here++;
                return;
            }
            // Different in each folder: this one's stays, the other's comes in beside it.
            string copyId = $"{note.Id}-{Hash(note.Text)}";
            if (store.Get(copyId) is not null || planned.ContainsKey(copyId) || gone.Contains(copyId))
            {
                here++;
                return;
            }
            var copy = note.Beside(copyId, Short(LibraryFolders.Where(from, userHome)));
            Enlist(store, cfg, copy, copy.Text, null, apply, newClasses);
            planned[copyId] = copy.Text;
            both++;
        }

        BringInCount Count() => new(from, fresh, here, both, deleted, newClasses.Count, files);

        /// <summary>Attached files a listed attachment is missing here, copied from the same place in the other folder
        /// (an attachment state.db already lists, whose file the folder the library used before has).</summary>
        int ListedAttachments(List<Attachment> listed)
        {
            int n = 0;
            foreach (var a in listed)
            {
                Step();
                string mine = store.AttachmentPath(a);
                if (!Under(mine, store.PoolDir)) continue;
                string target = Path.Combine(into, Path.GetRelativePath(store.PoolDir, mine));
                string other = Path.Combine(from, Path.GetRelativePath(store.PoolDir, mine));
                if (!File.Exists(other)) continue;
                copiedFiles.Add(Path.GetFullPath(other));
                if (File.Exists(target))
                {
                    if (SameBytes(target, other)) continue;
                    // A different file of that name here (this folder's own): this attachment's comes in beside it, and
                    // the attachment is pointed at it, so it never opens or removes the other.
                    string file = Core.Attachments.FreeName(Py.Parent(target), Path.GetFileName(target));
                    if (apply)
                    {
                        Store.PutWhole(Path.Combine(Py.Parent(target), file), temp => File.Copy(other, temp), overwrite: false);
                        store.SetAttachmentFile(a.Id, file);
                    }
                }
                else if (apply) Store.PutWhole(target, temp => File.Copy(other, temp), overwrite: false);
                n++;
            }
            return n;
        }

        /// <summary>The other folder's attached files this folder has no copy of: copied into the same class's
        /// Attachments here (a free name when theirs is taken by a different file) and listed with the class, their words
        /// read again here. Which lecture each belonged to was in the other folder's state.db, not in the folder.</summary>
        int OtherAttachments(List<string> attached)
        {
            int n = 0;
            foreach (string other in attached)
            {
                Step();
                if (copiedFiles.Contains(Path.GetFullPath(other))) continue;
                string clsFolder = Path.GetFileName(Py.Parent(Py.Parent(other)));
                string dir = Path.Combine(into, clsFolder, Store.AttachmentsFolder);
                string sha;
                long size;
                try
                {
                    size = new FileInfo(other).Length;
                    sha = Sha(other);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    continue;
                }
                if (HasCopy(dir, size, sha)) continue;
                // The same file attached to two classes is one attachment in each: the id is the class's and the file's.
                string id = "folder-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(clsFolder.ToLowerInvariant() + "/" + sha)))[..24];
                if (gone.Contains(id)) continue; // removed by the student once: it stays removed
                if (store.GetAttachment(id) is not null || planned.ContainsKey(id)) continue;
                planned[id] = "";
                n++;
                if (!apply) continue;
                string cls = ClassFor(clsFolder);
                string file = Core.Attachments.FreeName(store.AttachmentsDir(cls), Path.GetFileName(other));
                string target = Path.Combine(store.AttachmentsDir(cls), file);
                Store.PutWhole(target, temp => File.Copy(other, temp), overwrite: false);
                byte[] head = new byte[64];
                int read;
                using (var stream = File.OpenRead(target)) read = stream.Read(head);
                store.AddAttachment(new Attachment(id, Path.GetFileName(other), file, cls, null, size,
                    Core.Attachments.Sniff(head.AsSpan(0, read), file), File.GetLastWriteTimeUtc(other).ToString("o", CultureInfo.InvariantCulture)));
            }
            return n;
        }

        /// <summary>The class a folder of the other library is: one of this library's whose folder has that name, else
        /// the folder's own name.</summary>
        string ClassFor(string folder) =>
            cfg.Classes.FirstOrDefault(c => Notes.FolderName(c.Name).Equals(folder, StringComparison.OrdinalIgnoreCase))?.Name
            ?? store.ClassesSummary().Select(c => c.ClassName).FirstOrDefault(c => Notes.FolderName(c).Equals(folder, StringComparison.OrdinalIgnoreCase))
            ?? folder;

        /// <summary>A note file's copy in this folder at <paramref name="want"/>, or beside it when that's a different
        /// file: where it is. Written whole or not at all.</summary>
        static string CopyIn(string text, string want)
        {
            string path = want;
            for (int i = 2; File.Exists(path); i++)
            {
                if (Same(Text(path), text)) return path;
                path = Path.Combine(Py.Parent(want), $"{Path.GetFileNameWithoutExtension(want)} ({i}).md");
            }
            Store.WriteWhole(path, text);
            return path;
        }
    }

    /// <summary>Every attached file in a library folder (<c>&lt;Class&gt;/Attachments/*</c>), hidden ones left out.</summary>
    static List<string> AttachedFiles(string root)
    {
        var found = new List<string>();
        var options = new EnumerationOptions { IgnoreInaccessible = true };
        try
        {
            foreach (var cls in new DirectoryInfo(root).EnumerateDirectories("*", options).OrderBy(d => d.Name, StringComparer.Ordinal))
            {
                if (cls.Name.StartsWith('.') || cls.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
                var dir = new DirectoryInfo(Path.Combine(cls.FullName, Store.AttachmentsFolder));
                if (!dir.Exists) continue;
                found.AddRange(dir.EnumerateFiles("*", options).Where(f => !f.Name.StartsWith('.')).Select(f => f.FullName).Order(StringComparer.Ordinal));
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Gone, or turned away part-way: what was found stands.
        }
        return found;
    }

    /// <summary>The files a student wrote by hand in a library folder, relative to it: the Inbox's captures, and the
    /// notes filed into each class's Notes folder. Hidden ones left out.</summary>
    static List<string> LooseFiles(string root)
    {
        var found = new List<string>();
        var options = new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = true };
        try
        {
            var dirs = new List<string> { Path.Combine(root, "Inbox") };
            dirs.AddRange(new DirectoryInfo(root).EnumerateDirectories("*", new EnumerationOptions { IgnoreInaccessible = true })
                .Where(d => !d.Name.StartsWith('.') && !d.Attributes.HasFlag(FileAttributes.ReparsePoint))
                .Select(d => Path.Combine(d.FullName, "Notes")));
            foreach (string dir in dirs.Where(Directory.Exists))
                found.AddRange(new DirectoryInfo(dir).EnumerateFiles("*", options)
                    .Where(f => !Path.GetRelativePath(root, f.FullName).Split(Path.DirectorySeparatorChar).Any(part => part.StartsWith('.')))
                    .Select(f => Path.GetRelativePath(root, f.FullName)));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Gone, or turned away part-way: what was found stands.
        }
        found.Sort(StringComparer.Ordinal);
        return found;
    }

    /// <summary>Copies <paramref name="other"/> to <paramref name="want"/>, or beside it as "name (2).ext" when that's a
    /// different file; nothing when a copy is there already, or this file (this path, these bytes) came over once
    /// before: a capture filed since, or a note the student deleted or edited, stays as the student left it. Whether it
    /// copied (or, not <paramref name="apply"/>ing, would).</summary>
    static bool CopyLoose(Store store, string rel, string other, string want, bool apply)
    {
        string sha;
        long size;
        try
        {
            size = new FileInfo(other).Length;
            sha = Sha(other);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
        string key = "bring-in:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(rel.Replace('\\', '/').ToLowerInvariant() + "/" + sha)))[..32];
        if (store.GetState(key) is not null) return false;
        string stem = Path.GetFileNameWithoutExtension(want), ext = Path.GetExtension(want), dir = Py.Parent(want);
        string path = want;
        for (int i = 2; File.Exists(path); i++)
        {
            try
            {
                if (new FileInfo(path).Length == size && Sha(path) == sha)
                {
                    if (apply) store.SetState(key, path);
                    return false;
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }
            path = Path.Combine(dir, $"{stem} ({i}){ext}");
        }
        if (!apply) return true;
        Store.PutWhole(path, temp => File.Copy(other, temp), overwrite: false);
        store.SetState(key, path);
        return true;
    }

    /// <summary><paramref name="want"/> when nothing is there or the note file there is lecture <paramref name="id"/>'s;
    /// otherwise the first free "name (2).md" beside it.</summary>
    static string FreeFor(string want, string id)
    {
        string path = want;
        for (int i = 2; File.Exists(path) && Read(path)?.Id != id; i++)
            path = Path.Combine(Py.Parent(want), $"{Path.GetFileNameWithoutExtension(want)} ({i}).md");
        return path;
    }

    static bool SameBytes(string a, string b)
    {
        try
        {
            return new FileInfo(a).Length == new FileInfo(b).Length && Sha(a) == Sha(b);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Whether <paramref name="dir"/> has a file of this size and SHA-256 already.</summary>
    static bool HasCopy(string dir, long size, string sha)
    {
        if (!Directory.Exists(dir)) return false;
        foreach (var f in new DirectoryInfo(dir).EnumerateFiles())
        {
            try
            {
                if (f.Length == size && Sha(f.FullName) == sha) return true;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }
        }
        return false;
    }

    static string Sha(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 81920);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    /// <summary>Lists <paramref name="note"/> in its class (added to the library when it hasn't got it) with
    /// <paramref name="text"/> as its file: at <paramref name="at"/>, or where its class, day and title put it in this
    /// library's folder (a free name when that's taken). How many classes it added (0 or 1).</summary>
    static int Enlist(Store store, Config cfg, NoteFile note, string text, string? at, bool apply, HashSet<string>? newClasses = null)
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
                newClasses?.Add(cls);
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
    /// <summary>How many lectures, listed or in the trash, still have their file inside <paramref name="dir"/> (reached by
    /// any path, links followed): what a switch away from it has to bring to 0.</summary>
    public static int PointingInto(Store store, string dir)
    {
        var inside = new Inside(dir);
        return store.NoteFiles().Count(p => inside.Rel(p) is not null) + store.TrashPaths().Count(p => inside.Rel(p) is not null);
    }

    /// <summary>Paths inside a folder however they're written: as written, or with every link followed (a row written
    /// as ~/Notes link/Study Stash/… is inside ~/OneDrive/Documents/Study Stash when the link goes there).</summary>
    sealed class Inside(string dir)
    {
        readonly string real = LibraryFolders.Real(dir);
        readonly Dictionary<string, string> parents = new(LibraryFolders.Comparer);

        /// <summary><paramref name="path"/> relative to the folder, or null when it isn't inside it.</summary>
        public string? Rel(string path)
        {
            if (Under(path, dir)) return Path.GetRelativePath(dir, path);
            string parent = Py.Parent(path);
            if (!parents.TryGetValue(parent, out string? realParent)) parents[parent] = realParent = LibraryFolders.Real(parent);
            string full = Path.Combine(realParent, Path.GetFileName(path));
            return Under(full, real) ? Path.GetRelativePath(real, full) : null;
        }
    }

    static bool Under(string path, string dir)
    {
        string p = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string d = Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var cmp = OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        return p.Equals(d, cmp) || p.StartsWith(d + Path.DirectorySeparatorChar, cmp);
    }
}

/// <summary>
/// One run of <see cref="LibraryBringIn"/> in the library, which the app asks after while it goes (a big OneDrive folder
/// takes longer than one request may): what it is (<see cref="Look"/>, or <see cref="Bring"/>), how far it has got, and
/// once it ends, what each folder did or would do, or why it stopped. The library runs one at a time: asking for
/// another while one runs gets the one running, which says whose it is (<see cref="Asked"/>), so the app waits for it to
/// end and asks again rather than taking another's counts for its own.
/// </summary>
public sealed class BringInJob(string kind, string asked)
{
    /// <summary>What bringing in would do (nothing changes).</summary>
    public const string Look = "look";
    /// <summary>The folder's own unlisted lectures listed, then each folder brought in (or only its listed lectures'
    /// files copied in: <see cref="LibraryBringIn.Refile"/>).</summary>
    public const string Bring = "bring";

    readonly Lock gate = new();
    int done, total;
    bool running = true;
    string? error;
    readonly List<BringInCount> results = [];

    public string Id { get; } = Guid.NewGuid().ToString("N");
    public string Kind { get; } = kind;
    /// <summary>The asker's own id for the job it wanted.</summary>
    public string Asked { get; } = asked;
    public bool Running { get { lock (gate) return running; } }

    public void Progress(int doneNow, int totalNow)
    {
        lock (gate) (done, total) = (doneNow, totalNow);
    }

    public void Add(BringInCount result)
    {
        lock (gate) results.Add(result);
    }

    /// <summary>It's over: <paramref name="why"/> says it stopped part-way (for the log; the app says it in its words).</summary>
    public void End(string? why = null)
    {
        lock (gate) (running, error) = (false, why);
    }

    public JsonObject Json()
    {
        lock (gate)
            return new JsonObject
            {
                ["id"] = Id, ["kind"] = Kind, ["asked"] = Asked, ["running"] = running, ["done"] = done, ["total"] = total, ["error"] = error,
                ["results"] = new JsonArray([.. results.Select(r => (JsonNode)r.Json())]),
            };
    }
}

/// <summary>A <see cref="BringInJob"/> as the app reads it.</summary>
public sealed record BringInStatus(string Id, string Kind, string Asked, bool Running, int Done, int Total, string? Error, List<BringInCount> Results)
{
    public static BringInStatus? FromJson(JsonObject? o) => o?["id"] is null ? null : new(
        Py.AsString(o["id"]) ?? "", Py.AsString(o["kind"]) ?? "", Py.AsString(o["asked"]) ?? "", o["running"] is JsonValue r && r.TryGetValue(out bool b) && b,
        o["done"] is JsonValue d && d.TryGetValue(out int dn) ? dn : 0, o["total"] is JsonValue t && t.TryGetValue(out int tn) ? tn : 0,
        Py.AsString(o["error"]), [.. (o["results"] as JsonArray ?? []).OfType<JsonObject>().Select(BringInCount.FromJson)]);
}
