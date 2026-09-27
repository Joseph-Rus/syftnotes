using StudyStash.Core.Canvas;

namespace StudyStash.Core.Tests;

public sealed partial class FakeCanvas
{
    /// <summary>
    /// What Canvas itself would answer at this address, for serving over real HTTP (the pretend Canvas a real Chrome
    /// reads): the route's status, body, paging link and headers, the same way <see cref="Answer"/> picks them for a
    /// job, including a path's first failures. A custom answer (<see cref="On"/>) is a job's answer and has no HTTP
    /// reply of its own: it comes back as its status and text. The URL is noted in <see cref="Requested"/>.
    /// </summary>
    public Reply Serve(string url)
    {
        Requested.Add(url);
        string key = Key(url);
        if (first.TryGetValue(key, out var q) && q.Count > 0) return q.Dequeue();
        if (custom.TryGetValue(key, out var answer))
        {
            var r = answer(new CanvasJob("http", url, "text"));
            return new Reply(r.Status, System.Text.Encoding.UTF8.GetBytes(r.Text), r.Link, r.Rate, r.RetryAfter, Type: r.Type.Length > 0 ? r.Type : "text/plain");
        }
        return routes.GetValueOrDefault(key) ?? new Reply(404, Missing);
    }
}
