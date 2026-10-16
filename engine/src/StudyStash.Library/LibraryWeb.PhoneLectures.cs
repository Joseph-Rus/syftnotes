using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using StudyStash.Core;

namespace StudyStash.Library;

/// <summary>
/// POST /api/v2/lectures: a lecture the iPhone app recorded and wrote down itself, on the phone, the way a laptop's
/// recording reaches /api/ingest. It carries the transcript ("[mm:ss] words" a line), what the student typed while it
/// recorded, the class it was recorded for and how long it ran; the library files it and writes its notes.
///
/// Its id is the phone's own ("phone-20261015-091500-a1b2c3"), so a phone can send one again (after a dropped
/// connection) without making a second lecture, and can never reach a lecture it didn't make.
/// </summary>
public sealed partial class LibraryWeb
{
    static readonly Regex PhoneLectureId = new("^phone-[0-9]{8}-[0-9]{6}-[0-9a-f]{6,16}$", RegexOptions.CultureInvariant);

    void MapPhoneLectures(WebApplication app)
    {
        app.MapPost("/api/v2/lectures", Http.Handle(ctx => ApiAsync(ctx, async () =>
        {
            var body = await Http.JsonBodyAsync(ctx.Request) as JsonObject;
            if (body is null) return Http.Detail(422, "send the lecture as a JSON object");
            if (PhoneLecturePayload(body, PhoneOf(ctx)?.Name) is not { } payload)
                return Http.Detail(400, "a lecture from the phone needs an id like phone-20261015-091500-a1b2c3 and a transcript");
            Meeting m;
            try
            {
                m = Wire.MeetingFromJson(payload);
            }
            catch (PayloadException e)
            {
                return Http.Detail(400, e.Message);
            }
            store.Enqueue(m);
            pipeline.Wake();
            Console.WriteLine($"[phone] lecture {m.Id} ({m.Transcript.Length} chars)");
            var c = Classify.ByRules(m, cfg.Classes);
            return Http.Json(new JsonObject
            {
                ["id"] = m.Id, ["status"] = "queued", ["class_name"] = c?.ClassName, ["has_transcript"] = Py.Strip(m.Transcript).Length > 0,
            });
        })));
    }

    /// <summary>
    /// What /api/ingest would take for the phone's lecture, or null when it isn't one: titled the way a laptop titles
    /// a recording until the library names it from what was said ("CS 101, Tue 22 Sep"), unless the student typed one.
    /// </summary>
    internal static JsonObject? PhoneLecturePayload(JsonObject body, string? phone)
    {
        string? id = Str(body, "id"), transcript = Str(body, "transcript");
        if (id is null || !PhoneLectureId.IsMatch(id) || transcript is null) return null;
        string cls = Str(body, "class") ?? "", typed = Str(body, "title") ?? "";
        bool titleTyped = typed.Length > 0 && body["title_typed"] is JsonValue tv && tv.TryGetValue(out bool t) && t;
        var started = DateTimeOffset.TryParse(Str(body, "date"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : DateTimeOffset.Now;
        string title = typed.Length > 0 ? typed
            : cls.Length > 0 ? $"{cls}, {started.ToString("ddd d MMM", CultureInfo.InvariantCulture)}"
            : $"Recording, {started.ToString("ddd d MMM 'at' h:mm tt", CultureInfo.InvariantCulture)}";
        double seconds = body["seconds"] is JsonValue sv && sv.TryGetValue(out double s) && double.IsFinite(s) ? Math.Max(0, s) : 0;
        var raw = new JsonObject
        {
            ["source"] = "phone", ["seconds"] = Math.Round(seconds, 1), ["language"] = Str(body, "language") ?? "",
            ["transcribed_by"] = "iPhone",
        };
        if (titleTyped) raw[Meeting.TitleBy] = Meeting.ByHand;
        var payload = new JsonObject
        {
            ["id"] = id,
            ["title"] = Py.Head(title, 200),
            ["date"] = started.ToString("o", CultureInfo.InvariantCulture),
            ["owner"] = Py.Head(Str(body, "owner") ?? phone ?? "", 60),
            ["folder"] = cls,
            ["transcript"] = transcript,
            ["raw"] = raw,
        };
        if (Str(body, "notes") is { } notes) payload["private_notes"] = notes;
        return payload;
    }
}
