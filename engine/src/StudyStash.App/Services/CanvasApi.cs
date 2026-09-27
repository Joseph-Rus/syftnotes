using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace StudyStash.App.Services;

/// <summary>
/// The library's Canvas JSON API (WS5 PLAN §5), as one static class so its wire records never clash with
/// <c>StudyStash.App.Services.CanvasCourse</c>/<c>CanvasLink</c> (Settings.cs) or <c>StudyStash.Core.Canvas</c>'s own
/// types. Every record uses <c>init</c> properties, never a primary constructor: a key the library hasn't shipped
/// yet must deserialize to a default, not throw.
/// </summary>
public static class CanvasApi
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        // Tolerant readers: a field the library sends as null, "" or another shape reads as nothing, never an error.
        Converters = { new WhenConverter(), new MaybeWhenConverter(), new FlagConverter(), new IntConverter(), new LongConverter(), new DoubleConverter() },
        TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { NullTextIsEmpty } },
    };

    /// <summary>A text field the records promise is never null (<c>string</c>, not <c>string?</c>) reads a null
    /// from the library as "": a library with no Canvas address sends <c>"school": null</c>, for one.</summary>
    static void NullTextIsEmpty(JsonTypeInfo type)
    {
        foreach (var p in type.Properties)
            if (p.PropertyType == typeof(string) && !p.IsSetNullable && p.Set is { } set)
                p.Set = (target, value) => set(target, value ?? "");
    }

    /// <summary>A date as the library may send it: ISO text, or "", null, or something that isn't a date at all (a
    /// library that never synced sent "" for several). Anything but a real date reads as null, so one odd field
    /// never breaks a whole screen.</summary>
    public static DateTimeOffset? ReadWhen(ref Utf8JsonReader reader)
    {
        if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray)
        {
            reader.Skip();
            return null;
        }
        if (reader.TokenType != JsonTokenType.String) return null;
        if (reader.TryGetDateTimeOffset(out var exact)) return exact;
        string text = reader.GetString() ?? "";
        return text.Trim().Length > 0 && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var loose) ? loose : null;
    }

    /// <summary><see cref="ReadWhen"/> for a <see cref="DateTimeOffset"/>? field.</summary>
    public sealed class MaybeWhenConverter : JsonConverter<DateTimeOffset?>
    {
        public override bool HandleNull => true;

        public override DateTimeOffset? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => ReadWhen(ref reader);

        public override void Write(Utf8JsonWriter writer, DateTimeOffset? value, JsonSerializerOptions options)
        {
            if (value is { } v) writer.WriteStringValue(v);
            else writer.WriteNullValue();
        }
    }

    /// <summary><see cref="ReadWhen"/> for a <see cref="DateTimeOffset"/> field: no date reads as the default.</summary>
    public sealed class WhenConverter : JsonConverter<DateTimeOffset>
    {
        public override bool HandleNull => true;

        public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => ReadWhen(ref reader) ?? default;

        public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) => writer.WriteStringValue(value);
    }

    /// <summary>A yes/no as the library may send it: true/false, or text that says something (a saved copy's path
    /// for <c>local</c>, why a file wasn't saved for <c>skipped</c>) or nothing, or a number, or null.</summary>
    public sealed class FlagConverter : JsonConverter<bool>
    {
        public override bool HandleNull => true;

        public override bool Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.True: return true;
                case JsonTokenType.String: return (reader.GetString() ?? "").Trim().Length > 0;
                case JsonTokenType.Number: return reader.TryGetDouble(out double d) && d != 0;
                case JsonTokenType.StartObject or JsonTokenType.StartArray: reader.Skip(); return false;
                default: return false;
            }
        }

        public override void Write(Utf8JsonWriter writer, bool value, JsonSerializerOptions options) => writer.WriteBooleanValue(value);
    }

    /// <summary>A number as the library may send it: a number, a number in text, or null or anything else (0).</summary>
    public static double ReadNumber(ref Utf8JsonReader reader)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Number: return reader.TryGetDouble(out double d) ? d : 0;
            case JsonTokenType.String:
                return double.TryParse(reader.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double t) ? t : 0;
            case JsonTokenType.StartObject or JsonTokenType.StartArray: reader.Skip(); return 0;
            default: return 0;
        }
    }

    public sealed class IntConverter : JsonConverter<int>
    {
        public override bool HandleNull => true;
        public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out int i) ? i : (int)Math.Clamp(ReadNumber(ref reader), int.MinValue, int.MaxValue);
        public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options) => writer.WriteNumberValue(value);
    }

    public sealed class LongConverter : JsonConverter<long>
    {
        public override bool HandleNull => true;
        public override long Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.TokenType == JsonTokenType.Number && reader.TryGetInt64(out long l) ? l : (long)Math.Clamp(ReadNumber(ref reader), long.MinValue, long.MaxValue);
        public override void Write(Utf8JsonWriter writer, long value, JsonSerializerOptions options) => writer.WriteNumberValue(value);
    }

    public sealed class DoubleConverter : JsonConverter<double>
    {
        public override bool HandleNull => true;
        public override double Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => ReadNumber(ref reader);
        public override void Write(Utf8JsonWriter writer, double value, JsonSerializerOptions options) => writer.WriteNumberValue(value);
    }

    /// <summary>Canvas ids travel as numbers or strings depending on the library version; we always want a string.</summary>
    public sealed class IdConverter : JsonConverter<string>
    {
        public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => reader.TokenType switch
        {
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.Number => reader.TryGetInt64(out long l) ? l.ToString(CultureInfo.InvariantCulture) : reader.GetDouble().ToString(CultureInfo.InvariantCulture),
            JsonTokenType.Null => null,
            JsonTokenType.StartObject => IdOf(ref reader, options),
            _ => throw new JsonException($"Expected a Canvas id (a string or a number), got {reader.TokenType}."),
        };

        /// <summary>An object that carries its id, like the library's <c>suggested</c> course (<c>{"id","name"}</c>).</summary>
        string? IdOf(ref Utf8JsonReader reader, JsonSerializerOptions options)
        {
            string? id = null;
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                bool isId = reader.ValueTextEquals("id");
                reader.Read();
                if (isId) id = Read(ref reader, typeof(string), options);
                else reader.Skip();
            }
            return id;
        }

        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) => writer.WriteStringValue(value);
    }

    // ---- GET canvas/state ----

    public sealed record State
    {
        [JsonPropertyName("state")] public string Status { get; init; } = "";
        public string School { get; init; } = "";
        public string Url { get; init; } = "";
        public ExtensionInfo? Extension { get; init; }
        public DateTimeOffset? LastSync { get; init; }
        public DateTimeOffset? NextSync { get; init; }
        public int PollMinutes { get; init; }
        public SyncingInfo? Syncing { get; init; }
        public DateTimeOffset? PausedUntil { get; init; }
        public ErrorInfo? Error { get; init; }
        public IReadOnlyList<string> Warnings { get; init; } = [];
    }

    /// <summary>The Chrome extension as the library sees it. <see cref="Seen"/> is when a Chrome last checked in with
    /// the library's current key; <see cref="Connected"/> is one checking in now with it (null from a library older
    /// than this field: go by the state); <see cref="KeyMatches"/> false means the Chrome that asked last has an old
    /// key and needs connecting again; <see cref="RefusedAt"/> is when a Chrome with another key was turned away.</summary>
    public sealed record ExtensionInfo
    {
        public DateTimeOffset? Seen { get; init; }
        public bool? Connected { get; init; }
        public bool? KeyMatches { get; init; }
        public DateTimeOffset? LastSeen { get; init; }
        public DateTimeOffset? RefusedAt { get; init; }
        public string Version { get; init; } = "";
        public string? Latest { get; init; }
        public bool Outdated { get; init; }
        public ExtensionUpdate? Updated { get; init; }
    }

    public sealed record ExtensionUpdate
    {
        public string From { get; init; } = "";
        public string To { get; init; } = "";
        public DateTimeOffset At { get; init; }
    }

    public sealed record SyncingInfo
    {
        public int Left { get; init; }
        public int Total { get; init; }
        public IReadOnlyList<string> Classes { get; init; } = [];
    }

    public sealed record ErrorInfo
    {
        public string Text { get; init; } = "";
        public DateTimeOffset At { get; init; }
    }

    // ---- GET canvas (overview, old keys kept); also T1's fallback source for /state on an older library ----

    public sealed record Overview
    {
        public string Url { get; init; } = "";
        /// <summary>Old key: class name → linked course id (0 = not linked).</summary>
        public IReadOnlyDictionary<string, double> Courses { get; init; } = new Dictionary<string, double>();
        /// <summary>Old key: course id (as a string) → its name.</summary>
        public IReadOnlyDictionary<string, string> Available { get; init; } = new Dictionary<string, string>();
        /// <summary>Canvas course id → its code, name and term.</summary>
        public IReadOnlyDictionary<string, Course> CourseInfo { get; init; } = new Dictionary<string, Course>();
        public IReadOnlyList<ChangeRow> LastChanges { get; init; } = [];
        public int PollMinutes { get; init; }
        public string ExtensionSeen { get; init; } = "";
        public string ExtensionVersion { get; init; } = "";
        public bool NeedsLogin { get; init; }
        public bool Syncing { get; init; }
        public int Left { get; init; }
        public string Error { get; init; } = "";
        public DateTimeOffset? LastSync { get; init; }
        public ExtensionUpdate? ExtensionUpdate { get; init; }
        /// <summary>The same as GET canvas/extension, without the key.</summary>
        public ExtensionKey? Extension { get; init; }
    }

    public sealed record ChangeRow
    {
        public string Kind { get; init; } = "";
        public string Text { get; init; } = "";
        public DateTimeOffset At { get; init; }
    }

    // ---- GET canvas/extension ----

    /// <summary>The library's extension: its key, the Canvas it points at, this library's version and protocol, the
    /// folder the library keeps ready, and the Chrome that last checked in (which computer, whether it's connected
    /// now). A library from before these fields leaves them empty.</summary>
    public sealed record ExtensionKey
    {
        public string Key { get; init; } = "";
        public string Canvas { get; init; } = "";
        public string Version { get; init; } = "";
        public int Protocol { get; init; }
        public string Folder { get; init; } = "";
        public bool FolderReady { get; init; }
        public string Seen { get; init; } = "";
        public string SeenVersion { get; init; } = "";
        public int SeenProtocol { get; init; }
        /// <summary>"this_computer", "another_computer", or "" (the extension didn't say).</summary>
        public string SeenWhere { get; init; } = "";
        /// <summary>A Chrome is checking in now with the library's current key.</summary>
        public bool Connected { get; init; }
        /// <summary>The Chrome that checked in last used the library's current key (false: an old registration).</summary>
        public bool KeyMatches { get; init; }
        /// <summary>When a Chrome last checked in with the current key.</summary>
        public DateTimeOffset? SeenWithKey { get; init; }
        /// <summary>When a Chrome with another key was last turned away.</summary>
        public DateTimeOffset? RefusedAt { get; init; }
    }

    // ---- GET canvas/classes ----

    public sealed record ClassRow
    {
        public string Class { get; init; } = "";
        public bool Linked { get; init; }
        public Course? Canvas { get; init; }
        [JsonConverter(typeof(IdConverter))] public string? Suggested { get; init; }
        public DateTimeOffset? LastSync { get; init; }
        public Counts Counts { get; init; } = new();
        public bool FilesHidden { get; init; }
        public Scout? Scout { get; init; }
    }

    public sealed record Course
    {
        [JsonConverter(typeof(IdConverter))] public string Id { get; init; } = "";
        public string Code { get; init; } = "";
        public string Name { get; init; } = "";
        public string Term { get; init; } = "";
        public string Url { get; init; } = "";
    }

    public sealed record Counts
    {
        public int ToHandIn { get; init; }
        public int Done { get; init; }
        public int Modules { get; init; }
        public int Files { get; init; }
        public int Announcements { get; init; }
        public int AnnouncementsNew { get; init; }
    }

    /// <summary>What Scout (the AI that explores a newly-linked course) found. <see cref="State"/> here is
    /// done|exploring|waiting|failed|never.</summary>
    public sealed record Scout
    {
        public string State { get; init; } = "";
        public int Files { get; init; }
        public DateTimeOffset? When { get; init; }
        public string? Report { get; init; }
    }

    // ---- GET canvas/due, GET canvas/assignments ----

    public sealed record DueResponse
    {
        public DateTimeOffset? Synced { get; init; }
        public int ToHandIn { get; init; }
        public Item? Next { get; init; }
        public IReadOnlyList<Group> Groups { get; init; } = [];
    }

    public sealed record Group
    {
        public string Key { get; init; } = ""; // overdue|week|later|undated|handed_in
        public string Label { get; init; } = "";
        public IReadOnlyList<Item> Items { get; init; } = [];
    }

    /// <summary>One assignment/quiz/discussion as the Due list and a class's assignment tabs show it. Submitted,
    /// GradedAt and MarkedDone are timestamps (their presence is the flag); Late/Missing/Excused are Canvas's own
    /// flags for a handed-in-or-not item.</summary>
    public sealed record Item
    {
        public string Class { get; init; } = "";
        [JsonConverter(typeof(IdConverter))] public string Id { get; init; } = "";
        public string Name { get; init; } = "";
        public string Kind { get; init; } = ""; // assignment|quiz|discussion|to-do
        public string? Due { get; init; } // library wall clock, yyyy-MM-ddTHH:mm
        public DateTimeOffset? DueAt { get; init; } // UTC
        public double? Points { get; init; }
        public string Status { get; init; } = "";
        public string Label { get; init; } = "";
        public double? Score { get; init; }
        public string? Grade { get; init; }
        public string? ScoreText { get; init; }
        public bool Late { get; init; }
        public bool Missing { get; init; }
        public bool Excused { get; init; }
        public DateTimeOffset? Submitted { get; init; }
        public DateTimeOffset? GradedAt { get; init; }
        public DateTimeOffset? MarkedDone { get; init; }
        public string? Url { get; init; }
        public string? Folder { get; init; }
    }

    public sealed record AssignmentsResponse
    {
        public string Class { get; init; } = "";
        public IReadOnlyList<Item> ToHandIn { get; init; } = [];
        public IReadOnlyList<Item> Done { get; init; } = [];
    }

    // ---- GET canvas/assignment ----

    public sealed record AssignmentDetail
    {
        public string Class { get; init; } = "";
        [JsonConverter(typeof(IdConverter))] public string Id { get; init; } = "";
        public string Name { get; init; } = "";
        public string Kind { get; init; } = "";
        public string? Due { get; init; }
        public DateTimeOffset? DueAt { get; init; }
        public double? Points { get; init; }
        public string Status { get; init; } = "";
        public string Label { get; init; } = "";
        public double? Score { get; init; }
        public string? Grade { get; init; }
        public string? ScoreText { get; init; }
        public bool Late { get; init; }
        public bool Missing { get; init; }
        public bool Excused { get; init; }
        public DateTimeOffset? Submitted { get; init; }
        public DateTimeOffset? GradedAt { get; init; }
        public DateTimeOffset? MarkedDone { get; init; }
        public string? Url { get; init; }
        public string? Folder { get; init; }

        public string? Instructions { get; init; }
        public DateTimeOffset? UnlockAt { get; init; }
        public DateTimeOffset? LockAt { get; init; }
        public IReadOnlyList<string> SubmissionTypes { get; init; } = [];
        public int? AllowedAttempts { get; init; }
        public string? GradingType { get; init; }
        public IReadOnlyList<AssignmentFile> Files { get; init; } = [];
        public IReadOnlyList<RubricRow> Rubric { get; init; } = [];
        public SubmissionInfo? Submission { get; init; }
        public IReadOnlyList<CommentInfo> Comments { get; init; } = [];
        public QuizInfo? Quiz { get; init; }
        public string? Spec { get; init; }
        public string? Feedback { get; init; }
    }

    public sealed record AssignmentFile
    {
        [JsonConverter(typeof(IdConverter))] public string Id { get; init; } = "";
        public string Name { get; init; } = "";
        public long Size { get; init; }
        public string? ContentType { get; init; }
        public string? Format { get; init; }
        public bool Local { get; init; }
        public bool Skipped { get; init; }
        public string? Url { get; init; }
    }

    public sealed record RubricRow
    {
        [JsonConverter(typeof(IdConverter))] public string Id { get; init; } = "";
        public string Criterion { get; init; } = "";
        public string? Description { get; init; }
        public double Points { get; init; }
        public IReadOnlyList<RatingRow> Ratings { get; init; } = [];
        public MarkInfo? Mark { get; init; }
    }

    public sealed record RatingRow
    {
        public string Label { get; init; } = "";
        public double Points { get; init; }
    }

    public sealed record MarkInfo
    {
        public double? Points { get; init; }
        public string? Rating { get; init; }
        public string? Comment { get; init; }
    }

    public sealed record SubmissionInfo
    {
        public string State { get; init; } = "";
        public int? Attempt { get; init; }
        public DateTimeOffset? SubmittedAt { get; init; }
        public DateTimeOffset? GradedAt { get; init; }
        public double? Score { get; init; }
        public string? Grade { get; init; }
        public bool Late { get; init; }
        public double? PointsDeducted { get; init; }
        public string? Body { get; init; }
        public IReadOnlyList<AssignmentFile> Files { get; init; } = [];
        /// <summary>Every attempt handed in, oldest first.</summary>
        public IReadOnlyList<AttemptInfo> Attempts { get; init; } = [];
    }

    public sealed record AttemptInfo
    {
        public int? Attempt { get; init; }
        public DateTimeOffset? SubmittedAt { get; init; }
        public bool Late { get; init; }
        public IReadOnlyList<AssignmentFile> Files { get; init; } = [];
    }

    /// <summary>A graded quiz's own facts, when the assignment is one.</summary>
    public sealed record QuizInfo
    {
        [JsonConverter(typeof(IdConverter))] public string Id { get; init; } = "";
        public string? QuizType { get; init; }
        public double? TimeLimit { get; init; }
        public int? AllowedAttempts { get; init; }
        public int? QuestionCount { get; init; }
    }

    public sealed record CommentInfo
    {
        public string Author { get; init; } = "";
        public DateTimeOffset At { get; init; }
        public string Text { get; init; } = "";
        public IReadOnlyList<AssignmentFile> Files { get; init; } = [];
        public string? MediaUrl { get; init; }
    }

    // ---- GET canvas/modules ----

    public sealed record ModulesResponse
    {
        public int Count { get; init; }
        public IReadOnlyList<ModuleRow> Modules { get; init; } = [];
    }

    public sealed record ModuleRow
    {
        [JsonConverter(typeof(IdConverter))] public string Id { get; init; } = "";
        public string Name { get; init; } = "";
        public int Position { get; init; }
        public string? State { get; init; }
        public DateTimeOffset? UnlockAt { get; init; }
        public int ItemsCount { get; init; }
        public IReadOnlyList<ModuleItem> Items { get; init; } = [];
    }

    public sealed record ModuleItem
    {
        [JsonConverter(typeof(IdConverter))] public string Id { get; init; } = "";
        public string Type { get; init; } = "";
        public string? Kind { get; init; } // file|page|assignment|quiz|discussion|link|tool|header
        public string Title { get; init; } = "";
        public int Indent { get; init; }
        public string? Format { get; init; }
        public long? Size { get; init; }
        public bool Local { get; init; }
        public bool Saved { get; init; }
        public string? Source { get; init; } // box|drive|onedrive|youtube|null
        public string? Url { get; init; }
        public string? ExternalUrl { get; init; }
        [JsonConverter(typeof(IdConverter))] public string? AssignmentId { get; init; }
        public bool Locked { get; init; }
        public bool Skipped { get; init; }
    }

    // ---- GET canvas/files ----

    public sealed record FilesResponse
    {
        public bool Allowed { get; init; }
        public int Count { get; init; }
        public IReadOnlyList<FileRow> Files { get; init; } = [];
    }

    public sealed record FileRow
    {
        [JsonConverter(typeof(IdConverter))] public string Id { get; init; } = "";
        public string? Folder { get; init; }
        public string Name { get; init; } = "";
        public long Size { get; init; }
        public string? ContentType { get; init; }
        public string? Format { get; init; }
        public DateTimeOffset? UpdatedAt { get; init; }
        public bool Local { get; init; }
        public bool Skipped { get; init; }
    }

    // ---- GET canvas/announcements ----

    public sealed record AnnouncementsResponse
    {
        public int Count { get; init; }
        public int New { get; init; }
        public IReadOnlyList<AnnouncementRow> Items { get; init; } = [];
    }

    public sealed record AnnouncementRow
    {
        [JsonConverter(typeof(IdConverter))] public string Id { get; init; } = "";
        public string Title { get; init; } = "";
        public DateTimeOffset PostedAt { get; init; }
        public string? Author { get; init; }
        public bool New { get; init; }
        public bool ReadOnCanvas { get; init; }
        public string? Body { get; init; }
        public IReadOnlyList<AssignmentFile> Files { get; init; } = [];
        public string? Url { get; init; }
    }

    // ---- GET canvas/notifications ----

    /// <summary>Notification ids are the library's own ever-increasing counter (<c>CanvasNotifications</c>), never
    /// Canvas's, so — unlike everywhere else in this file — they travel as plain numbers, not through
    /// <see cref="IdConverter"/>: <see cref="Last"/> is the highest id the library knows (0/null when there are
    /// none), and the same number is what a client sends back as <c>after</c>/<c>up_to</c> next time.</summary>
    public sealed record NotificationsResponse
    {
        public long? Last { get; init; }
        public IReadOnlyList<NotificationRow> Items { get; init; } = [];
    }

    public sealed record NotificationRow
    {
        public long Id { get; init; }
        public string Kind { get; init; } = "";
        public string Title { get; init; } = "";
        public string? Text { get; init; }
        public string? Class { get; init; }
        public long? AssignmentId { get; init; }
        public long? AnnouncementId { get; init; }
        public DateTimeOffset At { get; init; }
        public bool Seen { get; init; }
    }

    // ---- GET files?class=&path= ----

    public sealed record TextFile
    {
        public string Text { get; init; } = "";
    }
}
