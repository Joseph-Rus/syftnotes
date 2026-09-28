using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StudyStash.Core;

namespace StudyStash.App;

/// <summary>
/// The self-test's own AI engine and model mirror, in one small Kestrel server: nothing the self-test does ever
/// reaches a real Ollama, Claude, Canvas or Hugging Face. Answers Ollama's API (<c>/api/tags</c>, <c>/api/show</c>,
/// <c>/api/chat</c> — classifying with a JSON schema, or writing plain Markdown notes, depending on which the
/// library asked for) and serves the tiny Whisper model at <c>/models/&lt;file&gt;</c> with byte-range support,
/// throttled so setup's download bar has something to show.
/// </summary>
public sealed class SelfTestEngine : IAsyncDisposable
{
    public const string NotesMarker = "Written by the self-test's notes engine.";
    const long ThrottleBytesPerSecond = 15_000_000;

    readonly WebApplication app;
    readonly string modelPath;

    public string Url { get; private set; } = "";
    public int ChatRequests { get; private set; }
    public int SortRequests { get; private set; }
    public int NotesRequests { get; private set; }

    SelfTestEngine(WebApplication app, string modelPath)
    {
        this.app = app;
        this.modelPath = modelPath;
    }

    public static async Task<SelfTestEngine> StartAsync(string modelPath)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        WebHostBuilderKestrelExtensions.ConfigureKestrel(builder.WebHost, k => k.Listen(IPAddress.Loopback, 0));
        var web = builder.Build();
        var engine = new SelfTestEngine(web, modelPath);
        engine.Map();
        await web.StartAsync();
        engine.Url = web.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        return engine;
    }

    void Map()
    {
        app.MapGet("/api/tags", () => Results.Json(new JsonObject
        {
            ["models"] = new JsonArray(new JsonObject { ["name"] = "self-test-notes", ["size"] = 1_000_000 }),
        }));
        app.MapPost("/api/show", async (HttpRequest req) =>
        {
            await ReadJsonAsync(req);
            return Results.Json(new JsonObject { ["model_info"] = new JsonObject { ["general.context_length"] = 32768 } });
        });
        app.MapPost("/api/chat", async (HttpRequest req) =>
        {
            ChatRequests++;
            var body = await ReadJsonAsync(req);
            string prompt = Py.AsString(body?["messages"]?[0]?["content"]) ?? "";
            if (body?["format"] is not null)
            {
                SortRequests++;
                var answer = new JsonObject
                {
                    ["class_name"] = FirstClassIn(prompt),
                    ["confidence"] = 0.9,
                    ["lecture_title"] = "Lecture",
                    ["topics"] = new JsonArray("midterm", "recursion"),
                };
                return Results.Json(new JsonObject { ["message"] = new JsonObject { ["content"] = answer.ToJsonString() }, ["done_reason"] = "stop" });
            }
            NotesRequests++;
            return Results.Json(new JsonObject { ["message"] = new JsonObject { ["content"] = Notes() }, ["done_reason"] = "stop" });
        });
        app.MapGet("/models/{file}", async (HttpContext ctx, string file) => await ServeModelAsync(ctx, file));
    }

    static async Task<JsonNode?> ReadJsonAsync(HttpRequest req)
    {
        req.EnableBuffering();
        var node = await JsonNode.ParseAsync(req.Body);
        req.Body.Position = 0;
        return node;
    }

    /// <summary>The prompt lists "- ClassName" lines (real classes first, "Unsorted" last): the first one is the
    /// self-test's answer, so filing is predictable without the fake engine reading the transcript at all.</summary>
    static string FirstClassIn(string prompt)
    {
        foreach (string line in prompt.Split('\n'))
        {
            string t = line.Trim();
            if (!t.StartsWith("- ", StringComparison.Ordinal)) continue;
            string name = t[2..];
            int cut = name.IndexOfAny(['(', '—']);
            if (cut >= 0) name = name[..cut];
            return name.Trim();
        }
        return Configs.Unsorted;
    }

    /// <summary>Fixed, non-repetitive Markdown (Summarize.Repetitive would reject a real loop): enough distinct
    /// lines that the pipeline keeps it, plus the marker line the self-test looks for. Its details carry a formula
    /// inline and one on its own line, and a flowchart, so the self-test sees the app draw them.</summary>
    internal static string Notes() => string.Join('\n',
    [
        "## Summary",
        "The lecture reviewed the coming midterm and worked through recursion, tying it back to the course's earlier units.",
        "",
        "## Key points",
        "- The midterm covers everything up to and including recursion.",
        "- Recursion breaks a problem into smaller versions of the same problem.",
        "- Every recursive function needs a base case, or it never stops.",
        "",
        "## Definitions",
        "- **Recursion**: a function that calls itself to solve a smaller instance of the same problem.",
        "- **Base case**: the condition that stops the recursion from continuing forever.",
        "",
        "## Details and examples",
        @"Summing a list of $n$ numbers recursively does one addition per call, $T(n) = T(n-1) + c$, so it takes $O(n)$ steps.",
        "",
        "$$",
        @"T(n) = \sum_{k=1}^{n} c = c\,n",
        "$$",
        "",
        "```mermaid",
        "flowchart TD",
        "  A[Sum the list] --> B{Is the list empty?}",
        "  B -->|yes| C([Return 0, the base case])",
        "  B -->|no| D[Add the first number to the sum of the rest]",
        "  D --> A",
        "```",
        "",
        "## Announcements",
        "- The midterm is coming up: review recursion and the earlier units before then.",
        "",
        "## Questions to review",
        "1. What is a base case, and why does recursion need one?",
        "2. How does recursion differ from a loop that does the same job?",
        "3. What happens if a recursive function never reaches its base case?",
        "",
        NotesMarker,
    ]);

    async Task ServeModelAsync(HttpContext ctx, string file)
    {
        if (!string.Equals(file, Path.GetFileName(modelPath), StringComparison.Ordinal) || !File.Exists(modelPath))
        {
            ctx.Response.StatusCode = 404;
            return;
        }
        long length = new FileInfo(modelPath).Length;
        long from = 0, to = length - 1;
        bool partial = false;
        string range = ctx.Request.Headers.Range.ToString();
        if (range.StartsWith("bytes=", StringComparison.Ordinal))
        {
            string[] parts = range["bytes=".Length..].Split('-');
            if (parts.Length == 2 && (parts[0].Length > 0 || parts[1].Length > 0))
            {
                if (parts[0].Length > 0) from = long.Parse(parts[0]);
                if (parts[1].Length > 0) to = long.Parse(parts[1]);
                partial = true;
            }
        }
        if (from >= length || from > to)
        {
            ctx.Response.StatusCode = 416;
            ctx.Response.Headers.ContentRange = $"bytes */{length}";
            return;
        }
        ctx.Response.StatusCode = partial ? 206 : 200;
        ctx.Response.Headers.AcceptRanges = "bytes";
        ctx.Response.ContentType = "application/octet-stream";
        long count = to - from + 1;
        ctx.Response.ContentLength = count;
        if (partial) ctx.Response.Headers.ContentRange = $"bytes {from}-{to}/{length}";

        await using var stream = new FileStream(modelPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        stream.Seek(from, SeekOrigin.Begin);
        var buffer = new byte[1 << 20];
        long remaining = count, sentThisWindow = 0;
        var clock = Stopwatch.StartNew();
        while (remaining > 0)
        {
            int n = await stream.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), ctx.RequestAborted);
            if (n <= 0) break;
            await ctx.Response.Body.WriteAsync(buffer.AsMemory(0, n), ctx.RequestAborted);
            remaining -= n;
            sentThisWindow += n;
            if (sentThisWindow < ThrottleBytesPerSecond) continue;
            var elapsed = clock.Elapsed;
            if (elapsed < TimeSpan.FromSeconds(1)) await Task.Delay(TimeSpan.FromSeconds(1) - elapsed, ctx.RequestAborted);
            clock.Restart();
            sentThisWindow = 0;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await app.StopAsync();
        await app.DisposeAsync();
    }
}

/// <summary>A port nobody on this computer is listening on, for the self-test's own library (never one already in
/// use — the owner may be running a real one).</summary>
static class SelfTestPorts
{
    static bool IsFree(int port)
    {
        using var s = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        if (!OperatingSystem.IsWindows()) s.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        try
        {
            s.Bind(new IPEndPoint(IPAddress.Loopback, port));
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    /// <summary>Two consecutive free ports (the library's own web port, and the one after it for Claude), well away
    /// from the default 8787 so a real library on this computer is never mistaken for ours.</summary>
    public static int FreePair(int start = 8850)
    {
        for (int port = start; port < start + 200; port++)
            if (IsFree(port) && IsFree(port + 1))
                return port;
        throw new InvalidOperationException("No free ports found for the self-test's library.");
    }
}
