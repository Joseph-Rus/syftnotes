using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace StudyStash.Core.Ai;

/// <summary>
/// API keys the student chose to give an engine instead of (or as well as) its command-line tool: Claude's from the
/// Anthropic Console, ChatGPT's from OpenAI's platform, Gemini's from Google AI Studio. Kept in ai_keys.json beside
/// ai.json, readable by this account only, and never sent anywhere but the engine's own API. A key is never shown
/// again once saved: only its last four characters (<see cref="Hint"/>).
/// </summary>
public static class ApiKeys
{
    /// <summary>The engines that can run on a key (Ollama needs none).</summary>
    public static readonly string[] Engines = ["claude", "codex", "gemini"];

    public static string PathIn(string home) => Path.Combine(home, "ai_keys.json");

    static Dictionary<string, string> Load(string home)
    {
        try
        {
            return File.Exists(PathIn(home))
                ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(PathIn(home))) ?? []
                : [];
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    public static string? Get(string home, string id) => Load(home).GetValueOrDefault(id) is { Length: > 0 } k ? k : null;

    public static bool Has(string home, string id) => Get(home, id) is not null;

    /// <summary>"…a1b2": enough to tell which key it is, never enough to use it. "" with none.</summary>
    public static string Hint(string home, string id) => Get(home, id) is { } k ? "…" + k[^Math.Min(4, k.Length)..] : "";

    /// <summary>Saves a key for an engine, or with an empty one takes it away.</summary>
    public static void Set(string home, string id, string? key)
    {
        if (!Engines.Contains(id)) throw new ArgumentException($"{id} doesn't take an API key");
        var keys = Load(home);
        if (string.IsNullOrWhiteSpace(key)) keys.Remove(id);
        else keys[id] = key.Trim();
        Directory.CreateDirectory(home);
        string path = PathIn(home);
        Py.WriteText(path, JsonSerializer.Serialize(keys, new JsonSerializerOptions { WriteIndented = true }) + "\n");
        Py.OwnerOnly(path);
    }

    /// <summary>The model an API call names: the engine's own pick when it's an API model id, else a sensible
    /// default for studying (the CLIs' short names, "sonnet" and the like, map to theirs).</summary>
    public static string Model(string id, string picked) => (id, picked) switch
    {
        ("claude", "opus") => "claude-opus-5-5",
        ("claude", "haiku") => "claude-haiku-4-5-20251001",
        ("claude", var m) when m.StartsWith("claude-", StringComparison.Ordinal) => m,
        ("claude", _) => "claude-sonnet-5-5",
        ("codex", var m) when m.Length > 0 => m,
        ("codex", _) => "gpt-5",
        ("gemini", var m) when m.StartsWith("gemini-", StringComparison.Ordinal) && !m.EndsWith("-medium", StringComparison.Ordinal) && !m.EndsWith("-high", StringComparison.Ordinal) => m,
        ("gemini", var m) when m.Contains("pro", StringComparison.Ordinal) => "gemini-2.5-pro",
        _ => "gemini-2.5-flash",
    };

    static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };

    /// <summary>
    /// One request to the engine's API with a key: the prompt (and system prompt) in, the answer streamed back as
    /// <c>text</c> events, then <c>final</c>. No tools and no files: what a key can do is answer, which is what notes,
    /// sorting and Ask need. Ends with an <c>error</c> event when it fails (the API's own words, so an expired key reads
    /// as one).
    /// </summary>
    public static async IAsyncEnumerable<AiEvent> RunAsync(string id, string key, AiRequest req, [EnumeratorCancellation] CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(req.Timeout);
        HttpResponseMessage? r = null;
        string? failed = null;
        try
        {
            r = await Http.SendAsync(Request(id, key, req), HttpCompletionOption.ResponseHeadersRead, cts.Token);
            if (!r.IsSuccessStatusCode)
                failed = $"{Name(id)} API: {(int)r.StatusCode} {ErrorWords(await r.Content.ReadAsStringAsync(cts.Token))}".TrimEnd();
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            failed = ct.IsCancellationRequested ? "Stopped." : $"{Name(id)} API couldn't be reached: {e.Message}";
        }
        if (failed is not null || r is null)
        {
            r?.Dispose();
            yield return AiEvent.Error(failed ?? "no answer");
            yield break;
        }
        using (r)
        {
            using var reader = new StreamReader(await r.Content.ReadAsStreamAsync(cts.Token));
            while (await reader.ReadLineAsync(cts.Token) is { } line)
            {
                if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
                string data = line[5..].Trim();
                if (data == "[DONE]") break;
                JsonNode? d;
                try
                {
                    d = JsonNode.Parse(data);
                }
                catch (JsonException)
                {
                    continue;
                }
                if (Piece(id, d) is { Length: > 0 } piece) yield return new AiEvent("text", piece);
                if (d?["type"]?.GetValue<string>() == "error")
                {
                    yield return AiEvent.Error($"{Name(id)} API: {d["error"]?["message"]}");
                    yield break;
                }
            }
        }
        yield return new AiEvent("final", "");
    }

    static string Name(string id) => id switch { "claude" => "Anthropic", "codex" => "OpenAI", _ => "Gemini" };

    static HttpRequestMessage Request(string id, string key, AiRequest req)
    {
        string model = Model(id, req.Model);
        HttpRequestMessage m;
        JsonObject body;
        switch (id)
        {
            case "claude":
                m = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
                m.Headers.Add("x-api-key", key);
                m.Headers.Add("anthropic-version", "2023-06-01");
                body = new JsonObject
                {
                    ["model"] = model, ["max_tokens"] = 16000, ["stream"] = true,
                    ["messages"] = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = req.Prompt }),
                };
                if (req.System.Length > 0) body["system"] = req.System;
                break;
            case "codex":
                m = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions");
                m.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
                var messages = new JsonArray();
                if (req.System.Length > 0) messages.Add(new JsonObject { ["role"] = "system", ["content"] = req.System });
                messages.Add(new JsonObject { ["role"] = "user", ["content"] = req.Prompt });
                body = new JsonObject { ["model"] = model, ["stream"] = true, ["messages"] = messages };
                break;
            default:
                m = new HttpRequestMessage(HttpMethod.Post,
                    $"https://generativelanguage.googleapis.com/v1beta/models/{Uri.EscapeDataString(model)}:streamGenerateContent?alt=sse");
                m.Headers.Add("x-goog-api-key", key);
                body = new JsonObject
                {
                    ["contents"] = new JsonArray(new JsonObject
                    {
                        ["role"] = "user", ["parts"] = new JsonArray(new JsonObject { ["text"] = req.Prompt }),
                    }),
                };
                if (req.System.Length > 0)
                    body["systemInstruction"] = new JsonObject { ["parts"] = new JsonArray(new JsonObject { ["text"] = req.System }) };
                break;
        }
        m.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        return m;
    }

    /// <summary>The new words in one streamed event, in each API's own shape.</summary>
    static string? Piece(string id, JsonNode? d) => id switch
    {
        "claude" => d?["type"]?.GetValue<string>() == "content_block_delta" ? Str(d["delta"]?["text"]) : null,
        "codex" => Str(d?["choices"]?[0]?["delta"]?["content"]),
        _ => d?["candidates"]?[0]?["content"]?["parts"] is JsonArray parts
            ? string.Concat(parts.Select(p => Str(p?["text"]) ?? "")) : null,
    };

    static string? Str(JsonNode? n) => n is JsonValue v && v.TryGetValue(out string? s) ? s : null;

    /// <summary>An API's error body, as its message: {"error":{"message":…}} in all three.</summary>
    static string ErrorWords(string body)
    {
        try
        {
            var n = JsonNode.Parse(body);
            return Str(n?["error"]?["message"]) ?? Str(n?[0]?["error"]?["message"]) ?? Py.Head(body, 200);
        }
        catch (JsonException)
        {
            return Py.Head(body, 200);
        }
    }
}
