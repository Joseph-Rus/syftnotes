namespace StudyStash.Core.PhoneDrive;

/// <summary>
/// A stand-in for the Google Drive folder, kept in a folder on this computer: each file is a file of the same name,
/// and its name is its id. The self-test's phone writes its box and answer files here and the library answers them,
/// across processes, with no Google at all (`serve --drive-folder DIR`). Never used for a student.
/// </summary>
public sealed class FolderDrive : IDrive
{
    readonly string dir;

    public FolderDrive(string dir)
    {
        this.dir = Path.GetFullPath(dir);
        Directory.CreateDirectory(this.dir);
    }

    /// <summary>A file's path, for a name that stays inside the folder; anything else isn't a file here.</summary>
    string? PathOf(string name) =>
        name.Length > 0 && name.IndexOfAny(['/', '\\', ':']) < 0 && !name.StartsWith('.') ? Path.Combine(dir, name) : null;

    public Task<List<DriveFile>> ListAsync(string prefix, CancellationToken ct = default) =>
        Task.FromResult(new DirectoryInfo(dir).EnumerateFiles(prefix + "*")
            .Where(f => !f.Name.StartsWith('.') && f.Name.StartsWith(prefix, StringComparison.Ordinal))
            .Select(f => new DriveFile(f.Name, f.Name, f.LastWriteTimeUtc, f.Length)).ToList());

    public async Task<string> CreateAsync(string name, Stream content, string type, CancellationToken ct = default)
    {
        string path = PathOf(name) ?? throw new DriveException(400, $"no file can be called {name}");
        await WriteAsync(path, content, ct);
        return name;
    }

    public async Task<bool> UpdateAsync(string id, Stream content, string type, CancellationToken ct = default)
    {
        if (PathOf(id) is not { } path || !File.Exists(path)) return false;
        await WriteAsync(path, content, ct);
        return true;
    }

    public async Task<bool> ReadAsync(string id, Stream into, CancellationToken ct = default)
    {
        if (PathOf(id) is not { } path) return false;
        try
        {
            await using var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            await f.CopyToAsync(into, ct);
            return true;
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            return false;
        }
    }

    public Task DeleteAsync(string id, CancellationToken ct = default)
    {
        if (PathOf(id) is { } path) File.Delete(path);
        return Task.CompletedTask;
    }

    /// <summary>Written whole beside it, then moved over it: a reader never sees half a file.</summary>
    static async Task WriteAsync(string path, Stream content, CancellationToken ct)
    {
        string tmp = Path.Combine(Path.GetDirectoryName(path)!, "." + Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N")[..8]);
        await using (var f = File.Create(tmp)) await content.CopyToAsync(f, ct);
        File.Move(tmp, path, overwrite: true);
    }
}
