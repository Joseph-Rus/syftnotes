using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StudyStash.App.ViewModels;
using StudyStash.App.Views;
using StudyStash.Core;

namespace StudyStash.App.Tests;

/// <summary>The attachments on a lecture's page and a class's list, on the Mac and on Windows: the files, Attach,
/// "Reading your handwriting…", and "Rewrite notes with your attachments".</summary>
public class AttachmentsViewTests
{
    /// <summary>One lecture's files: slides, and a photo of a page still being read.</summary>
    sealed class OneLecture(bool rewrite) : IAttachmentLibrary
    {
        public Task<JsonObject?> AttachmentListAsync(string? className, string? lectureId, CancellationToken stop = default) =>
            Task.FromResult<JsonObject?>(new JsonObject
            {
                ["attachments"] = new JsonArray(
                    new JsonObject { ["id"] = "a", ["name"] = "Week 3 slides.pdf", ["type"] = "application/pdf", ["size"] = 2_516_582, ["hasText"] = true, ["reading"] = false },
                    new JsonObject { ["id"] = "b", ["name"] = "IMG_2231.png", ["type"] = "image/png", ["size"] = 830_000, ["hasText"] = false, ["reading"] = lectureId is not null }),
                ["rewrite"] = lectureId is not null ? rewrite : null,
            });
        public Task<JsonArray> AttachAsync(IEnumerable<string> paths, string? c, string? l, CancellationToken s = default) => Task.FromResult(new JsonArray());
        public Task<bool> RemoveAttachmentAsync(string id) => Task.FromResult(true);
        public Task DownloadAttachmentAsync(string id, string path, CancellationToken s = default) => Task.CompletedTask;
        public Task RewriteAsync(string id) => Task.CompletedTask;
    }

    static Window Host(Control content, SkinKind skin)
    {
        ((App)Application.Current!).UseSkin(skin);
        var w = new Window { Width = 1280, Height = 800, RequestedThemeVariant = ThemeVariant.Light, Content = content };
        w.Show();
        Dispatcher.UIThread.RunJobs();
        return w;
    }

    static bool Shows(Control root, string text) =>
        root.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == text && t.IsEffectivelyVisible);

    [AvaloniaTheory]
    [InlineData(SkinKind.Mac)]
    [InlineData(SkinKind.Win)]
    public async Task A_lectures_page_lists_its_files_says_its_reading_and_offers_the_rewrite(SkinKind skin)
    {
        var m = new LibraryModel { Note = new NoteModel { Id = "l1", ClassName = "CS 101", Title = "Recursion", Markdown = "## Summary\nRecursion." } };
        var files = new AttachmentsModel(new OneLecture(rewrite: true), "CS 101", "l1") { PollEvery = TimeSpan.FromHours(1) };
        m.LectureFiles = files;
        var loading = files.LoadAsync();
        Control view = skin == SkinKind.Mac ? new MacLibrary { DataContext = m } : new WinLibrary { DataContext = m };
        var w = Host(view, skin);
        try
        {
            Dispatcher.UIThread.RunJobs();
            Assert.True(Shows(view, "Attachments"));
            Assert.True(Shows(view, "Attach"));
            Assert.True(Shows(view, "Week 3 slides.pdf"));
            Assert.True(Shows(view, "Slides · 2.4 MB"));
            Assert.True(Shows(view, "IMG_2231.png"));
            Assert.True(Shows(view, AttachmentWords.Reading));
            Assert.True(Shows(view, AttachmentWords.Rewrite));
            // The Attach button has a file picker behind it.
            Assert.NotNull(files.PickFiles);

            // Another lecture: this one's files go, and stop looking again.
            m.Note = new NoteModel { Id = "l2", ClassName = "CS 101", Title = "Trees" };
            Dispatcher.UIThread.RunJobs();
            Assert.Null(m.LectureFiles);
            Assert.False(Shows(view, "Week 3 slides.pdf"));
            await loading.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        }
        finally
        {
            w.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(SkinKind.Mac)]
    [InlineData(SkinKind.Win)]
    public async Task A_class_list_shows_its_files_above_its_lectures(SkinKind skin)
    {
        var m = new LibraryModel { ClassTitle = "CS 101" };
        var files = new AttachmentsModel(new OneLecture(rewrite: true), "CS 101", null);
        m.ClassFiles = files;
        await files.LoadAsync();
        Control view = skin == SkinKind.Mac ? new MacLibrary { DataContext = m } : new WinLibrary { DataContext = m };
        var w = Host(view, skin);
        try
        {
            Assert.True(Shows(view, "Week 3 slides.pdf"));
            Assert.False(Shows(view, AttachmentWords.Reading));
            Assert.False(Shows(view, AttachmentWords.Rewrite)); // a class never offers it
            var name = view.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == "Week 3 slides.pdf");
            var title = view.GetVisualDescendants().OfType<TextBlock>().First(t => t.Name == "ListTitle");
            Assert.True(name.TranslatePoint(default, view)!.Value.Y > title.TranslatePoint(default, view)!.Value.Y);
            m.ClassFiles = null;
            Dispatcher.UIThread.RunJobs();
            Assert.False(Shows(view, "Week 3 slides.pdf"));
        }
        finally
        {
            w.Close();
        }
    }
}
