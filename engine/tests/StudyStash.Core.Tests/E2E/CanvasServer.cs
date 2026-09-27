using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Logging;

namespace StudyStash.Core.Tests.E2E;

/// <summary>
/// A pretend Canvas on real HTTP for a real Chrome: a <see cref="FakeCanvas"/>'s routes served by Kestrel on
/// 127.0.0.1, reached by Chrome as <c>http://canvas.test:&lt;port&gt;</c> (Chrome maps the name to 127.0.0.1). The
/// fixtures' absolute <c>https://canvas.test</c> links are rewritten to that address so a crawl follows them here.
/// Like Canvas, it wants a signed-in session: <c>GET /login/e2e</c> signs Chrome in (a <c>canvas_session</c>
/// cookie); without one the API answers 401 "unauthenticated" and pages and downloads send you to the sign-in page.
/// </summary>
public sealed class CanvasServer : IAsyncDisposable
{
    /// <summary>One request as it arrived: whether it carried a session cookie, and whether that session was good.</summary>
    public sealed record Hit(string Method, string PathAndQuery, bool Cookie, bool SignedIn, int Status, string Host = CanvasServer.Host);

    readonly WebApplication app;
    readonly ConcurrentDictionary<string, bool> sessions = new();
    volatile bool signedOut;

    public FakeCanvas Canvas { get; }
    public int Port { get; }
    /// <summary>The address Chrome and the library use for this Canvas.</summary>
    public string Url => $"http://{Host}:{Port}";
    public const string Host = "canvas.test";
    /// <summary>The same Canvas under another name, for a school whose Canvas address changes.</summary>
    public const string OtherHost = "canvas2.test";
    public string OtherUrl => $"http://{OtherHost}:{Port}";
    /// <summary>Every request, in order.</summary>
    public ConcurrentQueue<Hit> Hits { get; } = new();

    CanvasServer(WebApplication app, FakeCanvas canvas, int port)
    {
        this.app = app;
        Canvas = canvas;
        Port = port;
    }

    public static async Task<CanvasServer> StartAsync(FakeCanvas canvas)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(k => k.Listen(IPAddress.Loopback, 0, o => o.Protocols = HttpProtocols.Http1));
        var app = builder.Build();
        CanvasServer? self = null;
        app.Run(ctx => self!.HandleAsync(ctx));
        await app.StartAsync();
        self = new CanvasServer(app, canvas, new Uri(app.Urls.First()).Port);
        return self;
    }

    /// <summary>Every session Chrome has stops working, as if the student signed out of Canvas (until
    /// <see cref="SignIn"/>).</summary>
    public void SignOut() => signedOut = true;

    /// <summary>Chrome's sessions work again, as if the student signed back in.</summary>
    public void SignIn() => signedOut = false;

    /// <summary>The requests that reached a path (any query).</summary>
    public IEnumerable<Hit> HitsTo(string path) => Hits.Where(h => h.PathAndQuery.Split('?')[0] == path);

    async Task HandleAsync(HttpContext ctx)
    {
        var req = ctx.Request;
        string pathAndQuery = req.Path + req.QueryString;
        string? cookie = req.Cookies["canvas_session"];
        bool signedIn = cookie is not null && sessions.ContainsKey(cookie) && !signedOut;
        string here = $"http://{req.Host.Host}:{Port}";
        var res = ctx.Response;
        try
        {
            if (req.Path == "/login/e2e")
            {
                string id = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
                sessions[id] = true;
                res.Headers.SetCookie = $"canvas_session={id}; Path=/; HttpOnly; SameSite=Lax";
                // Signed in under this name, then on to the other one (headless Chrome opens a single tab): Chrome
                // keeps a cookie set on a redirect.
                if (req.Host.Host == Host)
                {
                    res.StatusCode = 302;
                    res.Headers.Location = OtherUrl + "/login/e2e";
                    return;
                }
                await Html(res, 200, "<h1>Signed in to the pretend Canvas</h1>");
                return;
            }
            if (req.Path == "/_e2e/sign-out" && HttpMethods.IsPost(req.Method))
            {
                SignOut();
                res.StatusCode = 204;
                return;
            }
            if (req.Path == "/_e2e/sign-in" && HttpMethods.IsPost(req.Method))
            {
                SignIn();
                res.StatusCode = 204;
                return;
            }
            if (req.Path.StartsWithSegments("/login"))
            {
                await Html(res, 200, "<form action=\"/login/canvas\" method=\"post\"><input name=\"pseudonym_session[unique_id]\"></form>");
                return;
            }
            if (!signedIn)
            {
                if (req.Path.StartsWithSegments("/api"))
                {
                    res.StatusCode = 401;
                    res.ContentType = "application/json; charset=utf-8";
                    await res.WriteAsync("""{"status":"unauthenticated","errors":[{"message":"user authorization required"}]}""");
                }
                else
                {
                    res.StatusCode = 302;
                    res.Headers.Location = here + "/login/canvas";
                }
                return;
            }

            var reply = Canvas.Serve(FakeCanvas.Base + pathAndQuery);
            res.StatusCode = reply.Status;
            res.ContentType = reply.Type;
            if (reply.Link.Length > 0) res.Headers["Link"] = Local(reply.Link, here);
            if (reply.Rate is { } rate) res.Headers["X-Rate-Limit-Remaining"] = rate.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (reply.RetryAfter is { } wait) res.Headers.RetryAfter = wait.ToString(System.Globalization.CultureInfo.InvariantCulture);
            byte[] body = reply.Type.Contains("octet-stream", StringComparison.Ordinal) ? reply.Body : Encoding.UTF8.GetBytes(Local(Encoding.UTF8.GetString(reply.Body), here));
            res.ContentLength = body.Length;
            await res.Body.WriteAsync(body);
        }
        finally
        {
            Hits.Enqueue(new Hit(req.Method, pathAndQuery, cookie is not null, signedIn, res.StatusCode, req.Host.Host));
        }
    }

    /// <summary>The fixtures' Canvas address as this server's, under the name it was asked by.</summary>
    static string Local(string text, string here) => text.Replace(FakeCanvas.Base, here, StringComparison.Ordinal);

    static Task Html(HttpResponse res, int status, string body)
    {
        res.StatusCode = status;
        res.ContentType = "text/html; charset=utf-8";
        return res.WriteAsync($"<!doctype html><title>Canvas</title>{body}");
    }

    public async ValueTask DisposeAsync()
    {
        await app.StopAsync();
        await app.DisposeAsync();
    }
}
