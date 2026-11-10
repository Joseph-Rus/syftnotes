using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using StudyStash.Core;

namespace StudyStash.Library;

/// <summary>
/// POST /api/v2/lectures/shared: a lecture a friend shared as a .syft file, which the app opened and checked
/// (<see cref="SyftFile"/>), into the class the student picked ("" lets the library sort it, its own rules first: a
/// class of the name it had there). Its notes stay as they came; nothing writes them again. 409 when it's here already.
/// </summary>
public sealed partial class LibraryWeb
{
    void MapShared(WebApplication app) =>
        app.MapPost("/api/v2/lectures/shared", Http.Handle(ctx => ApiAsync(ctx, async () =>
        {
            if (await Http.JsonBodyAsync(ctx.Request) is not JsonObject body) return Http.Detail(422, "send the lecture as a JSON object");
            SharedLecture lecture;
            try
            {
                lecture = SharedLecture.FromJson(body);
            }
            catch (SyftFileException e)
            {
                return Http.Detail(400, e.Message);
            }
            if (store.Get(lecture.Id) is { } here) return Here(lecture, here.ClassName);
            string asked = Py.AsString(body["class"]) ?? "";
            var known = cfg.Classes.FirstOrDefault(c => c.Name.Equals(asked, StringComparison.OrdinalIgnoreCase));
            var c = known is not null ? new Classification(known.Name, 1.0, "human")
                : await pipeline.SortAsync(lecture.Meeting().WithNotes(lecture.Notes));
            if (lecture.Add(store, c) is not { } id) return Here(lecture, store.Get(lecture.Id)?.ClassName);
            Console.WriteLine($"[shared] lecture {id} → {c.ClassName}");
            return Http.Json(new JsonObject { ["id"] = id, ["class_name"] = c.ClassName });
        })));

    /// <summary>409: it's here already. The lecture's id and class come too, so a retry of a call that ran long (the
    /// first one added it) can still open what it added.</summary>
    static IResult Here(SharedLecture lecture, string? className) => Http.Json(new JsonObject
    {
        ["detail"] = SharedLecture.AlreadyHere(lecture.Title), ["id"] = lecture.Id, ["class_name"] = className ?? "",
    }, 409);
}
