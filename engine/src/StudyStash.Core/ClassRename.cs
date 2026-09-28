using System.Globalization;
using System.Text.Json.Nodes;
using StudyStash.Core.Canvas;

namespace StudyStash.Core;

/// <summary>One class to rename: from its name now to its Canvas course's name, with the course's short code.</summary>
public sealed record ClassRenameStep(string From, string To, long CourseId, string Code);

/// <summary>
/// "Use Canvas course names": a library whose classes were named from course codes ("202710.TS.CSCI321.A") renames
/// each class linked to a Canvas course to the course's name ("Software Engineering"). <see cref="Plan"/> is the
/// preview; <see cref="Apply"/> does it, class by class, moving the class's folder and every reference to it: its
/// lectures and note files, its Canvas link, index, assignments, notifications and what the crawl remembers, chats
/// about it, and the recordings on this computer. The old name stays as one of the class's other names,
/// so a lecture a laptop recorded under it still files here. Never deletes a lecture.
/// </summary>
public static class ClassRename
{
    /// <summary>Why nothing can be renamed right now, or null when it can.</summary>
    public static string? Blocked(Crawl? crawl) =>
        crawl is not null && (crawl.Active || crawl.Ready || crawl.Left != (0, 0))
            ? "Canvas is syncing. Try again when it's done." : null;

    /// <summary>The classes that would be renamed: each class linked to a Canvas course whose (cleaned) name differs
    /// from the class's, to a name no other class has and whose folder is free.</summary>
    public static List<ClassRenameStep> Plan(Config cfg, CanvasSettings s)
    {
        var linked = cfg.Classes.Where(c => s.Courses.ContainsKey(c.Name)).Select(c => c.Name).ToList();
        var courses = new List<(string Id, string Name, string Code)>();
        foreach (string cls in linked)
        {
            long id = s.Courses[cls];
            string key = id.ToString(CultureInfo.InvariantCulture);
            var info = s.CourseInfo.GetValueOrDefault(key);
            var index = info is null ? CourseIndex.Load(cfg.Home, cls) : null;
            string name = info is { Name.Length: > 0 } ? info.Name : s.Available.GetValueOrDefault(key) ?? index?.Name ?? "";
            string code = info?.Code ?? index?.Code ?? "";
            // A course Canvas never named is left alone.
            if (name.Length == 0) continue;
            courses.Add((cls, name, code));
        }
        var unlinked = cfg.ClassNames().Where(n => !courses.Any(c => c.Id == n));
        var titles = CourseNames.Unique(courses, unlinked);
        var steps = new List<ClassRenameStep>();
        foreach (var (cls, name, code) in courses)
        {
            string to = titles[cls];
            if (to == cls) continue;
            steps.Add(new ClassRenameStep(cls, to, s.Courses[cls], CourseNames.ShortCode(code, name)));
        }
        // A folder is free when nothing's there, or it's this class's own (a change of case), or another class's
        // that is itself being renamed away.
        var leaving = steps.Select(st => st.From).ToHashSet();
        string Folder(string name) => Path.Combine(cfg.PoolDir, Notes.Slugify(name, 60));
        return [.. steps.Where(st =>
            Py.SamePath(Folder(st.From), Folder(st.To))
            || !Directory.Exists(Folder(st.To)) || !Directory.EnumerateFileSystemEntries(Folder(st.To)).Any()
            || cfg.ClassNames().Any(n => leaving.Contains(n) && n != st.From && Py.SamePath(Folder(n), Folder(st.To))))];
    }

    /// <summary>What <see cref="Apply"/> did: the classes renamed, how many lectures moved, and what stopped it
    /// (null when everything asked for was done).</summary>
    public sealed record Outcome(List<ClassRenameStep> Renamed, int Lectures, string? Problem);

    /// <summary>Rename each class in <paramref name="steps"/>, one at a time, in an order where no class takes a name
    /// another still has (a class whose name is still taken waits; a circle of them is left alone). Stops at the first
    /// class whose folder can't move, with everything before it done and it untouched.</summary>
    public static Outcome Apply(Config cfg, Store store, IReadOnlyList<ClassRenameStep> steps, Crawl? crawl = null)
    {
        if (Blocked(crawl) is { } why) return new([], 0, why);
        var pending = steps.Where(st => st.From != st.To && cfg.ClassNames().Contains(st.From)).ToList();
        var done = new List<ClassRenameStep>();
        int lectures = 0;
        while (pending.Count > 0)
        {
            var next = pending.FirstOrDefault(st => !cfg.Classes.Any(c => c.Name != st.From && c.Name.Equals(st.To, StringComparison.OrdinalIgnoreCase)));
            if (next is null) return new(done, lectures, "Some classes would swap names, so they were left as they are.");
            pending.Remove(next);
            if (next.To.Length is 0 or > CourseNames.MaxLength || next.To.Equals(Configs.Unsorted, StringComparison.OrdinalIgnoreCase)) continue;
            try
            {
                lectures += store.RenameClass(next.From, next.To);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return new(done, lectures, $"{next.From} couldn't be renamed: {e.Message}");
            }
            RenameEverywhere(cfg, crawl, next);
            done.Add(next);
        }
        return new(done, lectures, null);
    }

    /// <summary>Everything but the lectures (<see cref="Store.RenameClass"/>) that knows the class by name.</summary>
    static void RenameEverywhere(Config cfg, Crawl? crawl, ClassRenameStep st)
    {
        string home = cfg.Home, from = st.From, to = st.To;
        var def = cfg.Classes.First(c => c.Name == from);
        var aliases = def.Aliases.Append(from).Append(st.Code)
            .Where(a => a.Length > 0 && !a.Equals(to, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        cfg.Classes[cfg.Classes.IndexOf(def)] = new ClassDef(to, aliases, def.Description);
        Configs.Save(cfg);

        CanvasSettings.Update(home, s =>
        {
            if (s.Courses.Remove(from, out long id)) s.Courses[to] = id;
            if (s.Scouts.Remove(from, out var scout)) s.Scouts[to] = scout;
        });
        foreach (var (oldPath, newPath) in new[] { (CourseIndex.PathIn(home, from), CourseIndex.PathIn(home, to)), (CourseIndex.StagedPathIn(home, from), CourseIndex.StagedPathIn(home, to)) })
        {
            if (!File.Exists(oldPath) || oldPath == newPath) continue;
            if (JsonNode.Parse(File.ReadAllText(oldPath)) is JsonObject index)
            {
                index["class"] = to;
                File.WriteAllText(newPath, index.ToJsonString());
            }
            if (!Py.SamePath(oldPath, newPath)) File.Delete(oldPath);
        }
        var all = Assignments.Load(home);
        if (all.Any(a => a.ClassName == from)) Assignments.Save(home, all.Select(a => a.ClassName == from ? a with { ClassName = to } : a));
        CanvasSeen.Rename(home, from, to);
        CanvasNotifications.Rename(home, from, to);
        (crawl ?? new Crawl(home, name => Path.Combine(cfg.PoolDir, Notes.Slugify(name, 60)), () => DateTimeOffset.UtcNow, () => TimeZoneInfo.Local))
            .RenameClass(from, to);
        RenameInChats(home, from, to);
        FollowHere(home, from, to);
    }

    static void RenameInChats(string home, string from, string to)
    {
        string dir = Path.Combine(home, "chats");
        if (!Directory.Exists(dir)) return;
        foreach (string file in Directory.EnumerateFiles(dir, "*.json"))
        {
            try
            {
                if (JsonNode.Parse(File.ReadAllText(file)) is JsonObject chat && chat["class_name"] is JsonValue v && v.TryGetValue(out string? cls) && cls == from)
                {
                    chat["class_name"] = to;
                    File.WriteAllText(file, chat.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
                }
            }
            catch (System.Text.Json.JsonException)
            {
                // A chat file that can't be read is left as it is.
            }
        }
    }

    /// <summary>This computer's own copies of a renamed class's name: the recorded lectures still waiting here. The app calls this for its home after the library renames; the library for its own.</summary>
    public static void FollowHere(string home, string from, string to)
    {
        if (Directory.Exists(Path.Combine(home, "recordings")))
        {
            var lectures = new LectureStore(home);
            foreach (var l in lectures.All().Where(l => l.ClassName == from || l.FiledClass == from))
                lectures.Update(l.Id, x =>
                {
                    if (x.ClassName == from) x.ClassName = to;
                    if (x.FiledClass == from) x.FiledClass = to;
                });
        }
    }
}
