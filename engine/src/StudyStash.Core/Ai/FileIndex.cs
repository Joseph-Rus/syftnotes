using System.Net;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace StudyStash.Core.Ai;

/// <summary>A file search found: which folder it's in, the file, its name, and the words around the match (HTML,
/// with the match in &lt;mark&gt;).</summary>
public sealed record FileHit(string Root, string Path, string Title, string Snippet);

/// <summary>
/// Full-text search over everything in the library that isn't a lecture (the Canvas mirror with its PDFs and
/// slides, files the AI wrote, your own notes) and the folders Study Stash may read (SQLite FTS5 in files.db,
/// brought up to date in the background). Markdown, code and notebooks are read as they are; PDFs, Word, PowerPoint
/// (and RTF on a Mac) by <see cref="DocumentText"/>, handwritten and scanned pages too; photos and scans (png, jpg,
/// heic) in the library only, so a folder of holiday photos isn't read through. A private folder by file name only.
/// </summary>
public sealed partial class FileIndex
{
    static readonly HashSet<string> Text = [".md", ".txt", ".py", ".js", ".ts", ".tsx", ".jsx", ".swift", ".java", ".kt", ".c", ".h", ".cpp", ".hpp",
        ".cs", ".go", ".rs", ".rb", ".php", ".html", ".css", ".toml", ".yaml", ".yml", ".tex", ".sql", ".sh", ".asm", ".s", ".m", ".r", ".xml", ".ini"];
    static readonly HashSet<string> Data = [".json", ".csv", ".tsv"];
    static readonly HashSet<string> Docs = [".pdf", ".docx", ".doc", ".rtf", ".pptx", ".ipynb"];
    static readonly HashSet<string> SkipDirs = [".git", "node_modules", ".venv", "venv", "env", "__pycache__", "build", "dist", ".next", "DerivedData",
        "Pods", ".gradle", "target", ".cache", ".idea", ".vscode", "site-packages", "Library", ".Trash", "coverage", "vendor", "bin", "obj"];
    const long MaxBytes = 25L * 1024 * 1024;
    const int MaxText = 400_000;

    readonly string dbPath;
    readonly DocumentTextOptions documents;
    readonly Func<IReadOnlyList<(string Name, string Path, bool Private)>> roots;
    readonly Func<IReadOnlyCollection<string>> lectures;
    readonly SemaphoreSlim busy = new(1, 1);

    public int Files { get; private set; }
    public string Last { get; private set; } = "";

    /// <param name="roots">The folders to index: the library first, then the ones Study Stash may read.</param>
    /// <param name="lectures">The lectures' own files (lectures have their own search).</param>
    public FileIndex(string home, Func<IReadOnlyList<(string Name, string Path, bool Private)>> roots, Func<IReadOnlyCollection<string>> lectures)
    {
        dbPath = System.IO.Path.Combine(home, "files.db");
        documents = new DocumentTextOptions { CacheDir = System.IO.Path.Combine(home, "cache", "text") };
        this.roots = roots;
        this.lectures = lectures;
        using var db = Open();
        Exec(db, """
            create table if not exists files(path text primary key, root text, mtime real, size integer);
            create virtual table if not exists fts using fts5(path unindexed, root unindexed, title, body, tokenize='porter unicode61 remove_diacritics 2');
            """);
        using var c = db.CreateCommand();
        c.CommandText = "select count(*) from files";
        Files = Convert.ToInt32(c.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }

    SqliteConnection Open()
    {
        var db = new SqliteConnection($"Data Source={dbPath}");
        db.Open();
        Exec(db, "pragma journal_mode=wal");
        return db;
    }

    static void Exec(SqliteConnection db, string sql, params (string, object?)[] args)
    {
        using var c = db.CreateCommand();
        c.CommandText = sql;
        foreach (var (k, v) in args) c.Parameters.AddWithValue(k, v ?? DBNull.Value);
        c.ExecuteNonQuery();
    }

    static IEnumerable<string> Walk(string root)
    {
        var stack = new Stack<string>([root]);
        while (stack.Count > 0)
        {
            string d = stack.Pop();
            IEnumerable<FileSystemInfo> entries;
            try
            {
                entries = new DirectoryInfo(d).EnumerateFileSystemInfos().ToList();
            }
            catch (Exception e) when (e is UnauthorizedAccessException or IOException)
            {
                continue;
            }
            foreach (var e in entries)
            {
                if (e.Name.StartsWith('.') || e.LinkTarget is not null) continue;
                if (e is DirectoryInfo)
                {
                    if (!SkipDirs.Contains(e.Name) && !e.Name.EndsWith(".app", StringComparison.Ordinal) && !e.Name.EndsWith(".photoslibrary", StringComparison.Ordinal))
                        stack.Push(e.FullName);
                }
                else yield return e.FullName;
            }
        }
    }

    /// <summary>The words in a file, or "" when it can't be read. PDFs, pictures, Word and PowerPoint are read by
    /// <see cref="DocumentText"/> (handwriting too), and what it read is kept in <paramref name="documents"/>'s cache.</summary>
    public static string TextOf(string path, DocumentTextOptions? documents = null)
    {
        string ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
        try
        {
            if (Text.Contains(ext) || (Data.Contains(ext) && new FileInfo(path).Length < 300_000))
                return Py.Head(File.ReadAllText(path), MaxText);
            if (ext == ".ipynb")
                return Py.Head(string.Join("\n\n", (JsonNode.Parse(File.ReadAllText(path))?["cells"] as JsonArray ?? [])
                    .Select(c => string.Concat((c?["source"] as JsonArray ?? []).Select(x => x?.GetValue<string>() ?? "")))), MaxText);
            if (DocumentText.CanRead(path))
                return Py.Head(DocumentText.ExtractAsync(path, documents ?? DocumentTextOptions.Default, CancellationToken.None).GetAwaiter().GetResult() ?? "", MaxText);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException
                                      or InvalidOperationException or FormatException)
        {
        }
        return "";
    }

    /// <summary>Bring the index up to date: new and changed files are read, removed ones dropped.</summary>
    public async Task UpdateAsync(Action<string>? log = null)
    {
        if (!await busy.WaitAsync(0)) return;
        try
        {
            await Task.Run(() => Update(log));
        }
        finally
        {
            busy.Release();
        }
    }

    void Update(Action<string>? log)
    {
        using var db = Open();
        var known = new Dictionary<string, (double, long)>();
        using (var c = db.CreateCommand())
        {
            c.CommandText = "select path, mtime, size from files";
            using var r = c.ExecuteReader();
            while (r.Read()) known[r.GetString(0)] = (r.GetDouble(1), r.GetInt64(2));
        }
        var seen = new HashSet<string>();
        int fresh = 0;
        var all = roots();
        var lectureFiles = lectures().Select(System.IO.Path.GetFullPath).ToHashSet();
        string library = all.Count > 0 ? System.IO.Path.GetFullPath(all[0].Path) : "";
        using var tx = db.BeginTransaction();
        foreach (var (name, root, priv) in all)
        {
            if (!Directory.Exists(root)) continue;
            bool isLibrary = System.IO.Path.GetFullPath(root) == library;
            foreach (string f in Walk(root))
            {
                // A readable folder that holds the library doesn't index it twice.
                if (!isLibrary && f.StartsWith(library + System.IO.Path.DirectorySeparatorChar, StringComparison.Ordinal)) continue;
                string ext = System.IO.Path.GetExtension(f).ToLowerInvariant();
                if (!Text.Contains(ext) && !Data.Contains(ext) && !Docs.Contains(ext) && !(isLibrary && DocumentText.Images.Contains(ext))) continue;
                if (isLibrary && lectureFiles.Contains(f)) continue;
                FileInfo fi;
                try
                {
                    fi = new FileInfo(f);
                    if (fi.Length > MaxBytes) continue;
                }
                catch (IOException)
                {
                    continue;
                }
                seen.Add(f);
                double mtime = (fi.LastWriteTimeUtc - DateTime.UnixEpoch).TotalSeconds;
                if (known.TryGetValue(f, out var k) && k == (mtime, fi.Length)) continue;
                string body = priv ? "" : TextOf(f, documents);
                string stem = System.IO.Path.GetFileNameWithoutExtension(f);
                // "spec", "feedback" and "README" say little alone: name them after their folder.
                string title = stem.ToLowerInvariant() is "spec" or "feedback" or "readme" or "index" or "notes"
                    ? $"{System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(f))} ({stem})" : stem.Replace('-', ' ').Replace('_', ' ');
                Exec(db, "delete from fts where path = $p", ("$p", f));
                Exec(db, "insert into fts(path, root, title, body) values ($p, $r, $t, $b)", ("$p", f), ("$r", name), ("$t", title), ("$b", body));
                Exec(db, "insert or replace into files values ($p, $r, $m, $s)", ("$p", f), ("$r", name), ("$m", mtime), ("$s", fi.Length));
                fresh++;
            }
        }
        var gone = known.Keys.Where(p => !seen.Contains(p)).ToList();
        foreach (string p in gone)
        {
            Exec(db, "delete from fts where path = $p", ("$p", p));
            Exec(db, "delete from files where path = $p", ("$p", p));
        }
        tx.Commit();
        using (var c = db.CreateCommand())
        {
            c.CommandText = "select count(*) from files";
            Files = Convert.ToInt32(c.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
        }
        Last = DateTimeOffset.Now.ToString("o", System.Globalization.CultureInfo.InvariantCulture);
        if (fresh > 0 || gone.Count > 0) log?.Invoke($"[files] {fresh} read, {gone.Count} removed, {Files} in all");
    }

    [GeneratedRegex(@"[\w'.+-]+")]
    private static partial Regex Words();

    /// <summary>The folder an indexed file was found in (its name, path and whether it's private), or null when the
    /// index doesn't hold that exact path: how Claude's read_file knows a path is one search could have shown.</summary>
    public (string Name, string Path, bool Private)? RootOf(string path)
    {
        string? name = null;
        try
        {
            using var db = Open();
            using var c = db.CreateCommand();
            c.CommandText = "select root from files where path = $p";
            c.Parameters.AddWithValue("$p", path);
            name = c.ExecuteScalar() as string;
        }
        catch (SqliteException)
        {
        }
        if (name is null) return null;
        foreach (var r in roots())
            if (r.Name == name && path.StartsWith(System.IO.Path.GetFullPath(r.Path).TrimEnd(System.IO.Path.DirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar, StringComparison.Ordinal))
                return r;
        return null;
    }

    /// <summary>Files matching every word (the last may be half typed), best first.</summary>
    public List<FileHit> Search(string query, int limit = 30, string? root = null)
    {
        var words = Words().Matches(query ?? "").Select(m => m.Value.Replace("\"", "")).Where(w => w.Length > 0).ToList();
        if (words.Count == 0) return [];
        string match = string.Join(" ", words.Select(w => $"\"{w}\"*"));
        var hits = new List<FileHit>();
        try
        {
            using var db = Open();
            using var c = db.CreateCommand();
            c.CommandText = "select path, root, title, snippet(fts, 3, char(1), char(2), '…', 18) from fts where fts match $m"
                + (root is null ? "" : " and root = $root") + " order by bm25(fts, 0, 0, 4.0, 1.0) limit $n";
            c.Parameters.AddWithValue("$m", match);
            c.Parameters.AddWithValue("$n", limit);
            if (root is not null) c.Parameters.AddWithValue("$root", root);
            using var r = c.ExecuteReader();
            while (r.Read())
                hits.Add(new FileHit(r.GetString(1), r.GetString(0), r.GetString(2),
                    WebUtility.HtmlEncode(r.IsDBNull(3) ? "" : r.GetString(3)).Replace("\u0001", "<mark>").Replace("\u0002", "</mark>")));
        }
        catch (SqliteException)
        {
        }
        return hits;
    }

    /// <summary>Keep it up to date: soon after starting, then every 15 minutes.</summary>
    public async Task RunAsync(Action<string> log, CancellationToken stop)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(20), stop);
            while (!stop.IsCancellationRequested)
            {
                try
                {
                    await UpdateAsync(log);
                }
                catch (Exception e) when (e is SqliteException or IOException)
                {
                    log($"[files] {e.Message}");
                }
                await Task.Delay(TimeSpan.FromMinutes(15), stop);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }
}
