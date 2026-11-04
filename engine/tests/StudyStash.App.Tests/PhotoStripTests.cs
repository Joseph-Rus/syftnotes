using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SkiaSharp;
using StudyStash.App.Controls;
using StudyStash.App.ViewModels;
using StudyStash.App.Views;
using StudyStash.Core;

namespace StudyStash.App.Tests;

/// <summary>A lecture's whiteboard photos: each labelled with its moment, and only that lecture's own attachments shown.</summary>
public class PhotoStripTests
{
    /// <summary>A library with a lecture's two whiteboard photos (and one more picture that is not a whiteboard photo).</summary>
    sealed class Board : IAttachmentLibrary
    {
        public Task<JsonObject?> AttachmentListAsync(string? className, string? lectureId, CancellationToken stop = default) =>
            Task.FromResult<JsonObject?>(new JsonObject
            {
                ["attachments"] = new JsonArray(
                    Entry("p1", "IMG_0412.jpg", "2026-10-12T09:41:04-07:00", 664),
                    Entry("p2", "IMG_0413.jpg", "2026-10-12T10:02:30-07:00", 1950)),
                ["rewrite"] = false,
            });

        static JsonObject Entry(string id, string name, string taken, double at) => new()
        {
            ["id"] = id, ["name"] = name, ["type"] = "image/jpeg", ["size"] = 1_400_000, ["hasText"] = false, ["reading"] = false,
            ["kind"] = "whiteboard", ["taken"] = taken, ["at"] = at,
        };

        public Task<JsonArray> AttachAsync(IEnumerable<string> paths, string? c, string? l, CancellationToken s = default) => Task.FromResult(new JsonArray());
        public Task<bool> RemoveAttachmentAsync(string id) => Task.FromResult(true);
        public Task RewriteAsync(string id) => Task.CompletedTask;

        public Task DownloadAttachmentAsync(string id, string path, CancellationToken s = default)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var bitmap = new SKBitmap(120, 80);
            using var canvas = new SKCanvas(bitmap);
            canvas.Clear(new SKColor(0xF4, 0xF3, 0xEE));
            using var data = SKImage.FromBitmap(bitmap).Encode(SKEncodedImageFormat.Jpeg, 85);
            using var file = File.Create(path);
            data.SaveTo(file);
            return Task.CompletedTask;
        }
    }

    [AvaloniaFact]
    public async Task A_lectures_photos_are_labelled_by_their_moment_and_only_its_own_are_shown()
    {
        string dir = Path.Combine(Path.GetTempPath(), "photo-strip-" + Guid.NewGuid().ToString("N"));
        var files = new AttachmentsModel(new Board(), "Machine Learning", "n1") { CacheDir = dir };
        try
        {
            await files.LoadAsync();
            Assert.Equal(["11:04", "32:30"], files.Photos.Select(p => p.Label));
            Assert.Null(await files.PhotoFileAsync("someone-elses")); // another lecture's id is not shown
            Assert.NotNull(await files.PhotoFileAsync("p1"));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>The photos are decoded on a worker thread, so they have to find their way back: each thumbnail in the strip,
    /// and the picture a note embeds under its words.</summary>
    [AvaloniaFact]
    public async Task A_photo_arrives_in_the_strip_and_in_the_notes_once_decoded()
    {
        string dir = Path.Combine(Path.GetTempPath(), "photo-strip-" + Guid.NewGuid().ToString("N"));
        var files = new AttachmentsModel(new Board(), "Machine Learning", "n1") { CacheDir = dir };
        var note = new NoteView { Width = 600, Markdown = "On the board:\n\n![Whiteboard at 11:04](attachment:p1)\n\nAfter it." };
        var window = new Window { Width = 700, Height = 800, Content = note };
        NoteView.PhotoSource = files.PhotoFileAsync;
        try
        {
            await files.LoadAsync();
            window.Show();
            await Wait.UntilAsync(() =>
            {
                Dispatcher.UIThread.RunJobs();
                return files.Photos.All(p => p.Thumb is not null) && note.GetVisualDescendants().OfType<Image>().Any(i => i.Source is not null && i.IsVisible);
            }, "the photos to be decoded");
        }
        finally
        {
            NoteView.PhotoSource = null;
            window.Close();
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }
}
