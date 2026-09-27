using System.Text.Json.Nodes;
using StudyStash.App.ViewModels;
using StudyStash.Core;

namespace StudyStash.App.Tests;

/// <summary>A library's settings routes, in memory: what the app sent, and answers the way the library does (all the
/// settings after a change). <see cref="Refuse"/> makes the next change fail the way the library refuses one.</summary>
public sealed class FakeLibrarySettings
{
    public JsonObject Settings { get; set; } = Demo();
    public List<(string Method, string Path, JsonObject? Body)> Calls { get; } = [];
    /// <summary>The next change is refused with this (the library's own words), then this clears.</summary>
    public string? Refuse { get; set; }
    /// <summary>The library doesn't answer at all.</summary>
    public bool Down { get; set; }
    /// <summary>A library older than these routes: it answers 404 (null).</summary>
    public bool Older { get; set; }
    public string? Password { get; private set; }

    public LibrarySettingsModel.Call Call => (method, path, body) =>
    {
        Calls.Add((method.Method, path, body?.DeepClone() as JsonObject));
        if (Down) throw new HttpRequestException("no route to host");
        if (Older) return Task.FromResult<JsonObject?>(null);
        if (method == HttpMethod.Post && Refuse is { } why)
        {
            Refuse = null;
            throw new LibraryRefusedException(400, why);
        }
        JsonObject? answer = (method.Method, path) switch
        {
            ("GET", "") => Settings,
            ("POST", "") => Apply(body!),
            ("POST", "/password") => Remember(body!),
            ("POST", "/rewrite-all") => new JsonObject { ["queued"] = Settings["lectures"]!.GetValue<int>() },
            ("POST", "/update") => new JsonObject { ["updating"] = true, ["latest"] = "v0.6.0" },
            ("POST", "/course-names") => UseCourseNames(),
            _ => null,
        };
        return Task.FromResult(answer?.DeepClone() as JsonObject);
    };

    JsonObject Remember(JsonObject body)
    {
        Password = body["password"]!.GetValue<string>();
        return new JsonObject { ["has_password"] = true };
    }

    JsonObject Apply(JsonObject body)
    {
        foreach (var (key, value) in body)
        {
            if (value is JsonObject inner && Settings[key] is JsonObject section)
                foreach (var (k, v) in inner) section[k] = v?.DeepClone();
            else if (key == "add_folder")
                ((JsonArray)Settings["folders"]!).Add(new JsonObject { ["name"] = Path.GetFileName(value!.GetValue<string>()), ["path"] = value.GetValue<string>(), ["ai"] = true, ["private"] = false });
            else if (key == "start_at_login" || key == "name" || key == "terminal" || key == "auto_update" || key == "sort_engine")
            {
                if (key == "terminal") Settings["terminal"]!["current"] = value?.DeepClone();
                else if (key == "sort_engine") Settings["sorting"]!["engine"] = value?.DeepClone();
                else if (key == "auto_update") Settings["updates"]!["auto"] = value?.DeepClone();
                else Settings[key] = value?.DeepClone();
            }
            else if (key == "folders")
            {
                var had = ((JsonArray)Settings["folders"]!).OfType<JsonObject>().ToDictionary(f => f["path"]!.GetValue<string>());
                Settings["folders"] = new JsonArray(((JsonArray)value!).OfType<JsonObject>().Select(f =>
                {
                    var row = (JsonObject)had[f["path"]!.GetValue<string>()].DeepClone();
                    row["ai"] = f["ai"]!.DeepClone();
                    row["private"] = f["private"]!.DeepClone();
                    return (JsonNode?)row;
                }).ToArray());
            }
            else if (key == "classes")
            {
                Settings["classes"] = new JsonArray(((JsonArray)value!).OfType<JsonObject>().Select(c => (JsonNode?)new JsonObject
                {
                    ["name"] = c["name"]!.DeepClone(), ["aliases"] = c["aliases"]!.DeepClone(), ["description"] = c["description"]!.DeepClone(),
                    ["folder"] = "/Users/sam/Study Stash/Lecture notes/" + c["name"]!.GetValue<string>(), ["lectures"] = 0,
                }).ToArray());
            }
        }
        return Settings;
    }

    /// <summary>Renames every class in the preview, as the library does, and answers what it did.</summary>
    JsonObject UseCourseNames()
    {
        var renames = (Settings["course_names"]?["renames"] as JsonArray ?? []).OfType<JsonObject>().ToList();
        int lectures = 0;
        foreach (var r in renames)
            foreach (var c in ((JsonArray)Settings["classes"]!).OfType<JsonObject>().Where(c => c["name"]!.GetValue<string>() == r["from"]!.GetValue<string>()))
            {
                c["name"] = r["to"]!.DeepClone();
                c["folder"] = "/Users/sam/Study Stash/Lecture notes/" + r["to"]!.GetValue<string>();
                lectures += r["lectures"]!.GetValue<int>();
            }
        Settings["course_names"] = new JsonObject { ["blocked"] = null, ["renames"] = new JsonArray() };
        return new JsonObject
        {
            ["blocked"] = null, ["renames"] = new JsonArray(), ["lectures"] = lectures, ["problem"] = null,
            ["renamed"] = new JsonArray(renames.Select(r => (JsonNode?)new JsonObject { ["from"] = r["from"]!.DeepClone(), ["to"] = r["to"]!.DeepClone() }).ToArray()),
        };
    }

    /// <summary>A library set up before course names: two of its classes are named from Canvas course codes.</summary>
    public static JsonObject CodeNamed()
    {
        var s = Demo();
        var classes = (JsonArray)s["classes"]!;
        classes[0]!["name"] = "202710.TS.CSCI321.A";
        classes[1]!["name"] = "202710.TS.ENGR401.A";
        s["course_names"] = JsonNode.Parse("""
            {"blocked": null, "renames": [
              {"from": "202710.TS.CSCI321.A", "to": "Software Engineering", "code": "CSCI 321", "lectures": 18},
              {"from": "202710.TS.ENGR401.A", "to": "Senior Design", "code": "ENGR 401", "lectures": 14}]}
            """);
        return s;
    }

    /// <summary>The design's example library: Sam's, on a Mac mini, with four classes and two folders.</summary>
    public static JsonObject Demo() => (JsonObject)JsonNode.Parse("""
        {
          "name": "Sam's library", "has_password": true, "notes_folder": "/Users/sam/Study Stash/Lecture notes", "lectures": 42,
          "classes": [
            {"name": "CS 101", "aliases": ["cs101", "intro to cs"], "description": "Recursion, the call stack and Big-O", "folder": "/Users/sam/Study Stash/Lecture notes/CS 101", "lectures": 18},
            {"name": "BIO 110", "aliases": ["biology"], "description": "Cells, membranes and osmosis", "folder": "/Users/sam/Study Stash/Lecture notes/BIO 110", "lectures": 14},
            {"name": "HIST 210", "aliases": [], "description": "", "folder": "/Users/sam/Study Stash/Lecture notes/HIST 210", "lectures": 9},
            {"name": "MATH 221", "aliases": ["calc"], "description": "", "folder": "/Users/sam/Study Stash/Lecture notes/MATH 221", "lectures": 1}
          ],
          "notes": {"write": true, "sort": true, "min_confidence": 0.6, "writer": "Ollama (gemma4:e4b)"},
          "ollama": {"host": "http://localhost:11434", "answering": true,
                     "models": [{"name": "gemma4:e4b", "size": "9.6 GB"}, {"name": "qwen3:1.7b", "size": "1.4 GB"}],
                     "summary_model": "", "sort_model": "gemma4:e4b", "recommended": "gemma4:e4b"},
          "sorting": {"engine": "", "default": "Ollama", "engines": [{"id": "ollama", "name": "Ollama", "installed": true}, {"id": "claude", "name": "Claude", "installed": true}, {"id": "codex", "name": "ChatGPT", "installed": false}]},
          "terminal": {"current": "ghostty", "choices": [{"id": "terminal", "name": "Terminal"}, {"id": "ghostty", "name": "Ghostty"}]},
          "folders": [
            {"name": "School", "path": "/Users/sam/Documents/School", "ai": true, "private": false},
            {"name": "Journal", "path": "/Users/sam/Documents/Journal", "ai": false, "private": true}
          ],
          "files": 312,
          "reach": {"addresses": ["http://mac-mini.tail1234.ts.net:8787", "http://100.64.0.9:8787", "http://mac-mini:8787"], "tailscale": true, "port": 8787},
          "start_at_login": false,
          "updates": {"version": "0.5.0", "latest": "v0.5.0", "newer": false, "page": null, "auto": true, "can_update": true}
        }
        """)!;
}
