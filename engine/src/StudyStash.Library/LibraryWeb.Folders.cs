using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using StudyStash.Core;

namespace StudyStash.Library;

/// <summary>A student with two library folders on this computer (<see cref="LibraryFolders"/>): what bringing in the
/// other's notes would do, and doing it (<see cref="LibraryBringIn"/>), as a job the app asks after (a big OneDrive
/// folder takes longer than one request may). One job at a time. A folder named is read only if it holds a Syft Notes
/// library, or lectures here are filed inside it; the computer's usual places are never looked through. Every route
/// needs the library's password, like the rest of the API.</summary>
public sealed partial class LibraryWeb
{
    readonly Lock folderGate = new();
    BringInJob? folderJob;

    void MapFolders(WebApplication app)
    {
        app.MapGet(LibraryBringIn.Route, (HttpContext ctx) => Api(ctx, () =>
        {
            lock (folderGate) return Http.Json(folderJob?.Json() ?? []);
        }));
        // {asked: the asker's id for it, kind: look|bring, into (look: the folder the student would use), from: [folders to
        // bring in], refile: [folders whose listed lectures' files to copy in]}: the job started, or the one already
        // running (its own "asked" tells the asker it isn't theirs).
        app.MapPost(LibraryBringIn.Route, Http.Handle(ctx => ApiAsync(ctx, async () =>
        {
            var body = await Http.JsonBodyAsync(ctx.Request) ?? [];
            string kind = Py.AsString(body["kind"]) == BringInJob.Look ? BringInJob.Look : BringInJob.Bring;
            lock (folderGate)
            {
                if (folderJob is { Running: true } running) return Http.Json(running.Json());
                var job = folderJob = new BringInJob(kind, Py.AsString(body["asked"]) ?? "");
                var from = (body["from"] as JsonArray ?? []).Select(Py.AsString).OfType<string>().ToList();
                var refile = (body["refile"] as JsonArray ?? []).Select(Py.AsString).OfType<string>().ToList();
                string? into = Py.AsString(body["into"]);
                _ = Task.Run(() => RunFolderJob(job, into, from, refile));
                return Http.Json(job.Json());
            }
        })));
    }

    void RunFolderJob(BringInJob job, string? into, List<string> from, List<string> refile)
    {
        try
        {
            // Only the folders asked about are looked at, never the computer's usual places: each counts if it really holds
            // a library, or if lectures here are still filed inside it.
            string? Usable(string asked) =>
                asked.Length > 0 && (LibraryFolders.Find(null, [asked]).Count > 0 || LibraryBringIn.PointingInto(store, asked) > 0) ? asked : null;
            string home = Py.UserHome();
            if (job.Kind == BringInJob.Look)
            {
                string target = into is { Length: > 0 } && Usable(into) is { } t ? t : cfg.PoolDir;
                foreach (string f in from)
                    if (Usable(f) is { } dir && !SameFolder(dir, target)) job.Add(LibraryBringIn.Plan(store, cfg, target, dir, home, job.Progress));
                job.End();
                return;
            }
            var reading = store.UnreadAttachments().Select(a => a.Id).ToHashSet();
            foreach (var (asked, bring) in from.Select(f => (f, true)).Concat(refile.Select(f => (f, false))))
            {
                // A folder asked for that this library can't use is said, never passed over: the switch isn't finished.
                if (Usable(asked) is not { } dir)
                {
                    job.End($"no library at a folder asked for ({asked.Length} characters)");
                    return;
                }
                if (SameFolder(dir, cfg.PoolDir))
                {
                    // The library answering still reads that folder (another copy's library, started before the switch).
                    job.End("the library still uses a folder asked to leave");
                    return;
                }
                job.Add(bring ? LibraryBringIn.Apply(store, cfg, dir, home, job.Progress) : LibraryBringIn.Refile(store, cfg, dir, home, job.Progress));
            }
            // The attached files that came in are read for their words, as an upload's are.
            foreach (var a in store.UnreadAttachments().Where(a => !reading.Contains(a.Id))) ReadLater(a);
            // Done only when no lecture, listed or in the trash, is still filed inside a folder it was asked to leave.
            int left = from.Concat(refile).Sum(f => LibraryBringIn.PointingInto(store, f));
            job.End(left == 0 ? null : $"{left} lectures still filed in a folder asked to leave");
        }
        catch (Exception e)
        {
            // Whatever stopped it (a folder gone, a full disk, a locked file), the job ends, so the app stops waiting and
            // says so; what came in before stays, and running it again brings only the rest.
            Console.Error.WriteLine($"[folders] {job.Kind} stopped: {e.GetType().Name}: {e.Message}");
            job.End(e.GetType().Name);
        }
    }

    static bool SameFolder(string a, string b) => LibraryFolders.Comparer.Equals(LibraryFolders.Real(a), LibraryFolders.Real(b));
}
