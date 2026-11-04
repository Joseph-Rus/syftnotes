using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using StudyStash.Core;

namespace StudyStash.Library;

/// <summary>A student with two library folders on this computer (<see cref="LibraryFolders"/>): what bringing in the
/// other's notes would do, and doing it (<see cref="LibraryBringIn"/>), as a job the app asks after (a big OneDrive
/// folder takes longer than one request may). One job at a time. Only a folder this computer's own look finds is read,
/// never a path a request names on its own. Every route needs the library's password, like the rest of the API.</summary>
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
        // {kind: look|bring, into (look: the folder the student would use), from: [folders to bring in], refile: [folders
        // whose listed lectures' files to copy in]}: the job started, or the one already running.
        app.MapPost(LibraryBringIn.Route, Http.Handle(ctx => ApiAsync(ctx, async () =>
        {
            var body = await Http.JsonBodyAsync(ctx.Request) ?? [];
            string kind = Py.AsString(body["kind"]) == BringInJob.Look ? BringInJob.Look : BringInJob.Bring;
            lock (folderGate)
            {
                if (folderJob is { Running: true } running) return Http.Json(running.Json());
                var job = folderJob = new BringInJob(kind);
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
            var found = LibraryFolders.Find(cfg.PoolDir);
            string home = Py.UserHome();
            if (job.Kind == BringInJob.Look)
            {
                string target = Known(found, into) ?? cfg.PoolDir;
                foreach (string f in from)
                    if (Known(found, f) is { } dir && !SameFolder(dir, target)) job.Add(LibraryBringIn.Plan(store, cfg, target, dir, home, job.Progress));
            }
            else
            {
                var reading = store.UnreadAttachments().Select(a => a.Id).ToHashSet();
                LibraryBringIn.ListOwn(store, cfg);
                foreach (string f in from)
                    if (Known(found, f) is { } dir && !SameFolder(dir, cfg.PoolDir)) job.Add(LibraryBringIn.Apply(store, cfg, dir, home, job.Progress));
                foreach (string f in refile)
                    if (Known(found, f) is { } dir && !SameFolder(dir, cfg.PoolDir)) job.Add(LibraryBringIn.Refile(store, cfg, dir, home, job.Progress));
                // The attached files that came in are read for their words, as an upload's are.
                foreach (var a in store.UnreadAttachments().Where(a => !reading.Contains(a.Id))) ReadLater(a);
            }
            job.End();
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

    /// <summary>The library folder this computer has at <paramref name="path"/>, or null when there's none there.</summary>
    static string? Known(List<LibraryFolder> found, string? path) =>
        path is { Length: > 0 } ? found.FirstOrDefault(f => SameFolder(f.Path, path))?.Path : null;
}
