using System.Globalization;
using System.Text.Json;

namespace StudyStash.Core.Canvas;

/// <summary>What the student did to one Due item in Syft Notes: ticked it off (<see cref="DueMarks.Done"/>) or took it
/// off the list (<see cref="DueMarks.Hidden"/>). <paramref name="Kind"/> is the item's own (assignment, quiz,
/// discussion or todo: a planner to-do's id is not an assignment's), <paramref name="At"/> when, UTC ISO.</summary>
public sealed record DueMark(string Class, string Kind, long Id, string Mark, string At);

/// <summary>
/// The student's own marks on the Due list. They live in Syft Notes only, in <c>home/due_marks.json</c>: nothing here
/// ever asks Canvas to change. <see cref="Apply"/> puts them on a list of <see cref="Assignment"/>s, and every reader
/// of the list gets them through <see cref="Assignments.Shown"/> (or, for the Due list that also shows what's hidden,
/// through <c>LibraryWeb.DueList</c>), so Home, the Due tab, the calendar, Ask and Claude's tools read one state.
/// </summary>
public static class DueMarks
{
    public const string Done = "done", Hidden = "hidden";

    static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
    static readonly Lock Gate = new();

    public static string PathIn(string home) => Path.Combine(home, "due_marks.json");

    /// <summary>The student's marks. A file that can't be read isn't taken for "no marks" and then written over by the next
    /// <see cref="Set"/>: it is kept beside as <c>due_marks.json.bad</c> (replacing an older one) and the marks start again.</summary>
    public static List<DueMark> Load(string home)
    {
        string path = PathIn(home);
        if (TryRead(path) is { } marks) return marks;
        lock (Gate)
        {
            if (TryRead(path) is { } mended) return mended; // a writer may have mended it since
            Console.WriteLine("[canvas] Your Due marks couldn't be read, so they were kept aside as due_marks.json.bad and start again.");
            if (File.Exists(path)) SharedFile.Replace(path, path + ".bad");
            return [];
        }
    }

    /// <summary>The marks in the file (none when there's no file), or null when it's there but isn't marks.</summary>
    static List<DueMark>? TryRead(string path)
    {
        try
        {
            return SharedFile.Read(path) is { } text ? JsonSerializer.Deserialize<List<DueMark>>(text, Options) ?? [] : [];
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The items with the student's marks on them (a hidden one stays in the list, as <c>Mark</c> "hidden").</summary>
    public static List<Assignment> Apply(IEnumerable<Assignment> items, IReadOnlyList<DueMark> marks)
    {
        var mine = new Dictionary<(string, string, long), DueMark>();
        foreach (var m in marks) mine[(m.Class, m.Kind, m.Id)] = m;
        return [.. items.Select(a => mine.TryGetValue((a.ClassName, a.Kind, a.Id), out var m) ? a with { Mark = m.Mark, MarkAt = m.At } : a)];
    }

    /// <summary>Marks an item <see cref="Done"/> or <see cref="Hidden"/>, or takes its mark off ("").</summary>
    public static void Set(string home, string cls, string kind, long id, string mark, DateTimeOffset now)
    {
        lock (Gate)
        {
            var all = Load(home);
            all.RemoveAll(m => m.Class == cls && m.Kind == kind && m.Id == id);
            if (mark.Length > 0) all.Add(new DueMark(cls, kind, id, mark, now.UtcDateTime.ToString("o", CultureInfo.InvariantCulture)));
            SharedFile.Write(PathIn(home), JsonSerializer.Serialize(all, Options));
        }
    }

    /// <summary>A class was renamed: its marks move to the new name.</summary>
    public static void Rename(string home, string from, string to)
    {
        lock (Gate)
        {
            var all = Load(home);
            if (!all.Any(m => m.Class == from)) return;
            SharedFile.Write(PathIn(home), JsonSerializer.Serialize(all.Select(m => m.Class == from ? m with { Class = to } : m).ToList(), Options));
        }
    }
}
