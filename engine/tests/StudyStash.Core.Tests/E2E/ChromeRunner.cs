using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;

namespace StudyStash.Core.Tests.E2E;

/// <summary>
/// A real, headless Chrome (Chrome for Testing: branded Chrome no longer loads an extension from the command line)
/// with one unpacked extension loaded, on a throwaway profile, never the person's own. What it prints goes to a log
/// whose end <see cref="Tail"/> gives a failing test. Disposing it ends Chrome and every process it started.
/// </summary>
public sealed class ChromeRunner : IDisposable
{
    /// <summary>The Chrome binary the end-to-end tests use; empty when they're skipped.</summary>
    public static string Binary => Environment.GetEnvironmentVariable("STUDYSTASH_E2E_CHROME") ?? "";

    readonly Process process;
    readonly StringBuilder log = new();
    readonly string profile;

    ChromeRunner(Process process, string profile)
    {
        this.process = process;
        this.profile = profile;
    }

    /// <summary>
    /// Start Chrome with the extension in <paramref name="extensionDir"/>, each host in <paramref name="hosts"/>
    /// resolving to 127.0.0.1, on <paramref name="startUrl"/>. <paramref name="profile"/> is an empty folder for its
    /// profile.
    /// </summary>
    public static ChromeRunner Start(string extensionDir, string profile, string startUrl, params string[] hosts)
    {
        Directory.CreateDirectory(profile);
        var info = new ProcessStartInfo(Binary)
        {
            UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true,
        };
        foreach (string arg in new[]
        {
            "--headless=new", $"--user-data-dir={profile}", "--no-first-run", "--no-default-browser-check", "--disable-sync",
            "--use-mock-keychain", "--password-store=basic", "--disable-search-engine-choice-screen",
            // Chrome no longer loads an extension from the command line, and disables an unpacked one that reloads
            // itself while developer mode is off (a fresh profile's): a student's Chrome has developer mode on.
            "--disable-features=DisableLoadExtensionCommandLineSwitch,ExtensionDisableUnsupportedDeveloper",
            // Nothing but the pretend Canvas and the library: no Google services, updates or metrics.
            "--disable-background-networking", "--disable-component-update", "--disable-domain-reliability",
            $"--load-extension={extensionDir}", $"--disable-extensions-except={extensionDir}",
            "--host-resolver-rules=" + string.Join(", ", hosts.Select(h => $"MAP {h} 127.0.0.1")),
            "--enable-logging=stderr", "--v=0",
            // DevTools on a loopback port Chrome picks (written to the profile's DevToolsActivePort), for EvaluateAsync.
            "--remote-debugging-port=0",
            startUrl,
        }) info.ArgumentList.Add(arg);
        var p = new Process { StartInfo = info, EnableRaisingEvents = true };
        var runner = new ChromeRunner(p, profile);
        p.ErrorDataReceived += (_, e) => runner.Append(e.Data);
        p.OutputDataReceived += (_, e) => runner.Append(e.Data);
        p.Start();
        p.BeginErrorReadLine();
        p.BeginOutputReadLine();
        return runner;
    }

    void Append(string? line)
    {
        if (line is null) return;
        lock (log) log.AppendLine(line);
    }

    public bool Exited => process.HasExited;

    /// <summary>The DevTools address Chrome wrote into its profile: "http://127.0.0.1:PORT". Waits for it.</summary>
    async Task<string> DevToolsAsync()
    {
        string file = Path.Combine(profile, "DevToolsActivePort");
        for (int i = 0; i < 80 && !File.Exists(file); i++) await Task.Delay(250);
        return "http://127.0.0.1:" + (await File.ReadAllLinesAsync(file))[0].Trim();
    }

    /// <summary>
    /// Evaluate <paramref name="expression"/> (JavaScript; a promise is awaited) in the Study Stash extension: in its
    /// service worker, or with <paramref name="page"/> (e.g. "popup.html") in that page opened as a tab. Returns the
    /// result as JSON text ("null" when the extension isn't running yet). Other extensions Chrome runs itself are
    /// told apart by the manifest's name.
    /// </summary>
    public async Task<string> EvaluateAsync(string expression, string? page = null)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        string devtools = await DevToolsAsync();
        var workers = JsonNode.Parse(await http.GetStringAsync(devtools + "/json/list"))!.AsArray()
            .Where(t => (string?)t!["type"] == "service_worker" && ((string?)t["url"] ?? "").EndsWith("/background.js", StringComparison.Ordinal));
        foreach (var worker in workers)
        {
            string url = (string)worker!["url"]!, ws = (string)worker["webSocketDebuggerUrl"]!;
            if (await RunAsync(ws, "chrome.runtime.getManifest().name") != "\"Study Stash for Canvas\"") continue;
            if (page is null) return await RunAsync(ws, expression);
            string at = url[..^"background.js".Length] + page;
            using var opened = new HttpRequestMessage(HttpMethod.Put, devtools + "/json/new?" + at);
            var tab = JsonNode.Parse(await (await http.SendAsync(opened)).Content.ReadAsStringAsync())!;
            string tabWs = (string)tab["webSocketDebuggerUrl"]!;
            string loaded = $"location.href === {System.Text.Json.JsonSerializer.Serialize(at)} && document.readyState === 'complete'";
            for (int i = 0; i < 50 && await RunAsync(tabWs, loaded) != "true"; i++) await Task.Delay(100);
            return await RunAsync(tabWs, expression);
        }
        return "null";
    }

    /// <summary>One Runtime.evaluate over a DevTools socket; the value as JSON, or the exception's text.</summary>
    static async Task<string> RunAsync(string ws, string expression)
    {
        using var socket = new ClientWebSocket();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await socket.ConnectAsync(new Uri(ws), timeout.Token);
        var ask = new JsonObject
        {
            ["id"] = 1, ["method"] = "Runtime.evaluate",
            ["params"] = new JsonObject { ["expression"] = expression, ["awaitPromise"] = true, ["returnByValue"] = true },
        };
        await socket.SendAsync(Encoding.UTF8.GetBytes(ask.ToJsonString()), WebSocketMessageType.Text, true, timeout.Token);
        var buffer = new byte[1 << 16];
        while (true)
        {
            using var message = new MemoryStream();
            WebSocketReceiveResult got;
            do
            {
                got = await socket.ReceiveAsync(buffer, timeout.Token);
                message.Write(buffer, 0, got.Count);
            } while (!got.EndOfMessage);
            var answer = JsonNode.Parse(message.ToArray())!;
            if ((int?)answer["id"] != 1) continue;
            var result = answer["result"]!;
            if (result["exceptionDetails"] is { } error) return "exception: " + (error["exception"]?["description"] ?? error["text"]);
            return result["result"]?["value"]?.ToJsonString() ?? "null";
        }
    }

    /// <summary>Everything Chrome printed that mentions the extension or a console message, then the last lines.</summary>
    public string Tail(int lines = 40)
    {
        string[] all;
        lock (log) all = log.ToString().Split('\n');
        var console = all.Where(l => l.Contains("CONSOLE", StringComparison.Ordinal) || l.Contains("xtension", StringComparison.Ordinal)).TakeLast(lines);
        return string.Join('\n', console.Concat(["--- last lines ---"]).Concat(all.TakeLast(lines)));
    }

    public void Dispose()
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(10_000);
            }
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
        }
        process.Dispose();
        try
        {
            Directory.Delete(profile, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }
}
