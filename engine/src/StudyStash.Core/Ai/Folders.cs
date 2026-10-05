using System.Text.Json;

namespace StudyStash.Core.Ai;

/// <summary>A folder on the library's computer, outside the library, that Study Stash may read: searched, and read
/// by the AI when <see cref="Ai"/> is on. A private folder is searched by file name only and never shown to the AI.
/// One linked to a class (<see cref="Class"/>: a project's GitHub repo, say) shows on that class's home and is read when
/// asking about that class; the folder stays where it is, never copied into the library.</summary>
public sealed record ReadFolder(string Path, string Name, bool Ai = true, bool Private = false, string Class = "");

/// <summary>The folders Study Stash may read (folders.json beside config.toml).</summary>
public static class Folders
{
    static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    public static string PathIn(string home) => System.IO.Path.Combine(home, "folders.json");

    public static List<ReadFolder> Load(string home)
    {
        try
        {
            return File.Exists(PathIn(home)) ? JsonSerializer.Deserialize<List<ReadFolder>>(File.ReadAllText(PathIn(home)), Options) ?? [] : [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public static void Save(string home, IEnumerable<ReadFolder> folders)
    {
        Directory.CreateDirectory(home);
        Py.WriteText(PathIn(home), JsonSerializer.Serialize(folders.ToList(), Options) + "\n");
    }
}
