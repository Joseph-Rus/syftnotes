using System.Globalization;
using System.Text.Json;

namespace StudyStash.App.Services;

/// <summary>What a student had typed into a lecture's notes and not saved: the editor's text, the notes the edit
/// started from (the library is told, so it never saves over other ones unseen), and when it was kept.</summary>
public sealed record NoteDraft(string From, string Text, string At);

/// <summary>
/// Unsaved typing in lectures' notes, kept in a small file beside the app's settings (note-drafts.json, by lecture),
/// so it's back in its editor after the app quits, updates itself or is restarted. A draft whose lecture isn't opened
/// again is dropped after <see cref="Kept"/>.
/// </summary>
public static class NoteDrafts
{
    public static readonly TimeSpan Kept = TimeSpan.FromDays(30);

    static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    public static string PathIn(string home) => Path.Combine(home, "note-drafts.json");

    /// <summary>The drafts kept in <paramref name="home"/>; none when there's no file, or it can't be read.</summary>
    public static Dictionary<string, NoteDraft> Load(string home, DateTimeOffset? now = null)
    {
        try
        {
            var drafts = JsonSerializer.Deserialize<Dictionary<string, NoteDraft>>(File.ReadAllText(PathIn(home)), Json) ?? [];
            var at = now ?? DateTimeOffset.UtcNow;
            return drafts.Where(d => d.Value is { From: not null, Text: not null }
                    && DateTimeOffset.TryParse(d.Value.At, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var kept) && at - kept <= Kept)
                .ToDictionary(d => d.Key, d => d.Value);
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>Writes <paramref name="drafts"/> as the file, whole (written beside it, then moved over it); with none,
    /// the file goes. False when it couldn't be written (a full disk, a folder that can't be written to).</summary>
    public static bool Save(string home, IReadOnlyDictionary<string, NoteDraft> drafts)
    {
        string path = PathIn(home), tmp = path + ".tmp";
        try
        {
            if (drafts.Count == 0)
            {
                if (File.Exists(path)) File.Delete(path);
                return true;
            }
            Directory.CreateDirectory(home);
            File.WriteAllText(tmp, JsonSerializer.Serialize(drafts, Json));
            File.Move(tmp, path, overwrite: true);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
