using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace StudyStash.Core;

/// <summary>What a watched command printed (its output and its errors, in the order they came), its exit code (null
/// when it was stopped: it printed the line being waited for, or ran out of time), and the line it stopped at.</summary>
public sealed record WatchResult(int? ExitCode, string Output, string? StoppedAt = null, bool TimedOut = false);

/// <summary>Runs a command and reads what it prints line by line as it prints it, stopping it at the first line
/// <c>stopAt</c> picks out. Null when it couldn't start.</summary>
public delegate WatchResult? StreamRunner(string exe, IReadOnlyList<string> args, Func<string, bool> stopAt, TimeSpan timeout);

public enum ReachKind { NotInstalled, NotRunning, NoName, NeedsPermission, NeedsHttps, Timeout, Other }

/// <summary>Why the library couldn't go on (or off) the internet, in words for the student, and the page that fixes
/// it when there is one.</summary>
public sealed record ReachProblem(ReachKind Kind, string Words, string? FixUrl = null);

/// <summary>
/// Putting the Claude port where Claude can reach it, with Tailscale: Serve (HTTPS on your tailnet only: Claude Code
/// on your other computers) or Funnel (HTTPS on the internet: claude.ai and the Claude apps). Off unless
/// <see cref="ThisComputer"/> is used, so a test can't publish anything.
/// </summary>
public sealed partial class ClaudeReach
{
    public const string DownloadPage = "https://tailscale.com/download";
    public const string DnsPage = "https://login.tailscale.com/admin/dns";
    static readonly TimeSpan SetWait = TimeSpan.FromSeconds(20), StatusWait = TimeSpan.FromSeconds(10);

    public Func<TailscaleInfo> Tailscale { get; init; } = () => new TailscaleInfo();
    /// <summary>Asks Tailscale something that answers straight away (funnel status).</summary>
    public Runner Run { get; init; } = (_, _, _) => throw new InvalidOperationException("Changing Tailscale is off here.");
    /// <summary>Changes Serve or Funnel. Watched rather than run: on a tailnet that doesn't allow Funnel yet, Tailscale
    /// prints the page that allows it and then waits for someone to, so the link has to be caught as it's printed.</summary>
    public StreamRunner Watch { get; init; } = (_, _, _, _) => throw new InvalidOperationException("Changing Tailscale is off here.");

    public static ClaudeReach ThisComputer() => new() { Tailscale = () => HostInfo.Tailscale(), Run = Machine.Run, Watch = WatchProcess };

    [GeneratedRegex(@"https://login\.tailscale\.com/\S+")]
    private static partial Regex LoginLink();

    /// <summary>Serve (tailnet) or Funnel (internet) the Claude port on https://&lt;this computer&gt;.ts.net, or turn it off.
    /// The address, or why not. <paramref name="httpsPort"/> is the port it answers on there: 443 for Claude, another
    /// (8443) for the phone app, so neither takes the other's place.</summary>
    public (string? Url, ReachProblem? Problem) Set(int port, bool internet, bool on, int httpsPort = 443)
    {
        var ts = Tailscale();
        if (!ts.Installed || ts.Exe.Length == 0)
            return (null, new(ReachKind.NotInstalled, "Tailscale isn't on the library's computer. Install it from tailscale.com/download and sign in.", DownloadPage));
        if (!ts.Running)
            return (null, new(ReachKind.NotRunning, ts.State == "NeedsLogin"
                ? "Tailscale is signed out on the library's computer. Open it and sign in."
                : "Tailscale isn't running on the library's computer. Open it and sign in."));
        if (ts.Dns.Length == 0)
            return (null, new(ReachKind.NoName, "Tailscale hasn't given this computer a name yet. Turn on MagicDNS in the Tailscale admin console.", DnsPage));
        string verb = internet ? "funnel" : "serve";
        string https = $"--https={httpsPort}";
        string[] args = on ? [verb, "--bg", https, $"http://127.0.0.1:{port}"] : [verb, https, "off"];
        var said = Watch(ts.Exe, args, line => LoginLink().IsMatch(line), SetWait);
        if (said is null) return (null, new(ReachKind.Other, "Tailscale didn't start on the library's computer. Check it's installed and running."));
        var link = LoginLink().Match(said.StoppedAt ?? said.Output);
        if (link.Success) return (null, NeedsAllowing(said.Output, link.Value));
        if (said.TimedOut) return (null, new(ReachKind.Timeout, "Tailscale didn't answer in time. Check it's running on the library's computer."));
        if (said.ExitCode != 0)
        {
            string words = Py.Strip(said.Output);
            return (null, new(ReachKind.Other, words.Length > 0 ? $"Tailscale said: {Py.Head(words, 300)}" : $"Tailscale stopped (code {said.ExitCode}) without saying why."));
        }
        return (on ? $"https://{ts.Dns.TrimEnd('.')}{(httpsPort == 443 ? "" : $":{httpsPort}")}" : null, null);
    }

    /// <summary>A tailnet admin has to allow Funnel (or HTTPS certificates) once: which one Tailscale is asking for.</summary>
    static ReachProblem NeedsAllowing(string output, string link)
    {
        bool https = link.Contains("/f/serve", StringComparison.Ordinal) || link.Contains("/f/https", StringComparison.Ordinal)
            || output.Contains("HTTPS", StringComparison.Ordinal) && !output.Contains("Funnel is not enabled", StringComparison.OrdinalIgnoreCase);
        return https
            ? new(ReachKind.NeedsHttps, "Your tailnet doesn't have HTTPS certificates turned on yet. Open the page below, turn them on, then turn this on again.", link)
            : new(ReachKind.NeedsPermission, "Your tailnet doesn't allow Funnel yet. Open the page below, allow it for this computer, then turn this on again.", link);
    }

    /// <summary>Whether Funnel sends this computer's https address to the Claude port right now (from
    /// <c>tailscale funnel status --json</c>). Null when it can't tell. Read-only: it changes nothing.</summary>
    public bool? Status(int port)
    {
        try
        {
            var ts = Tailscale();
            if (!ts.Installed || ts.Exe.Length == 0 || !ts.Running) return null;
            var p = Run(ts.Exe, ["funnel", "status", "--json"], StatusWait);
            return p is { ExitCode: 0 } ? FunnelsTo(p.Stdout, port) : null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>Reads Tailscale's serve config: true when 443 is open to the internet and proxies to the port.</summary>
    public static bool? FunnelsTo(string json, int port)
    {
        if (Py.Strip(json).Length == 0) return null;
        JsonObject? config;
        try
        {
            config = JsonNode.Parse(json) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
        if (config is null) return null;
        foreach (var (hostPort, allowed) in config["AllowFunnel"] as JsonObject ?? [])
        {
            if (!hostPort.EndsWith(":443", StringComparison.Ordinal) || allowed is not JsonValue v || !v.TryGetValue(out bool open) || !open) continue;
            if (config["Web"]?[hostPort]?["Handlers"] is not JsonObject handlers) continue;
            foreach (var (_, handler) in handlers)
                if (handler?["Proxy"] is JsonValue pv && pv.TryGetValue(out string? proxy)
                    && (proxy.TrimEnd('/').EndsWith($":{port}", StringComparison.Ordinal) || proxy == port.ToString(System.Globalization.CultureInfo.InvariantCulture)))
                    return true;
        }
        return false;
    }

    /// <summary>The real <see cref="Watch"/>: starts the command with no window, reads its output and its errors as they
    /// come, and kills it at the line being waited for or when time runs out.</summary>
    public static WatchResult? WatchProcess(string exe, IReadOnlyList<string> args, Func<string, bool> stopAt, TimeSpan timeout)
    {
        var psi = new ProcessStartInfo(exe)
        {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true,
        };
        foreach (string a in args) psi.ArgumentList.Add(a);
        var output = new StringBuilder();
        string? hit = null;
        var found = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var closed = new CountdownEvent(2); // not disposed: a stream can close after this returns
        void Line(string? line)
        {
            if (line is null)
            {
                closed.Signal();
                return;
            }
            lock (output)
            {
                output.AppendLine(line);
                if (hit is null && stopAt(line))
                {
                    hit = line;
                    found.TrySetResult();
                }
            }
        }
        Process? p;
        try
        {
            p = Process.Start(psi);
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException or IOException)
        {
            return null;
        }
        if (p is null) return null;
        using (p)
        {
            p.OutputDataReceived += (_, e) => Line(e.Data);
            p.ErrorDataReceived += (_, e) => Line(e.Data);
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            using var stop = new CancellationTokenSource(timeout);
            var exited = p.WaitForExitAsync(stop.Token);
            try
            {
                Task.WhenAny(found.Task, exited).GetAwaiter().GetResult().GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
            }
            bool done = exited.IsCompletedSuccessfully;
            if (!done)
            {
                try
                {
                    p.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                }
            }
            closed.Wait(TimeSpan.FromMilliseconds(500)); // the last lines, still on their way
            lock (output)
                return new WatchResult(done && hit is null ? p.ExitCode : null, output.ToString(), hit, TimedOut: !done && hit is null);
        }
    }
}

/// <summary>
/// Whether Claude on the web could reach the library right now: looked up the way the internet sees it (public DNS,
/// not this computer's MagicDNS, which would send it straight over the tailnet), then asked the three things Claude
/// asks first. Off unless <see cref="ThisComputer"/> is used, so a test can't reach out.
/// </summary>
public sealed class ReachCheck
{
    public static readonly TimeSpan Budget = TimeSpan.FromSeconds(10);
    public const string Answers = "Answers from the internet.";

    /// <summary>The host's public IPv4 addresses.</summary>
    public Func<string, CancellationToken, Task<IPAddress[]>> Resolve { get; init; } =
        (_, _) => throw new InvalidOperationException("checking from the internet is off here");
    /// <summary>What sends the requests, given the address that was looked up: every connection goes there.</summary>
    public Func<IPAddress, HttpMessageHandler> Handler { get; init; } =
        _ => throw new InvalidOperationException("checking from the internet is off here");
    public TimeSpan Wait { get; init; } = Budget;

    public static ReachCheck ThisComputer() => new() { Resolve = PublicDnsAsync, Handler = ClientDocuments.PinnedTo };

    static readonly HttpClient Doh = new() { Timeout = Budget };

    /// <summary>Looks the name up with DNS over HTTPS, the way anyone on the internet would find it.</summary>
    static async Task<IPAddress[]> PublicDnsAsync(string host, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://cloudflare-dns.com/dns-query?name={Uri.EscapeDataString(host)}&type=A");
        request.Headers.Accept.ParseAdd("application/dns-json");
        using var r = await Doh.SendAsync(request, ct);
        r.EnsureSuccessStatusCode();
        var answer = JsonNode.Parse(await r.Content.ReadAsStringAsync(ct));
        return [.. (answer?["Answer"] as JsonArray ?? [])
            .Where(a => a?["type"] is JsonValue t && t.TryGetValue(out int type) && type == 1)
            .Select(a => IPAddress.TryParse(Py.AsString(a?["data"]), out var ip) ? ip : null)
            .OfType<IPAddress>()];
    }

    /// <summary>Whether Study Stash answers at <paramref name="publicUrl"/> from the internet, in words. Never more than
    /// <see cref="Wait"/>.</summary>
    public async Task<(bool Reachable, string Words)> RunAsync(string publicUrl, CancellationToken ct = default)
    {
        string root = publicUrl.TrimEnd('/');
        if (!Uri.TryCreate(root, UriKind.Absolute, out var url) || url.Scheme != Uri.UriSchemeHttps)
            return (false, "Couldn't check from here: the address isn't https.");
        string host = url.IdnHost;
        string notUs = $"{host} answers, but not with Study Stash. Is Funnel pointing at another app?";
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(Wait);
        try
        {
            var found = (await Resolve(host, cts.Token)).Where(a => a.AddressFamily == AddressFamily.InterNetwork).ToArray();
            if (found.Length == 0 || !found.All(PublicAddress.IsGlobal))
                return (false, $"The internet can't find {host} yet. Funnel can take a minute to start.");
            using var handler = Handler(found[0]);
            using var http = new HttpClient(handler, disposeHandler: false) { BaseAddress = new Uri(root + "/"), Timeout = Timeout.InfiniteTimeSpan };

            using var prm = await http.GetAsync(".well-known/oauth-protected-resource/mcp", cts.Token);
            if (prm.StatusCode != HttpStatusCode.OK || !Same(Field(await Body(prm, cts.Token), "resource"), root + "/mcp")) return (false, notUs);

            using var meta = await http.GetAsync(".well-known/oauth-authorization-server", cts.Token);
            if (meta.StatusCode != HttpStatusCode.OK || !Same(Field(await Body(meta, cts.Token), "issuer"), root)) return (false, notUs);

            using var mcp = await http.SendAsync(new HttpRequestMessage(HttpMethod.Post, "mcp")
            {
                Content = new StringContent("""{"jsonrpc":"2.0","id":1,"method":"ping"}""", Encoding.UTF8, "application/json"),
                Headers = { Accept = { new("application/json"), new("text/event-stream") } },
            }, cts.Token);
            string challenge = string.Join(", ", mcp.Headers.WwwAuthenticate.Select(h => h.ToString()));
            var metadata = Regex.Match(challenge, "resource_metadata=\"([^\"]+)\"");
            if (mcp.StatusCode != HttpStatusCode.Unauthorized || !metadata.Success
                || !metadata.Groups[1].Value.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase))
                return (false, notUs);
            return (true, Answers);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return (false, "Couldn't check from here: it took too long to answer.");
        }
        catch (HttpRequestException)
        {
            return (false, $"The internet can't reach {host} yet. Funnel can take a minute to start.");
        }
        catch (Exception e) when (e is InvalidOperationException or SocketException or IOException or JsonException)
        {
            return (false, $"Couldn't check from here: {e.Message.TrimEnd('.')}.");
        }
    }

    static async Task<string> Body(HttpResponseMessage r, CancellationToken ct) => await r.Content.ReadAsStringAsync(ct);

    static string? Field(string json, string key)
    {
        try
        {
            return Py.AsString((JsonNode.Parse(json) as JsonObject)?[key]);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    static bool Same(string? a, string b) => a is not null && string.Equals(a.TrimEnd('/'), b, StringComparison.OrdinalIgnoreCase);
}
