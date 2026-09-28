using System.Globalization;
using System.Text.RegularExpressions;

namespace StudyStash.Core.Canvas;

/// <summary>What happened when the student changed which Canvas courses to bring in: the classes made (or linked) for
/// newly chosen courses, the classes that stopped syncing, of those the ones removed with their Canvas files, and the
/// ones kept because they hold lectures.</summary>
public sealed record ChoiceOutcome(List<string> Added, List<string> Stopped, List<string> Removed, List<string> KeptForLectures);

/// <summary>
/// Which Canvas courses become classes and sync. Find my courses lists every course the student is in, and a
/// school's list is full of things that aren't classes this term: last term's courses, sandboxes, chapel and
/// convocation, orientation. <see cref="Suggest"/> ticks this term's courses and leaves the rest unticked (saying
/// why); <see cref="Apply"/> makes the student's choice real: a class for each chosen course, and a course no longer
/// chosen stops syncing, its class and files kept or removed as the student says (a class with lectures always
/// stays).
/// </summary>
public static partial class CourseChoices
{
    /// <summary>Why a course starts unticked, as the picker says it; "" for one that starts ticked.</summary>
    public const string PastTerm = "Past term", LaterTerm = "Later term", NoTerm = "No term", NotAClass = "Not a class";

    // Words in a course's name that mean it isn't a class a student records: practice spaces, worship and assemblies,
    // orientation and advising, training and resource sites.
    [GeneratedRegex(@"\b(?:sandbox|chapel|convocation|orientation|advising|advisement|training|resource center|student life|community|playground|practice course|test course)\b", RegexOptions.IgnoreCase)]
    private static partial Regex NotAClassWords();

    [GeneratedRegex(@"\b(Spring|Summer|Fall|Autumn|Winter)\b\D{0,3}((?:19|20)\d{2})\b|\b((?:19|20)\d{2})\D{0,3}\b(Spring|Summer|Fall|Autumn|Winter)\b", RegexOptions.IgnoreCase)]
    private static partial Regex SeasonYear();

    /// <summary>How long after a term ends its courses still count as this term's (grades, a late final).</summary>
    static readonly TimeSpan Grace = TimeSpan.FromDays(7);
    /// <summary>How long before a term starts its courses already count (the student is getting ready).</summary>
    static readonly TimeSpan Ahead = TimeSpan.FromDays(30);

    /// <summary>Whether the course starts ticked, and if not, why (<see cref="PastTerm"/>, <see cref="LaterTerm"/>,
    /// <see cref="NoTerm"/> or <see cref="NotAClass"/>). A course is this term's when its term's (or its own) dates
    /// say so, or, without dates, when its term's name ("Fall 2026") does; a course in no real term (Canvas's
    /// "Default Term") or named like a sandbox or chapel isn't.</summary>
    public static (bool Ticked, string Why) Suggest(CourseInfo info, DateTimeOffset now)
    {
        if (NotAClassWords().IsMatch(info.Name) || NotAClassWords().IsMatch(info.Code)) return (false, NotAClass);
        var (start, end) = Dates(info);
        if (end is { } e && e + Grace < now) return (false, PastTerm);
        if (start is { } s && s - Ahead > now) return (false, LaterTerm);
        if (start is not null || end is not null) return (true, "");
        string term = info.Term.Trim();
        if (term.Length == 0 || term.Equals("Default Term", StringComparison.OrdinalIgnoreCase)) return (false, NoTerm);
        if (Season(term) is var (from, to))
        {
            if (to + Grace < now) return (false, PastTerm);
            if (from - Ahead > now) return (false, LaterTerm);
        }
        return (true, "");
    }

    /// <summary>When the course's term (or else the course itself) starts and ends, where Canvas said.</summary>
    static (DateTimeOffset? Start, DateTimeOffset? End) Dates(CourseInfo info)
    {
        static DateTimeOffset? When(string iso) =>
            iso.Length > 0 && DateTimeOffset.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var t) ? t : null;
        return (When(info.TermStart) ?? When(info.CourseStart), When(info.TermEnd) ?? When(info.CourseEnd));
    }

    /// <summary>The months a season names ("Fall 2026" is August to December 2026); null when the term's name has no
    /// season and year.</summary>
    static (DateTimeOffset From, DateTimeOffset To)? Season(string term)
    {
        var m = SeasonYear().Match(term);
        if (!m.Success) return null;
        string season = (m.Groups[1].Success ? m.Groups[1].Value : m.Groups[4].Value).ToLowerInvariant();
        int year = int.Parse(m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value, CultureInfo.InvariantCulture);
        (int fromMonth, int toMonth, int toYear) = season switch
        {
            "spring" => (1, 5, year),
            "summer" => (5, 8, year),
            "winter" => (12, 2, year + 1),
            _ => (8, 12, year),
        };
        var from = new DateTimeOffset(year, fromMonth, 1, 0, 0, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(toYear, toMonth, 1, 0, 0, 0, TimeSpan.Zero).AddMonths(1);
        return (from, to);
    }

    /// <summary>
    /// Makes <paramref name="chosen"/> (course ids) the courses this library brings in. Each chosen course not yet
    /// linked gets a class: its cleaned, unique name (<see cref="CourseNames"/>), or an existing class of that name
    /// that isn't linked yet, or — with <paramref name="matchClasses"/>, for a library that had classes before Canvas —
    /// the unlinked class <see cref="CourseMatch"/> suggests for it. Each linked course not chosen stops syncing;
    /// unless <paramref name="keep"/>, its Canvas files and what the library knew of it go, and so does its class when
    /// no lecture is filed in it. Removing waits while a sync is running (<see cref="Blocked"/>).
    /// </summary>
    public static ChoiceOutcome Apply(Config cfg, Store store, IReadOnlyCollection<string> chosen, bool keep, bool matchClasses = false, Crawl? crawl = null)
    {
        string home = cfg.Home;
        var s = CanvasSettings.Load(home);
        var want = chosen.Where(id => s.Available.ContainsKey(id)).Distinct().ToList();
        var outcome = new ChoiceOutcome([], [], [], []);

        foreach (var (cls, id) in s.Courses.ToList())
        {
            if (want.Contains(Id(id))) continue;
            s.Courses.Remove(cls);
            s.Scouts.Remove(cls);
            outcome.Stopped.Add(cls);
        }

        var linked = s.Courses.Values.Select(Id).ToHashSet();
        var adding = want.Where(id => !linked.Contains(id)).ToList();
        var unlinked = cfg.ClassNames().Where(n => !s.Courses.ContainsKey(n) && !outcome.Stopped.Contains(n)).ToList();
        if (matchClasses)
            foreach (string cls in unlinked.ToList())
                if (CourseMatch.Suggest(cls, adding.ToDictionary(id => id, id => s.Available[id]), s.CourseInfo) is { } pick)
                {
                    s.Courses[cls] = long.Parse(pick.Id, CultureInfo.InvariantCulture);
                    adding.Remove(pick.Id);
                    unlinked.Remove(cls);
                    outcome.Added.Add(cls);
                }
        var linkedNames = s.Courses.Keys.Concat(outcome.Stopped).ToList();
        var titles = CourseNames.Unique(adding.Select(id => (id, NameOf(s, id), CodeOf(s, id))), linkedNames);
        bool changedClasses = false;
        foreach (string id in adding)
        {
            string name = titles[id];
            var existing = cfg.Classes.FirstOrDefault(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                cfg.Classes.Add(new ClassDef(name));
                changedClasses = true;
            }
            else name = existing.Name;
            s.Courses[name] = long.Parse(id, CultureInfo.InvariantCulture);
            outcome.Added.Add(name);
        }

        if (!keep)
            foreach (string cls in outcome.Stopped)
            {
                Forget(cfg, store, cls, crawl);
                bool hasLectures = store.ListNotes(cls, 1).Count > 0 || store.Processing().Any(n => n.ClassName == cls);
                if (hasLectures)
                {
                    outcome.KeptForLectures.Add(cls);
                    continue;
                }
                if (cfg.Classes.RemoveAll(c => c.Name == cls) > 0) changedClasses = true;
                RemoveEmptyFolder(store.ClassFolder(cls));
                outcome.Removed.Add(cls);
            }
        if (changedClasses) Configs.Save(cfg);

        CanvasSettings.Update(home, st =>
        {
            st.Courses = s.Courses;
            st.Scouts = s.Scouts;
            st.Chosen = want;
            if (outcome.Added.Count > 0) st.SyncNow = true;
        });
        return outcome;
    }

    /// <summary>Why courses can't be dropped with their files right now, or null when they can.</summary>
    public static string? Blocked(Crawl? crawl) => ClassRename.Blocked(crawl);

    static string Id(long id) => id.ToString(CultureInfo.InvariantCulture);

    static string NameOf(CanvasSettings s, string id) =>
        s.CourseInfo.TryGetValue(id, out var i) && i.Name.Length > 0 ? i.Name : s.Available.GetValueOrDefault(id) ?? "";

    static string CodeOf(CanvasSettings s, string id) => s.CourseInfo.TryGetValue(id, out var i) ? i.Code : "";

    /// <summary>Everything the library keeps from Canvas for a class: its Canvas folder, its index, its assignments,
    /// its opened announcements and what the sync remembered about it. Its lectures are never touched.</summary>
    static void Forget(Config cfg, Store store, string cls, Crawl? crawl)
    {
        string home = cfg.Home;
        string folder = Path.Combine(store.ClassFolder(cls), "Canvas");
        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        foreach (string p in new[] { CourseIndex.PathIn(home, cls), CourseIndex.StagedPathIn(home, cls) })
            if (File.Exists(p)) File.Delete(p);
        var all = Assignments.Load(home);
        if (all.Any(a => a.ClassName == cls)) Assignments.Save(home, all.Where(a => a.ClassName != cls));
        (crawl ?? new Crawl(home, name => Path.Combine(cfg.PoolDir, Notes.Slugify(name, 60)), () => DateTimeOffset.UtcNow, () => TimeZoneInfo.Local))
            .ForgetClass(cls);
    }

    static void RemoveEmptyFolder(string folder)
    {
        try
        {
            if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any()) Directory.Delete(folder);
        }
        catch (IOException)
        {
        }
    }
}
