using System.Text.Json.Nodes;
using StudyStash.App.Services;
using StudyStash.App.ViewModels;
using StudyStash.Core.Ai;

namespace StudyStash.App.Pictures;

/// <summary>What the films build their guided-setup screens and a fake AI CLI's replies from.</summary>
static class FilmSupport
{
    internal static string Init(string session = "s-1") => new JsonObject
    {
        ["type"] = "system", ["subtype"] = "init", ["session_id"] = session,
        ["tools"] = new JsonArray("mcp__study_stash_setup__get_setup_status"),
        ["mcp_servers"] = new JsonArray(new JsonObject { ["name"] = "study_stash_setup", ["status"] = "connected" }),
    }.ToJsonString();

    internal static string Says(string text) => new JsonObject
    {
        ["type"] = "assistant", ["message"] = new JsonObject { ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text }) },
    }.ToJsonString();

    internal static string Done(string text, string session = "s-1", bool error = false) =>
        new JsonObject { ["type"] = "result", ["is_error"] = error, ["result"] = text, ["session_id"] = session }.ToJsonString();
}
