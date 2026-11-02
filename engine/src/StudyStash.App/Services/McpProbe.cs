using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using StudyStash.Core;

namespace StudyStash.App.Services;

/// <summary>What starting a server as an app's settings say came to: whether it answered, the tools it listed, and why
/// not in a few words (never shown to a student as it is).</summary>
public sealed record McpProbeResult(bool Ok, IReadOnlyList<string> Tools, string Why = "");

/// <summary>
/// Starts an MCP server the way an AI app does from its settings (the command run straight, its arguments as they are,
/// no shell between), and has the conversation an app has when it loads it: <c>initialize</c>, then <c>tools/list</c>,
/// one line of JSON each way. Connect uses it to check that what it wrote really starts Syft Notes (a path with a space
/// or an accent in it, an install that moved), and CI uses it on what the real CLIs read.
/// </summary>
public static class McpProbe
{
    public static async Task<McpProbeResult> ToolsAsync(string command, IReadOnlyList<string> args, IReadOnlyDictionary<string, string>? env = null,
        TimeSpan? timeout = null, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo(command)
        {
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8, StandardInputEncoding = new UTF8Encoding(false),
        };
        foreach (string a in args) psi.ArgumentList.Add(a);
        // Not an app loading it: the server leaves no "started" note for this.
        psi.Environment[McpClients.ProbeEnv] = "1";
        foreach (var (k, v) in env ?? new Dictionary<string, string>()) psi.Environment[k] = v;
        Process? p;
        try
        {
            p = Process.Start(psi);
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return new(false, [], $"didn't start: {e.Message}");
        }
        if (p is null) return new(false, [], "didn't start");
        using var proc = p;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout ?? TimeSpan.FromSeconds(30));
        var said = new StringBuilder();
        var errors = Task.Run(async () =>
        {
            try
            {
                while (await proc.StandardError.ReadLineAsync(CancellationToken.None) is { } line)
                    lock (said) if (said.Length < 2000) said.AppendLine(line);
            }
            catch (Exception e) when (e is IOException or ObjectDisposedException)
            {
            }
        }, CancellationToken.None);
        try
        {
            await Send(proc, new JsonObject
            {
                ["jsonrpc"] = "2.0", ["id"] = 1, ["method"] = "initialize",
                ["params"] = new JsonObject
                {
                    ["protocolVersion"] = "2025-06-18", ["capabilities"] = new JsonObject(),
                    ["clientInfo"] = new JsonObject { ["name"] = "syft-notes-check", ["version"] = Engine.Version },
                },
            });
            if (await Reply(proc, 1, cts.Token) is not { } init || init["result"]?["serverInfo"] is null)
                return new(false, [], "no answer to initialize" + Tail(said));
            await Send(proc, new JsonObject { ["jsonrpc"] = "2.0", ["method"] = "notifications/initialized" });
            await Send(proc, new JsonObject { ["jsonrpc"] = "2.0", ["id"] = 2, ["method"] = "tools/list" });
            if (await Reply(proc, 2, cts.Token) is not { } list || list["result"]?["tools"] is not JsonArray tools)
                return new(false, [], "no answer to tools/list" + Tail(said));
            var names = tools.Select(t => t?["name"] is JsonValue v && v.TryGetValue(out string? n) ? n : "").Where(n => n.Length > 0).ToList();
            return new(names.Count > 0, names, names.Count > 0 ? "" : "it listed no tools");
        }
        catch (OperationCanceledException)
        {
            return new(false, [], "no answer in time" + Tail(said));
        }
        catch (IOException e)
        {
            return new(false, [], $"it stopped: {e.Message}" + Tail(said));
        }
        finally
        {
            try
            {
                proc.StandardInput.Close();
                if (!proc.WaitForExit(3000)) proc.Kill(entireProcessTree: true);
            }
            catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
            {
            }
            await errors.WaitAsync(TimeSpan.FromSeconds(3), CancellationToken.None).ContinueWith(_ => { }, TaskScheduler.Default);
        }
    }

    static async Task Send(Process proc, JsonObject message)
    {
        await proc.StandardInput.WriteAsync(message.ToJsonString() + "\n");
        await proc.StandardInput.FlushAsync();
    }

    /// <summary>The reply with this id, skipping the server's own notifications and log lines; null when it ends first.</summary>
    static async Task<JsonObject?> Reply(Process proc, int id, CancellationToken ct)
    {
        while (await proc.StandardOutput.ReadLineAsync(ct) is { } line)
        {
            JsonObject? message;
            try
            {
                message = JsonNode.Parse(line) as JsonObject;
            }
            catch (JsonException)
            {
                continue;
            }
            if (message?["id"] is JsonValue v && v.TryGetValue(out int got) && got == id) return message;
        }
        return null;
    }

    static string Tail(StringBuilder said)
    {
        lock (said)
        {
            string last = said.ToString().Trim().Split('\n').LastOrDefault()?.Trim() ?? "";
            return last.Length > 0 ? $" ({Py.Head(last, 160)})" : "";
        }
    }
}
