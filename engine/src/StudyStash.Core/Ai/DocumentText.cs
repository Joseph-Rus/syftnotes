namespace StudyStash.Core.Ai;

/// <summary>
/// The words in a document a student hands the library: a PDF, a picture of a page, Word or PowerPoint. For now,
/// what file search already reads (<see cref="FileIndex.TextOf"/>); reading handwriting and scans comes with OCR,
/// which replaces this file and keeps its one method exactly as it is.
/// </summary>
public static class DocumentText
{
    /// <summary>The text of the document at <paramref name="path"/>, or null when there's none to be had.</summary>
    public static Task<string?> ExtractAsync(string path, CancellationToken ct) =>
        Task.Run(() => FileIndex.TextOf(path) is { Length: > 0 } text ? text : null, ct);
}
