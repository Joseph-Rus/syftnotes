using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using SkiaSharp;
using StudyStash.App.Controls;
using StudyStash.App.ViewModels;
using StudyStash.App.Views;
using StudyStash.Core;

namespace StudyStash.App.Tests;

/// <summary>A lecture's page with whiteboard photos: the strip of photos with their moments, and a photo embedded in the
/// notes (<c>![alt](attachment:ID)</c>), only for that lecture's own attachments.</summary>
public class PhotosShots
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
            using var bitmap = new SKBitmap(1200, 800);
            using var canvas = new SKCanvas(bitmap);
            canvas.Clear(new SKColor(0xF4, 0xF3, 0xEE));
            using var pen = new SKPaint { Color = new SKColor(0x1D, 0x2B, 0x4A), StrokeWidth = 7, IsAntialias = true, Style = SKPaintStyle.Stroke };
            using var text = new SKPaint { Color = new SKColor(0x1D, 0x2B, 0x4A), IsAntialias = true };
            using var font = new SKFont { Size = 76 };
            if (id == "p1")
            {
                canvas.DrawText("softmax(QK^T / sqrt(d)) V", 90, 160, font, text);
                canvas.DrawText("Q = X Wq   K = X Wk   V = X Wv", 90, 300, font, text);
                canvas.DrawLine(90, 340, 1000, 340, pen);
            }
            else
            {
                foreach (var (x, y) in new[] { (140f, 220f), (520f, 220f), (900f, 220f), (520f, 520f) }) canvas.DrawRoundRect(x, y, 200, 110, 14, 14, pen);
                canvas.DrawLine(340, 275, 520, 275, pen);
                canvas.DrawLine(720, 275, 900, 275, pen);
                canvas.DrawLine(620, 330, 620, 520, pen);
            }
            using var data = SKImage.FromBitmap(bitmap).Encode(SKEncodedImageFormat.Jpeg, 85);
            using var file = File.Create(path);
            data.SaveTo(file);
            return Task.CompletedTask;
        }
    }

    const string Notes = """
        ## Attention, as drawn on the board

        Each token asks a question (the query), and every other token answers with a key. The scores become weights with a softmax, and the weights mix the values.

        ![Whiteboard at 11:04](attachment:p1)

        The layer is repeated in blocks; the second photo shows how the blocks connect.

        ![Whiteboard at 32:30](attachment:p2)
        """;

    [AvaloniaFact]
    public async Task A_lecture_page_shows_its_photo_strip_and_an_embedded_photo()
    {
        string dir = Path.Combine(Path.GetTempPath(), "photo-shots-" + Guid.NewGuid().ToString("N"));
        var files = new AttachmentsModel(new Board(), "Machine Learning", "n1") { CacheDir = dir };
        await files.LoadAsync();
        NoteView.PhotoSource = files.PhotoFileAsync;
        try
        {
            Assert.Equal(["11:04", "32:30"], files.Photos.Select(p => p.Label));
            Assert.Null(await files.PhotoFileAsync("someone-elses")); // another lecture's id is not shown
            foreach (var p in files.Photos)
                for (int i = 0; i < 100 && p.Thumb is null; i++)
                {
                    Dispatcher.UIThread.RunJobs();
                    await Task.Delay(20);
                }
            foreach (var t in new[] { ThemeVariant.Light })
            {
                var size = new Size(1408, 1500);
                Shot.Take("mac-46-photos", SkinKind.Mac, t, () =>
                {
                    var page = Demo.Semester(Demo.Page.Note);
                    page.Notes = AiDemo.LectureNotes(Notes, "Claude");
                    page.LectureFiles = files;
                    return new MacLibrary { DataContext = page, Width = 1280, Height = 1360 };
                }, size: size);
                Shot.Take("win-46-photos", SkinKind.Win, t, () =>
                {
                    var page = Demo.Semester(Demo.Page.Note);
                    page.Notes = AiDemo.LectureNotes(Notes, "Claude");
                    page.LectureFiles = files;
                    return new WinLibrary { DataContext = page, Width = 1280, Height = 1360 };
                }, size: size);
            }
        }
        finally
        {
            NoteView.PhotoSource = null;
        }
    }
}
